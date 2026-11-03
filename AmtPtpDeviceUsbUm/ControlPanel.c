// ControlPanel.c: Control Panel named-pipe server.

#include <driver.h>
#include <sddl.h>
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
// Fires the haptic actuator once. Optionally followed, in the same write, by three bytes:
// the strength in the encoding of the FeedbackClick setting, low byte first (e.g. 17 06 06
// for 0x060617, a medium click). Without them the current FeedbackClick setting is used.
// Actuations are at least 20 ms apart.
#define CONTROLPANEL_CMD_ACTUATE            0x02

// Minimum time between two actuations, in ms (as LinuxMagicForce uses).
#define ACTUATE_MIN_GAP_MS                  20

// SYSTEM, Administrators, the UMDF host (LocalService, User-Mode Drivers): all access.
// Interactive users: read/write, so that an app in the user's session can fire the actuator
// without elevation. This DACL is only applied when the AllowNonAdminAccess setting is
// present and non-zero; otherwise the pipe keeps its default (admin-only) DACL.
#define CONTROLPANEL_PIPE_SDDL L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;GA;;;UD)(A;;GRGW;;;IU)"

//
// Response status bytes.
//
#define CONTROLPANEL_STATUS_OK      0x00
#define CONTROLPANEL_STATUS_FAIL    0xFF
#define CONTROLPANEL_STATUS_UNKNOWN 0xFE

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

// Milliseconds on a precise monotonic clock (GetTickCount64 steps by ~16 ms).
static
ULONGLONG
AmtPtpMilliseconds(VOID)
{
    LARGE_INTEGER counter, frequency;

    QueryPerformanceCounter(&counter);
    QueryPerformanceFrequency(&frequency);
    return (ULONGLONG)(counter.QuadPart / (frequency.QuadPart / 1000));
}

//
// Waits on either the overlapped I/O event or the stop event.
// Returns TRUE if the I/O completed (caller must call GetOverlappedResult),
// FALSE if stop was requested (caller must CancelIoEx + GetOverlappedResult).
//
static
BOOL
AmtPtpWaitForOverlappedIoOrStop(
    _In_  HANDLE    hPipe,
    _In_  PDEVICE_CONTEXT devCtx,
    _In_  LPOVERLAPPED pov
)
{
    HANDLE waitHandles[2];
    DWORD  waitResult;

    waitHandles[0] = pov->hEvent;
    waitHandles[1] = devCtx->ControlPanelStopEvent;

    waitResult = WaitForMultipleObjects(2, waitHandles, FALSE, INFINITE);

    if (waitResult == WAIT_OBJECT_0 + 1) {
        //
        // Stop requested. Cancel the pending I/O and reap it so the
        // OVERLAPPED is no longer in use before we return.
        //
        CancelIoEx(hPipe, pov);
        {
            DWORD dummy = 0;
            GetOverlappedResult(hPipe, pov, &dummy, TRUE);
        }
        return FALSE;
    }

    return TRUE;
}

