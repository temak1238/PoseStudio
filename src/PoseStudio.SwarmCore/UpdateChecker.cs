using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PoseStudio.Update;

public sealed record UpdateAsset(string Name, string Url, long Size);

/// <summary>Neuestes Release auf GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, IReadOnlyList<UpdateAsset> Assets)
{
    /// <summary>Windows-Paket (PoseStudio-&lt;v&gt;-win-x64.zip).</summary>
    public UpdateAsset? WindowsZip => Assets.FirstOrDefault(a => a.Name.StartsWith("PoseStudio-", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));
    /// <summary>Tablet-App (PoseStudioSwarm-&lt;v&gt;.apk).</summary>
    public UpdateAsset? AndroidApk => Assets.FirstOrDefault(a => a.Name.StartsWith("PoseStudioSwarm-", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
    public string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";
}

/// <summary>Abgleich mit dem neuesten Release im GitHub-Repo und Download der Release-Dateien (nur von dort).</summary>
public static class UpdateChecker
{
    public const string Repo = "temak1238/PoseStudio";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    private const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PoseStudio-Updater", "1.0"));
        return h;
    }

    /// <summary>„v0.22.18“ → 0.22.18 (immer drei Stellen).</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static bool IsNewer(UpdateInfo info, Version current) => info.Version > Normalize(current);

    /// <summary>Neuestes Release abfragen. Wirft bei Netzwerk- oder API-Fehlern.</summary>
    public static async Task<UpdateInfo> GetLatestAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, cts.Token);
        string body = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"GitHub antwortet mit {(int)resp.StatusCode}.");
        var o = JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException("Unerwartete Antwort von GitHub.");
        string tag = o["tag_name"]?.GetValue<string>() ?? "";
        var version = ParseVersion(tag) ?? throw new InvalidDataException($"Release-Tag „{tag}“ ist keine Version.");
        var assets = new List<UpdateAsset>();
        foreach (var a in o["assets"] as JsonArray ?? new JsonArray())
        {
            if (a is not JsonObject ao) continue;
            string url = ao["browser_download_url"]?.GetValue<string>() ?? "";
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;   // nur Dateien aus diesem Repo
            assets.Add(new UpdateAsset(ao["name"]?.GetValue<string>() ?? "", url, ao["size"]?.GetValue<long>() ?? 0));
        }
        return new UpdateInfo(version, tag, o["body"]?.GetValue<string>() ?? "", o["html_url"]?.GetValue<string>() ?? ReleasesPage, assets);
    }

    /// <summary>Release-Datei herunterladen (erst als .part, Größe wird geprüft). progress: 0…1.</summary>
    public static async Task DownloadAsync(UpdateAsset asset, string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!asset.Url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) throw new InvalidOperationException("Download nur aus dem Pose-Studio-Repo.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string part = path + ".part";
        using (var resp = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(part);
            var buf = new byte[1 << 16];
            long done = 0; int n; double last = -1;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0 && progress != null)
                {
                    double f = (double)done / total;
                    if (f - last >= 0.01) { last = f; progress.Report(f); }
                }
            }
        }
        long size = new FileInfo(part).Length;
        if (asset.Size > 0 && size != asset.Size) { File.Delete(part); throw new InvalidDataException($"Download unvollständig ({size} von {asset.Size} Bytes)."); }
        File.Move(part, path, true);
        progress?.Report(1);
    }

    /// <summary>Release-Text gekürzt für Dialoge.</summary>
    public static string ShortNotes(string notes, int max = 900)
    {
        string t = notes.Replace("\r", "").Trim();
        int cut = t.IndexOf("**Dateien:**", StringComparison.Ordinal);
        if (cut > 0) t = t[..cut].TrimEnd();
        return t.Length <= max ? t : t[..max].TrimEnd() + " …";
    }
}
