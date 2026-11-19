using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace HapticsLab;

/// <summary>
/// Raw access to the actuator through the drivers' own control interfaces: fire one 0x53 pulse
/// with arbitrary S0/S1/S2, and make the driver re-read its registry settings.
/// </summary>
public interface IRawTransport
{
    string Name { get; }
    void Fire(Pulse pulse);
    void ReloadSettings();
}

/// <summary>USB driver (AmtPtpDeviceUsbUm): named pipe, one command per connection.</summary>
public sealed class UsbPipeTransport : IRawTransport
{
    private const string PipeName = "AmtPtpControlPanelUsbUmInterface";
    private const byte CmdReload = 0x00;
    private const byte CmdActuate = 0x02;

    public string Name => "USB (control pipe)";

    public static bool IsAvailable()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(300);
            return true;
        }
        catch { return false; }
    }

    public void Fire(Pulse p) => Send(new byte[] { CmdActuate, p.S0, p.S1, p.S2 });

    public void ReloadSettings() => Send(new byte[] { CmdReload });

    private static void Send(byte[] request)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                pipe.Connect(1000);
                pipe.Write(request, 0, request.Length);
                pipe.Flush();
                var response = new byte[2];
                if (pipe.Read(response, 0, 2) != 2)
                    throw new IOException("Short response from the driver");
                if (response[0] != 0)
                    throw new IOException($"The driver answered 0x{response[0]:X2}");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException("Access to the USB driver was denied. Run HapticsLab as administrator.");
            }
            catch (Exception e) when (e is IOException or TimeoutException)
            {
                // The driver recreates the pipe instance after each command
                last = e;
                Thread.Sleep(30);
            }
        }
        throw new IOException("The USB driver's control pipe is not reachable.", last);
    }
}

/// <summary>Bluetooth filter (AmtPtpHidFilter): \\.\AmtPtpControlDeviceUm IOCTLs.</summary>
public sealed class BluetoothControlTransport : IRawTransport
{
    private const string DevicePath = @"\\.\AmtPtpControlDeviceUm";
    private const uint IoctlReloadSettings = 0x00222000; // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IoctlActuate = 0x0022A008;        // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_WRITE_ACCESS)

    public string Name => "Bluetooth (control device)";

