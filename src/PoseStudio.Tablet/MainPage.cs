using System.Collections.ObjectModel;
using System.Diagnostics;
using Switch = Microsoft.Maui.Controls.Switch;
using System.Text.Json.Nodes;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Hauptseite: links (quer) bzw. unten (hoch) die Einstellungen, daneben Generieren und die Ergebnisse.</summary>
public sealed class MainPage : ContentPage
{
    private readonly TabletSettings _t;
    private SwarmSettings S => _t.Swarm;
    private SwarmClient? _client;
    private CancellationTokenSource? _cts;
    private List<SwarmModel> _models = new(), _loras = new();
    private List<TemplateInfo> _templates = new();
    private readonly ObservableCollection<ResultItem> _results = new();
    private bool _suppress;

    // Bedienelemente
    private readonly Entry _url = new() { Keyboard = Keyboard.Url };
    private readonly Label _server = new() { TextColor = Ui.Muted, FontSize = 12 };
    private readonly Picker _profile = new() { Title = "Profil" };
    private readonly Image _modelImg = new() { WidthRequest = 72, HeightRequest = 72, Aspect = Aspect.AspectFill, BackgroundColor = Ui.PanelLight };
    private readonly Label _modelName = new() { TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
    private readonly VerticalStackLayout _loraRows = new() { Spacing = 4 };
    private readonly Switch _autoTrigger = new();
    private readonly Editor _prompt = new() { HeightRequest = 150, Placeholder = "Prompt" };
    private readonly Editor _negative = new() { HeightRequest = 80, Placeholder = "Negativ-Prompt" };
    private readonly Picker _source = new() { ItemsSource = new[] { "Nur Prompt", "Posen-Vorlage aus Pose Studio", "Eigenes Bild" } };
    private readonly VerticalStackLayout _templateBox = new() { Spacing = 8 }, _imageBox = new() { Spacing = 8 }, _initBox = new() { Spacing = 8 }, _cnBox = new() { Spacing = 8 }, _aspectBox = new() { Spacing = 8 };
    private readonly Image _templateImg = new() { HeightRequest = 180, Aspect = Aspect.AspectFit, BackgroundColor = Ui.PanelLight };
    private readonly Label _templateName = new() { TextColor = Colors.White };
    private readonly Label _folderName = new() { TextColor = Ui.Muted, FontSize = 12 };
    private readonly Label _resultFolderName = new() { TextColor = Ui.Muted, FontSize = 12 };
    private readonly VerticalStackLayout _backupBox = new() { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Picker _look = new() { ItemsSource = TabletTemplate.LookNames };
    private readonly Image _ownImg = new() { HeightRequest = 180, Aspect = Aspect.AspectFit, BackgroundColor = Ui.PanelLight };
    private readonly Switch _useInit = new();
    private readonly Slider _creativity = new() { Minimum = 0, Maximum = 1 };
    private readonly Label _creativityText = new() { TextColor = Colors.White, WidthRequest = 44 };
    private readonly Switch _useCn = new();
    private readonly Picker _cnKind = new() { ItemsSource = new[] { "3D-Pose (anima_3dpose)", "Regionen (anima_regional)" } };
    private readonly Picker _cnModel = new() { Title = "ControlNet-Modell" };
    private readonly Entry _cnStrength = Num(), _cnStart = Num(), _cnEnd = Num();
    private readonly Picker _aspect = new() { ItemsSource = TabletSettings.Aspects.Select(a => a.Name).ToList() };
    private readonly Picker _side = new() { ItemsSource = new[] { "1024", "1152", "1280", "1408", "1536" } };
    private readonly Label _sizeText = new() { TextColor = Ui.Muted, FontSize = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Stepper _count = new() { Minimum = 1, Maximum = 16, Increment = 1 };
    private readonly Label _countText = new() { TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, WidthRequest = 30 };
    private readonly Entry _seed = Num(), _steps = Num(), _cfg = Num();
    private readonly Picker _sampler = new(), _scheduler = new(), _vae = new();
    private readonly Entry _refControl = Num(), _refUpscale = Num(), _refSteps = Num(), _refCfg = Num();
    private readonly Picker _refUpMethod = new(), _refMethod = new();
    private readonly Label _baseText = new() { TextColor = Ui.Muted, FontSize = 12 };
    private readonly Button _generate = new() { Text = "Generieren", FontSize = 18, FontAttributes = FontAttributes.Bold, BackgroundColor = Ui.Accent, TextColor = Colors.White, HeightRequest = 56 };
    private readonly Button _cancel = new() { Text = "Abbrechen", IsEnabled = false, BackgroundColor = Ui.PanelLight, TextColor = Colors.White, HeightRequest = 56 };
    private readonly ProgressBar _progress = new() { ProgressColor = Ui.Accent };
    private readonly Label _status = new() { TextColor = Ui.Muted, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
    private readonly CollectionView _resultList = new() { SelectionMode = SelectionMode.None };
    private readonly Grid _root = new();
    private readonly ScrollView _settingsView;
    private readonly Grid _resultsView;

    private static Entry Num() => new() { Keyboard = Keyboard.Numeric, HorizontalTextAlignment = TextAlignment.End };

    public MainPage()
    {
        _t = TabletSettings.Load();
        BackgroundColor = Ui.Page;
        NavigationPage.SetHasNavigationBar(this, false);
        _settingsView = new ScrollView { Content = BuildSettings(), Padding = new Thickness(10, 10, 6, 10) };
        _resultsView = BuildResults();
        Content = _root;
        ShowSettings();
        RefreshProfiles();
        LoadResults();
        ShowResultFolder();
        Loaded += async (_, _) => { await ConnectAsync(); await LoadTemplatesAsync(false); await SyncResultsAsync(merge: true, report: false); };
    }

    // ------------------------------------------------------------------ Aufbau

    private static View Header(string text) => new Label { Text = text, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, Margin = new Thickness(0, 0, 0, 2) };

    private static Border Section(string title, params View[] content)
    {
        var s = new VerticalStackLayout { Spacing = 8 };
        s.Add(Header(title));
        foreach (var v in content) s.Add(v);
        return new Border { Content = s, BackgroundColor = Ui.Panel, StrokeThickness = 0, Padding = 12, Margin = new Thickness(0, 0, 0, 10), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 } };
    }

    private static Grid Row(string label, View view, View? extra = null)
    {
        var g = new Grid { ColumnDefinitions = { new(new GridLength(150)), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        g.Add(new Label { Text = label, TextColor = Ui.Muted, VerticalOptions = LayoutOptions.Center }, 0);
        g.Add(view, 1);
        if (extra != null) g.Add(extra, 2);
        return g;
    }

    private static Grid Cols(params View[] views)
    {
        var g = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < views.Length; i++) { g.ColumnDefinitions.Add(new(GridLength.Star)); g.Add(views[i], i); }
        return g;
    }

    private static Button Btn(string text, Func<Task> act, bool accent = false)
    {
        var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White };
        b.Clicked += async (_, _) => { try { await act(); } catch (Exception ex) { Debug.WriteLine(ex); } };
        return b;
    }

    private View BuildSettings()
    {
        var connect = Btn("Verbinden", ConnectAsync);
        var saveProfile = Btn("Speichern", SaveProfileAsync);
        var delProfile = Btn("Löschen", DeleteProfileAsync);
        _profile.SelectedIndexChanged += (_, _) => ProfileChanged();

        var modelTile = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10 };
        modelTile.Add(_modelImg, 0); modelTile.Add(_modelName, 1);
        var modelTap = new TapGestureRecognizer(); modelTap.Tapped += async (_, _) => await PickModelAsync();
        modelTile.GestureRecognizers.Add(modelTap);

        _creativity.ValueChanged += (_, e) => _creativityText.Text = Ui.F(Math.Round(e.NewValue, 2));
        _count.ValueChanged += (_, e) => _countText.Text = ((int)e.NewValue).ToString();
        _source.SelectedIndexChanged += (_, _) => { if (!_suppress) { _t.Source = _source.SelectedIndex; UpdateSourceUi(); } };
        _aspect.SelectedIndexChanged += (_, _) => { if (!_suppress) { _t.Aspect = Math.Max(0, _aspect.SelectedIndex); UpdateSizeText(); } };
        _side.SelectedIndexChanged += (_, _) => { if (!_suppress) UpdateSizeText(); };
        _look.SelectedIndexChanged += (_, _) => { if (!_suppress) { S.Look = _look.SelectedIndex; ShowTemplate(); } };
        _useInit.Toggled += (_, _) => _creativity.IsEnabled = _useInit.IsToggled;

        var dice = Btn("🎲", () => { _seed.Text = "-1"; return Task.CompletedTask; });
        var templateButtons = Cols(Btn("Vorlage wählen …", PickTemplateAsync, true), Btn("Ordner …", PickFolderAsync), Btn("Neu laden", () => LoadTemplatesAsync(true)));
        _templateBox.Children.Add(_templateImg); _templateBox.Children.Add(_templateName); _templateBox.Children.Add(templateButtons); _templateBox.Children.Add(_folderName);
        _templateBox.Children.Add(Row("Darstellung", _look));
        _imageBox.Children.Add(_ownImg); _imageBox.Children.Add(Btn("Bild wählen …", PickImageAsync, true));
        _initBox.Children.Add(Row("Als Init Image", _useInit));
        var crea = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; crea.Add(_creativity, 0); crea.Add(_creativityText, 1);
        _initBox.Children.Add(Row("Creativity", crea));
        _cnBox.Children.Add(Row("ControlNet", _useCn));
        _cnBox.Children.Add(Row("Art", _cnKind));
        _cnBox.Children.Add(Row("Modell", _cnModel));
        _cnBox.Children.Add(Row("Stärke / Start / Ende", Cols(_cnStrength, _cnStart, _cnEnd)));
        _aspectBox.Children.Add(Row("Seitenverhältnis", _aspect));

        var countRow = new HorizontalStackLayout { Spacing = 8, Children = { _countText, _count } };

        return new VerticalStackLayout
        {
            Children =
            {
                Section("Server und Profil",
                    Row("SwarmUI", _url, connect), _server,
                    Row("Profil", _profile), Cols(saveProfile, delProfile, Btn("Aus Historie …", PickHistoryAsync)), _baseText),
                Section("Modell", modelTile, Btn("Modell wählen …", PickModelAsync)),
                Section("LoRAs", _loraRows, Btn("LoRA hinzufügen …", PickLorasAsync), Row("Trigger-Wörter automatisch", _autoTrigger)),
                Section("Prompt", _prompt, _negative),
                Section("Vorlage", Row("Quelle", _source), _templateBox, _imageBox, _initBox, _cnBox, _aspectBox),
                Section("Generierung",
                    Row("Seitenlänge", _side, _sizeText), Row("Anzahl", countRow), Row("Seed (-1 = zufällig)", _seed, dice),
                    Row("Steps / CFG (0 = Standard)", Cols(_steps, _cfg)), Row("Sampler", _sampler), Row("Scheduler", _scheduler), Row("VAE", _vae)),
                Section("Nachbessern (Refiner)",
                    Row("Refiner-Anteil / Faktor", Cols(_refControl, _refUpscale)), Row("Hochskalieren mit", _refUpMethod), Row("Methode", _refMethod),
                    Row("Steps / CFG (0 = wie oben)", Cols(_refSteps, _refCfg))),
                Section("Ablage und Sicherung", _resultFolderName, Btn("Ergebnis-Ordner …", PickResultFolderAsync), BuildBackupBox()),
            }
        };
    }

    private Grid BuildResults()
    {
        _generate.Clicked += async (_, _) => await GenerateAsync();
        _cancel.Clicked += async (_, _) => { if (_client != null) await _client.InterruptAsync(); _cts?.Cancel(); };
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(140)) }, ColumnSpacing = 8 };
        top.Add(_generate, 0); top.Add(_cancel, 1);

