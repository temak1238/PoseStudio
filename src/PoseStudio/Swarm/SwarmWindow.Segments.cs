using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoseStudio.Swarm;

/// <summary>Bereichs-Refiner (Gesicht, Hände …): Zeilen unter dem Prompt, beim Senden als &lt;segment:…&gt; angehängt.</summary>
public partial class SwarmWindow
{
    private const string CustomEntry = "Eigener …";
    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <summary>Jede Änderung an den Refinern (Text, Haken, Regler, Auswahl, Hinzufügen/Entfernen, LoRAs) speichert nach kurzer Pause die Einstellungen.</summary>
    private void HookSegmentAutosave()
    {
        void Any(object s, RoutedEventArgs e) => SaveSoon();
        SegmentGroup.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Any));
        SegmentGroup.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(Any));
        SegmentGroup.AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => SaveSoon()));
        Closing += (_, _) => { if (_saveTimer?.IsEnabled == true) { _saveTimer.Stop(); _scene.SaveSettingsNow(); } };
    }

    private void SaveSoon()
    {
        if (_suppress) return;
        if (_saveTimer == null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _scene.SaveSettingsNow(); };
        }
        _saveTimer.Stop(); _saveTimer.Start();
    }
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

    /// <summary>LoRAs nur für diesen Refiner: Chips mit Gewicht und ✕, dazu eine durchsuchbare Auswahl der LoRAs vom Server.</summary>
    private FrameworkElement SegmentLoraPanel(SegmentRefiner r)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        void Fill()
        {
            wrap.Children.Clear();
            wrap.Children.Add(new TextBlock { Text = "LoRA nur hier:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 3) });
            foreach (var l in r.Loras.ToList())
            {
                var info = _loras.FirstOrDefault(x => x.Id.Equals(l.Name, StringComparison.OrdinalIgnoreCase));
                var name = new TextBlock { Text = info?.Short ?? l.Name, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = l.Name };
                var w = new TextBox { Text = l.Weight.ToString("0.##", CultureInfo.InvariantCulture), Width = 38, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Gewicht" };
                w.TextChanged += (_, _) => l.Weight = PD(w.Text, l.Weight);
                var tr = new CheckBox { Content = "Trigger", IsChecked = l.Trigger ?? true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0),
                    ToolTip = "Trigger-Wörter dieser LoRA automatisch in den Bereichs-Prompt" + (string.IsNullOrWhiteSpace(info?.Trigger) ? "" : Environment.NewLine + info!.Trigger) };
                tr.Checked += (_, _) => l.Trigger = true; tr.Unchecked += (_, _) => l.Trigger = false;
                var x = new Button { Content = "✕", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(3, 0, 0, 0) };
                x.Click += (_, _) => { r.Loras.Remove(l); Fill(); };
                var chip = new Border { Padding = new Thickness(5, 2, 3, 2), Margin = new Thickness(0, 0, 4, 3), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                    Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, w, tr, x } } };
                chip.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                wrap.Children.Add(chip);
            }
            var pick = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Width = 190, Margin = new Thickness(0, 0, 4, 3),
                ToolTip = "LoRA nur für diesen Bereich (z. B. ein Detail- oder Augen-LoRA). Namen tippen zum Suchen." };
            pick.ItemsSource = _loras.Select(m => m.Id).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var add = new Button { Content = "+ LoRA", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 0, 3) };
            add.Click += (_, _) =>
            {
                string n = (pick.SelectedItem as string ?? pick.Text).Trim();
                if (n == "") { SegmentHint.Text = _loras.Count == 0 ? "Noch keine Verbindung zu SwarmUI – LoRA-Namen eintippen oder erst verbinden." : "Erst eine LoRA auswählen."; return; }
                if (!r.Loras.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) r.Loras.Add(new SwarmLora { Name = n, Weight = 0.8, Trigger = true });
                Fill();
            };
            wrap.Children.Add(pick); wrap.Children.Add(add);
        }
        Fill();
        return wrap;
    }

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
        var withChar = new CheckBox { Content = "Charakter", IsChecked = r.WithCharacter, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Charakter (Name, Serie, Haare/Augen-Tags) vor den Refiner-Prompt setzen – sinnvoll für Gesicht, Augen, Haare." };
        withChar.Click += (_, _) => r.WithCharacter = withChar.IsChecked == true;
        DockPanel.SetDock(withChar, Dock.Right);
        head.Children.Add(on); head.Children.Add(name); head.Children.Add(remove); head.Children.Add(save); head.Children.Add(withChar); head.Children.Add(target);

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

        var box = new StackPanel { Children = { head, prompt, SegmentLoraPanel(r), sliders } };
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
            string expect = "1girl, smile <segment:yolo-face_yolov8m.pt,0.5,0.3> detailed face, blush <segment:yolo-anzhc-eyes-seg-hd.pt,0.4,0.25> detailed eyes, beautiful eyes, detailed pupils <segment:feet,0.45,0.5> detailed feet, five toes";
            // LoRA nur für den Gesichts-Refiner, mit Trigger-Wort; danach wieder zerlegen
            _s.Segments[0].Loras.Add(new SwarmLora { Name = "Detail/eyes_v2", Weight = 0.7 });
            string withLora = SegmentRefiners.Compose("1girl", _s.Segments.Take(1), new Dictionary<string, string?> { ["Detail/eyes_v2"] = "sparkling eyes" });
            var (m2, l2) = SegmentRefiners.Split(withLora);
            log.Add($"Refiner-LoRA: „{withLora}“ → zurück {l2[0].Loras.Count} LoRA {l2[0].Loras[0].Name}@{l2[0].Loras[0].Weight}, Prompt „{l2[0].Prompt}“");
            bool loraOk = withLora == "1girl <segment:yolo-face_yolov8m.pt,0.5,0.3> <lora:Detail/eyes_v2:0.7> detailed face, blush, sparkling eyes"
                          && l2[0].Loras[0].Name == "Detail/eyes_v2" && l2[0].Loras[0].Weight == 0.7 && l2[0].Prompt == "detailed face, blush, sparkling eyes" && m2 == "1girl";
            {
                // Trigger je LoRA: nur LoRAs mit Haken bekommen ihre Trigger-Wörter; alter gemeinsamer Haken wird übernommen
                var tt = new SwarmSettings { Prompt = "1girl", AutoTrigger = false,
                    Loras = { new SwarmLora { Name = "A" }, new SwarmLora { Name = "B", Trigger = true } } };
                tt.Segments.Add(new SegmentRefiner { Target = "face", Loras = { new SwarmLora { Name = "C", Trigger = false } } });
                tt.MigrateTriggers();
                var trig = new Dictionary<string, string?> { ["A"] = "trig_a", ["B"] = "trig_b", ["C"] = "trig_c" };
                string tp = tt.BuildParams("data:x", 512, 512, null, trig)["prompt"]!.ToString();
                tt.Loras[0].Trigger = true; tt.Segments[0].Loras[0].Trigger = true;
                string tp2 = tt.BuildParams("data:x", 512, 512, null, trig)["prompt"]!.ToString();
                bool trOk = tt.AutoTrigger && !tp.Contains("trig_a") && tp.Contains("trig_b") && !tp.Contains("trig_c")
                            && tp2.Contains("trig_a") && tp2.Contains("trig_c") && new SwarmLora().UsesTrigger(tt.AutoTrigger);
                log.Add($"Trigger je LoRA: „{tp}“ / „{tp2}“{(trOk ? "" : " FEHLER")}");
            }
            // Übernahme aus einem SwarmUI-Bild: LoRA mit Bereich (lorasectionconfinement ≠ -1) gehört zum Refiner, nicht zu den normalen LoRAs
            var imp = new SwarmSettings();
            imp.ImportFrom((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
                """{"prompt":"x","loras":["Background/Dojo","Effekts/Eye_wallpaper"],"loraweights":["0.5","0.8"],"lorasectionconfinement":["-1","11"]}""")!,
                "1girl, smile <segment:face,0.4,0.5> <lora:Effekts/Eye_wallpaper:0.8> detailed eyes");
            bool impOk = imp.Loras.Count == 1 && imp.Loras[0].Name == "Background/Dojo" && imp.Segments.Count == 1 && imp.Segments[0].Loras[0].Name == "Effekts/Eye_wallpaper"
                         && imp.Base?.ContainsKey("lorasectionconfinement") != true && imp.Prompt == "1girl, smile";
            log.Add($"Übernahme: LoRAs {string.Join(",", imp.Loras.Select(x => x.Name))}, Refiner-LoRAs {string.Join(",", imp.Segments.SelectMany(x => x.Loras).Select(x => x.Name))}{(impOk ? "" : " FEHLER")}");
            // Nachbessern „Hintergrund unscharf lassen“: Refiner-Anteil 0, Person als erster Bereich mit Hauptprompt (ohne LoRA-Tags)
            var kb = new SwarmSettings { RefineKeepBackground = true, RefinerControl = 0.4 };
            var sentKb = new System.Text.Json.Nodes.JsonObject { ["prompt"] = "1girl, bokeh, <lora:x:0.5> <segment:face,0.5,0.5> nice face", ["steps"] = 12, ["cfgscale"] = 3.5 };
            var rk = kb.BuildRefine(sentKb, 42, new[] { "face_yolov8m.pt", "person_yolov8s-seg.pt" });
            bool kbOk = rk["prompt"]?.ToString() == "1girl, bokeh, <lora:x:0.5> <segment:yolo-person_yolov8s-seg.pt,0.4,0.25> 1girl, bokeh <segment:face,0.5,0.5> nice face"
                        && rk["refinercontrolpercentage"]?.ToString() == "0" && rk["segmentsteps"]?.GetValue<int>() == 12 && rk["segmentcfgscale"]?.ToString() == "3.5";
            log.Add($"Hintergrund behalten: „{rk["prompt"]}“, Anteil {rk["refinercontrolpercentage"]}, Steps {rk["segmentsteps"]}, CFG {rk["segmentcfgscale"]}{(kbOk ? "" : " FEHLER")}");
            // Charakter + Stil vor dem Prompt; Gesichts-Refiner bekommt Name, Serie und nur die Kopf-Tags; Übernahme trennt alles wieder
            var cs = new SwarmSettings { Character = "frieren, sousou no frieren, white hair, twintails, green eyes, white capelet, earrings", CharacterTrigger = "frieren, sousou no frieren",
                                         Style = "@abe tsukasa", Prompt = "sitting, forest" };
            cs.Segments.Add(SegmentRefiners.FromPreset(SegmentRefiners.Presets[0], new[] { "face_yolov8m.pt" }));
            cs.Segments.Add(SegmentRefiners.FromPreset(SegmentRefiners.Presets[1], new[] { "face_yolov8m.pt" }));
            string cp = cs.BuildParams("data:x", 512, 512, null, new Dictionary<string, string?>())["prompt"]!.ToString();
            string cpExpect = "frieren, sousou no frieren, white hair, twintails, green eyes, white capelet, earrings, @abe tsukasa, sitting, forest "
                            + "<segment:yolo-face_yolov8m.pt,0.45,0.25> frieren, sousou no frieren, white hair, twintails, green eyes, earrings, detailed face, beautiful detailed eyes "
                            + "<segment:hands,0.45,0.5> detailed hands, perfect hands, five fingers";
            var ci = new SwarmSettings { Character = cs.Character, CharacterTrigger = cs.CharacterTrigger, Style = cs.Style };
            ci.ImportPromptsFrom(new System.Text.Json.Nodes.JsonObject { ["prompt"] = cp }, null);
            var cf = new SwarmSettings { Character = cs.Character, Style = cs.Style };
            cf.ImportPromptsFrom(new System.Text.Json.Nodes.JsonObject { ["prompt"] = "1girl, beach" }, null);
            bool chOk = cp == cpExpect && ci.Prompt == "sitting, forest" && ci.Character == cs.Character && ci.Style == cs.Style && ci.Segments.Count == 2
                        && ci.Segments[0].WithCharacter && ci.Segments[0].Prompt == "detailed face, beautiful detailed eyes" && !ci.Segments[1].WithCharacter
                        && cf.Character == "" && cf.Style == "" && cf.Prompt == "1girl, beach";
            log.Add($"Charakter: „{cp}“ | zurück: Prompt „{ci.Prompt}“, Charakter {(ci.Character == cs.Character ? "gleich" : "„" + ci.Character + "“")}, Stil „{ci.Style}“, Gesicht mit Charakter {ci.Segments[0].WithCharacter} „{ci.Segments[0].Prompt}“ | fremdes Bild: Charakter „{cf.Character}“{(chOk ? "" : " FEHLER erwartet " + cpExpect)}");
            // Speichern/Laden: Refiner-LoRAs müssen einen Neustart überstehen (wie AppSettings.Save/Load)
            var st = new AppSettings(); st.Swarm.Segments.Add(SegmentRefiners.FromPreset(SegmentRefiners.Presets[2], Array.Empty<string>()));
            st.Swarm.Segments[0].Loras.Add(new SwarmLora { Name = "Detail/eyes-illustrious-perfect-anime-eyes", Weight = 0.7 });
            st.Swarm.CustomSegments.Add(st.Swarm.Segments[0].Clone());
            string json = System.Text.Json.JsonSerializer.Serialize(st, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
            log.Add($"Speichern/Laden: Refiner {back.Swarm.Segments.Count}, LoRAs {back.Swarm.Segments.FirstOrDefault()?.Loras.Count}, Vorlage-LoRAs {back.Swarm.CustomSegments.FirstOrDefault()?.Loras.Count}, Charakter-Haken {back.Swarm.Segments.FirstOrDefault()?.WithCharacter}");
            bool ok = chOk &&kbOk &&impOk &&loraOk &&p["prompt"]?.ToString() == expect && main == "a, b" && list[0].Target == "yolo-multi.pt:0,apple:" && list[1].Creativity == 0.6;
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

public partial class SwarmWindow
{
    /// <summary>Testmodus: Refiner mit LoRA über die Oberfläche anlegen, Fenster schließen, Einstellungen wie beim Beenden serialisieren,
    /// neu laden und in einem neuen Fenster prüfen. Schreibt nichts nach %APPDATA%.</summary>
    internal static async Task<string> TestSegmentPersistenceAsync(ISwarmScene scene, string url)
    {
        var log = new List<string>();
        var app = new AppSettings();
        app.Swarm.Url = url;
        var w = new SwarmWindow(app.Swarm, scene) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = 0 };
        w.Show(); w.Left = -20000;
        await Task.Delay(6000);   // Verbinden (LoRA-Liste)
        w.SegmentAddCombo.SelectedItem = "Augen"; w.SegmentAdd_Click(w, new RoutedEventArgs());
        var row = (Border)w.SegmentRows.Children[0];
        var combos = FindAll<ComboBox>(row).Where(c => c.IsEditable).ToList();
        var pick = combos.Last();
        var addBtn = FindAll<Button>(row).First(b => (b.Content as string) == "+ LoRA");
        pick.Text = "Detail/eyes-illustrious-perfect-anime-eyes";
        addBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        log.Add($"Nach Hinzufügen: Refiner {app.Swarm.Segments.Count}, LoRAs {app.Swarm.Segments[0].Loras.Count}, LoRA-Liste {w._loras.Count}");
        w.Close();
        log.Add($"Nach Schließen: LoRAs {app.Swarm.Segments.FirstOrDefault()?.Loras.Count}");
        string json = System.Text.Json.JsonSerializer.Serialize(app, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        log.Add($"Nach Laden: Refiner {back.Swarm.Segments.Count}, LoRAs {back.Swarm.Segments.FirstOrDefault()?.Loras.Count}");
        var w2 = new SwarmWindow(back.Swarm, scene) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = 0 };
        w2.Show(); w2.Left = -20000;
        await Task.Delay(6000);
        var row2 = w2.SegmentRows.Children.Count > 0 ? (Border)w2.SegmentRows.Children[0] : null;
        int chips = row2 == null ? -1 : FindAll<TextBlock>(row2).Count(t => (t.ToolTip as string) == "Detail/eyes-illustrious-perfect-anime-eyes");
        log.Add($"Neues Fenster: LoRAs {back.Swarm.Segments.FirstOrDefault()?.Loras.Count}, angezeigt {chips}");
        w2.Close();
        return string.Join("\n", log);
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }
}