static
DWORD
WINAPI
AmtPtpControlPanelPipeThread(
    _In_ LPVOID Context
)
{
    PDEVICE_CONTEXT devCtx = (PDEVICE_CONTEXT)Context;
    HANDLE          hPipe = INVALID_HANDLE_VALUE;
    HANDLE          ioEvent = NULL;
    OVERLAPPED      ov;
    BOOL            connected;
    UCHAR           request[4] = { 0 };
    UCHAR           response[2] = { 0 };
    DWORD           bytesRead = 0;
    DWORD           bytesWritten = 0;
    SECURITY_ATTRIBUTES pipeSecurity = { sizeof(SECURITY_ATTRIBUTES), NULL, FALSE };
    ULONGLONG       lastActuate = 0;

    ioEvent = CreateEventW(NULL, TRUE /*manual reset*/, FALSE, NULL);
    if (ioEvent == NULL) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
            "%!FUNC! CreateEventW failed %lu", GetLastError());
        return 0;
    }

    // Non-admins may open the pipe only when the AllowNonAdminAccess setting is present
    // and non-zero. Otherwise leave the default DACL, which only lets administrators write.
    if (ReadSettingValue(L"AllowNonAdminAccess", 0) != 0) {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(CONTROLPANEL_PIPE_SDDL, SDDL_REVISION_1,
                &pipeSecurity.lpSecurityDescriptor, NULL)) {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
                "%!FUNC! ConvertStringSecurityDescriptorToSecurityDescriptorW failed %lu, using the default DACL", GetLastError());
            pipeSecurity.lpSecurityDescriptor = NULL;
        }
    }

    while (InterlockedCompareExchange(&devCtx->ControlPanelPipeRunning, 1, 1) == 1) {

        hPipe = CreateNamedPipeW(
            CONTROLPANEL_PIPE_NAME,
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,   // <-- overlapped
            // Message mode so a request is read as one message: the 1-byte commands
            // (RELOAD_SETTINGS / GET_BATTERY_LEVEL) and the 1- or 4-byte ACTUATE command
            // all complete their read as soon as that single write arrives. Byte mode
            // would make the 4-byte read below block forever for the 1-byte commands.
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            CONTROLPANEL_PIPE_BUFFER,
            CONTROLPANEL_PIPE_BUFFER,
            0,
            pipeSecurity.lpSecurityDescriptor != NULL ? &pipeSecurity : NULL
        );

        if (hPipe == INVALID_HANDLE_VALUE) {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
                "%!FUNC! CreateNamedPipeW failed %lu", GetLastError());
            break;
        }

        //
        // Publish the handle so the stop routine can CancelIoEx it.
        // The worker remains the sole owner and will close it in cleanup.
        //
        InterlockedExchangePointer(
            (PVOID volatile*)&devCtx->ControlPanelPipeHandle,
            hPipe
        );

        //
        // Overlapped ConnectNamedPipe.
        //
        RtlZeroMemory(&ov, sizeof(ov));
        ov.hEvent = ioEvent;
        ResetEvent(ioEvent);

        connected = ConnectNamedPipe(hPipe, &ov);
        if (!connected) {
            DWORD err = GetLastError();

            if (err == ERROR_IO_PENDING) {
                if (!AmtPtpWaitForOverlappedIoOrStop(hPipe, devCtx, &ov)) {
                    goto cleanup_pipe;
                }
                {
                    DWORD dummy = 0;
                    if (!GetOverlappedResult(hPipe, &ov, &dummy, FALSE)) {
                        goto cleanup_pipe;
                    }
                }
                connected = TRUE;
            }
            else if (err == ERROR_PIPE_CONNECTED) {
                connected = TRUE;
            }
            else if (err == ERROR_NO_DATA) {
                //
                // Client connected and disconnected before ConnectNamedPipe.
                // Treat as a completed connection so we can recycle.
                //
                connected = TRUE;
            }
            else {
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
                    "%!FUNC! ConnectNamedPipe failed %lu", err);
                goto cleanup_pipe;
            }
        }

        if (InterlockedCompareExchange(&devCtx->ControlPanelPipeRunning, 0, 0) == 0) {
            goto cleanup_pipe;
        }

        //
        // Overlapped read of one request message: the command byte and, for ACTUATE,
        // its optional three strength bytes. Message mode completes the read as soon as
        // the client's single write arrives, whether it is 1 byte (legacy commands) or
        // 4 bytes (ACTUATE with an explicit strength).
        //
        RtlZeroMemory(&ov, sizeof(ov));
        ov.hEvent = ioEvent;
        ResetEvent(ioEvent);

        if (!ReadFile(hPipe, request, sizeof(request), &bytesRead, &ov)) {
            DWORD err = GetLastError();

            if (err == ERROR_IO_PENDING) {
                if (!AmtPtpWaitForOverlappedIoOrStop(hPipe, devCtx, &ov)) {
                    goto cleanup_pipe;
                }
                if (!GetOverlappedResult(hPipe, &ov, &bytesRead, FALSE)) {
                    goto cleanup_pipe;
                }
            }
            else {
                goto cleanup_pipe;
            }
        }

        if (bytesRead < 1) {
            goto cleanup_pipe;
        }

        //
        // Dispatch the command.
        //
        switch (request[0]) {
        case CONTROLPANEL_CMD_RELOAD_SETTINGS:
        {
            ULONG feedbackClick = ReadSettingValue(L"FeedbackClick", 0x08081E);
            ULONG feedbackRelease = ReadSettingValue(L"FeedbackRelease", 0x020218);
            NTSTATUS st = AmtPtpSetHapticFeedback(devCtx, feedbackClick, feedbackRelease);

            if (NT_SUCCESS(st)) {
                devCtx->PrevPtpReportAuxAndSettingsInited = FALSE;
            }

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
        case CONTROLPANEL_CMD_ACTUATE:
        {
            // One request at a time is served, so the gap needs no lock.
            ULONG       feedback = (bytesRead == 4)
                ? (ULONG)request[1] | ((ULONG)request[2] << 8) | ((ULONG)request[3] << 16)
                : ReadSettingValue(L"FeedbackClick", 0x08081E);
            ULONGLONG   elapsed = AmtPtpMilliseconds() - lastActuate;
            NTSTATUS    st;

            if (elapsed < ACTUATE_MIN_GAP_MS) {
                Sleep((DWORD)(ACTUATE_MIN_GAP_MS - elapsed));
            }
            lastActuate = AmtPtpMilliseconds();
            st = AmtPtpActuate(devCtx, feedback);

            response[0] = NT_SUCCESS(st)
                ? CONTROLPANEL_STATUS_OK
                : CONTROLPANEL_STATUS_FAIL;
            response[1] = 0;
            break;
        }
        default:
            response[0] = CONTROLPANEL_STATUS_UNKNOWN;
            response[1] = 0;
            break;
        }

        //
        // Overlapped write of the two response bytes.
        //
        RtlZeroMemory(&ov, sizeof(ov));
        ov.hEvent = ioEvent;
        ResetEvent(ioEvent);

        if (!WriteFile(hPipe, response, sizeof(response), &bytesWritten, &ov)) {
            DWORD err = GetLastError();

            if (err == ERROR_IO_PENDING) {
                if (!AmtPtpWaitForOverlappedIoOrStop(hPipe, devCtx, &ov)) {
                    goto cleanup_pipe;
                }
                GetOverlappedResult(hPipe, &ov, &bytesWritten, FALSE);
            }
            // else: best-effort; fall through to disconnect
        }

        FlushFileBuffers(hPipe);
        DisconnectNamedPipe(hPipe);

cleanup_pipe:
        //
        // Worker is the sole owner of hPipe. Clear the published pointer
        // and close it here so the stop routine never races with us.
        //
        InterlockedExchangePointer(
            (PVOID volatile*)&devCtx->ControlPanelPipeHandle,
            NULL
        );

        if (hPipe != INVALID_HANDLE_VALUE) {
            CloseHandle(hPipe);
        }
        hPipe = INVALID_HANDLE_VALUE;
    }

    CloseHandle(ioEvent);
    if (pipeSecurity.lpSecurityDescriptor != NULL) {
        LocalFree(pipeSecurity.lpSecurityDescriptor);
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

    //
    // Manual-reset event: once stop is requested, every waiter wakes.
    //
    DeviceContext->ControlPanelStopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (DeviceContext->ControlPanelStopEvent == NULL) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
            "%!FUNC! CreateEventW failed %lu", GetLastError());
        return STATUS_INSUFFICIENT_RESOURCES;
    }

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
        CloseHandle(DeviceContext->ControlPanelStopEvent);
        DeviceContext->ControlPanelStopEvent = NULL;

        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE,
            "%!FUNC! CreateThread failed %lu", GetLastError());
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE,
        "%!FUNC! control panel pipe server started");

    return STATUS_SUCCESS;
}

