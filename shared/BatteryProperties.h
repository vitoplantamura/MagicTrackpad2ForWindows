// Battery properties shared by the Bluetooth and USB drivers.
#pragma once
#include <devpropdef.h>

#define AMT_BATTERY_POLL_INTERVAL_SEC 60
#define AMT_BATTERY_REQUEST_TIMEOUT_SEC 1

// Battery percentage property used by Windows Bluetooth device enumeration.
static const DEVPROPKEY AmtBatteryPercentKey = {
    {0x104ea319, 0x6ee2, 0x4701, {0xbd, 0x47, 0x8d, 0xdb, 0xf4, 0x25, 0xbb, 0xe5}}, 2
};

// System.Devices.BatteryLife (PKEY_Devices_BatteryLife), a BYTE percentage.
// https://learn.microsoft.com/windows/win32/properties/props-system-devices-batterylife
static const DEVPROPKEY AmtBatteryLifeKey = {
    {0x49cd1f76, 0x5626, 0x4b17, {0xa4, 0xe8, 0x18, 0xb4, 0xaa, 0x1a, 0x22, 0x13}}, 10
};

static inline BOOLEAN
AmtIsValidBatteryReport(_In_reads_bytes_(Length) const UCHAR* Buffer, _In_ SIZE_T Length)
{
    return Length >= 3 && Buffer[0] == 0x90 && Buffer[2] <= 100;
}

_IRQL_requires_(PASSIVE_LEVEL)
static inline NTSTATUS
AmtPublishBattery(_In_ WDFDEVICE Device, _In_ UCHAR Level)
{
    WDF_DEVICE_PROPERTY_DATA property;
    NTSTATUS percentStatus;
    NTSTATUS lifeStatus;

    if (Level > 100) {
        return STATUS_DEVICE_DATA_ERROR;
    }

    WDF_DEVICE_PROPERTY_DATA_INIT(&property, &AmtBatteryPercentKey);
    percentStatus = WdfDeviceAssignProperty(Device, &property, DEVPROP_TYPE_BYTE, sizeof(Level), &Level);
    WDF_DEVICE_PROPERTY_DATA_INIT(&property, &AmtBatteryLifeKey);
    lifeStatus = WdfDeviceAssignProperty(Device, &property, DEVPROP_TYPE_BYTE, sizeof(Level), &Level);

    // Attempt both properties, but let the caller retry if either write failed.
    return NT_SUCCESS(percentStatus) ? lifeStatus : percentStatus;
}
