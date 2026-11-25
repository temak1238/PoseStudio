using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Bereichs-Refiner (Gesicht, Hände …): Zeilen unter dem Prompt, beim Senden als &lt;segment:…&gt; angehängt.</summary>
public sealed partial class MainPage
{
    private const string CustomSegmentEntry = "Eigener …";
    private const string CustomTargetEntry = "Eigenes Ziel eingeben …";
    private List<string> _yolos = new();
    private readonly VerticalStackLayout _segRows = new() { Spacing = 8 };
    private readonly Picker _segAdd = new() { Title = "Refiner wählen" };
    private Button? _segDelete;

    private View BuildSegmentSection()
    {
        var add = Btn("+ Hinzufügen", () => { AddSegment(); return Task.CompletedTask; });
        _segDelete = Btn("Vorlage löschen", () =>
        {
            if (_segAdd.SelectedItem is string n) { S.CustomSegments.RemoveAll(c => c.Name == n); FillSegmentAdd(); _t.Save(); }
            return Task.CompletedTask;
        });
        _segAdd.SelectedIndexChanged += (_, _) => _segDelete.IsVisible = _segAdd.SelectedItem is string n && S.CustomSegments.Any(c => c.Name == n);
        var hint = new Label
        {
            Text = "Jeder Refiner sucht seinen Bereich im fertigen Bild (YOLO-Modell oder Textbeschreibung) und malt ihn mit eigenem Prompt neu – wird beim Senden ans Ende des Prompts gehängt.",
            TextColor = Ui.Muted, FontSize = 12,
        };
        return Section("Refiner (Gesicht, Hände …)", _segRows, Cols(_segAdd, add, _segDelete), hint);
    }

    private void FillSegmentAdd()
    {
        var names = S.AllSegmentPresets().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        names.Add(CustomSegmentEntry);
        string? keep = _segAdd.SelectedItem as string;
        _segAdd.ItemsSource = names;
        _segAdd.SelectedIndex = keep != null && names.Contains(keep) ? names.IndexOf(keep) : 0;
    }

    private void AddSegment()
    {
        string name = _segAdd.SelectedItem as string ?? CustomSegmentEntry;
        SegmentRefiner r;
        if (name == CustomSegmentEntry)
            r = new SegmentRefiner { Name = "Eigener", Target = "", Creativity = 0.45, Threshold = SegmentRefiners.ClipThreshold };
        else if (S.CustomSegments.FirstOrDefault(c => c.Name == name) is { } custom) { r = custom.Clone(); r.Enabled = true; }
        else r = SegmentRefiners.FromPreset(SegmentRefiners.Presets.First(p => p.Name == name), _yolos);
        S.Segments.Add(r);
        ShowSegments();
    }

    /// <summary>Beim Start: gab es seit dem letzten Mal einen Absturz, Protokoll anzeigen (zum Weitergeben kopierbar).</summary>
    private async Task ShowLastCrashAsync()
    {
        if (CrashLog.Take() is not { } log) return;
        string shortLog = log.Length > 1500 ? log[..1500] + " …" : log;
        if (await DisplayAlertAsync("Pose Studio ist abgestürzt", "Beim letzten Mal ist die App abgestürzt. Protokoll:\n\n" + shortLog, "Kopieren", "Schließen"))
            await Clipboard.Default.SetTextAsync(log);
    }

    private CancellationTokenSource? _segSave;

    /// <summary>Refiner-Änderungen nach kurzer Pause speichern – geht sonst verloren, wenn Android die App beendet.</summary>
    private async void SaveSoon()
    {
        _segSave?.Cancel();
        var c = _segSave = new CancellationTokenSource();
        try { await Task.Delay(1500, c.Token); _t.Save(); } catch (OperationCanceledException) { }
    }

    private void ShowSegments()
    {
        SaveSoon();
        _segRows.Children.Clear();
        foreach (var r in S.Segments.ToList()) _segRows.Children.Add(SegmentRow(r));
        FillSegmentAdd();
    }

