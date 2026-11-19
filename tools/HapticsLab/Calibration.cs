using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HapticsLab;

/// <summary>One 0x53 actuation: S0 (strength / click force), S1 (louder, sharper), S2 (louder, deeper).</summary>
public readonly record struct Pulse(byte S0, byte S1, byte S2)
{
    public const int S1Limit = 0x20, S2Limit = 0x40;

    /// <summary>The drivers' FeedbackClick encoding: S0 | S1 &lt;&lt; 8 | S2 &lt;&lt; 16.</summary>
    public uint Packed => S0 | ((uint)S1 << 8) | ((uint)S2 << 16);

    public static Pulse FromPacked(uint v) => new((byte)v, (byte)(v >> 8), (byte)(v >> 16));

    public override string ToString() => $"{S0:X2},{S1:X2},{S2:X2}";

    /// <summary>"S0,S1,S2" in hex, chained with "+": "10,00,00+1E,08,08".</summary>
    public static List<Pulse> ParseChain(string text)
    {
        var pulses = new List<Pulse>();
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = part.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (bytes.Length != 3)
                throw new FormatException($"'{part}': each pulse needs three hex bytes S0,S1,S2");
            var b = bytes.Select(x => byte.Parse(x.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber)).ToArray();
            if (b[1] > S1Limit || b[2] > S2Limit)
                throw new FormatException($"'{part}': S1 must be <= 20 and S2 <= 40 (hardware limits)");
            pulses.Add(new Pulse(b[0], b[1], b[2]));
        }
        if (pulses.Count == 0) throw new FormatException("Empty candidate");
        if (pulses.Count > Calibration.MaxPulses) throw new FormatException($"At most {Calibration.MaxPulses} pulses");
        return pulses;
    }

    public static string FormatChain(IEnumerable<Pulse> pulses) => string.Join("+", pulses);
}

public sealed class Waveform
{
    public string Key { get; set; } = "";          // PRESS, RELEASE, HOVER, ...
    public int DurationMs { get; set; } = 15;
    public List<Pulse> Pulses { get; set; } = new();
    public string Comment { get; set; } = "";

    public Waveform Clone() => new() { Key = Key, DurationMs = DurationMs, Pulses = new List<Pulse>(Pulses), Comment = Comment };
}

/// <summary>
/// Mirror of the "Calibration table" section of common\MagicTrackpad2Haptics.h, with the same mapping
/// functions as the drivers (Mt2HapticClickFromWindows, Mt2HapticPulsesForTrigger).
/// </summary>
public sealed class Calibration
{
    public const int MaxPulses = 3;
    public const int SingleDurationMs = 15;
    public static readonly string[] WaveformOrder = { "PRESS", "RELEASE", "HOVER", "COLLIDE", "ALIGN", "STEP", "GROW" };

    public byte[] PressS0 { get; set; } = { 0x10, 0x17, 0x1E };
    public byte[] ReleaseS0 { get; set; } = { 0x10, 0x14, 0x18 };
    public int PressS12AtMax { get; set; } = 0x0A;
    public int ReleaseS12Strong { get; set; } = 0x02;
    public int ReleaseStrongIntensity { get; set; } = 80;
    public List<Waveform> Waveforms { get; set; } = new();
    public int PulseGapMs { get; set; } = 30;
    public int PulseS0MinPercent { get; set; } = 50;
    public int PulseS0Floor { get; set; } = 0x0C;

    public Calibration Clone() => new()
    {
        PressS0 = (byte[])PressS0.Clone(), ReleaseS0 = (byte[])ReleaseS0.Clone(),
        PressS12AtMax = PressS12AtMax, ReleaseS12Strong = ReleaseS12Strong, ReleaseStrongIntensity = ReleaseStrongIntensity,
        Waveforms = Waveforms.Select(w => w.Clone()).ToList(),
        PulseGapMs = PulseGapMs, PulseS0MinPercent = PulseS0MinPercent, PulseS0Floor = PulseS0Floor,
    };

    public Waveform Get(string key) => Waveforms.First(w => w.Key == key);

