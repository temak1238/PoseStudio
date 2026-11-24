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
    public LightSettings Light { get; set; } = new();
    /// <summary>Auflösung der Schattentextur (Vorschau kleiner, Export größer).</summary>
    public int ShadowResolution { get; set; } = 1024;
    /// <summary>Farbe, aus der der Boden abgeleitet wird (wichtig bei transparentem Hintergrund).</summary>
    public Color? FloorBase { get; set; }
    /// <summary>3D-Kulisse (Studio, Landschaft, Raum, Straße).</summary>
    public SetSettings Set { get; set; } = new();
    /// <summary>Transparenter Hintergrund (PNG): kein Himmel/Studio-Hintergrund zeichnen.</summary>
    public bool Transparent { get; set; }
    public ImageSource? BackgroundImage { get; set; }
    public Stretch BackgroundStretch { get; set; } = Stretch.UniformToFill;
    /// <summary>Deckkraft (0…1) eines zufälligen Farbrauschens über Hintergrundfarbe/-bild, unter Figuren und 3D-Boden.</summary>
    public double BackgroundNoise { get; set; }
    /// <summary>Schwarz-Weiß- statt Farbrauschen.</summary>
    public bool BackgroundNoiseMono { get; set; }
}

public static class SceneFactory
{
    /// <summary>Hauptlicht aus der eingestellten Richtung (wirft den Schatten), dazu ein schwaches
    /// Aufhelllicht von der Kamera aus und ein Kantenlicht, damit Schattenseiten nicht schwarz werden.</summary>
    public static Model3DGroup CreateLights(OrbitCamera cam, LightSettings l)
    {
        var look = cam.LookDirection;
        var right = cam.Right;
        var up = cam.Up;
        var key = -l.FromDirection(cam);
        var fill = look - right * 0.9 + up * 0.1;
        var rim = -look - up * 0.5;
        byte Gray(double v) => (byte)Math.Clamp(v, 0, 255);
        byte k = Gray(225 * l.Intensity / 100), a = Gray(2.2 * l.Ambient);
        var g = new Model3DGroup();
        g.Children.Add(new AmbientLight(Color.FromRgb(a, a, a)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(k, k, k), key));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0x38, 0x38, 0x40), fill));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0x28, 0x28, 0x28), rim));
        return g;
    }
    private static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    private static Color Lighten(Color c, int d) =>
        Color.FromRgb((byte)Math.Min(255, c.R + d), (byte)Math.Min(255, c.G + d), (byte)Math.Min(255, c.B + d));

    public static Model3DGroup CreateFloor(SceneOptions o, Mannequin figure, OrbitCamera cam) => CreateFloor(o, new[] { figure }, cam);

    public static Model3DGroup CreateFloor(SceneOptions o, IReadOnlyList<Mannequin> figures, OrbitCamera cam)
    {
        var g = new Model3DGroup();
        bool hasSet = o.Set.Type != SetType.None;
        if (o.Floor && !hasSet)
        {
            // Helle Hintergründe: Boden etwas dunkler; dunkle Hintergründe: etwas heller.
            var bg = o.FloorBase ?? o.Background;
            double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255;
            var floor = lum > 0.35 ? Shade(bg, 0.92) : Lighten(bg, 18);
            var line = lum > 0.35 ? Shade(bg, 0.78) : Lighten(bg, 45);

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
            double blob = 1.0;
            if (o.Light.CastShadow)
            {
                var from = o.Light.FromDirection(cam);
                var receivers = hasSet ? SetBuilder.Get(o.Set).Receivers : new List<ShadowReceiver> { ShadowReceiver.Ground(0) };
                foreach (var r in receivers)
                {
                    var cast = ShadowCaster.Create(figures, from, o.Light.ShadowStrength, o.Light.Softness, o.ShadowResolution, r);
                    if (cast != null) g.Children.Add(cast);
                }
                blob = 0.45; // nur noch leichter Kontaktschatten unter der Figur
            }
            foreach (var fig in figures)
            {
                var pelvis = fig.PelvisWorld;
                var rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.55));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1));
                rg.Opacity = blob;
                var mat = new DiffuseMaterial(rg);
                g.Children.Add(new GeometryModel3D(MeshFactory.FloorQuad(pelvis.X, pelvis.Z, 0.45, 0.004), mat) { BackMaterial = mat });
            }
        }
        return g;
    }

    public static BitmapSource Render(Mannequin figure, OrbitCamera cam, SceneOptions o, int width, int height) =>
        Render(new[] { figure }, cam, o, width, height);

    /// <summary>Rendert die Szene offscreen in der Zielauflösung (mit 2x-Supersampling für glatte Kanten).</summary>
    public static BitmapSource Render(IReadOnlyList<Mannequin> figures, OrbitCamera cam, SceneOptions o, int width, int height)
    {
        var scene = new Model3DGroup();
        scene.Children.Add(CreateLights(cam, o.Light));
        var set = o.Set.Type != SetType.None ? SetBuilder.Get(o.Set) : null;
        if (set != null) scene.Children.Add(set.Model);
        if (o.Floor || o.Shadow) scene.Children.Add(CreateFloor(o, figures, cam));
        SelfShadow.Apply(figures, cam, o.Light);
        foreach (var fig in figures) scene.Children.Add(fig.CreateSnapshot());
        if (Outline.Create(figures, cam, o.Light) is { } outline) scene.Children.Add(outline);

        Brush back = new SolidColorBrush(o.Background);
        if (set != null && !o.Transparent && SetBuilder.BackgroundBrush(set, cam) is { } sky) back = sky;
        return RenderScene(scene, cam, back, o.BackgroundImage, o.BackgroundStretch, width, height, o.Transparent ? 0 : o.BackgroundNoise, o.BackgroundNoiseMono);
    }

    // ================================================================= ControlNet

    /// <summary>Farben je Figur für die ControlNet-Bilder (kräftig, gut unterscheidbar – wie im Trainingsmaterial).</summary>
    public static readonly Color[] FigurePalette =
    {
        Color.FromRgb(0x9E, 0xA6, 0xF2), Color.FromRgb(0xF2, 0xAE, 0x9C), Color.FromRgb(0xB3, 0xAA, 0x22), Color.FromRgb(0x1E, 0x8F, 0xC0),
        Color.FromRgb(0x8F, 0xD0, 0x7A), Color.FromRgb(0xE0, 0x7A, 0xB8), Color.FromRgb(0xF0, 0xC2, 0x4A), Color.FromRgb(0x7A, 0x5A, 0xD0),
    };

    public enum ControlMode { Pose3D, Regions }

    /// <summary>ControlNet-Eingabebild: Figuren einfarbig (je Figur eigene Farbe) auf Schwarz.
    /// Pose3D = glänzend schattiert (für anima_3dpose), Regions = flache Farbflächen (für anima_regional).
    /// Ohne Boden, Kulisse, Schatten, Haare und Objekte.</summary>
    public static BitmapSource RenderControl(IReadOnlyList<Mannequin> figures, OrbitCamera cam, ControlMode mode, int width, int height,
        bool includeProps = false)
    {
        var scene = new Model3DGroup();
        if (mode == ControlMode.Pose3D)
        {
            // Studiolicht relativ zur Kamera: Hauptlicht oben links, Aufheller, Kante – weich und gleichmäßig
            var look = cam.LookDirection; var right = cam.Right; var up = cam.Up;
            scene.Children.Add(new AmbientLight(Color.FromRgb(0x46, 0x46, 0x46)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(0xC8, 0xC8, 0xC8), look + right * 0.55 - up * 0.75));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x50), look - right * 0.8 + up * 0.2));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(0x38, 0x38, 0x38), -look - up * 0.3));
        }
        for (int i = 0; i < figures.Count; i++)
            scene.Children.Add(figures[i].CreateControlSnapshot(FigurePalette[i % FigurePalette.Length], mode == ControlMode.Regions, includeProps));
        return RenderScene(scene, cam, Brushes.Black, null, Stretch.None, width, height);
    }

    private static BitmapSource RenderScene(Model3DGroup scene, OrbitCamera cam, Brush back, ImageSource? image, Stretch stretch, int width, int height, double noise = 0, bool noiseMono = false)
    {
        int ss = Math.Max(width, height) <= 4000 ? 2 : 1;
        int rw = width * ss, rh = height * ss;
        var vp = new Viewport3D { Camera = cam.CreateCamera((double)width / height), Width = rw, Height = rh, ClipToBounds = true };
        vp.Children.Add(new ModelVisual3D { Content = scene });
        var root = new Grid { Width = rw, Height = rh, Background = back };
        if (image != null)
            root.Children.Add(new Image { Source = image, Stretch = stretch, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (noise > 0)
        {
            var img = new Image { Source = NoiseBitmap(width, height, noiseMono), Stretch = Stretch.Fill, Opacity = Math.Clamp(noise, 0, 1) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);   // Körnung = 1 Pixel im fertigen Bild
            root.Children.Add(img);
        }
        root.Children.Add(vp);
        root.Measure(new Size(rw, rh));
        root.Arrange(new Rect(0, 0, rw, rh));
        root.UpdateLayout();

        var big = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
        big.Render(root);
        if (ss == 1) { big.Freeze(); return big; }

        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
            dc.DrawImage(big, new Rect(0, 0, width, height));
        var small = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        small.Render(dv);
        small.Freeze();
        return small;
    }
    /// <summary>Rauschen, gleichverteilt zufällig: bunt (jeder Kanal einzeln) oder Schwarz-Weiß (ein Wert für alle Kanäle).</summary>
    public static BitmapSource NoiseBitmap(int width, int height, bool mono = false, int? seed = null)
    {
        var px = NoiseBytes(width * height * 4, mono, seed is int s ? new Random(s) : Random.Shared);
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, px, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Rauschen überall außer auf den Figuren: <paramref name="figureMask"/> ist die Szene mit transparentem Hintergrund,
    /// ihr Alpha schützt Figuren, Haare und Objekte (auch die weichen Kanten).</summary>
    private static byte[] NoiseBytes(int length, bool mono, Random rnd)
    {
        var px = new byte[length];
        rnd.NextBytes(px);
        for (int i = 0; i < px.Length; i += 4)
        {
            if (mono) px[i + 1] = px[i + 2] = px[i];
            px[i + 3] = 255;
        }
        return px;
    }

    public static BitmapSource ApplyNoise(BitmapSource image, BitmapSource figureMask, double opacity, bool mono = false)
    {
        int w = image.PixelWidth, h = image.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(px, stride, 0);
        var mask = new byte[stride * h];
        new FormatConvertedBitmap(figureMask, PixelFormats.Pbgra32, null, 0).CopyPixels(mask, stride, 0);
        var noise = NoiseBytes(stride * h, mono, Random.Shared);
        double op = Math.Clamp(opacity, 0, 1);
        for (int i = 0; i < px.Length; i += 4)
        {
            double k = op * (1 - mask[i + 3] / 255.0);
            if (k <= 0) continue;
            for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Round(px[i + c] + (noise[i + c] - px[i + c]) * k);
            px[i + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        // In nicht vormultipliziertes BGRA wandeln; fast deckende Pixel (Rundung beim Verkleinern) voll deckend machen
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] >= 250) px[i] = 255;
        var fixedBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(fixedBmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public static void SaveJpeg(BitmapSource bmp, string path, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
