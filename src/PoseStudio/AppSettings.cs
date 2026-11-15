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
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PoseStudio");
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

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoseStudio", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* defekte Einstellungen ignorieren */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
    }
}
