// Periodic battery telemetry. The touch read queue is independent of this target.
#include <Driver.h>

#include "Battery.tmh"

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

static EVT_WDF_TIMER BatteryTick;
static EVT_WDF_REQUEST_COMPLETION_ROUTINE BatteryComplete;
static IO_WORKITEM_ROUTINE BatteryPublish;

static VOID BatteryDone(PDEVICE_CONTEXT context)
{
    WDFDEVICE device = context->Device;
    InterlockedExchange(&context->Battery.Busy, 0);
    WdfObjectDereference(device);
}

static VOID BatteryPublish(PDEVICE_OBJECT associatedDevice, PVOID argument)
{
    PDEVICE_CONTEXT context = (PDEVICE_CONTEXT)argument;
    BATTERY_CONTEXT* battery = &context->Battery;
    UCHAR level = battery->Report[2];
    NTSTATUS first, second;
    UNREFERENCED_PARAMETER(associatedDevice);

    // This native work item holds its own PDO and device-context references.
    // PnP callbacks never wait for a property write on the PnP manager.
    if (battery->Epoch == battery->SampleEpoch) {
        first = IoSetDevicePropertyData(battery->Pdo, &AmtBatteryPercentKey, LOCALE_NEUTRAL,
            0, DEVPROP_TYPE_BYTE, sizeof(level), &level);
        second = IoSetDevicePropertyData(battery->Pdo, &AmtBatteryLifeKey, LOCALE_NEUTRAL,
            0, DEVPROP_TYPE_BYTE, sizeof(level), &level);
        if (NT_SUCCESS(first) && NT_SUCCESS(second)) battery->Published = battery->Cached;
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "Battery %u, properties %!STATUS! / %!STATUS!",
            (ULONG)level, first, second);
    }
    ObDereferenceObject(battery->Pdo);
    IoFreeWorkItem(battery->Work);
    BatteryDone(context);
}

static VOID BatteryComplete(WDFREQUEST request, WDFIOTARGET target,
    PWDF_REQUEST_COMPLETION_PARAMS params, WDFCONTEXT argument)
{
    PDEVICE_CONTEXT context = (PDEVICE_CONTEXT)argument;
    BATTERY_CONTEXT* battery = &context->Battery;
    NTSTATUS status = params->IoStatus.Status;
    SIZE_T length = params->IoStatus.Information;
    LONG64 sample;
    UNREFERENCED_PARAMETER(target);
    WdfObjectDelete(request);
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "Battery read %!STATUS!, bytes=%Iu", status, length);
    if (NT_SUCCESS(status) && battery->SampleEpoch == battery->Epoch &&
        length <= sizeof(battery->Report) && AmtIsValidBatteryReport(battery->Report, length)) {
        sample = ((LONG64)(ULONG)battery->SampleEpoch << 8) | battery->Report[2];
        InterlockedExchange64(&battery->Cached, sample);
        if (sample != battery->Published) {
            battery->Work = IoAllocateWorkItem(context->WdmDeviceObject);
            if (battery->Work != NULL) {
                battery->Pdo = WdfDeviceWdmGetPhysicalDevice(context->Device);
                ObReferenceObject(battery->Pdo);
                IoQueueWorkItem(battery->Work, BatteryPublish, DelayedWorkQueue, context);
                return;
            }
        }
    }
    BatteryDone(context);
}