    private View SegmentRow(SegmentRefiner r)
    {
        var on = new Switch { IsToggled = r.Enabled, OnColor = Ui.Accent, VerticalOptions = LayoutOptions.Center };
        on.Toggled += (_, e) => { r.Enabled = e.Value; SaveSoon(); };
        var name = new Entry { Text = r.Name, WidthRequest = 120, TextColor = Colors.White, Placeholder = "Name" };
        name.TextChanged += (_, _) => { r.Name = name.Text ?? ""; SaveSoon(); };

        var target = new Picker { TextColor = Colors.White, Title = "Ziel" };
        Slider thr = null!;
        List<(string Target, string Label)> choices = new();
        void FillTargets()
        {
            choices = SegmentRefiners.TargetChoices(r.Name, r.Target, _yolos, S.AllSegmentPresets());
            target.ItemsSource = choices.Select(c => c.Label).Append(CustomTargetEntry).ToList();
            int i = choices.FindIndex(c => c.Target.Equals(r.Target, StringComparison.OrdinalIgnoreCase));
            target.SelectedIndex = i;
        }
        FillTargets();
        target.SelectedIndexChanged += async (_, _) =>
        {
            int i = target.SelectedIndex;
            if (i < 0) return;
            string t;
            if (i >= choices.Count)
            {
                string? typed = await DisplayPromptAsync("Eigenes Ziel", "Englischer Begriff für die Textbeschreibung (z. B. „feet“, „belly“) oder „yolo-datei.pt“ für ein YOLO-Modell.",
                                                         "Übernehmen", "Abbrechen", initialValue: r.IsYolo ? "" : r.Target);
                if (string.IsNullOrWhiteSpace(typed)) { FillTargets(); return; }
                t = typed.Trim();
            }
            else t = choices[i].Target;
            if (t.Equals(r.Target, StringComparison.OrdinalIgnoreCase)) return;
            bool wasYolo = r.IsYolo;
            r.Target = t; SaveSoon();
            if (wasYolo != r.IsYolo && thr != null) thr.Value = r.IsYolo ? SegmentRefiners.YoloThreshold : SegmentRefiners.ClipThreshold;
            if (i >= choices.Count) FillTargets();
        };

        var save = new Button { Text = "Merken", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, Padding = new Thickness(8, 0) };
        save.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Target)) { await DisplayAlertAsync("Refiner", "Zum Merken braucht der Refiner einen Namen und ein Ziel.", "OK"); return; }
            S.CustomSegments.RemoveAll(c => c.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            S.CustomSegments.Add(r.Clone());
            FillSegmentAdd();
            _t.Save();
            await DisplayAlertAsync("Refiner", $"Vorlage „{r.Name}“ gemerkt – sie steht jetzt in der Auswahl.", "OK");
        };
        var del = new Button { Text = "✕", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, WidthRequest = 44, Padding = 0 };
        del.Clicked += (_, _) => { S.Segments.Remove(r); ShowSegments(); };

        var head = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
        head.Add(on, 0); head.Add(name, 1); head.Add(target, 2); head.Add(save, 3); head.Add(del, 4);

        var withChar = new Switch { IsToggled = r.WithCharacter, OnColor = Ui.Accent, VerticalOptions = LayoutOptions.Center };
        withChar.Toggled += (_, e) => { r.WithCharacter = e.Value; SaveSoon(); };
        var charRow = new HorizontalStackLayout { Spacing = 4, Children = { withChar, new Label { Text = "Charakter mitgeben (Name, Serie, Haare/Augen)", TextColor = Ui.Muted, FontSize = 12, VerticalOptions = LayoutOptions.Center } } };

        var prompt = new Entry { Text = r.Prompt, TextColor = Colors.White, Placeholder = "Prompt für diesen Bereich, z. B. detailed face, blush" };
        prompt.TextChanged += (_, _) => { r.Prompt = prompt.Text ?? ""; SaveSoon(); };

        View SliderCell(string label, double value, Action<double> set, out Slider sl)
        {
            var val = new Label { Text = Ui.F(value), TextColor = Colors.White, WidthRequest = 40, VerticalOptions = LayoutOptions.Center };
            var s = new Slider { Minimum = 0, Maximum = 1, Value = value, VerticalOptions = LayoutOptions.Center };
            s.ValueChanged += (_, e) => { double v = Math.Round(e.NewValue * 20) / 20; set(v); val.Text = Ui.F(v); SaveSoon(); };
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 4 };
            g.Add(new Label { Text = label, TextColor = Ui.Muted, VerticalOptions = LayoutOptions.Center }, 0); g.Add(s, 1); g.Add(val, 2);
            sl = s;
            return g;
        }
        var cre = SliderCell("Kreativität", r.Creativity, v => r.Creativity = v, out _);
        var th = SliderCell("Schwelle", r.Threshold, v => r.Threshold = v, out thr);

        return new Border
        {
            Content = new VerticalStackLayout { Spacing = 4, Children = { head, charRow, prompt, SegmentLoras(r), Cols(cre, th) } },
            BackgroundColor = Ui.PanelLight.WithAlpha(0.35f), StrokeThickness = 0, Padding = 8,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
    }

    /// <summary>LoRAs nur für diesen Refiner: Zeilen mit Gewicht-Regler und ✕, „+ LoRA“ öffnet die Kachelauswahl.</summary>
    private View SegmentLoras(SegmentRefiner r)
    {
        var box = new VerticalStackLayout { Spacing = 4 };
        foreach (var l in r.Loras.ToList())
        {
            var info = _loras.FirstOrDefault(x => x.Id.Equals(l.Name, StringComparison.OrdinalIgnoreCase));
            var name = new Label { Text = "LoRA: " + (info?.Short ?? l.Name), TextColor = Colors.White, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center };
            var val = new Label { Text = Ui.F(l.Weight), TextColor = Colors.White, WidthRequest = 40, VerticalOptions = LayoutOptions.Center };
            var slider = new Slider { Minimum = -1, Maximum = 2, Value = l.Weight, WidthRequest = 150, VerticalOptions = LayoutOptions.Center };
            slider.ValueChanged += (_, e) => { l.Weight = Math.Round(e.NewValue * 20) / 20; val.Text = Ui.F(l.Weight); SaveSoon(); };
            var del = new Button { Text = "✕", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, WidthRequest = 44, Padding = 0 };
            del.Clicked += (_, _) => { r.Loras.Remove(l); ShowSegments(); };
            var trig = TriggerToggle(l);
            var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
            g.Add(name, 0); g.Add(slider, 1); g.Add(val, 2); g.Add(trig, 3); g.Add(del, 4);
            box.Add(g);
        }
        var add = new Button { Text = "+ LoRA nur für diesen Bereich", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(10, 0) };
        add.Clicked += async (_, _) =>
        {
            try { await PickSegmentLorasAsync(r); }
            catch (Exception ex) { await DisplayAlertAsync("Fehler", ex.GetType().Name + ": " + ex.Message, "OK"); }   // abgefangen: kein Eintrag im Absturzprotokoll
        };
        box.Add(add);
        return box;
    }

    private async Task PickSegmentLorasAsync(SegmentRefiner r)
    {
        {
            if (_loras.Count == 0) { await DisplayAlertAsync("LoRA", "Noch keine Verbindung zu SwarmUI.", "OK"); return; }
            var tiles = _loras.Select(m => new Tile
            {
                Label = m.Short, Sub = string.IsNullOrWhiteSpace(m.Trigger) ? m.Folder : "Trigger: " + m.Trigger, Image = Ui.Web(m.PreviewUrl), Tag = m,
                Marked = r.Loras.Any(x => x.Name.Equals(m.Id, StringComparison.OrdinalIgnoreCase)),
            }).ToList();
            await TilePickerPage.ShowAsync(Navigation, new TilePickerPage("LoRAs für " + r.Name, tiles, t => ((SwarmModel)t.Tag).Folder, multi: true, onPick: t =>
            {
                var m = (SwarmModel)t.Tag;
                if (!r.Loras.Any(x => x.Name.Equals(m.Id, StringComparison.OrdinalIgnoreCase))) r.Loras.Add(new SwarmLora { Name = m.Id, Weight = 0.8, Trigger = true });
            }));
            ShowSegments();
        }
    }
}
