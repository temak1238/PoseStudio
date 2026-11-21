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
    /// <summary>Ergebnis-Ordner (Storage-Access-Framework-Adresse); darin der Unterordner „SwarmUI-Tablet“. Leer = nur intern.</summary>
    public string? ResultFolder { get; set; }
    /// <summary>Historie: nur Prompts übernehmen.</summary>
    public bool HistoryPromptsOnly { get; set; }
    /// <summary>Bis 0.22.6: Seitenverhältnis als Index in die alte Liste (wird beim Laden in Format/Portrait übernommen).</summary>
    public int Aspect { get; set; }
    /// <summary>Format ohne Vorlage (Index in Formats), -1 = noch nicht gesetzt.</summary>
    public int Format { get; set; } = -1;
    /// <summary>Hochformat (sonst Querformat).</summary>
    public bool Portrait { get; set; } = true;

    /// <summary>Formate als Verhältnis lange : kurze Seite; Hoch/Quer kommt aus Portrait.</summary>
    public static readonly (string Name, double Value)[] Formats =
    {
        ("1:1 (quadratisch)", 1), ("5:4", 5 / 4.0), ("4:3", 4 / 3.0), ("3:2", 3 / 2.0), ("16:9", 16 / 9.0), ("21:9", 21 / 9.0)
    };

    /// <summary>Seitenverhältnis Breite/Höhe aus Format und Ausrichtung.</summary>
    public double AspectValue()
    {
        double v = Formats[Math.Clamp(Format, 0, Formats.Length - 1)].Value;
        return Portrait ? 1 / v : v;
    }

    /// <summary>Alte Auswahl (1:1, 3:4 hoch, 2:3 hoch, 9:16 hoch, 4:3 quer, 3:2 quer, 16:9 quer) übernehmen.</summary>
    private void MigrateAspect()
    {
        if (Format >= 0) return;
        (Format, Portrait) = Aspect switch { 1 => (2, true), 2 => (3, true), 3 => (4, true), 4 => (2, false), 5 => (3, false), 6 => (4, false), _ => (0, true) };
    }

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static TabletSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TabletSettings>(File.ReadAllText(FilePath), Json) is { } s) { s.MigrateAspect(); return s; }
        }
        catch { }
        var n = new TabletSettings(); n.MigrateAspect(); return n;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static TabletSettings? FromJson(string json) { var s = JsonSerializer.Deserialize<TabletSettings>(json, Json); s?.MigrateAspect(); return s; }

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
