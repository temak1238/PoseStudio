namespace PoseStudio.Swarm;

/// <summary>Chips für den Charakter: Name und Serie (AnimaDex-Trigger) stehen fest, die Aussehen-Tags lassen sich an- und abwählen.
/// Der Charaktertext bleibt die eine Wahrheit – ein Chip ist „an“, wenn sein Tag als eigenes Stück im Text steht.</summary>
public static class CharacterChips
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    /// <summary>Alle Chips in fester Reihenfolge: Trigger-Teile (fest), angebotene Tags, dann von Hand eingetragene Stücke.</summary>
    public static List<(string Tag, bool On, bool Fixed)> List(string character, string trigger, IEnumerable<string> offered)
    {
        var parts = LoraTags.SplitList(character);
        var res = new List<(string, bool, bool)>();
        var seen = new HashSet<string>(Cmp);
        foreach (var t in LoraTags.SplitList(trigger)) if (seen.Add(t)) res.Add((t, parts.Contains(t, Cmp), true));
        foreach (var t in offered) if (seen.Add(t)) res.Add((t, parts.Contains(t, Cmp), false));
        foreach (var t in parts) if (seen.Add(t)) res.Add((t, true, false));
        return res;
    }

    /// <summary>Kurzanzeige unter dem Charakterfeld: „Aussehen: white hair, twintails (2 von 10)“ bzw. „keine gewählt“.</summary>
    public static string Summary(string character, string trigger, IEnumerable<string> offered)
    {
        var chips = List(character, trigger, offered).Where(c => !c.Fixed).ToList();
        var on = chips.Where(c => c.On).Select(c => c.Tag).ToList();
        return on.Count == 0 ? $"Aussehen-Tags: keine gewählt ({chips.Count} zur Auswahl)"
                             : $"Aussehen: {string.Join(", ", on)} ({on.Count} von {chips.Count})";
    }

    /// <summary>Tag an- bzw. abwählen und den neuen Charaktertext liefern. Abgewählte Tags bleiben als Chip im Angebot.
    /// Angewählte kommen an ihre Stelle in der Chip-Reihenfolge (hinter den nächsten vorhandenen Vorgänger).</summary>
    public static string Toggle(string character, string trigger, List<string> offered, string tag)
    {
        var parts = LoraTags.SplitList(character);
        int i = parts.FindIndex(p => Cmp.Equals(p, tag));
        if (i >= 0)
        {
            parts.RemoveAt(i);
            if (!offered.Contains(tag, Cmp)) offered.Add(tag);
        }
        else
        {
            var order = List(character, trigger, offered).Select(c => c.Tag).ToList();
            int k = order.FindIndex(t => Cmp.Equals(t, tag));
            int at = k < 0 ? parts.Count : 0;
            for (int j = k - 1; j >= 0; j--)
            {
                int p = parts.FindIndex(x => Cmp.Equals(x, order[j]));
                if (p >= 0) { at = p + 1; break; }
            }
            parts.Insert(at, tag);
        }
        return string.Join(", ", parts);
    }
}
