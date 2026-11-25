using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using PoseStudio.Model;
using PoseStudio.Rendering;

namespace PoseStudio;

public partial class MainWindow : Window, PoseStudio.Swarm.ISwarmScene
{
    /// <summary>Aktive Figur (wird bearbeitet). Alle Figuren der Szene stehen in _figures.</summary>
    private Mannequin _figure = new();
    private readonly List<Mannequin> _figures = new();
    private readonly Model3DGroup _figuresGroup = new();
    private readonly OrbitCamera _cam = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Model3DGroup _scene = new();
    private readonly Model3DGroup _lights = new();
    private readonly Model3DGroup _floor = new();
    private readonly ObservableCollection<AxisViewModel> _axes = new();
    private readonly ObservableCollection<AxisViewModel> _positionAxes = new();
    private readonly ObservableCollection<AxisViewModel> _cameraAxes = new();
    private readonly ObservableCollection<FormatOption> _exportFormats = new();
    private readonly Stack<PoseData> _undo = new();
    private readonly ObservableCollection<AxisViewModel> _proportionAxes = new();
    private readonly ObservableCollection<AxisViewModel> _shapeAxes = new();
    private Proportions _props = new();
    private HairSettings _hair = new();
    private readonly ObservableCollection<AxisViewModel> _hairAxes = new();
    private bool _suppressHair;
    private Color _background;
    private bool _ready;
    private bool _suppressFigure;
    private BitmapSource? _bgImage;
    private LightSettings _light = new();
    private readonly ObservableCollection<AxisViewModel> _lightAxes = new();
    private readonly Model3DGroup _lightMarker = new();
    private bool _floorPending, _lightDrag;
    private string? _bgImagePath;

    private static readonly (FigureStyle Style, string Name)[] Styles =
    {
        (FigureStyle.Mannequin, "Gliederpuppe"),
        (FigureStyle.Male, "Mensch – männlich"),
        (FigureStyle.Female, "Mensch – weiblich"),
        (FigureStyle.AnimeFemale, "Anime – weiblich"),
        (FigureStyle.AnimeMale, "Anime – männlich"),
    };
    private static readonly (Stretch Mode, string Name)[] StretchModes =
    {
        (Stretch.UniformToFill, "Füllen (zuschneiden)"),
        (Stretch.Uniform, "Einpassen"),
        (Stretch.Fill, "Strecken"),
    };
    private static readonly Color MannequinColor = Color.FromRgb(0xC9, 0xB2, 0x92);
    private static readonly Color SkinColor = Color.FromRgb(0xE3, 0xBE, 0xA2);
    private static readonly Color AnimeSkinColor = Color.FromRgb(0xF8, 0xE2, 0xD4);
    private static readonly string[] HairPresets = { "#3A2A20", "#151515", "#7A4A25", "#D9B866", "#A33A1E", "#9C9C9C" };

    // Maus
    private Point _mouseStart, _mouseLast;
    private bool _leftDown, _rightDown, _dragging, _jointDrag, _propDrag, _figureDrag;
    private Point3D? _lastGroundHit;
    private GeometryModel3D? _pressedPart;

    private static readonly string[] BgPresets = { "#FFFFFF", "#E6E6E6", "#9E9E9E", "#3A3A3A", "#000000", "#00B140", "#2B6CB0" };
    private static readonly string[] BodyPresets = { "#C9B292", "#BDBDBD", "#7A7A7A", "#F2F2F2", "#5C8DD6" };

    public MainWindow()
    {
        if (!Theme.Locked) Theme.Apply(_settings.UiTheme);
        InitializeComponent();
        ThemeCombo.ItemsSource = Theme.Modes;
        ThemeCombo.SelectedItem = Theme.Mode;
        PortableCheck.IsChecked = AppPaths.IsPortable;
        Title = $"Pose Studio {GetVersion()}";

        _scene.Children.Add(_lights);
        _scene.Children.Add(_setGroup);
        _scene.Children.Add(_floor);
        _figures.Add(_figure);
        _figuresGroup.Children.Add(_figure.Group);
        _scene.Children.Add(_figuresGroup);
        SceneVisual.Content = _scene;

        // Vorlagen nach Kategorie gruppiert (Reihenfolge der Kategorien wie im ersten Auftreten)
        RefreshPresetCombo();
        PresetCombo.SelectedIndex = 0;
        JointList.ItemsSource = _figure.Joints.Where(j => !j.IsFinger).ToList();
        AxisItems.ItemsSource = _axes;
        PositionItems.ItemsSource = _positionAxes;
        CameraItems.ItemsSource = _cameraAxes;
        ProportionItems.ItemsSource = _proportionAxes;
        ShapeItems.ItemsSource = _shapeAxes;
        InitHands();
        FigureStyleCombo.ItemsSource = Styles.Select(s => s.Name).ToList();
        BgStretchCombo.ItemsSource = StretchModes.Select(s => s.Name).ToList();

        BuildPositionAxes();
        BuildCameraAxes();
        BuildProportionAxes();
        InitHair();
        InitClothes();
        LookCombo.ItemsSource = Looks;
        PreviewMouseLeftButtonDown += Window_PreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += Window_PreviewMouseLeftButtonUp;
        // Maustaste außerhalb des Fensters losgelassen: Ziehen trotzdem beenden
        Deactivated += (_, _) => Window_PreviewMouseLeftButtonUp(this, null!);
        PreviewMouseMove += (_, e2) => { if (_interacting && e2.LeftButton == MouseButtonState.Released) Window_PreviewMouseLeftButtonUp(this, null!); };
        DynamicsCombo.ItemsSource = new[] { "Live (immer neu berechnen)", "Beim Ziehen einfrieren", "Eingefroren" };
        BuildLightAxes();
        InitSetUi();
        InitPropUi();
        LightItems.ItemsSource = _lightAxes;
        _scene.Children.Add(_lightMarker);
        BuildColorPresets();
        bool restored = LoadSettings();

        if (!restored)
        {
            _figure.SetPose(Presets.All[0].ToPose());
            _figure.GroundToFloor();
        }
        RefreshFigureList();
        _ready = true;
        ApplyDynamicsMode();
        foreach (var f in _exportFormats) f.PropertyChanged += (_, _) => DrawGuides();

        CustomWBox.LostFocus += CustomSize_LostFocus;
        // Alt allein soll nicht das Systemmenü aktivieren (Alt + ziehen bewegt das Licht).
        PreviewKeyDown += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        Closing += (_, _) => { if (_noSaveOnClose) return; try { SaveSettings(); } catch { /* beim Beenden nie blockieren */ } };
        Closed += (_, _) => ShutdownCleanly();
        Loaded += (_, _) => { UpdateFrameSize(); RefreshAll(); };
        if (App.NormalStart)
            ContentRendered += async (_, _) =>
            {
                Update.WindowsUpdater.CleanupOld();
                if (!_settings.CheckUpdates) return;
                await Task.Delay(2500);
                await Update.WindowsUpdater.CheckAsync(this, _settings, false, RestartForUpdate);
            };
    }

