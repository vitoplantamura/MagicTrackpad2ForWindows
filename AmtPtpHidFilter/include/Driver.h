// Driver.h: common definition
#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <usb.h>
#include <usbdlib.h>
#include <wdfusb.h>
#include <initguid.h>
#include <hidport.h>

#include "Trace.h"

EXTERN_C_START

// Driver context
typedef struct _DRIVER_CONTEXT {
    WDFDEVICE ControlDevice;
	WDFDEVICE CDFirstDevice;

	// \\.\AmtPtpHapticDeviceUm. Unlike the control device it is not deleted with the trackpad
	// while an app holds it open: the app's handle keeps working across a reconnect, and the
	// name stays free for the next trackpad. HapticLock guards the fields below.
	WDFWAITLOCK HapticLock;
	WDFDEVICE HapticDevice;
	LONG HapticHandles;           // open file objects on HapticDevice
	LONG Trackpads;               // trackpad devices that exist (created, not yet cleaned up)
	WDFDEVICE HapticTrackpad;     // started trackpad that IOCTL_PTPFILTER_ACTUATE sends to
	ULONGLONG LastActuateTime;    // KeQueryInterruptTime() of the last actuation
	
	BOOLEAN ButtonDisabled;
	ULONG StopPressure;
	ULONG StopSize;
	BOOLEAN IgnoreButtonFinger;
	BOOLEAN IgnoreNearFingers;
	BOOLEAN PalmRejection;
	ULONG FeedbackClick;
	ULONG FeedbackRelease;
} DRIVER_CONTEXT, *PDRIVER_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DRIVER_CONTEXT, PtpFilterDriverGetContext)

VOID
PtpFilterReadSettings(
	_Inout_ PDRIVER_CONTEXT DriverContext
);

// Common entry points
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD PtpFilterEvtDeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP PtpFilterEvtDriverContextCleanup;

// Device specific routines
#include "Device.h"
#include "Queue.h"
#include "Hac.h"
#include "Diagnostics.h"
#include "Metadata/StaticHidRegistry.h"
#include "HidMiniport.h"
#include "HidDevice.h"
#include "Input.h"
#include "ControlDevice.h"

// Pool Tag
#define PTP_LIST_POOL_TAG 'LTPA'
#define REPORT_BUFFER_SIZE   1024

EXTERN_C_END
