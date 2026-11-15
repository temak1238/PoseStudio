using System.IO;
using System.Windows;
using PoseStudio.Model;
using PoseStudio.Rendering;

namespace PoseStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Pose Studio – Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Testmodus: rendert alle Vorlagen als JPG und beendet sich (für Entwicklung/Prüfung).
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SelfTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--make-icon")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            IconMaker.Write(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--presets")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PresetSheet(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : "Male");
            Shutdown();
            return;
        }

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    /// <summary>Alle Pose-Vorlagen als Kontaktbogen (je Vorlage 3/4-Ansicht und Seite).</summary>
    private static void PresetSheet(string folder, string style)
    {
        Directory.CreateDirectory(folder);
        var m = new Mannequin();
        m.SetFigure(Enum.Parse<FigureStyle>(style), true, new Proportions());
        var opts = new SceneOptions { Floor = true, Shadow = true, Light = new LightSettings { CastShadow = false } };
        const int cw = 170, ch = 250, perRow = 8;
        int rows = (Presets.All.Count + perRow - 1) / perRow;
        int W = cw * 2 * perRow, H = ch * rows;
        var dv = new System.Windows.Media.DrawingVisual();
        var tf = new System.Windows.Media.Typeface("Segoe UI");
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, W, H));
            for (int i = 0; i < Presets.All.Count; i++)
            {
                m.SetPose(Presets.All[i].ToPose()); m.GroundToFloor();
                double minY = m.ComputeMinY(), maxY = m.ComputeMaxY();
                var pel = m.PelvisWorld;
                for (int v = 0; v < 2; v++)
                {
                    var cam = new OrbitCamera { Yaw = v == 0 ? 30 : 95, Pitch = 10, Target = new System.Windows.Media.Media3D.Point3D(pel.X, (minY + maxY) / 2, pel.Z), Distance = 5.2 };
                    double x = ((i % perRow) * 2 + v) * cw, y = (i / perRow) * ch;
                    dc.DrawImage(SceneFactory.Render(m, cam, opts, cw, ch), new Rect(x, y, cw, ch));
                    if (v == 0)
                        dc.DrawText(new System.Windows.Media.FormattedText($"{i}: {Presets.All[i].Name}", System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, tf, 11, System.Windows.Media.Brushes.Black, 1.0), new Point(x + 3, y + 2));
                }
            }
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W, H, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"presets_{style}.jpg"), 90);
    }

    private static void SelfTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var m = new Mannequin();
        var opts = new SceneOptions { Floor = false, Shadow = true };
        int[] presets = { 0, 2, 4, 6, 8, 3 };
        var styles = new[] { FigureStyle.Mannequin, FigureStyle.Male, FigureStyle.Female };
        const int cw = 240, ch = 360;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, cw * presets.Length * 2, ch * styles.Length + 320));
            for (int si = 0; si < styles.Length; si++)
            {
                m.SetFigure(styles[si], true, new Proportions { HeightCm = styles[si] == FigureStyle.Female ? 168 : 180 });
                for (int pi = 0; pi < presets.Length; pi++)
                {
                    m.SetPose(Presets.All[presets[pi]].ToPose());
                    m.GroundToFloor();
                    for (int v = 0; v < 2; v++)
                    {
                        var cam = new OrbitCamera { Yaw = v == 0 ? 25 : 100, Target = new System.Windows.Media.Media3D.Point3D(0, 0.9, 0), Distance = 4.6 };
                        var bmp = SceneFactory.Render(m, cam, opts, cw, ch);
                        dc.DrawImage(bmp, new Rect((pi * 2 + v) * cw, si * ch, cw, ch));
                    }
                }
            }
            // Gesichter in Nahaufnahme
            int x = 0;
            foreach (var st in new[] { FigureStyle.Male, FigureStyle.Female })
            {
                m.SetFigure(st, true, new Proportions());
                m.SetPose(Presets.All[0].ToPose()); m.GroundToFloor();
                foreach (var yaw in new[] { 0.0, 35.0, 90.0 })
                {
                    var head = m["head"].World.Transform(new System.Windows.Media.Media3D.Point3D(0, 0.1, 0));
                    var cam = new OrbitCamera { Yaw = yaw, Pitch = 0, Target = head, Distance = 1.1 };
                    dc.DrawImage(SceneFactory.Render(m, cam, opts, 320, 320), new Rect(x, styles.Length * ch, 320, 320));
                    x += 320;
                }
            }
            // Proportionen-Test: lange Beine, kurze Arme, großer Kopf
            m.SetFigure(FigureStyle.Male, false, new Proportions { HeightCm = 150, Leg = 125, Arm = 80, Head = 130, Build = 140 });
            m.SetPose(Presets.All[1].ToPose()); m.GroundToFloor();
            dc.DrawImage(SceneFactory.Render(m, new OrbitCamera(), opts, 320, 320), new Rect(x, styles.Length * ch, 320, 320));
        }
        int W = cw * presets.Length * 2, Hh = ch * styles.Length + 320;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W, Hh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "styles_sheet.jpg"), 90);

        // Licht & Schattenwurf inkl. Eigenschatten
        var lights = new[] { (35.0, 50.0, 30.0), (90.0, 25.0, 10.0), (180.0, 35.0, 60.0), (20.0, 85.0, 30.0) };
        var rows = new[] { (FigureStyle.Male, 8), (FigureStyle.Mannequin, 7), (FigureStyle.Female, 3) };
        var lv = new System.Windows.Media.DrawingVisual();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var dc = lv.RenderOpen())
            for (int row = 0; row < rows.Length; row++)
            {
                m.SetFigure(rows[row].Item1, true, new Proportions());
                m.SetPose(Presets.All[rows[row].Item2].ToPose()); m.GroundToFloor();
                for (int i = 0; i < lights.Length; i++)
                {
                    var lo = new SceneOptions { Floor = true, Shadow = true, ShadowResolution = 1024,
                        Light = new LightSettings { Azimuth = lights[i].Item1, Elevation = lights[i].Item2, Softness = lights[i].Item3 } };
                    var cam = new OrbitCamera { Yaw = 30, Pitch = 12, Distance = 4.4, Target = new System.Windows.Media.Media3D.Point3D(0, 0.9, 0) };
                    dc.DrawImage(SceneFactory.Render(m, cam, lo, 480, 560), new Rect(i * 480, row * 560, 480, 560));
                }
            }
        var lr = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 560 * rows.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        lr.Render(lv);
        SceneFactory.SaveJpeg(lr, Path.Combine(folder, "light_sheet.jpg"), 90);
        sw.Stop();
        m.SetFigure(FigureStyle.Male, true, new Proportions()); m.SetPose(Presets.All[8].ToPose()); m.GroundToFloor();
        foreach (var cast in new[] { false, true })
        {
            var lo = new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Azimuth = 35, Elevation = 50, CastShadow = cast } };
            var cam = new OrbitCamera { Yaw = 30, Pitch = 5, Distance = 1.6, Target = new System.Windows.Media.Media3D.Point3D(0, 1.25, 0) };
            SceneFactory.SaveJpeg(SceneFactory.Render(m, cam, lo, 700, 700), Path.Combine(folder, cast ? "torso_shadow.jpg" : "torso_plain.jpg"), 92);
        }
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) SelfShadow.Apply(m, new OrbitCamera(), new LightSettings());
        File.WriteAllText(Path.Combine(folder, "timing.txt"), $"12 Renderings: {sw.ElapsedMilliseconds} ms, Eigenschatten: {sw2.ElapsedMilliseconds / 10} ms je Berechnung");
    }
}