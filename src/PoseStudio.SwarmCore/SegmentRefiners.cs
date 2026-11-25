using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PoseStudio.Swarm;

/// <summary>Ein Bereichs-Refiner (SwarmUI „&lt;segment:…&gt;“): erkennt einen Bildbereich (YOLO-Modell oder Textbeschreibung per CLIPSeg)
/// und malt ihn mit eigenem Prompt neu. Beim Senden hängt das Programm alle aktiven Refiner ans Ende des Prompts.</summary>
public sealed class SegmentRefiner
{
    public bool Enabled { get; set; } = true;
    /// <summary>Anzeigename (Vorgabe oder eigener), z. B. „Gesicht“.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ziel wie in SwarmUI: „yolo-face_yolov8m.pt“ oder ein Begriff für CLIPSeg („face“, „hands“ …).</summary>
    public string Target { get; set; } = "";
    public string Prompt { get; set; } = "";
    /// <summary>Kreativität = wie stark der Bereich neu gemalt wird (0–1, SwarmUI-Standard 0,6).</summary>
    public double Creativity { get; set; } = 0.5;
    /// <summary>Erkennungsschwelle: YOLO-Konfidenz (Standard 0,25) bzw. CLIPSeg-Mindestwert (Standard 0,5).</summary>
    public double Threshold { get; set; } = 0.25;
    /// <summary>LoRAs nur für diesen Bereich (SwarmUI wendet ein &lt;lora:…&gt; hinter &lt;segment:…&gt; nur beim Neumalen des Bereichs an).</summary>
    public List<SwarmLora> Loras { get; set; } = new();
    /// <summary>Charakter (Name, Serie, Kopf-Tags) vor den Refiner-Prompt setzen – sonst weiß z. B. der Gesichts-Refiner nicht, wer gemalt wird.</summary>
    public bool WithCharacter { get; set; }

    public SegmentRefiner Clone()
    {
        var c = (SegmentRefiner)MemberwiseClone();
        c.Loras = Loras.Select(l => l.Clone()).ToList();
        return c;
    }
    public bool IsYolo => SegmentRefiners.IsYolo(Target);
}

/// <summary>Vorgabe für einen Refiner: Text-Ziel (CLIPSeg) und Namensteile passender YOLO-Modelle (erstes vorhandenes gewinnt).</summary>
public sealed record SegmentPreset(string Name, string ClipTarget, string[] YoloHints, string Prompt, double Creativity, bool WithCharacter = false);

public static class SegmentRefiners
{
    public const double YoloThreshold = 0.25, ClipThreshold = 0.5;

    public static readonly SegmentPreset[] Presets =
    {
        new("Gesicht", "face", new[] { "anzhc-face", "anzhc face", "face_yolov8m-seg", "face_yolov8", "face" }, "detailed face, beautiful detailed eyes", 0.45, true),
        new("Hände", "hands", new[] { "hand_yolov8s", "hand_yolov9", "hand_yolov8", "hand" }, "detailed hands, perfect hands, five fingers", 0.45),
        new("Augen", "eyes", new[] { "anzhc-eyes", "anzhc eyes", "eyes", "eye" }, "detailed eyes, beautiful eyes, detailed pupils", 0.4, true),
        new("Brust", "breasts", new[] { "anzhc-breasts", "anzhc breasts", "breast" }, "detailed breasts", 0.4),
        new("Haare", "hair", new[] { "anzhc-headhair", "headhair", "hair" }, "detailed hair, shiny hair", 0.4, true),
        new("Füße", "feet", new[] { "feet", "foot" }, "detailed feet, five toes", 0.45),
    };

