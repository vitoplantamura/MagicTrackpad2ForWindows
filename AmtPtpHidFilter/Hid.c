// Hid.c: HID specific routines

#include <Driver.h>
#include "Hid.tmh"

// DEFAULT_PTP_HQA_BLOB is a comma-separated list of byte literals. Materialize it once
// so the whole blob can be copied into the feature report. Assigning the macro to the
// first array element only stores the first byte (comma operator) and leaves the rest
// of the 256-byte report untouched.
static const UCHAR PtpHqaCertificationBlob[] = { DEFAULT_PTP_HQA_BLOB };
C_ASSERT(sizeof(PtpHqaCertificationBlob) == RTL_FIELD_SIZE(PTP_DEVICE_HQA_CERTIFICATION_REPORT, CertificationBlob));

NTSTATUS
PtpFilterGetHidDescriptor(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
)
{

	NTSTATUS        status = STATUS_SUCCESS;
	PDEVICE_CONTEXT deviceContext;
	size_t			hidDescriptorSize = 0;
	WDFMEMORY       requestMemory;
	PHID_DESCRIPTOR pSelectedHidDescriptor = NULL;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	status = WdfRequestRetrieveOutputMemory(Request, &requestMemory);
	if (!NT_SUCCESS(status)) {
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfRequestRetrieveOutputBuffer failed with %!STATUS!", status);
		goto exit;
	}

	switch (deviceContext->ProductID) {
		case HID_PID_MAGIC_TRACKPAD_2:
		case HID_PID_MAGIC_TRACKPAD_2_USBC:
		{
			TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Request HID Report Descriptor for Apple Magic Trackpad 2 Family");
			hidDescriptorSize = PtpDefaultHidDescriptorMagicTrackpad2.bLength;
			pSelectedHidDescriptor = &PtpDefaultHidDescriptorMagicTrackpad2;
			break;
		}
	}

	if (pSelectedHidDescriptor != NULL && hidDescriptorSize > 0) {
		status = WdfMemoryCopyFromBuffer(requestMemory, 0, (PVOID)pSelectedHidDescriptor, hidDescriptorSize);
		if (!NT_SUCCESS(status)) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfMemoryCopyFromBuffer failed with %!STATUS!", status);
			goto exit;
		}

		WdfRequestSetInformation(Request, hidDescriptorSize);
	}
	else {
		TraceEvents(TRACE_LEVEL_WARNING, TRACE_HID, "%!FUNC! Device HID registry is not found");
		status = STATUS_NOT_SUPPORTED;
		goto exit;
	}

exit:
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

