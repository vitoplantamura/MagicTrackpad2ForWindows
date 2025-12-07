// Driver.c: Common entry point and WPP trace filter handler

#include <Driver.h>
#include "Driver.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text (INIT, DriverEntry)
#pragma alloc_text (PAGE, PtpFilterEvtDeviceAdd)
#pragma alloc_text (PAGE, PtpFilterEvtDriverContextCleanup)
#endif

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    NTSTATUS status;
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;
	WDFDRIVER driver;

    PAGED_CODE();

    // Initialize WPP
    WPP_INIT_TRACING(DriverObject, RegistryPath);

    // Register a cleanup callback
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DRIVER_CONTEXT);
    attributes.EvtCleanupCallback = PtpFilterEvtDriverContextCleanup;

    // Register WDF driver
    WDF_DRIVER_CONFIG_INIT(&config, PtpFilterEvtDeviceAdd);
    status = WdfDriverCreate(DriverObject, RegistryPath, &attributes, &config, &driver);

    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "WdfDriverCreate failed %!STATUS!", status);
        WPP_CLEANUP(DriverObject);
        return status;
    }
	
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Driver Initialized");
    return STATUS_SUCCESS;
}

NTSTATUS
PtpFilterEvtDeviceAdd(
    _In_    WDFDRIVER       Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
)
{
    NTSTATUS status;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

    // We do not own power control.
    // In addition we do not own every I/O request.
    WdfFdoInitSetFilter(DeviceInit);

    // Create the device.
    status = PtpFilterCreateDevice(DeviceInit, Driver);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");
    return status;
}

VOID
PtpFilterEvtDriverContextCleanup(
    _In_ WDFOBJECT DriverObject
)
{
    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Cleanup");

    //
    // Stop WPP Tracing
    //
    WPP_CLEANUP(WdfDriverWdmGetDriverObject((WDFDRIVER)DriverObject));
}

ULONG
PtpFilterReadSettingValue(
    _In_ PWCHAR SettingName,
    _In_ ULONG DefaultValue
)
{
    WDFKEY key = NULL;
    NTSTATUS status;
    ULONG value = DefaultValue;
    UNICODE_STRING valueName;

    PAGED_CODE();
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry (%ws)", SettingName);

    // Open the Parameters Registry Key
    status = WdfDriverOpenParametersRegistryKey(
        WdfGetDriver(),
        KEY_READ,
        WDF_NO_OBJECT_ATTRIBUTES,
        &key
    );

    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "%!FUNC! WdfDriverOpenParametersRegistryKey failed with %!STATUS!", status);
        goto exit;
    }

    RtlInitUnicodeString(&valueName, SettingName);

    status = WdfRegistryQueryULong(key, &valueName, &value);
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "%!FUNC! WdfRegistryQueryULong failed with %!STATUS! - Using default", status);
        value = DefaultValue;
    } else {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Read value of %ws is %u", SettingName, value);
    }

exit:
    if (key != NULL) {
        WdfRegistryClose(key);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");
    return value;
}

VOID
PtpFilterReadSettings(
	_Inout_ PDRIVER_CONTEXT DriverContext
)
{
	DriverContext->ButtonDisabled = PtpFilterReadSettingValue(L"ButtonDisabled", 0) ? TRUE : FALSE;
	DriverContext->StopPressure = PtpFilterReadSettingValue(L"StopPressure", 0);
	DriverContext->StopSize = PtpFilterReadSettingValue(L"StopSize", 0xffffffff);
	DriverContext->IgnoreButtonFinger = PtpFilterReadSettingValue(L"IgnoreButtonFinger", 1) ? TRUE : FALSE;
	DriverContext->IgnoreNearFingers = PtpFilterReadSettingValue(L"IgnoreNearFingers", 1) ? TRUE : FALSE;
	DriverContext->PalmRejection = PtpFilterReadSettingValue(L"PalmRejection", 0) ? TRUE : FALSE;
	DriverContext->FeedbackClick = PtpFilterReadSettingValue(L"FeedbackClick", 0x08081E);
	DriverContext->FeedbackRelease = PtpFilterReadSettingValue(L"FeedbackRelease", 0x020218);
}
