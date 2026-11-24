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

        if (e.Args.Length >= 2 && e.Args[0] == "--uitest-hair")
        {
            // Fehler in die Datei schreiben statt als Meldungsfenster (sonst hängt der unsichtbare Test)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var mw = new MainWindow(); File.WriteAllText(e.Args[1], mw.SelfTestHair()); }
            catch (Exception ex) { File.WriteAllText(e.Args[1], "FEHLER: " + ex); }
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

        if (e.Args.Length >= 5 && e.Args[0] == "--render-control")
        {
            // --render-control <datei.png> <breite> <höhe> [Pose]: 3D-Pose-Bild einer Anime-Figur (für Tests)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var rm = new Mannequin(); rm.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
            rm.SetPose(Presets.All.First(p => p.Name == (e.Args.Length > 5 ? e.Args[5] : "Sprung – Beine gespreizt")).ToPose()); rm.GroundToFloor();
            int rw = int.Parse(e.Args[2]), rh = int.Parse(e.Args[3]);
            var rp = rm.PelvisWorld;
            var rc = new OrbitCamera { Yaw = 10, Pitch = 5, Distance = 3.6, Target = new P3(rp.X, (rm.ComputeMinY() + rm.ComputeMaxY()) / 2, rp.Z) };
            SceneFactory.SavePng(SceneFactory.RenderControl(new[] { rm }, rc, SceneFactory.ControlMode.Pose3D, rw, rh), e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--swarm-cn")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () => { await SwarmCnTest(e.Args[1], e.Args[2], e.Args.Length > 3 ? e.Args[3] : "Sprung – Beine gespreizt", e.Args.Length > 4 ? double.Parse(e.Args[4], System.Globalization.CultureInfo.InvariantCulture) : 1.0); Shutdown(); });
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--swarm-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () => { await SwarmTest(e.Args[1], e.Args[2]); Shutdown(); });
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--bust")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            BustTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--shape")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ShapeTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--look")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LookTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--perf")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PerfTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--seat")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SeatTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--clothes")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ClothesTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--hair")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            HairTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--neck")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            NeckTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--anime")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AnimeTest(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--pelvis")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PelvisTest(e.Args[1]);
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

        if (e.Args.Length >= 2 && e.Args[0] == "--pose-review")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            PoseReview(e.Args[1], e.Args.Length >= 3 ? int.Parse(e.Args[2]) : 0);
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

        if (e.Args.Length >= 3 && e.Args[0] == "--theme-shot")
        {
            // --theme-shot <ordner> <Hell|Dunkel|Klassisch>: Fenster im gewählten Design als PNG (außerhalb des Bildschirms, ohne Einstellungen zu schreiben)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Theme.Apply(e.Args[2]); Theme.Locked = true;
            var mw = new MainWindow();
            Dispatcher.InvokeAsync(async () =>
            {
                try { await mw.ThemeShotAsync(e.Args[1], e.Args[2]); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[1], "FEHLER.txt"), ex.ToString()); }
                Environment.Exit(0);   // ohne Closing-Handler, damit nichts gespeichert wird
            });
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--folder-test")
        {
            // --folder-test <ausgabe.txt>: Zielordner je PC, Übernahme der Einträge anderer PCs beim Speichern (nur Temp-Datei)
            string tmpDir = Path.Combine(Path.GetTempPath(), "PoseStudioFolderTest");
            Directory.CreateDirectory(tmpDir);
            AppSettings.FileOverride = Path.Combine(tmpDir, "settings.json");
            File.Delete(AppSettings.FileOverride);
            var log = new System.Text.StringBuilder();
            string me = Environment.MachineName;
            // alte Datei ohne Einträge, Ordner gibt es hier nicht → Standard
            File.WriteAllText(AppSettings.FileOverride, System.Text.Json.JsonSerializer.Serialize(new { OutputFolder = @"Q:\gibtsnicht", FilePrefix = "alt" }));
            var a = AppSettings.Load();
            log.AppendLine($"fremder Ordner → {a.OutputFolderHere()} ok={a.OutputFolderHere() == AppSettings.DefaultOutputFolder && a.FilePrefix == "alt"}");
            // alte Datei, Ordner existiert → übernehmen
            File.WriteAllText(AppSettings.FileOverride, System.Text.Json.JsonSerializer.Serialize(new { OutputFolder = tmpDir }));
            a = AppSettings.Load();
            log.AppendLine($"vorhandener Ordner ok={a.OutputFolderHere() == tmpDir}");
            // dieser PC speichert, ein anderer PC hat inzwischen seinen Eintrag in die Datei geschrieben
            a.SetOutputFolderHere(@"C:\Hier");
            var b = AppSettings.Load(); b.OutputFolders["ANDERER-PC"] = @"E:\Dort"; b.Save();
            a.Save();
            var c = AppSettings.Load();
            log.AppendLine($"nach Speichern: hier={c.OutputFolderHere()} anderer={c.OutputFolders.GetValueOrDefault("anderer-pc")} ok={c.OutputFolderHere() == @"C:\Hier" && c.OutputFolders.GetValueOrDefault("ANDERER-PC") == @"E:\Dort"}");
            // Eintrag eines anderen PCs bestimmt hier nichts
            c.OutputFolders.Remove(me); c.OutputFolder = @"E:\Dort";
            log.AppendLine($"nur anderer PC eingetragen ok={c.OutputFolderHere() == AppSettings.DefaultOutputFolder || Directory.Exists(@"E:\Dort")}");
            Directory.Delete(tmpDir, true);
            File.WriteAllText(e.Args[1], log.ToString());
            Environment.Exit(0);
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--paths-test")
        {
            // --paths-test <ausgabe.txt>: Ablageort der Daten (normal / portabel) und was davon geladen wird
            var st = AppSettings.Load();
            File.WriteAllText(e.Args[1], $"Portabel {AppPaths.IsPortable}\nEinstellungen {AppPaths.SettingsFile}\nVorlagen {AppPaths.TemplatesDir}\nCache {AppPaths.CacheDir}\nGeladen: Ausgabeordner {st.OutputFolder}, Prefix {st.FilePrefix}");
            Environment.Exit(0);
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--segment-persist-test")
        {
            // --segment-persist-test <swarm-url> <ausgabe.txt>: Refiner-LoRAs über Schließen/Speichern/Laden prüfen (ohne %APPDATA% zu schreiben)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () =>
            {
                string r;
                try { var mw = new MainWindow(); r = await Swarm.SwarmWindow.TestSegmentPersistenceAsync(mw, e.Args[1]); }
                catch (Exception ex) { r = "FEHLER: " + ex; }
                File.WriteAllText(e.Args[2], r);
                Environment.Exit(0);
            });
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--animadex-test")
        {
            // --animadex-test <adresse> <ausgabe.txt>: AnimaDex-Anbindung gegen eine laufende Instanz prüfen
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () =>
            {
                string r;
                try
                {
                    r = await Swarm.AnimaDexWindow.SelfTestAsync(e.Args[1]);
                    if (e.Args.Length >= 4)
                    {
                        // optional: Fenster außerhalb des Bildschirms anzeigen und als PNG speichern (4. Argument), Design im 5.
                        if (e.Args.Length >= 5) Theme.Apply(e.Args[4]);
                        var dex = new Swarm.AnimaDexWindow(new Swarm.SwarmSettings { AnimaDexUrl = e.Args[1] }, (_, _) => { }, false)
                        { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
                        dex.Show(); dex.Left = -20000;
                        await System.Threading.Tasks.Task.Delay(6000);
                        PoseStudio.MainWindow.SaveWindowShot(dex, e.Args[3]);
                    }
                }
                catch (Exception ex) { r = "FEHLER: " + ex; }
                File.WriteAllText(e.Args[2], r);
                Shutdown();
            });
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--update-test")
        {
            // --update-test <testordner> <ausgabe.txt>: neuestes Release laden und in den Testordner einspielen (nicht in den Programmordner)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () =>
            {
                var log = new System.Text.StringBuilder();
                try { log.Append(await Update.UpdateSelfTest.RunAsync(e.Args[1])); }
                catch (Exception ex) { log.AppendLine("FEHLER: " + ex); }
                File.WriteAllText(e.Args[2], log.ToString());
                Shutdown();
            });
            return;
        }

        NormalStart = e.Args.Length == 0;
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    /// <summary>Gewöhnlicher Start ohne Testmodus – nur dann wird nach Updates gesucht.</summary>
    public static bool NormalStart { get; private set; }

    /// <summary>Alle Pose-Vorlagen als Kontaktbogen (je Vorlage 3/4-Ansicht und Seite).</summary>
    /// <summary>Testmodus --pose-review &lt;Ordner&gt; [ab Index]: Vorlagen groß, je vorne/schräg/seitlich, 4 Vorlagen pro Zeile, 16 pro Blatt.</summary>
    private static void PoseReview(string folder, int start)
    {
        Directory.CreateDirectory(folder);
        var m = new Mannequin();
        m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
        m.SetHair(new HairSettings { Style = "kurz", Wind = 0 });
        var opts = new SceneOptions { Floor = true, Shadow = true, Light = new LightSettings { CastShadow = false } };
        const int cw = 190, ch = 260, perRow = 4, perSheet = 16;
        var yaws = new[] { 0.0, 40.0, 90.0 };
        var tf = new System.Windows.Media.Typeface("Segoe UI");
        for (int sheet = 0; start + sheet * perSheet < Presets.All.Count; sheet++)
        {
            int first = start + sheet * perSheet, count = Math.Min(perSheet, Presets.All.Count - first);
            int rows = (count + perRow - 1) / perRow, W = cw * yaws.Length * perRow, H = ch * rows;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, W, H));
                for (int k = 0; k < count; k++)
                {
                    int i = first + k;
                    m.SetPose(Presets.All[i].ToPose()); m.GroundToFloor();
                    { var sh = m["shoulder_r"].World.Transform(new System.Windows.Media.Media3D.Point3D()); var hd = m["wrist_r"].World.Transform(new System.Windows.Media.Media3D.Point3D()); var pw = m.Root.World; pw.Invert(); var d = pw.Transform(hd - sh); d.Normalize(); File.AppendAllText(Path.Combine(folder, "arms.txt"), FormattableString.Invariant($"{i} {Presets.All[i].Name}: rechte Hand rel. Schulter (Becken-System x=links, y=oben, z=vorne) {d.X:F2} {d.Y:F2} {d.Z:F2}") + Environment.NewLine); }
                    double minY = m.ComputeMinY(), maxY = m.ComputeMaxY();
                    var pel = m.PelvisWorld;
                    for (int v = 0; v < yaws.Length; v++)
                    {
                        var cam = new OrbitCamera { Yaw = yaws[v], Pitch = 8, Target = new System.Windows.Media.Media3D.Point3D(pel.X, (minY + maxY) / 2, pel.Z), Distance = 4.2 };
                        double x = ((k % perRow) * yaws.Length + v) * cw, y = (k / perRow) * ch;
                        dc.DrawImage(SceneFactory.Render(m, cam, opts, cw, ch), new Rect(x, y, cw, ch));
                        if (v == 0)
                            dc.DrawText(new System.Windows.Media.FormattedText($"{i}: {Presets.All[i].Name}", System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight, tf, 12, System.Windows.Media.Brushes.Black, 1.0), new Point(x + 3, y + 2));
                    }
                }
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W, H, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"review_{first:D3}.jpg"), 88);
        }
    }

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
                var kept = fMp.FitLandmarks(PoseStudio.Detection.PoseDetector.ToScene(pose, cam), PoseStudio.Detection.PoseDetector.Visible33(pose, bi.PixelWidth, bi.PixelHeight, false));
                fMp.GroundToFloor();
                var opts = new SceneOptions { Floor = true, Shadow = false };
                // Kamera an das Foto anpassen, Figur halbtransparent über dem Foto
                var cfit = new OrbitCamera { Yaw = cam.Yaw, Pitch = cam.Pitch, Distance = cam.Distance, Target = cam.Target, FocalLength = cam.FocalLength };
                var camErr = CameraFit.Fit(cfit, PW, H, CameraFit.Pairs(fMp, pose, bi.PixelWidth, bi.PixelHeight, PW, H));
                dc.DrawImage(bi, new Rect(2 * PW + ox, y0, bi.PixelWidth * sc, bi.PixelHeight * sc));
                dc.PushOpacity(0.6);
                dc.DrawImage(SceneFactory.Render(fMp, cfit, new SceneOptions { Floor = false, Shadow = false, Transparent = true }, PW, H), new Rect(2 * PW, y0, PW, H));
                dc.Pop();
                Label(dc, $"MediaPipe, Kamera angepasst ({camErr:F0} px)", 2 * PW, y0);
                log.AppendLine($"{name}: beibehalten: {string.Join(", ", kept)}; Kamera-Abweichung {camErr:F1} px");
                var wbi = new[] { 0, 0, 0, 0, 0, 0, 0, 3, 4, 0, 0, 5, 6, 7, 8, 9, 10, 0, 0, 0, 0, 0, 0, 11, 12, 13, 14, 15, 16, 19, 22, 17, 20 };
                foreach (var k in new[] { 0, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 })
                    log.AppendLine($"   {k}: MP vis {pose.Visibility[k]:F2} @ {pose.Image[k].X:F0},{pose.Image[k].Y:F0}" +
                                   (wb != null ? $" | RTM {wb.Score[wbi[k]]:F2} @ {wb.Image[wbi[k]].X:F0},{wb.Image[wbi[k]].Y:F0}" : ""));
                if (wb == null) continue;
                // Figur aus RTMW3D mit Fingern
                var fWb = new Mannequin(); fWb.SetFigure(FigureStyle.Female, true, new Proportions { HeightCm = 165 });
                fWb.FitLandmarks(PoseStudio.Detection.PoseDetector.ToScene(wb.ToMediaPipe33(), cam), PoseStudio.Detection.PoseDetector.Visible33(pose, bi.PixelWidth, bi.PixelHeight, true));
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

    /// <summary>Testmodus --pelvis: Beckenbereich nah (vorne, schräg, seitlich, hinten) in mehreren Posen, je Figurenstil.</summary>
    /// <summary>Testmodus --anime: Anime-Figuren (normal und Cel-Shading), ganze Figur und Gesicht.</summary>
    private static void AnimeTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var poses = new[] { "Neutral stehen (A-Pose)", "Gehen", "Sitzen (Stuhl)" };
        const int W = 300;
        foreach (var style in new[] { FigureStyle.AnimeFemale, FigureStyle.AnimeMale, FigureStyle.Female })
        {
            var cols = new (double yaw, double pitch, bool face, bool toon)[]
            {
                (0, 5, false, false), (35, 5, false, true), (90, 5, false, true), (0, 0, true, false), (0, 0, true, true), (35, 5, true, true)
            };
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
                for (int i = 0; i < poses.Length; i++)
                {
                    var m = new Mannequin(); m.SetFigure(style, true, new Proportions { HeightCm = style.IsFemale() ? 160 : 175 });
                    m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                    m.HairColor = System.Windows.Media.Color.FromRgb(0x5A, 0x3A, 0x2A);
                    m.SetPose(Presets.All.First(p => p.Name == poses[i]).ToPose()); m.GroundToFloor();
                    double top = m.ComputeMaxY(), bottom = m.ComputeMinY();
                    var head = m.NoseWorld + new V3(0, 0.04, -0.04);
                    for (int v = 0; v < cols.Length; v++)
                    {
                        var c = cols[v];
                        var cam = c.face
                            ? new OrbitCamera { Yaw = c.yaw, Pitch = c.pitch, Distance = 0.75, Target = head }
                            : new OrbitCamera { Yaw = c.yaw, Pitch = c.pitch, Distance = 3.7, Target = new P3(m.PelvisWorld.X, (top + bottom) / 2, m.PelvisWorld.Z) };
                        var light = new LightSettings { Toon = c.toon };
                        dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = !c.face, Light = light, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                            new Rect(v * W, i * W, W, W));
                    }
                    if (i == 0)
                    {
                        // Kopfhöhen zählen: Körperhöhe / Kopfhöhe (Kinn bis Scheitel ohne Haare)
                        m.SetPose(new PoseData()); m.GroundToFloor();
                        var hj = m["head"].World;
                        double chin = hj.Transform(new P3(0, 0.03, 0.05)).Y, crown = hj.Transform(new P3(0, 0.245, 0)).Y;
                        File.AppendAllText(Path.Combine(folder, "anime.txt"), $"{style}: Höhe {m.ComputeMaxY() - m.ComputeMinY():F3} m, Kopf {crown - chin:F3} m, Kopfhöhen {(m.ComputeMaxY() - m.ComputeMinY()) / (crown - chin):F2}\n");
                    }
                }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * cols.Length, W * poses.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"anime-{style}.jpg"), 90);
        }
    }

    /// <summary>Testmodus --neck: Kopf/Hals/Schultern der Anime-Figuren aus mehreren Richtungen (Cel-Shading).</summary>
    private static void NeckTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 300;
        var views = new (double yaw, double pitch)[] { (0, 0), (35, 0), (90, 0), (150, 0), (0, 25), (0, -20) };
        var styles = new[] { FigureStyle.AnimeFemale, FigureStyle.AnimeMale, FigureStyle.Female };
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int si = 0; si < styles.Length; si++)
            {
                var m = new Mannequin(); m.SetFigure(styles[si], true, new Proportions { HeightCm = styles[si].IsFemale() ? 160 : 175 });
                m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                m.HairColor = System.Windows.Media.Color.FromRgb(0x5A, 0x3A, 0x2A);
                m.SetPose(Presets.All.First(p => p.Name == "Neutral stehen (A-Pose)").ToPose()); m.GroundToFloor();
                var t = m["neck"].World.Transform(new P3(0, 0.02, 0));
                for (int v = 0; v < views.Length; v++)
                {
                    var cam = new OrbitCamera { Yaw = views[v].yaw, Pitch = views[v].pitch, Distance = 1.0, Target = t };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                        new Rect(v * W, si * W, W, W));
                }
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * views.Length, W * styles.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "neck.jpg"), 90);
        // Zweites Blatt: Kopfbewegungen (Hals + Kopf gemeinsam) – vorne, schräg, seitlich
        var moves = new (string name, double nb, double ns, double nt, double hb, double hs, double ht)[]
        {
            ("gerade", 0, 0, 0, 0, 0, 0), ("Kopf drehen", 0, 0, 25, 0, 0, 25), ("nach unten", 15, 0, 0, 20, 0, 0),
            ("nach oben", -20, 0, 0, -25, 0, 0), ("zur Seite neigen", 0, 12, 0, 0, 15, 0),
        };
        var yaws = new[] { 0.0, 40.0, 90.0 };
        var dv2 = new System.Windows.Media.DrawingVisual();
        using (var dc = dv2.RenderOpen())
            for (int r = 0; r < moves.Length; r++)
            {
                var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeMale, true, new Proportions { HeightCm = 175 });
                m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                var pose = Presets.All.First(p => p.Name == "Neutral stehen (A-Pose)").ToPose();
                var mv = moves[r];
                pose.Joints["neck"] = new[] { mv.nb, mv.ns, mv.nt }; pose.Joints["head"] = new[] { mv.hb, mv.hs, mv.ht };
                m.SetPose(pose); m.GroundToFloor();
                var t = m["neck"].World.Transform(new P3(0, 0.02, 0));
                for (int v = 0; v < yaws.Length; v++)
                {
                    var cam = new OrbitCamera { Yaw = yaws[v], Pitch = 0, Distance = 1.0, Target = t };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                        new Rect(v * W, r * W, W, W));
                }
            }
        var rtb2 = new System.Windows.Media.Imaging.RenderTargetBitmap(W * yaws.Length, W * moves.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb2.Render(dv2);
        SceneFactory.SaveJpeg(rtb2, Path.Combine(folder, "neck-moves.jpg"), 90);
    }

    /// <summary>Testmodus --hair: alle Frisuren (Anime weiblich, Cel-Shading) bei Windstille, Brise, starkem Wind und Schwung.</summary>
    private static void HairTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 260;
        var cols = new (double yaw, double wind, double dir, double lift, string pose)[]
        {
            (0, 0, 0, 0, "Neutral stehen (A-Pose)"), (35, 25, 30, 0, "Neutral stehen (A-Pose)"), (90, 70, 0, 0, "Gehen"),
            (0, 70, 90, 0, "Neutral stehen (A-Pose)"), (160, 25, 30, 0, "Neutral stehen (A-Pose)"), (20, 0, 0, 80, "Neutral stehen (A-Pose)"),
        };
        foreach (var bangs in new[] { "gerade", "seitlich", "scheitel", "offen" })
        {
            var styles = bangs == "gerade" ? HairStyleDef.All.Select(s => s.Id).ToArray() : new[] { "schulter", "lang" };
            var sw = System.Diagnostics.Stopwatch.StartNew(); int updates = 0;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
                for (int r = 0; r < styles.Length; r++)
                    for (int c = 0; c < cols.Length; c++)
                    {
                        var col = cols[c];
                        var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                        m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                        m.HairColor = System.Windows.Media.Color.FromRgb(0x6A, 0x4A, 0x36);
                        m.SetPose(Presets.All.First(p => p.Name == col.pose).ToPose());
                        m.SetHair(new HairSettings { Style = styles[r], Bangs = bangs, Wind = col.wind, WindDirection = col.dir, Lift = col.lift });
                        m.GroundToFloor();
                        var t0 = sw.Elapsed; m.Update(); updates++;
                        var head = m.NoseWorld;
                        var cam = new OrbitCamera { Yaw = col.yaw, Pitch = 5, Distance = 2.1, Target = new P3(head.X, head.Y - 0.25, head.Z) };
                        dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                            new Rect(c * W, r * W, W, W));
                    }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * cols.Length, W * styles.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"hair-{bangs}.jpg"), 90);
        }
        // Nahaufnahmen der Pony-Varianten (Schulterlang): vorne, schräg, seitlich – Windstille und Brise
        {
            var bangsIds = BangsDef.All.Select(b => b.Id).ToArray();
            var views = new (double yaw, double wind)[] { (0, 0), (35, 0), (90, 0), (0, 40), (35, 40) };
            var dvb = new System.Windows.Media.DrawingVisual();
            using (var dc = dvb.RenderOpen())
                for (int r = 0; r < bangsIds.Length; r++)
                    for (int c = 0; c < views.Length; c++)
                    {
                        var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                        m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                        m.HairColor = System.Windows.Media.Color.FromRgb(0x6A, 0x4A, 0x36);
                        m.SetPose(Presets.All.First(p => p.Name == "Neutral stehen (A-Pose)").ToPose());
                        m.SetHair(new HairSettings { Style = "schulter", Bangs = bangsIds[r], Wind = views[c].wind });
                        var nose = m.NoseWorld;
                        var cam = new OrbitCamera { Yaw = views[c].yaw, Pitch = 3, Distance = 0.9, Target = new P3(nose.X, nose.Y + 0.01, nose.Z) };
                        dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                            new Rect(c * W, r * W, W, W));
                    }
            var rb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * views.Length, W * bangsIds.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rb.Render(dvb);
            SceneFactory.SaveJpeg(rb, Path.Combine(folder, "bangs.jpg"), 90);
        }
        // Zeitmessung: Update mit Haaren (beim Ziehen von Gelenken)
        var mt = new Mannequin(); mt.SetFigure(FigureStyle.AnimeFemale, true, new Proportions());
        mt.SetHair(new HairSettings { Style = "sehrlang" });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) mt.Update();
        File.WriteAllText(Path.Combine(folder, "hair.txt"), $"Update mit sehr langen Haaren: {watch.Elapsed.TotalMilliseconds / 50:F1} ms\n");
    }

    /// <summary>Testmodus --clothes: Outfits (Anime weiblich, Cel-Shading) in verschiedenen Posen, bei Wind und Schwung.</summary>
    private static void ClothesTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 260;
        var outfits = new (string top, string bottom, string shoes, double full)[]
        {
            ("tshirt", "shorts", "schuhe", 100), ("langarm", "hose", "stiefel", 100), ("bandeau", "minirock", "socken", 100),
            ("tanktop", "rock", "schuhe", 100), ("none", "minikleid", "schuhe", 130), ("tshirt", "kleid", "socken", 100),
            ("langarm", "langkleid", "schuhe", 100), ("none", "langkleid", "none", 200),
        };
        var cols = new (double yaw, double wind, double dir, double lift, string pose)[]
        {
            (0, 0, 0, 0, "Neutral stehen (A-Pose)"), (35, 25, 30, 0, "Neutral stehen (A-Pose)"), (90, 25, 0, 0, "Gehen"),
            (35, 0, 0, 0, "Sitzen (Stuhl)"), (0, 70, 90, 0, "Neutral stehen (A-Pose)"), (20, 10, 0, 85, "Neutral stehen (A-Pose)"),
        };
        var sw = new System.Diagnostics.Stopwatch();
        int updates = 0;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int r = 0; r < outfits.Length; r++)
                for (int c = 0; c < cols.Length; c++)
                {
                    var o = outfits[r]; var col = cols[c];
                    var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                    m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                    m.HairColor = System.Windows.Media.Color.FromRgb(0x6A, 0x4A, 0x36);
                    m.SetPose(Presets.All.First(p => p.Name == col.pose).ToPose());
                    m.SetHair(new HairSettings { Style = "schulter", Wind = col.wind, WindDirection = col.dir, Lift = col.lift });
                    m.SetClothing(new ClothingSettings { Top = o.top, Bottom = o.bottom, Shoes = o.shoes, Fullness = o.full, TopColor = "#EDE7DA", BottomColor = r % 2 == 0 ? "#3E4C6E" : "#8A3346" });
                    m.GroundToFloor();
                    sw.Start(); m.Update(); sw.Stop(); updates++;
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = col.yaw, Pitch = 5, Distance = 3.3, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = true, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                        new Rect(c * W, r * W, W, W));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * cols.Length, W * outfits.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "clothes.jpg"), 90);
        // Sitzen aus der Nähe (Rock knielang): vorne, schräg, seitlich, von oben, von unten
        {
            var views = new (double yaw, double pitch)[] { (0, 5), (45, 10), (90, 5), (20, 70), (0, -40) };
            var dvs = new System.Windows.Media.DrawingVisual();
            using (var dc = dvs.RenderOpen())
                for (int c = 0; c < views.Length; c++)
                {
                    var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                    m.SetPose(Presets.All.First(p => p.Name == "Sitzen (Stuhl)").ToPose());
                    m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = "rock", Shoes = "schuhe", BottomColor = "#8A3346" });
                    m.GroundToFloor();
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = views[c].yaw, Pitch = views[c].pitch, Distance = 1.6, Target = new P3(pel.X, pel.Y - 0.1, pel.Z + 0.15) };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                        new Rect(c * W, 0, W, W));
                }
            var rs = new System.Windows.Media.Imaging.RenderTargetBitmap(W * views.Length, W, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rs.Render(dvs);
            SceneFactory.SaveJpeg(rs, Path.Combine(folder, "sitting.jpg"), 90);
            var md = new Mannequin(); md.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
            md.SetPose(Presets.All.First(p => p.Name == "Sitzen (Stuhl)").ToPose());
            md.SetClothing(new ClothingSettings { Bottom = "rock" }); md.GroundToFloor();
            File.WriteAllText(Path.Combine(folder, "skirt-debug.txt"), md.DebugSkirt());
        }
        File.WriteAllText(Path.Combine(folder, "clothes.txt"), $"Update mit Kleidung und Haaren: {sw.Elapsed.TotalMilliseconds / updates:F1} ms\n");
    }

    /// <summary>Testmodus --seat: Sitzblock bei allen Sitz-/Knie-Vorlagen und beim Stehen (mit und ohne Rock).</summary>
    private static void SeatTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 260;
        var poses = new[] { "Sitzen (Stuhl)", "Lässig sitzen (Bein überschlagen)", "Sitzen am Boden (Knie angezogen)", "Schneidersitz", "Fersensitz", "Knien (ein Knie)", "Hocke", "Neutral stehen (A-Pose)" };
        var rows = new (string bottom, double yaw)[] { ("none", 40), ("kleid", 40), ("langkleid", 90) };
        var log = new System.Text.StringBuilder();
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < poses.Length; c++)
                {
                    var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                    m.SetPose(Presets.All.First(p => p.Name == poses[c]).ToPose());
                    m.SetHair(new HairSettings { Style = "schulter", Wind = 0 });
                    m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = rows[r].bottom, Shoes = "schuhe", BottomColor = "#8A3346" });
                    m.GroundToFloor();
                    if (r == 0) log.AppendLine($"{poses[c]}: Block {(m.HasSeat ? "ja" : "nein")}");
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = rows[r].yaw, Pitch = 8, Distance = 3.0, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = true, Shadow = true, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, W, W),
                        new Rect(c * W, r * W, W, W));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * poses.Length, W * rows.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "seat.jpg"), 90);
        File.WriteAllText(Path.Combine(folder, "seat.txt"), log.ToString());
    }

    /// <summary>Testmodus --perf: Rechenzeit von Mannequin.Update je Ausstattung (Mittel über 40 Aufrufe).</summary>
    private static void PerfTest(string file)
    {
        var sb = new System.Text.StringBuilder();
        var cases = new (string name, bool hair, string hairStyle, string bottom, bool seat, string pose)[]
        {
            ("nackt, ohne Haare, ohne Block", false, "kurz", "none", false, "Neutral stehen (A-Pose)"),
            ("Haare kurz", true, "kurz", "none", false, "Neutral stehen (A-Pose)"),
            ("Haare lang", true, "lang", "none", false, "Neutral stehen (A-Pose)"),
            ("Haare sehr lang", true, "sehrlang", "none", false, "Neutral stehen (A-Pose)"),
            ("Rock knielang (ohne Haare)", false, "kurz", "rock", false, "Neutral stehen (A-Pose)"),
            ("Kleid lang (ohne Haare)", false, "kurz", "langkleid", false, "Neutral stehen (A-Pose)"),
            ("Sitzblock, sitzend (ohne Haare)", false, "kurz", "none", true, "Sitzen (Stuhl)"),
            ("Haare lang + Kleid lang + Block, sitzend", true, "lang", "langkleid", true, "Sitzen (Stuhl)"),
        };
        foreach (var c in cases)
        {
            var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, c.hair, new Proportions());
            m.SetHair(new HairSettings { Style = c.hairStyle });
            m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = c.bottom });
            m.SetSeatBlock(c.seat);
            m.SetPose(Presets.All.First(p => p.Name == c.pose).ToPose());
            m.Update();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 40; i++) m.Update();
            m.FreezeDynamics = true; m.Update();
            var swf = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 40; i++) m.Update();
            m.FreezeDynamics = false;
            sb.AppendLine($"{c.name}: {sw.Elapsed.TotalMilliseconds / 40:F1} ms, eingefroren {swf.Elapsed.TotalMilliseconds / 40:F1} ms");
            // Schatten/Konturen wie in der Vorschau nach jeder Änderung
            var cam = new OrbitCamera();
            foreach (var toon in new[] { false, true })
            {
                var light = new LightSettings { Toon = toon };
                var o = new SceneOptions { Light = light, ShadowResolution = 640 };
                var figs = new[] { m };
                SelfShadow.Apply(figs, cam, light); Outline.Create(figs, cam, light); SceneFactory.CreateFloor(o, figs, cam);
                double tS = 0, tO = 0, tF = 0;
                for (int i = 0; i < 5; i++)
                {
                    var s1 = System.Diagnostics.Stopwatch.StartNew(); SelfShadow.Apply(figs, cam, light); tS += s1.Elapsed.TotalMilliseconds;
                    s1.Restart(); Outline.Create(figs, cam, light); tO += s1.Elapsed.TotalMilliseconds;
                    s1.Restart(); SceneFactory.CreateFloor(o, figs, cam); tF += s1.Elapsed.TotalMilliseconds;
                }
                sb.AppendLine($"    {(toon ? "Cel" : "normal")}: Eigenschatten {tS / 5:F0} ms, Konturen {tO / 5:F0} ms, Bodenschatten {tF / 5:F0} ms, Punkte {m.WorldMeshes().Sum(x => x.Mesh.Positions.Count)}");
            }
        }
        File.WriteAllText(file, sb.ToString());
    }

    /// <summary>Testmodus --look: dieselbe Szene in allen Darstellungen (Normal, Cel, 2D flach, Strichzeichnung).</summary>
    private static void LookTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 320;
        var poses = new[] { "Kontrapost", "Sitzen (Stuhl)", "Tanzen" };
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int r = 0; r < poses.Length; r++)
                for (int c = 0; c < 4; c++)
                {
                    var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
                    m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                    m.HairColor = System.Windows.Media.Color.FromRgb(0xC9, 0x8A, 0x4E);
                    m.SetPose(Presets.All.First(p => p.Name == poses[r]).ToPose());
                    m.SetHair(new HairSettings { Style = "lang", Wind = 30 });
                    m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = r == 1 ? "rock" : "kleid", Shoes = "socken", TopColor = "#F2EEE6", BottomColor = "#5B6FA8" });
                    m.GroundToFloor();
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = 25, Pitch = 5, Distance = 3.1, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
                    var light = new LightSettings { Toon = c > 0, ToonStyle = Math.Max(0, c - 1) };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = c < 2, Light = light, Background = System.Windows.Media.Colors.White }, W, W),
                        new Rect(c * W, r * W, W, W));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * 4, W * poses.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "looks.jpg"), 92);
    }

    /// <summary>Testmodus --shape: Körperform-Regler (Anime weiblich/männlich, 2D flach) mit und ohne Kleidung.</summary>
    private static void ShapeTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 260;
        var cases = new (string name, Action<Proportions> set)[]
        {
            ("Standard", p => { }), ("Brust 50", p => p.Bust = 50), ("Brust 170", p => p.Bust = 170),
            ("Taille 75, Hüfte 135", p => { p.Waist = 75; p.Hips = 135; }), ("Gesäß 150", p => p.Glutes = 150),
            ("Bauch 160", p => p.Belly = 160), ("Oberschenkel 140, Waden 130", p => { p.Thighs = 140; p.Calves = 130; }),
            ("Oberarme 160", p => p.UpperArms = 160), ("Hals 140, Hände 125", p => { p.Neck = 140; p.Hands = 125; }),
        };
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < cases.Length; c++)
                {
                    var props = new Proportions { HeightCm = 160 }; cases[c].set(props);
                    var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, props);
                    m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
                    m.SetHair(new HairSettings { Style = "zopf", Wind = 0 });
                    if (r == 1) m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = "minirock", Shoes = "socken" });
                    m.SetPose(Presets.All.First(p => p.Name == "Kontrapost").ToPose()); m.GroundToFloor();
                    var pel = m.PelvisWorld;
                    var cam = new OrbitCamera { Yaw = 35, Pitch = 5, Distance = 3.0, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = true, ToonStyle = 1 }, Background = System.Windows.Media.Colors.White }, W, W * 3 / 2),
                        new Rect(c * W, r * W * 3 / 2, W, W * 3 / 2));
                    if (r == 0)
                        dc.DrawText(new System.Windows.Media.FormattedText(cases[c].name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new System.Windows.Media.Typeface("Segoe UI"), 12, System.Windows.Media.Brushes.Black, 1.0), new Point(c * W + 4, 2));
                }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * cases.Length, W * 3, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "shape.jpg"), 90);
    }

    /// <summary>Testmodus --bust: Oberkörper-Nahaufnahmen (vorne, schräg, seitlich) bei Brust 60/100/150, nackt und mit Shirt, Normal und Cel.</summary>
    private static void BustTest(string folder)
    {
        Directory.CreateDirectory(folder);
        const int W = 240;
        var views = new[] { 0.0, 35.0, 90.0 };
        var rows = new (double bust, bool shirt, bool toon, FigureStyle style)[]
        {
            (100, false, false, FigureStyle.AnimeFemale), (60, false, true, FigureStyle.AnimeFemale), (100, false, true, FigureStyle.AnimeFemale),
            (150, false, true, FigureStyle.AnimeFemale), (100, true, true, FigureStyle.AnimeFemale), (100, false, false, FigureStyle.Female),
        };
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                var m = new Mannequin(); m.SetFigure(row.style, true, new Proportions { HeightCm = 160, Bust = row.bust });
                m.BodyColor = System.Windows.Media.Color.FromRgb(0xF2, 0xD6, 0xC4);
                m.SetHair(new HairSettings { Style = "zopf", Wind = 0 });
                if (row.shirt) m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = "rock" });
                m.SetPose(Presets.All.First(p => p.Name == "Neutral stehen (A-Pose)").ToPose());
                var t = m["chest"].World.Transform(new P3(0, 0.12, 0.05));
                for (int c = 0; c < views.Length; c++)
                {
                    var cam = new OrbitCamera { Yaw = views[c], Pitch = 5, Distance = 1.1, Target = t };
                    dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = false, Light = new LightSettings { Toon = row.toon }, Background = System.Windows.Media.Color.FromRgb(0xF4, 0xF4, 0xF4) }, W, W),
                        new Rect(c * W, r * W, W, W));
                }
            }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * views.Length, W * rows.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        SceneFactory.SaveJpeg(rtb, Path.Combine(folder, "bust.jpg"), 90);
    }

    /// <summary>Testmodus --swarm-test &lt;Ordner&gt; &lt;URL&gt;: Verbindung, Listen, Übernahme aus der Historie, ein kleines Bild generieren, Historie prüfen, nachbessern.</summary>
    private static async Task SwarmTest(string folder, string url)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        void L(string s) { log.AppendLine(s); File.WriteAllText(Path.Combine(folder, "swarm.txt"), log.ToString()); }
        using var c = new PoseStudio.Swarm.SwarmClient(url);
        try
        {
            L("Version: " + await c.ConnectAsync());
            var models = await c.ListModelsAsync("Stable-Diffusion"); var loras = await c.ListModelsAsync("LoRA");
            L($"Modelle {models.Count}, LoRAs {loras.Count}, mit Vorschaubild {models.Count(m => m.PreviewUrl != null)}/{loras.Count(m => m.PreviewUrl != null)}, LoRAs mit Trigger {loras.Count(l => !string.IsNullOrWhiteSpace(l.Trigger))}");
            var thumb = await PoseStudio.Swarm.ThumbCache.LoadAsync(loras[0].PreviewUrl!, 128);
            L($"Vorschaubild geladen: {thumb != null} ({loras[0].Name})");
            var hist = await c.ListHistoryAsync(5);
            L($"Historie: {hist.Count} Bilder, neuestes {hist[0].Src}");
            var s = new PoseStudio.Swarm.SwarmSettings { Url = url, SideLength = 768, Count = 1, Creativity = 0.6 };
            s.ImportFrom(hist[0].Params!, hist[0].OriginalPrompt);
            s.SideLength = 768; s.Count = 1;
            L($"Übernommen: Modell {s.Model}, LoRAs {string.Join(", ", s.Loras.Select(l => l.Name + "@" + l.Weight))}, Steps {s.Steps}, Sampler {s.Sampler}, weitere {s.Base?.Count}: {string.Join(", ", s.Base!.Select(x => x.Key))}");
            L($"Refiner-Vorgabe: {s.RefinerMethod}, Anteil {s.RefinerControl}, ×{s.RefinerUpscale}, {s.RefinerUpscaleMethod}");
            // Szene: Anime-Figur, Cel-Shading, sitzend mit Rock
            var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
            m.BodyColor = System.Windows.Media.Color.FromRgb(0xF8, 0xE2, 0xD4);
            m.SetHair(new HairSettings { Style = "lang", Wind = 30 });
            m.SetClothing(new ClothingSettings { Top = "tshirt", Bottom = "rock", Shoes = "socken" });
            m.SetPose(Presets.All.First(p => p.Name == "Peace-Zeichen").ToPose()); m.GroundToFloor();
            var (w, h) = s.SizeFor(16.0 / 9);
            var pel = m.PelvisWorld;
            var cam = new OrbitCamera { Yaw = 15, Pitch = 5, Distance = 3.2, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
            var bmp = SceneFactory.Render(m, cam, new SceneOptions { Floor = true, Shadow = true, Light = new LightSettings { Toon = true }, Background = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE) }, w, h);
            SceneFactory.SavePng(bmp, Path.Combine(folder, "init.png"));
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgr24, null, 0)));
            using var ms = new MemoryStream(); enc.Save(ms);
            var p = s.BuildParams("data:image/png;base64," + Convert.ToBase64String(ms.ToArray()), w, h, null, loras.ToDictionary(l => l.Id, l => l.Trigger));
            L($"Auftrag: {w}x{h}, Schlüssel: {string.Join(", ", p.Select(x => x.Key))}");
            p["seed"] = (long)Random.Shared.Next(1, int.MaxValue); long sent = p["seed"]!.GetValue<long>();
            var sw = System.Diagnostics.Stopwatch.StartNew(); int prog = 0;
            var res = await c.GenerateAsync(p, new Progress<PoseStudio.Swarm.SwarmProgress>(_ => prog++), CancellationToken.None);
            L($"Generiert in {sw.Elapsed.TotalSeconds:F0} s, Fortschrittsmeldungen {prog}, Bilder {res.Count}: {string.Join(", ", res.Select(r => r.Path))}");
            var bytes = await c.DownloadAsync(res[0].Path);
            File.WriteAllBytes(Path.Combine(folder, "result.png"), bytes);
            long seed = -1;
            if (res[0].Metadata != null && System.Text.Json.Nodes.JsonNode.Parse(res[0].Metadata!) is System.Text.Json.Nodes.JsonObject meta) seed = long.Parse(meta["sui_image_params"]!["seed"]!.ToString());
            var looked = await c.LookupSeedAsync(res[0].Path);
            L($"Heruntergeladen {bytes.Length} Bytes, Seed gesendet {sent}, aus Rückmeldung {seed}, aus Historie {looked}");
            seed = looked ?? sent;
            var hist2 = await c.ListHistoryAsync(3);
            L($"In der Historie: {hist2.Any(x => res[0].Path.Contains(x.Src.Replace(" ", "%20")) || res[0].Path.EndsWith(x.Src))} (neuestes jetzt {hist2[0].Src})");
            s.RefinerUpscale = 1.5;
            var r2 = await c.GenerateAsync(s.BuildRefine(p, seed), null, CancellationToken.None);
            var b2 = await c.DownloadAsync(r2[0].Path);
            File.WriteAllBytes(Path.Combine(folder, "refined.png"), b2);
            var bi = PoseStudio.Swarm.ThumbCache.Decode(b2, 0);
            L($"Nachgebessert: {r2[0].Path}, {bi.PixelWidth}x{bi.PixelHeight}, Seed laut Historie {await c.LookupSeedAsync(r2[0].Path)}");
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
    }

    /// <summary>Testmodus --swarm-cn &lt;Ordner&gt; &lt;URL&gt; [Pose] [Stärke]: gleiche Seed/Prompt ohne und mit anima_3dpose (ohne Init Image).</summary>
    private static async Task SwarmCnTest(string folder, string url, string pose, double strength)
    {
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        void L(string s) { log.AppendLine(s); File.WriteAllText(Path.Combine(folder, "cn.txt"), log.ToString()); }
        using var c = new PoseStudio.Swarm.SwarmClient(url);
        try
        {
            await c.ConnectAsync();
            var m = new Mannequin(); m.SetFigure(FigureStyle.AnimeFemale, true, new Proportions { HeightCm = 160 });
            m.SetPose(Presets.All.First(p => p.Name == pose).ToPose()); m.GroundToFloor();
            const int w = 832, h = 1216;
            var pel = m.PelvisWorld;
            var cam = new OrbitCamera { Yaw = 10, Pitch = 5, Distance = 3.6, Target = new P3(pel.X, (m.ComputeMinY() + m.ComputeMaxY()) / 2, pel.Z) };
            var ctl = SceneFactory.RenderControl(new[] { m }, cam, SceneFactory.ControlMode.Pose3D, w, h);
            SceneFactory.SavePng(ctl, Path.Combine(folder, "control.png"));
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(new System.Windows.Media.Imaging.FormatConvertedBitmap(ctl, System.Windows.Media.PixelFormats.Bgr24, null, 0)));
            using var ms = new MemoryStream(); enc.Save(ms);
            string dataUrl = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            var baseP = new System.Text.Json.Nodes.JsonObject
            {
                ["prompt"] = "1girl, solo, white dress, simple background, full body, anime", ["negativeprompt"] = "worst quality, bad quality, lowres",
                ["model"] = "ghostbubblemango", ["images"] = 1, ["seed"] = 777, ["steps"] = 21, ["cfgscale"] = 4.0, ["width"] = w, ["height"] = h, ["aspectratio"] = "Custom",
            };
            foreach (var withCn in new[] { false, true })
            {
                var p = (System.Text.Json.Nodes.JsonObject)baseP.DeepClone();
                if (withCn) { p["controlnetimageinput"] = dataUrl; p["controlnetmodel"] = "anima_3dpose_6200"; p["controlnetstrength"] = strength; p["controlnetstart"] = 0.0; p["controlnetend"] = 1.0; p["controlnetpreprocessor"] = "None"; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = await c.GenerateAsync(p, null, CancellationToken.None);
                File.WriteAllBytes(Path.Combine(folder, withCn ? "mit_cn.png" : "ohne_cn.png"), await c.DownloadAsync(res[0].Path));
                L($"{(withCn ? "mit" : "ohne")} ControlNet: {sw.Elapsed.TotalSeconds:F0} s, {res[0].Path}");
            }
        }
        catch (Exception ex) { L("FEHLER: " + ex.Message); }
    }

    private static void PelvisTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var poses = new[] { "Neutral stehen (A-Pose)", "Gehen", "Sitzen (Stuhl)", "Ausfallschritt" };
        var yaws = new[] { 0.0, 35.0, 90.0, 180.0 };
        const int W = 260;
        foreach (var style in new[] { FigureStyle.Female, FigureStyle.Male })
        {
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
                for (int i = 0; i < poses.Length; i++)
                {
                    var m = new Mannequin(); m.SetFigure(style, true, new Proportions());
                    m.SetPose(Presets.All.First(p => p.Name == poses[i]).ToPose()); m.GroundToFloor();
                    var pel = m.PelvisWorld;
                    for (int v = 0; v < yaws.Length; v++)
                    {
                        var cam = new OrbitCamera { Yaw = yaws[v], Pitch = 5, Distance = 1.3, Target = new P3(pel.X, pel.Y - 0.08, pel.Z) };
                        dc.DrawImage(SceneFactory.Render(m, cam, new SceneOptions { Floor = false, Shadow = true }, W, W), new Rect(v * W, i * W, W, W));
                    }
                }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W * yaws.Length, W * poses.Length, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            SceneFactory.SaveJpeg(rtb, Path.Combine(folder, $"pelvis-{style}.jpg"), 88);
        }
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