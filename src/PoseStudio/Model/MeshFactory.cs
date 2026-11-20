using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Querschnitt für Loft-Körper: Mittelpunkt und zwei Halbachsen (Länge = Radius).</summary>
public readonly record struct Ring(Point3D C, Vector3D U, Vector3D V);

public static class MeshFactory
{
    /// <summary>Ellipsoid mit Mittelpunkt c und Radien rx/ry/rz, Dreiecke gegen den Uhrzeigersinn (Außenseite).
    /// Mit phiMax (abhängig vom Winkel um die Y-Achse, 0 = vorne/+Z) entsteht eine offene Kappe, z. B. für Haare.</summary>
    public static MeshGeometry3D Ellipsoid(Point3D c, double rx, double ry, double rz, int slices = 36, int stacks = 22,
        Func<double, double>? phiMax = null)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= stacks; i++)
        {
            for (int j = 0; j <= slices; j++)
            {
                double th = 2 * Math.PI * j / slices;
                double pm = phiMax?.Invoke(th) ?? Math.PI;
                double phi = pm * i / stacks;
                double y = Math.Cos(phi), r = Math.Sin(phi);
                double x = r * Math.Sin(th), z = r * Math.Cos(th);
                mesh.Positions.Add(new Point3D(c.X + rx * x, c.Y + ry * y, c.Z + rz * z));
                var n = new Vector3D(x / rx, y / ry, z / rz);
                if (n.LengthSquared > 0) n.Normalize();
                mesh.Normals.Add(n);
            }
        }
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int a = i * (slices + 1) + j;
                int b = a + slices + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }
        }
        mesh.Freeze();
        return mesh;
    }

    public static MeshGeometry3D Sphere(Point3D c, double r) => Ellipsoid(c, r, r, r, 24, 16);

    /// <summary>Glatter Körper durch eine Folge von elliptischen Querschnitten, Enden mit gerundeten Kappen.</summary>
    public static MeshGeometry3D Loft(IReadOnlyList<Ring> input, int slices = 28, double cap = 0.8)
    {
        // Zwischenringe einfügen (max. ~2 cm Abstand) – entlang einer Catmull-Rom-Kurve, damit die
        // Oberfläche ohne Knicke (und damit ohne Schattierungsstreifen) verläuft und Eigenschatten saubere Kanten bekommen.
        const double step = 0.02;
        var rings = new List<Ring> { input[0] };
        for (int i = 1; i < input.Count; i++)
        {
            var p0 = input[Math.Max(0, i - 2)]; var p1 = input[i - 1]; var p2 = input[i]; var p3 = input[Math.Min(input.Count - 1, i + 1)];
            int steps = Math.Max(1, (int)Math.Ceiling((p2.C - p1.C).Length / step));
            for (int k = 1; k <= steps; k++)
            {
                double t = (double)k / steps;
                rings.Add(new Ring(
                    (Point3D)CatmullRom((Vector3D)p0.C, (Vector3D)p1.C, (Vector3D)p2.C, (Vector3D)p3.C, t),
                    CatmullRom(p0.U, p1.U, p2.U, p3.U, t),
                    CatmullRom(p0.V, p1.V, p2.V, p3.V, t)));
            }
        }

        var all = new List<Ring>();
        var (startRings, startApex) = CapRings(rings[0], rings[1], cap);
        startRings.Reverse();
        all.AddRange(startRings);
        all.AddRange(rings);
        var (endRings, endApex) = CapRings(rings[^1], rings[^2], cap);
        all.AddRange(endRings);

        var pts = new List<Point3D>();
        foreach (var r in all)
            for (int j = 0; j < slices; j++)
            {
                double th = 2 * Math.PI * j / slices;
                pts.Add(r.C + r.U * Math.Cos(th) + r.V * Math.Sin(th));
            }
        int apexS = pts.Count; pts.Add(startApex);
        int apexE = pts.Count; pts.Add(endApex);

        var tri = new List<int>();
        int n = all.Count;
        for (int k = 0; k < n - 1; k++)
            for (int j = 0; j < slices; j++)
            {
                int a = k * slices + j, b = k * slices + (j + 1) % slices;
                int c = (k + 1) * slices + j, d = (k + 1) * slices + (j + 1) % slices;
                tri.AddRange(new[] { a, c, b, b, c, d });
            }
        for (int j = 0; j < slices; j++)
        {
            tri.AddRange(new[] { apexS, j, (j + 1) % slices });
            int last = (n - 1) * slices;
            tri.AddRange(new[] { last + j, apexE, last + (j + 1) % slices });
        }

        // Ausrichtung prüfen: Normale eines mittleren Dreiecks muss vom Ring-Mittelpunkt weg zeigen.
        int mid = (n / 2) * slices;
        var mr = all[n / 2];
        var nrm = Vector3D.CrossProduct(pts[mid + slices] - pts[mid], pts[mid + 1] - pts[mid]);
        if (Vector3D.DotProduct(nrm, pts[mid] - mr.C) < 0)
            for (int i = 0; i < tri.Count; i += 3) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);

        return Build(pts, tri);
    }

    private static Vector3D CatmullRom(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    private static (List<Ring> rings, Point3D apex) CapRings(Ring end, Ring neighbor, double cap)
    {
        var axis = end.C - neighbor.C;
        axis.Normalize();
        double r = (end.U.Length + end.V.Length) / 2 * cap;
        var list = new List<Ring>();
        foreach (var (shift, scale) in new[] { (0.5, 0.866), (0.866, 0.5), (0.97, 0.24) })
            list.Add(new Ring(end.C + axis * r * shift, end.U * scale, end.V * scale));
        return (list, end.C + axis * r);
    }

    internal static MeshGeometry3D Build(List<Point3D> pts, List<int> tri)
    {
        var normals = new Vector3D[pts.Count];
        for (int i = 0; i < tri.Count; i += 3)
        {
            var fn = Vector3D.CrossProduct(pts[tri[i + 1]] - pts[tri[i]], pts[tri[i + 2]] - pts[tri[i]]);
            normals[tri[i]] += fn; normals[tri[i + 1]] += fn; normals[tri[i + 2]] += fn;
        }
        var mesh = new MeshGeometry3D();
        foreach (var p in pts) mesh.Positions.Add(p);
        foreach (var nv in normals)
        {
            var v = nv;
            if (v.LengthSquared > 0) v.Normalize();
            mesh.Normals.Add(v);
        }
        foreach (var i in tri) mesh.TriangleIndices.Add(i);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Loft entlang Y: (y, Radius X, Radius Z, Versatz Z).</summary>
    public static MeshGeometry3D LoftY(double k, params (double y, double rx, double rz, double cz)[] p) => LoftY(k, 0.8, p);

    /// <summary>Wie LoftY, mit eigener Kappenhöhe (Anteil des Endradius).</summary>
    public static MeshGeometry3D LoftY(double k, double cap, params (double y, double rx, double rz, double cz)[] p) =>
        Loft(p.Select(q => new Ring(new Point3D(0, q.y * k, q.cz * k), new Vector3D(q.rx * k, 0, 0), new Vector3D(0, 0, q.rz * k))).ToList(), 28, cap);

    /// <summary>Loft entlang Z (z. B. Fuß): (z, Radius X, Radius Y, Versatz Y).</summary>
    public static MeshGeometry3D LoftZ(params (double z, double rx, double ry, double cy)[] p) =>
        Loft(p.Select(q => new Ring(new Point3D(0, q.cy, q.z), new Vector3D(q.rx, 0, 0), new Vector3D(0, q.ry, 0))).ToList());

    /// <summary>Konische Röhre mit runden Enden von a nach b (z. B. Finger).</summary>
    public static MeshGeometry3D Tube(Point3D a, Point3D b, double ra, double rb, int slices = 12)
    {
        var axis = b - a;
        var dir = axis; dir.Normalize();
        var p = Vector3D.CrossProduct(dir, Math.Abs(dir.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
        p.Normalize();
        var q = Vector3D.CrossProduct(dir, p);
        var rings = new List<Ring>();
        for (int i = 0; i <= 3; i++)
        {
            double t = i / 3.0, r = ra + (rb - ra) * t;
            rings.Add(new Ring(a + axis * t, p * r, q * r));
        }
        return Loft(rings, slices, 1.0);
    }

    /// <summary>Rechteck in der Ebene durch o mit Achsen u/v; Vorderseite zeigt nach n.
    /// Textur (0,0) bei (minA, minB), u läuft entlang u, v entlang v.</summary>
    public static MeshGeometry3D PlaneQuad(Point3D o, Vector3D u, Vector3D v, Vector3D n, double minA, double maxA, double minB, double maxB)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(o + u * minA + v * minB);
        mesh.Positions.Add(o + u * maxA + v * minB);
        mesh.Positions.Add(o + u * maxA + v * maxB);
        mesh.Positions.Add(o + u * minA + v * maxB);
        for (int i = 0; i < 4; i++) mesh.Normals.Add(n);
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        bool flip = Vector3D.DotProduct(Vector3D.CrossProduct(u, v), n) < 0;
        foreach (var idx in flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(idx);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Rechteckige Bodenfläche; Textur-u läuft mit X, Textur-v mit Z (v = 0 bei minZ).</summary>
    public static MeshGeometry3D RectQuad(double minX, double maxX, double minZ, double maxZ, double y)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(minX, y, maxZ));
        mesh.Positions.Add(new Point3D(maxX, y, maxZ));
        mesh.Positions.Add(new Point3D(maxX, y, minZ));
        mesh.Positions.Add(new Point3D(minX, y, minZ));
        for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(idx);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Horizontale Fläche (XZ) auf Höhe y, Texturkoordinaten 0..1.</summary>
    public static MeshGeometry3D FloorQuad(double cx, double cz, double halfSize, double y)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(cx - halfSize, y, cz + halfSize));
        mesh.Positions.Add(new Point3D(cx + halfSize, y, cz + halfSize));
        mesh.Positions.Add(new Point3D(cx + halfSize, y, cz - halfSize));
        mesh.Positions.Add(new Point3D(cx - halfSize, y, cz - halfSize));
        for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(idx);
        mesh.Freeze();
        return mesh;
    }
}