_IRQL_requires_(PASSIVE_LEVEL)
VOID
AmtPtpControlPanelPipeStop(
    _In_ PDEVICE_CONTEXT DeviceContext
)
{
    HANDLE hThread;
    HANDLE hPipe;

    PAGED_CODE();

    if (DeviceContext->ControlPanelPipeThread == NULL) {
        return;
    }

    //
    // 1. Tell the worker we are stopping.
    //
    InterlockedExchange(&DeviceContext->ControlPanelPipeRunning, 0);

    //
    // 2. Wake the worker out of any overlapped wait.
    //
    if (DeviceContext->ControlPanelStopEvent != NULL) {
        SetEvent(DeviceContext->ControlPanelStopEvent);
    }

    //
    // 3. Cancel any in-flight overlapped I/O on the published handle.
    //    The worker still owns the handle and will close it in cleanup.
    //
    hPipe = (HANDLE)DeviceContext->ControlPanelPipeHandle;
    if (hPipe != NULL && hPipe != INVALID_HANDLE_VALUE) {
        CancelIoEx(hPipe, NULL);
    }

    //
    // 4. Now it is safe to wait indefinitely: the stop event guarantees
    //    the worker will not stay blocked in ConnectNamedPipe/ReadFile.
    //
    hThread = DeviceContext->ControlPanelPipeThread;
    WaitForSingleObject(hThread, INFINITE);
    CloseHandle(hThread);
    DeviceContext->ControlPanelPipeThread = NULL;

    if (DeviceContext->ControlPanelStopEvent != NULL) {
        CloseHandle(DeviceContext->ControlPanelStopEvent);
        DeviceContext->ControlPanelStopEvent = NULL;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE,
        "%!FUNC! control panel pipe server stopped");
}
