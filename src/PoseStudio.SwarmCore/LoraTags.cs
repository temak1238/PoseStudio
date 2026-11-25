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
            foreach (var raw in Pieces(line))
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

    /// <summary>Stücke einer Zeile: normalerweise kommagetrennt. Manche LoRAs listen ihre Trigger aber ohne Kommas:
    /// mit Tabs bzw. breiten Lücken („m44aaffAgentRionAida   brown_eyes brown_hair   glasses hairband“) oder nur mit Leerzeichen
    /// („m44aaffaikatrans dark-skinned_female green eyes long hair 1980s_(style)“). Dann an den Lücken trennen und Wortgruppen mit
    /// der Danbooru-Liste wieder zu Tags zusammensetzen („green eyes“, „long hair“).</summary>
    private static IEnumerable<string> Pieces(string line)
    {
        if (line.Contains(',')) return line.Split(',');
        bool gaps = System.Text.RegularExpressions.Regex.IsMatch(line, @"\t| {2,}");
        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        bool tagLike = tokens.Count(t => t.Contains('_')) >= 2 && !line.Contains(". ");   // Danbooru-Schreibweise → Tag-Liste, kein Satz
        if (!gaps && !tagLike) return new[] { line };
        if (!gaps) return Cover(tokens, false)!;
        var res = new List<string>();
        foreach (var p in System.Text.RegularExpressions.Regex.Split(line, @"\t+| {2,}"))
        {
            var w = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w.Length > 1 && w.All(x => x.Contains('_'))) res.AddRange(w);
            else if (w.Length > 1 && Cover(w, true) is { } c) res.AddRange(c);   // „glasses hairband“ → glasses, hairband
            else res.Add(p);
        }
        return res;
    }

    /// <summary>Wörter von links nach rechts zu möglichst langen bekannten Tags zusammenfassen (bis 4 Wörter).
    /// full = nur, wenn dabei jedes Stück ein bekannter Tag ist (sonst null); sonst bleiben unbekannte Wörter einzeln.</summary>
    private static List<string>? Cover(string[] words, bool full)
    {
        var db = BooruTags.Default;
        var res = new List<string>();
        for (int i = 0; i < words.Length;)
        {
            int take = 1;
            if (db != null)
                for (int n = Math.Min(4, words.Length - i); n >= 1; n--)
                    if (db.Find(string.Join(" ", words, i, n)) != null) { take = n; break; }
            string piece = string.Join(" ", words, i, take);
            if (full && (db == null || db.Find(piece) == null)) return null;
            res.Add(piece);
            i += take;
        }
        return res;
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