NTSTATUS
PtpFilterGetDeviceAttribs(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
)
{

	NTSTATUS               status = STATUS_SUCCESS;
	PDEVICE_CONTEXT        deviceContext;
	PHID_DEVICE_ATTRIBUTES pDeviceAttributes = NULL;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	status = WdfRequestRetrieveOutputBuffer(Request, sizeof(HID_DEVICE_ATTRIBUTES), &pDeviceAttributes, NULL);
	if (!NT_SUCCESS(status)) {
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfRequestRetrieveOutputBuffer failed with %!STATUS!", status);
		goto exit;
	}

	pDeviceAttributes->Size = sizeof(HID_DEVICE_ATTRIBUTES);
	// Okay here's one thing: we cannot report the real ID here, otherwise there's will be some great conflict with the USB/BT driver.
	// Therefore Vendor ID is changed to a hardcoded number
	pDeviceAttributes->ProductID = deviceContext->ProductID;
	pDeviceAttributes->VendorID = DEVICE_VID;
	pDeviceAttributes->VersionNumber = DEVICE_VERSION;
	WdfRequestSetInformation(Request, sizeof(HID_DEVICE_ATTRIBUTES));

exit:
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

NTSTATUS
PtpFilterGetReportDescriptor(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
)
{

	NTSTATUS               status = STATUS_SUCCESS;
	PDEVICE_CONTEXT        deviceContext;
	size_t			       hidDescriptorSize = 0;
	WDFMEMORY              requestMemory;
	PHID_REPORT_DESCRIPTOR selectedHidDescriptor = NULL;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	status = WdfRequestRetrieveOutputMemory(Request, &requestMemory);
	if (!NT_SUCCESS(status)) {
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfRequestRetrieveOutputBuffer failed with %!STATUS!", status);
		goto exit;
	}

	switch (deviceContext->ProductID) {
		case HID_PID_MAGIC_TRACKPAD_2:
		case HID_PID_MAGIC_TRACKPAD_2_USBC:
		{
			hidDescriptorSize = PtpDefaultHidDescriptorMagicTrackpad2.DescriptorList[0].wReportLength;
			selectedHidDescriptor = PtpReportDescriptorMagicTrackpad2;
			break;
		}
	}

	if (selectedHidDescriptor != NULL && hidDescriptorSize > 0) {
		status = WdfMemoryCopyFromBuffer(requestMemory, 0, (PVOID)selectedHidDescriptor, hidDescriptorSize);
		if (!NT_SUCCESS(status)) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfMemoryCopyFromBuffer failed with %!STATUS!", status);
			goto exit;
		}

		WdfRequestSetInformation(Request, hidDescriptorSize);
	}
	else {
		TraceEvents(TRACE_LEVEL_WARNING, TRACE_HID, "%!FUNC! Device HID registry is not found");
		status = STATUS_NOT_SUPPORTED;
		goto exit;
	}

exit:
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

NTSTATUS
PtpFilterGetStrings(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request,
	_Out_ BOOLEAN* Pending
)
{
	NTSTATUS status = STATUS_SUCCESS;
	PDEVICE_CONTEXT deviceContext;
	BOOLEAN requestSent;
	WDF_REQUEST_SEND_OPTIONS sendOptions;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	// Forward the IRP to our upstream IO target
	// We don't really care about the content
	WdfRequestFormatRequestUsingCurrentType(Request);
	WDF_REQUEST_SEND_OPTIONS_INIT(&sendOptions, WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET);

	// This IOCTL is METHOD_NEITHER, so we just send it without IRP modification
	requestSent = WdfRequestSend(Request, deviceContext->HidIoTarget, &sendOptions);
	*Pending = TRUE;

	if (!requestSent)
	{
		status = WdfRequestGetStatus(Request);
		*Pending = FALSE;
	}

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

NTSTATUS
PtpFilterGetHidFeatures(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
)
{
	NTSTATUS status = STATUS_SUCCESS;
	PDEVICE_CONTEXT deviceContext;
	
	WDF_REQUEST_PARAMETERS requestParameters;
	size_t reportSize;
	PHID_XFER_PACKET hidContent;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	WDF_REQUEST_PARAMETERS_INIT(&requestParameters);
	WdfRequestGetParameters(Request, &requestParameters);
	if (requestParameters.Parameters.DeviceIoControl.OutputBufferLength < sizeof(HID_XFER_PACKET))
	{
		status = STATUS_BUFFER_TOO_SMALL;
		goto exit;
	}

	hidContent = (PHID_XFER_PACKET)WdfRequestWdmGetIrp(Request)->UserBuffer;
	if (hidContent == NULL)
	{
		status = STATUS_INVALID_DEVICE_REQUEST;
		goto exit;
	}

	switch (hidContent->reportId)
	{
	case REPORTID_DEVICE_CAPS:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_DEVICE_CAPS is requested");

		// Size sanity check
		reportSize = sizeof(PTP_DEVICE_CAPS_FEATURE_REPORT);
		if (hidContent->reportBufferLen < reportSize) {
			status = STATUS_INVALID_BUFFER_SIZE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Report buffer is too small");
			goto exit;
		}

		PPTP_DEVICE_CAPS_FEATURE_REPORT capsReport = (PPTP_DEVICE_CAPS_FEATURE_REPORT)hidContent->reportBuffer;
		capsReport->MaximumContactPoints = PTP_MAX_CONTACT_POINTS;
		capsReport->ButtonType = PTP_BUTTON_TYPE_CLICK_PAD;
		capsReport->ReportID = REPORTID_DEVICE_CAPS;
		WdfRequestSetInformation(Request, reportSize);

		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_DEVICE_CAPS has maximum contact points of %d", capsReport->MaximumContactPoints);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_DEVICE_CAPS has touchpad type %d", capsReport->ButtonType);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_DEVICE_CAPS is fulfilled");
		break;
	}
	case REPORTID_PTPHQA:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_PTPHQA is requested");

		// Size sanity check
		reportSize = sizeof(PTP_DEVICE_HQA_CERTIFICATION_REPORT);
		if (hidContent->reportBufferLen < reportSize)
		{
			status = STATUS_INVALID_BUFFER_SIZE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Report buffer is too small.");
			goto exit;
		}

		PPTP_DEVICE_HQA_CERTIFICATION_REPORT certReport = (PPTP_DEVICE_HQA_CERTIFICATION_REPORT)hidContent->reportBuffer;
		RtlCopyMemory(certReport->CertificationBlob, PtpHqaCertificationBlob, sizeof(certReport->CertificationBlob));
		certReport->ReportID = REPORTID_PTPHQA;
		WdfRequestSetInformation(Request, reportSize);

		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_PTPHQA is fulfilled");
		break;
	}
	case REPORTID_HAPTIC_BUTTON_THRESHOLD:
	{
		reportSize = sizeof(MT2_HAPTIC_BUTTON_THRESHOLD_REPORT);
		if (hidContent->reportBufferLen < reportSize) {
			status = STATUS_INVALID_BUFFER_SIZE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Report buffer is too small");
			goto exit;
		}

		PMT2_HAPTIC_BUTTON_THRESHOLD_REPORT thresholdReport = (PMT2_HAPTIC_BUTTON_THRESHOLD_REPORT)hidContent->reportBuffer;
		thresholdReport->ReportID = REPORTID_HAPTIC_BUTTON_THRESHOLD;
		thresholdReport->Threshold = Mt2HapticStateThreshold(InterlockedCompareExchange(&deviceContext->WindowsHapticState, 0, 0));
		WdfRequestSetInformation(Request, reportSize);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_HAPTIC_BUTTON_THRESHOLD: %d", thresholdReport->Threshold);
		break;
	}
	case REPORTID_HAPTIC_INTENSITY:
	{
		reportSize = sizeof(MT2_HAPTIC_INTENSITY_REPORT);
		if (hidContent->reportBufferLen < reportSize) {
			status = STATUS_INVALID_BUFFER_SIZE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Report buffer is too small");
			goto exit;
		}

		PMT2_HAPTIC_INTENSITY_REPORT intensityReport = (PMT2_HAPTIC_INTENSITY_REPORT)hidContent->reportBuffer;
		intensityReport->ReportID = REPORTID_HAPTIC_INTENSITY;
		intensityReport->Intensity = Mt2HapticStateIntensity(InterlockedCompareExchange(&deviceContext->WindowsHapticState, 0, 0));
		WdfRequestSetInformation(Request, reportSize);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_HAPTIC_INTENSITY: %d", intensityReport->Intensity);
		break;
	}
	case REPORTID_HAPTIC_WAVEFORMS:
	{
		reportSize = sizeof(MT2_HAPTIC_WAVEFORM_LIST_REPORT);
		if (hidContent->reportBufferLen < reportSize) {
			status = STATUS_INVALID_BUFFER_SIZE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Report buffer is too small");
			goto exit;
		}

		Mt2HapticFillWaveformListReport((PMT2_HAPTIC_WAVEFORM_LIST_REPORT)hidContent->reportBuffer);
		WdfRequestSetInformation(Request, reportSize);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_HAPTIC_WAVEFORMS is fulfilled");
		break;
	}
	default:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Unsupported type %d is requested", hidContent->reportId);
		status = STATUS_NOT_SUPPORTED;
		goto exit;
	}
	}