    public static int DurationFor(int pulses, int gapMs) => Math.Min(50, (pulses - 1) * gapMs + SingleDurationMs);

    // ------------------------------------------------------------------------------------------
    // Mapping, identical to the header
    // ------------------------------------------------------------------------------------------

    /// <summary>Windows' click sensitivity (1..3) and haptic click intensity (0..100) -> click/release pulses.</summary>
    public (Pulse Click, Pulse Release) ClickFromWindows(int threshold, int intensity)
    {
        threshold = Math.Clamp(threshold, 1, 3);
        intensity = Math.Clamp(intensity, 0, 100);
        int level = threshold - 1;
        int press = (intensity * PressS12AtMax + 50) / 100;
        int release = intensity >= ReleaseStrongIntensity ? ReleaseS12Strong : 0;
        return (Clamp(PressS0[level], press, press), Clamp(ReleaseS0[level], release, release));
    }

    /// <summary>A waveform at Windows' "Haptic signals" intensity (0..100) -> the pulses the driver fires.</summary>
    public List<Pulse> PulsesForTrigger(IReadOnlyList<Pulse> pulses, int intensity)
    {
        var result = new List<Pulse>();
        if (intensity <= 0) return result;
        intensity = Math.Min(intensity, 100);
        int s0Percent = PulseS0MinPercent + (100 - PulseS0MinPercent) * intensity / 100;
        foreach (var p in pulses.Take(MaxPulses))
        {
            int s0 = Math.Max((p.S0 * s0Percent + 50) / 100, PulseS0Floor);
            int s1 = (p.S1 * intensity + 50) / 100;
            int s2 = (p.S2 * intensity + 50) / 100;
            result.Add(Clamp(s0, s1, s2));
        }
        return result;
    }

    private static Pulse Clamp(int s0, int s1, int s2) =>
        new((byte)Math.Clamp(s0, 0, 255), (byte)Math.Min(s1, Pulse.S1Limit), (byte)Math.Min(s2, Pulse.S2Limit));

    // ------------------------------------------------------------------------------------------
    // Header file
    // ------------------------------------------------------------------------------------------

    private const string StartMarker = "// Calibration table. See CALIBRATION";
    private const string EndMarker = "// End of calibration table";

    public static Calibration ParseHeader(string text)
    {
        var c = new Calibration();
        c.PressS0 = ParseArray(text, "Mt2HapticPressS0");
        c.ReleaseS0 = ParseArray(text, "Mt2HapticReleaseS0");
        c.PressS12AtMax = ParseDefine(text, "MT2_HAPTIC_PRESS_S12_AT_MAX");
        c.ReleaseS12Strong = ParseDefine(text, "MT2_HAPTIC_RELEASE_S12_STRONG");
        c.ReleaseStrongIntensity = ParseDefine(text, "MT2_HAPTIC_RELEASE_STRONG_INTENSITY");
        c.PulseGapMs = ParseDefine(text, "MT2_HAPTIC_PULSE_GAP_MS");
        c.PulseS0MinPercent = ParseDefine(text, "MT2_HAPTIC_PULSE_S0_MIN_PERCENT");
        c.PulseS0Floor = ParseDefine(text, "MT2_HAPTIC_PULSE_S0_FLOOR");

        var row = new Regex(@"^\s*\{\s*HAPTIC_WAVEFORM_(\w+),\s*(\d+),\s*(\d+),\s*\{(.*)\}\s*\},[ \t]*(?://[ \t]*(.*))?$", RegexOptions.Multiline);
        var pulse = new Regex(@"\{\s*0x([0-9A-Fa-f]+),\s*0x([0-9A-Fa-f]+),\s*0x([0-9A-Fa-f]+)\s*\}");
        foreach (Match m in row.Matches(text))
        {
            var w = new Waveform
            {
                Key = m.Groups[1].Value,
                DurationMs = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Comment = m.Groups[5].Value.Trim(),
            };
            int count = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            foreach (Match p in pulse.Matches(m.Groups[4].Value).Take(count))
                w.Pulses.Add(new Pulse(Hex(p.Groups[1].Value), Hex(p.Groups[2].Value), Hex(p.Groups[3].Value)));
            c.Waveforms.Add(w);
        }
        var missing = WaveformOrder.Except(c.Waveforms.Select(w => w.Key)).ToList();
        if (missing.Count > 0)
            throw new FormatException("The header's waveform table is missing: " + string.Join(", ", missing));
        return c;
    }

