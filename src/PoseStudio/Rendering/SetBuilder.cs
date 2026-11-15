using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Ebene, auf die Schatten fallen können: Ursprung, Achsen U/V (Einheitsvektoren), Normale
/// (zeigt zur Figur hin) und Ausdehnung in U/V (Unendlich = unbegrenzt).</summary>
public readonly record struct ShadowReceiver(Point3D Origin, Vector3D U, Vector3D V, Vector3D Normal,
    double MinA = double.NegativeInfinity, double MaxA = double.PositiveInfinity,
    double MinB = double.NegativeInfinity, double MaxB = double.PositiveInfinity)
{
    public static ShadowReceiver Ground(double y = 0, double minX = double.NegativeInfinity, double maxX = double.PositiveInfinity,
        double minZ = double.NegativeInfinity, double maxZ = double.PositiveInfinity) =>
        new(new Point3D(0, y, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, 1, 0), minX, maxX, minZ, maxZ);
}

public sealed class SetScene
{
    public Model3DGroup Model { get; } = new();
    public List<ShadowReceiver> Receivers { get; } = new();
    /// <summary>Himmel als Verlauf hinter der Szene (oben → Horizont).</summary>
    public (Color Top, Color Horizon)? Sky { get; set; }
    /// <summary>Einfarbiger Hintergrund hinter der Kulisse (z. B. Studio).</summary>
    public Color? Backdrop { get; set; }
}

/// <summary>Baut die 3D-Kulissen. Die Figur steht im Ursprung und schaut nach +Z (zur Kamera in Grundstellung).
/// Wände sind einseitig: Von außen gesehen werden sie nicht gezeichnet (Puppenhaus-Prinzip),
/// damit sie nie zwischen Kamera und Figur stehen.</summary>
public static class SetBuilder
{
    private static string? _cacheKey;
    private static SetScene? _cache;

    /// <summary>Kulisse (zwischengespeichert, eingefroren – kann in Vorschau und Export gleichzeitig verwendet werden).</summary>
    public static SetScene Get(SetSettings s)
    {
        if (_cache != null && _cacheKey == s.CacheKey) return _cache;
        var scene = Build(s);
        scene.Model.Freeze();
        _cacheKey = s.CacheKey;
        _cache = scene;
        return scene;
    }

    /// <summary>Höhe des Horizonts als Anteil der Bildhöhe von oben (0 = oben, 1 = unten).</summary>
    public static double HorizonFraction(OrbitCamera cam)
    {
        double vf = cam.VerticalFov * Math.PI / 180;
        return 0.5 - Math.Tan(cam.Pitch * Math.PI / 180) / (2 * Math.Tan(vf / 2));
    }