exit:
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

NTSTATUS
PtpFilterSetHidFeatures(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
)
{
	NTSTATUS status = STATUS_SUCCESS;
	PDEVICE_CONTEXT deviceContext;

	PHID_XFER_PACKET hidPacket;
	WDF_REQUEST_PARAMETERS requestParameters;

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Entry");
	deviceContext = PtpFilterGetContext(Device);

	WDF_REQUEST_PARAMETERS_INIT(&requestParameters);
	WdfRequestGetParameters(Request, &requestParameters);
	if (requestParameters.Parameters.DeviceIoControl.InputBufferLength < sizeof(HID_XFER_PACKET))
	{
		status = STATUS_BUFFER_TOO_SMALL;
		goto exit;
	}

	hidPacket = (PHID_XFER_PACKET)WdfRequestWdmGetIrp(Request)->UserBuffer;
	if (hidPacket == NULL)
	{
		status = STATUS_INVALID_DEVICE_REQUEST;
		goto exit;
	}

	switch (hidPacket->reportId)
	{
	case REPORTID_REPORTMODE:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_REPORTMODE is requested");

		PPTP_DEVICE_INPUT_MODE_REPORT DeviceInputMode = (PPTP_DEVICE_INPUT_MODE_REPORT)hidPacket->reportBuffer;
		switch (DeviceInputMode->Mode)
		{
		case PTP_COLLECTION_MOUSE:
		{
			TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_REPORTMODE requested Mouse Input");
			deviceContext->PtpInputOn = FALSE;
			break;
		}
		case PTP_COLLECTION_WINDOWS:
		{

			TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_REPORTMODE requested Windows PTP Input");
			deviceContext->PtpInputOn = TRUE;
			break;
		}
		}

		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_REPORTMODE is fulfilled");
		break;
	}
	case REPORTID_FUNCSWITCH:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_FUNCSWITCH is requested");

		PPTP_DEVICE_SELECTIVE_REPORT_MODE_REPORT InputSelection = (PPTP_DEVICE_SELECTIVE_REPORT_MODE_REPORT)hidPacket->reportBuffer;
		deviceContext->PtpReportButton = InputSelection->ButtonReport;
		deviceContext->PtpReportTouch = InputSelection->SurfaceReport;

		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_FUNCSWITCH requested Button = %d, Surface = %d",
			InputSelection->ButtonReport, InputSelection->SurfaceReport);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_FUNCSWITCH is fulfilled");
		break;
	}
	case REPORTID_HAPTIC_BUTTON_THRESHOLD:
	{
		if (hidPacket->reportBufferLen < sizeof(MT2_HAPTIC_BUTTON_THRESHOLD_REPORT)) {
			status = STATUS_INVALID_BUFFER_SIZE;
			goto exit;
		}

		UCHAR threshold = ((PMT2_HAPTIC_BUTTON_THRESHOLD_REPORT)hidPacket->reportBuffer)->Threshold;
		if (threshold < MT2_HAPTIC_THRESHOLD_MIN || threshold > MT2_HAPTIC_THRESHOLD_MAX) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Button press threshold %d out of range", threshold);
			status = STATUS_INVALID_PARAMETER;
			goto exit;
		}

		// Pushed to the pad by a work item: the send can take seconds on a link that is coming up.
		Mt2HapticUpdateState(&deviceContext->WindowsHapticState, threshold, MT2_HAPTIC_UNCHANGED);
		WdfWorkItemEnqueue(deviceContext->HapticApplyWorkItem);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_HAPTIC_BUTTON_THRESHOLD set to %d", threshold);
		break;
	}
	case REPORTID_HAPTIC_INTENSITY:
	{
		if (hidPacket->reportBufferLen < sizeof(MT2_HAPTIC_INTENSITY_REPORT)) {
			status = STATUS_INVALID_BUFFER_SIZE;
			goto exit;
		}

		UCHAR intensity = ((PMT2_HAPTIC_INTENSITY_REPORT)hidPacket->reportBuffer)->Intensity;
		if (intensity > MT2_HAPTIC_INTENSITY_MAX) {
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Haptic intensity %d out of range", intensity);
			status = STATUS_INVALID_PARAMETER;
			goto exit;
		}

		Mt2HapticUpdateState(&deviceContext->WindowsHapticState, MT2_HAPTIC_UNCHANGED, intensity);
		WdfWorkItemEnqueue(deviceContext->HapticApplyWorkItem);
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Report REPORTID_HAPTIC_INTENSITY set to %d", intensity);
		break;
	}
	default:
	{
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Unsupported type %d is requested", hidPacket->reportId);
		status = STATUS_NOT_SUPPORTED;
		goto exit;
	}
	}

