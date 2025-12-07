// ControlDevice.c: control device routines

#include <Driver.h>
#include "ControlDevice.tmh"

VOID
PtpFilterEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    size_t bytesReturned = 0;
	WDFDEVICE controlDevice;
	WDFDRIVER driver;
	PDRIVER_CONTEXT driverContext;
	PULONG pOutputBuffer = NULL;

    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(OutputBufferLength);
	UNREFERENCED_PARAMETER(InputBufferLength);
	
	controlDevice = WdfIoQueueGetDevice(Queue);
	driver = WdfDeviceGetDriver(controlDevice);
	driverContext = PtpFilterDriverGetContext(driver);

    switch (IoControlCode)
    {
    case IOCTL_PTPFILTER_RELOAD_SETTINGS:
		if (driverContext->CDFirstDevice == NULL) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! CDFirstDevice == NULL");
			status = STATUS_INVALID_DEVICE_STATE;
		} else {
			PtpFilterReadSettings(driverContext);
			PtpFilterSetHapticFeedback(driverContext->CDFirstDevice, driverContext->FeedbackClick, driverContext->FeedbackRelease);
		}
        break;
		
	case IOCTL_PTPFILTER_GET_BATTERY:
		if (driverContext->CDFirstDevice == NULL) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! CDFirstDevice == NULL");
			status = STATUS_INVALID_DEVICE_STATE;
		} else {
			BYTE mt2Battery[64] = { 0 };
			status = PtpFilterGetHidInputReport(driverContext->CDFirstDevice, 0x90, mt2Battery, sizeof(mt2Battery));
			if (!NT_SUCCESS(status)) {
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterGetHidInputReport failed: %!STATUS!", status);
			}
			else if (mt2Battery[0] != 0x90) {
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! mt2Battery[0] != 0x90");
				status = STATUS_INVALID_DEVICE_STATE;
			}
			else if (OutputBufferLength < sizeof(ULONG)) {
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! OutputBufferLength < sizeof(ULONG)");
				status = STATUS_BUFFER_TOO_SMALL;
			}
			else {
				pOutputBuffer = NULL;
				status = WdfRequestRetrieveOutputBuffer(Request, sizeof(ULONG), (PVOID*)&pOutputBuffer, NULL);
				if (!NT_SUCCESS(status)) {
					TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestRetrieveOutputBuffer failed: %!STATUS!", status);
				}
				else {
					*pOutputBuffer = (ULONG)mt2Battery[2];
					bytesReturned = sizeof(ULONG);
				}
			}
		}
		break;

    default:
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_DRIVER, 
            "Unsupported IOCTL: 0x%08lX", IoControlCode);
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    WdfRequestCompleteWithInformation(Request, status, bytesReturned);
}

NTSTATUS
PtpFilterControlDeviceQueueInitialize(
    _In_ WDFDEVICE Device
)
{
    WDF_IO_QUEUE_CONFIG queueConfig;
    NTSTATUS status;
    WDFQUEUE queue;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = PtpFilterEvtIoDeviceControl;

    status = WdfIoQueueCreate(Device,
                              &queueConfig,
                              WDF_NO_OBJECT_ATTRIBUTES,
                              &queue);

    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, 
            "WdfIoQueueCreate failed: %!STATUS!", status);
    }

    return status;
}

NTSTATUS
PtpFilterCreateControlDevice(
    _In_ WDFDRIVER Driver
)
{
    NTSTATUS status;
    PWDFDEVICE_INIT deviceInit = NULL;
    WDFDEVICE controlDevice;
    WDF_OBJECT_ATTRIBUTES attributes;
    PDRIVER_CONTEXT driverContext;
    
    DECLARE_CONST_UNICODE_STRING(controlDeviceName, L"\\Device\\AmtPtpControlDeviceUm");
    DECLARE_CONST_UNICODE_STRING(controlDeviceSymLink, L"\\DosDevices\\AmtPtpControlDeviceUm");

    deviceInit = WdfControlDeviceInitAllocate(Driver, 
                                              &SDDL_DEVOBJ_SYS_ALL_ADM_ALL);
    if (deviceInit == NULL) {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, 
            "WdfControlDeviceInitAllocate failed");
        return status;
    }

    status = WdfDeviceInitAssignName(deviceInit, &controlDeviceName);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, 
            "WdfDeviceInitAssignName failed: %!STATUS!", status);
        WdfDeviceInitFree(deviceInit);
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    status = WdfDeviceCreate(&deviceInit, &attributes, &controlDevice);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, 
            "WdfDeviceCreate (control) failed: %!STATUS!", status);
        return status;
    }

    status = WdfDeviceCreateSymbolicLink(controlDevice, &controlDeviceSymLink);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, 
            "WdfDeviceCreateSymbolicLink failed: %!STATUS!", status);
        WdfObjectDelete(controlDevice); 
        return status;
    }

    status = PtpFilterControlDeviceQueueInitialize(controlDevice);
    if (!NT_SUCCESS(status)) {
        WdfObjectDelete(controlDevice);
        return status;
    }

    WdfControlFinishInitializing(controlDevice);

    driverContext = PtpFilterDriverGetContext(Driver);
    driverContext->ControlDevice = controlDevice;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, 
        "Control device created successfully");
    
    return STATUS_SUCCESS;
}

VOID
PtpFilterDeleteControlDevice(
    _In_ WDFOBJECT Context
)
{
    PDRIVER_CONTEXT driverContext = PtpFilterDriverGetContext((WDFDRIVER)Context);

    if (driverContext->ControlDevice != NULL) {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "Deleting Control Device");
        WdfObjectDelete(driverContext->ControlDevice);
        driverContext->ControlDevice = NULL;
    }
}
