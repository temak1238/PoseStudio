using System.Text.Json.Nodes;

namespace PoseStudio.Swarm;

/// <summary>Gespeichertes Ergebnis: Bilddatei plus Beschriftung, Seed und gesendete Parameter (für „Nachbessern“).</summary>
public sealed record StoredResult(string Path, string Caption, long Seed, JsonObject? Sent);

/// <summary>Ergebnisliste dauerhaft ablegen: je Bild eine Begleitdatei &lt;bild&gt;.json, die Reihenfolge in results.json.
/// Gleiches Format in Pose Studio und der Tablet-App.</summary>
public static class ResultStore
{
    public const string IndexName = "results.json";

    public static void Save(string imagePath, string caption, long seed, JsonObject? sent)
    {
        try
        {
            var o = new JsonObject { ["caption"] = caption, ["seed"] = seed, ["sent"] = sent?.DeepClone() };
            File.WriteAllText(imagePath + ".json", o.ToJsonString());
        }
        catch { /* Bild bleibt trotzdem in der Liste dieser Sitzung */ }
    }

    public static void SaveIndex(string folder, IEnumerable<string> imagePaths)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var arr = new JsonArray(imagePaths.Select(p => (JsonNode?)JsonValue.Create(System.IO.Path.GetFileName(p))).ToArray());
            File.WriteAllText(System.IO.Path.Combine(folder, IndexName), arr.ToJsonString());
        }
        catch { }
    }

    public static List<StoredResult> Load(string folder)
    {
        var list = new List<StoredResult>();
        try
        {
            string index = System.IO.Path.Combine(folder, IndexName);
            if (!File.Exists(index) || JsonNode.Parse(File.ReadAllText(index)) is not JsonArray arr) return list;
            foreach (var n in arr)
            {
                string path = System.IO.Path.Combine(folder, n?.ToString() ?? "");
                if (!File.Exists(path)) continue;
                string caption = System.IO.Path.GetFileName(path); long seed = -1; JsonObject? sent = null;
                try
                {
                    if (File.Exists(path + ".json") && JsonNode.Parse(File.ReadAllText(path + ".json")) is JsonObject o)
                    {
                        caption = o["caption"]?.ToString() ?? caption;
                        seed = long.TryParse(o["seed"]?.ToString(), out var sd) ? sd : -1;
                        sent = o["sent"] is JsonObject s ? (JsonObject)s.DeepClone() : null;
                    }
                }
                catch { }
                list.Add(new StoredResult(path, caption, seed, sent));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Tatsächlichen Seed aus den Metadaten einer Generierung lesen.</summary>
    public static long? SeedFrom(string? metadata)
    {
        try
        {
            if (metadata != null && JsonNode.Parse(metadata) is JsonObject m && m["sui_image_params"]?["seed"] is JsonNode s
                && long.TryParse(s.ToString(), out var seed)) return seed;
        }
        catch { }
        return null;
    }

    /// <summary>Dateiendung eines Ergebnisses nach seinem Pfad auf dem Server.</summary>
    public static string ExtensionOf(string serverPath) =>
        serverPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : serverPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".png";
}
