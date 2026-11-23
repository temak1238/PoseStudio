using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoseStudio.Swarm;

/// <summary>Bereichs-Refiner (Gesicht, Hände …): Zeilen unter dem Prompt, beim Senden als &lt;segment:…&gt; angehängt.</summary>
public partial class SwarmWindow
{
    private const string CustomEntry = "Eigener …";
    /// <summary>YOLO-Modelle auf dem Server (SwarmUI-Parameter yolomodelinternal).</summary>
    private List<string> _yolos = new();

    private void FillSegmentAddCombo()
    {
        var names = _s.AllSegmentPresets().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        names.Add(CustomEntry);
        string? keep = SegmentAddCombo.SelectedItem as string;
        SegmentAddCombo.ItemsSource = names;
        SegmentAddCombo.SelectedItem = keep != null && names.Contains(keep) ? keep : names[0];
    }

    private void SegmentAddCombo_Changed(object sender, SelectionChangedEventArgs e) =>
        SegmentDeleteButton.Visibility = SegmentAddCombo.SelectedItem is string n && _s.CustomSegments.Any(c => c.Name == n) ? Visibility.Visible : Visibility.Collapsed;

    private void SegmentAdd_Click(object sender, RoutedEventArgs e)
    {
        string name = SegmentAddCombo.SelectedItem as string ?? CustomEntry;
        SegmentRefiner r;
        if (name == CustomEntry)
            r = new SegmentRefiner { Name = "Eigener", Target = "", Creativity = 0.45, Threshold = SegmentRefiners.ClipThreshold };
        else if (_s.CustomSegments.FirstOrDefault(c => c.Name == name) is { } custom)
        {
            r = custom.Clone();
            r.Enabled = true;
        }
        else r = SegmentRefiners.FromPreset(SegmentRefiners.Presets.First(p => p.Name == name), _yolos);
        _s.Segments.Add(r);
        BuildSegmentRows();
    }

