using System.IO.Compression;
using System.Text;

namespace PoseStudio.Swarm;

/// <summary>Ein Danbooru-Tag: Name (mit Leerzeichen statt Unterstrichen), Kategorie, Anzahl Bilder, Aliase.</summary>
public sealed record BooruTag(string Name, int Category, int Count, string[] Aliases)
{
    public const int General = 0, Copyright = 3, Character = 4, Meta = 5;

    /// <summary>Von Danbooru als veraltet markiert (umbenannt/aufgeteilt) – Modelle kennen ihn aus dem Training meist trotzdem.</summary>
    public bool IsDeprecated => Count == 0;

    public string CategoryName => Category switch { Character => "Charakter", Copyright => "Serie", Meta => "Meta", _ => "Allgemein" };

    /// <summary>„1,2 Mio“, „45.300“ …</summary>
    public string CountText => Count == 0 ? "veraltet" : Count >= 1_000_000 ? (Count / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " Mio"
                             : Count >= 10_000 ? (Count / 1000) + " Tsd." : Count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Text für den Prompt (Klammern maskiert, sonst liest SwarmUI sie als Gewichtung).</summary>
    public string PromptText => AnimaDexClient.EscapeParens(Name);
}

/// <summary>Ein Vorschlag: Tag und ggf. der Alias, über den er gefunden wurde.</summary>
public sealed record BooruSuggestion(BooruTag Tag, string? Via);

/// <summary>Danbooru-Tag-Liste (eingebettet, erzeugt mit tools/booru/Build-TagList.ps1): Suche für die Autovervollständigung,
/// Nachschlagen für die Sortierung der LoRA-Chips.</summary>
public sealed class BooruTags
{
    private static readonly Lazy<BooruTags?> _default = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Die eingebettete Liste (null, wenn sie fehlt oder kaputt ist).</summary>
    public static BooruTags? Default => _default.Value;

    /// <summary>Im Hintergrund laden, damit das erste Tippen nicht wartet.</summary>
    public static void Preload() => Task.Run(() => _ = Default);

    public IReadOnlyList<BooruTag> All => _all;
    private readonly BooruTag[] _all;          // nach Anzahl absteigend
    private readonly string[] _keys;           // Suchschlüssel je Tag (gleiche Reihenfolge)
    private readonly Dictionary<string, BooruTag> _byKey = new();
    private readonly (string Key, string Alias, BooruTag Tag)[] _aliases;

    public BooruTags(IEnumerable<BooruTag> tags)
    {
        _all = tags.OrderByDescending(t => t.Count).ToArray();
        _keys = _all.Select(t => Key(t.Name)).ToArray();
        var al = new List<(string, string, BooruTag)>();
        for (int i = 0; i < _all.Length; i++) _byKey.TryAdd(_keys[i], _all[i]);
        foreach (var t in _all)
            foreach (var a in t.Aliases)
            {
                string k = Key(a);
                if (_byKey.TryAdd(k, t)) al.Add((k, a, t));
            }
        _aliases = al.ToArray();
    }

