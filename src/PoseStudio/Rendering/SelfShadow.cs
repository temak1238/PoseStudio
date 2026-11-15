using System.Windows.Media;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Eigenschatten der Figur per Shadow-Mapping auf der CPU:
/// 1. Tiefenkarte aus Sicht des Lichts (orthografisch, da Richtungslicht) rastern.
/// 2. Für jeden Punkt prüfen, ob ein anderer Teil näher am Licht liegt (mit weicher Mehrfachabtastung).
/// 3. Ergebnis als Texturkoordinate u ablegen; das Material färbt u = 0 normal, u = 1 schwarz.
/// Die Abdunklung entspricht dem Wegfall des Hauptlichts, Umgebungs- und Aufhelllicht bleiben.</summary>
public static class SelfShadow
{
    private const int Res = 1024;

    public static void Apply(Mannequin figure, OrbitCamera cam, LightSettings light)
    {
        var meshes = figure.WorldMeshes().ToList();
        bool enabled = light.CastShadow && light.ShadowStrength > 0 && light.Intensity > 0;
        if (!enabled)
        {
            foreach (var (mesh, _) in meshes) SetCoords(mesh, null);
            return;
        }

        var f = light.FromDirection(cam);
        var r = Vector3D.CrossProduct(f, Math.Abs(f.Y) < 0.95 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        r.Normalize();
        var u = Vector3D.CrossProduct(r, f);

        // Weltpunkte + Lichtraum-Koordinaten (x, y quer zum Licht, d = Nähe zum Licht)
        var data = new List<(MeshGeometry3D mesh, Matrix3D m, double[] lx, double[] ly, double[] ld)>(meshes.Count);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (mesh, m) in meshes)
        {
            int n = mesh.Positions.Count - Mannequin.DummyVertices;
            var lx = new double[n]; var ly = new double[n]; var ld = new double[n];
            for (int i = 0; i < n; i++)
            {
                var p = (Vector3D)m.Transform(mesh.Positions[i]);
                lx[i] = Vector3D.DotProduct(p, r);
                ly[i] = Vector3D.DotProduct(p, u);
                ld[i] = Vector3D.DotProduct(p, f);
                if (lx[i] < minX) minX = lx[i]; if (lx[i] > maxX) maxX = lx[i];
                if (ly[i] < minY) minY = ly[i]; if (ly[i] > maxY) maxY = ly[i];
            }
            data.Add((mesh, m, lx, ly, ld));
        }
        double size = Math.Max(maxX - minX, maxY - minY) * 1.02 + 0.01;
        double scale = (Res - 1) / size;

        // ---- 1. Tiefenkarte rastern (größter d = am nächsten zum Licht)
        var depth = new float[Res * Res];
        Array.Fill(depth, float.NegativeInfinity);
        foreach (var (mesh, _, lx, ly, ld) in data)
        {
            var idx = mesh.TriangleIndices;
            for (int t = 0; t + 2 < idx.Count; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                Raster(depth,
                    (lx[a] - minX) * scale, (ly[a] - minY) * scale, ld[a],
                    (lx[b] - minX) * scale, (ly[b] - minY) * scale, ld[b],
                    (lx[c] - minX) * scale, (ly[c] - minY) * scale, ld[c]);
            }
        }

        // ---- 2. + 3. Punkte prüfen
        double amb = 2.2 * light.Ambient / 255 + 0.18;              // Umgebung + Aufhellung (grob)
        double key = 225 * light.Intensity / 100 / 255;
        double strength = Math.Min(1, light.ShadowStrength / 50);
        int radius = 1 + (int)Math.Round(light.Softness / 100 * 5); // Weichheit = Abtastradius in Zellen
        double cell = 1 / scale;

        foreach (var (mesh, m, lx, ly, ld) in data)
        {
            int n = lx.Length;
            var coords = new double[n];
            for (int i = 0; i < n; i++)
            {
                var nrm = m.Transform(mesh.Normals[i]);
                if (nrm.LengthSquared < 1e-12) continue;
                nrm.Normalize();
                double ndl = Vector3D.DotProduct(nrm, f);
                if (ndl <= 0.02) continue; // vom Licht abgewandt: schon dunkel durch die Beleuchtung

                // "Normal Offset": Prüfpunkt entlang der Normale nach außen schieben, damit die eigene
                // Oberfläche (v. a. bei streifendem Licht) sich nicht selbst verschattet.
                double off = cell * (radius + 2.5) + 0.004;
                double px = lx[i] + Vector3D.DotProduct(nrm, r) * off;
                double py = ly[i] + Vector3D.DotProduct(nrm, u) * off;
                double pd = ld[i] + ndl * off;
                double gx = (px - minX) * scale, gy = (py - minY) * scale;
                double bias = 0.004;
                int occ = 0, tot = 0;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = (int)Math.Round(gx) + dx, y = (int)Math.Round(gy) + dy;
                        if (x < 0 || y < 0 || x >= Res || y >= Res) continue;
                        tot++;
                        if (depth[y * Res + x] > pd + bias) occ++;
                    }
                if (occ == 0 || tot == 0) continue;
                // bei sehr flachem Lichteinfall sanft ausblenden (dort wirkt das Hauptlicht ohnehin kaum)
                double fade = Math.Clamp((ndl - 0.08) / 0.22, 0, 1);
                double shadow = (double)occ / tot * fade;
                double uPhys = key * ndl / (amb + key * ndl);
                coords[i] = Math.Clamp(shadow * uPhys * strength, 0, 0.98);
            }
            SetCoords(mesh, coords);
        }
    }

    private static void SetCoords(MeshGeometry3D mesh, double[]? u)
    {
        int n = mesh.Positions.Count - Mannequin.DummyVertices;
        var tc = new PointCollection(mesh.Positions.Count);
        for (int i = 0; i < n; i++) tc.Add(new System.Windows.Point(u == null ? 0 : u[i], 0.5));
        tc.Add(new System.Windows.Point(0, 0));
        tc.Add(new System.Windows.Point(1, 1));
        tc.Freeze();
        mesh.TextureCoordinates = tc;
    }

    /// <summary>Dreieck in die Tiefenkarte rastern (Kantenfunktionen, Pixelmitten).</summary>
    private static void Raster(float[] buf,
        double x0, double y0, double d0, double x1, double y1, double d1, double x2, double y2, double d2)
    {
        double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (Math.Abs(area) < 1e-9) return;
        int minx = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxx = Math.Min(Res - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int miny = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxy = Math.Min(Res - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
        double inv = 1 / area;
        for (int y = miny; y <= maxy; y++)
        {
            for (int x = minx; x <= maxx; x++)
            {
                double w0 = ((x1 - x) * (y2 - y) - (x2 - x) * (y1 - y)) * inv;
                double w1 = ((x2 - x) * (y0 - y) - (x0 - x) * (y2 - y)) * inv;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                float d = (float)(w0 * d0 + w1 * d1 + w2 * d2);
                int k = y * Res + x;
                if (d > buf[k]) buf[k] = d;
            }
        }
    }
}
