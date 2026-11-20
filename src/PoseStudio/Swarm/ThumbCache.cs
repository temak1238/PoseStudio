using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoseStudio.Swarm;

/// <summary>Vorschaubilder von SwarmUI nachladen: höchstens 6 gleichzeitig, auf der Platte zwischengespeichert
/// (%LOCALAPPDATA%\PoseStudio\SwarmCache), verkleinert dekodiert.</summary>
public static class ThumbCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly SemaphoreSlim Gate = new(6);
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoseStudio", "SwarmCache");

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