    public static bool IsAvailable()
    {
        using var h = Native.CreateFile(DevicePath, Native.GENERIC_READ | Native.GENERIC_WRITE, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return !h.IsInvalid;
    }

    public void Fire(Pulse p) => Ioctl(IoctlActuate, BitConverter.GetBytes(p.Packed));

    public void ReloadSettings() => Ioctl(IoctlReloadSettings, Array.Empty<byte>());

    private static void Ioctl(uint code, byte[] input)
    {
        using var h = Native.CreateFile(DevicePath, Native.GENERIC_READ | Native.GENERIC_WRITE, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Bluetooth filter's control device");
        if (!Native.DeviceIoControl(h, code, input, (uint)input.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Bluetooth filter rejected the request");
    }
}

/// <summary>
/// The touch pad HID collection, used the way Windows uses it: haptic feature reports
/// (click sensitivity, haptic click intensity, waveform list) and the Manual Trigger output report.
/// Works over USB and Bluetooth, with whatever values are compiled into the installed driver.
/// </summary>
public sealed class HidTouchpad : IDisposable
{
    public const ushort DeviceVid = 0x8910;
    private const byte ReportThreshold = 0x0A, ReportIntensity = 0x0B, ReportWaveforms = 0x0C, ReportTrigger = 0x0D;

    private readonly SafeFileHandle _handle;
    private readonly int _featureLength, _outputLength;
    public string Path { get; }
    public int HapticCollections { get; }

    private HidTouchpad(SafeFileHandle handle, string path, int featureLength, int outputLength, int hapticCollections)
    {
        _handle = handle; Path = path; _featureLength = featureLength; _outputLength = outputLength;
        HapticCollections = hapticCollections;
    }

    public static HidTouchpad? Open()
    {
        foreach (var path in Native.HidInterfaces())
        {
            var h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); continue; }
            var attrs = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
            if (Native.HidD_GetAttributes(h, ref attrs) && attrs.VendorID == DeviceVid &&
                Native.HidD_GetPreparsedData(h, out var pp))
            {
                try
                {
                    Native.HidP_GetCaps(pp, out var caps);
                    if (caps.UsagePage == 0x0D && caps.Usage == 0x05)
                    {
                        int count = caps.NumberLinkCollectionNodes;
                        var nodes = new Native.HIDP_LINK_COLLECTION_NODE[count];
                        uint n = (uint)count;
                        Native.HidP_GetLinkCollectionNodes(nodes, ref n, pp);
                        int shc = nodes.Take((int)n).Count(x => x.LinkUsagePage == 0x0E && x.LinkUsage == 0x01);
                        return new HidTouchpad(h, path, caps.FeatureReportByteLength, caps.OutputReportByteLength, shc);
                    }
                }
                finally { Native.HidD_FreePreparsedData(pp); }
            }
            h.Dispose();
        }
        return null;
    }

    public bool HasHaptics => HapticCollections > 0 && _outputLength > 0;

    private byte[] GetFeature(byte id)
    {
        var buf = new byte[_featureLength];
        buf[0] = id;
        if (!Native.HidD_GetFeature(_handle, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"GET_FEATURE 0x{id:X2} failed");
        return buf;
    }

    private void SetFeature(byte id, byte value)
    {
        var buf = new byte[_featureLength];
        buf[0] = id; buf[1] = value;
        if (!Native.HidD_SetFeature(_handle, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SET_FEATURE 0x{id:X2} failed");
    }

    public int Threshold => GetFeature(ReportThreshold)[1];
    public int Intensity => GetFeature(ReportIntensity)[1];
    public void SetThreshold(int value) => SetFeature(ReportThreshold, (byte)value);
    public void SetIntensity(int value) => SetFeature(ReportIntensity, (byte)value);

    /// <summary>Waveform usage IDs and durations as the installed driver reports them.</summary>
    public List<(ushort Id, int DurationMs)> WaveformList(int count)
    {
        var r = GetFeature(ReportWaveforms);
        var list = new List<(ushort, int)>();
        for (int i = 0; i < count; i++)
            list.Add(((ushort)(r[1 + 2 * i] | (r[2 + 2 * i] << 8)), r[1 + 2 * count + i]));
        return list;
    }

    /// <summary>Fires a waveform through the driver, exactly as Windows does. Ordinal 3 is the first waveform.</summary>
    public void Trigger(int ordinal, int intensity)
    {
        var buf = new byte[_outputLength];
        buf[0] = ReportTrigger; buf[1] = (byte)ordinal; buf[2] = (byte)intensity;
        if (!Native.HidD_SetOutputReport(_handle, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SET_OUTPUT_REPORT failed");
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Temporarily changes the click waveform through the drivers' registry settings, and puts the
/// previous values back. The snapshot is kept in the session file, so a crash can still be undone.
/// </summary>
public sealed class DriverSettings
{
    public static readonly string[] Keys =
    {
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters",
        @"SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters",
    };
    public static readonly string[] Names = { "FeedbackClick", "FeedbackRelease", "OverrideWindowsHaptics" };

    /// <summary>Key path -> value name -> previous value (null: the value did not exist).</summary>
    public static Dictionary<string, Dictionary<string, int?>> Snapshot()
    {
        var snap = new Dictionary<string, Dictionary<string, int?>>();
        foreach (var path in Keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var values = new Dictionary<string, int?>();
            foreach (var name in Names)
                values[name] = key?.GetValue(name) is int v ? v : null;
            snap[path] = values;
        }
        return snap;
    }

    public static void ApplyTestClick(uint click, uint release)
    {
        foreach (var path in Keys)
        {
            // Only touch drivers that are installed
            using var service = Registry.LocalMachine.OpenSubKey(path[..path.LastIndexOf('\\')]);
            if (service == null) continue;
            using var key = Registry.LocalMachine.CreateSubKey(path, true);
            key.SetValue("FeedbackClick", unchecked((int)click), RegistryValueKind.DWord);
            key.SetValue("FeedbackRelease", unchecked((int)release), RegistryValueKind.DWord);
            key.SetValue("OverrideWindowsHaptics", 1, RegistryValueKind.DWord);
        }
    }

    public static void Restore(Dictionary<string, Dictionary<string, int?>> snap)
    {
        foreach (var (path, values) in snap)
        {
            using var service = Registry.LocalMachine.OpenSubKey(path[..path.LastIndexOf('\\')]);
            if (service == null) continue;
            using var key = Registry.LocalMachine.CreateSubKey(path, true);
            foreach (var (name, value) in values)
            {
                if (value is int v) key.SetValue(name, v, RegistryValueKind.DWord);
                else key.DeleteValue(name, false);
            }
        }
    }
}

internal static class Native
{
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_LINK_COLLECTION_NODE
    {
        public ushort LinkUsage, LinkUsagePage, Parent, NumberOfChildren, NextSibling, FirstChild;
        public uint Bits;
        public IntPtr UserContext;
    }

    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attrs);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("hid.dll")] public static extern int HidP_GetLinkCollectionNodes([Out] HIDP_LINK_COLLECTION_NODE[] nodes, ref uint length, IntPtr pp);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int length);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int length);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int length);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid guid, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid guid, string? deviceId, char[] buffer, uint length, uint flags);

    private static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    public static IEnumerable<string> HidInterfaces()
    {
        var guid = HidGuid;
        if (CM_Get_Device_Interface_List_SizeW(out var length, ref guid, null, 0) != 0 || length == 0)
            return Array.Empty<string>();
        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, 0) != 0)
            return Array.Empty<string>();
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
