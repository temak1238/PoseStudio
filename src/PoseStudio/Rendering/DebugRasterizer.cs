using System.Windows.Media.Media3D;
using PoseStudio.Model;
using SD = System.Drawing;

namespace PoseStudio.Rendering;

/// <summary>Einfacher GDI+-Renderer (Maler-Algorithmus, flach schattiert) nur für den Selbsttest.
/// Er funktioniert auch in Sitzungen ohne Grafikausgabe, in denen WPF nichts zeichnet.</summary>
internal static class DebugRasterizer
{
    public static SD.Bitmap Render(Mannequin m, OrbitCamera cam, int w, int h)
    {
        var view = cam.Position;
        var fwd = cam.LookDirection; var right = cam.Right; var up = cam.Up;
        double f = (h / 2.0) / Math.Tan(cam.VerticalFov * Math.PI / 360);
        var light = -(fwd + right * 0.6 - up * 0.9); light.Normalize();

        var tris = new List<(double depth, SD.PointF[] pts, SD.Color c)>();
        foreach (var j in m.Joints)
        {
            foreach (var part in j.Parts)
            {
                bool hl = ReferenceEquals(j, m.Selected);
                var mesh = (MeshGeometry3D)part.Model.Geometry;
                var mat = ((MatrixTransform3D)part.Model.Transform).Matrix;
                var pos = mesh.Positions.Select(p => mat.Transform(p)).ToArray();
                var idx = mesh.TriangleIndices;
                for (int i = 0; i < idx.Count; i += 3)
                {
                    var a = pos[idx[i]]; var b = pos[idx[i + 1]]; var c = pos[idx[i + 2]];
                    var n = Vector3D.CrossProduct(b - a, c - a);
                    if (n.LengthSquared < 1e-14) continue;
                    n.Normalize();
                    var center = new Point3D((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
                    if (Vector3D.DotProduct(n, center - view) >= 0) continue; // Rückseite
                    var pts = new[] { Project(a), Project(b), Project(c) };
                    double depth = Vector3D.DotProduct(center - view, fwd);
                    double lum = 0.35 + 0.65 * Math.Max(0, Vector3D.DotProduct(n, light));
                    var baseC = hl ? SD.Color.FromArgb(255, 140, 26) : SD.Color.FromArgb(201, 178, 146);
                    tris.Add((depth, pts, SD.Color.FromArgb((int)(baseC.R * lum), (int)(baseC.G * lum), (int)(baseC.B * lum))));
                }
            }
        }

        var bmp = new SD.Bitmap(w, h);
        using var g = SD.Graphics.FromImage(bmp);
        g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(SD.Color.FromArgb(230, 230, 230));
        // Bodenlinie
        var g0 = Project(new Point3D(-3, 0, 0)); var g1 = Project(new Point3D(3, 0, 0));
        g.DrawLine(SD.Pens.Gray, g0, g1);
        var g2 = Project(new Point3D(0, 0, -3)); var g3 = Project(new Point3D(0, 0, 3));
        g.DrawLine(SD.Pens.Gray, g2, g3);
        foreach (var t in tris.OrderByDescending(t => t.depth))
            using (var br = new SD.SolidBrush(t.c)) g.FillPolygon(br, t.pts);
        return bmp;

        SD.PointF Project(Point3D p)
        {
            var v = p - view;
            double z = Vector3D.DotProduct(v, fwd);
            double x = Vector3D.DotProduct(v, right), y = Vector3D.DotProduct(v, up);
            return new SD.PointF((float)(w / 2.0 + x * f / z), (float)(h / 2.0 - y * f / z));
        }
    }
}