    public static bool IsYolo(string target) => target.TrimStart().StartsWith("yolo-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Neuer Refiner aus einer Vorgabe: YOLO-Modell, wenn eines passt, sonst Text-Ziel.</summary>
    public static SegmentRefiner FromPreset(SegmentPreset p, IEnumerable<string> yoloModels)
    {
        string? yolo = SuggestYolo(p, yoloModels);
        if (yolo == null && p.ClipTarget == "" && p.YoloHints.Length > 0) yolo = p.YoloHints[0];   // eigene Vorlage mit YOLO-Modell, das (noch) fehlt
        return new SegmentRefiner
        {
            Name = p.Name, Prompt = p.Prompt, Creativity = p.Creativity, WithCharacter = p.WithCharacter,
            Target = yolo != null ? "yolo-" + yolo : p.ClipTarget,
            Threshold = yolo != null ? YoloThreshold : ClipThreshold,
        };
    }

    public static string? SuggestYolo(SegmentPreset p, IEnumerable<string> yoloModels)
    {
        var list = yoloModels.ToList();
        foreach (var hint in p.YoloHints)
            if (list.FirstOrDefault(m => m.Contains(hint, StringComparison.OrdinalIgnoreCase)) is { } m) return m;
        return null;
    }

    /// <summary>Auswahl für das Ziel eines Refiners: passende YOLO-Modelle zuerst, dann alle übrigen, dann das Text-Ziel.</summary>
    public static List<(string Target, string Label)> TargetChoices(string name, string current, IEnumerable<string> yoloModels, IEnumerable<SegmentPreset> presets)
    {
        var res = new List<(string, string)>();
        var preset = presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var models = yoloModels.ToList();
        IEnumerable<string> ordered = models;
        if (preset != null)
            ordered = models.OrderBy(m => { int i = Array.FindIndex(preset.YoloHints, h => m.Contains(h, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; });
        foreach (var m in ordered) res.Add(("yolo-" + m, "YOLO: " + m));
        if (preset != null) res.Add((preset.ClipTarget, $"Text: {preset.ClipTarget} (ohne Modell)"));
        if (current != "" && !res.Any(r => r.Item1.Equals(current, StringComparison.OrdinalIgnoreCase)))
            res.Add((current, IsYolo(current) ? "YOLO: " + current[5..] + " (fehlt auf dem Server)" : "Text: " + current));
        return res;
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Prompt mit angehängten Refinern: „Haupt &lt;segment:ziel,kreativität,schwelle&gt; refiner-prompt …“.</summary>
    public static string Compose(string prompt, IEnumerable<SegmentRefiner>? refiners, IReadOnlyDictionary<string, string?>? triggers = null, string? character = null,
                                 bool legacyTrigger = true)
    {
        var sb = new StringBuilder(prompt.TrimEnd());
        foreach (var r in refiners ?? Enumerable.Empty<SegmentRefiner>())
        {
            if (!r.Enabled || string.IsNullOrWhiteSpace(r.Target)) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("<segment:").Append(r.Target.Trim()).Append(',').Append(Num(Math.Clamp(r.Creativity, 0, 1)))
              .Append(',').Append(Num(Math.Clamp(r.Threshold, 0, 1))).Append('>');
            string rp = r.Prompt.Trim();
            if (r.WithCharacter && !string.IsNullOrWhiteSpace(character)) rp = character.Trim() + (rp == "" ? "" : ", " + rp);
            foreach (var l in r.Loras)
            {
                if (string.IsNullOrWhiteSpace(l.Name)) continue;
                sb.Append(" <lora:").Append(l.Name.Trim()).Append(':').Append(l.Weight.ToString("0.###", CultureInfo.InvariantCulture)).Append('>');
                if (triggers != null && l.UsesTrigger(legacyTrigger) && triggers.TryGetValue(l.Name, out var t) && !string.IsNullOrWhiteSpace(t) && !rp.Contains(t!, StringComparison.OrdinalIgnoreCase))
                    rp = rp.TrimEnd().TrimEnd(',') + (rp == "" ? "" : ", ") + t;
            }
            if (rp != "") sb.Append(' ').Append(rp);
        }
        return sb.ToString();
    }

    private static readonly Regex SegmentTag = new(@"<segment:([^>]*)>", RegexOptions.IgnoreCase);
    private static readonly Regex LoraTag = new(@"<lora:([^>]+?)(?::(-?[0-9.]+))?>", RegexOptions.IgnoreCase);

    /// <summary>„&lt;segment:…&gt;“-Teile aus einem Prompt lösen: Hauptprompt (davor) und je Teil ein Refiner (Text bis zum nächsten Teil).</summary>
    public static (string Main, List<SegmentRefiner> Refiners) Split(string prompt, IEnumerable<SegmentPreset>? presets = null)
    {
        prompt ??= "";
        var matches = SegmentTag.Matches(prompt);
        if (matches.Count == 0) return (prompt, new());
        var list = new List<SegmentRefiner>();
        var all = (presets ?? Presets).ToList();
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            int end = i + 1 < matches.Count ? matches[i + 1].Index : prompt.Length;
            var parts = m.Groups[1].Value.Split(',').Select(x => x.Trim()).ToList();
            var nums = new List<double>();
            while (parts.Count > 1 && nums.Count < 2 && double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            { nums.Insert(0, v); parts.RemoveAt(parts.Count - 1); }
            string target = string.Join(",", parts);
            var r = new SegmentRefiner
            {
                Target = target,
                Prompt = LoraTag.Replace(prompt[(m.Index + m.Length)..end], "").Trim(),
                Loras = LoraTag.Matches(prompt[(m.Index + m.Length)..end]).Select(l => new SwarmLora
                {
                    Name = l.Groups[1].Value.Trim(),
                    Weight = double.TryParse(l.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1,
                }).ToList(),
                Creativity = nums.Count > 0 ? nums[0] : 0.6,
                Threshold = nums.Count > 1 ? nums[1] : IsYolo(target) ? YoloThreshold : ClipThreshold,
            };
            r.Name = GuessName(target, all);
            list.Add(r);
        }
        return (prompt[..matches[0].Index].TrimEnd().TrimEnd(',').TrimEnd(), list);
    }

    /// <summary>Anzeigename zu einem Ziel: passende Vorgabe, sonst das Ziel selbst.</summary>
    public static string GuessName(string target, IEnumerable<SegmentPreset> presets)
    {
        string t = IsYolo(target) ? target[5..] : target;
        foreach (var p in presets)
            if (t.Equals(p.ClipTarget, StringComparison.OrdinalIgnoreCase) || (IsYolo(target) && p.YoloHints.Any(h => t.Contains(h, StringComparison.OrdinalIgnoreCase))))
                return p.Name;
        return IsYolo(target) ? Path.GetFileNameWithoutExtension(t) : t;
    }
}
