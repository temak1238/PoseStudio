using System.Windows;
using System.Windows.Media.Media3D;
using PoseStudio.Detection;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Kamera an ein Foto anpassen: Zielpunkt quer zur Blickrichtung und Abstand so wählen, dass die sichtbaren
/// Gelenke der Figur auf den im Foto erkannten Punkten liegen. Blickwinkel und Brennweite bleiben.</summary>
public static class CameraFit
{
    /// <summary>Bildpunkt eines 3D-Punkts in einer Ansicht der Größe W×H (wie die Vorschau).</summary>
    public static Point ToScreen(OrbitCamera cam, Point3D p, double W, double H)
    {
        var v = p - cam.Position;
        double z = Math.Max(0.01, Vector3D.DotProduct(v, cam.LookDirection));
        double f = (H / 2) / Math.Tan(cam.VerticalFov * Math.PI / 360);
        return new Point(W / 2 + Vector3D.DotProduct(v, cam.Right) * f / z, H / 2 - Vector3D.DotProduct(v, cam.Up) * f / z);
    }

    /// <summary>Paare Figur-Gelenk ↔ Fotopunkt (in Ansichtspixeln; das Foto ist einheitlich eingepasst und zentriert).</summary>
    public static List<(Point3D World, Point Screen)> Pairs(Mannequin fig, DetectedPose pose, int iw, int ih, double W, double H)
    {
        double s = Math.Min(W / iw, H / ih), ox = (W - iw * s) / 2, oy = (H - ih * s) / 2;
        var vis = PoseDetector.Visible33(pose, iw, ih, false);
        var pairs = new List<(Point3D, Point)>();
        void Add(int mp, Point3D w) { if (vis[mp]) pairs.Add((w, new Point(ox + pose.Image[mp].X * s, oy + pose.Image[mp].Y * s))); }
        foreach (var (mp, id) in new[] { (11, "shoulder_l"), (12, "shoulder_r"), (13, "elbow_l"), (14, "elbow_r"), (15, "wrist_l"), (16, "wrist_r"),
                                         (23, "hip_l"), (24, "hip_r"), (25, "knee_l"), (26, "knee_r"), (27, "ankle_l"), (28, "ankle_r") })
            Add(mp, fig[id].World.Transform(new Point3D()));
        Add(0, fig["head"].World.Transform(new Point3D(0, 0.085, 0.1))); // Nasenspitze
        return pairs;
    }

    /// <summary>Gauß-Newton über (rechts, oben, Abstand). Ändert die Kamera; Rückgabe: mittlere Abweichung in Pixeln oder null.</summary>
    public static double? Fit(OrbitCamera cam, double W, double H, List<(Point3D World, Point Screen)> pairs)
    {
        if (pairs.Count < 3 || W < 10 || H < 10) return null;
        var right = cam.Right; var up = cam.Up;
        var t0 = cam.Target;
        double[] Res(double[] q)
        {
            cam.Target = t0 + right * q[0] + up * q[1];
            cam.Distance = Math.Clamp(q[2], OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
            var res = new double[pairs.Count * 2];
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = ToScreen(cam, pairs[i].World, W, H);
                res[2 * i] = (p.X - pairs[i].Screen.X) / H; res[2 * i + 1] = (p.Y - pairs[i].Screen.Y) / H;
            }
            return res;
        }
        // Startwert: Schwerpunkte zur Deckung bringen, Abstand aus der Ausdehnung
        var q = new[] { 0.0, 0.0, cam.Distance };
        double[] h = { 0.002, 0.002, 0.01 };
        for (int it = 0; it < 60; it++)
        {
            var r0 = Res(q);
            var J = new double[3][];
            for (int k = 0; k < 3; k++)
            {
                var qq = (double[])q.Clone(); qq[k] += h[k];
                var r1 = Res(qq);
                J[k] = r1.Select((v, i) => (v - r0[i]) / h[k]).ToArray();
            }
            var A = new double[3, 3]; var g = new double[3];
            for (int a = 0; a < 3; a++)
            {
                for (int b = 0; b < 3; b++) A[a, b] = J[a].Zip(J[b], (x, y) => x * y).Sum() + (a == b ? 1e-7 : 0);
                g[a] = -J[a].Zip(r0, (x, y) => x * y).Sum();
            }
            var d = Solve3(A, g);
            if (d == null) break;
            q[0] += Math.Clamp(d[0], -0.5, 0.5);
            q[1] += Math.Clamp(d[1], -0.5, 0.5);
            q[2] = Math.Clamp(q[2] + Math.Clamp(d[2], -q[2] * 0.4, q[2] * 0.4), OrbitCamera.MinDistance, OrbitCamera.MaxDistance);
            if (Math.Abs(d[0]) + Math.Abs(d[1]) + Math.Abs(d[2]) < 1e-6) break;
        }
        var fin = Res(q);
        double err = 0;
        for (int i = 0; i < pairs.Count; i++) err += Math.Sqrt(fin[2 * i] * fin[2 * i] + fin[2 * i + 1] * fin[2 * i + 1]) * H;
        return err / pairs.Count;
    }

    private static double[]? Solve3(double[,] m, double[] v)
    {
        static double Det(double[,] a) => a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        double D = Det(m);
        if (Math.Abs(D) < 1e-20) return null;
        var x = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var a = (double[,])m.Clone();
            for (int r = 0; r < 3; r++) a[r, c] = v[r];
            x[c] = Det(a) / D;
        }
        return x;
    }
}
