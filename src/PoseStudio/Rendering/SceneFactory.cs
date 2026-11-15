using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

public sealed class SceneOptions
{
    public Color Background { get; set; } = Color.FromRgb(0xE6, 0xE6, 0xE6);
    public bool Floor { get; set; } = true;
    public bool Shadow { get; set; } = true;
}

public static class SceneFactory
{
    /// <summary>Licht hängt an der Kamera, damit die Figur aus jeder Richtung gleich gut ausgeleuchtet ist.</summary>
    public static Model3DGroup CreateLights(OrbitCamera cam)
    {
        var look = cam.LookDirection;
        var right = cam.Right;
        var up = cam.Up;
        var key = look + right * 0.6 - up * 0.9;
        var fill = look - right * 0.9 + up * 0.1;
        var rim = -look - up * 0.5;
        var g = new Model3DGroup();
        g.Children.Add(new AmbientLight(Color.FromRgb(0x50, 0x50, 0x50)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0xD8, 0xD8, 0xD8), key));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0x48, 0x48, 0x50), fill));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x30), rim));
        return g;
    }

    private static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    private static Color Lighten(Color c, int d) =>
        Color.FromRgb((byte)Math.Min(255, c.R + d), (byte)Math.Min(255, c.G + d), (byte)Math.Min(255, c.B + d));

    public static Model3DGroup CreateFloor(SceneOptions o, Point3D pelvis)
    {
        var g = new Model3DGroup();
        if (o.Floor)
        {
            // Helle Hintergründe: Boden etwas dunkler; dunkle Hintergründe: etwas heller.
            double lum = (0.299 * o.Background.R + 0.587 * o.Background.G + 0.114 * o.Background.B) / 255;
            var floor = lum > 0.35 ? Shade(o.Background, 0.92) : Lighten(o.Background, 18);
            var line = lum > 0.35 ? Shade(o.Background, 0.78) : Lighten(o.Background, 45);

            const double half = 20, tile = 0.5;
            var brush = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(floor), new Pen(new SolidColorBrush(line), 0.025),
                new RectangleGeometry(new Rect(0, 0, 1, 1))))
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, tile / (2 * half), tile / (2 * half)),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(0, 0, 1, 1),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            var mat = new DiffuseMaterial(brush);
            g.Children.Add(new GeometryModel3D(MeshFactory.FloorQuad(0, 0, half, 0), mat) { BackMaterial = mat });
        }
        if (o.Shadow)
        {
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.55));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1));
            var mat = new DiffuseMaterial(rg);
            g.Children.Add(new GeometryModel3D(MeshFactory.FloorQuad(pelvis.X, pelvis.Z, 0.45, 0.004), mat) { BackMaterial = mat });
        }
        return g;
    }

    /// <summary>Rendert die Szene offscreen in der Zielauflösung (mit 2x-Supersampling für glatte Kanten).</summary>
    public static BitmapSource Render(Mannequin figure, OrbitCamera cam, SceneOptions o, int width, int height)
    {
        int ss = Math.Max(width, height) <= 4000 ? 2 : 1;
        int rw = width * ss, rh = height * ss;

        var scene = new Model3DGroup();
        scene.Children.Add(CreateLights(cam));
        if (o.Floor || o.Shadow) scene.Children.Add(CreateFloor(o, figure.PelvisWorld));
        scene.Children.Add(figure.CreateSnapshot());

        var vp = new Viewport3D { Camera = cam.CreateCamera((double)width / height), Width = rw, Height = rh, ClipToBounds = true };
        vp.Children.Add(new ModelVisual3D { Content = scene });
        var root = new Border { Width = rw, Height = rh, Background = new SolidColorBrush(o.Background), Child = vp };
        root.Measure(new Size(rw, rh));
        root.Arrange(new Rect(0, 0, rw, rh));
        root.UpdateLayout();

        var big = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
        big.Render(root);
        if (ss == 1) { big.Freeze(); return big; }

        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(o.Background), null, new Rect(0, 0, width, height));
            dc.DrawImage(big, new Rect(0, 0, width, height));
        }
        var small = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        small.Render(dv);
        small.Freeze();
        return small;
    }

    public static void SaveJpeg(BitmapSource bmp, string path, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