static VOID BatteryTick(WDFTIMER timer)
{
    WDFDEVICE device = WdfTimerGetParentObject(timer);
    PDEVICE_CONTEXT context = PtpFilterGetContext(device);
    BATTERY_CONTEXT* battery = &context->Battery;
    LONG epoch = battery->Epoch;
    WDFREQUEST request = NULL;
    WDFMEMORY output;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_REQUEST_SEND_OPTIONS options;
    NTSTATUS status;

    if (!(epoch & 1)) return;
    if (context->ConfigurationInProgress) {
        WdfTimerStart(timer, WDF_REL_TIMEOUT_IN_SEC(3));
        return;
    }
    WdfTimerStart(timer, WDF_REL_TIMEOUT_IN_SEC(AMT_BATTERY_POLL_INTERVAL_SEC));
    if (InterlockedCompareExchange(&battery->Busy, 1, 0) != 0) return;
    WdfObjectReference(device); // held through completion and optional publication
    battery->SampleEpoch = epoch;
    RtlZeroMemory(battery->Report, sizeof(battery->Report));
    battery->Packet.reportId = battery->Report[0] = 0x90;
    battery->Packet.reportBuffer = battery->Report;
    battery->Packet.reportBufferLen = sizeof(battery->Report);

    status = WdfRequestCreate(WDF_NO_OBJECT_ATTRIBUTES, battery->Target, &request);
    if (!NT_SUCCESS(status)) goto failed;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = request;
    status = WdfMemoryCreatePreallocated(&attributes, battery->Report, sizeof(battery->Report), &output);
    if (!NT_SUCCESS(status)) goto failed;
    // Let KMDF format the direct-I/O MDL for the actual report bytes.
    status = WdfIoTargetFormatRequestForInternalIoctl(battery->Target, request,
        IOCTL_HID_GET_INPUT_REPORT, NULL, NULL, output, NULL);
    if (!NT_SUCCESS(status)) goto failed;
    WdfRequestWdmGetIrp(request)->UserBuffer = &battery->Packet;
    WdfRequestSetCompletionRoutine(request, BatteryComplete, context);
    WDF_REQUEST_SEND_OPTIONS_INIT(&options, WDF_REQUEST_SEND_OPTION_TIMEOUT);
    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(&options, WDF_REL_TIMEOUT_IN_SEC(AMT_BATTERY_REQUEST_TIMEOUT_SEC));
    if (epoch != battery->Epoch) goto failed;
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "Submitting battery read");
    if (WdfRequestSend(request, battery->Target, &options)) return;
failed:
    if (request != NULL) WdfObjectDelete(request);
    BatteryDone(context);
}

VOID PtpBatteryInitialize(WDFDEVICE device)
{
    PDEVICE_CONTEXT context = PtpFilterGetContext(device);
    BATTERY_CONTEXT* battery = &context->Battery;
    WDF_IO_TARGET_OPEN_PARAMS open;
    WDF_TIMER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;
    NTSTATUS status = WdfIoTargetCreate(device, WDF_NO_OBJECT_ATTRIBUTES, &battery->Target);
    if (NT_SUCCESS(status)) {
        WDF_IO_TARGET_OPEN_PARAMS_INIT_EXISTING_DEVICE(&open,
            WdfIoTargetWdmGetTargetDeviceObject(context->HidIoTarget));
        status = WdfIoTargetOpen(battery->Target, &open);
    }
    if (NT_SUCCESS(status)) {
        WDF_TIMER_CONFIG_INIT(&config, BatteryTick);
        config.AutomaticSerialization = FALSE;
        WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
        attributes.ParentObject = device;
        attributes.ExecutionLevel = WdfExecutionLevelPassive;
        status = WdfTimerCreate(&config, &attributes, &battery->Timer);
    }
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_DEVICE, "Battery initialization: %!STATUS!", status);
    }
}

VOID PtpBatteryStart(WDFDEVICE device)
{
    BATTERY_CONTEXT* battery = &PtpFilterGetContext(device)->Battery;
    if (battery->Timer == NULL || (battery->Epoch & 1)) return;
    if (!NT_SUCCESS(WdfIoTargetStart(battery->Target))) return;
    InterlockedIncrement(&battery->Epoch);
    WdfTimerStart(battery->Timer, WDF_REL_TIMEOUT_IN_SEC(3));
}

NTSTATUS PtpBatterySuspend(WDFDEVICE device)
{
    BATTERY_CONTEXT* battery = &PtpFilterGetContext(device)->Battery;
    if (battery->Epoch & 1) {
        InterlockedIncrement(&battery->Epoch);
        WdfTimerStop(battery->Timer, FALSE);
        WdfIoTargetPurge(battery->Target, WdfIoTargetPurgeIo);
    }
    return STATUS_SUCCESS;
}
