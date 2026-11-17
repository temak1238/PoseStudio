using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using PoseStudio.Rendering;

namespace PoseStudio.Model;

/// <summary>Eintrag der Posen-Bibliothek (eingebaute Vorlage oder eigene, gespeicherte Pose).</summary>
public sealed class LibraryEntry : INotifyPropertyChanged
{
    private ImageSource? _image;

    public required string Name { get; init; }
    public required string Category { get; init; }
    public required PoseData Pose { get; init; }
    public bool IsUser { get; init; }
    /// <summary>Datei der eigenen Vorlage (ohne Endung), sonst null.</summary>
    public string? BasePath { get; init; }

    public ImageSource? Image
    {
        get => _image;
        set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Eigene Posen-Vorlagen speichern/laden und Vorschaubilder erzeugen.</summary>
public static class PoseLibrary
{
    public const string UserCategory = "Eigene";
    public const int ThumbW = 150, ThumbH = 190;

    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoseStudio");
    public static string UserFolder => Path.Combine(Root, "Vorlagen");
    private static string CacheFolder => Path.Combine(Root, "Cache");

    public static PoseData Clone(PoseData p) =>
        JsonSerializer.Deserialize<PoseData>(JsonSerializer.Serialize(p))!;

    public static List<LibraryEntry> LoadUser()
    {
        var list = new List<LibraryEntry>();
        if (!Directory.Exists(UserFolder)) return list;
        foreach (var file in Directory.GetFiles(UserFolder, "*.pose.json").OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var pose = PoseData.Load(file);
                var basePath = file[..^".pose.json".Length];
                var entry = new LibraryEntry { Name = Path.GetFileName(basePath), Category = UserCategory, Pose = pose, IsUser = true, BasePath = basePath };
                if (File.Exists(basePath + ".png")) entry.Image = LoadImage(basePath + ".png");
                list.Add(entry);
            }
            catch { /* defekte Datei überspringen */ }
        }
        return list;
    }

    public static IEnumerable<LibraryEntry> BuiltIn() =>
        Presets.All.Select(p => new LibraryEntry { Name = p.Name, Category = p.Category, Pose = p.ToPose() });

    public static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    /// <summary>Pose als eigene Vorlage speichern (JSON + Vorschaubild). Gibt den Basis-Pfad zurück.</summary>
    public static string SaveUser(string name, PoseData pose, BitmapSource thumb)
    {
        Directory.CreateDirectory(UserFolder);
        var basePath = Path.Combine(UserFolder, SafeName(name));
        pose.Save(basePath + ".pose.json");
        SceneFactory.SavePng(thumb, basePath + ".png");
        return basePath;
    }

    public static void DeleteUser(LibraryEntry e)
    {
        if (!e.IsUser || e.BasePath == null) return;
        File.Delete(e.BasePath + ".pose.json");
        if (File.Exists(e.BasePath + ".png")) File.Delete(e.BasePath + ".png");
    }

    public static BitmapImage LoadImage(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Vorschaubild einer Pose: Figur in 3/4-Ansicht, eingepasst, heller Hintergrund.</summary>
    public static BitmapSource RenderThumb(PoseData pose, FigureData figure, Color body)
    {
        var m = new Mannequin();
        m.SetFigure(figure.Style, figure.Hair, figure.Proportions);
        if (figure.HairStyle != null) m.SetHair(figure.HairStyle);
        m.BodyColor = body;
        var p = Clone(pose);
        p.RootPosition = new double[3];
        m.SetPose(p);
        m.GroundToFloor();
        double min = m.ComputeMinY(), max = m.ComputeMaxY();
        var pel = m.PelvisWorld;
        var cam = new OrbitCamera { Yaw = 28, Pitch = 10, FocalLength = 50, Target = new Point3D(pel.X, (min + max) / 2, pel.Z) };
        double half = Math.Max((max - min) / 2 * 1.15, 0.45);
        cam.Distance = Math.Clamp(half / Math.Tan(cam.VerticalFov * Math.PI / 360), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
        var o = new SceneOptions
        {
            Background = Color.FromRgb(0xEC, 0xEC, 0xEC), Floor = false, Shadow = true,
            Light = new LightSettings { CastShadow = false, ShowSource = false }
        };
        return SceneFactory.Render(m, cam, o, ThumbW, ThumbH);
    }

    /// <summary>Vorschaubild einer eingebauten Vorlage aus dem Cache (oder neu erzeugen und ablegen).</summary>
    public static ImageSource BuiltInThumb(LibraryEntry e, FigureData figure, Color body, string version)
    {
        Directory.CreateDirectory(CacheFolder);
        var key = $"{version}_{figure.Style}_{SafeName(e.Name)}_{body.R:X2}{body.G:X2}{body.B:X2}.png";
        var path = Path.Combine(CacheFolder, key);
        if (File.Exists(path)) return LoadImage(path);
        var bmp = RenderThumb(e.Pose, figure, body);
        try { SceneFactory.SavePng(bmp, path); } catch { /* Cache ist optional */ }
        return bmp;
    }
}
