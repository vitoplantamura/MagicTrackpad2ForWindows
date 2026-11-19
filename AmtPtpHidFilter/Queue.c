// Queue.c: IO-queue related operations

#include <Driver.h>
#include "Queue.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, PtpFilterIoQueueInitialize)
#endif

NTSTATUS
PtpFilterIoQueueInitialize(
    _In_ WDFDEVICE Device
)
{
    WDFQUEUE queue;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_OBJECT_ATTRIBUTES queueAttributes;
    PDEVICE_CONTEXT deviceContext;
    PQUEUE_CONTEXT queueContext;
    NTSTATUS status;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

    deviceContext = PtpFilterGetContext(Device);

    // First queue for system-wide HID controls
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&queueAttributes, QUEUE_CONTEXT);
    queueConfig.EvtIoInternalDeviceControl = FilterEvtIoIntDeviceControl;
    queueConfig.EvtIoStop = FilterEvtIoStop;
    status = WdfIoQueueCreate(Device, &queueConfig, &queueAttributes, &queue);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "%!FUNC! WdfIoQueueCreate failed %!STATUS!", status);
        goto exit;
    }

    queueContext = PtpFilterQueueGetContext(queue);
    queueContext->Device = deviceContext->Device;
    queueContext->DeviceIoTarget = deviceContext->HidIoTarget;

    // Second queue for HID read requests
    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchManual);
    queueConfig.PowerManaged = WdfFalse;
    status = WdfIoQueueCreate(Device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &deviceContext->HidReadQueue);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "%!FUNC! WdfIoQueueCreate (Input) failed %!STATUS!", status);
        goto exit;
    }

    // Third queue for haptic Manual Trigger output reports from Windows, one at a time. Its callback
    // waits on a lock, sleeps and sends synchronously, so it must run at PASSIVE_LEVEL: the device
    // object doesn't set an execution level, and a request forwarded to an idle queue is otherwise
    // dispatched at the IRQL of whoever sent it.
    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.PowerManaged = WdfFalse;
    queueConfig.EvtIoInternalDeviceControl = PtpFilterHapticEvtIoIntDeviceControl;
    WDF_OBJECT_ATTRIBUTES_INIT(&queueAttributes);
    queueAttributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfIoQueueCreate(Device, &queueConfig, &queueAttributes, &deviceContext->HapticQueue);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "%!FUNC! WdfIoQueueCreate (Haptic) failed %!STATUS!", status);
    }

exit:
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

VOID
FilterEvtIoIntDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
)
{
    PQUEUE_CONTEXT queueContext;
    PDEVICE_CONTEXT deviceContext;
    BOOLEAN requestPending = FALSE;
	NTSTATUS status = STATUS_UNSUCCESSFUL;
    
    UNREFERENCED_PARAMETER(InputBufferLength);
    UNREFERENCED_PARAMETER(OutputBufferLength);

    queueContext = PtpFilterQueueGetContext(Queue);
    deviceContext = PtpFilterGetContext(queueContext->Device);

	switch (IoControlCode)
	{
	case IOCTL_HID_GET_DEVICE_DESCRIPTOR:
		status = PtpFilterGetHidDescriptor(queueContext->Device, Request);
		break;
	case IOCTL_HID_GET_DEVICE_ATTRIBUTES:
		status = PtpFilterGetDeviceAttribs(queueContext->Device, Request);
		break;
	case IOCTL_HID_GET_REPORT_DESCRIPTOR:
		status = PtpFilterGetReportDescriptor(queueContext->Device, Request);
		break;
	case IOCTL_HID_GET_STRING:
		status = PtpFilterGetStrings(queueContext->Device, Request, &requestPending);
		break;
	case IOCTL_HID_READ_REPORT:
        PtpFilterInputProcessRequest(queueContext->Device, Request);
		requestPending = TRUE;
		break;
	case IOCTL_HID_GET_FEATURE:
		status = PtpFilterGetHidFeatures(queueContext->Device, Request);
		break;
	case IOCTL_HID_SET_FEATURE:
		status = PtpFilterSetHidFeatures(queueContext->Device, Request);
		break;
	case IOCTL_HID_WRITE_REPORT:
	case IOCTL_HID_SET_OUTPUT_REPORT:
		status = PtpFilterHapticOutputReport(queueContext->Device, Request, &requestPending);
		break;
	case IOCTL_UMDF_HID_SET_OUTPUT_REPORT:
	case IOCTL_UMDF_HID_GET_INPUT_REPORT:
	case IOCTL_HID_ACTIVATE_DEVICE:
	case IOCTL_HID_DEACTIVATE_DEVICE:
	case IOCTL_HID_SEND_IDLE_NOTIFICATION_REQUEST:
	default:
		status = STATUS_NOT_SUPPORTED;
		TraceEvents(TRACE_LEVEL_WARNING, TRACE_QUEUE, "%!FUNC!: %s is not yet implemented", PtpFilterDiagnosticsIoControlGetString(IoControlCode));
		break;
	}

    if (requestPending != TRUE)
    {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC!: %s, Status = %!STATUS!", PtpFilterDiagnosticsIoControlGetString(IoControlCode), status);
        WdfRequestComplete(Request, status);
    }
}

VOID
FilterEvtIoStop(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ ULONG ActionFlags
)
{
    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(Request);
    UNREFERENCED_PARAMETER(ActionFlags);
}
