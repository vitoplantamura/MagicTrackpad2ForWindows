// ControlPanel.c: Control Panel named-pipe server.

#include <driver.h>
#include "ControlPanel.tmh"

//
// Named pipe exposed to user mode. Client connects with just "AmtPtpControlPanelUsbUmInterface".
//
#define CONTROLPANEL_PIPE_NAME      L"\\\\.\\pipe\\AmtPtpControlPanelUsbUmInterface"
#define CONTROLPANEL_PIPE_BUFFER    64

//
// Command bytes that a client may send.
//
#define CONTROLPANEL_CMD_RELOAD_SETTINGS    0x00
#define CONTROLPANEL_CMD_GET_BATTERY_LEVEL  0x01

//
// Response status bytes.
//
#define CONTROLPANEL_STATUS_OK      0x00
#define CONTROLPANEL_STATUS_FAIL    0xFF
#define CONTROLPANEL_STATUS_UNKNOWN 0xFE

static
DWORD
WINAPI
AmtPtpControlPanelPipeThread(
	_In_ LPVOID Context
);

//
// Sends the command 0x90 to the device and returns the battery level.
//
_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS
AmtPtpReadBatteryLevel(
	_In_  PDEVICE_CONTEXT DeviceContext,
	_Out_ UCHAR* BatteryLevel
)
{
	NTSTATUS                        status;
	WDF_USB_CONTROL_SETUP_PACKET    setupPacket;
	WDF_MEMORY_DESCRIPTOR           memoryDescriptor;
	ULONG                           cbTransferred = 0;
	UCHAR                           buffer[128];

	PAGED_CODE();

	*BatteryLevel = 0;

	if (!AmtPtpIsMagicTrackpad2(DeviceContext)) {
		TraceEvents(
			TRACE_LEVEL_INFORMATION,
			TRACE_DEVICE,
			"%!FUNC! Device family has no battery, skipping"
		);
		return STATUS_NOT_SUPPORTED;
	}

	RtlZeroMemory(buffer, sizeof(buffer));

	//
	// Prepare the output buffer
	// descriptor.
	//
	WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(
		&memoryDescriptor,
		buffer,
		sizeof(buffer)
	);

	//
	// Class request, device-to-host,
	// recipient = interface.
	//
	WDF_USB_CONTROL_SETUP_PACKET_INIT(
		&setupPacket,
		BmRequestDeviceToHost,
		BmRequestToInterface,
		1,
		0x0190,
		0
	);
	setupPacket.Packet.bm.Request.Type = BmRequestClass;

	status = WdfUsbTargetDeviceSendControlTransferSynchronously(
		DeviceContext->UsbDevice,
		WDF_NO_HANDLE,
		NULL,
		&setupPacket,
		&memoryDescriptor,
		&cbTransferred
	);

	if (!NT_SUCCESS(status)) {
		TraceEvents(
			TRACE_LEVEL_ERROR,
			TRACE_DEVICE,
			"%!FUNC! battery control transfer failed %!STATUS!, cbTransferred = %lu",
			status,
			cbTransferred
		);
		return status;
	}

	if (cbTransferred < 3) {
		TraceEvents(
			TRACE_LEVEL_WARNING,
			TRACE_DEVICE,
			"%!FUNC! battery control transfer returned less than 3 bytes"
		);
		return STATUS_DEVICE_DATA_ERROR;
	}

	//
	// Return the battery level.
	//
	*BatteryLevel = buffer[2];

	TraceEvents(
		TRACE_LEVEL_INFORMATION,
		TRACE_DEVICE,
		"%!FUNC! battery level = %u",
		(UINT)*BatteryLevel
	);

	return STATUS_SUCCESS;
}

