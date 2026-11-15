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

public partial class MainWindow : Window
{
    private readonly Mannequin _figure = new();
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
    private Proportions _props = new();
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
    };
    private static readonly (Stretch Mode, string Name)[] StretchModes =
    {
        (Stretch.UniformToFill, "Füllen (zuschneiden)"),
        (Stretch.Uniform, "Einpassen"),
        (Stretch.Fill, "Strecken"),
    };
    private static readonly Color MannequinColor = Color.FromRgb(0xC9, 0xB2, 0x92);
    private static readonly Color SkinColor = Color.FromRgb(0xE3, 0xBE, 0xA2);
    private static readonly string[] HairPresets = { "#3A2A20", "#151515", "#7A4A25", "#D9B866", "#A33A1E", "#9C9C9C" };

    // Maus
    private Point _mouseStart, _mouseLast;
    private bool _leftDown, _rightDown, _dragging, _jointDrag;
    private GeometryModel3D? _pressedPart;

    private static readonly string[] BgPresets = { "#FFFFFF", "#E6E6E6", "#9E9E9E", "#3A3A3A", "#000000", "#00B140", "#2B6CB0" };
    private static readonly string[] BodyPresets = { "#C9B292", "#BDBDBD", "#7A7A7A", "#F2F2F2", "#5C8DD6" };

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Pose Studio {GetVersion()}";

        _scene.Children.Add(_lights);
        _scene.Children.Add(_floor);
        _scene.Children.Add(_figure.Group);
        SceneVisual.Content = _scene;

        // Vorlagen nach Kategorie gruppiert (Reihenfolge der Kategorien wie im ersten Auftreten)
        var cats = Presets.All.Select(p => p.Category).Distinct().ToList();
        var presetView = new System.Windows.Data.ListCollectionView(Presets.All.OrderBy(p => cats.IndexOf(p.Category)).ToList());
        presetView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PosePreset.Category)));
        PresetCombo.ItemsSource = presetView;
        PresetCombo.SelectedIndex = 0;
        JointList.ItemsSource = _figure.Joints;
        AxisItems.ItemsSource = _axes;
        PositionItems.ItemsSource = _positionAxes;
        CameraItems.ItemsSource = _cameraAxes;
        ProportionItems.ItemsSource = _proportionAxes;
        FigureStyleCombo.ItemsSource = Styles.Select(s => s.Name).ToList();
        BgStretchCombo.ItemsSource = StretchModes.Select(s => s.Name).ToList();

        BuildPositionAxes();
        BuildCameraAxes();
        BuildProportionAxes();
        BuildLightAxes();
        LightItems.ItemsSource = _lightAxes;
        _scene.Children.Add(_lightMarker);
        BuildColorPresets();
        LoadSettings();

        _figure.SetPose(Presets.All[0].ToPose());
        _figure.GroundToFloor();
        _ready = true;
        foreach (var f in _exportFormats) f.PropertyChanged += (_, _) => DrawGuides();

        CustomWBox.LostFocus += CustomSize_LostFocus;
        // Alt allein soll nicht das Systemmenü aktivieren (Alt + ziehen bewegt das Licht).
        PreviewKeyDown += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        Closing += (_, _) => SaveSettings();
        Loaded += (_, _) => { UpdateFrameSize(); RefreshAll(); };
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

    private FigureStyle CurrentStyle => FigureStyleCombo.SelectedIndex >= 0 ? Styles[FigureStyleCombo.SelectedIndex].Style : FigureStyle.Mannequin;

    /// <summary>Überträgt Stil, Haare und Proportionen auf die Figur (Pose bleibt erhalten).</summary>
    private void ApplyFigure()
    {
        if (_suppressFigure) return;
        _figure.SetFigure(CurrentStyle, HairCheck.IsChecked == true, _props);
        HairCheck.IsEnabled = CurrentStyle != FigureStyle.Mannequin;
        PoseChanged();
    }

    private void ShowFigure(FigureData fd)
    {
        _suppressFigure = true;
        _props = fd.Proportions.Clone();
        _props.Clamp();
        FigureStyleCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == fd.Style));
        HairCheck.IsChecked = fd.Hair;
        _suppressFigure = false;
        foreach (var a in _proportionAxes) a.Refresh();
        ApplyFigure();
    }

    private void FigureStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFigure) return;
        var style = CurrentStyle;
        // Sinnvolle Standards beim Wechsel: Körpergröße und Farbe (Holz ↔ Haut), sofern nicht selbst geändert.
        if (style == FigureStyle.Female && Math.Abs(_props.HeightCm - 180) < 0.5) _props.HeightCm = 168;
        if (style != FigureStyle.Female && Math.Abs(_props.HeightCm - 168) < 0.5) _props.HeightCm = 180;
        if (style == FigureStyle.Mannequin && _figure.BodyColor == SkinColor) SetBodyColor(MannequinColor);
        if (style != FigureStyle.Mannequin && _figure.BodyColor == MannequinColor) SetBodyColor(SkinColor);
        foreach (var a in _proportionAxes) a.Refresh();
        ApplyFigure();
    }

    private void Figure_Changed(object sender, RoutedEventArgs e) => ApplyFigure();

    private void ResetProportions_Click(object sender, RoutedEventArgs e)
    {
        _props = new Proportions { HeightCm = CurrentStyle == FigureStyle.Female ? 168 : 180 };
        foreach (var a in _proportionAxes) a.Refresh();
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

    private void LoadSettings()
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
        FolderBox.Text = s.OutputFolder;
        PrefixBox.Text = s.FilePrefix;
        SetHairColor(ParseColor(s.HairColor, Color.FromRgb(0x3A, 0x2A, 0x20)));
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
        s.OutputFolder = FolderBox.Text;
        s.FilePrefix = PrefixBox.Text;
        s.HairColor = ToHex(_figure.HairColor);
        s.Guides = GuidesCheck.IsChecked == true;
        s.OutputPng = PngRadio.IsChecked == true;
        s.Transparent = TransparentCheck.IsChecked == true;
        s.BackgroundImage = _bgImagePath;
        s.BackgroundStretch = StretchModes[Math.Max(0, BgStretchCombo.SelectedIndex)].Mode.ToString();
        s.Figure = _figure.GetFigure();
        s.Light = _light.Clone();
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
        if (_light.FollowCamera) RebuildFloor();
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
        SelfShadow.Apply(_figure, _cam, _light);
        _floor.Children.Clear();
        var o = new SceneOptions
        {
            Background = _background,
            Floor = FloorPreviewCheck.IsChecked == true,
            Shadow = ShadowPreviewCheck.IsChecked == true,
            Light = _light,
            ShadowResolution = 640
        };
        if (o.Floor || o.Shadow) _floor.Children.Add(SceneFactory.CreateFloor(o, _figure, _cam));
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

    // =================================================================== Gelenkauswahl

    private void SelectJoint(Joint? j)
    {
        _figure.Select(j);
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

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auswahl in der Liste wendet die Vorlage direkt an (nicht beim Programmstart)
        if (_ready && PresetCombo.IsDropDownOpen) ApplyPreset_Click(sender, e);
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PosePreset p) return;
        PushUndo();
        _figure.SetPose(p.ToPose());
        _figure.GroundToFloor();
        PoseChanged();
        StatusText.Text = $"Vorlage „{p.Name}“ angewendet.";
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
        var pose = _figure.GetPose();
        pose.Camera = _cam.ToData();
        pose.Figure = _figure.GetFigure();
        pose.Light = _light.Clone();
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
            PushUndo();
            if (pose.Figure != null) ShowFigure(pose.Figure);
            if (pose.Light != null) ShowLight(pose.Light);
            _figure.SetPose(pose);
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
        double min = _figure.ComputeMinY(), max = _figure.ComputeMaxY();
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
    private bool TransparentActive => PngRadio.IsChecked == true && TransparentCheck.IsChecked == true && _bgImage == null;

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
            Frame.Background = new SolidColorBrush(_background);
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

    // =================================================================== Hilfslinien

    private void Guides_Changed(object sender, RoutedEventArgs e) => DrawGuides();

    /// <summary>Zeichnet für jedes angehakte Exportformat, das schmaler als die Vorschau ist, seinen
    /// Bildausschnitt als gestrichelte Linien. (Die Bildhöhe ist bei allen Formaten gleich, nur die Breite ändert sich.)</summary>
    private void DrawGuides()
    {
        if (!_ready) return;
        GuideCanvas.Children.Clear();
        if (GuidesCheck.IsChecked != true) return;
        double W = View.ActualWidth, H = View.ActualHeight;
        if (W < 10 || H < 10) return;
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
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D g && _figure.TryGetJoint(g, out _))
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
        _lightDrag = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool mod = !_lightDrag && (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _jointDrag = false;
        if (mod)
        {
            // Strg/Umschalt: angeklicktes (oder bereits gewähltes) Gelenk mit der Maus bewegen.
            if (_pressedPart != null && _figure.TryGetJoint(_pressedPart, out var j) && !ReferenceEquals(j, _figure.Selected))
                SelectJoint(j);
            if (_figure.Selected != null) { _jointDrag = true; PushUndo(); }
        }
        Frame.CaptureMouse();
        Frame.Focus();
    }

    private void Frame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_leftDown && !_dragging)
        {
            // Einfacher Klick: Gelenk auswählen bzw. Auswahl aufheben.
            if (_pressedPart != null && _figure.TryGetJoint(_pressedPart, out var j)) SelectJoint(j);
            else if (!_jointDrag) SelectJoint(null);
        }
        _leftDown = false;
        _jointDrag = false;
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

    private void DragJoint(Joint j, Vector d)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (j.Twist != null) j.TwistValue = Math.Clamp(Math.Round(j.TwistValue + d.X * 0.5, 1), j.Twist.Min, j.Twist.Max);
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
        if (j.Side != null) axes.Add((() => j.SideValue, v => j.SideValue = v, j.Side));
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
        var child = _figure.Joints.FirstOrDefault(c => ReferenceEquals(c.Parent, j));
        if (child != null) return (Point3D)child.Offset;
        return j.Id.StartsWith("ankle") ? new Point3D(0, -0.04, 0.15) : new Point3D(0, -0.15, 0); // Hand: Richtung Fingerspitzen
    }

    private Point ToScreen(Point3D p)
    {
        var v = p - _cam.Position;
        double z = Math.Max(0.01, Vector3D.DotProduct(v, _cam.LookDirection));
        double f = (View.ActualHeight / 2) / Math.Tan(_cam.VerticalFov * Math.PI / 360);
        return new Point(View.ActualWidth / 2 + Vector3D.DotProduct(v, _cam.Right) * f / z,
                         View.ActualHeight / 2 - Vector3D.DotProduct(v, _cam.Up) * f / z);
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
                var bmp = SceneFactory.Render(_figure, _cam, opts, w, h);
                string tag = f.IsCustom || f.Fixed ? $"{w}x{h}" : f.Key;
                string path = Path.Combine(folder, $"{prefix}_{stamp}_{tag}.{(png ? "png" : "jpg")}");
                if (png) SceneFactory.SavePng(bmp, path);
                else SceneFactory.SaveJpeg(bmp, path, quality);
                written.Add(Path.GetFileName(path));
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
