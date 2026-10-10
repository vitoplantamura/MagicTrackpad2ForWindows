// Hidminiport.h: HID miniport communication structures
#pragma once

#include <pshpack1.h>

// PTP device capabilites Feature Report
typedef struct _PTP_DEVICE_CAPS_FEATURE_REPORT {
	UCHAR ReportID;
	UCHAR MaximumContactPoints;
	UCHAR ButtonType;
} PTP_DEVICE_CAPS_FEATURE_REPORT, * PPTP_DEVICE_CAPS_FEATURE_REPORT;

// PTP device certification Feature Report
typedef struct _PTP_DEVICE_HQA_CERTIFICATION_REPORT {
	UCHAR ReportID;
	UCHAR CertificationBlob[256];
} PTP_DEVICE_HQA_CERTIFICATION_REPORT, * PPTP_DEVICE_HQA_CERTIFICATION_REPORT;

// PTP input mode Feature Report
typedef struct _PTP_DEVICE_INPUT_MODE_REPORT {
	UCHAR ReportID;
	UCHAR Mode;
} PTP_DEVICE_INPUT_MODE_REPORT, * PPTP_DEVICE_INPUT_MODE_REPORT;

// PTP input selection Feature Report
typedef struct _PTP_DEVICE_SELECTIVE_REPORT_MODE_REPORT {
	UCHAR ReportID;
	UCHAR ButtonReport : 1;
	UCHAR SurfaceReport : 1;
	UCHAR Padding : 6;
} PTP_DEVICE_SELECTIVE_REPORT_MODE_REPORT, * PPTP_DEVICE_SELECTIVE_REPORT_MODE_REPORT;

// PTP single finger
typedef struct _PTP_CONTACT {
	UCHAR		Confidence : 1;
	UCHAR		TipSwitch : 1;
	UCHAR		Padding : 6;
	ULONG		ContactID;
	USHORT		X;
	USHORT		Y;
	// Optional Precision Touchpad usages (Width 0x48, Height 0x49 in X/Y units; Pressure 0x30,
	// 16-bit, same range as Mechanical Force) then vendor-defined extras (page 0xFF00: 0x02 Size, 0x03/0x04 raw TouchMajor/Minor, 0x05 Orientation,
	// 0x06 Finger type, 0x07 State). Layout must match AAPL_MAGIC_TRACKPAD2_PTP_FINGER_EXTRAS.
	USHORT		Width;
	USHORT		Height;
	USHORT		Pressure;
	UCHAR		Size;
	UCHAR		TouchMajor;
	UCHAR		TouchMinor;
	UCHAR		Orientation;
	UCHAR		Finger;
	UCHAR		State;
} PTP_CONTACT, * PPTP_CONTACT;

// PTP single scan frame Input Report
typedef struct _PTP_REPORT {
	UCHAR       ReportID;
	PTP_CONTACT Contacts[5];
	USHORT      ScanTime;
	UCHAR       ContactCount;
	USHORT      MechanicalForce; // Sensors page 0x20 / 0x494: sum of all contact pressures
	UCHAR       IsButtonClicked;
} PTP_REPORT, * PPTP_REPORT;

C_ASSERT(sizeof(PTP_CONTACT) == 21);
C_ASSERT(sizeof(PTP_REPORT) == 1 + 5 * 21 + 2 + 1 + 2 + 1);

#include <poppack.h>

// HID routines
NTSTATUS
PtpFilterGetHidDescriptor(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);

NTSTATUS
PtpFilterGetDeviceAttribs(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);

NTSTATUS
PtpFilterGetReportDescriptor(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);

NTSTATUS
PtpFilterGetStrings(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request,
	_Out_ BOOLEAN* Pending
);

NTSTATUS
PtpFilterGetHidFeatures(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);

NTSTATUS
PtpFilterSetHidFeatures(
	_In_ WDFDEVICE Device,
	_In_ WDFREQUEST Request
);
