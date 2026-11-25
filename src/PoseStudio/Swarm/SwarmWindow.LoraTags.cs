using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PoseStudio.Swarm;

/// <summary>LoRA-Zeilen mit Tag-Auswahl: die Trigger-Angabe aus den Metadaten wird in Chips zerlegt, man wählt die passenden an.</summary>
public partial class SwarmWindow
{
    private readonly HashSet<string> _loraOpen = new(StringComparer.OrdinalIgnoreCase);
    private const int ChipsFirst = 40;   // bei sehr vielen Tags erst so viele zeigen

    private SwarmModel? LoraInfo(string name) => _loras.FirstOrDefault(x => x.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Kurzanzeige unter dem LoRA-Namen: was angehängt wird.</summary>
    private string TagSummary(SwarmLora l)
    {
        if (l.Trigger == false) return "Trigger aus";
        var all = _s.TagChoice(l.Name)?.All() ?? new();
        if (all.Count > 0) return "+ " + string.Join(", ", all);
        var info = LoraInfo(l.Name);
        if (info != null && string.IsNullOrWhiteSpace(info.Trigger)) return "keine Trigger-Wörter in den Metadaten – eigene eintragen";
        return "keine Tags gewählt – zum Auswählen klicken";
    }

    private void BuildLoraRows()
    {
        LoraRows.Children.Clear();
        foreach (var l in _selectedLoras.ToList())
        {
            var name = new TextBlock { Text = LoraInfo(l.Name)?.Short ?? l.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = l.Name };
            bool open = _loraOpen.Contains(l.Name);
            var sum = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand, ToolTip = "Trigger-Wörter dieser LoRA auswählen" };
            void ShowSum()
            {
                sum.Text = TagSummary(l) + (open ? "  ▲" : "  ✎");
                sum.SetResourceReference(TextBlock.ForegroundProperty, l.Trigger != false && (_s.TagChoice(l.Name)?.IsEmpty == false) ? "LinkText" : "MutedText");
                UpdateTriggerPreview();
            }
            ShowSum();
            sum.MouseLeftButtonUp += (_, _) => { if (!_loraOpen.Remove(l.Name)) _loraOpen.Add(l.Name); BuildLoraRows(); };

            var trig = new CheckBox { Content = "Trigger", IsChecked = l.Trigger != false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0),
                                      ToolTip = "Gewählte Trigger-Wörter dieser LoRA anhängen (aus = nichts anhängen, die Auswahl bleibt erhalten)" };
            trig.Click += (_, _) => { l.Trigger = trig.IsChecked == true; BuildLoraRows(); };
            var box = new TextBox { Width = 44, Text = l.Weight.ToString("0.##", CultureInfo.InvariantCulture), ToolTip = "Gewicht", VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Width = 90, Minimum = -1, Maximum = 2, SmallChange = 0.05, LargeChange = 0.1, Value = l.Weight, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            bool sync = false;
            slider.ValueChanged += (_, e) => { if (sync) return; sync = true; l.Weight = Math.Round(e.NewValue, 2); box.Text = l.Weight.ToString("0.##", CultureInfo.InvariantCulture); sync = false; };
            box.LostFocus += (_, _) => { if (sync) return; sync = true; l.Weight = PD(box.Text, l.Weight); slider.Value = l.Weight; sync = false; };
            var del = new Button { Content = "✕", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Entfernen", VerticalAlignment = VerticalAlignment.Center };
            del.Click += (_, _) => _selectedLoras.Remove(l);

            var head = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var c in new FrameworkElement[] { del, box, slider, trig }) { DockPanel.SetDock(c, Dock.Right); head.Children.Add(c); }
            head.Children.Add(new StackPanel { Children = { name, sum }, VerticalAlignment = VerticalAlignment.Center });

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 3) };
            row.Children.Add(head);
            if (open) row.Children.Add(LoraTagEditor(l.Name, ShowSum));
            LoraRows.Children.Add(row);
        }
        UpdateTriggerPreview();
    }

    private void UpdateTriggerPreview()
    {
        _s.Loras = _selectedLoras.ToList();   // Vorschau braucht die aktuelle Liste (gleiche Objekte, ReadUi klont beim Speichern)
        var tags = _s.MainTriggerTags();
        LoraTriggerPreview.Text = tags.Count == 0 ? "" : "Wird an den Prompt angehängt: " + string.Join(", ", tags);
        LoraTriggerPreview.Visibility = tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Auswahl der Trigger-Wörter einer LoRA: Tags als Chips, Beschreibungen getrennt, eigene Wörter, Schnellknöpfe.</summary>
    private FrameworkElement LoraTagEditor(string lora, Action changed)
    {
        var info = LoraInfo(lora);
        var (tags, descs) = LoraTags.Parse(info?.Trigger);
        var choice = _s.TagChoice(lora, true)!;
        // gewählte Tags, die (nicht mehr) in den Metadaten stehen, trotzdem zeigen
        var allTags = choice.Tags.Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase)).Concat(tags).ToList();

        var panel = new StackPanel { Margin = new Thickness(8, 4, 0, 6) };
        var border = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0), Child = panel, Margin = new Thickness(0, 2, 0, 4) };
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        TextBlock Hint(string t)
        {
            var tb = new TextBlock { Text = t, FontSize = 11, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
            return tb;
        }

        if (info == null && allTags.Count == 0)
            panel.Children.Add(Hint("Keine Metadaten – erst mit SwarmUI verbinden. Eigene Trigger-Wörter gehen trotzdem."));
        else if (allTags.Count == 0)
            panel.Children.Add(Hint("In den Metadaten stehen keine Tags."));
        else
            panel.Children.Add(Hint($"Tags ({allTags.Count}) – anklicken zum An- und Abwählen"));

        var wrap = new WrapPanel();
        TextBox? search = null;
        bool showAll = allTags.Count <= ChipsFirst + 10;
        void FillChips()
        {
            wrap.Children.Clear();
            string q = search?.Text.Trim() ?? "";
            var shown = allTags.Where(t => q == "" || t.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            int max = showAll || q != "" ? shown.Count : ChipsFirst;
            foreach (var t in shown.Take(max))
            {
                bool on = choice.Tags.Contains(t, StringComparer.OrdinalIgnoreCase);
                var chip = new ToggleButton { Content = t, IsChecked = on, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 1, 8, 2), FontSize = 11 };
                chip.Click += (_, _) =>
                {
                    if (chip.IsChecked == true) { if (!choice.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) choice.Tags.Add(t); }
                    else choice.Tags.RemoveAll(x => x.Equals(t, StringComparison.OrdinalIgnoreCase));
                    SortChoice(choice, allTags);
                    changed();
                };
                wrap.Children.Add(chip);
            }
            if (shown.Count > max)
            {
                var more = new Button { Content = $"+ {shown.Count - max} weitere …", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 1, 8, 2), FontSize = 11 };
                more.Click += (_, _) => { showAll = true; FillChips(); };
                wrap.Children.Add(more);
            }
        }
        if (allTags.Count > 30)
        {
            search = new TextBox { Margin = new Thickness(0, 0, 0, 4), ToolTip = "Tags durchsuchen" };
            search.TextChanged += (_, _) => FillChips();
            panel.Children.Add(search);
        }
        FillChips();
        panel.Children.Add(wrap);

        if (descs.Count > 0)
        {
            var list = new StackPanel();
            foreach (var d in descs)
            {
                var tb = new TextBlock { Text = d, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
                list.Children.Add(tb);
            }
            panel.Children.Add(new Expander { Header = $"Beschreibungen ({descs.Count}) – werden nie automatisch angehängt", Content = list, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        }

        panel.Children.Add(Hint("Eigene Trigger-Wörter (kommagetrennt)"));
        var own = new TextBox { Text = choice.Own };
        own.LostFocus += (_, _) => { if (own.Text != choice.Own) { choice.Own = own.Text.Trim(); changed(); } };
        own.KeyDown += (_, e) => { if (e.Key == Key.Enter) { choice.Own = own.Text.Trim(); changed(); } };
        panel.Children.Add(own);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        void Quick(string text, Func<List<string>> pick)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 1, 8, 1) };
            b.Click += (_, _) => { choice.Tags = pick(); FillChips(); changed(); };
            buttons.Children.Add(b);
        }
        if (allTags.Count <= 30) Quick("Alle", () => allTags.ToList());
        Quick("Nur erstes", () => allTags.Take(1).ToList());
        Quick("Keine", () => new List<string>());
        panel.Children.Add(buttons);
        return border;
    }

    /// <summary>Für Bildschirmfotos: drei LoRAs mit Auswahl, zwei aufgeklappt (nur im Speicher, wird nicht gespeichert).</summary>
    internal void ShowLorasForShot()
    {
        _selectedLoras.Clear(); _loraOpen.Clear();
        foreach (var n in new[] { "Anima-Clothes/BellyDancerOutfitAnima.safetensors", "Anima-Clothes/LavaDressAnima.safetensors", "Anima-Styles/violet cinematography-000012.safetensors" })
        {
            var id = LoraInfo(n)?.Id ?? LoraInfo(n.Replace(".safetensors", ""))?.Id ?? n;
            _s.LoraTagChoices.Remove(id);
            _selectedLoras.Add(new SwarmLora { Name = id, Weight = 0.8, Trigger = true });
        }
        _s.TagChoice(_selectedLoras[0].Name, true)!.Tags.AddRange(new[] { "arabian clothes", "belly dancer outfit" });
        _s.TagChoice(_selectedLoras[2].Name, true)!.Tags.Add("violet cinematography");
        _loraOpen.Add(_selectedLoras[0].Name); _loraOpen.Add(_selectedLoras[2].Name);
        BuildLoraRows();
        LoraRows.BringIntoView();
    }

    /// <summary>Gewählte Tags in der Reihenfolge der Metadaten halten.</summary>
    private static void SortChoice(LoraTagChoice c, List<string> order) =>
        c.Tags = c.Tags.OrderBy(t => { int i = order.FindIndex(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).ToList();

    /// <summary>Tag-Auswahl in einem kleinen Fenster (für LoRAs in den Refinern).</summary>
    private void OpenTagWindow(string lora, Action after)
    {
        var host = new ContentControl();
        var w = new Window
        {
            Title = "Trigger-Wörter: " + (LoraInfo(lora)?.Short ?? lora), Owner = this, Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) }
        };
        host.Content = LoraTagEditor(lora, after);
        w.ShowDialog();
        after();
    }
}