    /// <summary>Hintergrund hinter der Kulisse (Himmelverlauf passend zum Horizont oder Studiofarbe).</summary>
    public static Brush? BackgroundBrush(SetScene scene, OrbitCamera cam)
    {
        if (scene.Sky is { } sky)
        {
            double h = Math.Clamp(HorizonFraction(cam), 0.02, 1.5);
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(sky.Top, 0));
            b.GradientStops.Add(new GradientStop(sky.Horizon, h));
            b.GradientStops.Add(new GradientStop(sky.Horizon, Math.Max(h, 1)));
            b.Freeze();
            return b;
        }
        if (scene.Backdrop is { } c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        return null;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    public static SetScene Build(SetSettings s)
    {
        var scene = new SetScene();
        Color main;
        try { main = C(s.Color); } catch { main = C("#D9D5CE"); }
        switch (s.Type)
        {
            case SetType.Studio: Studio(scene, main, s.WallDistance); break;
            case SetType.Meadow: Landscape(scene, C("#5E8C3E"), C("#7FA25A"), C("#B9CDB0"), C("#3F7CC4"), C("#CFE2F2"), null); break;
            case SetType.Beach: Landscape(scene, C("#E2CFA4"), C("#D8C59A"), C("#CFE0E8"), C("#3A84CC"), C("#D6EAF5"), C("#3C82A6")); break;
            case SetType.Desert: Landscape(scene, C("#D8B276"), C("#E0BE88"), C("#EBD9B8"), C("#5D97CF"), C("#F0E2C6"), null); break;
            case SetType.Snow: Landscape(scene, C("#F2F5F8"), C("#E6ECF2"), C("#DDE5EE"), C("#8AA6C2"), C("#E9EEF4"), null); break;
            case SetType.Room: Room(scene, main, s.Floor, s.RoomWidth, s.WallDistance); break;
            case SetType.Street: Street(scene); break;
        }
        return scene;
    }

    // ================================================================= Grundbausteine

    /// <summary>Viereck a-b-c-d, Vorderseite zeigt in Richtung n. Textur: (0,0) bei a, (1,0) bei b, (1,1) bei c.</summary>
    private static void Quad(Model3DGroup g, Point3D a, Point3D b, Point3D c, Point3D d, Vector3D n, Brush brush, bool twoSided = false)
    {
        var mesh = new MeshGeometry3D();
        foreach (var p in new[] { a, b, c, d }) { mesh.Positions.Add(p); mesh.Normals.Add(n); }
        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        bool flip = Vector3D.DotProduct(Vector3D.CrossProduct(b - a, c - a), n) < 0;
        foreach (var i in flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i);
        var mat = Mat(brush);
        g.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = twoSided ? mat : null });
    }

    /// <summary>Diffus beleuchtet plus etwas Eigenleuchten – ersetzt fehlendes Streulicht (Bounce),
    /// damit vom Licht abgewandte Wände nicht schwarz werden.</summary>
    private static Material Mat(Brush brush)
    {
        var glow = brush.Clone();
        glow.Opacity = 0.22;
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(brush));
        g.Children.Add(new EmissiveMaterial(glow));
        return g;
    }

    /// <summary>Kachelmuster: Zeichnung im Bereich tileW × tileH (Meter), wiederholt über eine Fläche von w × h Metern.</summary>
    private static Brush Tiled(Drawing drawing, double tileW, double tileH, double w, double h) =>
        new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, tileW, tileH),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tileW / w, tileH / h),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };

    private static GeometryDrawing Rect(Color c, double x, double y, double w, double h) =>
        new(new SolidColorBrush(c), null, new RectangleGeometry(new System.Windows.Rect(x, y, w, h)));

    private static Drawing Grid(Color fill, Color line, double size, double lineW)
    {
        var g = new DrawingGroup();
        g.Children.Add(Rect(line, 0, 0, size, size));
        g.Children.Add(Rect(fill, lineW / 2, lineW / 2, size - lineW, size - lineW));
        return g;
    }

    private static Drawing Planks()
    {
        var g = new DrawingGroup();
        var colors = new[] { C("#A87A4F"), C("#9C6F46"), C("#B08356"), C("#A27449") };
        var seam = C("#6E4C2E");
        // 4 Dielenreihen à 0,2 m auf 1,2 m Länge, Stöße versetzt
        double[] offsets = { 0.0, 0.45, 0.85, 0.25 };
        for (int r = 0; r < 4; r++)
        {
            g.Children.Add(Rect(colors[r], 0, r * 0.2, 1.2, 0.2));
            g.Children.Add(Rect(seam, 0, r * 0.2 + 0.196, 1.2, 0.004));
            g.Children.Add(Rect(seam, offsets[r], r * 0.2, 0.006, 0.2));
        }
        return g;
    }

    // ================================================================= Studio (Hohlkehle)

    private static void Studio(SetScene scene, Color color, double dist)
    {
        double D = dist, R = Math.Min(1.2, D * 0.6), W = 12, H = 8, front = 15;
        var brush = new SolidColorBrush(color);
        var mat = Mat(brush);

        // Profil in (z, y) mit Normalen: Boden → Viertelkreis → Wand
        var prof = new List<(double z, double y, double nz, double ny)> { (front, 0, 0, 1), (-D + R, 0, 0, 1) };
        for (int i = 1; i <= 14; i++)
        {
            double th = Math.PI / 2 * i / 14;
            prof.Add((-D + R - Math.Sin(th) * R, R - Math.Cos(th) * R, Math.Sin(th), Math.Cos(th)));
        }
        prof.Add((-D, H, 1, 0));

        var mesh = new MeshGeometry3D();
        foreach (var (z, y, nz, ny) in prof)
        {
            mesh.Positions.Add(new Point3D(-W, y, z)); mesh.Normals.Add(new Vector3D(0, ny, nz));
            mesh.Positions.Add(new Point3D(W, y, z)); mesh.Normals.Add(new Vector3D(0, ny, nz));
        }
        for (int i = 0; i < prof.Count - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 3, d = a + 2;
            var n = new Vector3D(0, prof[i].ny, prof[i].nz);
            var pa = mesh.Positions[a]; var pb = mesh.Positions[b]; var pc = mesh.Positions[c];
            bool flip = Vector3D.DotProduct(Vector3D.CrossProduct(pb - pa, pc - pa), n) < 0;
            foreach (var k in flip ? new[] { a, c, b, a, d, c } : new[] { a, b, c, a, c, d }) mesh.TriangleIndices.Add(k);
        }
        scene.Model.Children.Add(new GeometryModel3D(mesh, mat));
        scene.Backdrop = color;
        scene.Receivers.Add(ShadowReceiver.Ground(0, -W, W, -D + R, front));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(0, 0, -D), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), -W, W, R, H));
    }

    // ================================================================= Landschaft mit Horizont

    private static void Landscape(SetScene scene, Color near, Color far, Color haze, Color skyTop, Color skyHorizon, Color? water)
    {
        const double S = 300;
        var ground = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        ground.GradientStops.Add(new GradientStop(near, 0));
        ground.GradientStops.Add(new GradientStop(far, 0.12));
        ground.GradientStops.Add(new GradientStop(haze, 0.55));
        ground.GradientStops.Add(new GradientStop(haze, 1));
        Quad(scene.Model, new Point3D(-S, 0, S), new Point3D(S, 0, S), new Point3D(S, 0, -S), new Point3D(-S, 0, -S), new Vector3D(0, 1, 0), ground);

        if (water is { } wc)
        {
            // Meer ab 7 m hinter der Figur, zum Horizont hin dunstig
            var wb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            wb.GradientStops.Add(new GradientStop(Shade(wc, 1.1), 0));
            wb.GradientStops.Add(new GradientStop(wc, 0.15));
            wb.GradientStops.Add(new GradientStop(Shade(haze, 0.95), 0.8));
            Quad(scene.Model, new Point3D(-S, 0.002, -7), new Point3D(S, 0.002, -7), new Point3D(S, 0.002, -S), new Point3D(-S, 0.002, -7 - (S - 7)), new Vector3D(0, 1, 0), wb);
            // nasser Sandstreifen am Ufer
            Quad(scene.Model, new Point3D(-S, 0.001, -5.5), new Point3D(S, 0.001, -5.5), new Point3D(S, 0.001, -7), new Point3D(-S, 0.001, -7), new Vector3D(0, 1, 0),
                new SolidColorBrush(Shade(near, 0.82)));
        }
        scene.Sky = (skyTop, skyHorizon);
        scene.Receivers.Add(ShadowReceiver.Ground(0));
    }

    // ================================================================= Innenraum

    private static void Room(SetScene scene, Color wall, string floorType, double width, double dist)
    {
        double W = width / 2, D = dist, H = 2.8, front = 4;
        var g = scene.Model;
        double fw = 2 * W, fd = front + D;

        Brush floor = floorType switch
        {
            "Tiles" => Tiled(Grid(C("#D9D6D0"), C("#A9A59E"), 0.6, 0.012), 0.6, 0.6, fw, fd),
            "Concrete" => new SolidColorBrush(C("#A4A19C")),
            "Carpet" => new SolidColorBrush(C("#7D7168")),
            _ => Tiled(Planks(), 1.2, 0.8, fw, fd),
        };
        Quad(g, new Point3D(-W, 0, front), new Point3D(W, 0, front), new Point3D(W, 0, -D), new Point3D(-W, 0, -D), new Vector3D(0, 1, 0), floor);

        var wallBrush = new SolidColorBrush(wall);
        var sideBrush = new SolidColorBrush(Shade(wall, 0.96));
        Quad(g, new Point3D(-W, 0, -D), new Point3D(W, 0, -D), new Point3D(W, H, -D), new Point3D(-W, H, -D), new Vector3D(0, 0, 1), wallBrush);
        Quad(g, new Point3D(-W, 0, front), new Point3D(-W, 0, -D), new Point3D(-W, H, -D), new Point3D(-W, H, front), new Vector3D(1, 0, 0), sideBrush);
        Quad(g, new Point3D(W, 0, -D), new Point3D(W, 0, front), new Point3D(W, H, front), new Point3D(W, H, -D), new Vector3D(-1, 0, 0), sideBrush);

        // Fußleisten
        var board = new SolidColorBrush(Shade(wall, 0.78));
        const double bh = 0.08, e = 0.006;
        Quad(g, new Point3D(-W, 0, -D + e), new Point3D(W, 0, -D + e), new Point3D(W, bh, -D + e), new Point3D(-W, bh, -D + e), new Vector3D(0, 0, 1), board);
        Quad(g, new Point3D(-W + e, 0, front), new Point3D(-W + e, 0, -D), new Point3D(-W + e, bh, -D), new Point3D(-W + e, bh, front), new Vector3D(1, 0, 0), board);
        Quad(g, new Point3D(W - e, 0, -D), new Point3D(W - e, 0, front), new Point3D(W - e, bh, front), new Point3D(W - e, bh, -D), new Vector3D(-1, 0, 0), board);

        // Fenster in der Rückwand (rechts von der Figur aus der Kamera gesehen)
        double wx = Math.Min(W - 1.0, Math.Max(0.8, W * 0.45)), ww = Math.Min(1.4, W - 0.3), y0 = 0.9, y1 = 2.3, z = -D + 0.008;
        var glass = new LinearGradientBrush(C("#9CC4E6"), C("#E8F2FA"), new Point(0, 1), new Point(0, 0));
        Quad(g, new Point3D(wx - ww / 2, y0, z), new Point3D(wx + ww / 2, y0, z), new Point3D(wx + ww / 2, y1, z), new Point3D(wx - ww / 2, y1, z), new Vector3D(0, 0, 1), glass);
        var frame = new SolidColorBrush(C("#F5F4F0"));
        double f = 0.06, z2 = z + 0.004;
        void Bar(double x0, double yb0, double x1, double yb1) =>
            Quad(g, new Point3D(x0, yb0, z2), new Point3D(x1, yb0, z2), new Point3D(x1, yb1, z2), new Point3D(x0, yb1, z2), new Vector3D(0, 0, 1), frame);
        Bar(wx - ww / 2 - f, y0 - f, wx + ww / 2 + f, y0);           // unten
        Bar(wx - ww / 2 - f, y1, wx + ww / 2 + f, y1 + f);           // oben
        Bar(wx - ww / 2 - f, y0, wx - ww / 2, y1);                   // links
        Bar(wx + ww / 2, y0, wx + ww / 2 + f, y1);                   // rechts
        Bar(wx - f / 2, y0, wx + f / 2, y1);                         // Mittelsprosse
        Bar(wx - ww / 2, (y0 + y1) / 2 - f / 2, wx + ww / 2, (y0 + y1) / 2 + f / 2); // Querriegel

        // Tür in der linken Wand
        if (D + front > 2)
        {
            double dz = -D / 2, dw = 0.9, dh = 2.05, x = -W + 0.008;
            var door = new SolidColorBrush(C("#8A6A4A"));
            var doorFrame = new SolidColorBrush(Shade(wall, 0.85));
            Quad(g, new Point3D(x, 0, dz + dw / 2 + 0.07), new Point3D(x, 0, dz - dw / 2 - 0.07), new Point3D(x, dh + 0.07, dz - dw / 2 - 0.07), new Point3D(x, dh + 0.07, dz + dw / 2 + 0.07), new Vector3D(1, 0, 0), doorFrame);
            Quad(g, new Point3D(x + 0.004, 0, dz + dw / 2), new Point3D(x + 0.004, 0, dz - dw / 2), new Point3D(x + 0.004, dh, dz - dw / 2), new Point3D(x + 0.004, dh, dz + dw / 2), new Vector3D(1, 0, 0), door);
        }

        scene.Receivers.Add(ShadowReceiver.Ground(0, -W, W, -D, front));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(0, 0, -D), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), -W, W, 0, H));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(-W, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, 1, 0), new Vector3D(1, 0, 0), -D, front, 0, H));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(W, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, 1, 0), new Vector3D(-1, 0, 0), -D, front, 0, H));
    }

    // ================================================================= Straße

    private static void Street(SetScene scene)
    {
        var g = scene.Model;
        const double X = 45, near = 8, curb1 = -1.6, curb2 = -8.6, back = -11.6, road = -0.13;

        var paving = Tiled(Grid(C("#BDBAB3"), C("#9C998F"), 0.5, 0.012), 0.5, 0.5, 2 * X, near - curb1);
        Quad(g, new Point3D(-X, 0, near), new Point3D(X, 0, near), new Point3D(X, 0, curb1), new Point3D(-X, 0, curb1), new Vector3D(0, 1, 0), paving);
        var curb = new SolidColorBrush(C("#A6A39C"));
        Quad(g, new Point3D(-X, road, curb1), new Point3D(X, road, curb1), new Point3D(X, 0, curb1), new Point3D(-X, 0, curb1), new Vector3D(0, 0, -1), curb);

        Quad(g, new Point3D(-X, road, curb1), new Point3D(X, road, curb1), new Point3D(X, road, curb2), new Point3D(-X, road, curb2), new Vector3D(0, 1, 0), new SolidColorBrush(C("#4C4E52")));
        // Mittellinie (gestrichelt)
        var white = new SolidColorBrush(C("#E8E8E2"));
        double zc = (curb1 + curb2) / 2;
        for (double x = -X; x < X; x += 6)
            Quad(g, new Point3D(x, road + 0.004, zc + 0.06), new Point3D(x + 3, road + 0.004, zc + 0.06), new Point3D(x + 3, road + 0.004, zc - 0.06), new Point3D(x, road + 0.004, zc - 0.06), new Vector3D(0, 1, 0), white);

        var paving2 = Tiled(Grid(C("#BDBAB3"), C("#9C998F"), 0.5, 0.012), 0.5, 0.5, 2 * X, curb2 - back);
        Quad(g, new Point3D(-X, 0, curb2), new Point3D(X, 0, curb2), new Point3D(X, 0, back), new Point3D(-X, 0, back), new Vector3D(0, 1, 0), paving2);
        Quad(g, new Point3D(-X, road, curb2), new Point3D(X, road, curb2), new Point3D(X, 0, curb2), new Point3D(-X, 0, curb2), new Vector3D(0, 0, 1), curb);

        // Häuserzeile mit Fensterraster
        // Gehweg und Häuserzeile auch hinter der Kamera (für Rückansichten)
        const double front = 13;
        var paving3 = Tiled(Grid(C("#BDBAB3"), C("#9C998F"), 0.5, 0.012), 0.5, 0.5, 2 * X, front - near);
        Quad(g, new Point3D(-X, 0, front), new Point3D(X, 0, front), new Point3D(X, 0, near), new Point3D(-X, 0, near), new Vector3D(0, 1, 0), paving3);
        double[] widths = { 8, 7, 9, 6, 8, 10, 7, 9, 8, 7, 9 };
        double[] heights = { 12, 16, 10, 18, 13, 9.5, 15, 11, 14, 12, 16 };
        string[] colors = { "#C9B79C", "#B8C2C8", "#D6C2A8", "#A8A39A", "#C8A890", "#BFC8B8", "#D2C7B6", "#B3A99B" };
        double xs = -X;
        for (int i = 0; xs < X; i++)
        {
            double w = widths[i % widths.Length], h = heights[i % heights.Length];
            var wallC = C(colors[i % colors.Length]);
            var facade = new DrawingGroup();
            facade.Children.Add(Rect(wallC, 0, 0, 3, 3.2));
            facade.Children.Add(Rect(Shade(wallC, 1.12), 0.75, 0.8, 1.5, 1.8));
            facade.Children.Add(Rect(C("#5E7487"), 0.85, 0.9, 1.3, 1.6));
            facade.Children.Add(Rect(Shade(wallC, 0.85), 0.7, 2.6, 1.6, 0.12));
            var fb = Tiled(facade, 3, 3.2, w, h);
            // Textur-v läuft von unten nach oben – Viereck von oben beginnen, damit Fenster aufrecht stehen
            Quad(g, new Point3D(xs, h, back), new Point3D(xs + w, h, back), new Point3D(xs + w, 0, back), new Point3D(xs, 0, back), new Vector3D(0, 0, 1), fb);
            // Erdgeschoss-Sockel
            Quad(g, new Point3D(xs, 0, back + 0.01), new Point3D(xs + w, 0, back + 0.01), new Point3D(xs + w, 0.6, back + 0.01), new Point3D(xs, 0.6, back + 0.01), new Vector3D(0, 0, 1), new SolidColorBrush(Shade(wallC, 0.7)));
            // gegenüberliegende Zeile (versetzt), Fassade zeigt nach -Z
            var wallC2 = C(colors[(i + 3) % colors.Length]);
            double h2 = heights[(i + 4) % heights.Length];
            var facade2 = new DrawingGroup();
            facade2.Children.Add(Rect(wallC2, 0, 0, 3, 3.2));
            facade2.Children.Add(Rect(Shade(wallC2, 1.12), 0.75, 0.8, 1.5, 1.8));
            facade2.Children.Add(Rect(C("#5E7487"), 0.85, 0.9, 1.3, 1.6));
            Quad(g, new Point3D(xs + w, h2, front), new Point3D(xs, h2, front), new Point3D(xs, 0, front), new Point3D(xs + w, 0, front), new Vector3D(0, 0, -1), Tiled(facade2, 3, 3.2, w, h2));
            xs += w;
        }

        scene.Sky = (C("#5B8FCB"), C("#DCE7F0"));
        scene.Receivers.Add(ShadowReceiver.Ground(0, -X, X, curb1, front));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(0, 0, front), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, -1), -X, X, 0, 9.5));
        scene.Receivers.Add(ShadowReceiver.Ground(road, -X, X, curb2, curb1));
        scene.Receivers.Add(ShadowReceiver.Ground(0, -X, X, back, curb2));
        scene.Receivers.Add(new ShadowReceiver(new Point3D(0, 0, back), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), -X, X, 0, 9.5));
    }
}
