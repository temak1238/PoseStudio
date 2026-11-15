using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private Color _background;
    private bool _ready;

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

        PresetCombo.ItemsSource = Presets.All;
        PresetCombo.SelectedIndex = 0;
        JointList.ItemsSource = _figure.Joints;
        AxisItems.ItemsSource = _axes;
        PositionItems.ItemsSource = _positionAxes;
        CameraItems.ItemsSource = _cameraAxes;
        ExportFormatList.ItemsSource = _exportFormats;

        BuildPositionAxes();
        BuildCameraAxes();
        BuildColorPresets();
        LoadSettings();

        _figure.SetPose(Presets.All[0].ToPose());
        _figure.GroundToFloor();
        _ready = true;

        CustomWBox.LostFocus += CustomSize_LostFocus;
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
        _lights.Children.Add(SceneFactory.CreateLights(_cam));
        foreach (var a in _cameraAxes) a.Refresh();
    }

    private void RebuildFloor()
    {
        if (!_ready) return;
        _floor.Children.Clear();
        var o = new SceneOptions
        {
            Background = _background,
            Floor = FloorPreviewCheck.IsChecked == true,
            Shadow = ShadowPreviewCheck.IsChecked == true
        };
        if (o.Floor || o.Shadow) _floor.Children.Add(SceneFactory.CreateFloor(o, _figure.PelvisWorld));
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
        // Ganze Figur: Abstand so wählen, dass ca. 2,1 m Höhe ins Bild passen.
        var pel = _figure.PelvisWorld;
        _cam.Target = new Point3D(pel.X, 0.92, pel.Z);
        _cam.Distance = Math.Clamp(1.05 / Math.Tan(_cam.VerticalFov * Math.PI / 360), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
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
        Frame.Background = new SolidColorBrush(c);
        BgSwatch.Background = new SolidColorBrush(c);
        BgHexBox.Text = ToHex(c);
        if (refresh) RebuildFloor();
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
        bool mod = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
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
                if (_jointDrag && _figure.Selected is { } j) DragJoint(j, d);
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
            if (j.Twist != null) j.TwistValue = Math.Clamp(j.TwistValue + d.X * 0.5, j.Twist.Min, j.Twist.Max);
        }
        else
        {
            if (j.Bend != null) j.BendValue = Math.Clamp(j.BendValue - d.Y * 0.5, j.Bend.Min, j.Bend.Max);
            if (j.Side != null) j.SideValue = Math.Clamp(j.SideValue + d.X * 0.5, j.Side.Min, j.Side.Max);
        }
        PoseChanged();
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

        var opts = new SceneOptions
        {
            Background = _background,
            Floor = FloorExportCheck.IsChecked == true,
            Shadow = ShadowExportCheck.IsChecked == true
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
                string tag = f.IsCustom ? $"{w}x{h}" : f.Key;
                string path = Path.Combine(folder, $"{prefix}_{stamp}_{tag}.jpg");
                SceneFactory.SaveJpeg(bmp, path, quality);
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
