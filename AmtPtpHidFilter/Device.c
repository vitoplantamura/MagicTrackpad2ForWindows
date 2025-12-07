// Device.c: Device-specific D0<->D3 handler and other misc procedures

#include <Driver.h>
#include "Device.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, PtpFilterCreateDevice)
#endif

VOID
PtpFilterEvtDeviceContextCleanup(
    _In_ WDFOBJECT DeviceObject
)
{
	WDFDRIVER driver;
	PDRIVER_CONTEXT driverContext;
	
    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Cleanup");

	// delete the control device
	driver = WdfDeviceGetDriver((WDFDEVICE)DeviceObject);
	driverContext = PtpFilterDriverGetContext(driver);
	if (driverContext->CDFirstDevice == (WDFDEVICE)DeviceObject) {
		PtpFilterDeleteControlDevice((WDFOBJECT)driver);
		driverContext->CDFirstDevice = NULL;
	}
}

NTSTATUS
PtpFilterCreateDevice(
    _Inout_ PWDFDEVICE_INIT DeviceInit,
	_In_ WDFDRIVER Driver
)
{
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDF_PNPPOWER_EVENT_CALLBACKS pnpPowerCallbacks;
    WDF_TIMER_CONFIG timerConfig;
    WDF_WORKITEM_CONFIG workitemConfig;
    WDFDEVICE device;
    PDEVICE_CONTEXT deviceContext;
	PDRIVER_CONTEXT driverContext;
    NTSTATUS status;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");

    // Initialize Power Callback
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDevicePrepareHardware = PtpFilterPrepareHardware;
    pnpPowerCallbacks.EvtDeviceD0Entry = PtpFilterDeviceD0Entry;
    pnpPowerCallbacks.EvtDeviceD0Exit = PtpFilterDeviceD0Exit;
    pnpPowerCallbacks.EvtDeviceSelfManagedIoInit = PtpFilterSelfManagedIoInit;
    pnpPowerCallbacks.EvtDeviceSelfManagedIoRestart = PtpFilterSelfManagedIoRestart;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnpPowerCallbacks);

    // Create WDF device object
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);
	deviceAttributes.EvtCleanupCallback = PtpFilterEvtDeviceContextCleanup;
    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "WdfDeviceCreate failed: %!STATUS!", status);
        goto exit;
    }

    // Initialize context and interface
    deviceContext = PtpFilterGetContext(device);
    deviceContext->Device = device;
    deviceContext->WdmDeviceObject = WdfDeviceWdmGetDeviceObject(device);
    if (deviceContext->WdmDeviceObject == NULL) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "WdfDeviceWdmGetDeviceObject failed");
        goto exit;
    }

    status = WdfDeviceCreateDeviceInterface(device,&GUID_DEVICEINTERFACE_AmtPtpHidFilter, NULL);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "WdfDeviceCreateDeviceInterface failed: %!STATUS!", status);
        goto exit;
    }
	
	// Initialize pointer lock vars
	deviceContext->PrevPtpReportAux1.Id = (UINT32)-1;
	deviceContext->PrevPtpReportAux2.Id = (UINT32)-1;

    // Initialize read buffer
    status = WdfLookasideListCreate(WDF_NO_OBJECT_ATTRIBUTES, REPORT_BUFFER_SIZE,
        NonPagedPoolNx, WDF_NO_OBJECT_ATTRIBUTES, PTP_LIST_POOL_TAG,
        &deviceContext->HidReadBufferLookaside
    );
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "WdfLookasideListCreate failed: %!STATUS!", status);
    }

    // Initialize HID recovery timer
    WDF_TIMER_CONFIG_INIT(&timerConfig, PtpFilterRecoveryTimerCallback);
    timerConfig.AutomaticSerialization = TRUE;
    WDF_OBJECT_ATTRIBUTES_INIT(&deviceAttributes);
    deviceAttributes.ParentObject = device;
    deviceAttributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfTimerCreate(&timerConfig, &deviceAttributes, &deviceContext->HidTransportRecoveryTimer);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "WdfTimerCreate failed: %!STATUS!", status);
    }

    // Initialize HID recovery workitem
    WDF_WORKITEM_CONFIG_INIT(&workitemConfig, PtpFilterWorkItemCallback);
    WDF_OBJECT_ATTRIBUTES_INIT(&deviceAttributes);
    deviceAttributes.ParentObject = device;
    status = WdfWorkItemCreate(&workitemConfig, &deviceAttributes, &deviceContext->HidTransportRecoveryWorkItem);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "HidTransportRecoveryWorkItem failed: %!STATUS!", status);
    }

    // Set initial state
    deviceContext->VendorID = 0;
    deviceContext->ProductID = 0;
    deviceContext->VersionNumber = 0;
    deviceContext->DeviceConfigured = FALSE;

    // Initialize IO queue
    status = PtpFilterIoQueueInitialize(device);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "PtpFilterIoQueueInitialize failed: %!STATUS!", status);
    }
	
	// Create the control device
	driverContext = PtpFilterDriverGetContext(Driver);
	if (driverContext->CDFirstDevice == NULL && driverContext->ControlDevice == NULL) {
		status = PtpFilterCreateControlDevice(Driver);
		if (!NT_SUCCESS(status)) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "PtpFilterCreateControlDevice failed %!STATUS!", status);
		}
		else {
			driverContext->CDFirstDevice = device;
		}
	}

