using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Einstellungen der Tablet-App: die SwarmUI-Einstellungen (gleiches Format wie Pose Studio) plus Vorlagen-Quelle.</summary>
public sealed class TabletSettings
{
    public SwarmSettings Swarm { get; set; } = new();
    /// <summary>0 = nur Prompt, 1 = Posen-Vorlage, 2 = eigenes Bild.</summary>
    public int Source { get; set; }
    /// <summary>Gewählter Vorlagen-Ordner (Storage-Access-Framework-Adresse).</summary>
    public string? TemplateFolder { get; set; }
    public string? TemplateName { get; set; }
    /// <summary>Eigenes Bild (Kopie im App-Ordner).</summary>
    public string? ImagePath { get; set; }
    /// <summary>Seitenverhältnis ohne Vorlage (Index in Aspects).</summary>
    public int Aspect { get; set; }

    public static readonly (string Name, double Value)[] Aspects =
    {
        ("1:1", 1), ("3:4 hoch", 3 / 4.0), ("2:3 hoch", 2 / 3.0), ("9:16 hoch", 9 / 16.0), ("4:3 quer", 4 / 3.0), ("3:2 quer", 3 / 2.0), ("16:9 quer", 16 / 9.0)
    };

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static TabletSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TabletSettings>(File.ReadAllText(FilePath), Json) is { } s) return s;
        }
        catch { }
        return new TabletSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); } catch { }
    }
}

/// <summary>Ein Ergebnis in der Liste (oder Platzhalter während der Generierung).</summary>
public sealed class ResultItem : INotifyPropertyChanged
{
    private ImageSource? _image; private string _caption = ""; private bool _done;
    public ImageSource? Image { get => _image; set { _image = value; Changed(nameof(Image)); } }
    public string Caption { get => _caption; set { _caption = value; Changed(nameof(Caption)); } }
    public bool Done { get => _done; set { _done = value; Changed(nameof(Done)); } }
    public string? LocalPath { get; set; }
    public JsonObject? Sent { get; set; }
    public long Seed { get; set; } = -1;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Kachel in einer Auswahl (Modell, LoRA, Historienbild, Vorlage).</summary>
public sealed class Tile : INotifyPropertyChanged
{
    private bool _marked;
    public required string Label { get; init; }
    public string? Sub { get; init; }
    public ImageSource? Image { get; init; }
    public required object Tag { get; init; }
    public bool Marked { get => _marked; set { _marked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marked))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class Ui
{
    public static readonly Color Panel = Color.FromArgb("#1E2633");
    public static readonly Color PanelLight = Color.FromArgb("#28323F");
    public static readonly Color Accent = Color.FromArgb("#2B7BD6");
    public static readonly Color Muted = Color.FromArgb("#9AA7B8");
    public static readonly Color Page = Color.FromArgb("#141A22");

    public static ImageSource? Web(string? url) => url == null ? null
        : url.StartsWith("data:") ? ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(url[(url.IndexOf(',') + 1)..])))
        : new UriImageSource { Uri = new Uri(url), CachingEnabled = true, CacheValidity = TimeSpan.FromDays(60) };

    public static ImageSource Bytes(byte[] data) => ImageSource.FromStream(() => new MemoryStream(data));

    public static double PD(string? s, double fallback) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public static string DataUrl(byte[] data) =>
        (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 ? "data:image/jpeg;base64," : data.Length > 3 && data[0] == 'R' && data[1] == 'I' ? "data:image/webp;base64," : "data:image/png;base64,")
        + Convert.ToBase64String(data);
}