//
// Worker thread: owns the pipe server for the lifetime of the device.
//
static
DWORD
WINAPI
AmtPtpControlPanelPipeThread(
	_In_ LPVOID Context
)
{
	PDEVICE_CONTEXT devCtx = (PDEVICE_CONTEXT)Context;
	HANDLE          hPipe = INVALID_HANDLE_VALUE;
	BOOL            connected;
	UCHAR           requestByte = 0;
	UCHAR           response[2] = { 0 };
	DWORD           bytesRead = 0;
	DWORD           bytesWritten = 0;

	while (InterlockedCompareExchange(&devCtx->ControlPanelPipeRunning, 1, 1) == 1) {

		hPipe = CreateNamedPipeW(
			CONTROLPANEL_PIPE_NAME,
			PIPE_ACCESS_DUPLEX,
			PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
			PIPE_UNLIMITED_INSTANCES,
			CONTROLPANEL_PIPE_BUFFER,
			CONTROLPANEL_PIPE_BUFFER,
			0,
			NULL    // default SD: grants access to SYSTEM/Admins.
			        // If a non-admin client must connect, supply an SD that
			        // grants read/write to the desired SID.
		);

		if (hPipe == INVALID_HANDLE_VALUE) {
			TraceEvents(
				TRACE_LEVEL_ERROR,
				TRACE_DEVICE,
				"%!FUNC! CreateNamedPipeW failed %lu",
				GetLastError()
			);
			break;
		}

		//
		// Publish so stop routine can close it and unblock ConnectNamedPipe.
		//
		InterlockedExchangePointer(
			(PVOID volatile*)&devCtx->ControlPanelPipeHandle,
			hPipe
		);

		connected = ConnectNamedPipe(hPipe, NULL)
			? TRUE
			: (GetLastError() == ERROR_PIPE_CONNECTED);

		if (InterlockedCompareExchange(&devCtx->ControlPanelPipeRunning, 0, 0) == 0) {
			goto cleanup_pipe;
		}

		if (!connected) {
			goto cleanup_pipe;
		}

		//
		// A client is connected. Read one request byte.
		//
		if (ReadFile(hPipe, &requestByte, 1, &bytesRead, NULL) && bytesRead == 1) {

			switch (requestByte) {
			case CONTROLPANEL_CMD_RELOAD_SETTINGS:
			{
				NTSTATUS st = STATUS_SUCCESS;

				//
				// Haptics exist only on the Magic Trackpad 2; other families
				// have nothing to push and simply report OK.
				//
				if (AmtPtpIsMagicTrackpad2(devCtx)) {
					ULONG feedbackClick = ReadSettingValue(L"FeedbackClick", 0x08081E);
					ULONG feedbackRelease = ReadSettingValue(L"FeedbackRelease", 0x020218);
					st = AmtPtpSetHapticFeedback(devCtx, feedbackClick, feedbackRelease);
				}

				//
				// The pointer-lock settings are re-read lazily by the TYPE5 parser
				// on the next frame. A failed haptic transfer must not block that.
				//
				devCtx->PrevPtpReportAuxAndSettingsInited = FALSE;

				response[0] = NT_SUCCESS(st)
					? CONTROLPANEL_STATUS_OK
					: CONTROLPANEL_STATUS_FAIL;
				response[1] = 0;
				break;
			}
			case CONTROLPANEL_CMD_GET_BATTERY_LEVEL:
			{
				UCHAR    level = 0;
				NTSTATUS st = AmtPtpReadBatteryLevel(devCtx, &level);

				response[0] = NT_SUCCESS(st)
					? CONTROLPANEL_STATUS_OK
					: CONTROLPANEL_STATUS_FAIL;
				response[1] = level;
				break;
			}
			default:
				response[0] = CONTROLPANEL_STATUS_UNKNOWN;
				response[1] = 0;
				break;
			}

			WriteFile(hPipe, response, sizeof(response), &bytesWritten, NULL);
		}

		FlushFileBuffers(hPipe);
		DisconnectNamedPipe(hPipe);

cleanup_pipe:
		//
		// Only close the handle if we still own it. The stop routine may
		// have already closed it via the InterlockedExchangePointer swap.
		//
		if (InterlockedCompareExchangePointer(
				(PVOID volatile*)&devCtx->ControlPanelPipeHandle,
				NULL,
				hPipe) == hPipe) {
			CloseHandle(hPipe);
		}
		hPipe = INVALID_HANDLE_VALUE;
	}

	return 0;
}

_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS
AmtPtpControlPanelPipeStart(
	_In_ PDEVICE_CONTEXT DeviceContext
)
{
	PAGED_CODE();

	if (DeviceContext->ControlPanelPipeThread != NULL) {
		return STATUS_SUCCESS;
	}

	DeviceContext->ControlPanelPipeHandle = NULL;
	InterlockedExchange(&DeviceContext->ControlPanelPipeRunning, 1);

	DeviceContext->ControlPanelPipeThread = CreateThread(
		NULL,
		0,
		AmtPtpControlPanelPipeThread,
		DeviceContext,
		0,
		NULL
	);

	if (DeviceContext->ControlPanelPipeThread == NULL) {
		InterlockedExchange(&DeviceContext->ControlPanelPipeRunning, 0);

		TraceEvents(
			TRACE_LEVEL_ERROR,
			TRACE_DEVICE,
			"%!FUNC! CreateThread failed %lu",
			GetLastError()
		);
		return STATUS_INSUFFICIENT_RESOURCES;
	}

	TraceEvents(
		TRACE_LEVEL_INFORMATION,
		TRACE_DEVICE,
		"%!FUNC! control panel pipe server started"
	);

	return STATUS_SUCCESS;
}

_IRQL_requires_(PASSIVE_LEVEL)
VOID
AmtPtpControlPanelPipeStop(
	_In_ PDEVICE_CONTEXT DeviceContext
)
{
	HANDLE hPipe;

	PAGED_CODE();

	if (DeviceContext->ControlPanelPipeThread == NULL) {
		return;
	}

	InterlockedExchange(&DeviceContext->ControlPanelPipeRunning, 0);

	//
	// Closing the pipe handle forces a blocking ConnectNamedPipe (or
	// ReadFile) on the worker thread to return with an error, so the
	// thread can observe ControlPanelPipeRunning == 0 and exit.
	//
	hPipe = (HANDLE)InterlockedExchangePointer(
		(PVOID volatile*)&DeviceContext->ControlPanelPipeHandle,
		NULL
	);

	if (hPipe != NULL && hPipe != INVALID_HANDLE_VALUE) {
		CloseHandle(hPipe);
	}

	WaitForSingleObject(DeviceContext->ControlPanelPipeThread, 5000);

	CloseHandle(DeviceContext->ControlPanelPipeThread);
	DeviceContext->ControlPanelPipeThread = NULL;

	TraceEvents(
		TRACE_LEVEL_INFORMATION,
		TRACE_DEVICE,
		"%!FUNC! control panel pipe server stopped"
	);
}
