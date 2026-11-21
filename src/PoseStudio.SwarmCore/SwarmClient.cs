using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoseStudio.Swarm;

/// <summary>Modell oder LoRA aus SwarmUI (ListModels).</summary>
public sealed record SwarmModel(string Name, string Title, string? PreviewUrl, string? Trigger, string? Architecture)
{
    /// <summary>Name ohne Endung, so wie SwarmUI ihn in Parametern erwartet.</summary>
    public string Id => Name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ? Name[..^12] : Name;
    public string Folder => Name.Contains('/') ? Name[..Name.LastIndexOf('/')] : "";
    public string Short => string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(Name) : Title;
}

/// <summary>Bild aus der SwarmUI-Historie mit seinen Generierungs-Parametern.</summary>
public sealed record SwarmHistoryImage(string Src, string Url, JsonObject? Params, string? OriginalPrompt)
{
    /// <summary>Kleines Vorschaubild von SwarmUI (JPG, ca. 35 KB statt mehrerer MB) – für Kacheln.</summary>
    public string PreviewUrl => Url.StartsWith("data:") ? Url : Url + (Url.Contains('?') ? "&" : "?") + "preview=true";
}

/// <summary>Fortschritt einer laufenden Generierung.</summary>
public sealed record SwarmProgress(double Overall, double Current, string? PreviewDataUrl, string? Status);

/// <summary>Fertiges Bild einer Generierung: Pfad auf dem Server und Metadaten (JSON).</summary>
public sealed record SwarmResult(string Path, string? Metadata);

/// <summary>Verbindung zu SwarmUI während einer Generierung verloren. <see cref="Started"/>: der Auftrag war schon beim Server
/// (die Bilder entstehen dort evtl. trotzdem und lassen sich aus der Historie nachholen); sonst kam er nie an.</summary>
public sealed class SwarmConnectionException(bool started, Exception inner)
    : Exception((started ? "Verbindung zu SwarmUI unterbrochen: " : "SwarmUI nicht erreichbar: ") + inner.Message, inner)
{
    public bool Started { get; } = started;
}

