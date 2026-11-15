using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Schattenwurf auf den Boden (y = 0): Alle dem Licht zugewandten Dreiecke der Figur werden
/// entlang der Lichtrichtung auf den Boden projiziert, als Silhouette in eine Textur gezeichnet,
/// weichgezeichnet und als halbtransparente Fläche auf den Boden gelegt.
/// (WPF-3D kennt selbst keine Schatten.)</summary>
public static class ShadowCaster
{
    public static GeometryModel3D? Create(Mannequin figure, Vector3D fromLight, double strength, double softness, int resolution)
    {
        if (fromLight.Y < 0.05 || strength <= 0) return null;
        var travel = -fromLight; // Richtung, in die das Licht fällt
        double invY = 1.0 / -travel.Y;

        var tris = new List<Point>(60000);
        double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var (mesh, m) in figure.WorldMeshes())
        {
            var pos = mesh.Positions;
            var world = new Point3D[pos.Count];
            for (int i = 0; i < pos.Count; i++) world[i] = m.Transform(pos[i]);
            var idx = mesh.TriangleIndices;
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                var a = world[idx[i]]; var b = world[idx[i + 1]]; var c = world[idx[i + 2]];
                // nur dem Licht zugewandte Dreiecke – die Silhouette bleibt gleich, halbe Arbeit
                var n = Vector3D.CrossProduct(b - a, c - a);
                if (Vector3D.DotProduct(n, travel) >= 0) continue;
                foreach (var p in new[] { a, b, c })
                {
                    double k = Math.Max(0, p.Y) * invY;
                    double x = p.X + travel.X * k, z = p.Z + travel.Z * k;
                    tris.Add(new Point(x, z));
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
            }
        }
        if (tris.Count == 0) return null;

        // Weichheit in Metern; Rand für die Unschärfe
        double blurWorld = 0.01 + softness / 100 * 0.18;
        double margin = blurWorld * 2.5 + 0.02;
        minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
        double w = maxX - minX, h = maxZ - minZ;
        double scale = resolution / Math.Max(w, h);
        int pw = Math.Max(8, (int)Math.Ceiling(w * scale)), ph = Math.Max(8, (int)Math.Ceiling(h * scale));

        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                ctx.BeginFigure(Map(tris[i]), true, true);
                ctx.LineTo(Map(tris[i + 1]), false, false);
                ctx.LineTo(Map(tris[i + 2]), false, false);
            }
        }
        geo.Freeze();

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 0.6), geo);
        var sharp = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        sharp.Render(dv);

        BitmapSource tex = sharp;
        double blurPx = Math.Min(80, blurWorld * scale);
        if (blurPx >= 1)
        {
            var img = new Image
            {
                Source = sharp, Width = pw, Height = ph,
                Effect = new BlurEffect { Radius = blurPx, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality }
            };
            img.Measure(new Size(pw, ph));
            img.Arrange(new Rect(0, 0, pw, ph));
            var soft = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            soft.Render(img);
            tex = soft;
        }
        tex.Freeze();

        var brush = new ImageBrush(tex) { Opacity = Math.Clamp(strength / 100, 0, 1) };
        var mat = new DiffuseMaterial(brush);
        var quad = MeshFactory.RectQuad(minX, maxX, minZ, maxZ, 0.003);
        return new GeometryModel3D(quad, mat) { BackMaterial = mat };

        Point Map(Point p) => new((p.X - minX) * scale, (p.Y - minZ) * scale);
    }
}
