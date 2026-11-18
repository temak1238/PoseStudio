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

        // Silhouette selbst rastern (schneller als WPF-Geometrie mit Zehntausenden Dreiecken) und mit drei Box-Durchgängen
        // weichzeichnen (entspricht etwa einem Gauß-Filter). Ergebnis: Schwarz mit Deckkraft = Schattenstärke.
        var cov = new float[pw * ph];
        for (int i = 0; i < tris.Count; i += 3)
            RasterTri(cov, pw, ph, Map(tris[i]), Map(tris[i + 1]), Map(tris[i + 2]));
        double blurPx = Math.Min(80, blurWorld * scale);
        if (blurPx >= 1)
        {
            int r = Math.Max(1, (int)Math.Round(blurPx / 2.2));
            var tmp = new float[cov.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(cov, tmp, pw, ph, r);
                BoxV(tmp, cov, pw, ph, r);
            }
        }
        var px = new byte[pw * ph * 4];
        for (int i = 0; i < cov.Length; i++) px[i * 4 + 3] = (byte)Math.Clamp(cov[i] * 255, 0, 255);
        var tex = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, px, pw * 4);
        tex.Freeze();

        var brush = new ImageBrush(tex) { Opacity = Math.Clamp(strength / 100, 0, 1) };
        var mat = new DiffuseMaterial(brush);
        var quad = MeshFactory.PlaneQuad(rcv.Origin + rcv.Normal * 0.003, rcv.U, rcv.V, rcv.Normal, minA, maxA, minB, maxB);
        return new GeometryModel3D(quad, mat); // einseitig: von hinten (z. B. außerhalb des Raums) unsichtbar

        Point Map(Point p) => new((p.X - minA) * scale, (p.Y - minB) * scale);
    }

    /// <summary>Dreieck füllen (Pixelmitten, mit einem halben Pixel Rand gegen Lücken zwischen Nachbardreiecken).</summary>
    private static void RasterTri(float[] buf, int w, int h, Point a, Point b, Point c)
    {
        double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-9) return;
        if (area < 0) (b, c) = (c, b);
        int minx = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) - 1));
        int maxx = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)) + 1));
        int miny = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 1));
        int maxy = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) + 1));
        // Kantenfunktionen normiert auf Pixel-Abstand, damit der Rand überall einen halben Pixel breit ist
        static (double nx, double ny, double d) Edge(Point p, Point q)
        {
            double nx = p.Y - q.Y, ny = q.X - p.X, len = Math.Sqrt(nx * nx + ny * ny);
            if (len < 1e-12) return (0, 0, 1);
            nx /= len; ny /= len;
            return (nx, ny, -(nx * p.X + ny * p.Y));
        }
        var e0 = Edge(a, b); var e1 = Edge(b, c); var e2 = Edge(c, a);
        for (int y = miny; y <= maxy; y++)
        {
            double py = y + 0.5;
            for (int x = minx; x <= maxx; x++)
            {
                double pxx = x + 0.5;
                if (e0.nx * pxx + e0.ny * py + e0.d < -0.5) continue;
                if (e1.nx * pxx + e1.ny * py + e1.d < -0.5) continue;
                if (e2.nx * pxx + e2.ny * py + e2.d < -0.5) continue;
                buf[y * w + x] = 1;
            }
        }
    }

    private static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0;
            for (int x = -r; x <= r; x++) sum += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = sum * inv;
                sum += src[row + Math.Min(w - 1, x + r + 1)] - src[row + Math.Max(0, x - r)];
            }
        }
    }

    private static void BoxV(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            for (int y = -r; y <= r; y++) sum += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * inv;
                sum += src[Math.Min(h - 1, y + r + 1) * w + x] - src[Math.Max(0, y - r) * w + x];
            }
        }
    }
}