exit:
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterPrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourceList,
    _In_ WDFCMRESLIST ResourceListTranslated
)
{
    PDEVICE_CONTEXT deviceContext;
    NTSTATUS status = STATUS_SUCCESS;
    
    // We don't need to retrieve resources since this works as a filter now
    UNREFERENCED_PARAMETER(ResourceList);
    UNREFERENCED_PARAMETER(ResourceListTranslated);

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");
    deviceContext = PtpFilterGetContext(Device);

    // Initialize IDs, set to zero
    deviceContext->VendorID = 0;
    deviceContext->ProductID = 0;
    deviceContext->VersionNumber = 0;
    deviceContext->DeviceConfigured = FALSE;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterDeviceD0Entry(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE PreviousState
)
{
    NTSTATUS status = STATUS_SUCCESS;

    PAGED_CODE();
    UNREFERENCED_PARAMETER(Device);
    UNREFERENCED_PARAMETER(PreviousState);
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterDeviceD0Exit(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE TargetState
)
{
    PDEVICE_CONTEXT deviceContext;
    NTSTATUS status = STATUS_SUCCESS;
    WDFREQUEST outstandingRequest;

    UNREFERENCED_PARAMETER(TargetState);

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");
    deviceContext = PtpFilterGetContext(Device);

    // Reset device state
    deviceContext->DeviceConfigured = FALSE;

    // Cancelling all outstanding requests
    while (NT_SUCCESS(status)) {
        status = WdfIoQueueRetrieveNextRequest(
            deviceContext->HidReadQueue,
            &outstandingRequest
        );

        if (NT_SUCCESS(status)) {
            WdfRequestComplete(outstandingRequest, STATUS_CANCELLED);
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", STATUS_SUCCESS);
    return STATUS_SUCCESS;
}

NTSTATUS
PtpFilterSelfManagedIoInit(
    _In_ WDFDEVICE Device
)
{
    NTSTATUS status;
    PDEVICE_CONTEXT deviceContext;
    WDF_MEMORY_DESCRIPTOR hidAttributeMemoryDescriptor;
    HID_DEVICE_ATTRIBUTES deviceAttributes;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");

    deviceContext = PtpFilterGetContext(Device);
    status = PtpFilterDetourWindowsHIDStack(Device);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterDetourWindowsHIDStack failed, Status = %!STATUS!", status);
        goto exit;
    }

    // Request device attribute descriptor for self-identification.
    RtlZeroMemory(&deviceAttributes, sizeof(deviceAttributes));
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(
        &hidAttributeMemoryDescriptor,
        (PVOID)&deviceAttributes,
        sizeof(deviceAttributes)
    );

    status = WdfIoTargetSendInternalIoctlSynchronously(
        deviceContext->HidIoTarget, NULL,
        IOCTL_HID_GET_DEVICE_ATTRIBUTES,
        NULL, &hidAttributeMemoryDescriptor, NULL, NULL);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfIoTargetSendInternalIoctlSynchronously failed, Status = %!STATUS!", status);
        goto exit;
    }

    deviceContext->VendorID = deviceAttributes.VendorID;
    deviceContext->ProductID = deviceAttributes.ProductID;
    deviceContext->VersionNumber = deviceAttributes.VersionNumber;
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Device %x:%x, Version 0x%x", deviceContext->VendorID,
        deviceContext->ProductID, deviceContext->VersionNumber);

    status = PtpFilterConfigureMultiTouch(Device);
    if (!NT_SUCCESS(status)) {
        // If this failed, we will retry after 2 seconds (and pretend nothing happens)
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterConfigureMultiTouch failed, Status = %!STATUS!", status);
        status = STATUS_SUCCESS;
        WdfTimerStart(deviceContext->HidTransportRecoveryTimer, WDF_REL_TIMEOUT_IN_SEC(2));
        goto exit;
    }

    // Stamp last query performance counter
    KeQueryPerformanceCounter(&deviceContext->LastReportTime);

    // Set device state
    deviceContext->DeviceConfigured = TRUE;

exit:
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterSelfManagedIoRestart(
    _In_ WDFDEVICE Device
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PDEVICE_CONTEXT deviceContext;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");
    deviceContext = PtpFilterGetContext(Device);

    // If this is first D0, it will be done in self-managed IO init.
    if (deviceContext->IsHidIoDetourCompleted) {
        status = PtpFilterConfigureMultiTouch(Device);
        if (!NT_SUCCESS(status)) {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterConfigureMultiTouch failed, Status = %!STATUS!", status);
            // If this failed, we will retry after 2 seconds (and pretend nothing happens)
            status = STATUS_SUCCESS;
            WdfTimerStart(deviceContext->HidTransportRecoveryTimer, WDF_REL_TIMEOUT_IN_SEC(2));
            goto exit;
        }
    }
    else {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! HID detour should already complete here");
        status = STATUS_INVALID_STATE_TRANSITION;
    }

    // Stamp last query performance counter
    KeQueryPerformanceCounter(&deviceContext->LastReportTime);

    // Set device state
    deviceContext->DeviceConfigured = TRUE;

exit:
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterGetHidInputReport(
    _In_ WDFDEVICE Device,
    _In_ UCHAR ReportId,
    _Out_ PUCHAR pReportBuffer,
    _In_ ULONG ReportBufferSize
    )
{
    NTSTATUS status = STATUS_SUCCESS;
    PDEVICE_CONTEXT deviceContext = NULL;
    WDFREQUEST request = NULL;
    WDFMEMORY memory = NULL;
    PHID_XFER_PACKET pHidPacket = NULL;
    PUCHAR pAllocatedBuffer = NULL;
	WDF_OBJECT_ATTRIBUTES attributes;
	PMDL pMDL = NULL;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry: ReportId=0x%02x, BufferSize=%lu", ReportId, ReportBufferSize);

    if (Device == NULL || pReportBuffer == NULL || ReportBufferSize <= 1) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Invalid parameter");
        return STATUS_INVALID_PARAMETER;
    }

    deviceContext = PtpFilterGetContext(Device);

    // Allocate request
    status = WdfRequestCreate(WDF_NO_OBJECT_ATTRIBUTES, deviceContext->HidIoTarget, &request);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestCreate failed: %!STATUS!", status);
        goto Exit;
    }

    // Allocate buffer for HID_XFER_PACKET + output report buffer
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = request;
    status = WdfMemoryCreate(&attributes,
                             NonPagedPoolNx,
                             0,
                             sizeof(HID_XFER_PACKET) + ReportBufferSize,
                             &memory,
                             (PVOID*)&pAllocatedBuffer);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfMemoryCreate failed: %!STATUS!", status);
        goto Exit;
    }

    pHidPacket = (PHID_XFER_PACKET)pAllocatedBuffer;
    PUCHAR pOutReportBuffer = pAllocatedBuffer + sizeof(HID_XFER_PACKET);

    // Initialize HID_XFER_PACKET for GET
    RtlZeroMemory(pHidPacket, sizeof(HID_XFER_PACKET) + ReportBufferSize);
    pHidPacket->reportId = ReportId;
    pHidPacket->reportBuffer = pOutReportBuffer;
    pHidPacket->reportBufferLen = ReportBufferSize - 1;
	pOutReportBuffer[0] = ReportId;

    // Format request
    status = WdfIoTargetFormatRequestForInternalIoctl(
        deviceContext->HidIoTarget,
        request,
        IOCTL_HID_GET_INPUT_REPORT,
        NULL,               // No input buffer for GET
		NULL,
        memory,             // Output buffer (contains HID_XFER_PACKET + data buffer)
        NULL);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfIoTargetFormatRequestForInternalIoctl failed: %!STATUS!", status);
        goto Exit;
    }

    PIRP irp = WdfRequestWdmGetIrp(request);
    if (irp == NULL) {
        status = STATUS_UNSUCCESSFUL;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestWdmGetIrp failed");
        goto Exit;
    }
    irp->UserBuffer = pHidPacket;
	
	pMDL = IoAllocateMdl((PVOID)pOutReportBuffer, ReportBufferSize - 1, FALSE, FALSE, NULL);
    if (pMDL == NULL) {
        status = STATUS_UNSUCCESSFUL;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! IoAllocateMdl failed");
        goto Exit;
    }
	MmBuildMdlForNonPagedPool(pMDL);
	irp->MdlAddress = pMDL;

    // Send synchronously
    WDF_REQUEST_SEND_OPTIONS sendOptions;
    WDF_REQUEST_SEND_OPTIONS_INIT(&sendOptions, WDF_REQUEST_SEND_OPTION_SYNCHRONOUS);

    if (!WdfRequestSend(request, deviceContext->HidIoTarget, &sendOptions)) {
        status = WdfRequestGetStatus(request);
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestSend failed: %!STATUS!", status);
        goto Exit;
    }

    // Copy response back to caller
    if (pHidPacket->reportBufferLen > ReportBufferSize) {
        status = STATUS_BUFFER_OVERFLOW;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Response too large");
        goto Exit;
    }

    RtlCopyMemory(pReportBuffer, pHidPacket->reportBuffer, pHidPacket->reportBufferLen);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Input report read successfully, size=%lu", pHidPacket->reportBufferLen);

