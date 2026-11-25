namespace PoseStudio.Swarm;

/// <summary>Ausgewählte Trigger-Wörter einer LoRA (gilt für die LoRA überall, unabhängig vom Profil).</summary>
public sealed class LoraTagChoice
{
    /// <summary>Angewählte Tags aus den Metadaten (in deren Reihenfolge).</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>Eigene Trigger-Wörter, kommagetrennt.</summary>
    public string Own { get; set; } = "";

    public bool IsEmpty => Tags.Count == 0 && string.IsNullOrWhiteSpace(Own);

    /// <summary>Alles, was angehängt wird: gewählte Tags, dann eigene Wörter (ohne Doppelte).</summary>
    public List<string> All()
    {
        var list = new List<string>();
        foreach (var t in Tags.Concat(LoraTags.SplitList(Own)))
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
        return list;
    }
}

/// <summary>Zerlegt die Trigger-Angabe aus den LoRA-Metadaten (Civitai „trained words“, oft auch ganze Bildbeschreibungen)
/// in einzelne Tags und längere Beschreibungen.</summary>
public static class LoraTags
{
    /// <summary>Ab so vielen Wörtern gilt ein Stück als Beschreibung statt als Tag.</summary>
    public const int MaxTagWords = 6;

    public static List<string> SplitList(string? text) =>
        (text ?? "").Split(',', '\n', '\r').Select(x => x.Trim()).Where(x => x != "").ToList();

    public static (List<string> Tags, List<string> Descriptions) Parse(string? phrase)
    {
        var tags = new List<string>();
        var descs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (phrase ?? "").Split('\n', '\r'))
        {
            string? desc = null;   // zusammenhängende Satzstücke wieder zu einer Beschreibung verbinden
            foreach (var raw in line.Split(','))
            {
                string part = raw.Trim().Trim('.').Trim();
                // „(blue eyes)“ → „blue eyes“ (Klammern = Gewichtung, in Metadaten meist nur Beiwerk); „\(“ bleibt
                if (part.Length > 2 && part[0] == '(' && part[^1] == ')' && part[^2] != '\\' && part.IndexOf('(', 1) < 0) part = part[1..^1].Trim();
                if (part == "") continue;
                bool isDesc = Words(part) >= MaxTagWords || part.Contains(". ") || part.Contains(": ");
                if (isDesc)
                {
                    desc = desc == null ? part : desc + ", " + part;
                    continue;
                }
                if (desc != null) { AddDesc(descs, desc); desc = null; }
                if (seen.Add(part)) tags.Add(part);
            }
            if (desc != null) AddDesc(descs, desc);
        }
        return (tags, descs);
    }

    private static void AddDesc(List<string> descs, string d)
    {
        if (!descs.Contains(d, StringComparer.OrdinalIgnoreCase)) descs.Add(d);
    }

    private static int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Hängt Tags an einen Prompt an, die noch nicht darin stehen.</summary>
    public static string Append(string prompt, IEnumerable<string> tags)
    {
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t) || Contains(prompt, t)) continue;
            prompt = prompt.TrimEnd().TrimEnd(',');
            prompt = prompt == "" ? t : prompt + ", " + t;
        }
        return prompt;
    }

    /// <summary>Steht der Tag schon als eigenes Stück im Prompt (zwischen Kommas)?</summary>
    public static bool Contains(string prompt, string tag) =>
        prompt.Split(',', '\n').Any(x => x.Trim().Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase));
}