        _resultList.ItemsSource = _results;
        _resultList.ItemTemplate = new DataTemplate(() =>
        {
            var img = new Image { Aspect = Aspect.AspectFit, HeightRequest = 300, BackgroundColor = Colors.Black };
            img.SetBinding(Image.SourceProperty, nameof(ResultItem.Image));
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, _) => { if (((BindableObject)s!).BindingContext is ResultItem r && r.Done) await OpenResultAsync(r); };
            img.GestureRecognizers.Add(tap);
            var cap = new Label { FontSize = 12, TextColor = Ui.Muted, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            cap.SetBinding(Label.TextProperty, nameof(ResultItem.Caption));
            var refine = new Button { Text = "Nachbessern", BackgroundColor = Ui.Accent, TextColor = Colors.White, FontSize = 13, Padding = new Thickness(8, 4) };
            refine.SetBinding(IsEnabledProperty, nameof(ResultItem.Done));
            refine.Clicked += async (s, _) => { if (((BindableObject)s!).BindingContext is ResultItem r) await RefineAsync(r); };
            var seed = new Button { Text = "Seed", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, FontSize = 13, Padding = new Thickness(8, 4) };
            seed.SetBinding(IsEnabledProperty, nameof(ResultItem.Done));
            seed.Clicked += (s, _) => { if (((BindableObject)s!).BindingContext is ResultItem r) { _seed.Text = r.Seed.ToString(); _status.Text = $"Seed {r.Seed} übernommen."; } };
            var buttons = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
            buttons.Add(refine, 0); buttons.Add(seed, 1);
            var g = new VerticalStackLayout { Spacing = 4, Children = { img, cap, buttons } };
            return new Border { Content = g, BackgroundColor = Ui.Panel, StrokeThickness = 0, Padding = 6, Margin = 4 };
        });