Exit:
	if (pMDL != NULL) {
		IoFreeMdl(pMDL);
	}
    if (request != NULL) {
        WdfObjectDelete(request);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit: %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterSendHidFeatureReport(
    _In_ WDFDEVICE Device,
    _In_ UCHAR ReportId,
    _In_ PUCHAR pReportData,
    _In_ ULONG ReportDataSize
    )
{
    NTSTATUS status = STATUS_SUCCESS;
    PDEVICE_CONTEXT deviceContext = NULL;
    WDFREQUEST request = NULL;
    WDFMEMORY memory = NULL;
    PHID_XFER_PACKET pHidPacket = NULL;
    ULONG totalBufferSize = 0;
    PUCHAR pAllocatedBuffer = NULL;
	WDF_OBJECT_ATTRIBUTES attributes;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry: ReportId=0x%02x, DataSize=%lu", ReportId, ReportDataSize);

    if (Device == NULL || pReportData == NULL) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Invalid parameter");
        return STATUS_INVALID_PARAMETER;
    }

    deviceContext = PtpFilterGetContext(Device);

    // Total buffer = ReportId (1 byte) + payload
    totalBufferSize = 1 + ReportDataSize;

    // Allocate request
    status = WdfRequestCreate(WDF_NO_OBJECT_ATTRIBUTES, deviceContext->HidIoTarget, &request);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestCreate failed: %!STATUS!", status);
        goto Exit;
    }

    // Allocate buffer for HID_XFER_PACKET + report data
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = request;
    status = WdfMemoryCreate(&attributes,
                             NonPagedPoolNx,
                             0,
                             sizeof(HID_XFER_PACKET) + totalBufferSize,
                             &memory,
                             (PVOID*)&pAllocatedBuffer);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfMemoryCreate failed: %!STATUS!", status);
        goto Exit;
    }

    // Layout: [HID_XFER_PACKET][ReportId][Payload...]
    pHidPacket = (PHID_XFER_PACKET)pAllocatedBuffer;
    PUCHAR pReportBuffer = pAllocatedBuffer + sizeof(HID_XFER_PACKET);

    // Build the report buffer
    pReportBuffer[0] = ReportId;
    if (ReportDataSize > 0) {
        RtlCopyMemory(&pReportBuffer[1], pReportData, ReportDataSize);
    }

    // Initialize HID_XFER_PACKET
    RtlZeroMemory(pHidPacket, sizeof(HID_XFER_PACKET));
    pHidPacket->reportId = ReportId;
    pHidPacket->reportBuffer = pReportBuffer;
    pHidPacket->reportBufferLen = totalBufferSize; // Include ReportId byte

    // Format request
    status = WdfIoTargetFormatRequestForInternalIoctl(
        deviceContext->HidIoTarget,
        request,
        IOCTL_HID_SET_FEATURE,
        memory,     // Input buffer (report)
        NULL,       // Output buffer
        NULL, NULL);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfIoTargetFormatRequestForInternalIoctl failed: %!STATUS!", status);
        goto Exit;
    }

    PIRP irp = WdfRequestWdmGetIrp(request);
    if (irp == NULL) {
        status = STATUS_UNSUCCESSFUL;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestWdmGetIrp failed");
        goto Exit;
    }
    irp->UserBuffer = pHidPacket;

    // Send synchronously
    WDF_REQUEST_SEND_OPTIONS sendOptions;
    WDF_REQUEST_SEND_OPTIONS_INIT(&sendOptions, WDF_REQUEST_SEND_OPTION_SYNCHRONOUS);

    if (!WdfRequestSend(request, deviceContext->HidIoTarget, &sendOptions)) {
        status = WdfRequestGetStatus(request);
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestSend failed: %!STATUS!", status);
        goto Exit;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Custom feature report sent successfully");

Exit:
    if (request != NULL) {
        WdfObjectDelete(request);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit: %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterSetHapticFeedback( // --> Based on: https://github.com/dos1/Linux-Magic-Trackpad-2-Driver/blob/master/linux/drivers/hid/hid-magicmouse.c
    _In_ WDFDEVICE Device,
    _In_ ULONG FeedbackClick,
    _In_ ULONG FeedbackRelease
)
{
    NTSTATUS status = STATUS_SUCCESS;
    BYTE mt2Click[] = { 0x22, 0x01, 0x00, 0x78, 0x02, 0x00, 0x24, 0x30, 0x06, 0x01, 0x00, 0x18, 0x48, 0x13 };
    BYTE mt2Release[] = { 0x23, 0x01, 0x00, 0x78, 0x02, 0x00, 0x24, 0x30, 0x06, 0x01, 0x00, 0x18, 0x48, 0x13 };

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");

    mt2Click[2] = (BYTE)(FeedbackClick >> 0);
    mt2Click[5] = (BYTE)(FeedbackClick >> 8);
    mt2Click[10] = (BYTE)(FeedbackClick >> 16);

    status = PtpFilterSendHidFeatureReport(Device, 0xF2, mt2Click, sizeof(mt2Click));
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterSendHidFeatureReport failed: %!STATUS!", status);
        goto Exit;
    }

    mt2Release[2] = (BYTE)(FeedbackRelease >> 0);
    mt2Release[5] = (BYTE)(FeedbackRelease >> 8);
    mt2Release[10] = (BYTE)(FeedbackRelease >> 16);

    status = PtpFilterSendHidFeatureReport(Device, 0xF2, mt2Release, sizeof(mt2Release));
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! PtpFilterSendHidFeatureReport failed: %!STATUS!", status);
        goto Exit;
    }

Exit:

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit: %!STATUS!", status);
    return status;
}

