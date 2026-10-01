// Input.h: Input processing and device definitions
#pragma once

VOID
PtpFilterInputProcessRequest(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);

VOID
PtpFilterWorkItemCallback(
	_In_ WDFWORKITEM WorkItem
);

// Issues one IOCTL_HID_READ_REPORT to the HID transport. Returns TRUE when the read
// was sent; on failure the recovery timer has been armed or the device failed.
_IRQL_requires_max_(DISPATCH_LEVEL)
BOOLEAN
PtpFilterInputIssueTransportRequest(
	_In_ WDFDEVICE Device
);

// Brings the number of in-flight transport reads up to the number of upstream
// read requests waiting in HidReadQueue. No-op while the device is not configured.
_IRQL_requires_max_(DISPATCH_LEVEL)
VOID
PtpFilterInputReplenishTransportRequests(
	_In_ WDFDEVICE Device
);

VOID
PtpFilterInputRequestCompletionCallback(
	_In_ WDFREQUEST Request,
	_In_ WDFIOTARGET Target,
	_In_ PWDF_REQUEST_COMPLETION_PARAMS Params,
	_In_ WDFCONTEXT Context
);
