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

        if (e.Args.Length >= 2 && e.Args[0] == "--ik")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            IkTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--detect")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DetectTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--sitting")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SittingTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--controlnet")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ControlNetTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--props")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PropSheet(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : "Male");
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--sets")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SetSheet(e.Args[1]);
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

    /// <summary>Alle Kulissen mit Figur, Licht und Schatten (je 3/4-Ansicht und Rückansicht).</summary>
    /// <summary>Alle Objekte × Haltungen: Ganzkörper 3/4-Ansicht und Nahaufnahme der Haupthand.</summary>
    /// <summary>Vier Figuren (ähnlich dem Beispiel aus dem anima_3dpose-Repo) als ControlNet-Bilder und normales Bild.</summary>
    /// <summary>Nahaufnahmen der Hüfte in Sitzposen (Prüfung der Gelenkübergänge).</summary>
    /// <summary>Selbsttest der Posenerkennung: eigene Renderings erkennen, auf neue Figur übertragen, vergleichen.</summary>
    /// <summary>IK-Test: rechte Hand schrittweise von der Hüfte an den Kopf, dann Ellbogen schwenken.</summary>
    private static void IkTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        var m = new Mannequin();
        m.SetFigure(FigureStyle.Male, true, new Proportions());
        m.SetPose(Presets.All[0].ToPose()); m.GroundToFloor();
        var wrist = m["wrist_r"];
        var start = wrist.World.Transform(new System.Windows.Media.Media3D.Point3D());
        var head = m["head"].World.Transform(new System.Windows.Media.Media3D.Point3D(-0.09, 0.08, 0.02)); // rechte Kopfseite
        var cams = new[] { new OrbitCamera { Yaw = 0, Pitch = 5, Distance = 2.6, Target = new System.Windows.Media.Media3D.Point3D(0, 1.3, 0) },
                           new OrbitCamera { Yaw = -70, Pitch = 10, Distance = 2.6, Target = new System.Windows.Media.Media3D.Point3D(0, 1.3, 0) } };
        const int W = 300, H = 360, steps = 5;
        var frames = new List<(string label, System.Windows.Media.Imaging.BitmapSource a, System.Windows.Media.Imaging.BitmapSource b)>();
        void Snap(string label) => frames.Add((label, SceneFactory.RenderControl(new[] { m }, cams[0], SceneFactory.ControlMode.Pose3D, W, H),
                                                      SceneFactory.RenderControl(new[] { m }, cams[1], SceneFactory.ControlMode.Pose3D, W, H)));
        Snap("Start");
        for (int s = 1; s <= 30; s++)
        {
            double tt = s / 30.0;
            // leicht nach vorne gebogener Weg (wie eine Mausbewegung vor dem Körper)
            var p = start + (head - start) * tt + new System.Windows.Media.Media3D.Vector3D(0, 0, 0.18 * Math.Sin(Math.PI * tt));
            double err = m.ReachTo(wrist, p);
            if (s % (30 / steps) == 0) { Snap($"Schritt {s}"); log.AppendLine($"Schritt {s}: Abstand Hand-Ziel {err * 100:F1} cm"); }
        }
        foreach (var sw in new[] { 40, 40 })
        {
            var before = wrist.World.Transform(new System.Windows.Media.Media3D.Point3D());
            m.SwivelLimb(m["elbow_r"], sw);
            var after = wrist.World.Transform(new System.Windows.Media.Media3D.Point3D());
            log.AppendLine($"Schwenken {sw}°: Hand bewegt um {(after - before).Length * 100:F2} cm – {m.LastLimbDebug}");
            Snap($"Ellbogen +{sw}°");
        }
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < frames.Count; i++)
            {
                dc.DrawImage(frames[i].a, new Rect(i * W, 0, W, H));
                dc.DrawImage(frames[i].b, new Rect(i * W, H, W, H));
                dc.DrawText(new System.Windows.Media.FormattedText(frames[i].label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), 13, System.Windows.Media.Brushes.White, 1.0), new Point(i * W + 6, 4));
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * frames.Count, H * 2, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "ik.jpg"), 88);
        File.WriteAllText(Path.Combine(folder, "ik.txt"), log.ToString());
    }

    private static void DetectTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        using var det = new PoseStudio.Detection.PoseDetector(Path.Combine(AppContext.BaseDirectory, "Models"));
        log.AppendLine(det.ModelInfo);
        var names = new[] { "Winken", "Laufen", "Sitzen (Stuhl)", "Arme verschränkt", "Hände in den Hüften", "Ausfallschritt" };
        const int W = 360, H = 540;
        var cam = new OrbitCamera { Yaw = 20, Pitch = 5, Distance = 4.6, Target = new System.Windows.Media.Media3D.Point3D(0, 0.9, 0) };
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < names.Length; i++)
            {
                var src = new Mannequin(); src.SetFigure(FigureStyle.Male, true, new Proportions());
                src.SetPose(Presets.All.First(p => p.Name == names[i]).ToPose()); src.GroundToFloor();
                var img = SceneFactory.Render(src, cam, new SceneOptions { Floor = false }, W, H);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var pose = det.Detect(img);
                log.AppendLine($"{names[i]}: {(pose == null ? "keine Person" : $"Konfidenz {pose.Confidence:F2}, Detektor {pose.DetectionScore:F2}")}, {sw.ElapsedMilliseconds} ms");
                dc.DrawImage(img, new Rect(i * W, 0, W, H));
                if (pose == null) continue;
                foreach (var p in pose.Image)
                    dc.DrawEllipse(System.Windows.Media.Brushes.Red, null, new Point(i * W + p.X, p.Y), 3, 3);
                // MediaPipe-Welt → Szene (X rechts = linke Seite der Figur, Y oben, Z zur Kamera), dann Kamerarichtung
                var P = PoseStudio.Detection.PoseDetector.ToScene(pose, cam);
                var fit = new Mannequin(); fit.SetFigure(FigureStyle.Female, true, new Proportions { HeightCm = 168 });
                fit.FitLandmarks(P); fit.GroundToFloor();
                dc.DrawImage(SceneFactory.Render(fit, cam, new SceneOptions { Floor = false, Background = System.Windows.Media.Color.FromRgb(0xD8, 0xE4, 0xF0) }, W, H), new Rect(i * W, H, W, H));
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * names.Length, H * 2, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "detect.jpg"), 88);
        File.WriteAllText(Path.Combine(folder, "detect.txt"), log.ToString());
    }

    private static void SittingTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var names = new[] { "Sitzen (Stuhl)", "Sitzen am Boden (Knie angezogen)", "Schneidersitz", "Hocke" };
        var m = new Mannequin();
        m.SetFigure(FigureStyle.Female, false, new Proportions { HeightCm = 168 });
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < names.Length; i++)
            {
                m.SetPose(Presets.All.First(p => p.Name == names[i]).ToPose()); m.GroundToFloor();
                var pel = m.PelvisWorld;
                for (int v = 0; v < 2; v++)
                {
                    var cam = new OrbitCamera { Yaw = v == 0 ? 40 : 110, Pitch = 12, Distance = 1.9, Target = new System.Windows.Media.Media3D.Point3D(pel.X, pel.Y - 0.05, pel.Z + 0.1) };
                    dc.DrawImage(SceneFactory.RenderControl(new[] { m }, cam, SceneFactory.ControlMode.Pose3D, 400, 400), new Rect(v * 400, i * 400, 400, 400));
                }
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 1600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "sitting.jpg"), 90);
    }

    private static void ControlNetTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var setup = new[]
        {
            (FigureStyle.Female, 11, -0.95, 0.2, 15.0),   // Hände hinter dem Kopf
            (FigureStyle.Female, 35, -0.2, -0.3, 0.0),    // Tanzen
            (FigureStyle.Female, 8, 0.75, -0.6, -20.0),   // Hände in den Hüften
            (FigureStyle.Male, 22, 0.45, 0.5, -25.0),     // am Boden sitzen
        };
        var figs = new List<Mannequin>();
        foreach (var (style, preset, x, z, rot) in setup)
        {
            var m = new Mannequin();
            m.SetFigure(style, true, new Proportions { HeightCm = style == FigureStyle.Female ? 168 : 180 });
            var pose = Presets.All[preset].ToPose();
            pose.Joints["pelvis"] = new[] { pose.Joints.TryGetValue("pelvis", out var pv) ? pv[0] : 0, pv?[1] ?? 0, rot };
            m.SetPose(pose);
            m.RootPosition = new System.Windows.Media.Media3D.Vector3D(x, m.RootPosition.Y, z);
            m.GroundToFloor();
            figs.Add(m);
        }
        var cam = new OrbitCamera { Yaw = 0, Pitch = 6, Distance = 7.6, Target = new System.Windows.Media.Media3D.Point3D(0, 0.85, 0) };
        SceneFactory.SavePng(SceneFactory.RenderControl(figs, cam, SceneFactory.ControlMode.Pose3D, 768, 1152), Path.Combine(folder, "cn_3dpose.png"));
        SceneFactory.SavePng(SceneFactory.RenderControl(figs, cam, SceneFactory.ControlMode.Regions, 768, 1152), Path.Combine(folder, "cn_regions.png"));
        SceneFactory.SaveJpeg(SceneFactory.Render(figs, cam, new SceneOptions(), 768, 1152), Path.Combine(folder, "normal.jpg"), 90);
    }

    private static void PropSheet(string folder, string style)
    {
        Directory.CreateDirectory(folder);
        var m = new Mannequin();
        m.SetFigure(Enum.Parse<FigureStyle>(style), true, new Proportions());
        var types = Enum.GetValues<PropType>().Where(t => t != PropType.None).ToList();
        var holds = new[] { HoldMode.Right, HoldMode.BothRightTop };
        const int cw = 260, ch = 340;
        var opts = new SceneOptions { Floor = true, Shadow = true, Light = new LightSettings { CastShadow = false } };
        var dv = new System.Windows.Media.DrawingVisual();
        var log = new System.Text.StringBuilder();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < types.Count; i++)
                for (int h = 0; h < holds.Length; h++)
                {
                    m.SetPose(Presets.All[0].ToPose());
                    var p = new PropSettings { Type = types[i], Hold = holds[h] };
                    p.ApplyDefaultPlacement();
                    m.SetProp(p);
                    m.GroundToFloor();
                    log.AppendLine($"{types[i]} {holds[h]}: Griff-Fehler {m.LastGripError * 100:F1} cm");
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = 30, Pitch = 8, Target = new System.Windows.Media.Media3D.Point3D(pel.X, 1.0, pel.Z), Distance = 5.6 };
                    double x = (h * 2) * cw, y = i * ch;
                    dc.DrawImage(SceneFactory.Render(m, cam, opts, cw, ch), new Rect(x, y, cw, ch));
                    var hand = m["wrist_r"].World.Transform(new System.Windows.Media.Media3D.Point3D(0, -0.08, 0));
                    var cam2 = new OrbitCamera { Yaw = 50, Pitch = 10, Target = hand, Distance = 0.9 };
                    dc.DrawImage(SceneFactory.Render(m, cam2, opts, cw, ch), new Rect(x + cw, y, cw, ch));
                    dc.DrawText(new System.Windows.Media.FormattedText($"{types[i]} / {holds[h]}", System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI"), 12, System.Windows.Media.Brushes.Black, 1.0), new Point(x + 4, y + 2));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cw * 4, ch * types.Count, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"props_{style}.jpg"), 88);
        File.WriteAllText(Path.Combine(folder, "props.txt"), log.ToString());
    }

    private static void SetSheet(string folder)
    {
        Directory.CreateDirectory(folder);
        var m = new Mannequin();
        m.SetFigure(FigureStyle.Male, true, new Proportions());
        m.SetPose(Presets.All[2].ToPose()); m.GroundToFloor();
        var types = Enum.GetValues<SetType>().Where(t => t != SetType.None).ToList();
        const int cw = 640, ch = 360;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < types.Count; i++)
                for (int v = 0; v < 2; v++)
                {
                    var set = new SetSettings { Type = types[i], Floor = i % 2 == 0 ? "Wood" : "Tiles" };
                    var o = new SceneOptions { Floor = false, Shadow = true, ShadowResolution = 1024, Set = set,
                        Light = new LightSettings { Azimuth = v == 0 ? 50 : -60, Elevation = 40, Softness = 25 } };
                    var cam = new OrbitCamera { Yaw = v == 0 ? 25 : 160, Pitch = 8, Distance = 5.2, Target = new System.Windows.Media.Media3D.Point3D(0, 1.0, 0) };
                    dc.DrawImage(SceneFactory.Render(m, cam, o, cw, ch), new Rect(v * cw, i * ch, cw, ch));
                    dc.DrawText(new System.Windows.Media.FormattedText(types[i].ToString(), System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI"), 14, System.Windows.Media.Brushes.Black, 1.0), new Point(v * cw + 6, i * ch + 4));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cw * 2, ch * types.Count, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "sets.jpg"), 88);
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