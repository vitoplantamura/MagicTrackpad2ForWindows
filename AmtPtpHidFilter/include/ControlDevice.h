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

EXTERN_C_END