    private static byte Hex(string s) => byte.Parse(s, NumberStyles.HexNumber);

    private static byte[] ParseArray(string text, string name)
    {
        var m = Regex.Match(text, name + @"\[3\]\s*=\s*\{\s*0x([0-9A-Fa-f]+),\s*0x([0-9A-Fa-f]+),\s*0x([0-9A-Fa-f]+)\s*\}");
        if (!m.Success) throw new FormatException($"{name} not found in the header");
        return new[] { Hex(m.Groups[1].Value), Hex(m.Groups[2].Value), Hex(m.Groups[3].Value) };
    }

    private static int ParseDefine(string text, string name)
    {
        var m = Regex.Match(text, @"^#define\s+" + name + @"\s+(0x[0-9A-Fa-f]+|\d+)", RegexOptions.Multiline);
        if (!m.Success) throw new FormatException($"{name} not found in the header");
        var v = m.Groups[1].Value;
        return v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(v[2..], NumberStyles.HexNumber)
            : int.Parse(v, CultureInfo.InvariantCulture);
    }

    /// <summary>The body of the calibration table section, in the header's own layout.</summary>
    public string GenerateSection(string nl)
    {
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append(nl);
        string H(int v) => $"0x{v:X2}";

        L();
        L("// Button Press Threshold (Low, Medium, High) -> S0 of the press (0x22) and release (0x23) waveforms.");
        L($"static const UCHAR Mt2HapticPressS0[3] = {{ {string.Join(", ", PressS0.Select(b => H(b)))} }};");
        L($"static const UCHAR Mt2HapticReleaseS0[3] = {{ {string.Join(", ", ReleaseS0.Select(b => H(b)))} }};");
        L();
        L("// Haptic Intensity 0..100 -> S1 = S2 of the press waveform, linearly from 0 to this value.");
        L($"#define MT2_HAPTIC_PRESS_S12_AT_MAX {H(PressS12AtMax)}");
        L();
        L("// Release waveform S1 = S2: this value at or above the intensity below, 0 otherwise.");
        L($"#define MT2_HAPTIC_RELEASE_S12_STRONG      {H(ReleaseS12Strong)}");
        L($"#define MT2_HAPTIC_RELEASE_STRONG_INTENSITY {ReleaseStrongIntensity}");
        L();
        L("// Host-initiated waveforms, in ordinal order starting at MT2_HAPTIC_ORDINAL_FIRST.");
        L("// The touchpad rules: Hover, Collide, Align, Step and Grow are mandatory; Press and Release are");
        L("// optional but come as a pair; Click (0x1003) and continuous waveforms are not allowed.");
        L("// A waveform with several pulses fires them MT2_HAPTIC_PULSE_GAP_MS apart (start to start). Its");
        L("// DurationMs should cover the whole chain: about (pulses - 1) * gap + 15.");
        L("static const MT2_HAPTIC_WAVEFORM Mt2HapticWaveforms[MT2_HAPTIC_WAVEFORM_COUNT] = {");
        foreach (var key in WaveformOrder)
        {
            var w = Get(key);
            var pulses = string.Join(", ", w.Pulses.Select(p => $"{{ {H(p.S0)}, {H(p.S1)}, {H(p.S2)} }}"));
            var body = $"{{ {("HAPTIC_WAVEFORM_" + key + ",").PadRight(25)}{w.DurationMs}, {w.Pulses.Count}, {{ {pulses} }} }},";
            L(w.Comment.Length == 0 ? "\t" + body : "\t" + (body.Length >= 80 ? body + " " : body.PadRight(80)) + "// " + w.Comment);
        }
        L("};");
        L();
        L("// Time between the starts of chained pulses. Over Bluetooth a send takes about 70 ms, so pulses");
        L("// land further apart there.");
        L($"#define MT2_HAPTIC_PULSE_GAP_MS {PulseGapMs}");
        L();
        L("// \"Haptic signals\" intensity (Manual Trigger Intensity, 0..100) -> how strongly a waveform above fires.");
        L("// S1/S2 scale linearly with the intensity. S0 scales from MT2_HAPTIC_PULSE_S0_MIN_PERCENT of its table");
        L("// value (at intensity 1) to 100 % (at 100), and never below MT2_HAPTIC_PULSE_S0_FLOOR, so even the lowest");
        L("// setting is felt. Intensity 0 fires nothing. Windows sends 25, 50, 75 or 100.");
        L($"#define MT2_HAPTIC_PULSE_S0_MIN_PERCENT {PulseS0MinPercent}");
        L($"#define MT2_HAPTIC_PULSE_S0_FLOOR       {H(PulseS0Floor)}");
        L();
        return sb.ToString();
    }

