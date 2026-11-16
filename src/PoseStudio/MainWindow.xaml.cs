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
    private bool _leftDown, _rightDown, _dragging, _jointDrag, _propDrag, _figureDrag;
    private Point3D? _lastGroundHit;
    private GeometryModel3D? _pressedPart;

    private static readonly string[] BgPresets = { "#FFFFFF", "#E6E6E6", "#9E9E9E", "#3A3A3A", "#000000", "#00B140", "#2B6CB0" };
    private static readonly string[] BodyPresets = { "#C9B292", "#BDBDBD", "#7A7A7A", "#F2F2F2", "#5C8DD6" };

    public MainWindow()
    {
        InitializeComponent();
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
        foreach (var f in _exportFormats) f.PropertyChanged += (_, _) => DrawGuides();

        CustomWBox.LostFocus += CustomSize_LostFocus;
        // Alt allein soll nicht das Systemmenü aktivieren (Alt + ziehen bewegt das Licht).
        PreviewKeyDown += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        Closing += (_, _) => { try { SaveSettings(); } catch { /* beim Beenden nie blockieren */ } };
        Closed += (_, _) => ShutdownCleanly();
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
        _suppressFigureList = true;
        FigureList.ItemsSource = _figures.Select((fg, i) =>
        {
            string style = fg.Style switch { FigureStyle.Male => "Mann", FigureStyle.Female => "Frau", _ => "Gliederpuppe" };
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
        JointList.ItemsSource = _figure.Joints;
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
        if (_ready) RefreshFigureList();
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
        ShowSet(s.Set ?? new SetSettings());
        _figure.SetProp(s.Prop ?? new PropSettings());
        ShowProp();
        Cn3dCheck.IsChecked = s.ControlNet3D;
        CnRegionCheck.IsChecked = s.ControlNetRegions;
        CnOnlyCheck.IsChecked = s.ControlNetOnly;
        ClickSelectsFigureCheck.IsChecked = s.ClickSelectsFigure;
        RefOpacitySlider.Value = Math.Clamp(s.ReferenceOpacity, 10, 90);
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
        SelfShadow.Apply(_figures, _cam, _light);
        _floor.Children.Clear();
        var o = new SceneOptions
        {
            Background = _background,
            Floor = FloorPreviewCheck.IsChecked == true,
            Shadow = ShadowPreviewCheck.IsChecked == true,
            Light = _light,
            Set = _set,
            ShadowResolution = 640
        };
        if (o.Floor || o.Shadow) _floor.Children.Add(SceneFactory.CreateFloor(o, _figures, _cam));
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
            _figure.FitLandmarks(PoseStudio.Detection.PoseDetector.ToScene(pose, _cam));
            _figure.RootPosition = root;
            _figure.GroundToFloor();
            PoseChanged();
            ShowProp();
            DrawGuides();
            StatusText.Text = $"Pose erkannt (Sicherheit {pose.Confidence:P0}) und übernommen – bei Bedarf nachkorrigieren (Strg+Z macht es rückgängig).";
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