        var clear = Btn("Liste leeren", ClearResultsAsync);
        var bottom = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        bottom.Add(new Label { Text = "Antippen: groß anzeigen, teilen, in die Galerie", TextColor = Ui.Muted, FontSize = 12, VerticalOptions = LayoutOptions.Center }, 0);
        bottom.Add(clear, 1);

        var g = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 6, Padding = new Thickness(6, 10, 10, 10) };
        g.Add(top, 0, 0); g.Add(_progress, 0, 1); g.Add(_status, 0, 2); g.Add(_resultList, 0, 3); g.Add(bottom, 0, 4);
        return g;
    }

    private bool? _landscape;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;
        bool landscape = width > height;
        int span = Math.Max(1, (int)((landscape ? width - 500 : width) / 330));
        if (_resultList.ItemsLayout is not GridItemsLayout gl || gl.Span != span)
            _resultList.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical);
        if (_landscape == landscape) return;
        _landscape = landscape;
        _root.Children.Clear(); _root.RowDefinitions.Clear(); _root.ColumnDefinitions.Clear();
        Grid.SetRow(_settingsView, 0); Grid.SetColumn(_settingsView, 0); Grid.SetRow(_resultsView, 0); Grid.SetColumn(_resultsView, 0);
        if (landscape)
        {
            _root.ColumnDefinitions.Add(new(new GridLength(500)));
            _root.ColumnDefinitions.Add(new(GridLength.Star));
            _root.Add(_settingsView, 0, 0); _root.Add(_resultsView, 1, 0);
        }
        else
        {
            _root.RowDefinitions.Add(new(new GridLength(0.45, GridUnitType.Star)));
            _root.RowDefinitions.Add(new(new GridLength(0.55, GridUnitType.Star)));
            _root.Add(_resultsView, 0, 0); _root.Add(_settingsView, 0, 1);
        }
    }

    // ------------------------------------------------------------------ Einstellungen ↔ Oberfläche

    private static void SetItems(Picker p, IEnumerable<string>? items, string current, string empty = "(Standard)")
    {
        var list = new List<string> { empty };
        if (items != null) list.AddRange(items.Where(x => x != ""));
        if (current != "" && !list.Contains(current)) list.Add(current);
        p.ItemsSource = list;
        p.SelectedIndex = current == "" ? 0 : list.IndexOf(current);
    }

    private static string GetItem(Picker p) => p.SelectedIndex > 0 && p.SelectedItem is string s ? s : "";

    private void ShowSettings()
    {
        _suppress = true;
        _url.Text = S.Url;
        _prompt.Text = S.Prompt;
        _negative.Text = S.Negative;
        _autoTrigger.IsToggled = S.AutoTrigger;
        _source.SelectedIndex = Math.Clamp(_t.Source, 0, 2);
        _look.SelectedIndex = Math.Clamp(S.Look < 0 ? 1 : S.Look, 0, TabletTemplate.LookNames.Length - 1);
        _useInit.IsToggled = S.UseInitImage;
        _creativity.Value = S.Creativity; _creativityText.Text = Ui.F(Math.Round(S.Creativity, 2));
        _useCn.IsToggled = S.UseControlNet;
        _cnKind.SelectedIndex = S.ControlRegions ? 1 : 0;
        SetItems(_cnModel, _cnModel.ItemsSource?.Cast<string>().Skip(1), S.ControlModel, "(keins)");
        _cnStrength.Text = Ui.F(S.ControlStrength); _cnStart.Text = Ui.F(S.ControlStart); _cnEnd.Text = Ui.F(S.ControlEnd);
        _aspect.SelectedIndex = Math.Clamp(_t.Aspect, 0, TabletSettings.Aspects.Length - 1);
        var sides = _side.ItemsSource.Cast<string>().ToList();
        string side = S.SideLength.ToString();
        if (!sides.Contains(side)) { sides.Add(side); _side.ItemsSource = sides; }
        _side.SelectedIndex = sides.IndexOf(side);
        _count.Value = Math.Clamp(S.Count, 1, 16); _countText.Text = ((int)_count.Value).ToString();
        _seed.Text = S.Seed.ToString(); _steps.Text = S.Steps.ToString(); _cfg.Text = Ui.F(S.Cfg);
        SetItems(_sampler, _sampler.ItemsSource?.Cast<string>().Skip(1), S.Sampler);
        SetItems(_scheduler, _scheduler.ItemsSource?.Cast<string>().Skip(1), S.Scheduler);
        SetItems(_vae, _vae.ItemsSource?.Cast<string>().Skip(1), S.Vae);
        _refControl.Text = Ui.F(S.RefinerControl); _refUpscale.Text = Ui.F(S.RefinerUpscale);
        SetItems(_refUpMethod, _refUpMethod.ItemsSource?.Cast<string>().Skip(1), S.RefinerUpscaleMethod);
        SetItems(_refMethod, _refMethod.ItemsSource?.Cast<string>().Skip(1), S.RefinerMethod);
        _refSteps.Text = S.RefinerSteps.ToString(); _refCfg.Text = Ui.F(S.RefinerCfg);
        _baseText.Text = S.Base is { Count: > 0 } b ? $"Weitere übernommene Einstellungen: {b.Count} ({string.Join(", ", b.Select(x => x.Key).Take(6))}{(b.Count > 6 ? " …" : "")})" : "";
        _suppress = false;
        ShowLoras();
        ShowModel();
        UpdateSourceUi();
    }

    private void ReadUi()
    {
        S.Url = _url.Text?.Trim() ?? S.Url;
        S.Prompt = _prompt.Text ?? "";
        S.Negative = _negative.Text ?? "";
        S.AutoTrigger = _autoTrigger.IsToggled;
        _t.Source = Math.Max(0, _source.SelectedIndex);
        S.Look = Math.Max(0, _look.SelectedIndex);
        S.UseInitImage = _useInit.IsToggled;
        S.Creativity = Math.Round(_creativity.Value, 2);
        S.UseControlNet = _useCn.IsToggled;
        S.ControlRegions = _cnKind.SelectedIndex == 1;
        S.ControlModel = GetItem(_cnModel);
        S.ControlStrength = Ui.PD(_cnStrength.Text, 1); S.ControlStart = Ui.PD(_cnStart.Text, 0); S.ControlEnd = Ui.PD(_cnEnd.Text, 1);
        _t.Aspect = Math.Max(0, _aspect.SelectedIndex);
        S.SideLength = Math.Clamp((int)Ui.PD(_side.SelectedItem as string, S.SideLength), 256, 4096);
        S.Count = (int)_count.Value;
        S.Seed = long.TryParse(_seed.Text?.Trim(), out var seed) ? seed : -1;
        S.Steps = Math.Max(0, (int)Ui.PD(_steps.Text, 0)); S.Cfg = Math.Max(0, Ui.PD(_cfg.Text, 0));
        S.Sampler = GetItem(_sampler); S.Scheduler = GetItem(_scheduler); S.Vae = GetItem(_vae);
        S.RefinerControl = Ui.PD(_refControl.Text, 0.1); S.RefinerUpscale = Ui.PD(_refUpscale.Text, 2);
        S.RefinerUpscaleMethod = GetItem(_refUpMethod); S.RefinerMethod = GetItem(_refMethod);
        S.RefinerSteps = Math.Max(0, (int)Ui.PD(_refSteps.Text, 0)); S.RefinerCfg = Math.Max(0, Ui.PD(_refCfg.Text, 0));
    }

    public void SaveState()
    {
        ReadUi();
        _t.Save();
    }

    protected override void OnDisappearing() { base.OnDisappearing(); SaveState(); }

    private void UpdateSourceUi()
    {
        int src = Math.Max(0, _source.SelectedIndex);
        _templateBox.IsVisible = src == 1;
        _imageBox.IsVisible = src == 2;
        _initBox.IsVisible = src != 0;
        _cnBox.IsVisible = src == 1;
        _aspectBox.IsVisible = src == 0;
        if (src == 1) ShowTemplate();
        if (src == 2) _ownImg.Source = _t.ImagePath != null && File.Exists(_t.ImagePath) ? ImageSource.FromFile(_t.ImagePath) : null;
        UpdateSizeText();
    }

    /// <summary>Seitenverhältnis: aus der Vorlage bzw. dem eigenen Bild, sonst aus der Auswahl.</summary>
    private double CurrentAspect()
    {
        try
        {
            int src = Math.Max(0, _source.SelectedIndex);
            byte[]? data = src == 1 && CurrentTemplate() is { } t && (InitUri(t) ?? t.Files.GetValueOrDefault(TabletTemplate.PoseFile)) is { } u ? AndroidStore.Read(u)
                : src == 2 && _t.ImagePath != null && File.Exists(_t.ImagePath) ? File.ReadAllBytes(_t.ImagePath) : null;
            if (data != null && AndroidStore.ImageSize(data) is var (w, h) && w > 0 && h > 0) return (double)w / h;
        }
        catch { }
        return TabletSettings.Aspects[Math.Clamp(_aspect.SelectedIndex, 0, TabletSettings.Aspects.Length - 1)].Value;
    }

    private void UpdateSizeText()
    {
        var probe = new SwarmSettings { SideLength = Math.Clamp((int)Ui.PD(_side.SelectedItem as string, S.SideLength), 256, 4096) };
        var (w, h) = probe.SizeFor(CurrentAspect());
        _sizeText.Text = $"→ {w} × {h}";
    }

    private void ShowModel()
    {
        var m = FindModel(S.Model);
        _modelName.Text = string.IsNullOrWhiteSpace(S.Model) ? "(Standardmodell von SwarmUI)" : m != null ? Path.GetFileNameWithoutExtension(m.Name) : S.Model;
        _modelImg.Source = Ui.Web(m?.PreviewUrl);
    }

    private SwarmModel? FindModel(string id) => _models.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(id, StringComparison.OrdinalIgnoreCase)
                                                                             || Path.GetFileNameWithoutExtension(x.Name).Equals(id, StringComparison.OrdinalIgnoreCase));

    private void ShowLoras()
    {
        _loraRows.Children.Clear();
        if (S.Loras.Count == 0) _loraRows.Children.Add(new Label { Text = "Keine LoRAs.", TextColor = Ui.Muted });
        foreach (var l in S.Loras.ToList())
        {
            var info = _loras.FirstOrDefault(x => x.Id.Equals(l.Name, StringComparison.OrdinalIgnoreCase));
            var img = new Image { WidthRequest = 44, HeightRequest = 44, Aspect = Aspect.AspectFill, Source = Ui.Web(info?.PreviewUrl), BackgroundColor = Ui.PanelLight };
            var name = new Label { Text = info?.Short ?? l.Name, TextColor = Colors.White, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2, VerticalOptions = LayoutOptions.Center };
            var val = new Label { Text = Ui.F(l.Weight), TextColor = Colors.White, WidthRequest = 40, VerticalOptions = LayoutOptions.Center };
            var slider = new Slider { Minimum = -1, Maximum = 2, Value = l.Weight, WidthRequest = 150, VerticalOptions = LayoutOptions.Center };
            slider.ValueChanged += (_, e) => { l.Weight = Math.Round(e.NewValue * 20) / 20; val.Text = Ui.F(l.Weight); };
            var del = new Button { Text = "✕", BackgroundColor = Ui.PanelLight, TextColor = Colors.White, WidthRequest = 44, Padding = 0 };
            del.Clicked += (_, _) => { S.Loras.Remove(l); ShowLoras(); };
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
            g.Add(img, 0); g.Add(name, 1); g.Add(slider, 2); g.Add(val, 3); g.Add(del, 4);
            _loraRows.Children.Add(g);
        }
    }

    // ------------------------------------------------------------------ Verbindung, Auswahl

    private async Task ConnectAsync()
    {
        ReadUi();
        _client?.Dispose();
        _client = new SwarmClient(S.Url);
        _server.Text = "Verbinde …";
        try
        {
            string ver = await _client.ConnectAsync();
            _models = await _client.ListModelsAsync("Stable-Diffusion");
            _loras = await _client.ListModelsAsync("LoRA");
            var vaes = await _client.ListModelsAsync("VAE");
            var cns = await _client.ListModelsAsync("ControlNet");
            var values = await _client.ListParamValuesAsync(new[] { "sampler", "scheduler", "refinerupscalemethod", "refinermethod" });
            _server.Text = $"Verbunden mit SwarmUI {ver}: {_models.Count} Modelle, {_loras.Count} LoRAs.";
            ReadUi();
            SetItems(_sampler, values.GetValueOrDefault("sampler"), S.Sampler);
            SetItems(_scheduler, values.GetValueOrDefault("scheduler"), S.Scheduler);
            SetItems(_refUpMethod, values.GetValueOrDefault("refinerupscalemethod"), S.RefinerUpscaleMethod);
            SetItems(_refMethod, values.GetValueOrDefault("refinermethod"), S.RefinerMethod);
            SetItems(_vae, vaes.Select(v => v.Id), S.Vae);
            SetItems(_cnModel, cns.Select(v => v.Id), S.ControlModel, "(keins)");
            if (S.ControlModel == "" && cns.FirstOrDefault(c => c.Id.Contains("3dpose", StringComparison.OrdinalIgnoreCase)) is { } pose)
                SetItems(_cnModel, cns.Select(v => v.Id), pose.Id, "(keins)");
            ShowModel();
            ShowLoras();
        }
        catch (Exception ex)
        {
            _server.Text = "Keine Verbindung: " + ex.Message;
        }
    }

    private async Task PickModelAsync()
    {
        if (_models.Count == 0) { await DisplayAlertAsync("Modell", "Noch keine Verbindung zu SwarmUI.", "OK"); return; }
        var tiles = _models.Select(m => new Tile { Label = Path.GetFileNameWithoutExtension(m.Name), Sub = m.Title, Image = Ui.Web(m.PreviewUrl), Tag = m }).ToList();
        var pick = await TilePickerPage.ShowAsync(Navigation, new TilePickerPage("Modell", tiles, t => ((SwarmModel)t.Tag).Folder, tileWidth: 190, imageHeight: 240));
        if (pick?.Tag is SwarmModel m) { S.Model = m.Id; ShowModel(); }
    }

    private async Task PickLorasAsync()
    {
        if (_loras.Count == 0) { await DisplayAlertAsync("LoRA", "Noch keine Verbindung zu SwarmUI.", "OK"); return; }
        var tiles = _loras.Select(l => new Tile
        {
            Label = l.Short, Sub = string.IsNullOrWhiteSpace(l.Trigger) ? l.Folder : "Trigger: " + l.Trigger, Image = Ui.Web(l.PreviewUrl), Tag = l,
            Marked = S.Loras.Any(x => x.Name.Equals(l.Id, StringComparison.OrdinalIgnoreCase))
        }).ToList();
        await TilePickerPage.ShowAsync(Navigation, new TilePickerPage("LoRAs", tiles, t => ((SwarmModel)t.Tag).Folder, multi: true, onPick: t =>
        {
            var l = (SwarmModel)t.Tag;
            if (!S.Loras.Any(x => x.Name.Equals(l.Id, StringComparison.OrdinalIgnoreCase))) S.Loras.Add(new SwarmLora { Name = l.Id, Weight = 0.8 });
        }));
        ShowLoras();
    }

    private async Task PickHistoryAsync()
    {
        if (_client == null) return;
        try
        {
            _status.Text = "Lade Historie …";
            var list = await _client.ListHistoryAsync(300);
            _status.Text = "";
            var tiles = list.Where(h => h.Params != null).Select(h => new Tile
            {
                Label = h.Params?["model"]?.ToString() ?? "", Sub = h.OriginalPrompt ?? h.Params?["prompt"]?.ToString(), Image = Ui.Web(h.PreviewUrl), Tag = h
            }).ToList();
            var page = new TilePickerPage("Einstellungen aus der SwarmUI-Historie übernehmen", tiles, tileWidth: 200, imageHeight: 260,
                option: "Nur Prompts übernehmen", optionValue: _t.HistoryPromptsOnly);
            var pick = await TilePickerPage.ShowAsync(Navigation, page);
            _t.HistoryPromptsOnly = page.OptionChecked;
            if (pick?.Tag is not SwarmHistoryImage hi || hi.Params == null) return;
            ReadUi();
            if (page.OptionChecked) S.ImportPromptsFrom(hi.Params, hi.OriginalPrompt);
            else S.ImportFrom(hi.Params, hi.OriginalPrompt);
            ShowSettings();
            _status.Text = (page.OptionChecked ? "Prompts übernommen aus " : "Einstellungen übernommen aus ") + Path.GetFileName(hi.Src);
        }
        catch (Exception ex) { _status.Text = "Historie nicht lesbar: " + ex.Message; }
    }

    // ------------------------------------------------------------------ Vorlagen, eigenes Bild

    private TemplateInfo? CurrentTemplate() => _templates.FirstOrDefault(t => t.Name == _t.TemplateName) ?? _templates.FirstOrDefault();

    private string? InitUri(TemplateInfo t)
    {
        int look = Math.Clamp(_look.SelectedIndex < 0 ? 1 : _look.SelectedIndex, 0, TabletTemplate.InitFiles.Length - 1);
        return t.Files.GetValueOrDefault(TabletTemplate.InitFiles[look]) ?? TabletTemplate.InitFiles.Select(t.Files.GetValueOrDefault).FirstOrDefault(x => x != null);
    }

    private void ShowTemplate()
    {
        _folderName.Text = _t.TemplateFolder == null ? "Noch kein Ordner gewählt: „Ordner …“ – den synchronisierten Export-Ordner (oder seinen Unterordner „Tablet“) wählen."
            : $"Ordner: {AndroidStore.FolderLabel(_t.TemplateFolder)} · {_templates.Count} Vorlage(n)";
        var t = CurrentTemplate();
        _templateName.Text = t?.Display ?? "Keine Vorlage – in Pose Studio „Tablet“ klicken.";
        string? uri = t != null ? InitUri(t) ?? t.Preview : null;
        _templateImg.Source = uri != null ? ImageSource.FromStream(() => AndroidStore.Open(uri)) : null;
        UpdateSizeText();
    }

    private async Task PickFolderAsync()
    {
        var tree = await AndroidStore.PickFolderAsync();
        if (tree == null) return;
        _t.TemplateFolder = tree;
        _t.Save();
        await LoadTemplatesAsync(true);
    }

    private async Task LoadTemplatesAsync(bool report)
    {
        if (_t.TemplateFolder == null) { if (_source.SelectedIndex == 1) ShowTemplate(); return; }
        try
        {
            string tree = _t.TemplateFolder;
            _templates = await Task.Run(() => AndroidStore.ListTemplates(tree));
            if (report) _status.Text = $"{_templates.Count} Vorlage(n) gefunden.";
        }
        catch (Exception ex) { _templates = new(); _status.Text = "Vorlagen-Ordner nicht lesbar: " + ex.Message; }
        if (_source.SelectedIndex == 1) ShowTemplate();
    }

    private async Task PickTemplateAsync()
    {
        await LoadTemplatesAsync(false);
        if (_templates.Count == 0) { await DisplayAlertAsync("Vorlagen", _t.TemplateFolder == null ? "Bitte zuerst den Ordner wählen." : "Im Ordner liegen keine Vorlagen. In Pose Studio auf „Tablet“ klicken und die Synchronisation abwarten.", "OK"); return; }
        var tiles = _templates.Select(t => new Tile
        {
            Label = t.Display, Image = t.Preview is { } p ? ImageSource.FromStream(() => AndroidStore.Open(p)) : null, Tag = t, Marked = t.Name == _t.TemplateName
        }).ToList();
        var pick = await TilePickerPage.ShowAsync(Navigation, new TilePickerPage("Posen-Vorlage", tiles, tileWidth: 220, imageHeight: 260));
        if (pick?.Tag is TemplateInfo ti) { _t.TemplateName = ti.Name; ShowTemplate(); }
    }

    private async Task PickImageAsync()
    {
        var res = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Bild wählen", SelectionLimit = 1 });
        if (res?.FirstOrDefault() is not { } file) return;
        await UseImageAsync(await ReadAllAsync(file));
    }

    private static async Task<byte[]> ReadAllAsync(FileResult f)
    {
        await using var s = await f.OpenReadAsync();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private async Task UseImageAsync(byte[] data)
    {
        string path = Path.Combine(FileSystem.AppDataDirectory, "eigenes_bild" + (data.Length > 2 && data[0] == 0xFF ? ".jpg" : ".png"));
        foreach (var old in Directory.GetFiles(FileSystem.AppDataDirectory, "eigenes_bild.*")) File.Delete(old);
        await File.WriteAllBytesAsync(path, data);
        _t.ImagePath = path;
        _suppress = true; _source.SelectedIndex = 2; _suppress = false;
        _t.Source = 2;
        _ownImg.Source = null;
        UpdateSourceUi();
    }

    // ------------------------------------------------------------------ Profile

    private void RefreshProfiles(string? select = null)
    {
        _suppress = true;
        var names = new[] { "–" }.Concat(S.Profiles.Keys.OrderBy(k => k)).ToList();
        _profile.ItemsSource = names;
        _profile.SelectedIndex = select != null ? Math.Max(0, names.IndexOf(select)) : 0;
        _suppress = false;
    }

    private void ProfileChanged()
    {
        if (_suppress || _profile.SelectedIndex <= 0 || _profile.SelectedItem is not string name || !S.Profiles.TryGetValue(name, out var p)) return;
        S.ApplyProfile(p);
        ShowSettings();
        _status.Text = $"Profil „{name}“ geladen.";
    }

    private async Task SaveProfileAsync()
    {
        ReadUi();
        string current = _profile.SelectedIndex > 0 ? _profile.SelectedItem as string ?? "" : "";
        var name = await DisplayPromptAsync("Profil speichern", "Name des Profils:", "Speichern", "Abbrechen", initialValue: current == "" ? "Mein Profil" : current);
        if (string.IsNullOrWhiteSpace(name)) return;
        S.Profiles[name.Trim()] = S.CloneProfile();
        RefreshProfiles(name.Trim());
        _t.Save();
        _status.Text = $"Profil „{name.Trim()}“ gespeichert.";
    }

    private async Task DeleteProfileAsync()
    {
        if (_profile.SelectedIndex <= 0 || _profile.SelectedItem is not string name) return;
        if (!await DisplayAlertAsync("Profil", $"Profil „{name}“ löschen?", "Löschen", "Abbrechen")) return;
        S.Profiles.Remove(name);
        RefreshProfiles();
        _t.Save();
    }

    // ------------------------------------------------------------------ Generieren

    private Dictionary<string, string?> LoraTriggers() =>
        _loras.GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Trigger, StringComparer.OrdinalIgnoreCase);

    private async Task<bool> EnsureClientAsync()
    {
        if (_client == null || !_client.BaseUrl.Equals(new SwarmClient(S.Url).BaseUrl, StringComparison.OrdinalIgnoreCase) || _models.Count == 0) await ConnectAsync();
        return _client != null;
    }

    private async Task GenerateAsync()
    {
        ReadUi();
        _t.Save();
        if (!await EnsureClientAsync()) return;
        int src = _t.Source;
        var s = S.CloneProfile();
        string init = "", what;
        string? cn = null;
        try
        {
            double aspect = CurrentAspect();
            var (w, h) = s.SizeFor(aspect);
            if (src == 1)
            {
                var t = CurrentTemplate() ?? throw new InvalidOperationException("Keine Posen-Vorlage gewählt.");
                if (s.UseInitImage) init = Ui.DataUrl(AndroidStore.Read(InitUri(t) ?? throw new InvalidOperationException("Die Vorlage enthält kein Init Image.")));
                if (s.UseControlNet)
                {
                    string file = s.ControlRegions ? TabletTemplate.RegionsFile : TabletTemplate.PoseFile;
                    cn = Ui.DataUrl(AndroidStore.Read(t.Files.GetValueOrDefault(file) ?? throw new InvalidOperationException($"Die Vorlage enthält kein {file}.")));
                }
                if (!s.UseInitImage && !s.UseControlNet) _status.Text = "Hinweis: weder Init Image noch ControlNet – die Pose wird nicht verwendet.";
                what = $"{t.Display}, {w}×{h}";
            }
            else if (src == 2)
            {
                if (_t.ImagePath == null || !File.Exists(_t.ImagePath)) throw new InvalidOperationException("Kein eigenes Bild gewählt.");
                if (s.UseInitImage) init = Ui.DataUrl(await File.ReadAllBytesAsync(_t.ImagePath));
                s.UseControlNet = false;
                what = $"eigenes Bild, {w}×{h}";
            }
            else
            {
                s.UseInitImage = false; s.UseControlNet = false;
                what = $"{w}×{h}";
            }
            if (s.UseInitImage) what += $", Creativity {Ui.F(s.Creativity)}";
            if (s.UseControlNet) what += $", ControlNet {Ui.F(s.ControlStrength)}";
            var p = s.BuildParams(init, w, h, cn, LoraTriggers());
            await RunAsync(p, s.Count, what);
        }
        catch (Exception ex) { _status.Text = "Fehler: " + ex.Message; }
    }

    private async Task RefineAsync(ResultItem item)
    {
        if (item.Sent == null) { _status.Text = "Für dieses Bild sind keine Einstellungen gespeichert."; return; }
        ReadUi();
        if (!await EnsureClientAsync()) return;
        await RunAsync(S.BuildRefine(item.Sent, item.Seed), 1, $"Nachgebessert (Seed {item.Seed}, ×{Ui.F(S.RefinerUpscale)}, Refiner {Ui.F(S.RefinerControl * 100)} %)");
    }

    private async Task RunAsync(JsonObject p, int count, string what)
    {
        // Zufallsseed selbst würfeln: so ist der Seed jedes Bildes bekannt (Nachbessern braucht ihn)
        if ((p["seed"]?.GetValue<long>() ?? -1) < 0) p["seed"] = (long)Random.Shared.Next(1, int.MaxValue);
        long baseSeed = p["seed"]!.GetValue<long>();
        _cts = new CancellationTokenSource();
        _generate.IsEnabled = false; _cancel.IsEnabled = true;
        var pending = new List<ResultItem>();
        for (int i = 0; i < count; i++) { var it = new ResultItem { Caption = "Warte auf SwarmUI …" }; pending.Add(it); _results.Insert(i, it); }
        _resultList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        _progress.Progress = 0;
        _status.Text = "Generiere: " + what;
        var sw = Stopwatch.StartNew();
        var progress = new Progress<SwarmProgress>(pr =>
        {
            _progress.Progress = pr.Overall;
            if (pr.Status != null) _status.Text = pr.Status;
            if (pr.PreviewDataUrl != null && pending.FirstOrDefault(x => !x.Done) is { } first)
            {
                try { first.Image = Ui.Web(pr.PreviewDataUrl); first.Caption = $"läuft … {pr.Overall:P0}"; } catch { }
            }
        });
        try
        {
            var res = await _client!.GenerateAsync(p, progress, _cts.Token);
            string folder = AndroidStore.ResultsFolder;
            for (int i = 0; i < res.Count; i++)
            {
                var item = i < pending.Count ? pending[i] : new ResultItem();
                if (i >= pending.Count) _results.Insert(i, item);
                var bytes = await _client.DownloadAsync(res[i].Path);
                item.LocalPath = Path.Combine(folder, $"swarm_{DateTime.Now:yyyyMMdd_HHmmss}_{i}{ResultStore.ExtensionOf(res[i].Path)}");
                await File.WriteAllBytesAsync(item.LocalPath, bytes);
                item.Image = ImageSource.FromFile(item.LocalPath);
                long? known = ResultStore.SeedFrom(res[i].Metadata);
                if (known is null or < 0) known = await _client.LookupSeedAsync(res[i].Path);
                item.Seed = known is >= 0 ? known.Value : baseSeed + i;
                item.Sent = p;
                item.Caption = what.Contains("Seed") ? what : $"{what} · Seed {item.Seed}";
                item.Done = true;
                ResultStore.Save(item.LocalPath, item.Caption, item.Seed, item.Sent);
            }
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            SaveIndex();
            _status.Text = $"Fertig: {res.Count} Bild(er) in {sw.Elapsed.TotalSeconds:F0} s – auch in der SwarmUI-Historie.";
        }
        catch (OperationCanceledException)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            _status.Text = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            _status.Text = "Fehler: " + ex.Message;
        }
        finally
        {
            _generate.IsEnabled = true; _cancel.IsEnabled = false; _progress.Progress = 0;
        }
    }

    // ------------------------------------------------------------------ Ergebnisse

    private List<string> ListedPaths() => _results.Where(r => r.Done && r.LocalPath != null).Select(r => r.LocalPath!).ToList();

    private void SaveIndex()
    {
        ResultStore.SaveIndex(AndroidStore.ResultsFolder, ListedPaths());
        _ = SyncResultsAsync(merge: false, report: false);
    }

    private void LoadResults()
    {
        _results.Clear();
        foreach (var r in ResultStore.Load(AndroidStore.ResultsFolder))
            _results.Add(new ResultItem { LocalPath = r.Path, Caption = r.Caption, Seed = r.Seed, Sent = r.Sent, Done = true, Image = ImageSource.FromFile(r.Path) });
    }

    private View BuildBackupBox()
    {
        _backupBox.Children.Add(new Label
        {
            TextColor = Ui.Muted, FontSize = 12,
            Text = "Einstellungen sichern: Server, Profile, Prompts, LoRAs und Generierungswerte als Datei „einstellungen.json“ im Ergebnis-Ordner ablegen. "
                 + "Sicherung laden: diese Werte zurückholen – z. B. nach einer Neuinstallation oder auf dem zweiten Tablet (dort denselben synchronisierten Ordner wählen). "
                 + "Die App speichert ihre Einstellungen ohnehin automatisch; das hier ist nur die Kopie zum Mitnehmen."
        });
        _backupBox.Children.Add(Cols(Btn("Einstellungen sichern", ExportSettingsAsync), Btn("Sicherung laden", ImportSettingsAsync)));
        return _backupBox;
    }

    private void ShowResultFolder()
    {
        _resultFolderName.Text = _t.ResultFolder == null
            ? "Ergebnisse liegen nur in der App. Mit „Ergebnis-Ordner …“ einen synchronisierten Ordner wählen – die Bilder landen dann in dessen Unterordner „SwarmUI-Tablet“ und überstehen auch eine Neuinstallation."
            : !ResultMirror.HasAccess(_t.ResultFolder)
                ? $"Kein Zugriff mehr auf {AndroidStore.FolderLabel(_t.ResultFolder)} (z. B. nach Neuinstallation) – bitte „Ergebnis-Ordner …“ neu wählen."
                : $"Ergebnisse: {AndroidStore.FolderLabel(_t.ResultFolder)}/{ResultMirror.SubFolder}";
        _backupBox.IsVisible = _t.ResultFolder != null && ResultMirror.HasAccess(_t.ResultFolder);
    }

    /// <summary>Liste mit dem Ergebnis-Ordner abgleichen (siehe ResultMirror.Sync). Läuft im Hintergrund, nie parallel.</summary>
    private async Task SyncResultsAsync(bool merge, bool report)
    {
        string? tree = _t.ResultFolder;
        if (tree == null || !ResultMirror.HasAccess(tree)) { ShowResultFolder(); return; }
        await _syncLock.WaitAsync();
        try
        {
            string local = AndroidStore.ResultsFolder;
            var names = ListedPaths().Select(Path.GetFileName).ToList();
            bool busy = _results.Any(r => !r.Done);
            var final = await Task.Run(() => ResultMirror.Sync(tree, local, names!, merge));
            if (merge && !busy && !final.SequenceEqual(names))
            {
                ResultStore.SaveIndex(local, final.Select(n => Path.Combine(local, n)));
                LoadResults();
            }
            if (report) _status.Text = $"Ergebnis-Ordner abgeglichen: {final.Count} Bild(er).";
        }
        catch (Exception ex) { _status.Text = "Ergebnis-Ordner: " + ex.Message; }
        finally { _syncLock.Release(); ShowResultFolder(); }
    }

    private async Task PickResultFolderAsync()
    {
        var tree = await AndroidStore.PickFolderAsync();
        if (tree == null) return;
        _t.ResultFolder = tree;
        _t.Save();
        ShowResultFolder();
        _status.Text = "Gleiche Ergebnisse mit dem Ordner ab …";
        await SyncResultsAsync(merge: true, report: true);
    }

    private async Task ExportSettingsAsync()
    {
        if (_t.ResultFolder == null || !ResultMirror.HasAccess(_t.ResultFolder)) { await DisplayAlertAsync("Einstellungen", "Bitte zuerst den Ergebnis-Ordner wählen – die Datei wird dort abgelegt.", "OK"); return; }
        ReadUi();
        string tree = _t.ResultFolder, json = _t.ToJson();
        try
        {
            await Task.Run(() => ResultMirror.WriteSettings(tree, json));
            _status.Text = $"Einstellungen gesichert: {AndroidStore.FolderLabel(tree)}/{ResultMirror.SubFolder}/{ResultMirror.SettingsFile}";
        }
        catch (Exception ex) { await DisplayAlertAsync("Einstellungen", "Export fehlgeschlagen: " + ex.Message, "OK"); }
    }

    private async Task ImportSettingsAsync()
    {
        if (_t.ResultFolder == null || !ResultMirror.HasAccess(_t.ResultFolder)) { await DisplayAlertAsync("Einstellungen", "Bitte zuerst den Ergebnis-Ordner wählen, in dem die exportierte Datei liegt.", "OK"); return; }
        string tree = _t.ResultFolder;
        TabletSettings? imp;
        try
        {
            var json = await Task.Run(() => ResultMirror.ReadSettings(tree));
            if (json == null) { await DisplayAlertAsync("Einstellungen", $"Noch keine Sicherung vorhanden ({ResultMirror.SettingsFile} im Ordner {ResultMirror.SubFolder}). Erst auf einem Tablet „Einstellungen sichern“ tippen.", "OK"); return; }
            imp = TabletSettings.FromJson(json);
        }
        catch (Exception ex) { await DisplayAlertAsync("Einstellungen", "Datei nicht lesbar: " + ex.Message, "OK"); return; }
        if (imp == null) return;
        if (!await DisplayAlertAsync("Einstellungen", "Die aktuellen Einstellungen (Server, Profile, Prompt, Generierung) durch die Sicherung ersetzen? Die gewählten Ordner bleiben.", "Laden", "Abbrechen")) return;
        // Ordner-Freigaben gelten nur für diese Installation – die aktuellen behalten.
        _t.Swarm = imp.Swarm; _t.Source = imp.Source; _t.Aspect = imp.Aspect; _t.TemplateName = imp.TemplateName; _t.HistoryPromptsOnly = imp.HistoryPromptsOnly;
        _t.Save();
        ShowSettings();
        RefreshProfiles();
        await ConnectAsync();
        _status.Text = "Sicherung geladen.";
    }

    private async Task OpenResultAsync(ResultItem r)
    {
        switch (await ViewerPage.ShowAsync(Navigation, r))
        {
            case ViewerAction.Refine: await RefineAsync(r); break;
            case ViewerAction.UseSeed: _seed.Text = r.Seed.ToString(); _status.Text = $"Seed {r.Seed} übernommen."; break;
            case ViewerAction.UseAsImage: await UseImageAsync(await File.ReadAllBytesAsync(r.LocalPath!)); _status.Text = "Ergebnis als eigenes Bild gesetzt."; break;
            case ViewerAction.Remove:
                _results.Remove(r);
                if (_t.ResultFolder != null && ResultMirror.HasAccess(_t.ResultFolder)) DeleteLocal(r);   // liegt im Ergebnis-Ordner, die lokale Kopie ist nur Zwischenspeicher
                SaveIndex();
                break;
        }
    }

    private static void DeleteLocal(ResultItem r)
    {
        try { File.Delete(r.LocalPath!); File.Delete(r.LocalPath + ".json"); } catch { }
    }

    private async Task ClearResultsAsync()
    {
        if (_results.Count == 0) return;
        bool mirrored = _t.ResultFolder != null && ResultMirror.HasAccess(_t.ResultFolder);
        string where = mirrored ? $"Die Bilder bleiben im Ergebnis-Ordner ({ResultMirror.SubFolder}) und in der SwarmUI-Historie." : "Die Bilder bleiben in der SwarmUI-Historie. Die lokalen Kopien auf dem Tablet werden gelöscht.";
        if (!await DisplayAlertAsync("Liste leeren", $"Alle {_results.Count} Ergebnisse aus der Liste entfernen?\n\n{where}", "Leeren", "Abbrechen")) return;
        if (mirrored) await _syncLock.WaitAsync();   // laufenden Upload abwarten, bevor die lokalen Kopien verschwinden
        try
        {
            foreach (var r in _results.Where(r => r.LocalPath != null).ToList()) DeleteLocal(r);
            _results.Clear();
            ResultStore.SaveIndex(AndroidStore.ResultsFolder, Array.Empty<string>());
        }
        finally { if (mirrored) _syncLock.Release(); }
        _ = SyncResultsAsync(merge: false, report: false);
    }
}