    /// <summary>Replaces the calibration table section of the header text with this calibration.</summary>
    public string ReplaceSection(string headerText)
    {
        string nl = headerText.Contains("\r\n") ? "\r\n" : "\n";
        int start = headerText.IndexOf(StartMarker, StringComparison.Ordinal);
        int end = headerText.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end < 0 || end < start)
            throw new FormatException("Calibration table markers not found in the header");
        // Body starts after the "// ====" line that follows the start marker ...
        int bodyStart = headerText.IndexOf(nl, headerText.IndexOf(nl, start, StringComparison.Ordinal) + nl.Length, StringComparison.Ordinal) + nl.Length;
        // ... and ends before the "// ====" line that precedes the end marker
        int bodyEnd = headerText.LastIndexOf(nl + "// ===", end, StringComparison.Ordinal) + nl.Length;
        return headerText[..bodyStart] + GenerateSection(nl) + headerText[bodyEnd..];
    }

    /// <summary>Human-readable differences to another calibration (e.g. the header as loaded).</summary>
    public List<string> DiffTo(Calibration other)
    {
        var d = new List<string>();
        string[] levels = { "Light", "Medium", "Heavy" };
        for (int i = 0; i < 3; i++)
        {
            if (PressS0[i] != other.PressS0[i]) d.Add($"Click force {levels[i]}: press S0 {other.PressS0[i]:X2} -> {PressS0[i]:X2}");
            if (ReleaseS0[i] != other.ReleaseS0[i]) d.Add($"Click force {levels[i]}: release S0 {other.ReleaseS0[i]:X2} -> {ReleaseS0[i]:X2}");
        }
        if (PressS12AtMax != other.PressS12AtMax) d.Add($"Click S1/S2 at max intensity {other.PressS12AtMax:X2} -> {PressS12AtMax:X2}");
        if (ReleaseS12Strong != other.ReleaseS12Strong) d.Add($"Strong release S1/S2 {other.ReleaseS12Strong:X2} -> {ReleaseS12Strong:X2}");
        if (ReleaseStrongIntensity != other.ReleaseStrongIntensity) d.Add($"Strong release from intensity {other.ReleaseStrongIntensity} -> {ReleaseStrongIntensity}");
        foreach (var key in WaveformOrder)
        {
            var a = other.Get(key); var b = Get(key);
            if (Pulse.FormatChain(a.Pulses) != Pulse.FormatChain(b.Pulses))
                d.Add($"{key}: {Pulse.FormatChain(a.Pulses)} -> {Pulse.FormatChain(b.Pulses)}");
            if (a.DurationMs != b.DurationMs) d.Add($"{key} duration {a.DurationMs} -> {b.DurationMs} ms");
        }
        if (PulseGapMs != other.PulseGapMs) d.Add($"Pulse gap {other.PulseGapMs} -> {PulseGapMs} ms");
        if (PulseS0MinPercent != other.PulseS0MinPercent) d.Add($"Signal S0 minimum {other.PulseS0MinPercent} -> {PulseS0MinPercent} %");
        if (PulseS0Floor != other.PulseS0Floor) d.Add($"Signal S0 floor {other.PulseS0Floor:X2} -> {PulseS0Floor:X2}");
        return d;
    }
}
