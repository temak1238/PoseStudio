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
        var opts = new SceneOptions { Floor = true, Shadow = true };
        const int cell = 300;
        var views = new[] { 30.0, 90.0, 0.0 };
        using var sheet = new System.Drawing.Bitmap(cell * views.Length * 2, cell * ((Presets.All.Count + 1) / 2));
        using (var g = System.Drawing.Graphics.FromImage(sheet))
        {
            g.Clear(System.Drawing.Color.White);
            for (int i = 0; i < Presets.All.Count; i++)
            {
                var p = Presets.All[i];
                m.SetPose(p.ToPose());
                m.GroundToFloor();
                for (int v = 0; v < views.Length; v++)
                {
                    var cam = new OrbitCamera { Yaw = views[v], Target = new System.Windows.Media.Media3D.Point3D(0, 0.9, 0), Distance = 4.2 };
                    using var bmp = DebugRasterizer.Render(m, cam, cell, cell);
                    int x = ((i % 2) * views.Length + v) * cell, y = (i / 2) * cell;
                    g.DrawImage(bmp, x, y);
                    g.DrawRectangle(System.Drawing.Pens.Silver, x, y, cell - 1, cell - 1);
                    if (v == 0) g.DrawString($"{i}: {p.Name}", new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Black, x + 4, y + 4);
                }
            }
        }
        sheet.Save(Path.Combine(folder, "presets_sheet.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);

        // WPF-Export (benötigt eine Sitzung mit Grafikausgabe)
        var c2 = new OrbitCamera();
        m.SetPose(Presets.All[0].ToPose()); m.GroundToFloor();
        SceneFactory.SaveJpeg(SceneFactory.Render(m, c2, opts, 1920, 1080), Path.Combine(folder, "fmt_16x9.jpg"), 95);
        SceneFactory.SaveJpeg(SceneFactory.Render(m, c2, opts, 1080, 1920), Path.Combine(folder, "fmt_9x16.jpg"), 95);
    }
}
