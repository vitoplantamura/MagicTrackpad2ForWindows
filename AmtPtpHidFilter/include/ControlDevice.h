// ControlDevice.h: control device related definitions

#pragma once

EXTERN_C_START

NTSTATUS
PtpFilterCreateControlDevice(
    _In_ WDFDRIVER Driver
);

VOID
PtpFilterDeleteControlDevice(
    _In_ WDFOBJECT Context
);

//
// IOCTLs
//

#define PTPFILTER_CTL_CODE(x) CTL_CODE(FILE_DEVICE_UNKNOWN, x, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_PTPFILTER_RELOAD_SETTINGS  PTPFILTER_CTL_CODE(0x800)
#define IOCTL_PTPFILTER_GET_BATTERY      PTPFILTER_CTL_CODE(0x801)

// IOCTL_PTPFILTER_ACTUATE: fires the haptic actuator once (Bluetooth MT2 only).
// Input (optional): ULONG in the encoding of the FeedbackClick setting, e.g. 0x040415 (light),
// 0x060617 (medium), 0x08081E (firm). Without input, the current FeedbackClick setting is used.
// Accepted by \\.\AmtPtpControlDeviceUm (administrators) and by \\.\AmtPtpHapticDeviceUm,
// which interactive users can open and which accepts no other IOCTL. FILE_WRITE_ACCESS makes the
// I/O manager require a handle opened for writing, so the device DACL is what grants the call.
#define IOCTL_PTPFILTER_ACTUATE          CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_WRITE_ACCESS)

EXTERN_C_END
