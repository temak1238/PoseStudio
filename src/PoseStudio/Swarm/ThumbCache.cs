using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoseStudio.Swarm;

/// <summary>Vorschaubilder von SwarmUI nachladen: höchstens 6 gleichzeitig, auf der Platte zwischengespeichert
/// (Unterordner SwarmCache im Programmordner; nur falls dort nicht schreibbar unter %LOCALAPPDATA%), verkleinert dekodiert.</summary>
public static class ThumbCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly SemaphoreSlim Gate = new(6);
    private static string LegacyFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoseStudio", "SwarmCache");
    public static string Folder { get; } = PickFolder();

    private static string PickFolder()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "SwarmCache");
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, ""); File.Delete(probe);
            return dir;
        }
        catch { return LegacyFolder; }
    }

    /// <summary>Bis 0.22.1 lagen hier ganze Historienbilder (mehrere MB); seitdem nur noch Vorschaubilder – große Einträge im Hintergrund löschen.</summary>
    private static void PruneLarge()
    {
        string dir = Folder;
        if (!Directory.Exists(dir)) return;
        Task.Run(() =>
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.img"))
                try { if (new FileInfo(f).Length > 1_000_000) File.Delete(f); } catch { }
        });
    }

    /// <summary>Alten Cache (%LOCALAPPDATA%\PoseStudio\SwarmCache, bis 0.21.4) in den Programmordner verschieben, im Hintergrund.</summary>
    public static void MigrateLegacy()
    {
        PruneLarge();
        string old = LegacyFolder, dst = Folder;
        if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(old)) return;
        Task.Run(() =>
        {
            foreach (var f in Directory.EnumerateFiles(old))
                try
                {
                    string t = Path.Combine(dst, Path.GetFileName(f));
                    if (File.Exists(t)) File.Delete(f); else File.Move(f, t);
                }
                catch { /* gerade in Benutzung – beim nächsten Mal */ }
            try { if (!Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old); } catch { }
        });
    }

    public static async Task<ImageSource?> LoadAsync(string url, int decodeWidth)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20] + ".img");
            byte[] data;
            if (File.Exists(file)) data = await File.ReadAllBytesAsync(file);
            else
            {
                await Gate.WaitAsync();
                try { data = await Http.GetByteArrayAsync(url); }
                finally { Gate.Release(); }
                await File.WriteAllBytesAsync(file, data);
            }
            return Decode(data, decodeWidth);
        }
        catch { return null; }
    }

    public static BitmapImage Decode(byte[] data, int decodeWidth)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
        bi.StreamSource = new MemoryStream(data);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}

/// <summary>Kachel mit nachgeladenem Bild (Modell, LoRA, Historienbild).</summary>
public sealed class ThumbItem : INotifyPropertyChanged
{
    private ImageSource? _image;
    private bool _requested;
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? ToolTip { get; init; }
    public string? Url { get; init; }
    public object? Tag { get; init; }
    public int DecodeWidth { get; init; } = 140;

    public ImageSource? Image
    {
        get
        {
            if (!_requested && Url != null)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _image;
        }
    }

    private async Task LoadAsync()
    {
        _image = await ThumbCache.LoadAsync(Url!, DecodeWidth);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
