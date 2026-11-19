using System.Runtime.InteropServices;

namespace HapticsLab;

internal static class Program
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    [STAThread]
    private static void Main(string[] args)
    {
        // Self-test without a window or hardware: parse the header and regenerate it
        if (args.Length == 2 && args[0] == "--selftest")
        {
            Environment.ExitCode = SelfTest.Run(args[1]);
            return;
        }

        // Screenshots of every page, to check the layout (writes nothing else)
        string? screenshots = args.Length == 2 && args[0] == "--screenshots" ? args[1] : null;

        // 1 ms timer resolution, so chained pulses are spaced as specified
        timeBeginPeriod(1);
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(screenshots));
        }
        finally { timeEndPeriod(1); }
    }
}

internal static class SelfTest
{
    public static int Run(string headerPath)
    {
        var text = File.ReadAllText(headerPath);
        var cal = Calibration.ParseHeader(text);
        var regenerated = cal.ReplaceSection(text);
        Console.WriteLine(regenerated == text ? "Round trip: identical" : "Round trip: DIFFERENT");
        foreach (var w in cal.Waveforms)
            Console.WriteLine($"{w.Key,-8} {w.DurationMs,2} ms  {Pulse.FormatChain(w.Pulses),-20} // {w.Comment}");
        var (click, release) = cal.ClickFromWindows(2, 60);
        Console.WriteLine($"Medium @60: click {click} release {release}");
        Console.WriteLine("Step @25: " + Pulse.FormatChain(cal.PulsesForTrigger(cal.Get("STEP").Pulses, 25)));
        Console.WriteLine("Grow @50: " + Pulse.FormatChain(cal.PulsesForTrigger(cal.Get("GROW").Pulses, 50)));
        if (regenerated != text)
        {
            File.WriteAllText(headerPath + ".regenerated", regenerated);
            return 1;
        }
        return 0;
    }
}