NTSTATUS
PtpFilterConfigureMultiTouch(
    _In_ WDFDEVICE Device
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PDEVICE_CONTEXT deviceContext;

    UCHAR hidPacketBuffer[HID_XFER_PACKET_SIZE];
    PHID_XFER_PACKET pHidPacket;
    WDFMEMORY hidMemory;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_REQUEST_SEND_OPTIONS configRequestSendOptions;
    WDFREQUEST configRequest;
    PIRP pConfigIrp = NULL;
	PDRIVER_CONTEXT driverContext;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");
    deviceContext = PtpFilterGetContext(Device); 
    
    // Check if this device is supported for configuration.
    // So far in this prototype, we support Magic Trackpad 2 in USB (05AC:0265) or Bluetooth mode (004c:0265)
    if (deviceContext->VendorID != HID_VID_APPLE_USB && deviceContext->VendorID != HID_VID_APPLE_BT) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Vendor not supported: 0x%x", deviceContext->VendorID);
        status = STATUS_NOT_SUPPORTED;
        goto exit;
    }
    if (deviceContext->ProductID != HID_PID_MAGIC_TRACKPAD_2 && deviceContext->ProductID != HID_PID_MAGIC_TRACKPAD_2_USBC) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Product not supported: 0x%x", deviceContext->ProductID);
        status = STATUS_NOT_SUPPORTED;
        goto exit;
    }

    if (deviceContext->VendorID == HID_VID_APPLE_BT) {
		driverContext = PtpFilterDriverGetContext(WdfDeviceGetDriver(Device));
        PtpFilterReadSettings(driverContext);
		PtpFilterSetHapticFeedback(Device, driverContext->FeedbackClick, driverContext->FeedbackRelease);
    }

    RtlZeroMemory(hidPacketBuffer, sizeof(hidPacketBuffer));
    pHidPacket = (PHID_XFER_PACKET) &hidPacketBuffer;

    if (deviceContext->VendorID == HID_VID_APPLE_USB) {
        deviceContext->X.snratio = 250;
        deviceContext->X.min = -3678;
        deviceContext->X.max = 3934;
        deviceContext->Y.snratio = 250;
        deviceContext->Y.min = -2479;
        deviceContext->Y.max = 2586;

        pHidPacket->reportId = 0x02;
        pHidPacket->reportBufferLen = 0x04;
        pHidPacket->reportBuffer = (PUCHAR)pHidPacket + sizeof(HID_XFER_PACKET);
        pHidPacket->reportBuffer[0] = 0x02;
        pHidPacket->reportBuffer[1] = 0x01;
        pHidPacket->reportBuffer[2] = 0x00;
        pHidPacket->reportBuffer[3] = 0x00;
    }
    else if (deviceContext->VendorID == HID_VID_APPLE_BT) {
        deviceContext->X.snratio = 250;
        deviceContext->X.min = -3678;
        deviceContext->X.max = 3934;
        deviceContext->Y.snratio = 250;
        deviceContext->Y.min = -2479;
        deviceContext->Y.max = 2586;

        pHidPacket->reportId = 0xF1;
        pHidPacket->reportBufferLen = 0x03;
        pHidPacket->reportBuffer = (PUCHAR)pHidPacket + sizeof(HID_XFER_PACKET);
        pHidPacket->reportBuffer[0] = 0xF1;
        pHidPacket->reportBuffer[1] = 0x02;
        pHidPacket->reportBuffer[2] = 0x01;
    }
    else {
        // Something we don't support yet.
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! Unrecognized device detected");
        status = STATUS_NOT_SUPPORTED;
        goto exit;
    }

    // Init a request entity.
    // Because we bypassed HIDCLASS driver, there's a few things that we need to manually take care of.
    status = WdfRequestCreate(WDF_NO_OBJECT_ATTRIBUTES, deviceContext->HidIoTarget, &configRequest);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestCreate failed, Status = %!STATUS!", status);
        goto exit;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = configRequest;
    status = WdfMemoryCreatePreallocated(&attributes, (PVOID) pHidPacket, HID_XFER_PACKET_SIZE, &hidMemory);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfMemoryCreatePreallocated failed, Status = %!STATUS!", status);
        goto cleanup;
    }

    status = WdfIoTargetFormatRequestForInternalIoctl(deviceContext->HidIoTarget,
        configRequest, IOCTL_HID_SET_FEATURE,
        hidMemory, NULL, NULL, NULL);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfIoTargetFormatRequestForInternalIoctl failed, Status = %!STATUS!", status);
        goto cleanup;
    }
    
    // Manually take care of IRP to meet requirements of mini drivers.
    pConfigIrp = WdfRequestWdmGetIrp(configRequest);
    if (pConfigIrp == NULL) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestWdmGetIrp failed");
        status = STATUS_UNSUCCESSFUL;
        goto cleanup;
    }

    // God-damn-it we have to configure it by ourselves :)
    pConfigIrp->UserBuffer = pHidPacket;

    WDF_REQUEST_SEND_OPTIONS_INIT(&configRequestSendOptions, WDF_REQUEST_SEND_OPTION_SYNCHRONOUS);
    if (WdfRequestSend(configRequest, deviceContext->HidIoTarget, &configRequestSendOptions) == FALSE) {
        status = WdfRequestGetStatus(configRequest);
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "%!FUNC! WdfRequestSend failed, Status = %!STATUS!", status);
        goto cleanup;
    } else {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Changed trackpad status to multitouch mode");
        status = STATUS_SUCCESS;
    }

cleanup:
    if (configRequest != NULL) {
        WdfObjectDelete(configRequest);
    }
exit:
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit, Status = %!STATUS!", status);
    return status;
}

VOID
PtpFilterRecoveryTimerCallback(
    WDFTIMER Timer
)
{
    WDFDEVICE device;
    PDEVICE_CONTEXT deviceContext;
    NTSTATUS status;

    device = WdfTimerGetParentObject(Timer);
    deviceContext = PtpFilterGetContext(device);

    // We will try to reinitialize the device
    status = PtpFilterSelfManagedIoRestart(device);
    if (NT_SUCCESS(status)) {
        // If succeeded, proceed to reissue the request.
        // Otherwise it will retry the process after a few seconds.
        PtpFilterInputIssueTransportRequest(device);
    }
}
