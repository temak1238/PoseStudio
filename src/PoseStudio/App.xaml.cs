using P3 = System.Windows.Media.Media3D.Point3D;
using V3 = System.Windows.Media.Media3D.Vector3D;
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

        if (e.Args.Length >= 2 && e.Args[0] == "--uitest-library")
        {
            // Hauptfenster erzeugen, aber nicht anzeigen und nicht schließen (keine Einstellungen schreiben)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var mw = new MainWindow();
            File.WriteAllText(e.Args[1], mw.SelfTestApplyLibrary());
            Environment.Exit(0);
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

        if (e.Args.Length >= 2 && e.Args[0] == "--rtmw3d")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Rtmw3dTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--photos")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PhotosTest(e.Args[1], e.Args[2]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--hands")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            foreach (var st in new[] { FigureStyle.Male, FigureStyle.Female, FigureStyle.Mannequin }) HandsTest(e.Args[1], st);
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

    /// <summary>Testmodus --rtmw3d: gerenderte Figuren mit bekannter Pose erkennen; Winkelfehler der Knochen-/Fingerrichtungen
    /// (Kamera-System) für MediaPipe und RTMW3D, je Einstellung (Tiefen-Vorzeichen, Farbreihenfolge).</summary>
    private static void Rtmw3dTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        using var det = new PoseStudio.Detection.PoseDetector(Path.Combine(AppContext.BaseDirectory, "Models"));
        var names = new[] { "Winken", "Zeigen", "Arme hoch (Jubeln)", "Telefonieren", "Kampfhaltung (Boxen)", "Heldenpose", "Laufen", "Nachdenken (Hand am Kinn)" };
        const int W = 640, H = 960;
        var cases = new List<(System.Windows.Media.Imaging.BitmapSource img, Mannequin m, OrbitCamera cam, string name)>();
        int gi = 0;
        foreach (var name in names)
            foreach (var yaw in new[] { 15.0, -40.0 })
            {
                var m = new Mannequin(); m.SetFigure(FigureStyle.Male, true, new Proportions());
                m.SetPose(Presets.All.First(p => p.Name == name).ToPose());
                var gs = HandGesture.All;
                m.ApplyGesture(1, gs[(gi * 2 + 1) % gs.Count]); m.ApplyGesture(-1, gs[(gi * 2 + 2) % gs.Count]); gi++;
                m.Update(); m.GroundToFloor();
                var cam = new OrbitCamera { Yaw = yaw, Pitch = 5, Distance = 3.6, Target = new P3(0, 0.95, 0) };
                cases.Add((SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false }, W, H), m, cam, name));
            }

        static V3 CamDir(OrbitCamera cam, P3 a, P3 b)
        {
            var d = b - a;
            var v = new V3(V3.DotProduct(d, cam.Right), -V3.DotProduct(d, cam.Up), V3.DotProduct(d, cam.LookDirection));
            v.Normalize(); return v;
        }
        static double Ang(V3 a, V3 b)
        {
            if (a.LengthSquared < 1e-12 || b.LengthSquared < 1e-12) return 90;
            a.Normalize(); b.Normalize();
            return Math.Acos(Math.Clamp(V3.DotProduct(a, b), -1, 1)) * 180 / Math.PI;
        }
        static P3 O(Mannequin m, string id) => m[id].World.Transform(new P3());
        static P3 Tip(Mannequin m, string id) => m[id].World.Transform(new P3(0, -m[id].BoneLength * m[id].UniformScale, 0));

        var body = new (string a, string b, int mpA, int mpB, int wbA, int wbB)[]
        {
            ("shoulder_l", "elbow_l", 11, 13, 5, 7), ("shoulder_r", "elbow_r", 12, 14, 6, 8), ("elbow_l", "wrist_l", 13, 15, 7, 9), ("elbow_r", "wrist_r", 14, 16, 8, 10),
            ("hip_l", "knee_l", 23, 25, 11, 13), ("hip_r", "knee_r", 24, 26, 12, 14), ("knee_l", "ankle_l", 25, 27, 13, 15), ("knee_r", "ankle_r", 26, 28, 14, 16),
        };
        foreach (var (vname, boxes) in new[] { ("RTMW3D mit MediaPipe-Rahmen", PoseStudio.Detection.PoseDetector.BoxSource.MediaPipe), ("RTMW3D mit YOLOX-Rahmen", PoseStudio.Detection.PoseDetector.BoxSource.Yolox) })
        {
            PoseStudio.Detection.PoseDetector.Boxes = boxes;
            double mpErr = 0, wbErr = 0, mpZ = 0, wbZ = 0, handErr = 0, handErr2 = 0, handScore = 0; int nb = 0, nh = 0, nhs = 0;
            var scaleRatios = new List<double>();
            foreach (var (img, m, cam, name) in cases)
            {
                var pose = det.Detect(img);
                if (pose?.Whole == null) { log.AppendLine($"  {name}: nichts erkannt"); continue; }
                var wb = pose.Whole;
                foreach (var (a, b, ma, mb, wa, wbb) in body)
                {
                    var t = CamDir(cam, O(m, a), O(m, b));
                    mpErr += Ang(pose.World[mb] - pose.World[ma], t);
                    wbErr += Ang(wb.World[wbb] - wb.World[wa], t);
                    mpZ += Math.Sign((pose.World[mb] - pose.World[ma]).Z) == Math.Sign(t.Z) ? 1 : 0;
                    wbZ += Math.Sign((wb.World[wbb] - wb.World[wa]).Z) == Math.Sign(t.Z) ? 1 : 0;
                    nb++;
                    scaleRatios.Add((wb.World[wbb] - wb.World[wa]).Length / (O(m, b) - O(m, a)).Length);
                }
                foreach (var (sfx, off) in new[] { ("_l", 91), ("_r", 112) })
                {
                    handScore += wb.HandScore(sfx == "_l"); nhs++;
                    for (int f = 0; f < 5; f++)
                    {
                        var id = HandGeometry.Ids[f];
                        var pts = new[] { O(m, id + "1" + sfx), O(m, id + "2" + sfx), O(m, id + "3" + sfx), Tip(m, id + "3" + sfx) };
                        for (int k = 0; k < 3; k++)
                        {
                            int i0 = off + 1 + f * 4 + k;
                            var dd = wb.World[i0 + 1] - wb.World[i0]; var tt = CamDir(cam, pts[k], pts[k + 1]);
                            handErr += Ang(dd, tt);
                            handErr2 += Ang(new V3(dd.X, dd.Y, 0), new V3(tt.X, tt.Y, 0));
                            nh++;
                        }
                    }
                }
            }
            scaleRatios.Sort();
            log.AppendLine($"{vname}: Körper MediaPipe {mpErr / nb:F1}° (Tiefe richtig {mpZ / nb:P0}), RTMW3D {wbErr / nb:F1}° (Tiefe richtig {wbZ / nb:P0}), " +
                           $"Längen RTMW3D/echt Median {scaleRatios[scaleRatios.Count / 2]:F2}; Finger 3D {handErr / Math.Max(1, nh):F1}°, 2D {handErr2 / Math.Max(1, nh):F1}°, Hand-Sicherheit {handScore / Math.Max(1, nhs):F2}");
        }
        PoseStudio.Detection.PoseDetector.Boxes = PoseStudio.Detection.PoseDetector.BoxSource.Yolox;

        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < 4; i++)
            {
                var img = cases[i].img;
                dc.DrawImage(img, new Rect(i * W, 0, W, H));
                var pose = det.Detect(img);
                if (pose?.Whole == null) continue;
                for (int k = 0; k < pose.Whole.Image.Length; k++)
                {
                    if (k >= 23 && k <= 90) continue;
                    var p = pose.Whole.Image[k];
                    var br = k >= 91 ? System.Windows.Media.Brushes.Blue : System.Windows.Media.Brushes.Red;
                    dc.DrawEllipse(br, null, new Point(i * W + p.X, p.Y), k >= 91 ? 2 : 4, k >= 91 ? 2 : 4);
                }
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * 4, H, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "rtmw3d.jpg"), 88);
        File.WriteAllText(Path.Combine(folder, "rtmw3d.txt"), det.ModelInfo + "\n" + log);
    }

    /// <summary>Testmodus --photos: Beispielfotos mit MediaPipe und RTMW3D (YOLOX-Rahmen) erkennen; je Foto eine Zeile:
    /// Foto + MediaPipe-Skelett, Foto + RTMW3D-Punkte, Figur aus MediaPipe, Figur aus RTMW3D mit Fingern, Hände nah.</summary>
    private static void PhotosTest(string inDir, string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        using var det = new PoseStudio.Detection.PoseDetector(Path.Combine(AppContext.BaseDirectory, "Models"));
        PoseStudio.Detection.PoseDetector.Boxes = PoseStudio.Detection.PoseDetector.BoxSource.Yolox;
        var files = Directory.GetFiles(inDir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
        const int H = 720, PW = 480, HW = 360;
        int rowW = PW * 4 + HW * 2;
        var dv = new System.Windows.Media.DrawingVisual();
        var cam = new OrbitCamera { Yaw = 0, Pitch = 0, Distance = 3.3, Target = new P3(0, 0.9, 0) };
        var mpPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.LimeGreen, 3);
        var wbPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.OrangeRed, 3);
        var hPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.DeepSkyBlue, 1.6);
        var body17 = new (int, int)[] { (5, 6), (5, 7), (7, 9), (6, 8), (8, 10), (5, 11), (6, 12), (11, 12), (11, 13), (13, 15), (12, 14), (14, 16), (15, 17), (15, 19), (16, 20), (16, 22), (0, 3), (0, 4) };
        void Label(System.Windows.Media.DrawingContext dc, string t, double x, double y) =>
            dc.DrawText(new System.Windows.Media.FormattedText(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe UI Semibold"), 15, System.Windows.Media.Brushes.Black, 1.0), new Point(x + 6, y + 4));
        using (var dc = dv.RenderOpen())
            for (int row = 0; row < files.Count; row++)
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(files[row]); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                double y0 = row * H;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var pose = det.Detect(bi);
                long ms = sw.ElapsedMilliseconds;
                sw.Restart();
                var all = det.DetectAll(bi);
                long msAll = sw.ElapsedMilliseconds;
                double sc = Math.Min(PW / (double)bi.PixelWidth, H / (double)bi.PixelHeight);
                double ox = (PW - bi.PixelWidth * sc) / 2;
                Point M(Point p, int panel) => new(panel * PW + ox + p.X * sc, y0 + p.Y * sc);
                for (int panel = 0; panel < 2; panel++)
                    dc.DrawImage(bi, new Rect(panel * PW + ox, y0, bi.PixelWidth * sc, bi.PixelHeight * sc));
                var name = Path.GetFileName(files[row]);
                if (pose == null) { log.AppendLine($"{name}: keine Person"); continue; }
                foreach (var (a, b) in PoseStudio.Detection.PoseDetector.Bones) dc.DrawLine(mpPen, M(pose.Image[a], 0), M(pose.Image[b], 0));
                Label(dc, "MediaPipe", 0, y0);
                var wb = pose.Whole;
                if (wb != null)
                {
                    foreach (var (a, b) in body17)
                        if (wb.Score[a] > 0.3 && wb.Score[b] > 0.3) dc.DrawLine(wbPen, M(wb.Image[a], 1), M(wb.Image[b], 1));
                    foreach (var off in new[] { 91, 112 })
                        for (int f = 0; f < 5; f++)
                            for (int k = 0; k < 4; k++)
                            {
                                int a = k == 0 ? off : off + 1 + f * 4 + k - 1, b = off + 1 + f * 4 + k;
                                dc.DrawLine(hPen, M(wb.Image[a], 1), M(wb.Image[b], 1));
                            }
                    Label(dc, $"RTMW3D ({all.Count} Person{(all.Count == 1 ? "" : "en")})", PW, y0);
                }
                // Figur aus MediaPipe (nur Körper)
                var fMp = new Mannequin(); fMp.SetFigure(FigureStyle.Female, true, new Proportions { HeightCm = 165 });
                fMp.FitLandmarks(PoseStudio.Detection.PoseDetector.ToScene(pose, cam)); fMp.GroundToFloor();
                var opts = new SceneOptions { Floor = true, Shadow = false };
                dc.DrawImage(SceneFactory.Render(fMp, cam, opts, PW, H), new Rect(2 * PW, y0, PW, H));
                Label(dc, "Figur aus MediaPipe", 2 * PW, y0);
                if (wb == null) continue;
                // Figur aus RTMW3D mit Fingern
                var fWb = new Mannequin(); fWb.SetFigure(FigureStyle.Female, true, new Proportions { HeightCm = 165 });
                fWb.FitLandmarks(PoseStudio.Detection.PoseDetector.ToScene(wb.ToMediaPipe33(), cam));
                var sceneAll = PoseStudio.Detection.PoseDetector.ToScene(wb.World, cam);
                int nl = fWb.FitHand(1, sceneAll.Skip(91).Take(21).ToArray(), wb.Score.Skip(91).Take(21).ToArray());
                int nr = fWb.FitHand(-1, sceneAll.Skip(112).Take(21).ToArray(), wb.Score.Skip(112).Take(21).ToArray());
                fWb.GroundToFloor();
                dc.DrawImage(SceneFactory.Render(fWb, cam, opts, PW, H), new Rect(3 * PW, y0, PW, H));
                Label(dc, "Figur aus RTMW3D + Finger", 3 * PW, y0);
                // Hände nah (Kamera wie im Foto)
                int i = 0;
                foreach (var sfx in new[] { "_r", "_l" })
                {
                    var c = fWb["wrist" + sfx].World.Transform(new P3(0, -0.07, 0));
                    var hc = new OrbitCamera { Yaw = 0, Pitch = 0, Distance = 0.7, Target = c };
                    dc.DrawImage(SceneFactory.Render(fWb, hc, new SceneOptions { Floor = false, Shadow = false }, HW, H / 2), new Rect(4 * PW + i * HW, y0, HW, H / 2));
                    Label(dc, sfx == "_r" ? "rechte Hand" : "linke Hand", 4 * PW + i * HW, y0);
                    // Ausschnitt aus dem Foto um dieselbe Hand
                    int off = sfx == "_l" ? 91 : 112;
                    var hp = Enumerable.Range(off, 21).Select(k => wb.Image[k]).ToList();
                    double cx = hp.Average(p => p.X), cy = hp.Average(p => p.Y);
                    double size = Math.Max(hp.Max(p => p.X) - hp.Min(p => p.X), hp.Max(p => p.Y) - hp.Min(p => p.Y)) * 1.8 + 20;
                    double s2 = Math.Min(HW, H / 2) / size;
                    dc.PushClip(new System.Windows.Media.RectangleGeometry(new Rect(4 * PW + i * HW, y0 + H / 2, HW, H / 2)));
                    double bx = 4 * PW + i * HW + HW / 2 - cx * s2, by = y0 + H * 0.75 - cy * s2;
                    dc.DrawImage(bi, new Rect(bx, by, bi.PixelWidth * s2, bi.PixelHeight * s2));
                    for (int f = 0; f < 5; f++)
                        for (int k = 0; k < 4; k++)
                        {
                            int a = k == 0 ? off : off + 1 + f * 4 + k - 1, b = off + 1 + f * 4 + k;
                            dc.DrawLine(hPen, new Point(bx + wb.Image[a].X * s2, by + wb.Image[a].Y * s2), new Point(bx + wb.Image[b].X * s2, by + wb.Image[b].Y * s2));
                        }
                    dc.Pop();
                    i++;
                }
                log.AppendLine($"{name}: MediaPipe {pose.Confidence:F2}, RTMW3D Körper {Enumerable.Range(0, 17).Average(k => wb.Score[k]):F2}, " +
                               $"Hand links {wb.HandScore(true):F2} ({nl} Finger), rechts {wb.HandScore(false):F2} ({nr} Finger), " +
                               $"Personen (YOLOX) {all.Count}; Zeit {ms} ms (alle: {msAll} ms)");
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(rowW, H * Math.Max(1, files.Count), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "photos.jpg"), 88);
        File.WriteAllText(Path.Combine(folder, "photos.txt"), det.ModelInfo + "\n" + log);
    }

    /// <summary>Testmodus --hands: alle Handhaltungen als Nahaufnahme, Griff um ein Objekt, Prüfwerte in hands.txt.</summary>
    private static void HandsTest(string folder, FigureStyle style)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        var m = new Mannequin();
        m.SetFigure(style, true, new Proportions { HeightCm = 178 });
        var basePose = new PoseData();
        basePose.Joints["shoulder_l"] = new double[] { 70, 20, 0 };
        basePose.Joints["elbow_l"] = new double[] { 30, 0, 0 };
        basePose.Joints["shoulder_r"] = new double[] { 70, 20, 0 };
        basePose.Joints["elbow_r"] = new double[] { 30, 0, 0 };
        var opts = new SceneOptions { Floor = false, Shadow = false };
        var gestures = HandGesture.All;
        const int W = 300;
        int cols = 4, rows = (gestures.Count + 1) / 2 + 1;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int i = 0; i < gestures.Count; i++)
            {
                m.SetPose(basePose);
                m.ApplyGesture(1, gestures[i]);
                m.ApplyGesture(-1, gestures[i]);
                m.Update();
                var w = m["wrist_l"].World;
                var c = w.Transform(new System.Windows.Media.Media3D.Point3D(0, -0.07, 0));
                for (int v = 0; v < 2; v++)
                {
                    var cam = new OrbitCamera { Yaw = v == 0 ? -60 : 120, Pitch = 15, Distance = 0.55, Target = c };
                    var rect = new Rect(((i % 2) * 2 + v) * W, (i / 2) * W, W, W);
                    dc.DrawImage(SceneFactory.Render(m, cam, opts, W, W), rect);
                }
                dc.DrawText(new System.Windows.Media.FormattedText(gestures[i].Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), 16, System.Windows.Media.Brushes.Black, 1.0), new Point((i % 2) * 2 * W + 6, (i / 2) * W + 4));
                // Spiegelgleichheit links/rechts: Fingerspitzen relativ zum Handgelenk
                double asym = 0;
                foreach (var f in new[] { "thumb3", "index3", "pinky3" })
                {
                    var pl = m[f + "_l"].World.Transform(new System.Windows.Media.Media3D.Point3D()) - m["wrist_l"].World.Transform(new System.Windows.Media.Media3D.Point3D());
                    var pr = m[f + "_r"].World.Transform(new System.Windows.Media.Media3D.Point3D()) - m["wrist_r"].World.Transform(new System.Windows.Media.Media3D.Point3D());
                    asym = Math.Max(asym, Math.Abs(pl.Length - pr.Length));
                }
                log.AppendLine($"{gestures[i].Name}: Asymmetrie {asym * 1000:F2} mm");
            }
            // Griff um ein Schwert (rechte Hand) und Stab beidhändig
            int gi = gestures.Count;
            foreach (var (type, hold, k) in new[] { (PropType.Sword, HoldMode.Right, 0), (PropType.Staff, HoldMode.BothRightTop, 1) })
            {
                m.SetPose(basePose);
                var p = new PropSettings { Type = type, Hold = hold };
                p.ApplyDefaultPlacement();
                m.SetProp(p);
                m.Update();
                var c = m["wrist_r"].World.Transform(new System.Windows.Media.Media3D.Point3D(0, -0.07, 0));
                for (int v = 0; v < 2; v++)
                {
                    var cam = new OrbitCamera { Yaw = v == 0 ? 60 : -120, Pitch = 15, Distance = 0.6, Target = c };
                    dc.DrawImage(SceneFactory.Render(m, cam, opts, W, W), new Rect((k * 2 + v) * W, ((gi + 1) / 2) * W, W, W));
                }
                log.AppendLine($"Griff {type}: Abstand Hand–Griff {m.LastGripError * 1000:F1} mm, Zeigefinger-Beugen {m["index1_r"].SideValue:F0}/{m["index2_r"].SideValue:F0}/{m["index3_r"].SideValue:F0}, Daumen {m["thumb1_r"].BendValue:F0}/{m["thumb1_r"].SideValue:F0}/{m["thumb2_r"].SideValue:F0}/{m["thumb3_r"].SideValue:F0}");
            }
            m.SetProp(new PropSettings());
            log.AppendLine($"Nach Loslassen: Zeigefinger {m["index2_r"].SideValue:F0}");
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cols * W, rows * W, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"hands-{style}.jpg"), 90);

        // Daumen abspreizen: Spitze soll sich vom Zeigefinger-Grundgelenk entfernen
        m.SetPose(basePose);
        double Dist() => (m["thumb3_l"].World.Transform(new System.Windows.Media.Media3D.Point3D()) - m["index1_l"].World.Transform(new System.Windows.Media.Media3D.Point3D())).Length;
        double d0 = Dist();
        m.SetThumbSpread(1, 60); m.Update();
        log.AppendLine($"Daumen abspreizen: Abstand Daumen–Zeigefinger {d0 * 1000:F0} → {Dist() * 1000:F0} mm");
        // Spiegeln
        m.SetPose(basePose); m.ApplyGesture(1, gestures.First(g => g.Name == "Zeigen")); m.Mirror();
        log.AppendLine($"Spiegeln: rechter Zeigefinger {m["index2_r"].SideValue:F0}, Mittelfinger {m["middle2_r"].SideValue:F0}, linker Mittelfinger {m["middle2_l"].SideValue:F0}");
        File.AppendAllText(Path.Combine(folder, "hands.txt"), $"== {style}\n" + log);
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