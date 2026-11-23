using System.Collections.ObjectModel;
using System.Diagnostics;
using Switch = Microsoft.Maui.Controls.Switch;
using System.Text.Json.Nodes;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Hauptseite: links (quer) bzw. unten (hoch) die Einstellungen, daneben Generieren und die Ergebnisse.</summary>
public sealed partial class MainPage : ContentPage
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
    private readonly Entry _character = new() { Placeholder = "Charakter (z. B. aus AnimaDex)" }, _style = new() { Placeholder = "Stil / Künstler (@name)" };
    private readonly Editor _prompt = new() { HeightRequest = 180, Placeholder = "Prompt (Szene, Pose, Kleidung …)" };
    private readonly Editor _negative = new() { HeightRequest = 90, Placeholder = "Negativ-Prompt" };
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
    private readonly Label _cnHint = new() { TextColor = Ui.Muted, FontSize = 12, Text = "SwarmUI macht aus dem Bild (bei der Vorlage: aus der gewählten Darstellung) zuerst ein Steuerbild (Linien, Skizze, Tiefenkarte oder Skelett) und hält sich beim Generieren an dessen Aufbau – Farben und Stil kommen aus dem Prompt. Für Posen ist eine Pose-Studio-Vorlage (3D-Pose) deutlich genauer." };
    private readonly Picker _cnLook = new() { ItemsSource = TabletTemplate.LookNames };
    private Grid? _cnLookRow;
    private int _cnShownFor = -1;
    private List<SwarmModel> _cns = new();
    /// <summary>ControlNets, die zur Familie des gewählten Modells passen (Familie unbekannt: alle).</summary>
    private List<string> _cnIds => (FindModel(S.Model)?.Family ?? "") is var f && f != "" ? _cns.Where(c => c.Family == f).Select(c => c.Id).ToList() : _cns.Select(c => c.Id).ToList();
    private readonly Entry _cnStrength = Num(), _cnStart = Num(), _cnEnd = Num();
    private readonly Picker _format = new() { ItemsSource = TabletSettings.Formats.Select(a => a.Name).ToList() };
    private readonly Picker _orient = new() { ItemsSource = new[] { "Hochformat", "Querformat" } };
    private readonly Picker _side = new() { ItemsSource = new[] { "1024", "1152", "1280", "1408", "1536" } };
    private readonly Label _sizeText = new() { TextColor = Ui.Muted, FontSize = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Stepper _count = new() { Minimum = 1, Maximum = 16, Increment = 1 };
    private readonly Label _countText = new() { TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, WidthRequest = 30 };
    private readonly Entry _seed = Num(), _steps = Num(), _cfg = Num();
    private readonly Picker _sampler = new(), _scheduler = new(), _vae = new();
    private readonly Entry _refControl = Num(), _refUpscale = Num(), _refSteps = Num(), _refCfg = Num();
    private readonly Switch _refKeepBg = new() { OnColor = Ui.Accent, HorizontalOptions = LayoutOptions.Start };
    private readonly Picker _refUpMethod = new(), _refMethod = new();
    private readonly Label _baseText = new() { TextColor = Ui.Muted, FontSize = 12 };
    private readonly Button _generate = new() { Text = "Generieren", FontSize = 18, FontAttributes = FontAttributes.Bold, BackgroundColor = Ui.Accent, TextColor = Colors.White, HeightRequest = 56 };
    private readonly Button _cancel = new() { Text = "Abbrechen", IsEnabled = false, BackgroundColor = Ui.PanelLight, TextColor = Colors.White, HeightRequest = 56 };
    private readonly Button _clearQueue = new() { IsVisible = false, BackgroundColor = Ui.PanelLight, TextColor = Colors.White, HeightRequest = 56, FontSize = 13 };
    private readonly ProgressBar _progress = new() { ProgressColor = Ui.Accent };
    private readonly Label _status = new() { TextColor = Ui.Muted, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
    private readonly CollectionView _resultList = new() { SelectionMode = SelectionMode.None };
    private readonly Grid _root = new();
    private readonly RefreshView _settingsView;
    private readonly Grid _resultsView;

    /// <summary>Zahlenfeld: beim Antippen ist der ganze Wert markiert, sodass Tippen ihn ersetzt (erneutes Tippen setzt den Cursor).</summary>
    private static Entry Num()
    {
        var e = new Entry { Keyboard = Keyboard.Numeric, HorizontalTextAlignment = TextAlignment.End };
        e.HandlerChanged += (_, _) => (e.Handler?.PlatformView as Android.Widget.EditText)?.SetSelectAllOnFocus(true);
        // Rückfallebene, falls das Antippen die Markierung gleich wieder aufhebt
        e.Focused += (_, _) => e.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () =>
        {
            if (e.IsFocused && e.SelectionLength == 0) { e.CursorPosition = 0; e.SelectionLength = e.Text?.Length ?? 0; }
        });
        return e;
    }

    public MainPage()
    {
        _t = TabletSettings.Load();
        S.TakeSegmentsFromPrompt();   // alte Prompts mit <segment:…> in Refiner-Zeilen umwandeln
        BackgroundColor = Ui.Page;
        NavigationPage.SetHasNavigationBar(this, false);
        // Ganz oben nach unten ziehen: SwarmUI liest die Modell-Ordner neu ein, danach Modelle, LoRAs usw. neu laden.
        _settingsView = new RefreshView { RefreshColor = Ui.Accent, Content = new ScrollView { Content = BuildSettings(), Padding = new Thickness(10, 10, 6, 10) } };
        _settingsView.Refreshing += async (_, _) => { try { await RefreshServerAsync(); } finally { _settingsView.IsRefreshing = false; } };
        _resultsView = BuildResults();
        Content = _root;
        ShowSettings();
        RefreshProfiles();
        LoadResults();
        ShowResultFolder();
        Loaded += async (_, _) => { await ShowLastCrashAsync(); ApkInstaller.Cleanup(); await ConnectAsync(); await RestoreQueueAsync(); await LoadTemplatesAsync(false); await SyncResultsAsync(merge: true, report: false); await CheckUpdatesAsync(false); };
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
        _source.SelectedIndexChanged += (_, _) => { if (!_suppress) { StoreCn(); _t.Source = _source.SelectedIndex; UpdateSourceUi(); } };
        _cnKind.SelectedIndexChanged += (_, _) =>
        {
            if (_suppress) return;
            int k = Math.Max(0, _cnKind.SelectedIndex);
            // Bild-Arten: Index in ImageControlKinds (bei der Vorlage nach 3D-Pose und Regionen)
            int img = _cnShownFor == 2 ? k : k - 2;
            string? m = img >= 0 ? SwarmSettings.SuggestImageControlModel(img, _cnIds)
                : k == 0 ? _cnIds.FirstOrDefault(c => c.Contains("3dpose", StringComparison.OrdinalIgnoreCase)) : null;
            if (m != null) { _suppress = true; SetItems(_cnModel, _cnIds, m, "(keins)"); _suppress = false; }
            UpdateCnExtras();
        };
        _format.SelectedIndexChanged += (_, _) => { if (!_suppress) { _t.Format = Math.Max(0, _format.SelectedIndex); _orient.IsEnabled = _t.Format != 0; UpdateSizeText(); } };
        _orient.SelectedIndexChanged += (_, _) => { if (!_suppress) { _t.Portrait = _orient.SelectedIndex != 1; UpdateSizeText(); } };
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
        // Einstellungen erst zeigen, wenn Init Image bzw. ControlNet eingeschaltet ist
        var initDetails = new VerticalStackLayout { Spacing = 8, Children = { Row("Creativity", crea) } };
        _initBox.Children.Add(initDetails);
        _cnBox.Children.Add(Row("ControlNet", _useCn));
        _cnLookRow = Row("Steuerbild aus", _cnLook);
        var cnDetails = new VerticalStackLayout { Spacing = 8, Children = { _cnHint, Row("Art", _cnKind), Row("Modell", _cnModel), _cnLookRow, Row("Stärke / Start / Ende", Cols(_cnStrength, _cnStart, _cnEnd)) } };
        _cnBox.Children.Add(cnDetails);
        initDetails.IsVisible = _useInit.IsToggled; cnDetails.IsVisible = _useCn.IsToggled;
        _useInit.Toggled += (_, e) => initDetails.IsVisible = e.Value;
        _useCn.Toggled += (_, e) => cnDetails.IsVisible = e.Value;
        _aspectBox.Children.Add(Row("Format", _format));
        _aspectBox.Children.Add(Row("Ausrichtung", _orient));

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
                Section("Prompt",
                    Row("Charakter", _character, Cols(Btn("Suchen …", () => AnimaDexPage.ShowAsync(Navigation, S, ApplyAnimaDex, false)), Btn("✕", () => { _character.Text = ""; S.CharacterTrigger = ""; return Task.CompletedTask; }))),
                    Row("Stil / Künstler", _style, Cols(Btn("Suchen …", () => AnimaDexPage.ShowAsync(Navigation, S, ApplyAnimaDex, true)), Btn("✕", () => { _style.Text = ""; return Task.CompletedTask; }))),
                    _prompt, _negative, Btn("⤢ Groß bearbeiten", EditPromptLargeAsync)),
                BuildSegmentSection(),
                Section("Vorlage", Row("Quelle", _source), _templateBox, _imageBox, _initBox, _cnBox, _aspectBox),
                Section("Generierung",
                    Row("Seitenlänge", _side, _sizeText), Row("Anzahl", countRow), Row("Seed (-1 = zufällig)", _seed, dice),
                    Row("Steps / CFG (0 = Standard)", Cols(_steps, _cfg)), Row("Sampler", _sampler), Row("Scheduler", _scheduler), Row("VAE", _vae)),
                Section("Nachbessern (Refiner)",
                    Row("Refiner-Anteil / Faktor", Cols(_refControl, _refUpscale)), Row("Hintergrund unscharf lassen", _refKeepBg),
                    new Label { Text = "An: Bild nur hochskalieren, dann nur die Figur neu malen (Refiner-Anteil = Kreativität) – Tiefenschärfe und Bokeh bleiben.", TextColor = Ui.Muted, FontSize = 12 }, Row("Hochskalieren mit", _refUpMethod), Row("Methode", _refMethod),
                    Row("Steps / CFG (0 = wie oben)", Cols(_refSteps, _refCfg))),
                Section("Ablage und Sicherung", _resultFolderName, Btn("Ergebnis-Ordner …", PickResultFolderAsync), BuildBackupBox()),
                Section("Update", new Label { Text = $"Installiert: Version {AppInfo.VersionString}. Die App gleicht beim Start mit dem neuesten Release auf GitHub ab.", TextColor = Ui.Muted, FontSize = 12 },
                    Btn("Nach Updates suchen", () => CheckUpdatesAsync(true))),
            }
        };
    }

    private Grid BuildResults()
    {
        _generate.Clicked += async (_, _) => { if (_paused && _queue.Count > 0 && _running == null) StartPump(); else await GenerateAsync(); };
        // Abbrechen: nur den laufenden Auftrag; die Warteschlange läuft danach weiter
        _cancel.Clicked += async (_, _) => { if (_running == null) return; _cts?.Cancel(); if (_client != null) try { await _client.InterruptAsync(); } catch { } };
        _clearQueue.Clicked += (_, _) => ClearQueue();
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(140)), new(GridLength.Auto) }, ColumnSpacing = 8 };
        top.Add(_generate, 0); top.Add(_cancel, 1); top.Add(_clearQueue, 2);

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
            var star = new Button { BackgroundColor = Ui.PanelLight, TextColor = Color.FromArgb("#FFD24A"), FontSize = 18, Padding = new Thickness(10, 0), MinimumWidthRequest = 44 };
            star.SetBinding(Button.TextProperty, nameof(ResultItem.StarText));
            star.SetBinding(IsEnabledProperty, nameof(ResultItem.CanStar));
            star.Clicked += async (s, _) => { if (((BindableObject)s!).BindingContext is ResultItem r) await ToggleStarAsync(r); };
            var buttons = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
            buttons.Add(refine, 0); buttons.Add(seed, 1); buttons.Add(star, 2);
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
        _character.Text = S.Character;
        _style.Text = S.Style;
        _negative.Text = S.Negative;
        ShowSegments();
        _autoTrigger.IsToggled = S.AutoTrigger;
        _source.SelectedIndex = Math.Clamp(_t.Source, 0, 2);
        _look.SelectedIndex = Math.Clamp(S.Look < 0 ? 1 : S.Look, 0, TabletTemplate.LookNames.Length - 1);
        _useInit.IsToggled = S.UseInitImage;
        _creativity.Value = S.Creativity; _creativityText.Text = Ui.F(Math.Round(S.Creativity, 2));
        _cnShownFor = -1;   // ControlNet-Felder füllt UpdateSourceUi je nach Quelle
        _cnStrength.Text = Ui.F(S.ControlStrength); _cnStart.Text = Ui.F(S.ControlStart); _cnEnd.Text = Ui.F(S.ControlEnd);
        _format.SelectedIndex = Math.Clamp(_t.Format, 0, TabletSettings.Formats.Length - 1);
        _orient.SelectedIndex = _t.Portrait ? 0 : 1;
        _orient.IsEnabled = _format.SelectedIndex != 0;
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
        _refKeepBg.IsToggled = S.RefineKeepBackground;
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
        S.Character = _character.Text ?? "";
        S.Style = _style.Text ?? "";
        if (S.CharacterTrigger != "" && !S.Character.Contains(S.CharacterTrigger, StringComparison.OrdinalIgnoreCase)) S.CharacterTrigger = "";   // von Hand ein anderer Charakter
        if (S.TakeSegmentsFromPrompt(true)) { _prompt.Text = S.Prompt; ShowSegments(); }   // eingefügte <segment:…> werden zu Refinern
        S.Negative = _negative.Text ?? "";
        S.AutoTrigger = _autoTrigger.IsToggled;
        _t.Source = Math.Max(0, _source.SelectedIndex);
        S.Look = Math.Max(0, _look.SelectedIndex);
        S.UseInitImage = _useInit.IsToggled;
        S.Creativity = Math.Round(_creativity.Value, 2);
        StoreCn();
        S.ControlStrength = Ui.PD(_cnStrength.Text, 1); S.ControlStart = Ui.PD(_cnStart.Text, 0); S.ControlEnd = Ui.PD(_cnEnd.Text, 1);
        _t.Format = Math.Max(0, _format.SelectedIndex);
        _t.Portrait = _orient.SelectedIndex != 1;
        S.SideLength = Math.Clamp((int)Ui.PD(_side.SelectedItem as string, S.SideLength), 256, 4096);
        S.Count = (int)_count.Value;
        S.Seed = long.TryParse(_seed.Text?.Trim(), out var seed) ? seed : -1;
        S.Steps = Math.Max(0, (int)Ui.PD(_steps.Text, 0)); S.Cfg = Math.Max(0, Ui.PD(_cfg.Text, 0));
        S.Sampler = GetItem(_sampler); S.Scheduler = GetItem(_scheduler); S.Vae = GetItem(_vae);
        S.RefinerControl = Ui.PD(_refControl.Text, 0.1); S.RefinerUpscale = Ui.PD(_refUpscale.Text, 2);
        S.RefineKeepBackground = _refKeepBg.IsToggled;
        S.RefinerUpscaleMethod = GetItem(_refUpMethod); S.RefinerMethod = GetItem(_refMethod);
        S.RefinerSteps = Math.Max(0, (int)Ui.PD(_refSteps.Text, 0)); S.RefinerCfg = Math.Max(0, Ui.PD(_refCfg.Text, 0));
    }

    public void SaveState()
    {
        ReadUi();
        _t.Save();
    }

    protected override void OnDisappearing() { base.OnDisappearing(); SaveState(); }

    /// <summary>ControlNet-Felder für die Quelle füllen: Vorlage = 3D-Pose/Regionen (Renderbilder), eigenes Bild = Arten mit Preprocessor.</summary>
    private void ShowCn(int src)
    {
        if (src == _cnShownFor) return;
        _cnShownFor = src;
        if (src != 1 && src != 2) return;
        bool old = _suppress; _suppress = true;
        if (src == 1)
        {
            _cnKind.ItemsSource = new[] { "3D-Pose (anima_3dpose)", "Regionen (anima_regional)" }.Concat(SwarmSettings.ImageControlKinds.Select(k => k.Name)).ToList();
            _cnKind.SelectedIndex = S.ControlFromImage ? 2 + Math.Clamp(S.TemplateControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1) : S.ControlRegions ? 1 : 0;
            _cnLook.SelectedIndex = Math.Clamp(S.ControlLook, 0, TabletTemplate.LookNames.Length - 1);
            _useCn.IsToggled = S.UseControlNet;
            SetItems(_cnModel, _cnIds, S.ControlModel, "(keins)");
        }
        else
        {
            _cnKind.ItemsSource = SwarmSettings.ImageControlKinds.Select(k => k.Name).ToList();
            _cnKind.SelectedIndex = Math.Clamp(S.ImageControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1);
            _useCn.IsToggled = S.ImageUseControlNet;
            if (S.ImageControlModel == "" && SwarmSettings.SuggestImageControlModel(S.ImageControlKind, _cnIds) is { } m) S.ImageControlModel = m;
            SetItems(_cnModel, _cnIds, S.ImageControlModel, "(keins)");
        }
        _suppress = old;
        UpdateCnExtras();
    }

    /// <summary>Hinweis und Darstellungs-Auswahl nur bei Bild-Arten (eigenes Bild bzw. Vorlage mit Linien/Skizze/Tiefe/Pose).</summary>
    private void UpdateCnExtras()
    {
        bool tplImage = _cnShownFor == 1 && _cnKind.SelectedIndex >= 2;
        _cnHint.IsVisible = _cnShownFor == 2 || tplImage;
        if (_cnLookRow != null) _cnLookRow.IsVisible = tplImage;
    }

    /// <summary>ControlNet-Felder in die Einstellungen der gerade angezeigten Quelle zurückschreiben.</summary>
    private void StoreCn()
    {
        if (_cnShownFor == 1)
        {
            int k = Math.Max(0, _cnKind.SelectedIndex);
            S.UseControlNet = _useCn.IsToggled; S.ControlRegions = k == 1; S.ControlFromImage = k >= 2; S.ControlModel = GetItem(_cnModel);
            if (k >= 2) S.TemplateControlKind = k - 2;
            S.ControlLook = Math.Max(0, _cnLook.SelectedIndex);
        }
        else if (_cnShownFor == 2)
        {
            S.ImageUseControlNet = _useCn.IsToggled; S.ImageControlKind = Math.Max(0, _cnKind.SelectedIndex); S.ImageControlModel = GetItem(_cnModel);
        }
    }

    private void UpdateSourceUi()
    {
        int src = Math.Max(0, _source.SelectedIndex);
        _templateBox.IsVisible = src == 1;
        _imageBox.IsVisible = src == 2;
        _initBox.IsVisible = src != 0;
        _cnBox.IsVisible = src == 1 || src == 2;
        ShowCn(src);
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
        return _t.AspectValue();
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

    private async Task EditPromptLargeAsync()
    {
        if (await PromptEditorPage.ShowAsync(Navigation, _prompt.Text ?? "", _negative.Text ?? "") is { } r)
        {
            _prompt.Text = r.Prompt; _negative.Text = r.Negative;
            ReadUi(); _t.Save();
        }
    }

    private async Task RefreshServerAsync()
    {
        if (_client != null)
        {
            _server.Text = "SwarmUI liest Modelle neu ein …";
            try { await _client.TriggerRefreshAsync(); } catch (Exception ex) { _status.Text = "Neu einlesen fehlgeschlagen: " + ex.Message; }
        }
        await ConnectAsync();
        _status.Text = _server.Text;
    }

    private async Task ConnectAsync()
    {
        ReadUi();
        if (_running == null) _client?.Dispose();   // laufender Auftrag behält seine Verbindung
        _client = new SwarmClient(S.Url);
        _server.Text = "Verbinde …";
        try
        {
            string ver = await _client.ConnectAsync();
            _models = await _client.ListModelsAsync("Stable-Diffusion");
            _loras = await _client.ListModelsAsync("LoRA");
            var vaes = await _client.ListModelsAsync("VAE");
            var cns = await _client.ListModelsAsync("ControlNet");
            var values = await _client.ListParamValuesAsync(new[] { "sampler", "scheduler", "refinerupscalemethod", "refinermethod", "vae", "yolomodelinternal" });
            _yolos = values.GetValueOrDefault("yolomodelinternal") ?? new();
            ShowSegments();
            _server.Text = $"Verbunden mit SwarmUI {ver}: {_models.Count} Modelle, {_loras.Count} LoRAs.";
            ReadUi();
            SetItems(_sampler, values.GetValueOrDefault("sampler"), S.Sampler);
            SetItems(_scheduler, values.GetValueOrDefault("scheduler"), S.Scheduler);
            SetItems(_refUpMethod, values.GetValueOrDefault("refinerupscalemethod"), S.RefinerUpscaleMethod);
            SetItems(_refMethod, values.GetValueOrDefault("refinermethod"), S.RefinerMethod);
            SetItems(_vae, SwarmClient.VaeChoices(values.GetValueOrDefault("vae"), vaes), S.Vae);
            _cns = cns;
            if (S.ControlModel == "" && _cnIds.FirstOrDefault(c => c.Contains("3dpose", StringComparison.OrdinalIgnoreCase)) is { } pose) S.ControlModel = pose;
            _cnShownFor = -1;
            ShowCn(Math.Max(0, _source.SelectedIndex));
            ShowModel();
            ShowLoras();
            _ = SyncStarsAsync();
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
        if (pick?.Tag is SwarmModel m)
        {
            ReadUi();
            string oldFamily = FindModel(S.Model)?.Family ?? "";
            S.Model = m.Id;
            if (S.SwitchFamily(oldFamily, m.Family))
            {
                // andere Modellfamilie (z. B. Anima → Illustrious): VAE und ControlNet der neuen Familie, neu = VAE None
                _suppress = true; SetItems(_vae, _vae.ItemsSource?.Cast<string>().Skip(1), S.Vae); _suppress = false;
                var fit = _cnIds;
                // Vorlage ohne anima_3dpose (z. B. Illustrious): Tiefe aus dem Renderbild per Preprocessor, passendes Modell wählen
                if (S.ControlModel == "" && fit.Count > 0 && !fit.Any(c => c.Contains("3dpose", StringComparison.OrdinalIgnoreCase)))
                {
                    S.ControlFromImage = true; S.ControlRegions = false; S.TemplateControlKind = 2; S.ControlLook = 0;
                    S.ControlModel = SwarmSettings.SuggestImageControlModel(2, fit) ?? fit[0];
                }
                _status.Text = $"Modellfamilie {SwarmModel.FamilyLabel(m.Family)}: VAE „{(S.Vae == "" ? "(Standard)" : S.Vae)}“, ControlNet „{(S.ControlModel == "" ? "(keins)" : S.ControlModel)}“"
                             + (fit.Count == 0 ? " – kein passendes ControlNet installiert." : ".");
            }
            _cnShownFor = -1;
            ShowCn(Math.Max(0, _source.SelectedIndex));
            ShowModel();
        }
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
                if (s.UseControlNet && s.ControlFromImage)
                {
                    // Bild-Art: gewählte Darstellung der Vorlage (z. B. Strichzeichnung) als Steuerbild, SwarmUI-Preprocessor macht daraus Linien/Tiefe …
                    string file = TabletTemplate.InitFiles[Math.Clamp(s.ControlLook, 0, TabletTemplate.InitFiles.Length - 1)];
                    string uri = t.Files.GetValueOrDefault(file) ?? InitUri(t) ?? throw new InvalidOperationException("Die Vorlage enthält kein Bild für das Steuerbild.");
                    cn = Ui.DataUrl(AndroidStore.Read(uri));
                    s.ControlPreprocessor = SwarmSettings.ImageControlKinds[Math.Clamp(s.TemplateControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1)].Preprocessor;
                }
                else if (s.UseControlNet)
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
                byte[] own = await File.ReadAllBytesAsync(_t.ImagePath);
                if (s.UseInitImage) init = Ui.DataUrl(own);
                s.UseControlNet = s.ImageUseControlNet && s.ImageControlModel != "";
                if (s.UseControlNet)
                {
                    var kind = SwarmSettings.ImageControlKinds[Math.Clamp(s.ImageControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1)];
                    cn = Ui.DataUrl(own);
                    s.ControlModel = s.ImageControlModel;
                    s.ControlPreprocessor = kind.Preprocessor;
                }
                else if (s.ImageUseControlNet) _status.Text = "Hinweis: kein ControlNet-Modell gewählt – ControlNet bleibt aus.";
                what = $"eigenes Bild, {w}×{h}";
            }
            else
            {
                s.UseInitImage = false; s.UseControlNet = false;
                what = $"{w}×{h}";
            }
            if (s.UseInitImage) what += $", Creativity {Ui.F(s.Creativity)}";
            if (s.UseControlNet) what += s.ControlPreprocessor != null
                ? $", {SwarmSettings.ImageControlKinds[Math.Clamp(src == 1 ? s.TemplateControlKind : s.ImageControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1)].Name.Split(' ')[0]} {Ui.F(s.ControlStrength)}"
                : $", ControlNet {Ui.F(s.ControlStrength)}";
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
        await RunAsync(S.BuildRefine(item.Sent, item.Seed, _yolos), 1, $"Nachgebessert (Seed {item.Seed}, ×{Ui.F(S.RefinerUpscale)}, {(S.RefineKeepBackground ? "nur Figur" : "Refiner")} {Ui.F(S.RefinerControl * 100)} %)");
    }

    // ------------------------------------------------------------------ Warteschlange

    /// <summary>Ein Auftrag: Parameter (Seed schon gewürfelt), Platzhalter-Kacheln in der Ergebnisliste.</summary>
    private sealed record Job(JsonObject P, int Count, string What, long BaseSeed, List<ResultItem> Pending);
    /// <summary>Auftrag, dessen Verbindung abriss, nachdem er schon bei SwarmUI war. SwarmUI arbeitet ihn trotzdem zu Ende (getestet) –
    /// die Bilder werden aus der Historie nachgeholt (erkannt an Seed und Prompt). Erst wenn die Sitzung <see cref="Session"/> nichts
    /// mehr rechnet und trotzdem Bilder fehlen, wird der Rest neu eingereiht.</summary>
    private sealed record LostJob(JsonObject P, int Count, string What, long BaseSeed, DateTime Since, List<ResultItem> Pending, HashSet<long> Found, bool Requeue, string? Session);
    private readonly List<Job> _queue = new();
    private readonly List<LostJob> _lost = new();
    private Job? _running;
    private bool _paused, _recovering, _askedNotify, _restored;
    private static string QueueFile => Path.Combine(FileSystem.AppDataDirectory, "queue.json");

    /// <summary>Auftrag einreihen – „Generieren“ bleibt bedienbar, die Aufträge laufen nacheinander.</summary>
    private Task RunAsync(JsonObject p, int count, string what)
    {
        // Zufallsseed selbst würfeln: so ist der Seed jedes Bildes bekannt (Nachbessern und Nachholen brauchen ihn)
        if ((p["seed"]?.GetValue<long>() ?? -1) < 0) p["seed"] = (long)Random.Shared.Next(1, int.MaxValue);
        _queue.Add(new Job(p, count, what, p["seed"]!.GetValue<long>(), AddPlaceholders(count, "In der Warteschlange: " + what)));
        _resultList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        if (_running != null) _status.Text = $"Eingereiht: {what}";
        _ = AskNotificationsAsync();
        StartPump();
        return Task.CompletedTask;
    }

    private List<ResultItem> AddPlaceholders(int count, string caption, int at = 0)
    {
        var list = new List<ResultItem>();
        for (int i = 0; i < count; i++) { var it = new ResultItem { Caption = caption }; list.Add(it); _results.Insert(Math.Min(at + i, _results.Count), it); }
        return list;
    }

    /// <summary>Warteschlange (wieder) anlaufen lassen – auch nach einer Pause wegen fehlender Verbindung.</summary>
    private void StartPump()
    {
        _paused = false;
        SaveQueue();
        if (_running == null && _queue.Count > 0) _ = PumpAsync();
        UpdateQueueUi();
    }

    private async Task PumpAsync()
    {
        while (!_paused && _queue.Count > 0)
        {
            var job = _queue[0];
            _queue.RemoveAt(0);
            _running = job;
            SaveQueue();
            UpdateQueueUi();
            try { await ExecuteAsync(job); }
            finally { _running = null; SaveQueue(); }
        }
        UpdateQueueUi();
    }

    private void UpdateQueueUi()
    {
        int waiting = _queue.Count;
        _cancel.IsEnabled = _running != null;
        _clearQueue.IsVisible = waiting > 0;
        _clearQueue.Text = $"Warteschlange leeren ({waiting})";
        _generate.Text = _running == null ? (_paused && waiting > 0 ? $"Fortsetzen ({waiting})" : "Generieren") : waiting > 0 ? $"Einreihen ({waiting} warten)" : "Einreihen";
        UpdateKeepAlive();
    }

    /// <summary>Solange etwas läuft, wartet oder nachgeholt wird: Vordergrund-Dienst, damit Android die App im Hintergrund nicht einfriert.</summary>
    private void UpdateKeepAlive()
    {
        int lost = _lost.Sum(l => l.Pending.Count(x => !x.Done));
        if (_running != null) GenerationService.Show($"Generiere: {_running.What}" + (_queue.Count > 0 ? $" · {_queue.Count} warten" : ""));
        else if (_queue.Count > 0 && !_paused) GenerationService.Show($"{_queue.Count} Auftrag/Aufträge in der Warteschlange");
        else if (lost > 0) GenerationService.Show($"Hole {lost} Bild(er) vom Server nach");
        else GenerationService.Stop();
    }

    /// <summary>Android 13+: einmal um Erlaubnis für die Fortschritts-Benachrichtigung bitten (ohne läuft der Dienst trotzdem).</summary>
    private async Task AskNotificationsAsync()
    {
        if (_askedNotify || !OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        _askedNotify = true;
        try
        {
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch { }
    }

    private void ClearQueue()
    {
        foreach (var job in _queue) foreach (var it in job.Pending) _results.Remove(it);
        _queue.Clear();
        _paused = false;
        SaveQueue();
        _status.Text = "Warteschlange geleert." + (_running != null ? " Der laufende Auftrag läuft weiter." : "");
        UpdateQueueUi();
    }

    /// <summary>App kommt zurück in den Vordergrund: angehaltene Warteschlange fortsetzen, verlorene Bilder nachholen.</summary>
    public void OnAppResumed()
    {
        if (_paused && _queue.Count > 0 && _running == null) StartPump();
        _ = RecoverLostAsync();
    }

    private async Task ExecuteAsync(Job job)
    {
        var (p, _, what, baseSeed, pending) = job;
        var client = _client;
        if (client == null) { RequeueAndPause(job, "Keine Verbindung zu SwarmUI"); return; }
        var cts = _cts = new CancellationTokenSource();
        foreach (var it in pending) it.Caption = "Warte auf SwarmUI …";
        _progress.Progress = 0;
        string Queued() => _queue.Count > 0 ? $" · {_queue.Count} in der Warteschlange" : "";
        _status.Text = "Generiere: " + what + Queued();
        var sw = Stopwatch.StartNew();
        var progress = new Progress<SwarmProgress>(pr =>
        {
            _progress.Progress = pr.Overall;
            if (pr.Status != null) _status.Text = pr.Status + Queued();
            if (pr.PreviewDataUrl != null && pending.FirstOrDefault(x => !x.Done) is { } first)
            {
                try { first.Image = Ui.Web(pr.PreviewDataUrl); first.Caption = $"läuft … {pr.Overall:P0}"; } catch { }
            }
        });
        try
        {
            var res = await client.GenerateAsync(p, progress, cts.Token);
            for (int i = 0; i < res.Count; i++)
            {
                ResultItem item;
                if (i < pending.Count) item = pending[i];
                else
                {
                    item = new ResultItem();
                    int at = pending.Count > 0 && _results.IndexOf(pending[^1]) is >= 0 and var last ? last + 1 : 0;
                    _results.Insert(Math.Min(at, _results.Count), item);
                    pending.Add(item);
                }
                var bytes = await client.DownloadAsync(res[i].Path);
                long? known = ResultStore.SeedFrom(res[i].Metadata);
                if (known is null or < 0) known = await client.LookupSeedAsync(res[i].Path);
                long seed = known is >= 0 ? known.Value : baseSeed + i;
                await FillResultAsync(item, bytes, res[i].Path, seed, p, what.Contains("Seed") ? what : $"{what} · Seed {seed}", i);
            }
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            SaveIndex();
            _status.Text = $"Fertig: {res.Count} Bild(er) in {sw.Elapsed.TotalSeconds:F0} s – auch in der SwarmUI-Historie." + Queued();
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            _status.Text = "Abgebrochen." + Queued();
        }
        catch (SwarmConnectionException ex) when (!ex.Started)
        {
            // Auftrag kam nie bei SwarmUI an: zurück an den Anfang der Warteschlange, anhalten bis die Verbindung wieder da ist
            RequeueAndPause(job, ex.Message);
        }
        catch (Exception ex) when (ex is SwarmConnectionException or HttpRequestException or IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException)
        {
            // Verbindung weg, während SwarmUI schon arbeitete (z. B. App im Hintergrund eingefroren): Bilder später aus der Historie holen
            var left = pending.Where(x => !x.Done).ToList();
            if (left.Count > 0)
            {
                foreach (var it in left) { it.Caption = "Verbindung unterbrochen – hole Bild vom Server …"; it.Image = null; }
                _lost.Add(new LostJob(p, job.Count, what, baseSeed, DateTime.UtcNow, left, pending.Where(x => x.Done).Select(x => x.Seed).ToHashSet(), true, client.SessionId));
            }
            if (pending.Any(x => x.Done)) SaveIndex();
            _status.Text = "Verbindung unterbrochen – SwarmUI rechnet weiter, die Bilder werden aus der Historie geholt." + Queued();
            SaveQueue();
            _ = RecoverLostAsync();
        }
        catch (Exception ex)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            _status.Text = "Fehler: " + ex.Message + Queued();
        }
        finally
        {
            _progress.Progress = 0;
        }
    }

    private void RequeueAndPause(Job job, string why)
    {
        foreach (var it in job.Pending) { it.Caption = "Angehalten: " + job.What; it.Image = null; }
        _queue.Insert(0, job);
        _paused = true;
        SaveQueue();
        _status.Text = $"{why} – Warteschlange angehalten. „Fortsetzen“ tippen, sobald SwarmUI erreichbar ist.";
    }

    /// <summary>Fertiges Bild in eine Platzhalter-Kachel übernehmen und lokal (samt Seed/Parametern) speichern.</summary>
    private async Task FillResultAsync(ResultItem item, byte[] bytes, string serverPath, long seed, JsonObject sent, string caption, int index)
    {
        item.LocalPath = Path.Combine(AndroidStore.ResultsFolder, $"swarm_{DateTime.Now:yyyyMMdd_HHmmss}_{index}{ResultStore.ExtensionOf(serverPath)}");
        await File.WriteAllBytesAsync(item.LocalPath, bytes);
        item.Image = ImageSource.FromFile(item.LocalPath);
        item.Seed = seed;
        item.Sent = sent;
        item.Caption = caption;
        item.ServerPath = serverPath;
        item.Done = true;
        ResultStore.Save(item.LocalPath, item.Caption, item.Seed, item.Sent, item.ServerPath);
    }

    // ------------------------------------------------------------------ Nachholen

    /// <summary>Verlorene Aufträge in der SwarmUI-Historie suchen (alle 10 s). Was noch fehlt, wenn SwarmUI für die Sitzung
    /// nichts mehr rechnet, wird neu eingereiht – solange der Server nicht antwortet, wird nur gewartet.</summary>
    private async Task RecoverLostAsync()
    {
        if (_recovering || _lost.Count == 0) return;
        _recovering = true;
        try
        {
            while (_lost.Count > 0)
            {
                if (_client == null) try { await ConnectAsync(); } catch { }
                if (_client is { } client)
                {
                    List<SwarmHistoryImage>? hist = null;
                    try { hist = await client.ListHistoryAsync(150); } catch { }
                    if (hist != null)
                        foreach (var lj in _lost.ToList())
                            try { await TryRecoverAsync(client, lj, hist); } catch { }
                }
                bool requeued = false;
                var finished = new List<LostJob>();
                foreach (var lj in _lost.Where(l => DateTime.UtcNow - l.Since > TimeSpan.FromSeconds(20)).ToList())
                {
                    // SwarmUI noch beschäftigt (oder nicht erreichbar)? Dann weiter warten.
                    if (_client is not { } c) continue;
                    try { if (lj.Session != null && await c.SessionBusyAsync(lj.Session) > 0) continue; }
                    catch { continue; }
                    finished.Add(lj);
                }
                // Letzter Blick in die Historie, nachdem SwarmUI fertig ist
                if (finished.Count > 0 && _client is { } c2)
                    try { var h = await c2.ListHistoryAsync(150); foreach (var lj in finished) await TryRecoverAsync(c2, lj, h); } catch { finished.Clear(); }
                foreach (var lj in finished.Where(_lost.Contains))
                {
                    var open = lj.Pending.Where(x => !x.Done).ToList();
                    _lost.Remove(lj);
                    var missing = Enumerable.Range(0, lj.Count).Select(i => lj.BaseSeed + i).Where(sd => !lj.Found.Contains(sd)).ToList();
                    if (lj.Requeue && open.Count > 0 && missing.Count > 0)
                    {
                        // Platzhalter behalten und als neuen Auftrag (nur die fehlenden Bilder, gleiche Seeds) vorne einreihen
                        var p = lj.P.DeepClone().AsObject();
                        int n = Math.Min(open.Count, missing.Count);
                        p["images"] = n; p["seed"] = missing[0];
                        foreach (var it in open.Skip(n)) _results.Remove(it);
                        foreach (var it in open.Take(n)) it.Caption = "In der Warteschlange (neu): " + lj.What;
                        _queue.Insert(0, new Job(p, n, lj.What, missing[0], open.Take(n).ToList()));
                        requeued = true;
                        _status.Text = $"{n} Bild(er) von „{lj.What}“ gingen beim Verbindungsabbruch verloren – neu eingereiht.";
                    }
                    else
                    {
                        foreach (var it in open) _results.Remove(it);
                        if (open.Count > 0) _status.Text = $"{open.Count} Bild(er) von „{lj.What}“ gingen beim Verbindungsabbruch verloren.";
                    }
                }
                if (requeued) StartPump();
                SaveQueue();
                UpdateQueueUi();
                if (_lost.Count == 0) break;
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
        finally { _recovering = false; }
    }

    private async Task TryRecoverAsync(SwarmClient client, LostJob lj, List<SwarmHistoryImage> hist)
    {
        string want = Norm(lj.P["prompt"]?.ToString());
        var have = _results.Where(r => r.ServerPath != null).Select(r => client.RelativePath(r.ServerPath!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int found = 0;
        foreach (var h in hist)
        {
            if (lj.Pending.All(x => x.Done)) break;
            if (h.Params is not { } prm || !long.TryParse(prm["seed"]?.ToString(), out long seed)) continue;
            if (seed < lj.BaseSeed || seed >= lj.BaseSeed + lj.Count) continue;
            if (Norm(prm["prompt"]?.ToString()) != want && Norm(h.OriginalPrompt) != want) continue;
            if (!SameValue(lj.P, prm, "width") || !SameValue(lj.P, prm, "height")) continue;
            if (lj.P.ContainsKey("refinercontrolpercentage") != prm.ContainsKey("refinercontrolpercentage")) continue;
            if (!have.Add(client.RelativePath(h.Src))) continue;
            var item = lj.Pending.First(x => !x.Done);
            lj.Found.Add(seed);
            var bytes = await client.DownloadAsync(h.Src);
            await FillResultAsync(item, bytes, h.Src, seed, lj.P, lj.What.Contains("Seed") ? lj.What : $"{lj.What} · Seed {seed}", (int)(seed - lj.BaseSeed));
            found++;
        }
        if (found > 0)
        {
            SaveIndex();
            _status.Text = $"{found} Bild(er) von „{lj.What}“ aus der SwarmUI-Historie nachgeholt.";
        }
        if (lj.Pending.All(x => x.Done)) _lost.Remove(lj);

        static string Norm(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        static bool SameValue(JsonObject a, JsonObject b, string key) =>
            a[key] is not JsonNode x || b[key] is not JsonNode y || x.ToString() == y.ToString();
    }

    // ------------------------------------------------------------------ Warteschlange speichern

    /// <summary>Warteschlange und verlorene Aufträge sichern – falls Android die App im Hintergrund beendet.
    /// Der laufende Auftrag wird als „verloren“ vermerkt (beim nächsten Start aus der Historie nachholen).</summary>
    private void SaveQueue()
    {
        try
        {
            static JsonObject J(JsonObject p, int count, string what, long seed) =>
                new() { ["p"] = p.DeepClone(), ["count"] = count, ["what"] = what, ["seed"] = seed };
            var lost = new JsonArray();
            foreach (var l in _lost) { var o = J(l.P, l.Count, l.What, l.BaseSeed); o["open"] = l.Pending.Count(x => !x.Done); o["since"] = l.Since; o["session"] = l.Session; lost.Add(o); }
            if (_running is { } r) { var o = J(r.P, r.Count, r.What, r.BaseSeed); o["open"] = r.Pending.Count(x => !x.Done); o["since"] = DateTime.UtcNow; o["session"] = _client?.SessionId; lost.Add(o); }
            var waiting = new JsonArray();
            foreach (var j in _queue) waiting.Add(J(j.P, j.Count, j.What, j.BaseSeed));
            if (lost.Count == 0 && waiting.Count == 0) { if (File.Exists(QueueFile)) File.Delete(QueueFile); return; }
            File.WriteAllText(QueueFile, new JsonObject { ["waiting"] = waiting, ["lost"] = lost }.ToJsonString());
        }
        catch { }
    }

    /// <summary>Beim Start: Aufträge wiederherstellen, die beim letzten Mal noch liefen oder warteten.</summary>
    private async Task RestoreQueueAsync()
    {
        if (_restored) return;
        _restored = true;
        JsonObject? o = null;
        try { if (File.Exists(QueueFile)) o = JsonNode.Parse(File.ReadAllText(QueueFile)) as JsonObject; } catch { }
        if (o == null) return;
        foreach (var l in o["lost"] as JsonArray ?? new JsonArray())
        {
            if (l?["p"] is not JsonObject p) continue;
            int open = l["open"]?.GetValue<int>() ?? 0;
            if (open <= 0) continue;
            string what = l["what"]?.ToString() ?? "";
            int count = l["count"]?.GetValue<int>() ?? open; long seed = l["seed"]?.GetValue<long>() ?? 0;
            _lost.Add(new LostJob(p.DeepClone().AsObject(), count, what, seed, l["since"]?.GetValue<DateTime>() ?? DateTime.UtcNow,
                AddPlaceholders(open, "Unterbrochen – hole Bild vom Server …"), Enumerable.Range(0, Math.Max(0, count - open)).Select(i => seed + i).ToHashSet(), false, l["session"]?.ToString()));
        }
        var waiting = (o["waiting"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(w => w["p"] is JsonObject).ToList();
        bool resume = (waiting.Count > 0 || _lost.Count > 0) && await DisplayAlertAsync("Warteschlange",
            $"Beim letzten Mal waren noch {waiting.Count + _lost.Count} Auftrag/Aufträge offen – Android hat die App vermutlich im Hintergrund beendet. "
            + "Fertige Bilder holt die App aus der SwarmUI-Historie. Den Rest erneut generieren?", "Fortsetzen", "Verwerfen");
        for (int i = 0; i < _lost.Count; i++) _lost[i] = _lost[i] with { Requeue = resume };
        if (resume)
        {
            foreach (var w in waiting)
            {
                int count = w["count"]?.GetValue<int>() ?? 1;
                string what = w["what"]?.ToString() ?? "";
                _queue.Add(new Job(w["p"]!.DeepClone().AsObject(), count, what, w["seed"]?.GetValue<long>() ?? 0,
                    AddPlaceholders(count, "In der Warteschlange: " + what, _results.Count(r => !r.Done))));
            }
        }
        StartPump();
        _ = RecoverLostAsync();
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
            _results.Add(new ResultItem { LocalPath = r.Path, Caption = r.Caption, Seed = r.Seed, Sent = r.Sent, ServerPath = r.ServerPath, Starred = r.Starred, Done = true, Image = ImageSource.FromFile(r.Path) });
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

    /// <summary>AnimaDex-Auswahl übernehmen: Charakter bzw. Stil wird ersetzt.</summary>
    private void ApplyAnimaDex(AnimaDexEntry entry, string text)
    {
        if (entry.IsArtist) { _style.Text = text; S.Style = text; }
        else { _character.Text = text; S.Character = text; S.CharacterTrigger = entry.PromptText(false); }
        _t.Save();
    }

    /// <summary>AnimaDex-Eintrag an der Cursorstelle des Prompts einfügen (Cursor am Ende/im Refine-Teil: am Anfang), mit Kommas abgetrennt.</summary>
    private void InsertIntoPrompt(string text)
    {
        string cur = _prompt.Text ?? "";
        int pos = AnimaDexClient.SmartInsertPos(cur, _prompt.CursorPosition);
        var (t, caret) = AnimaDexClient.InsertIntoPrompt(cur, pos, text);
        _prompt.Text = t;
        _prompt.CursorPosition = caret;
        S.Prompt = t;
    }

    // ------------------------------------------------------------------ Update

    private bool _updating;

    /// <summary>Mit dem neuesten GitHub-Release abgleichen; nach Rückfrage APK laden und den Android-Installer öffnen.
    /// manual = über den Knopf (meldet auch „aktuell“ und Fehler, ignoriert „überspringen“).</summary>
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            Update.UpdateInfo info;
            try { info = await Update.UpdateChecker.GetLatestAsync(); }
            catch (Exception ex) { if (manual) await DisplayAlertAsync("Update", "Update-Prüfung fehlgeschlagen: " + ex.Message, "OK"); return; }
            var current = Update.UpdateChecker.ParseVersion(AppInfo.VersionString) ?? new Version(0, 0, 0);
            if (!Update.UpdateChecker.IsNewer(info, current))
            {
                if (manual) await DisplayAlertAsync("Update", $"Die App ist aktuell (Version {current.ToString(3)}).", "OK");
                return;
            }
            if (!manual && _t.SkipUpdateVersion == info.Tag) return;
            bool busy = _running != null || _queue.Count > 0 || _lost.Count > 0;
            if (busy && !manual) return;   // beim nächsten Start bzw. über den Knopf

            const string now = "Jetzt aktualisieren", skip = "Diese Version überspringen";
            string notes = Update.UpdateChecker.ShortNotes(info.Notes, 500);
            string? pick = await DisplayActionSheetAsync($"Update auf {info.VersionText} (installiert {current.ToString(3)})\n\n{notes}", "Später", null, now, skip);
            if (pick == skip) { _t.SkipUpdateVersion = info.Tag; SaveState(); return; }
            if (pick != now) return;
            if (_running != null || _queue.Count > 0 || _lost.Count > 0)
            {
                await DisplayAlertAsync("Update", "Es laufen noch Aufträge. Bitte warten, bis die Warteschlange leer ist, und dann „Nach Updates suchen“ tippen.", "OK");
                return;
            }
            if (info.AndroidApk is not { } apk) { await DisplayAlertAsync("Update", "Im Release fehlt die APK.", "OK"); return; }
            if (!ApkInstaller.CanInstall())
            {
                await DisplayAlertAsync("Update", "Android fragt einmalig, ob diese App Updates installieren darf. Auf der folgenden Seite „Dieser Quelle vertrauen“ einschalten, zurückkehren und erneut „Nach Updates suchen“ tippen.", "Weiter");
                ApkInstaller.OpenInstallPermission();
                return;
            }

            string path = Path.Combine(ApkInstaller.DownloadDir, apk.Name);
            _status.Text = $"Lade Update {info.VersionText} …";
            await Update.UpdateChecker.DownloadAsync(apk, path, new Progress<double>(f => _status.Text = $"Lade Update {info.VersionText} … {f:P0}"));
            SaveState();
            _status.Text = $"Update {info.VersionText} geladen – bitte im Android-Dialog „Installieren“ tippen.";
            ApkInstaller.Install(path);
        }
        catch (Exception ex) { _status.Text = "Update fehlgeschlagen: " + ex.Message; }
        finally { _updating = false; }
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
        _t.Swarm = imp.Swarm; _t.Source = imp.Source; _t.Format = imp.Format; _t.Portrait = imp.Portrait; _t.TemplateName = imp.TemplateName; _t.HistoryPromptsOnly = imp.HistoryPromptsOnly;
        _t.Save();
        ShowSettings();
        RefreshProfiles();
        await ConnectAsync();
        _status.Text = "Sicherung geladen.";
    }

    private async Task OpenResultAsync(ResultItem clicked)
    {
        // alle fertigen Bilder in Listenreihenfolge, damit man im Viewer weiterblättern kann
        var items = _results.Where(x => x.Done && x.LocalPath != null && File.Exists(x.LocalPath)).ToList();
        int index = items.IndexOf(clicked);
        if (index < 0) { items = new List<ResultItem> { clicked }; index = 0; }
        var (action, r) = await ViewerPage.ShowAsync(Navigation, items, index);
        switch (action)
        {
            case ViewerAction.Refine: await RefineAsync(r); break;
            case ViewerAction.UseSeed: _seed.Text = r.Seed.ToString(); _status.Text = $"Seed {r.Seed} übernommen."; break;
            case ViewerAction.UseAsImage: await UseImageAsync(await File.ReadAllBytesAsync(r.LocalPath!)); _status.Text = "Ergebnis als eigenes Bild gesetzt."; break;
            case ViewerAction.ToggleStar: await ToggleStarAsync(r); break;
            case ViewerAction.Remove:
                _results.Remove(r);
                if (_t.ResultFolder != null && ResultMirror.HasAccess(_t.ResultFolder)) DeleteLocal(r);   // liegt im Ergebnis-Ordner, die lokale Kopie ist nur Zwischenspeicher
                SaveIndex();
                break;
        }
    }

    /// <summary>Stern in SwarmUI umschalten (Bild landet dort zusätzlich im Ordner „Starred“).</summary>
    private async Task ToggleStarAsync(ResultItem r)
    {
        if (r.ServerPath == null) { _status.Text = "Für dieses Bild ist der Pfad in SwarmUI unbekannt (vor 0.22.10 erzeugt)."; return; }
        if (!await EnsureClientAsync()) return;
        try
        {
            r.Starred = await _client!.ToggleStarAsync(r.ServerPath);
            if (r.LocalPath != null) ResultStore.Save(r.LocalPath, r.Caption, r.Seed, r.Sent, r.ServerPath, r.Starred);
            _status.Text = r.Starred ? "Stern gesetzt – in SwarmUI unter „Starred“." : "Stern entfernt.";
        }
        catch (Exception ex) { _status.Text = "Stern nicht gesetzt: " + ex.Message; }
    }

    /// <summary>Sterne aus SwarmUI übernehmen (auch dort gesetzte oder entfernte).</summary>
    private async Task SyncStarsAsync()
    {
        if (_client == null) return;
        try
        {
            var starred = await _client.ListStarredAsync();
            foreach (var r in _results.Where(x => x.Done && x.ServerPath != null))
            {
                bool s = starred.Contains(_client.RelativePath(r.ServerPath!));
                if (s == r.Starred) continue;
                r.Starred = s;
                if (r.LocalPath != null) ResultStore.Save(r.LocalPath, r.Caption, r.Seed, r.Sent, r.ServerPath, s);
            }
        }
        catch { /* Sterne sind nur Zusatz */ }
    }

    private static void DeleteLocal(ResultItem r)
    {
        try { File.Delete(r.LocalPath!); File.Delete(r.LocalPath + ".json"); } catch { }
    }

    private async Task ClearResultsAsync()
    {
        int done = _results.Count(r => r.Done);
        if (done == 0) return;
        bool mirrored = _t.ResultFolder != null && ResultMirror.HasAccess(_t.ResultFolder);
        string where = mirrored ? $"Die Bilder bleiben im Ergebnis-Ordner ({ResultMirror.SubFolder}) und in der SwarmUI-Historie." : "Die Bilder bleiben in der SwarmUI-Historie. Die lokalen Kopien auf dem Tablet werden gelöscht.";
        if (!await DisplayAlertAsync("Liste leeren", $"Alle {done} fertigen Ergebnisse aus der Liste entfernen? Laufende und wartende Aufträge bleiben.\n\n{where}", "Leeren", "Abbrechen")) return;
        if (mirrored) await _syncLock.WaitAsync();   // laufenden Upload abwarten, bevor die lokalen Kopien verschwinden
        try
        {
            foreach (var r in _results.Where(r => r.Done).ToList()) { if (r.LocalPath != null) DeleteLocal(r); _results.Remove(r); }
            ResultStore.SaveIndex(AndroidStore.ResultsFolder, Array.Empty<string>());
        }
        finally { if (mirrored) _syncLock.Release(); }
        _ = SyncResultsAsync(merge: false, report: false);
    }
}
