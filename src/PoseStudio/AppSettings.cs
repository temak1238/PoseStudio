using System.IO;
using System.Text.Json;

namespace PoseStudio;

public sealed class AppSettings
{
    public string Background { get; set; } = "#E6E6E6";
    public string BodyColor { get; set; } = "#C9B292";
    public bool FloorPreview { get; set; } = true;
    public bool ShadowPreview { get; set; } = true;
    public bool FloorExport { get; set; } = false;
    public bool ShadowExport { get; set; } = false;
    public string PreviewFormat { get; set; } = "16x9";
    public List<string> ExportFormats { get; set; } = new() { "16x9", "9x16" };
    public int LongSide { get; set; } = 1920;
    public int CustomWidth { get; set; } = 1200;
    public int CustomHeight { get; set; } = 1600;
    public int JpegQuality { get; set; } = 95;
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    /// <summary>Zielordner je Computer (Schlüssel = Rechnername): bei synchronisiertem Programmordner (Portabel) hat jeder PC seinen eigenen.</summary>
    public Dictionary<string, string> OutputFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PoseStudio");

    /// <summary>Zielordner dieses PCs: eigener Eintrag, sonst der zuletzt gespeicherte, falls es ihn hier gibt, sonst Bilder\PoseStudio.</summary>
    public string OutputFolderHere()
    {
        if (OutputFolders.TryGetValue(Environment.MachineName, out var f) && !string.IsNullOrWhiteSpace(f)) return f;
        if (!string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder)) return OutputFolder;
        return DefaultOutputFolder;
    }

    public void SetOutputFolderHere(string folder)
    {
        OutputFolders[Environment.MachineName] = folder;
        OutputFolder = folder;
    }
    public string FilePrefix { get; set; } = "pose";
    public string HairColor { get; set; } = "#3A2A20";
    public bool Guides { get; set; } = true;
    public bool OutputPng { get; set; } = false;
    public bool Transparent { get; set; } = false;
    public string? BackgroundImage { get; set; }
    public string BackgroundStretch { get; set; } = "UniformToFill";
    public PoseStudio.Model.FigureData Figure { get; set; } = new();
    public PoseStudio.Rendering.LightSettings Light { get; set; } = new();
    public PoseStudio.Rendering.SetSettings Set { get; set; } = new();
    public PoseStudio.Model.PropSettings Prop { get; set; } = new();
    public List<PoseStudio.Model.PoseData>? Figures { get; set; }
    public int ActiveFigure { get; set; }
    public bool ControlNet3D { get; set; } = true;
    public string? ReferenceImage { get; set; }
    /// <summary>Design: System, Hell oder Dunkel.</summary>
    public string UiTheme { get; set; } = "System";
    /// <summary>Update-Prüfung beim Start; übersprungene Version (Release-Tag).</summary>
    public bool CheckUpdates { get; set; } = true;
    public string? SkipUpdateVersion { get; set; }
    public double ReferenceOpacity { get; set; } = 50;
    /// <summary>Pose aus Foto: Körper aus RTMW3D statt MediaPipe; Finger mit RTMW3D erkennen.</summary>
    public bool PhotoBodyRtmw3d { get; set; }
    public bool PhotoFingers { get; set; } = true;
    public bool ControlNetRegions { get; set; }
    public bool ControlNetOnly { get; set; }
    public bool ClickSelectsFigure { get; set; }
    /// <summary>Haare und Stoff: 0 = immer live, 1 = beim Ziehen einfrieren, 2 = eingefroren.</summary>
    public int DynamicsMode { get; set; } = 1;
    /// <summary>SwarmUI-Fenster: Server, Einstellungen, Profile.</summary>
    public PoseStudio.Swarm.SwarmSettings Swarm { get; set; } = new();

    internal static string? FileOverride;   // nur Tests
    private static string FilePath => FileOverride ?? AppPaths.SettingsFile;

    public static AppSettings Load()
    {
        try
        {
            // portabel, aber noch keine Datei im Programmordner: bisherige Einstellungen aus %APPDATA% als Ausgangspunkt
            string path = File.Exists(FilePath) || !AppPaths.IsPortable || FileOverride != null ? FilePath : Path.Combine(AppPaths.RoamingDir, "settings.json");
            if (File.Exists(path))
            {
                var st = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                st.OutputFolders = new Dictionary<string, string>(st.OutputFolders ?? new(), StringComparer.OrdinalIgnoreCase);
                return st;
            }
        }
        catch { /* defekte Einstellungen ignorieren */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            MergeOtherPcs();
            // erst in eine Hilfsdatei, dann ersetzen: kein halbes settings.json bei Absturz oder während eine Synchronisierung liest
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch { /* nicht kritisch */ }
    }

    /// <summary>Zielordner anderer PCs aus der Datei übernehmen, die inzwischen (z. B. per Syncthing) dazugekommen sind –
    /// sonst würde dieser PC sie beim Speichern überschreiben.</summary>
    private void MergeOtherPcs()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (!doc.RootElement.TryGetProperty(nameof(OutputFolders), out var of) || of.ValueKind != JsonValueKind.Object) return;
            foreach (var p in of.EnumerateObject())
                if (!p.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    OutputFolders[p.Name] = p.Value.GetString()!;
        }
        catch { /* Datei defekt oder gesperrt: eigene Werte reichen */ }
    }
}