    private bool _noSaveOnClose;

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is string m && m != Theme.Mode) Theme.Apply(m);
    }

    private void Portable_Click(object sender, RoutedEventArgs e)
    {
        bool on = PortableCheck.IsChecked == true;
        string where = on ? AppPaths.PortableDir : AppPaths.RoamingDir;
        var answer = MessageBox.Show(this, on
                ? $"Einstellungen und eigene Vorlagen ab jetzt im Programmordner speichern?\n\n{AppPaths.PortableDir}\n\nDie bisherigen Daten in %APPDATA% bleiben als Sicherung liegen."
                : $"Einstellungen und eigene Vorlagen wieder im Benutzerprofil speichern?\n\n{AppPaths.RoamingDir}\n\nDie dortige alte settings.json wird als .bak gesichert, der Ordner „Daten“ im Programmordner umbenannt (nicht gelöscht).",
            "Portabel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) { PortableCheck.IsChecked = AppPaths.IsPortable; return; }
        string? err = on ? AppPaths.EnablePortable() : AppPaths.DisablePortable();
        if (err != null) { MessageBox.Show(this, err, "Portabel", MessageBoxButton.OK, MessageBoxImage.Warning); PortableCheck.IsChecked = AppPaths.IsPortable; return; }
        SaveSettings();
        StatusText.Text = "Einstellungen liegen jetzt in " + where;
        RefreshPresetCombo();
    }

    /// <summary>Testmodus --theme-shot: Haupt- und SwarmUI-Fenster außerhalb des Bildschirms anzeigen und als PNG speichern (schreibt keine Einstellungen).</summary>
    internal async Task ThemeShotAsync(string dir, string tag)
    {
        _noSaveOnClose = true;
        Directory.CreateDirectory(dir);
        WindowStartupLocation = WindowStartupLocation.Manual; ShowActivated = false; ShowInTaskbar = false;
        Width = 1520; Height = 920; Left = -20000; Top = 0;
        Show(); Left = -20000;
        await Task.Delay(2500);
        SaveWindowShot(this, Path.Combine(dir, tag + "-main.png"));
        PoseStudio.Swarm.SwarmWindow.OutputOverride = Path.Combine(Path.GetTempPath(), "PoseStudioThemeShot");
        var sw = new PoseStudio.Swarm.SwarmWindow(_swarmSettings, this) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        sw.Show(); sw.Left = -20000; sw.Top = 0;
        await Task.Delay(2500);
        SaveWindowShot(sw, Path.Combine(dir, tag + "-swarm.png"));
        sw.ShowSegmentsForShot();   // Refiner-Bereich mit Beispielzeilen sichtbar machen (nur im Speicher, wird nicht gespeichert)
        await Task.Delay(1500);
        SaveWindowShot(sw, Path.Combine(dir, tag + "-swarm-refiner.png"));
        sw.ShowCharacterForShot();   // Charakter-Chips (nur im Speicher)
        await Task.Delay(1500);
        SaveWindowShot(sw, Path.Combine(dir, tag + "-swarm-character.png"));
        sw.ShowSuggestForShot();   // Tag-Vorschläge unter dem Prompt (nur im Speicher)
        await Task.Delay(1500);
        SaveWindowShot(sw, Path.Combine(dir, tag + "-swarm-suggest.png"));
        sw.ShowLorasForShot();   // LoRAs mit Tag-Auswahl (nur im Speicher)
        await Task.Delay(1500);
        SaveWindowShot(sw, Path.Combine(dir, tag + "-swarm-loras.png"));
    }

    internal static void SaveWindowShot(Window w, string path)
    {
        var v = (FrameworkElement)w.Content;
        int pw = (int)Math.Ceiling(v.ActualWidth), ph = (int)Math.Ceiling(v.ActualHeight);
        var rtb = new RenderTargetBitmap(Math.Max(1, pw), Math.Max(1, ph), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(w.Background ?? Brushes.Transparent, null, new Rect(0, 0, pw, ph));
            dc.DrawRectangle(new VisualBrush(v), null, new Rect(0, 0, pw, ph));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path); enc.Save(fs);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await Update.WindowsUpdater.CheckAsync(this, _settings, true, RestartForUpdate);

    /// <summary>Nach dem Update: Einstellungen sichern, neue Version starten, dieses Fenster schließen.</summary>
    private void RestartForUpdate(string message)
    {
        SaveSettings();
        _noSaveOnClose = true;
        MessageBox.Show(this, message, "Pose Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Update.WindowsUpdater.AppDir, Update.WindowsUpdater.ExeName)) { UseShellExecute = false, WorkingDirectory = Update.WindowsUpdater.AppDir }); }
        catch (Exception ex) { MessageBox.Show(this, "Neustart fehlgeschlagen – bitte Pose Studio selbst starten: " + ex.Message, "Pose Studio"); }
        Close();
    }

    private static string GetVersion()
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // =================================================================== Aufbau

    private void BuildPositionAxes()
    {
        _positionAxes.Clear();
        _positionAxes.Add(new AxisViewModel("Links/rechts (cm)", -200, 200,
            () => _figure.RootPosition.X * 100, v => { var p = _figure.RootPosition; _figure.RootPosition = new Vector3D(v / 100, p.Y, p.Z); PoseChanged(false); }));
        _positionAxes.Add(new AxisViewModel("Höhe (cm)", -150, 150,
            () => _figure.RootPosition.Y * 100, v => { var p = _figure.RootPosition; _figure.RootPosition = new Vector3D(p.X, v / 100, p.Z); PoseChanged(false); }));
        _positionAxes.Add(new AxisViewModel("Vor/zurück (cm)", -200, 200,
            () => _figure.RootPosition.Z * 100, v => { var p = _figure.RootPosition; _figure.RootPosition = new Vector3D(p.X, p.Y, v / 100); PoseChanged(false); }));
    }

    private void BuildCameraAxes()
    {
        _cameraAxes.Clear();
        _cameraAxes.Add(new AxisViewModel("Entfernung (cm)", OrbitCamera.MinDistance * 100, OrbitCamera.MaxDistance * 100,
            () => _cam.Distance * 100, v => { _cam.Distance = v / 100; CameraChanged(); }));
        _cameraAxes.Add(new AxisViewModel("Brennweite (mm)", 14, 200,
            () => _cam.FocalLength, v => { _cam.FocalLength = v; CameraChanged(); }));
        _cameraAxes.Add(new AxisViewModel("Blickhöhe (cm)", -50, 250,
            () => _cam.Target.Y * 100, v => { _cam.Target = new Point3D(_cam.Target.X, v / 100, _cam.Target.Z); CameraChanged(); }));
        _cameraAxes.Add(new AxisViewModel("Drehung (°)", -180, 180,
            () => NormalizeAngle(_cam.Yaw), v => { _cam.Yaw = v; CameraChanged(); }));
        _cameraAxes.Add(new AxisViewModel("Neigung (°)", -85, 85,
            () => _cam.Pitch, v => { _cam.Pitch = v; CameraChanged(); }));
    }

    private void BuildProportionAxes()
    {
        _proportionAxes.Clear();
        void Add(string label, double min, double max, Func<double> get, Action<double> set) =>
            _proportionAxes.Add(new AxisViewModel(label, min, max, get, v => { set(v); ApplyFigure(); }));
        Add("Körpergröße (cm)", 50, 230, () => _props.HeightCm, v => _props.HeightCm = v);
        Add("Armlänge (%)", 70, 130, () => _props.Arm, v => _props.Arm = v);
        Add("Beinlänge (%)", 70, 130, () => _props.Leg, v => _props.Leg = v);
        Add("Oberkörper (%)", 75, 125, () => _props.Torso, v => _props.Torso = v);
        Add("Schulterbreite (%)", 75, 130, () => _props.Shoulder, v => _props.Shoulder = v);
        Add("Kopfgröße (%)", 75, 140, () => _props.Head, v => _props.Head = v);
        Add("Körperbau (%)", 70, 150, () => _props.Build, v => _props.Build = v);
        void S(string label, double min, double max, Func<double> get, Action<double> set) =>
            _shapeAxes.Add(new AxisViewModel(label, min, max, get, v => { set(v); ApplyFigure(); }));
        _shapeAxes.Clear();
        S("Brust (%)", 30, 220, () => _props.Bust, v => _props.Bust = v);
        S("Taille (%)", 70, 140, () => _props.Waist, v => _props.Waist = v);
        S("Hüfte (%)", 75, 150, () => _props.Hips, v => _props.Hips = v);
        S("Gesäß (%)", 60, 170, () => _props.Glutes, v => _props.Glutes = v);
        S("Bauch (%)", 70, 180, () => _props.Belly, v => _props.Belly = v);
        S("Oberschenkel (%)", 70, 160, () => _props.Thighs, v => _props.Thighs = v);
        S("Waden (%)", 70, 160, () => _props.Calves, v => _props.Calves = v);
        S("Oberarme (%)", 70, 170, () => _props.UpperArms, v => _props.UpperArms = v);
        S("Halslänge (%)", 60, 150, () => _props.Neck, v => _props.Neck = v);
        S("Handgröße (%)", 75, 130, () => _props.Hands, v => _props.Hands = v);
    }

    // =================================================================== Mehrere Figuren

    private sealed record FigureItem(string Name, Brush Swatch);

    private Mannequin? OwnerOf(GeometryModel3D g) =>
        _figures.FirstOrDefault(fg => fg.TryGetJoint(g, out _) || fg.IsPropPart(g));

    private PoseData FigureToData(Mannequin m)
    {
        var p = m.GetPose();
        p.ReferenceImage = _refPaths.TryGetValue(m, out var refPath) ? refPath : null;
        p.Figure = m.GetFigure();
        p.BodyColor = ToHex(m.BodyColor);
        p.HairColor = ToHex(m.HairColor);
        return p;
    }

    private static void DataToFigure(Mannequin m, PoseData d)
    {
        if (d.Figure != null) m.SetFigure(d.Figure.Style, d.Figure.Hair, d.Figure.Proportions);
        if (d.BodyColor != null) m.BodyColor = ParseColor(d.BodyColor, m.BodyColor);
        if (d.HairColor != null) m.HairColor = ParseColor(d.HairColor, m.HairColor);
        m.SetPose(d);
    }

    /// <summary>Szene aus gespeicherten Figuren aufbauen (Einstellungen oder Pose-Datei).</summary>
    private void RestoreFigures(List<PoseData> list, int active)
    {
        _figure.Select(null);
        _figure.SelectProp(false);
        _figures.Clear();
        _figuresGroup.Children.Clear();
        _refPaths.Clear();
        _refDetections.Clear();
        foreach (var d in list)
        {
            var m = new Mannequin();
            DataToFigure(m, d);
            if (!string.IsNullOrEmpty(d.ReferenceImage) && File.Exists(d.ReferenceImage)) _refPaths[m] = d.ReferenceImage;
            _figures.Add(m);
            _figuresGroup.Children.Add(m.Group);
        }
        _figure = _figures[Math.Clamp(active, 0, _figures.Count - 1)];
        ActivateFigure(_figures.IndexOf(_figure));
    }

    private void RefreshFigureList()
    {
        if (_ready) ApplyDynamicsMode();
        _suppressFigureList = true;
        FigureList.ItemsSource = _figures.Select((fg, i) =>
        {
            string style = fg.Style switch { FigureStyle.Male => "Mann", FigureStyle.Female => "Frau", FigureStyle.AnimeFemale => "Anime w", FigureStyle.AnimeMale => "Anime m", _ => "Gliederpuppe" };
            string prop = fg.Prop.Active ? " + Objekt" : "";
            return new FigureItem($"Figur {i + 1} – {style}{prop}", new SolidColorBrush(SceneFactory.FigurePalette[i % SceneFactory.FigurePalette.Length]));
        }).ToList();
        FigureList.SelectedIndex = _figures.IndexOf(_figure);
        _suppressFigureList = false;
    }

    private bool _suppressFigureList;

    /// <summary>Figur zur aktiven machen: alle Bedienelemente zeigen danach deren Werte.</summary>
    private void ActivateFigure(int index)
    {
        if (index < 0 || index >= _figures.Count) return;
        _figure.Select(null);
        _figure.SelectProp(false);
        _figure = _figures[index];
        JointList.ItemsSource = _figure.Joints.Where(j => !j.IsFinger).ToList();
        _axes.Clear();
        SelectedJointText.Text = "– Gelenk anklicken –";
        ShowFigure(_figure.GetFigure());
        SetBodyColor(_figure.BodyColor);
        SetHairColor(_figure.HairColor);
        ShowProp();
        foreach (var a in _positionAxes) a.Refresh();
        _undo.Clear(); // Rückgängig gilt je Figur
        ShowReferenceForActive();
        RefreshFigureList();
        StatusText.Text = $"Figur {index + 1} ausgewählt.";
    }

    private void FigureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFigureList || FigureList.SelectedIndex < 0) return;
        ActivateFigure(FigureList.SelectedIndex);
    }

    private void AddFigureFrom(PoseData data)
    {
        var m = new Mannequin();
        DataToFigure(m, data);
        // rechts neben die bisherigen Figuren stellen
        double maxX = _figures.Max(fg => fg.PelvisWorld.X);
        m.RootPosition = new Vector3D(maxX + 0.75, m.RootPosition.Y, _figure.RootPosition.Z);
        m.GroundToFloor();
        _figures.Add(m);
        _figuresGroup.Children.Add(m.Group);
        ActivateFigure(_figures.Count - 1);
        PoseChanged();
    }

    private void AddFigure_Click(object sender, RoutedEventArgs e)
    {
        var d = Presets.All[0].ToPose();
        d.Figure = _figure.GetFigure();
        d.BodyColor = ToHex(_figure.BodyColor);
        d.HairColor = ToHex(_figure.HairColor);
        d.Prop = new PropSettings();
        AddFigureFrom(d);
    }

    private void DuplicateFigure_Click(object sender, RoutedEventArgs e)
    {
        var d = FigureToData(_figure);
        d.ReferenceImage = null; // Referenzfoto gehört nur zur Originalfigur
        AddFigureFrom(d);
    }

    private void RemoveFigure_Click(object sender, RoutedEventArgs e)
    {
        if (_figures.Count <= 1) { StatusText.Text = "Die letzte Figur kann nicht entfernt werden."; return; }
        int i = _figures.IndexOf(_figure);
        _figure.Select(null);
        _figuresGroup.Children.Remove(_figure.Group);
        _refPaths.Remove(_figure);
        _refDetections.Remove(_figure);
        _figures.RemoveAt(i);
        _figure = _figures[Math.Max(0, i - 1)];
        ActivateFigure(_figures.IndexOf(_figure));
        PoseChanged();
    }

    // =================================================================== Objekt in der Hand

    private PropSettings _propUi = new();
    private readonly ObservableCollection<AxisViewModel> _propAxes = new();
    private bool _suppressProp;

    private static readonly (PropType Type, string Name)[] PropTypes =
    {
        (PropType.None, "Keins"), (PropType.Sword, "Schwert"), (PropType.Wand, "Zauberstab"),
        (PropType.Stick, "Stock"), (PropType.Staff, "Langer Stab"), (PropType.Spear, "Speer"),
    };
    private static readonly (HoldMode Mode, string Name)[] HoldModes =
    {
        (HoldMode.Right, "Rechte Hand"), (HoldMode.Left, "Linke Hand"),
        (HoldMode.BothRightTop, "Beide Hände (rechte oben)"), (HoldMode.BothLeftTop, "Beide Hände (linke oben)"),
    };

    private void InitPropUi()
    {
        PropCombo.ItemsSource = PropTypes.Select(p => p.Name).ToList();
        HoldCombo.ItemsSource = HoldModes.Select(h => h.Name).ToList();
        PropItems.ItemsSource = _propAxes;
        void Add(string label, double min, double max, Func<double> get, Action<double> set) =>
            _propAxes.Add(new AxisViewModel(label, min, max, get, v => { set(v); ApplyProp(); }));
        Add("Vor/zurück (cm)", -100, 120, () => _propUi.Z, v => _propUi.Z = v);
        Add("Höhe (cm)", -150, 120, () => _propUi.Y, v => _propUi.Y = v);
        Add("Links/rechts (cm)", -120, 120, () => _propUi.X, v => _propUi.X = v);
        Add("Spitze vor/zurück (°)", -180, 180, () => _propUi.Pitch, v => _propUi.Pitch = v);
        Add("Spitze seitlich (°)", -90, 90, () => _propUi.Tilt, v => _propUi.Tilt = v);
        Add("Drehen (°)", -180, 180, () => _propUi.Yaw, v => _propUi.Yaw = v);
        Add("Um eigene Achse (°)", -180, 180, () => _propUi.Roll, v => _propUi.Roll = v);
        Add("Griffabstand (cm)", 8, 80, () => _propUi.GripSpacing, v => _propUi.GripSpacing = v);
    }

    /// <summary>Bedienelemente auf den Stand der Figur bringen (nach Laden, Rückgängig, Spiegeln).</summary>
    private void ShowProp()
    {
        _suppressProp = true;
        _propUi = _figure.Prop;
        PropCombo.SelectedIndex = Math.Max(0, Array.FindIndex(PropTypes, p => p.Type == _propUi.Type));
        HoldCombo.SelectedIndex = Math.Max(0, Array.FindIndex(HoldModes, h => h.Mode == _propUi.Hold));
        PropPanel.Visibility = _propUi.Active ? Visibility.Visible : Visibility.Collapsed;
        foreach (var a in _propAxes) a.Refresh();
        _suppressProp = false;
        UpdatePropStatus();
    }

    private void ApplyProp()
    {
        if (_suppressProp) return;
        _figure.SetProp(_propUi);
        PoseChanged();
        UpdatePropStatus();
    }

    private void UpdatePropStatus()
    {
        PropStatus.Text = _propUi.Active && _figure.LastGripError > 0.03
            ? "Griff nicht erreichbar – Objekt näher an den Körper bewegen."
            : "";
    }

    private void PropType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProp || PropCombo.SelectedIndex < 0) return;
        PushUndo();
        var type = PropTypes[PropCombo.SelectedIndex].Type;
        bool wasActive = _propUi.Active;
        _propUi.Type = type;
        // Typische Haltung je Objekt, beim ersten Aufnehmen bzw. Wechsel des Objekts
        if (type is PropType.Staff or PropType.Spear && !wasActive) _propUi.Hold = HoldMode.BothRightTop;
        if (type != PropType.None) _propUi.ApplyDefaultPlacement();
        ShowPropAfterChange();
    }

    private void Hold_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProp || HoldCombo.SelectedIndex < 0) return;
        PushUndo();
        _propUi.Hold = HoldModes[HoldCombo.SelectedIndex].Mode;
        _propUi.ApplyDefaultPlacement();
        ShowPropAfterChange();
    }

    private void PropDefault_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _propUi.ApplyDefaultPlacement();
        ShowPropAfterChange();
    }

    private void ShowPropAfterChange()
    {
        _figure.SetProp(_propUi);
        PoseChanged();
        ShowProp();
        RefreshFigureList();
    }

    private void SelectPropInView()
    {
        SelectJoint(null);
        _figure.SelectProp(true);
        SelectedJointText.Text = "Objekt ausgewählt – Strg + ziehen verschiebt, Umschalt + ziehen dreht";
    }

    /// <summary>Objekt mit der Maus bewegen: Strg = in der Bildebene verschieben, Umschalt = drehen.</summary>
    private void DragProp(Vector d)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _propUi.Yaw = NormalizeAngle(_propUi.Yaw + d.X * 0.6);
            _propUi.Pitch = NormalizeAngle(_propUi.Pitch + d.Y * 0.6);
        }
        else
        {
            var pos = _figure.PropWorld.Transform(new Point3D(0, 0, 0));
            double z = Math.Max(0.2, Vector3D.DotProduct(pos - _cam.Position, _cam.LookDirection));
            double px = z * Math.Tan(_cam.VerticalFov * Math.PI / 360) * 2 / Math.Max(1, View.ActualHeight);
            var world = _cam.Right * d.X * px - _cam.Up * d.Y * px;
            var inv = _figure.ChestWorld;
            inv.Invert();
            var local = inv.Transform(world); // Bewegung im Brustkorb-System (das Objekt hängt am Oberkörper)
            _propUi.X += local.X * 100;
            _propUi.Y += local.Y * 100;
            _propUi.Z += local.Z * 100;
        }
        _propUi.Clamp();
        _figure.SetProp(_propUi);
        PoseChanged(false);
        foreach (var a in _propAxes) a.Refresh();
        UpdatePropStatus();
    }

    // =================================================================== Kulisse

    private SetSettings _set = new();
    private readonly Model3DGroup _setGroup = new();
    private readonly ObservableCollection<AxisViewModel> _setAxes = new();
    private bool _suppressSet;

    private static readonly (SetType Type, string Name)[] SetTypes =
    {
        (SetType.None, "Keine (einfarbig / Bild)"),
        (SetType.Studio, "Fotostudio (Hohlkehle)"),
        (SetType.Meadow, "Draußen – Wiese"),
        (SetType.Beach, "Draußen – Strand"),
        (SetType.Desert, "Draußen – Wüste"),
        (SetType.Snow, "Draußen – Schnee"),
        (SetType.Room, "Innenraum"),
        (SetType.Street, "Straße"),
    };
    private static readonly (string Key, string Name)[] FloorTypes =
    {
        ("Wood", "Holzdielen"), ("Tiles", "Fliesen"), ("Concrete", "Beton"), ("Carpet", "Teppich"),
    };
    private static readonly string[] SetColorPresets = { "#D9D5CE", "#F2F0EB", "#8E8A84", "#3A3A3C", "#C9D6E3", "#E8D9C4", "#BFD3C1" };

    private void InitSetUi()
    {
        SetCombo.ItemsSource = SetTypes.Select(s => s.Name).ToList();
        SetFloorCombo.ItemsSource = FloorTypes.Select(f => f.Name).ToList();
        SetItems.ItemsSource = _setAxes;
        foreach (var hex in SetColorPresets)
            SetColorPresetPanel.Children.Add(MakeSwatchButton(hex, c => { _set.Color = ToHex(c); ApplySet(); }));
    }

    private void ShowSet(SetSettings s)
    {
        _suppressSet = true;
        _set = s.Clone();
        _set.Clamp();
        SetCombo.SelectedIndex = Math.Max(0, Array.FindIndex(SetTypes, t => t.Type == _set.Type));
        SetFloorCombo.SelectedIndex = Math.Max(0, Array.FindIndex(FloorTypes, f => f.Key == _set.Floor));
        HorizonCheck.IsChecked = _set.HorizonLine;
        _suppressSet = false;
        ApplySet();
    }

    /// <summary>Kulisse neu aufbauen und Bedienelemente an den Typ anpassen.</summary>
    private void ApplySet()
    {
        if (_suppressSet) return;
        var t = _set.Type;
        SetColorRow.Visibility = t is SetType.Studio or SetType.Room ? Visibility.Visible : Visibility.Collapsed;
        SetColorLabel.Text = t == SetType.Room ? "Wandfarbe" : "Studiofarbe";
        SetSwatch.Background = new SolidColorBrush(ParseColor(_set.Color, Colors.LightGray));
        SetFloorRow.Visibility = t == SetType.Room ? Visibility.Visible : Visibility.Collapsed;

        _setAxes.Clear();
        if (t == SetType.Room)
            _setAxes.Add(new AxisViewModel("Raumbreite (m)", 3, 14, () => _set.RoomWidth, v => { _set.RoomWidth = v; ApplySet(); }));
        if (t is SetType.Room or SetType.Studio)
            _setAxes.Add(new AxisViewModel("Abstand Rückwand (m)", 1.2, 12, () => _set.WallDistance, v => { _set.WallDistance = v; ApplySet(); }));

        _setGroup.Children.Clear();
        if (t != SetType.None) _setGroup.Children.Add(SetBuilder.Get(_set).Model);
        UpdateFileTypeUi();   // Transparenz ist nur ohne Kulisse möglich; aktualisiert auch den Hintergrund
        RebuildFloor();
        DrawGuides();
    }

    private void SetType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSet || SetCombo.SelectedIndex < 0) return;
        _set.Type = SetTypes[SetCombo.SelectedIndex].Type;
        ApplySet();
        if (_set.Type != SetType.None && FloorPreviewCheck.IsChecked == true)
            StatusText.Text = "Kulisse aktiv – das Bodenraster wird dabei ausgeblendet.";
    }

    private void SetFloor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSet || SetFloorCombo.SelectedIndex < 0) return;
        _set.Floor = FloorTypes[SetFloorCombo.SelectedIndex].Key;
        ApplySet();
    }

    private void PickSetColor_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(ParseColor(_set.Color, Colors.LightGray)) is { } c) { _set.Color = ToHex(c); ApplySet(); }
    }

    private void Horizon_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSet) return;
        _set.HorizonLine = HorizonCheck.IsChecked == true;
        DrawGuides();
    }

    // =================================================================== Licht

    private void BuildLightAxes()
    {
        _lightAxes.Clear();
        void Add(string label, double min, double max, Func<double> get, Action<double> set) =>
            _lightAxes.Add(new AxisViewModel(label, min, max, get, v => { set(v); LightChanged(); }));
        Add("Richtung (°)", -180, 180, () => NormalizeAngle(_light.Azimuth), v => _light.Azimuth = v);
        Add("Höhe (°)", 5, 90, () => _light.Elevation, v => _light.Elevation = v);
        Add("Helligkeit (%)", 0, 200, () => _light.Intensity, v => _light.Intensity = v);
        Add("Umgebungslicht (%)", 0, 100, () => _light.Ambient, v => _light.Ambient = v);
        Add("Schattenstärke (%)", 0, 100, () => _light.ShadowStrength, v => _light.ShadowStrength = v);
        Add("Schattenweichheit (%)", 0, 100, () => _light.Softness, v => _light.Softness = v);
        Add("Kontur Anime (‰)", 0, 8, () => _light.OutlineWidth, v => _light.OutlineWidth = v);
    }

    private bool _suppressLight;

    private void ShowLight(LightSettings l)
    {
        _suppressLight = true;
        _light = l.Clone();
        _light.Clamp();
        CastShadowCheck.IsChecked = _light.CastShadow;
        LightFollowCheck.IsChecked = _light.FollowCamera;
        ShowLightCheck.IsChecked = _light.ShowSource;
        LookCombo.SelectedIndex = _light.Toon ? 1 + _light.ToonStyle : 0;
        _suppressLight = false;
        LightChanged();
    }

    private void LightChanged()
    {
        foreach (var a in _lightAxes) a.Refresh();
        if (!_ready) return;
        _lights.Children.Clear();
        _lights.Children.Add(SceneFactory.CreateLights(_cam, _light));
        UpdateLightMarker();
        RebuildFloor();
    }

    private static readonly string[] Looks = { "Normal (3D)", "Cel-Shading (Anime)", "2D flach", "Strichzeichnung" };

    private void Look_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLight || LookCombo.SelectedIndex < 0) return;
        _light.Toon = LookCombo.SelectedIndex > 0;
        _light.ToonStyle = Math.Max(0, LookCombo.SelectedIndex - 1);
        LightChanged();
    }

    private void Light_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLight) return;
        _light.CastShadow = CastShadowCheck.IsChecked == true;
        _light.FollowCamera = LightFollowCheck.IsChecked == true;
        _light.ShowSource = ShowLightCheck.IsChecked == true;

        LightChanged();
    }

    private void LightPreset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((Button)sender).Tag).Split(',');
        _light.Azimuth = double.Parse(parts[0], CultureInfo.InvariantCulture) + (_light.FollowCamera ? 0 : _cam.Yaw);
        _light.Azimuth = NormalizeAngle(_light.Azimuth);
        _light.Elevation = double.Parse(parts[1], CultureInfo.InvariantCulture);
        LightChanged();
    }

    /// <summary>Gelbe Kugel mit Strahl zeigt in der Vorschau, woher das Licht kommt (nicht im Export).</summary>
    private void UpdateLightMarker()
    {
        _lightMarker.Children.Clear();
        if (!_ready || !_light.ShowSource) return;
        var from = _light.FromDirection(_cam);
        var pel = _figure.PelvisWorld;
        var center = new Point3D(pel.X, Math.Max(0.5, pel.Y), pel.Z);
        var sun = center + from * 1.05;
        var glow = new MaterialGroup();
        glow.Children.Add(new DiffuseMaterial(Brushes.Black));
        glow.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x30))));
        _lightMarker.Children.Add(new GeometryModel3D(MeshFactory.Sphere(sun, 0.06), glow));
        _lightMarker.Children.Add(new GeometryModel3D(MeshFactory.Tube(sun - from * 0.08, center + from * 0.45, 0.009, 0.004), glow));
    }

    // =================================================================== Haare

    private void InitHair()
    {
        HairStyleCombo.ItemsSource = new[] { "Standard (nach Figur)" }.Concat(HairStyleDef.All.Select(s => s.Name)).ToList();
        BangsCombo.ItemsSource = BangsDef.All.Select(b => b.Name).ToList();
        HairItems.ItemsSource = _hairAxes;
        void Add(string label, double min, double max, Func<double> get, Action<double> set) =>
            _hairAxes.Add(new AxisViewModel(label, min, max, get, v => { set(v); HairChanged(); }));
        Add("Länge (%)", 40, 180, () => _hair.Length, v => _hair.Length = v);
        Add("Fülle (%)", 50, 200, () => _hair.Volume, v => _hair.Volume = v);
        Add("Wind (%)", 0, 100, () => _hair.Wind, v => _hair.Wind = v);
        Add("Wehrichtung (°)", -180, 180, () => _hair.WindDirection, v => _hair.WindDirection = v);
        Add("Schwung (%)", 0, 100, () => _hair.Lift, v => _hair.Lift = v);
        Add("Unordnung (%)", 0, 100, () => _hair.Messiness, v => _hair.Messiness = v);
    }

    /// <summary>Haar-Bedienelemente auf die Einstellungen der aktiven Figur setzen.</summary>
    private void ShowHair(HairSettings? h)
    {
        _hair = h?.Clone() ?? new HairSettings();
        _hair.Clamp();
        _suppressHair = true;
        HairStyleCombo.SelectedIndex = _hair.Style == "auto" ? 0 : 1 + HairStyleDef.All.ToList().FindIndex(s => s.Id == _hair.Style);
        BangsCombo.SelectedIndex = Math.Max(0, BangsDef.All.ToList().FindIndex(b => b.Id == _hair.Bangs));
        _suppressHair = false;
        foreach (var a in _hairAxes) a.Refresh();
    }

    private void HairChanged()
    {
        if (_suppressHair || !_ready) return;
        _figure.SetHair(_hair);
        PoseChanged();
    }

    private void HairStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHair) return;
        int i = HairStyleCombo.SelectedIndex;
        _hair.Style = i <= 0 ? "auto" : HairStyleDef.All[i - 1].Id;
        if (BangsCombo.SelectedIndex >= 0) _hair.Bangs = BangsDef.All[BangsCombo.SelectedIndex].Id;
        HairChanged();
    }

    /// <summary>Schnellwahl für Wind und Schwung (Tag: Wind, Richtung, Schwung).</summary>
    private void HairPreset_Click(object sender, RoutedEventArgs e)
    {
        var p = ((string)((Button)sender).Tag).Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        _hair.Wind = p[0]; _hair.WindDirection = p[1]; _hair.Lift = p[2];
        foreach (var a in _hairAxes) a.Refresh();
        HairChanged();
    }

    private void HairReroll_Click(object sender, RoutedEventArgs e)
    {
        _hair.Seed = _hair.Seed % 9999 + 1;
        HairChanged();
    }

    // =================================================================== Kleidung

    private ClothingSettings _clothes = new();
    private readonly ObservableCollection<AxisViewModel> _clothAxes = new();
    private bool _suppressClothes;

    private void InitClothes()
    {
        TopCombo.ItemsSource = TopDef.All.Select(t => t.Name).ToList();
        BottomCombo.ItemsSource = BottomDef.All.Select(b => b.Name).ToList();
        ShoeCombo.ItemsSource = ShoeDef.All.Select(s => s.Name).ToList();
        ClothItems.ItemsSource = _clothAxes;
        _clothAxes.Add(new AxisViewModel("Weite Rock/Kleid (%)", 40, 220, () => _clothes.Fullness, v => { _clothes.Fullness = v; ClothesChanged(); }));
    }

    private void ShowClothing(ClothingSettings? c)
    {
        _clothes = c?.Clone() ?? new ClothingSettings();
        _clothes.Clamp();
        _suppressClothes = true;
        TopCombo.SelectedIndex = Math.Max(0, TopDef.All.ToList().FindIndex(t => t.Id == _clothes.Top));
        BottomCombo.SelectedIndex = Math.Max(0, BottomDef.All.ToList().FindIndex(b => b.Id == _clothes.Bottom));
        ShoeCombo.SelectedIndex = Math.Max(0, ShoeDef.All.ToList().FindIndex(s => s.Id == _clothes.Shoes));
        _suppressClothes = false;
        UpdateClothSwatches();
        foreach (var a in _clothAxes) a.Refresh();
    }

    private void UpdateClothSwatches()
    {
        TopSwatch.Background = new SolidColorBrush(ParseColor(_clothes.TopColor, Colors.White));
        BottomSwatch.Background = new SolidColorBrush(ParseColor(_clothes.BottomColor, Colors.SteelBlue));
        ShoeSwatch.Background = new SolidColorBrush(ParseColor(_clothes.ShoeColor, Colors.SaddleBrown));
    }

    private void ClothesChanged()
    {
        if (_suppressClothes || !_ready) return;
        _figure.SetClothing(_clothes);
        PoseChanged();
    }

    private void Clothing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressClothes) return;
        if (TopCombo.SelectedIndex >= 0) _clothes.Top = TopDef.All[TopCombo.SelectedIndex].Id;
        if (BottomCombo.SelectedIndex >= 0) _clothes.Bottom = BottomDef.All[BottomCombo.SelectedIndex].Id;
        if (ShoeCombo.SelectedIndex >= 0) _clothes.Shoes = ShoeDef.All[ShoeCombo.SelectedIndex].Id;
        ClothesChanged();
    }

    private void PickClothColor_Click(object sender, RoutedEventArgs e)
    {
        string which = (string)((Button)sender).Tag;
        string current = which switch { "top" => _clothes.TopColor, "bottom" => _clothes.BottomColor, _ => _clothes.ShoeColor };
        if (PickColor(ParseColor(current, Colors.Gray)) is not { } c) return;
        switch (which)
        {
            case "top": _clothes.TopColor = ToHex(c); break;
            case "bottom": _clothes.BottomColor = ToHex(c); break;
            default: _clothes.ShoeColor = ToHex(c); break;
        }
        UpdateClothSwatches();
        ClothesChanged();
    }

    /// <summary>Schnellwahl ganzer Outfits (Tag: Oberteil, Unterteil, Schuhe).</summary>
    private void Outfit_Click(object sender, RoutedEventArgs e)
    {
        var p = ((string)((Button)sender).Tag).Split(',');
        _clothes.Top = p[0]; _clothes.Bottom = p[1]; _clothes.Shoes = p[2];
        ShowClothing(_clothes);
        ClothesChanged();
    }

    // =================================================================== Haare und Stoff: live oder eingefroren

    private bool _frozenByPress, _interacting;

    private int DynamicsMode => Math.Max(0, DynamicsCombo.SelectedIndex);

    /// <summary>Einfrier-Zustand auf alle Figuren übertragen (Modus 2 = dauerhaft eingefroren).</summary>
    private void ApplyDynamicsMode()
    {
        foreach (var fg in _figures) fg.FreezeDynamics = DynamicsMode == 2 || _frozenByPress;
        RecomputeButton.IsEnabled = DynamicsMode == 2;
    }

    private void DynamicsMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _frozenByPress = false;
        ApplyDynamicsMode();
        if (DynamicsMode != 2) RecomputeDynamics_Click(this, new RoutedEventArgs());
    }

    private void RecomputeDynamics_Click(object sender, RoutedEventArgs e)
    {
        foreach (var fg in _figures) fg.RecomputeDynamics();
        RebuildFloor();
    }

    /// <summary>Solange die linke Maustaste gedrückt ist (Gelenk ziehen, Regler schieben …): Eigenschatten aussetzen,
    /// Bodenschatten gröber; im Modus „Beim Ziehen einfrieren“ zusätzlich Haare und Stoff nur starr mitführen.
    /// Beim Loslassen wird alles einmal vollständig berechnet.</summary>
    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _interacting = true;
        if (DynamicsMode != 1) return;
        _frozenByPress = true;
        ApplyDynamicsMode();
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_interacting) return;
        _interacting = false;
        if (_frozenByPress)
        {
            _frozenByPress = false;
            ApplyDynamicsMode();
            foreach (var fg in _figures) fg.RecomputeDynamics();
        }
        RebuildFloor();
    }

    // =================================================================== SwarmUI

    private PoseStudio.Swarm.SwarmSettings _swarmSettings = new();
    private PoseStudio.Swarm.SwarmWindow? _swarmWindow;

    private void OpenSwarm_Click(object sender, RoutedEventArgs e)
    {
        if (_swarmWindow is { IsLoaded: true }) { _swarmWindow.Activate(); return; }
        string? moved = PoseStudio.Swarm.SwarmWindow.MigrateLegacy(PoseStudio.Swarm.SwarmWindow.FolderFor(FolderBox.Text));
        PoseStudio.Swarm.ThumbCache.MigrateLegacy();
        _swarmWindow = new PoseStudio.Swarm.SwarmWindow(_swarmSettings, this) { Owner = this };
        _swarmWindow.Show();
        if (moved != null) _swarmWindow.ShowStatus(moved);
    }

    string PoseStudio.Swarm.ISwarmScene.ExportFolder => FolderBox.Text;

    void PoseStudio.Swarm.ISwarmScene.SaveSettingsNow()
    {
        if (!App.NormalStart || _noSaveOnClose) return;   // Testmodi schreiben nie Einstellungen
        try { SaveSettings(); } catch { }
    }

    double PoseStudio.Swarm.ISwarmScene.PreviewAspect => CurrentPreviewAspect();

    /// <summary>Posen-Vorlage für die Tablet-App: Init Image in allen Darstellungen + ControlNet-Bilder in einen eigenen Unterordner.</summary>
    private void ExportTablet_Click(object sender, RoutedEventArgs e)
    {
        string? dir = ExportTabletTemplate(FolderBox.Text.Trim());
        if (dir != null) StatusText.Text = "Tablet-Vorlage gespeichert: " + dir;
    }

    internal string? ExportTabletTemplate(string exportFolder, string? stamp = null)
    {
        var scene = (PoseStudio.Swarm.ISwarmScene)this;
        var probe = new PoseStudio.Swarm.SwarmSettings { SideLength = 1280 };
        var (w, h) = probe.SizeFor(CurrentPreviewAspect());
        stamp ??= DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string dir = Path.Combine(exportFolder, PoseStudio.Swarm.TabletTemplate.FolderName, $"{SafeFileName(PrefixBox.Text)}_{stamp}");
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            Directory.CreateDirectory(dir);
            for (int look = 0; look < PoseStudio.Swarm.TabletTemplate.InitFiles.Length; look++)
                SceneFactory.SaveJpeg(scene.RenderInit(w, h, look), Path.Combine(dir, PoseStudio.Swarm.TabletTemplate.InitFiles[look]), 92);
            // Rauschen macht das Tablet selbst (einstellbar) – dafür Figurmaske und Horizont mitgeben
            {
                var mask = FigureMask(w, h, _light);
                var gray = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, mask, w);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(gray));
                using (var fs = File.Create(Path.Combine(dir, PoseStudio.Swarm.TabletTemplate.MaskFile))) enc.Save(fs);
                RebuildFloor();
                File.WriteAllText(Path.Combine(dir, PoseStudio.Swarm.TabletTemplate.InfoFile),
                    System.Text.Json.JsonSerializer.Serialize(new PoseStudio.Swarm.TabletTemplate.Info { Horizon = SceneFactory.HorizonRow(_cam, h) / h }));
            }
            SceneFactory.SavePng(scene.RenderControl(w, h, false), Path.Combine(dir, PoseStudio.Swarm.TabletTemplate.PoseFile));
            SceneFactory.SavePng(scene.RenderControl(w, h, true), Path.Combine(dir, PoseStudio.Swarm.TabletTemplate.RegionsFile));
            return dir;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Tablet-Vorlage fehlgeschlagen:\n" + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    /// <summary>Szene wie beim Export rendern (ohne Transparenz), wahlweise in einer anderen Darstellung.</summary>
    BitmapSource PoseStudio.Swarm.ISwarmScene.RenderInit(int w, int h, int look, PoseStudio.Swarm.NoiseSettings? noise)
    {
        var light = _light.Clone();
        if (look >= 0) { light.Toon = look > 0; light.ToonStyle = Math.Max(0, look - 1); }
        var opts = new SceneOptions
        {
            Background = _background, FloorBase = _background,
            Floor = FloorExportCheck.IsChecked == true, Shadow = ShadowExportCheck.IsChecked == true,
            BackgroundImage = _bgImage, BackgroundStretch = CurrentStretch,
            Light = light, Set = _set, ShadowResolution = 2048
        };
        try
        {
            var img = SceneFactory.Render(_figures, _cam, opts, w, h);
            if (noise is not { Active: true }) return img;
            return SceneFactory.ApplyNoise(img, FigureMask(w, h, light), SceneFactory.HorizonRow(_cam, h), noise);
        }
        finally { RebuildFloor(); } // Vorschau wieder in der eigenen Darstellung
    }

    /// <summary>Maske der Figuren (mit Haaren und Objekten) ohne Boden, Schatten und Kulisse – 1 Byte Deckung je Pixel.</summary>
    private byte[] FigureMask(int w, int h, LightSettings light)
    {
        var maskOpts = new SceneOptions { Background = Colors.Transparent, Transparent = true, Floor = false, Shadow = false, Light = light, ShadowResolution = 512 };
        return SceneFactory.AlphaMask(SceneFactory.Render(_figures, _cam, maskOpts, w, h));
    }

    BitmapSource PoseStudio.Swarm.ISwarmScene.RenderControl(int w, int h, bool regions) =>
        SceneFactory.RenderControl(_figures, _cam, regions ? SceneFactory.ControlMode.Regions : SceneFactory.ControlMode.Pose3D, w, h);

    void PoseStudio.Swarm.ISwarmScene.UseAsReference(string path) => LoadReferenceImage(path);
    string? PoseStudio.Swarm.ISwarmScene.ReferenceImagePath => _refPaths.TryGetValue(_figure, out var p) && File.Exists(p) ? p : null;

    private FigureStyle CurrentStyle => FigureStyleCombo.SelectedIndex >= 0 ? Styles[FigureStyleCombo.SelectedIndex].Style : FigureStyle.Mannequin;

    /// <summary>Überträgt Stil, Haare und Proportionen auf die Figur (Pose bleibt erhalten).</summary>
    private void ApplyFigure()
    {
        if (_suppressFigure) return;
        _figure.SetFigure(CurrentStyle, HairCheck.IsChecked == true, _props);
        _figure.SetHair(_hair);
        _figure.SetClothing(_clothes);
        _figure.SetSeatBlock(SeatCheck.IsChecked == true);
        HairCheck.IsEnabled = CurrentStyle != FigureStyle.Mannequin;
        HairExpander.IsEnabled = CurrentStyle != FigureStyle.Mannequin; // Wind gilt auch für Röcke und Kleider
        ClothesExpander.IsEnabled = CurrentStyle != FigureStyle.Mannequin;
        PoseChanged();
        if (_ready) RefreshFigureList();
    }

    private void ShowFigure(FigureData fd)
    {
        _suppressFigure = true;
        _props = fd.Proportions.Clone();
        _props.Clamp();
        FigureStyleCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == fd.Style));
        HairCheck.IsChecked = fd.Hair;
        ShowHair(fd.HairStyle);
        ShowClothing(fd.Clothing);
        SeatCheck.IsChecked = fd.SeatBlock;
        _suppressFigure = false;
        foreach (var a in _proportionAxes.Concat(_shapeAxes)) a.Refresh();
        ApplyFigure();
    }

    private void FigureStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFigure) return;
        var style = CurrentStyle;
        // Sinnvolle Standards beim Wechsel: Körpergröße und Farbe (Holz ↔ Haut), sofern nicht selbst geändert.
        if (style.IsFemale() && Math.Abs(_props.HeightCm - 180) < 0.5) _props.HeightCm = 168;
        if (!style.IsFemale() && Math.Abs(_props.HeightCm - 168) < 0.5) _props.HeightCm = 180;
        // Standardfarben je Stil (Holz, Haut, helle Anime-Haut), nur wenn die Farbe nicht selbst gewählt wurde
        var std = style == FigureStyle.Mannequin ? MannequinColor : style.IsAnime() ? AnimeSkinColor : SkinColor;
        if (_figure.BodyColor == MannequinColor || _figure.BodyColor == SkinColor || _figure.BodyColor == AnimeSkinColor) SetBodyColor(std);
        foreach (var a in _proportionAxes.Concat(_shapeAxes)) a.Refresh();
        ApplyFigure();
    }

    // Während InitializeComponent können Haken schon Ereignisse auslösen, bevor alle Bedienelemente existieren
    private void Figure_Changed(object sender, RoutedEventArgs e) { if (ClothesExpander != null) ApplyFigure(); }

    private void ResetShape_Click(object sender, RoutedEventArgs e)
    {
        _props.Bust = _props.Waist = _props.Hips = _props.Glutes = _props.Belly = _props.Thighs = _props.Calves = _props.UpperArms = _props.Neck = _props.Hands = 100;
        foreach (var a in _shapeAxes) a.Refresh();
        ApplyFigure();
    }

    private void ResetProportions_Click(object sender, RoutedEventArgs e)
    {
        _props = new Proportions { HeightCm = CurrentStyle.IsFemale() ? 168 : 180 };
        foreach (var a in _proportionAxes.Concat(_shapeAxes)) a.Refresh();
        ApplyFigure();
    }

    private static double NormalizeAngle(double a)
    {
        a %= 360;
        if (a > 180) a -= 360;
        if (a < -180) a += 360;
        return a;
    }

    private void BuildColorPresets()
    {
        foreach (var hex in BgPresets)
            BgPresetPanel.Children.Add(MakeSwatchButton(hex, c => SetBackground(c)));
        foreach (var hex in BodyPresets)
            BodyPresetPanel.Children.Add(MakeSwatchButton(hex, c => SetBodyColor(c)));
        foreach (var hex in HairPresets)
            HairPresetPanel.Children.Add(MakeSwatchButton(hex, c => SetHairColor(c)));
    }

    private static Button MakeSwatchButton(string hex, Action<Color> onClick)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new Button
        {
            Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(1),
            Background = new SolidColorBrush(c), ToolTip = hex, BorderBrush = Brushes.Gray
        };
        b.Click += (_, _) => onClick(c);
        return b;
    }

    // =================================================================== Einstellungen

    /// <summary>Lädt die Einstellungen; true, wenn dabei Figuren samt Posen wiederhergestellt wurden.</summary>
    private bool LoadSettings()
    {
        var s = _settings;
        SetBackground(ParseColor(s.Background, Color.FromRgb(0xE6, 0xE6, 0xE6)), refresh: false);
        SetBodyColor(ParseColor(s.BodyColor, Color.FromRgb(0xC9, 0xB2, 0x92)));
        FloorPreviewCheck.IsChecked = s.FloorPreview;
        ShadowPreviewCheck.IsChecked = s.ShadowPreview;
        FloorExportCheck.IsChecked = s.FloorExport;
        ShadowExportCheck.IsChecked = s.ShadowExport;

        PreviewFormatCombo.ItemsSource = AspectFormat.All;
        PreviewFormatCombo.SelectedItem = AspectFormat.All.FirstOrDefault(f => f.Key == s.PreviewFormat) ?? AspectFormat.All[0];
        foreach (var f in AspectFormat.All)
            _exportFormats.Add(new FormatOption { Format = f, IsChecked = s.ExportFormats.Contains(f.Key) });

        LongSideCombo.ItemsSource = new[] { 1024, 1280, 1536, 1920, 2048, 2560, 3072, 3840 };
        LongSideCombo.Text = s.LongSide.ToString(CultureInfo.InvariantCulture);
        CustomWBox.Text = s.CustomWidth.ToString(CultureInfo.InvariantCulture);
        CustomHBox.Text = s.CustomHeight.ToString(CultureInfo.InvariantCulture);
        QualitySlider.Value = Math.Clamp(s.JpegQuality, 50, 100);
        FolderBox.Text = s.OutputFolderHere();
        PrefixBox.Text = s.FilePrefix;
        SetHairColor(ParseColor(s.HairColor, Color.FromRgb(0x3A, 0x2A, 0x20)));
        DynamicsCombo.SelectedIndex = Math.Clamp(s.DynamicsMode, 0, 2);
        _swarmSettings = s.Swarm ?? new PoseStudio.Swarm.SwarmSettings();
        GuidesCheck.IsChecked = s.Guides;
        ExportFormatList.ItemsSource = _exportFormats.Where(o => !o.IsFixed).ToList();
        FixedFormatList.ItemsSource = _exportFormats.Where(o => o.IsFixed).ToList();
        _suppressFileType = true;
        PngRadio.IsChecked = s.OutputPng;
        JpgRadio.IsChecked = !s.OutputPng;
        TransparentCheck.IsChecked = s.Transparent;
        _suppressFileType = false;
        UpdateFileTypeUi();
        BgStretchCombo.SelectedIndex = Math.Max(0, Array.FindIndex(StretchModes, m => m.Mode.ToString() == s.BackgroundStretch));
        if (!string.IsNullOrEmpty(s.BackgroundImage) && File.Exists(s.BackgroundImage)) LoadBackgroundImage(s.BackgroundImage, quiet: true);
        ShowFigure(s.Figure ?? new FigureData());
        ShowLight(s.Light ?? new LightSettings());
        ShowSet(s.Set ?? new SetSettings());
        _figure.SetProp(s.Prop ?? new PropSettings());
        ShowProp();
        Cn3dCheck.IsChecked = s.ControlNet3D;
        CnRegionCheck.IsChecked = s.ControlNetRegions;
        CnOnlyCheck.IsChecked = s.ControlNetOnly;
        ClickSelectsFigureCheck.IsChecked = s.ClickSelectsFigure;
        RefOpacitySlider.Value = Math.Clamp(s.ReferenceOpacity, 10, 90);
        PhotoBodyCombo.SelectedIndex = s.PhotoBodyRtmw3d ? 1 : 0;
        PhotoFingersCheck.IsChecked = s.PhotoFingers;
        if (s.Figures is { Count: > 0 })
        {
            try { RestoreFigures(s.Figures, s.ActiveFigure); return true; }
            catch { /* defekte Szene ignorieren */ }
        }
        return false;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Nach dem Schließen alles freigeben und die Anwendung sicher beenden.
    /// Falls doch etwas hängen bleibt, beendet eine Notbremse den Prozess nach 3 Sekunden.</summary>
    private void ShutdownCleanly()
    {
        var guard = new System.Threading.Thread(() => { System.Threading.Thread.Sleep(3000); Environment.Exit(0); }) { IsBackground = true };
        guard.Start();
        try { _library?.Close(); } catch { }
        try { _detector?.Dispose(); _detector = null; } catch { }
        Application.Current.Shutdown();
    }

    private void SaveSettings()
    {
        var s = _settings;
        s.Background = ToHex(_background);
        s.BodyColor = ToHex(_figure.BodyColor);
        s.FloorPreview = FloorPreviewCheck.IsChecked == true;
        s.ShadowPreview = ShadowPreviewCheck.IsChecked == true;
        s.FloorExport = FloorExportCheck.IsChecked == true;
        s.ShadowExport = ShadowExportCheck.IsChecked == true;
        s.PreviewFormat = (PreviewFormatCombo.SelectedItem as AspectFormat)?.Key ?? "16x9";
        s.ExportFormats = _exportFormats.Where(f => f.IsChecked).Select(f => f.Format.Key).ToList();
        s.LongSide = ParseInt(LongSideCombo.Text, 1920);
        s.CustomWidth = ParseInt(CustomWBox.Text, 1200);
        s.CustomHeight = ParseInt(CustomHBox.Text, 1600);
        s.JpegQuality = (int)QualitySlider.Value;
        s.SetOutputFolderHere(FolderBox.Text);
        s.FilePrefix = PrefixBox.Text;
        s.HairColor = ToHex(_figure.HairColor);
        s.DynamicsMode = Math.Max(0, DynamicsCombo.SelectedIndex);
        s.UiTheme = Theme.Mode;
        s.Swarm = _swarmSettings;
        s.Guides = GuidesCheck.IsChecked == true;
        s.OutputPng = PngRadio.IsChecked == true;
        s.Transparent = TransparentCheck.IsChecked == true;
        s.BackgroundImage = _bgImagePath;
        s.BackgroundStretch = StretchModes[Math.Max(0, BgStretchCombo.SelectedIndex)].Mode.ToString();
        s.Figure = _figure.GetFigure();
        s.Light = _light.Clone();
        s.Set = _set.Clone();
        s.Prop = _figure.Prop;
        s.Figures = _figures.Select(FigureToData).ToList();
        s.ActiveFigure = _figures.IndexOf(_figure);
        s.ControlNet3D = Cn3dCheck.IsChecked == true;
        s.ControlNetRegions = CnRegionCheck.IsChecked == true;
        s.ControlNetOnly = CnOnlyCheck.IsChecked == true;
        s.ClickSelectsFigure = ClickSelectsFigureCheck.IsChecked == true;
        s.ReferenceImage = null; // Referenzfotos stehen jetzt je Figur in s.Figures
        s.ReferenceOpacity = RefOpacitySlider.Value;
        s.PhotoBodyRtmw3d = PhotoBodyCombo.SelectedIndex == 1;
        s.PhotoFingers = PhotoFingersCheck.IsChecked == true;
        s.Save();
    }

    private static int ParseInt(string? t, int fallback) =>
        int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static Color ParseColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // =================================================================== Aktualisierung

    private void RefreshAll()
    {
        CameraChanged();
        RebuildFloor();
        foreach (var a in _positionAxes) a.Refresh();
    }

    private void CameraChanged()
    {
        if (!_ready) return;
        _cam.ApplyTo(ViewCamera, CurrentPreviewAspect());
        _lights.Children.Clear();
        _lights.Children.Add(SceneFactory.CreateLights(_cam, _light));
        foreach (var a in _cameraAxes) a.Refresh();
        UpdateLightMarker();
        if (_light.FollowCamera || _light.Toon) RebuildFloor(); // Konturbreite hängt vom Kameraabstand ab
        if (_set.Type != SetType.None) UpdateFrameBackground(); // Himmel folgt dem Horizont
        if (_set.HorizonLine) DrawGuides();
    }

    /// <summary>Boden/Schatten neu aufbauen – mehrere Aufrufe kurz hintereinander werden zusammengefasst,
    /// damit das Ziehen von Gelenken flüssig bleibt (Schattenwurf kostet etwas Rechenzeit).</summary>
    private void RebuildFloor()
    {
        if (!_ready || _floorPending) return;
        _floorPending = true;
        Dispatcher.BeginInvoke(() => { _floorPending = false; RebuildFloorNow(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RebuildFloorNow()
    {
        // Während gedrückter Maustaste (Ziehen) kein Eigenschatten – er ist der teuerste Schritt und kommt beim Loslassen.
        // Im Cel-Shading bleiben die Licht-/Schattenstufen live (ohne Schattenwurf, kostet kaum etwas).
        if (!_interacting) SelfShadow.Apply(_figures, _cam, _light);
        else if (_light.Toon) { var quick = _light.Clone(); quick.CastShadow = false; SelfShadow.Apply(_figures, _cam, quick); }
        _floor.Children.Clear();
        var o = new SceneOptions
        {
            Background = _background,
            Floor = FloorPreviewCheck.IsChecked == true,
            Shadow = ShadowPreviewCheck.IsChecked == true,
            Light = _light,
            Set = _set,
            ShadowResolution = _interacting ? 320 : 640
        };
        if (o.Floor || o.Shadow) _floor.Children.Add(SceneFactory.CreateFloor(o, _figures, _cam));
        if (Outline.Create(_figures, _cam, _light) is { } outline) _floor.Children.Add(outline);
    }

    /// <summary>Nach jeder Änderung an der Pose: ggf. auf den Boden stellen, Schatten nachführen, Regler aktualisieren.</summary>
    private void PoseChanged(bool refreshJointAxes = true)
    {
        if (!_ready) return;
        _figure.Update();
        if (AutoGroundCheck.IsChecked == true) _figure.GroundToFloor();
        RebuildFloor();
        foreach (var a in _positionAxes) a.Refresh();
        if (refreshJointAxes) foreach (var a in _axes) a.Refresh();
        foreach (var a in _fingerAxes) a.Refresh();
    }

    private double CurrentPreviewAspect()
    {
        var f = PreviewFormatCombo.SelectedItem as AspectFormat ?? AspectFormat.All[0];
        return f.Aspect(ParseInt(CustomWBox.Text, 1200), ParseInt(CustomHBox.Text, 1600));
    }

    private void UpdateFrameSize()
    {
        double aw = PreviewHost.ActualWidth - 32, ah = PreviewHost.ActualHeight - 40;
        if (aw <= 10 || ah <= 10) return;
        double aspect = CurrentPreviewAspect();
        double w = aw, h = aw / aspect;
        if (h > ah) { h = ah; w = ah * aspect; }
        Frame.Width = w;
        Frame.Height = h;
        var f = PreviewFormatCombo.SelectedItem as AspectFormat;
        FrameInfo.Text = $"Vorschau: {f?.Name}  –  Rahmen entspricht dem Bildausschnitt";
        CameraChanged();
        Dispatcher.BeginInvoke(DrawGuides, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFrameSize();
    private void PreviewFormat_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateFrameSize(); }
    private void CustomSize_LostFocus(object sender, RoutedEventArgs e) { if (_ready) UpdateFrameSize(); }
    private void Display_Changed(object sender, RoutedEventArgs e) => RebuildFloor();

    // =================================================================== Hände und Finger

    private readonly ObservableCollection<AxisViewModel> _fingerAxes = new();
    private int _fingerSide = 1;
    private bool _gestureReset;
    private const string GesturePlaceholder = "– Handhaltung wählen –";

    private void InitHands()
    {
        foreach (var combo in new[] { GestureLeftCombo, GestureRightCombo })
        {
            var items = new List<object> { GesturePlaceholder };
            items.AddRange(HandGesture.All);
            combo.ItemsSource = items;
            combo.SelectedIndex = 0;
        }
        FingerItems.ItemsSource = _fingerAxes;
        BuildFingerAxes();
    }

    private void BuildFingerAxes()
    {
        _fingerAxes.Clear();
        int s = _fingerSide;
        for (int f = 0; f < 5; f++)
        {
            int fi = f;
            _fingerAxes.Add(new AxisViewModel(HandGeometry.Names[f] + " beugen", -20, 100,
                () => _figure.GetFingerCurl(s, fi), v => { _figure.SetFingerCurl(s, fi, v); FingerChanged(s); }));
            if (f == 0)
                _fingerAxes.Add(new AxisViewModel("Daumen abspreizen", -40, 100,
                    () => _figure.GetThumbSpread(s), v => { _figure.SetThumbSpread(s, v); FingerChanged(s); }));
        }
        _fingerAxes.Add(new AxisViewModel("Finger spreizen", -60, 100,
            () => _figure.GetFingerSpread(s), v => { _figure.SetFingerSpread(s, v); FingerChanged(s); }));
    }

    private void FingerChanged(int s)
    {
        PoseChanged();
        HandStatus.Text = _figure.HandGrips(s)
            ? "Diese Hand hält ein Objekt – der Griff bestimmt die Finger."
            : "Einzelne Fingerglieder: Finger in der Ansicht anklicken. Hält eine Hand ein Objekt, formt der Griff die Finger.";
    }

    private void FingerHand_Changed(object sender, RoutedEventArgs e)
    {
        if (FingerItems == null) return;
        _fingerSide = FingerLeftRadio.IsChecked == true ? 1 : -1;
        BuildFingerAxes();
    }

    private void Gesture_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_gestureReset || sender is not ComboBox combo || combo.SelectedItem is not HandGesture g) return;
        int s = ReferenceEquals(combo, GestureLeftCombo) ? 1 : -1;
        PushUndo();
        _figure.ApplyGesture(s, g);
        FingerChanged(s);
        StatusText.Text = $"Handhaltung „{g.Name}“ für die {(s > 0 ? "linke" : "rechte")} Hand.";
        // Auswahl zurücksetzen, damit dieselbe Haltung erneut gewählt werden kann
        Dispatcher.BeginInvoke(() => { _gestureReset = true; combo.SelectedIndex = 0; _gestureReset = false; });
    }

    // =================================================================== Gelenkauswahl

    private void SelectJoint(Joint? j)
    {
        _figure.Select(j);
        if (j != null) _figure.SelectProp(false);
        if (!ReferenceEquals(JointList.SelectedItem, j)) JointList.SelectedItem = j;
        _axes.Clear();
        if (j == null)
        {
            SelectedJointText.Text = "– Gelenk anklicken –";
            return;
        }
        SelectedJointText.Text = j.Name;
        if (j.Bend != null)
            _axes.Add(new AxisViewModel(j.Bend.Label, j.Bend.Min, j.Bend.Max, () => j.BendValue, v => { j.BendValue = v; PoseChanged(false); }));
        if (j.Side != null)
            _axes.Add(new AxisViewModel(j.Side.Label, j.Side.Min, j.Side.Max, () => j.SideValue, v => { j.SideValue = v; PoseChanged(false); }));
        if (j.Twist != null)
            _axes.Add(new AxisViewModel(j.Twist.Label, j.Twist.Min, j.Twist.Max, () => j.TwistValue, v => { j.TwistValue = v; PoseChanged(false); }));
    }

    private void JointList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var j = JointList.SelectedItem as Joint;
        if (!ReferenceEquals(j, _figure.Selected)) SelectJoint(j);
    }

    // =================================================================== Undo

    private void PushUndo()
    {
        _undo.Push(_figure.GetPose());
        if (_undo.Count > 100)
        {
            var keep = _undo.Take(100).Reverse().ToList();
            _undo.Clear();
            foreach (var p in keep) _undo.Push(p);
        }
    }

    private void UndoPoint_PreviewMouseDown(object sender, MouseButtonEventArgs e) => PushUndo();

    private void DoUndo()
    {
        if (_undo.Count == 0) { StatusText.Text = "Nichts rückgängig zu machen."; return; }
        _figure.SetPose(_undo.Pop());
        ShowProp();
        PoseChanged();
        StatusText.Text = "Rückgängig gemacht.";
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => DoUndo();

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return; // Textfelder behalten ihr eigenes Strg+Z
        DoUndo();
    }

    // =================================================================== Pose-Buttons

    // =================================================================== Vorlagen-Bibliothek

    /// <summary>Auswahlliste: eigene Vorlagen (oben, „Eigene“) und eingebaute, nach Kategorie gruppiert.</summary>
    private void RefreshPresetCombo()
    {
        var user = PoseLibrary.LoadUser().Select(e => new PosePreset(PoseLibrary.UserCategory, e.Name,
            new Dictionary<string, (double, double, double)>()) { Data = e.Pose });
        var all = user.Concat(Presets.All).ToList();
        var cats = all.Select(p => p.Category).Distinct().ToList();
        var view = new System.Windows.Data.ListCollectionView(all.OrderBy(p => cats.IndexOf(p.Category)).ToList());
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PosePreset.Category)));
        bool ready = _ready;
        _ready = false; // Auswahl beim Neuaufbau nicht anwenden
        PresetCombo.ItemsSource = view;
        PresetCombo.SelectedItem = all.FirstOrDefault(p => p.Category != PoseLibrary.UserCategory);
        _ready = ready;
    }

    private PoseLibraryWindow? _library;

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_library is { IsVisible: true }) { _library.Activate(); return; }
        _library = new PoseLibraryWindow(() => _figure.GetFigure(), () => _figure.BodyColor, ApplyLibraryEntry, SaveTemplate, GetVersion()) { Owner = this };
        // links am Hauptfenster über dem Bedienbereich platzieren, damit die 3D-Ansicht sichtbar bleibt
        var tl = WindowState == WindowState.Maximized ? new Point(0, 0) : new Point(Left, Top);
        var area = SystemParameters.WorkArea;
        _library.Left = Math.Max(area.Left, tl.X + 8);
        _library.Top = Math.Max(area.Top, tl.Y + 60);
        _library.Height = Math.Min(Math.Max(400, ActualHeight - 100), area.Height - 20);
        _library.Show();
    }

    /// <summary>Pose aus der Bibliothek auf die aktive Figur anwenden (Position der Figur bleibt).</summary>
    private void ApplyLibraryEntry(LibraryEntry entry)
    {
        PushUndo();
        var p = PoseLibrary.Clone(entry.Pose);
        p.RootPosition = new[] { _figure.RootPosition.X, _figure.RootPosition.Y, _figure.RootPosition.Z };
        _figure.SetPose(p);
        _figure.GroundToFloor();
        ShowProp();
        PoseChanged();
        RefreshFigureList();
        StatusText.Text = $"Vorlage „{entry.Name}“ angewendet.";
    }

    /// <summary>Selbsttest (ohne sichtbares Fenster): Vorlage anwenden und prüfen, ob sich die Pose ändert.</summary>
    /// <summary>Testmodus --uitest-hair: Frisur-Bedienelemente durchspielen (Fenster wird nicht angezeigt, nichts gespeichert).</summary>
    internal string SelfTestHair()
    {
        var sb = new System.Text.StringBuilder();
        FigureStyleCombo.SelectedIndex = Array.FindIndex(Styles, s => s.Style == FigureStyle.AnimeFemale);
        sb.AppendLine($"Stil: {_figure.Style}, Haare an: {_figure.Hair}, Bedienfeld aktiv: {HairExpander.IsEnabled}, Frisur: {_figure.HairStyle.Style}");
        double TopY() => _figure.WorldMeshes().Max(m => m.Mesh.Positions.Count == 0 ? double.MinValue : m.Mesh.Positions.Max(p => m.Transform.Transform(p).Y));
        foreach (var i in new[] { 1, 6, 7 })
        {
            HairStyleCombo.SelectedIndex = i;
            sb.AppendLine($"Auswahl {HairStyleCombo.SelectedItem}: Figur-Frisur {_figure.HairStyle.Style}, Teile {_figure.WorldMeshes().Count()}");
        }
        BangsCombo.SelectedIndex = 2;
        sb.AppendLine($"Pony: {_figure.HairStyle.Bangs}");
        HairStyleCombo.SelectedIndex = 4; // lang
        double calm = TopY();
        HairPreset_Click(new Button { Tag = "0,0,0" }, new RoutedEventArgs());
        sb.AppendLine($"Still: Wind {_figure.HairStyle.Wind}, Schwung {_figure.HairStyle.Lift}, höchster Punkt {TopY():F3}");
        HairPreset_Click(new Button { Tag = "15,0,75" }, new RoutedEventArgs());
        sb.AppendLine($"Sprung: Wind {_figure.HairStyle.Wind}, Schwung {_figure.HairStyle.Lift}, höchster Punkt {TopY():F3} (vorher {calm:F3})");
        _hairAxes[0].Value = 150;
        sb.AppendLine($"Länge-Regler 150: Figur {_figure.HairStyle.Length}");
        int seed = _figure.HairStyle.Seed;
        HairReroll_Click(this, new RoutedEventArgs());
        sb.AppendLine($"Neu würfeln: Seed {seed} → {_figure.HairStyle.Seed}");
        var fd = _figure.GetFigure();
        sb.AppendLine($"GetFigure: {fd.HairStyle?.Style} / {fd.HairStyle?.Bangs} / Länge {fd.HairStyle?.Length}");
        BottomCombo.SelectedIndex = BottomDef.All.ToList().FindIndex(b => b.Id == "kleid");
        TopCombo.SelectedIndex = TopDef.All.ToList().FindIndex(t => t.Id == "tshirt");
        Outfit_Click(new Button { Tag = "tshirt,minirock,socken" }, new RoutedEventArgs());
        sb.AppendLine($"Kleidung: {_figure.Clothing.Top} / {_figure.Clothing.Bottom} / {_figure.Clothing.Shoes}, Teile {_figure.WorldMeshes().Count()}, GetFigure {_figure.GetFigure().Clothing?.Bottom}");
        _clothAxes[0].Value = 180;
        sb.AppendLine($"Weite 180: {_figure.Clothing.Fullness}");
        // Einfrieren: Kopf drehen, Haare müssen starr mitgehen (Abstand Haarpunkt–Nase bleibt gleich)
        DynamicsCombo.SelectedIndex = 2;
        var hairMesh = _figure.WorldMeshes().Select(x => x.Mesh).OrderByDescending(x => x.Positions.Count).First();
        var nose0 = _figure.NoseWorld; var p0 = hairMesh.Positions[hairMesh.Positions.Count / 2];
        _figure["neck"].TwistValue = 40; _figure.Update();
        var nose1 = _figure.NoseWorld; var p1 = hairMesh.Positions[hairMesh.Positions.Count / 2];
        sb.AppendLine($"Eingefroren: Freeze {_figure.FreezeDynamics}, Abstand Haar–Nase vorher {(p0 - nose0).Length:F4} nachher {(p1 - nose1).Length:F4}, Knopf aktiv {RecomputeButton.IsEnabled}");
        DynamicsCombo.SelectedIndex = 1;
        Window_PreviewMouseLeftButtonDown(this, null!);
        sb.AppendLine($"Beim Ziehen: gedrückt Freeze {_figure.FreezeDynamics}");
        Window_PreviewMouseLeftButtonUp(this, null!);
        sb.AppendLine($"Beim Ziehen: losgelassen Freeze {_figure.FreezeDynamics}");
        // Ziehen simulieren: eine Mausbewegung = Gelenk ändern, PoseChanged, Boden/Schatten neu (gedrückt vs. losgelassen)
        _light.CastShadow = true;
        double Step() { var w = System.Diagnostics.Stopwatch.StartNew(); _figure["elbow_l"].BendValue += 1; PoseChanged(); RebuildFloorNow(); return w.Elapsed.TotalMilliseconds; }
        Step();
        double free = Enumerable.Range(0, 10).Select(_ => Step()).Average();
        Window_PreviewMouseLeftButtonDown(this, null!);
        double pressed = Enumerable.Range(0, 10).Select(_ => Step()).Average();
        Window_PreviewMouseLeftButtonUp(this, null!);
        sb.AppendLine($"Mausbewegung beim Ziehen: ohne Taste {free:F0} ms, mit gedrückter Taste {pressed:F0} ms, danach interagiert {_interacting}");
        // Körperform: Hüfte und Brust ändern die Körperbreite bzw. -tiefe
        double Width() { var b = Rect3D.Empty; foreach (var (mesh, mx) in _figure.WorldMeshes()) { if (mesh.Positions.Count == 0) continue; var r = mesh.Bounds; } return _figure.WorldMeshes().SelectMany(x => x.Mesh.Positions.Select(p => x.Transform.Transform(p))).Where(p => Math.Abs(p.Y - _figure.PelvisWorld.Y) < 0.05).Select(p => p.X).DefaultIfEmpty().Max() - _figure.WorldMeshes().SelectMany(x => x.Mesh.Positions.Select(p => x.Transform.Transform(p))).Where(p => Math.Abs(p.Y - _figure.PelvisWorld.Y) < 0.05).Select(p => p.X).DefaultIfEmpty().Min(); }
        double w0 = Width();
        _shapeAxes.First(a => a.Label.StartsWith("Hüfte")).Value = 140;
        sb.AppendLine($"Körperform Hüfte 140: Breite auf Beckenhöhe {w0:F3} → {Width():F3} m, Proportionen {_figure.Proportions.Hips}");
        ResetShape_Click(this, new RoutedEventArgs());
        sb.AppendLine($"Körperform zurückgesetzt: Hüfte {_figure.Proportions.Hips}, Breite {Width():F3}");
        // Alle Vorlagen anwenden: keine Ausnahme, Handhaltungen kommen an
        int okP = 0, withHands = 0; var bad = new List<string>();
        foreach (var pr in Presets.All)
        {
            try { _figure.SetPose(pr.ToPose()); okP++; if (pr.HandR != null && Math.Abs(_figure["index2_r"].SideValue) + Math.Abs(_figure["thumb2_r"].SideValue) > 1) withHands++; }
            catch (Exception ex) { bad.Add(pr.Name + ": " + ex.Message); }
        }
        sb.AppendLine($"Vorlagen: {okP}/{Presets.All.Count} angewendet, {Presets.All.Count(x => x.HandR != null)} mit Handhaltung rechts, davon Finger gesetzt {withHands}, Kategorien {string.Join(", ", Presets.All.Select(x => x.Category).Distinct())}{(bad.Count > 0 ? " FEHLER: " + string.Join("; ", bad) : "")}");
        var swWin = new PoseStudio.Swarm.SwarmWindow(_swarmSettings, this);
        sb.AppendLine($"SwarmUI-Fenster erzeugt: {swWin.Title}");
        {
            string tdir = Path.Combine(Path.GetTempPath(), "PoseStudioSwarmTest"); if (Directory.Exists(tdir)) Directory.Delete(tdir, true); Directory.CreateDirectory(tdir);
            PoseStudio.Swarm.SwarmWindow.OutputOverride = tdir;
            string img = Path.Combine(tdir, "swarm_test_0.png");
            SceneFactory.SavePng(SceneFactory.Render(_figures, _cam, new SceneOptions(), 64, 64), img);
            var w1 = new PoseStudio.Swarm.SwarmWindow(_swarmSettings, this);
            w1.AddResultForTest(img, "Testbild", 4242, new System.Text.Json.Nodes.JsonObject { ["prompt"] = "x", ["seed"] = 4242 });
            var w2 = new PoseStudio.Swarm.SwarmWindow(_swarmSettings, this);
            sb.AppendLine($"Ergebnisse nach neuem Öffnen: {w2.ResultCount}, erstes: {w2.FirstResult}");
            string cnImg = Path.Combine(tdir, "steuerbild.png");
            SceneFactory.SavePng(SceneFactory.Render(_figures, _cam, new SceneOptions(), 300, 150), cnImg);
            sb.AppendLine("ControlNet aus Bild: " + w2.TestControlImage(cnImg));
            sb.AppendLine("Bildfenster: " + w2.TestViewer(img, cnImg));
            sb.AppendLine("Warteschlange: " + w2.TestQueue());
            sb.AppendLine("Refiner: " + w2.TestSegments());
            {
                var fs = new PoseStudio.Swarm.SwarmSettings { Vae = "qwen_image_vae", ControlModel = "anima_3dpose_6200" };
                fs.SwitchFamily("anima", "stable-diffusion-xl-v1");
                string toXl = $"{fs.Vae}/{(fs.ControlModel == "" ? "-" : fs.ControlModel)}";
                fs.Vae = "Flux/ae"; fs.SwitchFamily("stable-diffusion-xl-v1", "anima");
                string back = $"{fs.Vae}/{fs.ControlModel}";
                bool ok = toXl == "None/-" && back == "qwen_image_vae/anima_3dpose_6200" && fs.CloneProfile().VaeByFamily["stable-diffusion-xl-v1"] == "Flux/ae";
                sb.AppendLine($"Modellfamilie: Anima→SDXL {toXl}, zurück {back}{(ok ? "" : " FEHLER")} | VAE-Liste {string.Join(",", PoseStudio.Swarm.SwarmClient.VaeChoices(null, new[] { new PoseStudio.Swarm.SwarmModel("Flux/ae.safetensors", "", null, null, null) }))}");
            }
            sb.AppendLine($"Ergebnisordner: {PoseStudio.Swarm.SwarmWindow.FolderFor(@"D:\Bilder\Pose")} | leer → {PoseStudio.Swarm.SwarmWindow.FolderFor("")} | aktuell → {PoseStudio.Swarm.SwarmWindow.FolderFor(FolderBox.Text)} | Vorschau-Cache: {PoseStudio.Swarm.ThumbCache.Folder}");
            string? tab = ExportTabletTemplate(tdir, "20260101_120000");
            sb.AppendLine($"Tablet-Vorlage: {(tab == null ? "FEHLER" : Path.GetFileName(tab) + " → " + string.Join(", ", Directory.GetFiles(tab).Select(f => $"{Path.GetFileName(f)} {new FileInfo(f).Length / 1024} KB")))}");
            {
                // Init-Rauschen: Hintergrund oben, Boden unten getrennt; Figur frei; Bilder zum Ansehen in %TEMP%\PoseStudioNoise
                var sc = (PoseStudio.Swarm.ISwarmScene)this;
                double Spread(BitmapSource b, int x0, int y0)
                {
                    var c = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
                    var px = new byte[40 * 40 * 4]; c.CopyPixels(new Int32Rect(x0, y0, 40, 40), px, 160, 0);
                    double m = 0, v = 0; int n = px.Length / 4;
                    for (int i = 0; i < px.Length; i += 4) m += px[i + 1];
                    m /= n;
                    for (int i = 0; i < px.Length; i += 4) v += (px[i + 1] - m) * (px[i + 1] - m);
                    return Math.Sqrt(v / n);
                }
                double Center(BitmapSource a, BitmapSource b)
                {
                    var ca = new FormatConvertedBitmap(a, PixelFormats.Bgra32, null, 0); var cb = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
                    var r = new Int32Rect(310, 150, 20, 20); var pa = new byte[1600]; var pb = new byte[1600];
                    ca.CopyPixels(r, pa, 80, 0); cb.CopyPixels(r, pb, 80, 0);
                    double d = 0; for (int i = 0; i < 1600; i++) d += Math.Abs(pa[i] - pb[i]); return d / 1600;
                }
                int Colorful(BitmapSource b)
                {
                    var c = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0); var p = new byte[1600 * 4];
                    c.CopyPixels(new Int32Rect(0, 0, 40, 40), p, 160, 0);
                    int n = 0; for (int i = 0; i < p.Length; i += 4) if (Math.Abs(p[i] - p[i + 1]) > 3 || Math.Abs(p[i + 1] - p[i + 2]) > 3) n++;
                    return n;
                }
                var oldFloor = FloorExportCheck.IsChecked; FloorExportCheck.IsChecked = true;
                var plain = sc.RenderInit(640, 360, -1);
                var onlyBg = sc.RenderInit(640, 360, -1, new PoseStudio.Swarm.NoiseSettings(0.5, 1, 0, 24, false));
                var onlyFloor = sc.RenderInit(640, 360, -1, new PoseStudio.Swarm.NoiseSettings(0, 16, 0.5, 1, false));
                var both = sc.RenderInit(640, 360, -1, new PoseStudio.Swarm.NoiseSettings(0.4, 16, 0.4, 32, false));
                var mono = sc.RenderInit(640, 360, -1, new PoseStudio.Swarm.NoiseSettings(0.5, 1, 0.5, 1, true));
                FloorExportCheck.IsChecked = oldFloor;
                string nd = Path.Combine(Path.GetTempPath(), "PoseStudioNoise"); Directory.CreateDirectory(nd);
                SceneFactory.SaveJpeg(both, Path.Combine(nd, "init-rauschen-getrennt.jpg"), 92);
                SceneFactory.SaveJpeg(mono, Path.Combine(nd, "init-rauschen-sw.jpg"), 92);
                double hz = SceneFactory.HorizonRow(_cam, 360);
                double top0 = Spread(plain, 0, 0), topBg = Spread(onlyBg, 0, 0), topFl = Spread(onlyFloor, 0, 0);
                double bot0 = Spread(plain, 0, 318), botBg = Spread(onlyBg, 0, 318), botFl = Spread(onlyFloor, 0, 318);
                bool okSplit = topBg > top0 + 20 && topFl < top0 + 3 && botFl > bot0 + 20 && botBg < bot0 + 3;
                double fc = Math.Max(Center(plain, onlyBg), Center(plain, both));
                int cb = Colorful(onlyBg), cm = Colorful(mono);
                sb.AppendLine($"Init-Rauschen: Horizont Zeile {hz:0}, oben ohne/Hgr/Boden {top0:0.0}/{topBg:0.0}/{topFl:0.0}, unten {bot0:0.0}/{botBg:0.0}/{botFl:0.0}, Figur {fc:0.0}, bunt {cb} s/w {cm}"
                    + (okSplit && fc < 3 && hz > 0 && hz < 318 && cb > 500 && cm < 20 ? "" : " FEHLER"));
            }
            PoseStudio.Swarm.SwarmWindow.OutputOverride = null; Directory.Delete(tdir, true);
        }
        foreach (var useInit in new[] { true, false })
        {
            var ts = new PoseStudio.Swarm.SwarmSettings { UseInitImage = useInit, UseControlNet = true, ControlModel = "anima_3dpose_6200" };
            var tp = ts.BuildParams("data:init", 1024, 576, "data:cn");
            sb.AppendLine($"Init Image {(useInit ? "an" : "aus")}: initimage {tp.ContainsKey("initimage")}, creativity {tp.ContainsKey("initimagecreativity")}, ControlNet {tp.ContainsKey("controlnetimageinput")}, Preprocessor {tp["controlnetpreprocessor"]}");
        }
        {
            // Kopf mit der Maus: Blickpunkt folgt der Maus, waagerecht = drehen, senkrecht = nicken, kein Neigen
            var head = _figure["head"];
            double b0 = head.BendValue, s0 = head.SideValue, t0 = head.TwistValue;
            var probe = ProbePoint(head);
            var a = ToScreen(head.World.Transform(probe));
            for (int i = 0; i < 10; i++) { FollowMouse(head, new Vector(4, 0)); _figure.Update(); }
            var b = ToScreen(head.World.Transform(probe));
            double turned = head.TwistValue - t0, nodH = head.BendValue - b0;
            for (int i = 0; i < 10; i++) { FollowMouse(head, new Vector(0, 4)); _figure.Update(); }
            var c = ToScreen(head.World.Transform(probe));
            double nod = head.BendValue - b0 - nodH;
            bool ok = Math.Abs(turned) > 3 && b.X - a.X > 20 && Math.Abs(b.Y - a.Y) < 10 && Math.Abs(nod) > 3 && c.Y - b.Y > 20
                && head.SideValue == s0;
            sb.AppendLine($"Kopf folgt Maus (Ansicht {View.ActualWidth:0}x{View.ActualHeight:0}): rechts {b.X - a.X:0}px/{b.Y - a.Y:0}px (Drehen {turned:0.0}°), runter {c.Y - b.Y:0}px (Nicken {nod:0.0}°){(ok ? "" : " FEHLER")}");
            head.BendValue = b0; head.SideValue = s0; head.TwistValue = t0; _figure.Update();
        }
        HairCheck.IsChecked = false;
        sb.AppendLine($"Haare aus: Bedienfeld aktiv {HairExpander.IsEnabled}, Teile {_figure.WorldMeshes().Count()}");
        return sb.ToString();
    }

    internal string SelfTestApplyLibrary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var name in new[] { "T-Pose", "Winken", "Neutral stehen (A-Pose)" })
        {
            var entry = PoseLibrary.BuiltIn().First(e => e.Name == name);
            double before = _figure["shoulder_l"].SideValue;
            var rootBefore = _figure.RootPosition;
            ApplyLibraryEntry(entry);
            double after = _figure["shoulder_l"].SideValue;
            sb.AppendLine($"{name}: Oberarm links seitlich {before:F0}° → {after:F0}°, Position X {rootBefore.X:F2} → {_figure.RootPosition.X:F2}, Status: {StatusText.Text}");
        }
        return sb.ToString();
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SaveTemplate() is { } name) StatusText.Text = $"Vorlage „{name}“ gespeichert.";
    }

    /// <summary>Pose der aktiven Figur als eigene Vorlage speichern (Name abfragen, Vorschaubild rendern).</summary>
    private string? SaveTemplate()
    {
        var owner = _library is { IsVisible: true } ? (Window)_library : this;
        var name = AskText(owner, "Als Vorlage speichern", "Name der Vorlage:", "Meine Pose");
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = PoseLibrary.SafeName(name);
        if (File.Exists(Path.Combine(PoseLibrary.UserFolder, name + ".pose.json")) &&
            MessageBox.Show(owner, $"Es gibt schon eine Vorlage „{name}“. Überschreiben?", "Vorlage", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return null;
        var pose = _figure.GetPose();
        pose.Figure = _figure.GetFigure();
        var thumb = PoseLibrary.RenderThumb(pose, pose.Figure, _figure.BodyColor);
        PoseLibrary.SaveUser(name, pose, thumb);
        RefreshPresetCombo();
        return name;
    }

    /// <summary>Einfacher Eingabedialog.</summary>
    private static string? AskText(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 10), MinWidth = 280 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var dlg = new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false
        };
        ok.Click += (_, _) => dlg.DialogResult = true;
        dlg.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dlg.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auswahl in der Liste wendet die Vorlage direkt an (nicht beim Programmstart)
        if (_ready && PresetCombo.IsDropDownOpen) ApplyPreset_Click(sender, e);
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PosePreset p) return;
        ApplyLibraryEntry(new LibraryEntry { Name = p.Name, Category = p.Category, Pose = p.ToPose() });
    }

    private void ResetJoint_Click(object sender, RoutedEventArgs e)
    {
        if (_figure.Selected == null) return;
        PushUndo();
        _figure.ResetJoint(_figure.Selected);
        PoseChanged();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _figure.ResetAll();
        _figure.GroundToFloor();
        PoseChanged();
    }

    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _figure.Mirror();
        ShowProp();
        PoseChanged();
    }

    private void AutoGround_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (AutoGroundCheck.IsChecked == true) PoseChanged();
    }

    private void SavePose_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Pose (*.pose.json)|*.pose.json|JSON (*.json)|*.json", FileName = "pose.pose.json" };
        if (dlg.ShowDialog(this) != true) return;
        var pose = FigureToData(_figure);
        pose.Camera = _cam.ToData();
        pose.Light = _light.Clone();
        pose.Set = _set.Clone();
        pose.Figures = _figures.Select(FigureToData).ToList();
        pose.ActiveFigure = _figures.IndexOf(_figure);
        pose.Save(dlg.FileName);
        StatusText.Text = $"Pose gespeichert: {dlg.FileName}";
    }

    private void LoadPose_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Pose (*.pose.json;*.json)|*.pose.json;*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var pose = PoseData.Load(dlg.FileName);
            if (pose.Light != null) ShowLight(pose.Light);
            if (pose.Set != null) ShowSet(pose.Set);
            if (pose.Figures is { Count: > 0 })
                RestoreFigures(pose.Figures, pose.ActiveFigure);   // ganze Szene
            else
            {
                PushUndo();
                DataToFigure(_figure, pose);                       // ältere Datei: eine Figur
                ActivateFigure(_figures.IndexOf(_figure));
            }
            if (pose.Camera != null) _cam.FromData(pose.Camera);
            PoseChanged();
            CameraChanged();
            StatusText.Text = $"Pose geladen: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Die Pose konnte nicht geladen werden:\n" + ex.Message, "Pose laden", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // =================================================================== Kamera-Buttons

    private void CamPreset_Click(object sender, RoutedEventArgs e)
    {
        _cam.Yaw = double.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        _cam.Pitch = 5;
        CameraChanged();
    }

    private void CamTop_Click(object sender, RoutedEventArgs e) { _cam.Pitch = 55; CameraChanged(); }
    private void CamLow_Click(object sender, RoutedEventArgs e) { _cam.Pitch = -25; CameraChanged(); }

    private void CamFull_Click(object sender, RoutedEventArgs e)
    {
        // Ganze Figur mit etwas Rand einpassen.
        var pel = _figure.PelvisWorld;
        double min = _figures.Min(fg => fg.ComputeMinY()), max = _figures.Max(fg => fg.ComputeMaxY());
        pel = new Point3D(_figures.Average(fg => fg.PelvisWorld.X), 0, _figures.Average(fg => fg.PelvisWorld.Z));
        double half = (max - min) / 2 * 1.12;
        _cam.Target = new Point3D(pel.X, (min + max) / 2, pel.Z);
        _cam.Distance = Math.Clamp(half / Math.Tan(_cam.VerticalFov * Math.PI / 360), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
        _cam.Pitch = 5;
        CameraChanged();
    }

    private void CamPortrait_Click(object sender, RoutedEventArgs e)
    {
        var head = _figure["head"].World.Transform(new Point3D(0, 0.05, 0));
        _cam.Target = new Point3D(head.X, head.Y - 0.12, head.Z);
        _cam.Distance = Math.Clamp(0.42 / Math.Tan(_cam.VerticalFov * Math.PI / 360), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
        _cam.Pitch = 0;
        CameraChanged();
    }

    // =================================================================== Farben

    private void SetBackground(Color c, bool refresh = true)
    {
        _background = c;
        UpdateFrameBackground();
        BgSwatch.Background = new SolidColorBrush(c);
        BgHexBox.Text = ToHex(c);
        if (refresh) { RebuildFloor(); DrawGuides(); }
    }

    private void SetBodyColor(Color c)
    {
        _figure.BodyColor = c;
        BodySwatch.Background = new SolidColorBrush(c);
        BodyHexBox.Text = ToHex(c);
    }

    private static Color? PickColor(Color initial)
    {
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B),
            FullOpen = true
        };
        return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)
            : null;
    }

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(_background) is { } c) SetBackground(c);
    }

    private void PickBody_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(_figure.BodyColor) is { } c) SetBodyColor(c);
    }

    private void SetHairColor(Color c)
    {
        _figure.HairColor = c;
        HairSwatch.Background = new SolidColorBrush(c);
    }

    private void PickHair_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(_figure.HairColor) is { } c) SetHairColor(c);
    }

    // =================================================================== Dateiformat / Transparenz

    private bool _suppressFileType;

    /// <summary>Transparent nur bei PNG, angehakter Option und ohne Hintergrundbild.</summary>
    private bool TransparentActive => PngRadio.IsChecked == true && TransparentCheck.IsChecked == true && _bgImage == null && _set.Type == SetType.None;

    private void FileType_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFileType || !IsInitialized) return; // feuert schon während InitializeComponent
        UpdateFileTypeUi();
    }

    private void UpdateFileTypeUi()
    {
        bool png = PngRadio.IsChecked == true;
        TransparentCheck.IsEnabled = png;
        QualityRow.Visibility = png ? Visibility.Collapsed : Visibility.Visible;
        ExportButton.Content = png ? "PNG exportieren" : "JPG exportieren";
        UpdateFrameBackground();
    }

    /// <summary>Vorschau-Hintergrund: Farbe, oder Schachbrett wenn transparent exportiert wird.</summary>
    private void UpdateFrameBackground()
    {
        if (TransparentActive)
        {
            var checker = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute
            };
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            var dark = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            checker.Drawing = g;
            Frame.Background = checker;
        }
        else
        {
            Frame.Background = (_set.Type != SetType.None ? SetBuilder.BackgroundBrush(SetBuilder.Get(_set), _cam) : null)
                               ?? new SolidColorBrush(_background);
        }
    }
    // =================================================================== Hintergrundbild

    private Stretch CurrentStretch => StretchModes[Math.Max(0, BgStretchCombo.SelectedIndex)].Mode;

    private void LoadBackgroundImage(string path, bool quiet = false)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            _bgImage = bi;
            _bgImagePath = path;
            BgImage.Source = bi;
            UpdateFrameBackground();
            BgImage.Stretch = CurrentStretch;
            BgImageText.Text = $"{Path.GetFileName(path)}  ({bi.PixelWidth}×{bi.PixelHeight})";
            BgImageText.ToolTip = path;
        }
        catch (Exception ex)
        {
            if (!quiet)
                MessageBox.Show(this, "Das Bild konnte nicht geladen werden:\n" + ex.Message, "Hintergrundbild", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PickBgImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff|Alle Dateien|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        LoadBackgroundImage(dlg.FileName);
        if (_bgImage != null && FloorPreviewCheck.IsChecked == true)
            StatusText.Text = "Tipp: Für Hintergrundbilder „Boden + Raster“ ausschalten, der Schatten kann bleiben.";
    }

    private void RemoveBgImage_Click(object sender, RoutedEventArgs e)
    {
        _bgImage = null;
        _bgImagePath = null;
        BgImage.Source = null;
        UpdateFrameBackground();
        BgImageText.Text = "(kein Bild)";
        BgImageText.ToolTip = null;
    }

    private void BgStretch_Changed(object sender, SelectionChangedEventArgs e) => BgImage.Stretch = CurrentStretch;

    // =================================================================== Pose aus Foto

    private PoseStudio.Detection.PoseDetector? _detector;
    private PoseStudio.Detection.DetectedPose? _lastDetection;
    private BitmapSource? _refBitmap;

    private void PoseFromPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_refBitmap == null)
        {
            var dlg = new OpenFileDialog { Title = "Foto mit der gewünschten Pose", Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Alle Dateien|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            LoadReferenceImage(dlg.FileName);
            if (_refBitmap == null) return;
        }
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            _detector ??= new PoseStudio.Detection.PoseDetector(Path.Combine(AppContext.BaseDirectory, "Models"));
            var pose = _detector.Detect(_refBitmap);
            _lastDetection = pose;
            if (pose != null) _refDetections[_figure] = pose;
            if (pose == null)
            {
                DrawGuides();
                MessageBox.Show(this, "Im Bild wurde keine Person erkannt.\nAm besten klappt es mit einer gut sichtbaren, ganzen Person.", "Pose aus Foto", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            PushUndo();
            var root = _figure.RootPosition;
            var wb = pose.Whole;
            bool rtmBody = PhotoBodyCombo.SelectedIndex == 1 && wb != null;
            var vis = PoseStudio.Detection.PoseDetector.Visible33(pose, _refBitmap.PixelWidth, _refBitmap.PixelHeight, rtmBody);
            var kept = _figure.FitLandmarks(rtmBody
                ? PoseStudio.Detection.PoseDetector.ToScene(wb!.ToMediaPipe33(), _cam)
                : PoseStudio.Detection.PoseDetector.ToScene(pose, _cam), vis);
            string fingers = kept.Count > 0 ? $" Nicht im Bild, Haltung beibehalten: {string.Join(", ", kept)}." : "";
            if (PhotoFingersCheck.IsChecked == true)
            {
                if (wb == null)
                    fingers += _detector.HasWholeBody ? " Hände nicht erkannt." : " Für Finger fehlt das RTMW3D-Modell im Ordner Models.";
                else
                {
                    var all = PoseStudio.Detection.PoseDetector.ToScene(wb.World, _cam);
                    int iw = _refBitmap.PixelWidth, ih = _refBitmap.PixelHeight;
                    double Sc(int i) => wb.Image[i].X < 0 || wb.Image[i].Y < 0 || wb.Image[i].X > iw || wb.Image[i].Y > ih ? 0 : wb.Score[i]; // Hand außerhalb des Fotos: nicht übernehmen
                    int Fit(int s, int off) => _figure.FitHand(s, all.Skip(off).Take(21).ToArray(), Enumerable.Range(off, 21).Select(Sc).ToArray());
                    int nl = Fit(1, PoseStudio.Detection.WholeBodyPose.LeftHand), nr = Fit(-1, PoseStudio.Detection.WholeBodyPose.RightHand);
                    fingers += $" Finger: links {nl}/5, rechts {nr}/5 übernommen.";
                }
            }
            _figure.RootPosition = root;
            _figure.GroundToFloor();
            PoseChanged();
            ShowProp();
            DrawGuides();
            StatusText.Text = $"Pose erkannt ({(rtmBody ? "RTMW3D" : "MediaPipe")}, Sicherheit {pose.Confidence:P0}) und übernommen.{fingers} Strg+Z macht es rückgängig.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Posenerkennung fehlgeschlagen:\n" + ex.Message, "Pose aus Foto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>Kamera (Zielpunkt quer zur Blickrichtung und Abstand; Blickwinkel bleibt) so einstellen, dass die sichtbaren
    /// Gelenke der aktiven Figur auf den erkannten Punkten im Referenzfoto liegen.</summary>
    private void CameraToPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_refBitmap == null || !_refDetections.TryGetValue(_figure, out var pose))
        {
            StatusText.Text = "Erst ein Foto laden und „Pose übernehmen“ – dann passt die Kamera die Figur ans Foto an.";
            return;
        }
        var r = FitCameraToPhoto(pose);
        if (r == null) { StatusText.Text = "Zu wenige sichtbare Gelenke im Foto, um die Kamera anzupassen."; return; }
        CameraChanged();
        DrawGuides();
        StatusText.Text = $"Kamera an das Foto angepasst (mittlere Abweichung {r.Value:F0} Pixel).";
    }

    internal double? FitCameraToPhoto(PoseStudio.Detection.DetectedPose pose)
    {
        if (_refBitmap == null) return null;
        double W = View.ActualWidth, H = View.ActualHeight;
        var pairs = PoseStudio.Rendering.CameraFit.Pairs(_figure, pose, _refBitmap.PixelWidth, _refBitmap.PixelHeight, W, H);
        return PoseStudio.Rendering.CameraFit.Fit(_cam, W, H, pairs);
    }

    /// <summary>Erkanntes Skelett über dem Referenzbild einzeichnen (gleiche Einpassung wie das Bild).</summary>
    private void DrawDetection(double W, double H)
    {
        if (_lastDetection == null || _refBitmap == null || RefImage.Source == null) return;
        double iw = _refBitmap.PixelWidth, ih = _refBitmap.PixelHeight;
        double s = Math.Min(W / iw, H / ih), ox = (W - iw * s) / 2, oy = (H - ih * s) / 2;
        Point Map(int i) => new(ox + _lastDetection.Image[i].X * s, oy + _lastDetection.Image[i].Y * s);
        var line = new SolidColorBrush(Color.FromArgb(0xD0, 0x00, 0xC8, 0x60));
        foreach (var (a, b) in PoseStudio.Detection.PoseDetector.Bones)
        {
            var p1 = Map(a); var p2 = Map(b);
            GuideCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = line, StrokeThickness = 2 });
        }
        if (_lastDetection.Whole is { } wb)
        {
            var hand = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0xA0, 0xFF));
            Point MapW(int i) => new(ox + wb.Image[i].X * s, oy + wb.Image[i].Y * s);
            foreach (var off in new[] { PoseStudio.Detection.WholeBodyPose.LeftHand, PoseStudio.Detection.WholeBodyPose.RightHand })
            {
                if (wb.HandScore(off == PoseStudio.Detection.WholeBodyPose.LeftHand) < 0.3) continue;
                for (int f = 0; f < 5; f++)
                    for (int k = 0; k < 4; k++)
                    {
                        var p1 = MapW(k == 0 ? off : off + f * 4 + k); var p2 = MapW(off + 1 + f * 4 + k);
                        GuideCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = hand, StrokeThickness = 1.5 });
                    }
            }
        }
        for (int i = 0; i < 33; i++)
        {
            var p = Map(i);
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brushes.OrangeRed };
            Canvas.SetLeft(dot, p.X - 3); Canvas.SetTop(dot, p.Y - 3);
            GuideCanvas.Children.Add(dot);
        }
    }

    // =================================================================== Referenzbild (nur Vorschau)

    /// <summary>Referenzfoto je Figur (Pfad) und die zuletzt darin erkannte Pose.</summary>
    private readonly Dictionary<Mannequin, string> _refPaths = new();
    private readonly Dictionary<Mannequin, PoseStudio.Detection.DetectedPose> _refDetections = new();

    /// <summary>Foto als Referenz für die aktive Figur laden und anzeigen.</summary>
    private void LoadReferenceImage(string path)
    {
        _refPaths[_figure] = path;
        _refDetections.Remove(_figure);
        ShowReferenceForActive();
    }

    /// <summary>Referenzfoto der aktiven Figur anzeigen (oder ausblenden, wenn sie keins hat).</summary>
    private void ShowReferenceForActive()
    {
        _refBitmap = null;
        _lastDetection = null;
        RefImage.Source = null;
        if (_refPaths.TryGetValue(_figure, out var path))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                RefImage.Source = bi;
                _refBitmap = bi;
                _refDetections.TryGetValue(_figure, out _lastDetection);
            }
            catch (Exception ex)
            {
                _refPaths.Remove(_figure);
                StatusText.Text = "Referenzfoto konnte nicht geladen werden: " + ex.Message;
            }
        }
        int idx = _figures.IndexOf(_figure) + 1;
        RemoveRefOverlayButton.Visibility = _refBitmap != null ? Visibility.Visible : Visibility.Collapsed;
        RefImageText.Text = _refBitmap != null
            ? $"Figur {idx}: {Path.GetFileName(path)} – Kamera grob wie im Foto einstellen, dann „Pose übernehmen“."
            : $"Figur {idx}: kein Foto. Jede Figur kann ihr eigenes Referenzfoto haben (wird nie exportiert).";
        DrawGuides();
    }

    private void PickRefImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = $"Referenzfoto für Figur {_figures.IndexOf(_figure) + 1}", Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Alle Dateien|*.*" };
        if (dlg.ShowDialog(this) == true) LoadReferenceImage(dlg.FileName);
    }

    private void RemoveRefImage_Click(object sender, RoutedEventArgs e)
    {
        _refPaths.Remove(_figure);
        _refDetections.Remove(_figure);
        ShowReferenceForActive();
    }
    private void RefOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RefImage != null) RefImage.Opacity = e.NewValue / 100;
    }

    // =================================================================== Hilfslinien

    private void Guides_Changed(object sender, RoutedEventArgs e) => DrawGuides();

    /// <summary>Zeichnet für jedes angehakte Exportformat, das schmaler als die Vorschau ist, seinen
    /// Bildausschnitt als gestrichelte Linien. (Die Bildhöhe ist bei allen Formaten gleich, nur die Breite ändert sich.)</summary>
    private void DrawGuides()
    {
        if (!_ready) return;
        GuideCanvas.Children.Clear();
        double W = View.ActualWidth, H = View.ActualHeight;
        if (W < 10 || H < 10) return;
        if (_set.HorizonLine) DrawHorizon(W, H);
        DrawDetection(W, H);
        if (GuidesCheck.IsChecked != true) return;
        double lum = (0.299 * _background.R + 0.587 * _background.G + 0.114 * _background.B) / 255;
        var color = lum > 0.5 ? Color.FromRgb(0xD0, 0x30, 0x30) : Color.FromRgb(0xFF, 0xC0, 0x40);
        var brush = new SolidColorBrush(color);
        var shade = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));
        int cw = ParseInt(CustomWBox.Text, 1200), ch = ParseInt(CustomHBox.Text, 1600);
        var preview = PreviewFormatCombo.SelectedItem as AspectFormat;

        var guides = _exportFormats.Where(f => f.IsChecked && !ReferenceEquals(f.Format, preview))
            .Select(f => (f.Format, Width: H * f.Format.Aspect(cw, ch)))
            .Where(g => g.Width < W - 2)
            .OrderBy(g => g.Width).ToList();
        if (guides.Count == 0) return;

        // Bereich außerhalb des schmalsten Formats leicht abdunkeln
        double narrow = guides[0].Width;
        AddRect(0, (W - narrow) / 2, shade);
        AddRect(W - (W - narrow) / 2, (W - narrow) / 2, shade);

        int i = 0;
        foreach (var (format, w) in guides)
        {
            double x0 = (W - w) / 2, x1 = x0 + w;
            foreach (var x in new[] { x0, x1 })
                GuideCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, X2 = x, Y1 = 0, Y2 = H, Stroke = brush, StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 6, 4 }
                });
            var label = new TextBlock
            {
                Text = format.IsCustom ? $"{cw}×{ch}" : format.Name.Split(' ')[0],
                Foreground = brush, FontSize = 11, FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(label, x0 + 4);
            Canvas.SetTop(label, 4 + i * 15);
            GuideCanvas.Children.Add(label);
            i++;
        }

        void AddRect(double x, double w, Brush b)
        {
            var r = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, w), Height = H, Fill = b };
            Canvas.SetLeft(r, x);
            GuideCanvas.Children.Add(r);
        }
    }

    /// <summary>Horizontlinie = Augenhöhe der Kamera. Hilft, 2D-Hintergrundbilder perspektivisch passend auszurichten.</summary>
    private void DrawHorizon(double W, double H)
    {
        double y = SetBuilder.HorizonFraction(_cam) * H;
        if (y < 0 || y > H) return;
        var brush = new SolidColorBrush(Color.FromRgb(0x20, 0x90, 0xE0));
        GuideCanvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 0, X2 = W, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 10, 4, 2, 4 }
        });
        var label = new TextBlock { Text = "Horizont / Augenhöhe", Foreground = brush, FontSize = 11, FontWeight = FontWeights.SemiBold };
        Canvas.SetRight(label, 6);
        Canvas.SetTop(label, y - 16);
        GuideCanvas.Children.Add(label);
    }

    private void BgHexBox_LostFocus(object sender, RoutedEventArgs e) => SetBackground(ParseColor(BgHexBox.Text, _background));
    private void BodyHexBox_LostFocus(object sender, RoutedEventArgs e) => SetBodyColor(ParseColor(BodyHexBox.Text, _figure.BodyColor));

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender == BgHexBox) SetBackground(ParseColor(BgHexBox.Text, _background));
        else SetBodyColor(ParseColor(BodyHexBox.Text, _figure.BodyColor));
    }

    // =================================================================== Maus in der 3D-Ansicht

    private GeometryModel3D? HitPart(Point p)
    {
        GeometryModel3D? found = null;
        VisualTreeHelper.HitTest(View, null, r =>
        {
            // Nur die aktive Figur ist anklickbar – andere Figuren gelten als leere Fläche (außer die Option ist an)
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D g && OwnerOf(g) is { } owner &&
                (ReferenceEquals(owner, _figure) || ClickSelectsFigureCheck.IsChecked == true))
            {
                found = g;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        return found;
    }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseStart = _mouseLast = e.GetPosition(View);
        _leftDown = true;
        _dragging = false;
        _pressedPart = HitPart(_mouseStart);
        // Klick auf eine andere Figur macht diese zur aktiven Figur
        if (_pressedPart != null && OwnerOf(_pressedPart) is { } owner && !ReferenceEquals(owner, _figure))
            ActivateFigure(_figures.IndexOf(owner));
        _lightDrag = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool mod = !_lightDrag && (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _jointDrag = false;
        _propDrag = false;
        bool onProp = _pressedPart != null && _figure.IsPropPart(_pressedPart);
        if (mod && (onProp || (_pressedPart == null && _figure.PropSelected)))
        {
            // Strg/Umschalt am Objekt: Objekt verschieben/drehen, die Arme folgen
            if (onProp) SelectPropInView();
            _propDrag = true;
            PushUndo();
        }
        else if (mod)
        {
            // Strg/Umschalt: angeklicktes (oder bereits gewähltes) Gelenk mit der Maus bewegen.
            if (_pressedPart != null && _figure.TryGetJoint(_pressedPart, out var j) && !ReferenceEquals(j, _figure.Selected))
                SelectJoint(j);
            if (_figure.Selected != null) { _jointDrag = true; _ikTargetRel = null; PushUndo(); }
            // Strg am Becken: ganze Figur über den Boden schieben (statt den Körper zu kippen)
            if (_jointDrag && _figure.Selected?.Id == "pelvis" && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _jointDrag = false;
                _figureDrag = true;
                _lastGroundHit = GroundHit(_mouseStart);
            }
        }
        Frame.CaptureMouse();
        Frame.Focus();
    }

    private void Frame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_leftDown && !_dragging)
        {
            // Einfacher Klick: Gelenk auswählen bzw. Auswahl aufheben.
            if (_pressedPart != null && _figure.IsPropPart(_pressedPart)) SelectPropInView();
            else if (_pressedPart != null && _figure.TryGetJoint(_pressedPart, out var j)) SelectJoint(j);
            else if (!_jointDrag && !_propDrag) { SelectJoint(null); _figure.SelectProp(false); }
        }
        _leftDown = false;
        _jointDrag = false;
        _propDrag = false;
        _figureDrag = false;
        _lightDrag = false;
        if (!_rightDown) Frame.ReleaseMouseCapture();
    }

    private void Frame_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseLast = e.GetPosition(View);
        _rightDown = true;
        Frame.CaptureMouse();
    }

    private void Frame_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _rightDown = false;
        if (!_leftDown) Frame.ReleaseMouseCapture();
    }

    private void Frame_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(View);
        var d = p - _mouseLast;
        if (_leftDown)
        {
            if (!_dragging && (p - _mouseStart).Length > 3) _dragging = true;
            if (_dragging)
            {
                if (_lightDrag)
                {
                    _light.Azimuth = NormalizeAngle(_light.Azimuth + d.X * 0.5);
                    _light.Elevation = Math.Clamp(_light.Elevation - d.Y * 0.4, 5, 90);
                    LightChanged();
                }
                else if (_figureDrag) DragFigure(p);
                else if (_propDrag) DragProp(d);
                else if (_jointDrag && _figure.Selected is { } j) DragJoint(j, d);
                else
                {
                    _cam.Yaw -= d.X * 0.4;
                    _cam.Pitch = Math.Clamp(_cam.Pitch + d.Y * 0.3, -85, 85);
                    CameraChanged();
                }
            }
        }
        else if (_rightDown)
        {
            double scale = _cam.Distance * Math.Tan(_cam.VerticalFov * Math.PI / 360) * 2 / Math.Max(1, View.ActualHeight);
            _cam.Target = _cam.Target - _cam.Right * d.X * scale + _cam.Up * d.Y * scale;
            CameraChanged();
        }
        _mouseLast = p;
    }

    private Vector3D? _ikTargetRel; // Zielpunkt beim Ziehen von Hand/Fuß, relativ zur Figurposition

    /// <summary>Schnittpunkt des Mausstrahls mit der Bodenebene (y = 0); null, wenn der Strahl über den Horizont zeigt.</summary>
    private Point3D? GroundHit(Point p)
    {
        double W = View.ActualWidth, H = View.ActualHeight;
        if (W < 1 || H < 1) return null;
        double t = Math.Tan(_cam.VerticalFov * Math.PI / 360);
        double nx = (p.X - W / 2) / (H / 2) * t, ny = -(p.Y - H / 2) / (H / 2) * t;
        var dir = _cam.LookDirection + _cam.Right * nx + _cam.Up * ny;
        var o = _cam.Position;
        if (dir.Y > -0.02) return null;               // (fast) parallel zum Boden oder nach oben
        double s = -o.Y / dir.Y;
        if (s <= 0 || s > 80) return null;
        return o + dir * s;
    }

    /// <summary>Figur folgt der Maus auf dem Boden.</summary>
    private void DragFigure(Point p)
    {
        var hit = GroundHit(p);
        if (hit == null || _lastGroundHit == null) { _lastGroundHit = hit; return; }
        var d = hit.Value - _lastGroundHit.Value;
        _lastGroundHit = hit;
        var r = _figure.RootPosition;
        _figure.RootPosition = new Vector3D(r.X + d.X, r.Y, r.Z + d.Z);
        PoseChanged(false);
    }

    private static bool IsLimbEnd(Joint j) => j.Id.StartsWith("wrist") || j.Id.StartsWith("ankle");

    /// <summary>Hand/Fuß mit der Maus führen: der Zielpunkt folgt der Maus in der Bildebene, Arm bzw. Bein per IK.</summary>
    private void DragLimbEnd(Joint j, Vector d)
    {
        if (_figure.HandHoldsProp(j))
        {
            StatusText.Text = "Diese Hand hält ein Objekt – bitte das Objekt bewegen (Strg + ziehen am Objekt).";
            return;
        }
        var root = _figure.RootPosition;
        if (_ikTargetRel == null)
            _ikTargetRel = (Vector3D)j.World.Transform(new Point3D(0, 0, 0)) - root;
        var cur = (Point3D)(_ikTargetRel.Value + root);
        double z = Math.Max(0.2, Vector3D.DotProduct(cur - _cam.Position, _cam.LookDirection));
        double px = z * Math.Tan(_cam.VerticalFov * Math.PI / 360) * 2 / Math.Max(1, View.ActualHeight);
        var target = cur + _cam.Right * d.X * px - _cam.Up * d.Y * px;
        _ikTargetRel = (Vector3D)target - root;
        _figure.ReachTo(j, target);
    }

    private void DragJoint(Joint j, Vector d)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && IsLimbEnd(j))
        {
            DragLimbEnd(j, d);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _figure.IsLimbJoint(j))
        {
            // Arm/Bein: Ellbogen bzw. Knie um die Achse Schulter–Hand schwenken, Hand/Fuß bleiben stehen
            if (j.Id.StartsWith("wrist") || j.Id.StartsWith("elbow") || j.Id.StartsWith("shoulder"))
            {
                var wrist = _figure["wrist" + j.Id[^2..]];
                if (_figure.HandHoldsProp(wrist)) { StatusText.Text = "Diese Hand hält ein Objekt – das Objekt steuert den Arm."; return; }
            }
            _figure.SwivelLimb(j, d.X * 0.6);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (j.Id == "head" && j.Side != null) j.SideValue = Math.Clamp(Math.Round(j.SideValue + d.X * 0.3, 1), j.Side.Min, j.Side.Max);
            else if (j.Twist != null) j.TwistValue = Math.Clamp(Math.Round(j.TwistValue + d.X * 0.5, 1), j.Twist.Min, j.Twist.Max);
        }
        else
        {
            FollowMouse(j, d);
        }
        PoseChanged();
    }

    /// <summary>Bewegt das Körperteil so, dass sein Ende der Maus folgt (gedämpfte kleinste Quadrate
    /// über die Achsen Beugen/Seitlich) – funktioniert dadurch aus jeder Kameraperspektive.</summary>
    private void FollowMouse(Joint j, Vector mouse)
    {
        var axes = new List<(Func<double> get, Action<double> set, AxisDef def)>();
        if (j.Bend != null) axes.Add((() => j.BendValue, v => j.BendValue = v, j.Bend));
        // Kopf: Blick folgt der Maus – waagerecht drehen, senkrecht nicken (seitlich neigen per Umschalt)
        if (j.Id == "head" && j.Twist != null) axes.Add((() => j.TwistValue, v => j.TwistValue = v, j.Twist));
        else if (j.Side != null) axes.Add((() => j.SideValue, v => j.SideValue = v, j.Side));
        if (axes.Count == 0) return;

        var probe = ProbePoint(j);
        var p0 = ToScreen(j.World.Transform(probe));
        var jac = new Vector[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            var (get, set, _) = axes[i];
            double old = get();
            set(old + 1);
            _figure.Update();
            jac[i] = ToScreen(j.World.Transform(probe)) - p0; // Pixel pro Grad
            set(old);
        }
        _figure.Update();

        const double damping = 0.3;
        double[] delta;
        if (axes.Count == 1)
        {
            delta = new[] { (jac[0] * mouse) / (jac[0].LengthSquared + damping) };
        }
        else
        {
            double a = jac[0].LengthSquared + damping, b = jac[0] * jac[1], c = jac[1].LengthSquared + damping;
            double r0 = jac[0] * mouse, r1 = jac[1] * mouse;
            double det = a * c - b * b;
            if (Math.Abs(det) < 1e-9) return;
            delta = new[] { (c * r0 - b * r1) / det, (a * r1 - b * r0) / det };
        }
        for (int i = 0; i < axes.Count; i++)
        {
            var (get, set, def) = axes[i];
            set(Math.Clamp(get() + Math.Clamp(delta[i], -8, 8), def.Min, def.Max));
        }
    }

    private Point3D ProbePoint(Joint j)
    {
        if (j.Parent == null) return new Point3D(0, 0.5, 0);
        if (j.Id == "head") return new Point3D(0, 0.09, 0.35);   // Punkt vor dem Gesicht: der Blick folgt der Maus
        var child = _figure.Joints.FirstOrDefault(c => ReferenceEquals(c.Parent, j));
        if (child != null) return (Point3D)child.Offset;
        if (j.IsFinger) return new Point3D(0, -0.02, 0);
        return j.Id.StartsWith("ankle") ? new Point3D(0, -0.04, 0.15) : new Point3D(0, -0.15, 0); // Hand: Richtung Fingerspitzen
    }

    private Point ToScreen(Point3D p)
    {
        var v = p - _cam.Position;
        double z = Math.Max(0.01, Vector3D.DotProduct(v, _cam.LookDirection));
        // Ansicht noch nicht angeordnet (Testmodus ohne Anzeige): feste Größe annehmen
        double W = View.ActualHeight >= 1 ? View.ActualWidth : 1280, H = View.ActualHeight >= 1 ? View.ActualHeight : 720;
        double f = (H / 2) / Math.Tan(_cam.VerticalFov * Math.PI / 360);
        return new Point(W / 2 + Vector3D.DotProduct(v, _cam.Right) * f / z,
                         H / 2 - Vector3D.DotProduct(v, _cam.Up) * f / z);
    }

    private void Frame_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _cam.Distance = Math.Clamp(_cam.Distance * (e.Delta > 0 ? 0.9 : 1.1), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
        CameraChanged();
    }

    // =================================================================== Export

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Zielordner für die JPG-Dateien" };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder)) { StatusText.Text = "Der Zielordner existiert noch nicht."; return; }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private static string SafeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "pose" : s.Trim();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var formats = _exportFormats.Where(f => f.IsChecked).Select(f => f.Format).ToList();
        if (formats.Count == 0)
        {
            MessageBox.Show(this, "Bitte mindestens ein Exportformat anhaken.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int longSide = ParseInt(LongSideCombo.Text, 0);
        if (longSide < 64 || longSide > 8192)
        {
            MessageBox.Show(this, "Die lange Seite muss zwischen 64 und 8192 Pixeln liegen.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int cw = ParseInt(CustomWBox.Text, 1200), ch = ParseInt(CustomHBox.Text, 1600);
        if (formats.Any(f => f.IsCustom) && (cw < 16 || ch < 16 || cw > 8192 || ch > 8192))
        {
            MessageBox.Show(this, "Benutzerdefinierte Größe: Breite und Höhe müssen zwischen 16 und 8192 Pixeln liegen.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var folder = FolderBox.Text.Trim();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Zielordner kann nicht angelegt werden:\n" + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool png = PngRadio.IsChecked == true;
        bool transparent = TransparentActive;
        var opts = new SceneOptions
        {
            Background = transparent ? Colors.Transparent : _background,
            FloorBase = _background,
            Floor = FloorExportCheck.IsChecked == true,
            Shadow = ShadowExportCheck.IsChecked == true,
            BackgroundImage = _bgImage,
            BackgroundStretch = CurrentStretch,
            Light = _light,
            Set = _set,
            Transparent = transparent,
            ShadowResolution = 2048
        };
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string prefix = SafeFileName(PrefixBox.Text);
        int quality = (int)QualitySlider.Value;
        var written = new List<string>();

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (var f in formats)
            {
                var (w, h) = f.PixelSize(longSide, cw, ch);
                string tag = f.IsCustom || f.Fixed ? $"{w}x{h}" : f.Key;
                if (CnOnlyCheck.IsChecked != true || (Cn3dCheck.IsChecked != true && CnRegionCheck.IsChecked != true))
                {
                    var bmp = SceneFactory.Render(_figures, _cam, opts, w, h);
                    string path = Path.Combine(folder, $"{prefix}_{stamp}_{tag}.{(png ? "png" : "jpg")}");
                    if (png) SceneFactory.SavePng(bmp, path);
                    else SceneFactory.SaveJpeg(bmp, path, quality);
                    written.Add(Path.GetFileName(path));
                }
                if (Cn3dCheck.IsChecked == true)
                {
                    string path = Path.Combine(folder, $"{prefix}_{stamp}_{tag}_cn3dpose.png");
                    SceneFactory.SavePng(SceneFactory.RenderControl(_figures, _cam, SceneFactory.ControlMode.Pose3D, w, h), path);
                    written.Add(Path.GetFileName(path));
                }
                if (CnRegionCheck.IsChecked == true)
                {
                    string path = Path.Combine(folder, $"{prefix}_{stamp}_{tag}_cnregions.png");
                    SceneFactory.SavePng(SceneFactory.RenderControl(_figures, _cam, SceneFactory.ControlMode.Regions, w, h), path);
                    written.Add(Path.GetFileName(path));
                }
            }
            StatusText.Text = $"{written.Count} Datei(en) gespeichert in {folder}: {string.Join(", ", written)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export fehlgeschlagen:\n" + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