    /// <summary>Vergleichsschlüssel: klein, Unterstrich = Leerzeichen, Maskierung „\(“ entfernt, Leerraum zusammengefasst.</summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s.Trim())
        {
            char c = ch == '_' ? ' ' : char.ToLowerInvariant(ch);
            if (c == '\\') continue;
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Tag nachschlagen (auch über einen Alias).</summary>
    public BooruTag? Find(string tag) => _byKey.TryGetValue(Key(tag), out var t) ? t : null;

    /// <summary>Vorschläge zu einem Wortanfang: erst Tags, die so anfangen, dann Tags mit einem Wort, das so anfängt, dann Aliase –
    /// jeweils die häufigsten zuerst.</summary>
    public List<BooruSuggestion> Suggest(string prefix, int max = 6)
    {
        string p = Key(prefix);
        var res = new List<BooruSuggestion>();
        if (p.Length < 2) return res;
        var seen = new HashSet<BooruTag>();
        string inner = " " + p;
        for (int i = 0; i < _all.Length && res.Count < max; i++)
            if (_keys[i].StartsWith(p, StringComparison.Ordinal) && seen.Add(_all[i])) res.Add(new(_all[i], null));
        if (res.Count < max)
        {
            var more = new List<BooruSuggestion>();
            for (int i = 0; i < _all.Length && res.Count + more.Count < max; i++)
                if (_keys[i].Contains(inner, StringComparison.Ordinal) && seen.Add(_all[i])) more.Add(new(_all[i], null));
            var alias = _aliases.Where(a => a.Key.StartsWith(p, StringComparison.Ordinal) && !seen.Contains(a.Tag))
                                .OrderByDescending(a => a.Tag.Count).Take(max).ToList();
            // ein Alias, der genau passt, schlägt Treffer mitten im Namen
            foreach (var a in alias.Where(a => a.Key == p)) if (seen.Add(a.Tag)) res.Add(new(a.Tag, a.Alias));
            res.AddRange(more);
            foreach (var a in alias) if (res.Count < max && seen.Add(a.Tag)) res.Add(new(a.Tag, a.Alias));
        }
        return res.Take(max).ToList();
    }

    /// <summary>Liste aus der TSV (name, kategorie, anzahl, aliase).</summary>
    public static BooruTags Parse(TextReader r)
    {
        var list = new List<BooruTag>(100_000);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var f = line.Split('\t');
            if (f.Length < 3 || f[0] == "" || !int.TryParse(f[1], out int cat) || !int.TryParse(f[2], out int n)) continue;
            list.Add(new BooruTag(f[0], cat, n, f.Length > 3 && f[3] != "" ? f[3].Split(',') : Array.Empty<string>()));
        }
        return new BooruTags(list);
    }

    private static BooruTags? LoadEmbedded()
    {
        try
        {
            using var s = typeof(BooruTags).Assembly.GetManifestResourceStream("danbooru-tags.tsv.gz");
            if (s == null) return null;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            return Parse(r);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ Vorschläge beim Tippen

    /// <summary>Was an der Cursorstelle ersetzt würde: Start, Länge bis zum Cursor, ob es ein ganzes Komma-Stück ist (dann kommt
    /// „, “ dahinter) und die Vorschläge. Null, wenn nichts passt (z. B. mitten im Wort, in &lt;lora:…&gt;, zu kurz).</summary>
    public (int Start, bool Whole, List<BooruSuggestion> Items)? SuggestAt(string text, int caret, int max = 6)
    {
        text ??= "";
        if (caret <= 0 || caret > text.Length) return null;
        if (caret < text.Length && char.IsLetterOrDigit(text[caret])) return null;   // mitten im Wort
        if (!char.IsLetterOrDigit(text[caret - 1]) && text[caret - 1] != ')' && text[caret - 1] != '\'') return null;
        int lt = text.LastIndexOf('<', caret - 1), gt = text.LastIndexOf('>', caret - 1);
        if (lt > gt) return null;                                                    // in <lora:…> oder <segment:…>
        int sep = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is ',' or '\n' or '\r' or '>' or '[' or ']' or '|' or '{' or '}' || ((c is '(' or ')') && (i == 0 || text[i - 1] != '\\')))
            { if (c == ')' && i == caret - 1) continue; sep = i; break; }
            if (c == '.' && i + 1 < text.Length && text[i + 1] == ' ') { sep = i; break; }   // Satzende in natürlicher Sprache
        }
        int pieceStart = sep + 1;
        while (pieceStart < caret && char.IsWhiteSpace(text[pieceStart])) pieceStart++;
        // Wortgrenzen im Stück (von hinten bis zu 3 Wörter probieren – längster Treffer gewinnt)
        var starts = new List<int>();
        for (int i = caret - 1; i >= pieceStart; i--)
            if (i == pieceStart || char.IsWhiteSpace(text[i - 1])) { if (!char.IsWhiteSpace(text[i])) starts.Add(i); if (starts.Count == 3) break; }
        for (int k = starts.Count - 1; k >= 0; k--)
        {
            int st = starts[k];
            string part = text[st..caret];
            if (Key(part).Length < 2) continue;
            var items = Suggest(part, max);
            if (k > 0) items = items.Where(x => Key(x.Tag.Name).Contains(' ')).ToList();   // mehrere Wörter: nur passende Mehrwort-Tags
            if (items.Count > 0) return (st, st == pieceStart, items);
        }
        return null;
    }

