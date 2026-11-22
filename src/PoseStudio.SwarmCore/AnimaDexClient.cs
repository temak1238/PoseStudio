using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PoseStudio.Swarm;

/// <summary>Eintrag aus AnimaDex: Charakter (Trigger „name, serie“ + Aussehen-Tags) oder Künstler (Stil-Tag „@name“).</summary>
public sealed record AnimaDexEntry(string Slug, string Name, string Sub, string Trigger, IReadOnlyList<string> Tags, int Count,
                                   string? ThumbUrl, string? ImageUrl, string? DanbooruUrl, bool IsArtist, double? Score,
                                   IReadOnlyList<(string Name, string Url)> Loras)
{
    /// <summary>Text für den Prompt. Klammern werden maskiert, sonst liest SwarmUI sie als Gewichtung (z. B. „ram \(re:zero\)“).</summary>
    public string PromptText(bool withTags)
    {
        if (IsArtist) return "@" + AnimaDexClient.EscapeParens(Trigger);
        string t = AnimaDexClient.EscapeParens(Trigger);
        return withTags && Tags.Count > 0 ? t + ", " + string.Join(", ", Tags.Select(AnimaDexClient.EscapeParens)) : t;
    }
}

public sealed record AnimaDexFacetValue(string Value, string Label, int Count);

public sealed record AnimaDexPage(int Total, int Page, int Pages, IReadOnlyList<AnimaDexEntry> Results);

/// <summary>Zugriff auf eine AnimaDex-Instanz (https://github.com/zetaneko/AnimaDex) über deren JSON-API.
/// Nur hier hängt Pose Studio am Format von AnimaDex – ändert sich die API, ist nur diese Klasse betroffen.</summary>
public sealed class AnimaDexClient : IDisposable
{
    /// <summary>Filter für Charaktere (Schlüssel der API) mit deutscher Beschriftung.</summary>
    public static readonly (string Key, string Label)[] CharacterFacets =
    {
        ("hair_color", "Haarfarbe"), ("hair_length", "Haarlänge"), ("eye_color", "Augenfarbe"), ("gender", "Geschlecht"),
    };

    public static readonly (string Key, string Label)[] Sorts = { ("count", "Beliebtheit"), ("az", "A–Z"), ("random", "Zufall") };

    private readonly HttpClient _http;
    public string BaseUrl { get; }

    public AnimaDexClient(string url)
    {
        BaseUrl = Normalize(url);
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PoseStudio", "1.0"));
    }

    /// <summary>„192.168.188.17“ → „http://192.168.188.17:5000“ (Standard-Port von AnimaDex, wenn keiner angegeben).</summary>
    public static string Normalize(string url)
    {
        string u = (url ?? "").Trim().TrimEnd('/');
        if (u == "") return "";
        if (!u.Contains("://")) u = "http://" + u;
        if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.IsDefaultPort && !u[(u.IndexOf("://") + 3)..].Contains(':'))
            u = $"{uri.Scheme}://{uri.Host}:5000{uri.AbsolutePath.TrimEnd('/')}";
        return u;
    }

    public static string EscapeParens(string s) => s.Replace("(", "\\(").Replace(")", "\\)");

    private string? Abs(string? path) => string.IsNullOrEmpty(path) ? null : path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : BaseUrl + path;

    private async Task<JsonObject> GetAsync(string path, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(path, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"AnimaDex antwortet mit {(int)resp.StatusCode} ({path}).");
        return JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException("Unerwartete Antwort von AnimaDex.");
    }

    /// <summary>Anzahl Einträge (Verbindungstest).</summary>
    public async Task<int> CountAsync(bool artists, CancellationToken ct = default) =>
        (await GetAsync($"api/{(artists ? "artists" : "characters")}/facets", ct))["total"]?.GetValue<int>() ?? 0;

    /// <summary>Werte eines Filters (häufigste zuerst, höchstens 30; mit q gesucht).</summary>
    public async Task<List<AnimaDexFacetValue>> FacetValuesAsync(string facet, string q = "", bool artists = false, CancellationToken ct = default)
    {
        var o = await GetAsync($"api/{(artists ? "artists" : "characters")}/facet/{Uri.EscapeDataString(facet)}?q={Uri.EscapeDataString(q)}", ct);
        return (o["values"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(v => new AnimaDexFacetValue(v["value"]?.ToString() ?? "", v["label"]?.ToString() ?? "", v["count"]?.GetValue<int>() ?? 0)).ToList();
    }

    /// <summary>Suche. filters: API-Schlüssel → gewählte Werte (z. B. hair_color → „blonde hair“). sort: count, az, random (Künstler auch score).</summary>
    public async Task<AnimaDexPage> SearchAsync(bool artists, string q, IReadOnlyDictionary<string, string>? filters = null, string sort = "count",
                                                int page = 1, int seed = 0, CancellationToken ct = default)
    {
        var sb = new StringBuilder($"api/{(artists ? "artists" : "characters")}/search?q={Uri.EscapeDataString(q ?? "")}&sort={Uri.EscapeDataString(sort)}&page={page}");
        if (sort == "random") sb.Append("&seed=").Append(seed);
        foreach (var (k, v) in filters ?? new Dictionary<string, string>())
            if (!string.IsNullOrEmpty(v)) sb.Append('&').Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
        var o = await GetAsync(sb.ToString(), ct);
        var list = new List<AnimaDexEntry>();
        foreach (var r in (o["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var loras = (r["loras"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(l => (l["name"]?.ToString() ?? "", l["url"]?.ToString() ?? "")).ToList();
            list.Add(new AnimaDexEntry(
                r["slug"]?.ToString() ?? "", r["name"]?.ToString() ?? "", artists ? "" : r["copyright_name"]?.ToString() ?? "",
                r["trigger"]?.ToString() ?? "",
                (r["tags"] as JsonArray ?? new JsonArray()).Select(t => t?.ToString() ?? "").Where(t => t != "").ToList(),
                r["count"]?.GetValue<int>() ?? 0, Abs(r["thumb_url"]?.ToString()), Abs(r["img_url"]?.ToString()), r["url"]?.ToString(),
                artists, r["score"] is JsonValue s && s.TryGetValue<double>(out var sc) ? sc : null, loras));
        }
        return new AnimaDexPage(o["total"]?.GetValue<int>() ?? 0, o["page"]?.GetValue<int>() ?? page, o["pages"]?.GetValue<int>() ?? 1, list);
    }

    /// <summary>Text an der Stelle pos in einen Prompt einfügen, mit Kommas sauber abgetrennt. Gibt neuen Text und Cursorposition zurück.</summary>
    public static (string Text, int Caret) InsertIntoPrompt(string prompt, int pos, string insert)
    {
        prompt ??= "";
        pos = Math.Clamp(pos, 0, prompt.Length);
        string before = prompt[..pos].TrimEnd(), after = prompt[pos..].TrimStart();
        if (before.EndsWith(',')) before = before.TrimEnd(',').TrimEnd();
        if (after.StartsWith(',')) after = after.TrimStart(',').TrimStart();
        string left = before == "" ? "" : before + ", ";
        string text = left + insert + (after == "" ? "" : ", " + after);
        return (text, (left + insert).Length);
    }

    public void Dispose() => _http.Dispose();
}