exit:
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Exit");
	return status;
}

// Haptic Manual Trigger output report (host-initiated feedback, "Haptic signals" in Windows Settings).
// Validated here, on the parallel default queue, then forwarded to the sequential HapticQueue.
NTSTATUS
PtpFilterHapticOutputReport(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request,
	_Out_ BOOLEAN* Pending
)
{
	NTSTATUS status;
	PDEVICE_CONTEXT deviceContext;
	PHID_XFER_PACKET hidPacket;
	PMT2_HAPTIC_MANUAL_TRIGGER_REPORT report;

	*Pending = FALSE;
	deviceContext = PtpFilterGetContext(Device);

	hidPacket = (PHID_XFER_PACKET)WdfRequestWdmGetIrp(Request)->UserBuffer;
	if (hidPacket == NULL || hidPacket->reportBuffer == NULL) {
		return STATUS_INVALID_DEVICE_REQUEST;
	}

	if (hidPacket->reportId != REPORTID_HAPTIC_MANUAL_TRIGGER ||
		hidPacket->reportBufferLen < sizeof(MT2_HAPTIC_MANUAL_TRIGGER_REPORT)) {
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! Unexpected output report 0x%02X, %lu bytes",
			hidPacket->reportId, hidPacket->reportBufferLen);
		return STATUS_INVALID_PARAMETER;
	}

	report = (PMT2_HAPTIC_MANUAL_TRIGGER_REPORT)hidPacket->reportBuffer;
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Manual trigger: ordinal %d, intensity %d",
		report->ManualTrigger, report->Intensity);

	// None: nothing to play, and it must not affect what is playing
	if (report->ManualTrigger == MT2_HAPTIC_ORDINAL_NONE) {
		WdfRequestSetInformation(Request, hidPacket->reportBufferLen);
		return STATUS_SUCCESS;
	}

	status = WdfRequestForwardToIoQueue(Request, deviceContext->HapticQueue);
	if (NT_SUCCESS(status)) {
		*Pending = TRUE;
	}
	else {
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HID, "%!FUNC! WdfRequestForwardToIoQueue failed: %!STATUS!", status);
	}

	return status;
}