/// <summary>Schlanker Client für die HTTP/WebSocket-API von SwarmUI (POST /API/&lt;Route&gt; mit JSON, session_id im Body).</summary>
public sealed class SwarmClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _session;
    public string BaseUrl { get; }
    public string UserId { get; private set; } = "local";
    public string? Version { get; private set; }

    public SwarmClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        if (!BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) BaseUrl = "http://" + BaseUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public void Dispose() => _http.Dispose();

    private async Task<string> SessionAsync(CancellationToken ct)
    {
        if (_session != null) return _session;
        var r = await PostRawAsync("GetNewSession", new JsonObject(), ct);
        _session = r["session_id"]?.GetValue<string>() ?? throw new InvalidOperationException("SwarmUI hat keine Sitzung geliefert.");
        UserId = r["user_id"]?.GetValue<string>() ?? "local";
        Version = r["version"]?.GetValue<string>();
        return _session;
    }

    private async Task<JsonObject> PostRawAsync(string route, JsonObject body, CancellationToken ct)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{BaseUrl}/API/{route}", content, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"SwarmUI {route}: HTTP {(int)resp.StatusCode} {text}");
        return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }

    /// <summary>API-Aufruf mit Sitzung; bei abgelaufener Sitzung einmal neu anmelden.</summary>
    private async Task<JsonObject> PostAsync(string route, JsonObject body, CancellationToken ct = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            body["session_id"] = await SessionAsync(ct);
            var r = await PostRawAsync(route, (JsonObject)body.DeepClone(), ct);
            if (r["error_id"]?.GetValue<string>() == "invalid_session_id" && attempt == 0) { _session = null; continue; }
            if (r["error"] is JsonNode err) throw new InvalidOperationException("SwarmUI: " + err);
            return r;
        }
    }

    /// <summary>Verbindung prüfen; liefert die SwarmUI-Version.</summary>
    public async Task<string> ConnectAsync(CancellationToken ct = default)
    {
        _session = null;
        await SessionAsync(ct);
        return Version ?? "?";
    }

    /// <summary>Modelle ("Stable-Diffusion") oder LoRAs ("LoRA") mit Vorschaubild-Adresse und Trigger-Wörtern.</summary>
    /// <summary>SwarmUI die Modell-Ordner neu einlesen lassen (neue Modelle/LoRAs auf der Platte), danach neu auflisten.</summary>
    public async Task TriggerRefreshAsync(CancellationToken ct = default) => await PostAsync("TriggerRefresh", new JsonObject { ["strong"] = true }, ct);

    public async Task<List<SwarmModel>> ListModelsAsync(string subtype, CancellationToken ct = default)
    {
        var r = await PostAsync("ListModels", new JsonObject { ["path"] = "", ["depth"] = 8, ["subtype"] = subtype, ["dataImages"] = false }, ct);
        var list = new List<SwarmModel>();
        foreach (var f in r["files"] as JsonArray ?? new JsonArray())
        {
            if (f is not JsonObject o) continue;
            string name = o["name"]?.GetValue<string>() ?? "";
            string? prev = o["preview_image"]?.GetValue<string>();
            if (prev != null && prev.StartsWith("/")) prev = BaseUrl + prev;
            list.Add(new SwarmModel(name, o["title"]?.GetValue<string>() ?? "", prev, o["trigger_phrase"]?.GetValue<string>(), o["architecture"]?.GetValue<string>()));
        }
        return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Auswahlwerte eines Parameters (z. B. sampler, scheduler, vae, refinerupscalemethod).</summary>
    public async Task<Dictionary<string, List<string>>> ListParamValuesAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var want = ids.ToHashSet();
        var r = await PostAsync("ListT2IParams", new JsonObject(), ct);
        var result = new Dictionary<string, List<string>>();
        foreach (var p in r["list"] as JsonArray ?? new JsonArray())
        {
            if (p is not JsonObject o) continue;
            string id = o["id"]?.GetValue<string>() ?? "";
            if (!want.Contains(id) || o["values"] is not JsonArray vals) continue;
            result[id] = vals.Select(v => v?.ToString() ?? "").ToList();
        }
        return result;
    }

    /// <summary>Neueste Bilder der Historie mit ihren Parametern (sui_image_params) und dem Original-Prompt (mit Wildcards).</summary>
    public async Task<List<SwarmHistoryImage>> ListHistoryAsync(int max, CancellationToken ct = default)
    {
        var r = await PostAsync("ListImages", new JsonObject { ["path"] = "", ["depth"] = 4, ["sortBy"] = "Date", ["sortReverse"] = false }, ct);
        var list = new List<SwarmHistoryImage>();
        foreach (var f in r["files"] as JsonArray ?? new JsonArray())
        {
            if (f is not JsonObject o) continue;
            string src = o["src"]?.GetValue<string>() ?? "";
            if (!src.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !src.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                && !src.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) continue;
            JsonObject? prm = null; string? orig = null;
            try
            {
                if (o["metadata"]?.GetValue<string>() is { } meta && JsonNode.Parse(meta) is JsonObject m)
                {
                    prm = m["sui_image_params"] as JsonObject;
                    orig = m["sui_extra_data"]?["original_prompt"]?.GetValue<string>();
                }
            }
            catch { /* Metadaten sind laut Doku nicht garantiert JSON */ }
            list.Add(new SwarmHistoryImage(src, ImageUrl(src), prm?.DeepClone() as JsonObject, orig));
            if (list.Count >= max) break;
        }
        return list;
    }

    /// <summary>Adresse eines Bildes aus der Historie bzw. eines Generierungs-Ergebnisses.</summary>
    public string ImageUrl(string path)
    {
        if (path.StartsWith("data:") || path.StartsWith("http")) return path;
        path = path.TrimStart('/');
        if (!path.StartsWith("View/", StringComparison.OrdinalIgnoreCase)) path = $"View/{UserId}/{path}";
        return BaseUrl + "/" + string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
    }

    public async Task<byte[]> DownloadAsync(string urlOrPath, CancellationToken ct = default)
    {
        if (urlOrPath.StartsWith("data:"))
            return Convert.FromBase64String(urlOrPath[(urlOrPath.IndexOf(',') + 1)..]);
        return await _http.GetByteArrayAsync(urlOrPath.StartsWith("http") ? urlOrPath : ImageUrl(urlOrPath), ct);
    }

    /// <summary>Bilder generieren (WebSocket mit Fortschritt). Parameter wie in SwarmUI (prompt, model, initimage …).</summary>
    public async Task<List<SwarmResult>> GenerateAsync(JsonObject parameters, IProgress<SwarmProgress>? progress, CancellationToken ct)
    {
        var body = (JsonObject)parameters.DeepClone();
        body["session_id"] = await SessionAsync(ct);
        var wsUrl = new Uri((BaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws") + BaseUrl[BaseUrl.IndexOf(':')..] + "/API/GenerateText2ImageWS");
        using var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(wsUrl, ct);
            await ws.SendAsync(Encoding.UTF8.GetBytes(body.ToJsonString()), WebSocketMessageType.Text, true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new SwarmConnectionException(false, ex); }
        var results = new List<SwarmResult>();
        var buffer = new byte[1 << 16];
        using var msg = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            msg.SetLength(0);
            WebSocketReceiveResult rr;
            do
            {
                try { rr = await ws.ReceiveAsync(buffer, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { throw new SwarmConnectionException(true, ex); }
                if (rr.MessageType == WebSocketMessageType.Close) break;
                msg.Write(buffer, 0, rr.Count);
            } while (!rr.EndOfMessage);
            if (rr.MessageType == WebSocketMessageType.Close) break;
            if (JsonNode.Parse(Encoding.UTF8.GetString(msg.ToArray())) is not JsonObject o) continue;
            if (o["error"] is JsonNode err)
            {
                if (o["error_id"]?.GetValue<string>() == "invalid_session_id") _session = null;
                throw new InvalidOperationException("SwarmUI: " + err);
            }
            if (o["gen_progress"] is JsonObject gp)
                progress?.Report(new SwarmProgress(gp["overall_percent"]?.GetValue<double>() ?? 0, gp["current_percent"]?.GetValue<double>() ?? 0,
                    gp["preview"]?.GetValue<string>(), null));
            if (o["status"] is JsonObject st)
                progress?.Report(new SwarmProgress(0, 0, null, $"Warteschlange: {st["waiting_gens"]}, läuft: {st["live_gens"]}"));
            if (o["image"] is JsonNode img)
            {
                string? path = img is JsonObject io ? io["image"]?.GetValue<string>() : img.GetValue<string>();
                string? meta = img is JsonObject io2 ? io2["metadata"]?.ToString() : null;
                if (path != null) results.Add(new SwarmResult(path, meta));
            }
        }
        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        return results;
    }

    /// <summary>Pfad eines Ergebnisses relativ zum Ausgabeordner des Benutzers (z. B. „raw/2026-11-21/bild.png“).</summary>
    public string RelativePath(string resultPath)
    {
        string p = Uri.UnescapeDataString(resultPath.TrimStart('/'));
        int q = p.IndexOf('?'); if (q >= 0) p = p[..q];
        if (p.StartsWith("View/", StringComparison.OrdinalIgnoreCase)) p = p[5..];
        if (p.StartsWith(UserId + "/", StringComparison.OrdinalIgnoreCase)) p = p[(UserId.Length + 1)..];
        if (p.StartsWith("Starred/", StringComparison.OrdinalIgnoreCase)) p = p[8..];
        return p;
    }

    /// <summary>Stern in SwarmUI umschalten (SwarmUI kopiert das Bild nach „Starred/…“ bzw. entfernt die Kopie). Liefert den neuen Zustand.</summary>
    public async Task<bool> ToggleStarAsync(string resultPath, CancellationToken ct = default)
    {
        var r = await PostAsync("ToggleImageStarred", new JsonObject { ["path"] = RelativePath(resultPath) }, ct);
        return r["new_state"]?.GetValue<bool>() ?? false;
    }

    /// <summary>Alle Bilder mit Stern (relative Pfade wie <see cref="RelativePath"/>).</summary>
    public async Task<HashSet<string>> ListStarredAsync(CancellationToken ct = default)
    {
        var r = await PostAsync("ListImages", new JsonObject { ["path"] = "Starred", ["depth"] = 6, ["sortBy"] = "Date", ["sortReverse"] = false }, ct);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in r["files"] as JsonArray ?? new JsonArray())
            if (f is JsonObject o && o["src"]?.GetValue<string>() is { } src) set.Add(RelativePath(src));
        return set;
    }

    /// <summary>Tatsächlichen Seed eines Ergebnisses aus der Historie lesen (die direkte Rückmeldung enthält nur den angefragten).</summary>
    public async Task<long?> LookupSeedAsync(string resultPath, CancellationToken ct = default)
    {
        string p = Uri.UnescapeDataString(resultPath.TrimStart('/'));
        if (p.StartsWith("View/", StringComparison.OrdinalIgnoreCase)) p = p[5..];
        if (p.StartsWith(UserId + "/", StringComparison.OrdinalIgnoreCase)) p = p[(UserId.Length + 1)..];
        int slash = p.LastIndexOf('/');
        string folder = slash > 0 ? p[..slash] : "", file = p[(slash + 1)..];
        var r = await PostAsync("ListImages", new JsonObject { ["path"] = folder, ["depth"] = 1, ["sortBy"] = "Date", ["sortReverse"] = false }, ct);
        foreach (var f in r["files"] as JsonArray ?? new JsonArray())
        {
            if (f is not JsonObject o || o["src"]?.GetValue<string>() is not { } src || !src.EndsWith(file, StringComparison.Ordinal)) continue;
            try
            {
                if (o["metadata"]?.GetValue<string>() is { } meta && JsonNode.Parse(meta)?["sui_image_params"]?["seed"] is JsonNode s
                    && long.TryParse(s.ToString(), out var seed) && seed >= 0) return seed;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Laufende Generierungen dieser Sitzung abbrechen.</summary>
    public async Task InterruptAsync()
    {
        try { await PostAsync("InterruptAll", new JsonObject { ["other_sessions"] = false }); } catch { }
    }
}
