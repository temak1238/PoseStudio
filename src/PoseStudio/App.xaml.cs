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

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
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

        // Licht & Schattenwurf
        m.SetFigure(FigureStyle.Male, true, new Proportions());
        m.SetPose(Presets.All[2].ToPose()); m.GroundToFloor();
        var lights = new[] { (35.0, 50.0, 30.0), (90.0, 25.0, 10.0), (180.0, 35.0, 60.0), (20.0, 85.0, 30.0) };
        var lv = new System.Windows.Media.DrawingVisual();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var dc = lv.RenderOpen())
            for (int i = 0; i < lights.Length; i++)
            {
                var lo = new SceneOptions { Floor = true, Shadow = true, ShadowResolution = 1024,
                    Light = new LightSettings { Azimuth = lights[i].Item1, Elevation = lights[i].Item2, Softness = lights[i].Item3 } };
                var cam = new OrbitCamera { Yaw = 30, Pitch = 18, Distance = 5.5, Target = new System.Windows.Media.Media3D.Point3D(0, 0.7, 0) };
                dc.DrawImage(SceneFactory.Render(m, cam, lo, 480, 480), new Rect(i * 480, 0, 480, 480));
            }
        var lr = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 480, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        lr.Render(lv);
        SceneFactory.SaveJpeg(lr, Path.Combine(folder, "light_sheet.jpg"), 90);
        File.WriteAllText(Path.Combine(folder, "timing.txt"), $"4 Renderings mit Schatten: {sw.ElapsedMilliseconds} ms");
    }
}
