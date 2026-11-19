using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HapticsLab;

/// <summary>
/// Facts about this PC for the feedback report, so a report from someone else's machine can be read
/// without asking them which Windows, trackpad and driver they had. Every lookup is best effort.
/// </summary>
internal static class SystemInfo
{
    private const string HidClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{745a17a0-74d3-11d0-b6fe-00a0c90f57da}";

    /// <summary>For example "Windows 11 Pro 24H2 (build 26100.2605)".</summary>
    public static string WindowsVersion()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k == null) return Environment.OSVersion.VersionString;
            var name = k.GetValue("ProductName") as string ?? "Windows";
            var build = k.GetValue("CurrentBuildNumber") as string ?? "?";
            // ProductName still says "Windows 10" on Windows 11
            if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
            var display = k.GetValue("DisplayVersion") as string;
            var ubr = k.GetValue("UBR") is int u ? $".{u}" : "";
            return $"{name}{(display != null ? " " + display : "")} (build {build}{ubr})";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    /// <summary>This project's drivers that are installed, with their versions and INF names.</summary>
    public static List<string> InstalledDrivers()
    {
        var result = new List<string>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(HidClassKey);
            if (cls == null) return result;
            foreach (var name in cls.GetSubKeyNames())
            {
                try
                {
                    using var k = cls.OpenSubKey(name);
                    if (k?.GetValue("InfSection") is not string section || !section.StartsWith("AmtPtp", StringComparison.OrdinalIgnoreCase)) continue;
                    var line = $"{k.GetValue("DriverDesc")} {k.GetValue("DriverVersion")} ({k.GetValue("InfPath")})";
                    if (!result.Contains(line)) result.Add(line);
                }
                catch { /* some subkeys are not readable */ }
            }
        }
        catch { }
        return result;
    }

    /// <summary>The Magic Trackpad 2 models that are connected, and how.</summary>
    public static List<string> Trackpads()
    {
        var result = new List<string>();
        try
        {
            foreach (var path in Native.HidInterfaces())
            {
                var m = Regex.Match(path, @"pid[_&](0265|0324)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var model = m.Groups[1].Value == "0265" ? "Magic Trackpad 2 (Lightning, PID 0265)" : "Magic Trackpad (USB-C, PID 0324)";
                var line = model + (path.Contains("00001124", StringComparison.OrdinalIgnoreCase) ? " over Bluetooth" : " over USB");
                if (!result.Contains(line)) result.Add(line);
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Whether Windows Settings shows the haptic touchpad controls, and what they are set to. Windows
    /// keeps them per user; they exist only on builds that have the controls.
    /// </summary>
    public static string WindowsHapticSettings()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad");
            var intensity = k?.GetValue("FeedbackIntensity");
            var force = k?.GetValue("ClickForceSensitivity");
            if (intensity == null && force == null) return "not found (this Windows may not have the haptic touchpad settings)";
            return $"found (FeedbackIntensity {intensity?.ToString() ?? "missing"}, ClickForceSensitivity {force?.ToString() ?? "missing"})";
        }
        catch { return "could not be read"; }
    }
}