    /// <summary>Vorschlag übernehmen: Text von start bis caret ersetzen; als ganzes Stück mit „, “ dahinter.</summary>
    public static (string Text, int Caret) Apply(string text, int start, int caret, BooruTag tag, bool wholePiece)
    {
        string ins = tag.PromptText;
        string after = text[caret..];
        bool comma = wholePiece && !after.TrimStart(' ').StartsWith(',');
        string add = comma ? ", " : "";
        if (comma && after.StartsWith(' ')) after = after.TrimStart(' ');
        return (text[..start] + ins + add + after, start + ins.Length + add.Length);
    }

    // ------------------------------------------------------------------ LoRA-Tags ordnen

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "with", "while", "in", "on", "at", "to", "is", "are", "was", "be", "her", "his", "their",
        "its", "other", "from", "by", "as", "for", "that", "this", "which", "who", "it", "into", "onto", "each", "both", "she", "he", "they",
    };

    /// <summary>LoRA-Tags ordnen: eigene Wörter der LoRA (unbekannt – meist die eigentlichen Trigger) zuerst, dann nach Danbooru-Kategorie.
    /// Besteht die Trigger-Angabe auch aus Bildbeschreibungen (descriptions ab 5), kommen unbekannte Satzstücke („while the other“,
    /// ab 5 Wörtern oder mit Füllwörtern) als eigene Gruppe ans Ende – wählbar bleiben sie, denn auch „belle in blue dress“ kann ein
    /// Outfit-Trigger sein. Nur Stücke ohne Buchstaben („2)“, „= =“) landen in Junk.</summary>
    public (List<(string Title, List<string> Tags)> Groups, List<string> Junk) Group(IEnumerable<string> tags, int descriptions = 0)
    {
        var own = new List<string>(); var frag = new List<string>(); var junk = new List<string>();
        var byCat = new Dictionary<int, List<string>>();
        bool captions = descriptions >= 5;
        foreach (var t in tags)
        {
            var b = Find(t);
            if (b == null)
            {
                if (!t.Any(char.IsLetter)) junk.Add(t);
                else if (captions && IsFragment(t)) frag.Add(t);
                else own.Add(t);
                continue;
            }
            int cat = b.Category is BooruTag.Character or BooruTag.Copyright or BooruTag.Meta ? b.Category : BooruTag.General;
            if (!byCat.TryGetValue(cat, out var l)) byCat[cat] = l = new();
            l.Add(t);
        }
        var groups = new List<(string, List<string>)>();
        if (own.Count > 0) groups.Add(("Eigene Wörter der LoRA", own));
        foreach (var (cat, title) in new[] { (BooruTag.Character, "Charakter"), (BooruTag.Copyright, "Serie"), (BooruTag.General, "Danbooru-Tags"), (BooruTag.Meta, "Meta") })
            if (byCat.TryGetValue(cat, out var l)) groups.Add((title, l));
        if (frag.Count > 0) groups.Add(("Satzstücke aus den Bildbeschreibungen", frag));
        return (groups, junk);
    }

    private static bool IsFragment(string t)
    {
        var w = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length >= 5 || (w.Length > 1 && w.Any(StopWords.Contains));
    }

    /// <summary>Kurzinfo für einen Tag (Tooltip): „Allgemein · 1,2 Mio Bilder“ oder null.</summary>
    public string? Info(string tag)
    {
        var b = Find(tag);
        if (b == null) return null;
        string via = Key(b.Name) == Key(tag) ? "" : $" (Alias von „{b.Name}“)";
        return b.IsDeprecated ? $"{b.CategoryName} · bei Danbooru veraltet (umbenannt), Modelle kennen ihn meist noch{via}"
                              : $"{b.CategoryName} · {b.CountText} Bilder{via}";
    }
}
