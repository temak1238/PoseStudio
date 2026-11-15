using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Schattenwurf auf eine Fläche (Boden, Wand): Alle dem Licht zugewandten Dreiecke der Figur werden
/// entlang der Lichtrichtung auf die Ebene projiziert, als Silhouette in eine Textur gezeichnet,
/// weichgezeichnet und als halbtransparente Fläche knapp vor die Ebene gelegt.
/// (WPF-3D kennt selbst keine Schatten.)</summary>
public static class ShadowCaster
{
    public static GeometryModel3D? Create(Mannequin figure, Vector3D fromLight, double strength, double softness, int resolution)
        => Create(new[] { figure }, fromLight, strength, softness, resolution, ShadowReceiver.Ground(0));

    public static GeometryModel3D? Create(IEnumerable<Mannequin> figures, Vector3D fromLight, double strength, double softness, int resolution, ShadowReceiver rcv)
    {
        if (strength <= 0) return null;
        var travel = -fromLight; // Richtung, in die das Licht fällt
        double denom = Vector3D.DotProduct(travel, rcv.Normal);
        if (denom > -0.05) return null; // Licht trifft die Fläche nicht (oder nur streifend)

        var tris = new List<Point>(60000);
        double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
        var proj = new Point[3];
        foreach (var (mesh, m) in figures.SelectMany(fg => fg.WorldMeshes()))
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
                bool behind = true;
                int k = 0;
                foreach (var p in new[] { a, b, c })
                {
                    double dist = Vector3D.DotProduct(p - rcv.Origin, rcv.Normal); // Abstand vor der Fläche
                    if (dist > 0) behind = false;
                    var q = p + travel * (Math.Max(0, dist) / -denom);
                    var rel = q - rcv.Origin;
                    proj[k++] = new Point(Vector3D.DotProduct(rel, rcv.U), Vector3D.DotProduct(rel, rcv.V));
                }
                if (behind) continue; // Teil liegt hinter der Fläche (z. B. hinter der Wand)
                foreach (var p in proj)
                {
                    tris.Add(p);
                    if (p.X < minA) minA = p.X; if (p.X > maxA) maxA = p.X;
                    if (p.Y < minB) minB = p.Y; if (p.Y > maxB) maxB = p.Y;
                }
            }
        }
        if (tris.Count == 0) return null;

        // Weichheit in Metern; Rand für die Unschärfe, dann auf die Ausdehnung der Fläche begrenzen
        double blurWorld = 0.01 + softness / 100 * 0.18;
        double margin = blurWorld * 2.5 + 0.02;
        minA = Math.Max(minA - margin, rcv.MinA); maxA = Math.Min(maxA + margin, rcv.MaxA);
        minB = Math.Max(minB - margin, rcv.MinB); maxB = Math.Min(maxB + margin, rcv.MaxB);
        if (maxA - minA < 0.01 || maxB - minB < 0.01) return null;
        double w = maxA - minA, h = maxB - minB;
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
        var quad = MeshFactory.PlaneQuad(rcv.Origin + rcv.Normal * 0.003, rcv.U, rcv.V, rcv.Normal, minA, maxA, minB, maxB);
        return new GeometryModel3D(quad, mat); // einseitig: von hinten (z. B. außerhalb des Raums) unsichtbar

        Point Map(Point p) => new((p.X - minA) * scale, (p.Y - minB) * scale);
    }
}
