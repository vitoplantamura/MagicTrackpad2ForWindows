using System.Text.Json;
using System.Text.Json.Serialization;

namespace HapticsLab;

/// <summary>
/// Everything worth keeping between sessions: the working values (not yet written to the header),
/// the candidates you tried, your picks and notes, and a pending registry restore if a click test
/// was still running when the app closed.
/// </summary>
public sealed class Session
{
    public Calibration? Working { get; set; }
    public Dictionary<string, List<string>> Candidates { get; set; } = new();
    public Dictionary<string, int> Picked { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();
    public Dictionary<string, string> Ratings { get; set; } = new();     // "waveform.STEP" -> "Just right"
    public Dictionary<string, bool> Checks { get; set; } = new();        // "clickForce.0" -> felt and fine
    public Dictionary<string, Dictionary<string, int?>>? PendingRestore { get; set; }
    public DateTime Saved { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HapticsLab", "session.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new PulseConverter() },
    };

    public static Session Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Session>(File.ReadAllText(path), Options) ?? new Session();
        }
        catch { /* a broken session file starts a new session */ }
        return new Session();
    }

    public void Save(string path)
    {
        Saved = DateTime.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, true);
    }

    /// <summary>Pulses as "S0,S1,S2" hex strings, readable in the file.</summary>
    private sealed class PulseConverter : JsonConverter<Pulse>
    {
        public override Pulse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Pulse.ParseChain(reader.GetString() ?? "")[0];

        public override void Write(Utf8JsonWriter writer, Pulse value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