// Fires queued Manual Triggers one at a time. A trigger with a newer one waiting behind it is
// dropped (latest wins): a send takes about 70 ms over Bluetooth, so a burst of triggers, or a
// Stop, must not build up a backlog.
VOID
PtpFilterHapticEvtIoIntDeviceControl(
	_In_ WDFQUEUE Queue,
	_In_ WDFREQUEST Request,
	_In_ size_t OutputBufferLength,
	_In_ size_t InputBufferLength,
	_In_ ULONG IoControlCode
)
{
	NTSTATUS status = STATUS_SUCCESS;
	WDFDEVICE device;
	PDEVICE_CONTEXT deviceContext;
	PHID_XFER_PACKET hidPacket;
	PMT2_HAPTIC_MANUAL_TRIGGER_REPORT report;
	ULONG waiting = 0;
	ULONG feedback[MT2_HAPTIC_MAX_PULSES];
	ULONG pulses;
	ULONG i;

	UNREFERENCED_PARAMETER(OutputBufferLength);
	UNREFERENCED_PARAMETER(InputBufferLength);
	UNREFERENCED_PARAMETER(IoControlCode);

	device = WdfIoQueueGetDevice(Queue);
	deviceContext = PtpFilterGetContext(device);

	// Already validated by PtpFilterHapticOutputReport
	hidPacket = (PHID_XFER_PACKET)WdfRequestWdmGetIrp(Request)->UserBuffer;
	report = (PMT2_HAPTIC_MANUAL_TRIGGER_REPORT)hidPacket->reportBuffer;

	WdfIoQueueGetState(Queue, &waiting, NULL);
	if (waiting != 0) {
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Ordinal %d superseded by %lu newer trigger(s)",
			report->ManualTrigger, waiting);
	}
	else if (InterlockedCompareExchange(&deviceContext->HapticsReady, 0, 0) == 0) {
		// Not configured yet: a send could stall on a link that is still coming up (#68)
		TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Pad not configured yet, ordinal %d dropped",
			report->ManualTrigger);
	}
	else {
		// Chained pulses: each send takes about 70 ms over Bluetooth, which is already longer than
		// MT2_HAPTIC_PULSE_GAP_MS, and PtpFilterActuateTrigger keeps the 20 ms minimum gap.
		pulses = Mt2HapticPulsesForTrigger(report->ManualTrigger, report->Intensity, feedback);
		for (i = 0; i < pulses && NT_SUCCESS(status); i++) {
			if (i != 0) {
				// A newer trigger cuts the rest of this chain short
				WdfIoQueueGetState(Queue, &waiting, NULL);
				if (waiting != 0) {
					TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HID, "%!FUNC! Ordinal %d cut short after %lu pulse(s)",
						report->ManualTrigger, i);
					break;
				}
			}

			status = PtpFilterActuateTrigger(device, feedback[i]);
			TraceEvents(NT_SUCCESS(status) ? TRACE_LEVEL_INFORMATION : TRACE_LEVEL_ERROR, TRACE_HID,
				"%!FUNC! Ordinal %d, intensity %d, pulse %lu/%lu -> 0x%06lX: %!STATUS!",
				report->ManualTrigger, report->Intensity, i + 1, pulses, feedback[i], status);
		}
	}

	WdfRequestCompleteWithInformation(Request, status, NT_SUCCESS(status) ? hidPacket->reportBufferLen : 0);
}