    private void SegmentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SegmentAddCombo.SelectedItem is not string n) return;
        _s.CustomSegments.RemoveAll(c => c.Name == n);
        FillSegmentAddCombo();
    }

    private void BuildSegmentRows()
    {
        SegmentRows.Children.Clear();
        foreach (var r in _s.Segments) SegmentRows.Children.Add(SegmentRow(r));
        FillSegmentAddCombo();
    }

    private static string N2(double v) => v.ToString("0.00", CultureInfo.CurrentCulture);

    private FrameworkElement SegmentRow(SegmentRefiner r)
    {
        var on = new CheckBox { IsChecked = r.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Refiner beim Senden verwenden" };
        on.Click += (_, _) => r.Enabled = on.IsChecked == true;
        var name = new TextBox { Text = r.Name, Width = 80, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Name (frei wählbar)" };
        name.TextChanged += (_, _) => r.Name = name.Text;
        var target = new ComboBox { IsEditable = true, Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Was erkannt wird: ein YOLO-Modell vom Server (genauer) oder ein englischer Begriff, z. B. „feet“ (Textbeschreibung, ohne Modell)." };
        var choices = SegmentRefiners.TargetChoices(r.Name, r.Target, _yolos, _s.AllSegmentPresets());
        target.ItemsSource = choices.Select(c => c.Label).ToList();
        target.Text = choices.FirstOrDefault(c => c.Target.Equals(r.Target, StringComparison.OrdinalIgnoreCase)).Label ?? r.Target;
        Slider thr = null!; TextBlock thrText = null!;
        void TargetChanged(string text)
        {
            var hit = choices.FirstOrDefault(c => c.Label == text);
            string t = hit.Label != null ? hit.Target : text.Trim();
            if (t.StartsWith("YOLO:", StringComparison.OrdinalIgnoreCase)) t = "yolo-" + t[5..].Trim();
            else if (t.StartsWith("Text:", StringComparison.OrdinalIgnoreCase)) t = t[5..].Trim();
            bool wasYolo = r.IsYolo;
            r.Target = t;
            if (wasYolo != r.IsYolo && thr != null)   // Standardschwelle der Erkennungsart übernehmen
                thr.Value = r.IsYolo ? SegmentRefiners.YoloThreshold : SegmentRefiners.ClipThreshold;
        }
        target.SelectionChanged += (_, _) => { if (target.SelectedItem is string s) TargetChanged(s); };
        target.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => TargetChanged(target.Text)));
        var save = new Button { Content = "Merken", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), ToolTip = "Als eigene Vorlage merken (erscheint dann in der Auswahl unten)" };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Target)) { SegmentHint.Text = "Zum Merken braucht der Refiner einen Namen und ein Ziel."; return; }
            _s.CustomSegments.RemoveAll(c => c.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            _s.CustomSegments.Add(r.Clone());
            FillSegmentAddCombo();
            SegmentHint.Text = $"Vorlage „{r.Name}“ gemerkt.";
        };
        var remove = new Button { Content = "✕", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), ToolTip = "Refiner entfernen" };
        remove.Click += (_, _) => { _s.Segments.Remove(r); BuildSegmentRows(); };

        var head = new DockPanel();
        DockPanel.SetDock(on, Dock.Left); DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right); DockPanel.SetDock(save, Dock.Right);
        head.Children.Add(on); head.Children.Add(name); head.Children.Add(remove); head.Children.Add(save); head.Children.Add(target);

        var prompt = new TextBox { Text = r.Prompt, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Margin = new Thickness(0, 3, 0, 0), MinHeight = 24,
            ToolTip = "Prompt nur für diesen Bereich, z. B. „detailed face, blush“" };
        prompt.TextChanged += (_, _) => r.Prompt = prompt.Text;

        FrameworkElement SliderCell(string label, string tip, double value, Action<double> set, out Slider sl, out TextBlock txt)
        {
            var s = new Slider { Minimum = 0, Maximum = 1, Value = value, SmallChange = 0.05, LargeChange = 0.1, TickFrequency = 0.05, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
            var t = new TextBlock { Text = N2(value), Width = 32, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            s.ValueChanged += (_, e) => { set(e.NewValue); t.Text = N2(e.NewValue); };
            var d = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
            DockPanel.SetDock(l, Dock.Left); DockPanel.SetDock(t, Dock.Right);
            d.Children.Add(l); d.Children.Add(t); d.Children.Add(s);
            sl = s; txt = t;
            return d;
        }
        var cre = SliderCell("Kreativität", "Wie stark der Bereich neu gemalt wird (0 = gar nicht, 1 = komplett neu). Gut: 0,35–0,6.", r.Creativity, v => r.Creativity = v, out _, out _);
        var th = SliderCell("Schwelle", "Wie sicher der Bereich erkannt sein muss. YOLO: Standard 0,25 (niedriger = findet mehr). Text: Standard 0,5.", r.Threshold, v => r.Threshold = v, out thr, out thrText);
        var sliders = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        sliders.ColumnDefinitions.Add(new ColumnDefinition()); sliders.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(th, 1);
        sliders.Children.Add(cre); sliders.Children.Add(th);

        var box = new StackPanel { Children = { head, prompt, sliders } };
        var b = new Border
        {
            Child = box, Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        };
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return b;
    }

    // ------------------------------------------------------------------ Tests

    internal void ShowSegmentsForShot()
    {
        if (_s.Segments.Count == 0)
        {
            _yolos = new() { "face_yolov8m.pt", "hand_yolov8n.pt" };
            _s.Segments.Add(SegmentRefiners.FromPreset(SegmentRefiners.Presets[0], _yolos));
            _s.Segments.Add(SegmentRefiners.FromPreset(SegmentRefiners.Presets.First(p => p.Name == "Füße"), _yolos));
            BuildSegmentRows();
        }
        SegmentRows.BringIntoView();
    }

    /// <summary>Selbsttest: alte Prompts zerlegen, Vorgaben hinzufügen, Prompt beim Senden zusammensetzen. Stellt die Einstellungen danach wieder her.</summary>
    internal string TestSegments()
    {
        var oldSeg = _s.Segments.Select(x => x.Clone()).ToList(); var oldCustom = _s.CustomSegments.Select(x => x.Clone()).ToList();
        string oldPrompt = _s.Prompt, oldText = PromptBox.Text; var oldYolo = _yolos;
        var log = new List<string>();
        try
        {
            _yolos = new() { "face_yolov8m-seg_60.pt", "face_yolov8m.pt", "hand_yolov8n.pt", "anzhc-eyes-seg-hd.pt" };
            _s.Segments.Clear();
            PromptBox.Text = "1girl, smile, <segment:yolo-face_yolov8m.pt,0.5,0.3> detailed face, blush <segment:hands> nice hands";
            ReadUi();
            log.Add($"Zerlegt: Prompt „{PromptBox.Text}“, {SegmentRows.Children.Count} Zeilen: " + string.Join(" / ", _s.Segments.Select(r => $"{r.Name}={r.Target} {r.Creativity}/{r.Threshold} „{r.Prompt}“")));
            SegmentAddCombo.SelectedItem = "Augen"; SegmentAdd_Click(this, new RoutedEventArgs());
            SegmentAddCombo.SelectedItem = "Füße"; SegmentAdd_Click(this, new RoutedEventArgs());
            _s.Segments[1].Enabled = false;
            var p = _s.BuildParams("data:x", 512, 512, null, new Dictionary<string, string?>());
            log.Add($"Gesendet: „{p["prompt"]}“");
            _s.Segments[3].Name = "Mein Fuß"; _s.CustomSegments.Clear(); _s.CustomSegments.Add(_s.Segments[3].Clone());
            FillSegmentAddCombo();
            log.Add($"Eigene Vorlage in Auswahl: {((IEnumerable<string>)SegmentAddCombo.ItemsSource).Contains("Mein Fuß")} | Zeilen {SegmentRows.Children.Count}");
            var (main, list) = SegmentRefiners.Split("a, b <segment:yolo-multi.pt:0,apple:,0.8,0.25> x <segment:face|hair> y");
            log.Add($"Klassenfilter: Haupt „{main}“, {list[0].Target} {list[0].Creativity}/{list[0].Threshold}, {list[1].Target} {list[1].Creativity}/{list[1].Threshold}");
            string expect = "1girl, smile, <segment:yolo-face_yolov8m.pt,0.5,0.3> detailed face, blush <segment:yolo-anzhc-eyes-seg-hd.pt,0.4,0.25> detailed eyes, beautiful eyes, detailed pupils <segment:feet,0.45,0.5> detailed feet, five toes";
            bool ok = p["prompt"]?.ToString() == expect && main == "a, b" && list[0].Target == "yolo-multi.pt:0,apple:" && list[1].Creativity == 0.6;
            log.Add(ok ? "OK" : "FEHLER erwartet: " + expect);
        }
        catch (Exception ex) { log.Add("FEHLER " + ex); }
        finally
        {
            _s.Segments = oldSeg; _s.CustomSegments = oldCustom; _s.Prompt = oldPrompt; PromptBox.Text = oldText; _yolos = oldYolo;
            BuildSegmentRows();
        }
        return string.Join(" | ", log);
    }
}
