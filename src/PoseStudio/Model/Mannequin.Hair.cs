using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Frisuren aus Strähnen. Jede Strähne ist eine Kette von Punkten, die bei jeder Pose neu berechnet wird:
/// Sie wächst in Kämmrichtung aus der Kopfhaut, biegt sich mit Schwerkraft, Wind und Schwung und wird aus Kopf,
/// Hals, Rumpf, Schultern und Oberarmen herausgeschoben. Gezeichnet wird jede Strähne als flaches, spitz zulaufendes Band.</summary>
public sealed partial class Mannequin
{
    private sealed class Strand
    {
        public Point3D Root;        // Kopf-System (unskaliert)
        public Vector3D Dir;        // Kämmrichtung am Ansatz (Kopf-System)
        public Vector3D Normal;     // Kopfhaut-Normale am Ansatz (Kopf-System)
        public double Length, Width, Stiffness, WindFactor;
        public double Hug;          // so weit (Kopf-Einheiten entlang der Strähne) liegt sie an der Kopfhaut an
        public int Segments;
        public double Phase, WindJitter;
        public Vector3D Jitter;     // zufällige Abweichung der Kraftrichtung
    }

    private readonly List<Strand> _strands = new();
    private Part? _hairPart;
    private const int HairSlices = 6;

    /// <summary>Frisur, Pony, Länge, Wind usw.</summary>
    public HairSettings HairStyle { get; private set; } = new();

    /// <summary>Frisur übernehmen. Neue Frisur/neuer Pony bauen die Strähnen neu auf, alles andere wird nur neu berechnet.</summary>
    public void SetHair(HairSettings h)
    {
        var n = h.Clone(); n.Clamp();
        bool rebuild = n.Style != HairStyle.Style || n.Bangs != HairStyle.Bangs || n.Seed != HairStyle.Seed
                       || Math.Abs(n.Length - HairStyle.Length) > 1e-9 || Math.Abs(n.Volume - HairStyle.Volume) > 1e-9
                       || Math.Abs(n.Messiness - HairStyle.Messiness) > 1e-9;
        HairStyle = n;
        if (rebuild) BuildParts();
        Update();
    }

    private bool HasStrandHair => Hair && Style != FigureStyle.Mannequin;

    /// <summary>Haarkappe, Dutt/Haargummis und die Strähnen anlegen (nach dem Körper).</summary>
    private void BuildHair()
    {
        _strands.Clear();
        _hairPart = null;
        if (!HasStrandHair) return;
        bool female = Style.IsFemale(), anime = Style.IsAnime();
        var def = HairStyle.Resolve(female);
        var bangs = BangsDef.Find(HairStyle.Bangs) ?? BangsDef.All[0];
        var (C, R) = BodyBuilder.Scalp(female, anime);
        double vol = HairStyle.Volume / 100;
        double kR = R.Y / 0.1; // Kopfgröße relativ zum Menschenkopf (Längen und Breiten skalieren mit)

        // ---- Kappe: liegt als Grundschicht auf der Kopfhaut (Haaransatz vorne je nach Pony)
        double front = bangs.CapFront * Math.PI / 180, back = def.CapBack * Math.PI / 180;
        double grow = 0.004 + 0.004 * (vol - 1);
        AddPart("head", MeshFactory.Ellipsoid(C, R.X + grow, R.Y + grow, R.Z + grow, 44, 26,
            th => back + (front - back) * (1 + Math.Cos(th)) / 2), MaterialKind.Hair, true);
        if (def.Bun is { } bun)
        {
            var (p, nrm) = ScalpPoint(C, R, bun.Phi, bun.Theta);
            double r = bun.Radius * kR * Math.Sqrt(vol);
            AddPart("head", MeshFactory.Ellipsoid(p + nrm * r * 0.55, r, r * 0.92, r), MaterialKind.Hair, true);
        }

        var rnd = new Random(HairStyle.Seed * 7919 + def.Id.Length * 31 + bangs.Id.Length);
        double mess = HairStyle.Messiness / 100;
        double lenK = HairStyle.Length / 100;
        var groups = def.Groups.Concat(bangs.Groups).ToList();
        if (def.SideLocks > 0)
            foreach (var s in new[] { 1, -1 })
                groups.Add(new StrandGroup
                {
                    PhiFrom = 55, PhiTo = 55, ThetaFrom = s * 80, ThetaTo = s * 80, Length = def.SideLocks, Width = 0.045,
                    Stiffness = 3, Sweep = s * 4, FixedLength = true, Segments = 8
                });

        foreach (var g in groups)
        {
            if (g.Tie is { } tie)
            {
                // Zopf: alle Strähnen kommen aus dem Haargummi
                var (tp, tn) = ScalpPoint(C, R, tie.Phi, tie.Theta);
                var tdir = g.TieDirection; tdir.Normalize();
                var tieCenter = tp + tn * 0.012 * kR;
                AddPart("head", MeshFactory.Ellipsoid(tieCenter, 0.017 * kR, 0.017 * kR, 0.017 * kR), MaterialKind.Hair, true);
                var (u, v) = Perp(tdir);
                for (int i = 0; i < g.Cols; i++)
                {
                    double a = 2 * Math.PI * i / g.Cols + rnd.NextDouble() * 0.5, rr = 0.008 * kR * (0.4 + rnd.NextDouble() * 0.6);
                    var d = tdir + (u * Math.Cos(a) + v * Math.Sin(a)) * (0.25 + 0.2 * vol);
                    d.Normalize();
                    AddStrand(tieCenter + (u * Math.Cos(a) + v * Math.Sin(a)) * rr, d, d, g, g.Length * lenK, rnd, mess, kR, vol);
                }
                continue;
            }
            for (int r = 0; r < g.Rows; r++)
                for (int c = 0; c < g.Cols; c++)
                {
                    double fr = g.Rows > 1 ? (double)r / (g.Rows - 1) : 0.5, fc = g.Cols > 1 ? (double)c / (g.Cols - 1) : 0.5;
                    double phi = g.PhiFrom + (g.PhiTo - g.PhiFrom) * fr + (rnd.NextDouble() - 0.5) * 6;
                    double theta = g.ThetaFrom + (g.ThetaTo - g.ThetaFrom) * fc
                                   + (g.Cols > 1 ? (rnd.NextDouble() - 0.5) * 0.6 * (g.ThetaTo - g.ThetaFrom) / (g.Cols - 1) : 0);
                    var (p, nrm) = ScalpPoint(C, R, phi, theta);
                    // Kämmrichtung: entlang des Längengrads vom Scheitel weg, um die Normale gedreht (Sweep)
                    var (pp, _) = ScalpPoint(C, R, phi + 1, theta);
                    var comb = pp - p; comb.Normalize();
                    if (g.Sweep != 0) comb = RotateAround(comb, nrm, -g.Sweep);
                    var dir = comb + nrm * (0.25 + 0.35 * (vol - 1));
                    dir.Normalize();
                    // Strähnen weiter oben laufen erst über den Kopf: entsprechend länger, damit die Spitzen etwa gleich enden
                    double len = g.Length * (g.Scales ? lenK : 1);
                    if (!g.Scales && !anime) len *= 0.72; // Pony: Menschenaugen sitzen höher am Kopf
                    if (!g.FixedLength) len += (Math.Cos(phi * Math.PI / 180) + 0.25) * R.Y * 1.15;
                    // Anliegen: bis zur breitesten Stelle des Kopfes (nach hinten Gekämmtes über den Scheitel), Pony fast ganz
                    double hugDeg = g.HugAll ? 400 : Math.Abs(Math.Abs(g.Sweep) - 180) < 1 ? phi + 75 : g.FixedLength ? (g.Scales ? 10 : 400) : Math.Max(0, 84 - phi);
                    double hug = Math.Min(hugDeg * Math.PI / 180 * (R.X + R.Y + R.Z) / 3, len * 0.85);
                    AddStrand(p - nrm * 0.004, dir, nrm, g, len, rnd, mess, kR, vol, hug);
                }
        }

        // ---- Ein gemeinsames Mesh für alle Strähnen (Punkte werden in UpdateHair gesetzt)
        var mesh = new MeshGeometry3D();
        int total = 0;
        foreach (var s in _strands)
        {
            int rings = RingCount(s);
            for (int k = 0; k < rings - 1; k++)
                for (int j = 0; j < HairSlices; j++)
                {
                    int a = total + k * HairSlices + j, b = total + k * HairSlices + (j + 1) % HairSlices;
                    int c = a + HairSlices, d = b + HairSlices;
                    // Reihenfolge so, dass die Vorderseite nach außen zeigt (Ring-Ebene: Breite × Dicke, Dicke = Breite × Richtung)
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
                }
            total += rings * HairSlices;
        }
        for (int i = 0; i < total; i++) { mesh.Positions.Add(new Point3D(0, 1.5, 0)); mesh.Normals.Add(new Vector3D(0, 0, 1)); }
        if (total == 0) return;
        AddPart("head", mesh, MaterialKind.Hair, true, dynamic: true);
        _hairPart = Joints.First(j => j.Id == "head").Parts[^1];
    }

    private void AddStrand(Point3D root, Vector3D dir, Vector3D normal, StrandGroup g, double len, Random rnd, double mess, double kR, double vol, double hug = 0)
    {
        _strands.Add(new Strand
        {
            Root = root, Dir = dir, Normal = normal, Hug = hug,
            Length = len * kR * (1 + (rnd.NextDouble() - 0.5) * 0.3 * mess),
            Width = g.Width * kR * (0.85 + rnd.NextDouble() * 0.3) * (0.8 + 0.2 * vol),
            Stiffness = g.Stiffness, WindFactor = g.WindFactor * (1 + (rnd.NextDouble() - 0.5) * 0.6 * mess),
            Segments = g.Segments, Phase = rnd.NextDouble() * Math.PI * 2, WindJitter = rnd.NextDouble(),
            Jitter = new Vector3D(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * mess
        });
    }

    private static int RingCount(Strand s) => s.Segments * 2 + 1;

    private static (Point3D P, Vector3D N) ScalpPoint(Point3D c, Vector3D r, double phiDeg, double thetaDeg)
    {
        double ph = phiDeg * Math.PI / 180, th = thetaDeg * Math.PI / 180;
        var d = new Vector3D(Math.Sin(ph) * Math.Sin(th), Math.Cos(ph), Math.Sin(ph) * Math.Cos(th));
        var p = c + new Vector3D(r.X * d.X, r.Y * d.Y, r.Z * d.Z);
        var n = new Vector3D(d.X / r.X, d.Y / r.Y, d.Z / r.Z);
        n.Normalize();
        return (p, n);
    }

    private static Vector3D RotateAround(Vector3D v, Vector3D axis, double deg)
    {
        var m = Matrix3D.Identity;
        m.Rotate(new Quaternion(axis, deg));
        return m.Transform(v);
    }

    private static (Vector3D U, Vector3D V) Perp(Vector3D d)
    {
        var u = Vector3D.CrossProduct(d, Math.Abs(d.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        u.Normalize();
        var v = Vector3D.CrossProduct(d, u);
        return (u, v);
    }

    // ------------------------------------------------------------------ Kollision

    /// <summary>Elliptische Kapsel im System eines Gelenks: Achse entlang lokal Y von Center+ya bis Center+yb, Radien rx/ry/rz.</summary>
    private readonly record struct Collider(Matrix3D ToLocal, Matrix3D ToWorld, Point3D Center, double Ya, double Yb, double Rx, double Ry, double Rz);

    private List<Collider> HairColliders(double margin)
    {
        var list = new List<Collider>();
        void Add(Joint j, Vector3D scale, Point3D c, double ya, double yb, double rx, double ry, double rz)
        {
            var w = Matrix3D.Identity; w.Scale(scale); w.Append(j.World);
            var inv = w; if (!inv.HasInverse) return; inv.Invert();
            list.Add(new Collider(inv, w, c, ya, yb, rx + margin, ry + margin, rz + margin));
        }
        var head = this["head"];
        double us = head.UniformScale;
        var (C, R) = BodyBuilder.Scalp(Style.IsFemale(), Style.IsAnime());
        Add(head, new Vector3D(us, us, us), C, 0, 0, R.X * 1.02, R.Y * 1.02, R.Z * 1.02);
        var neck = this["neck"];
        Add(neck, neck.SegScale, new Point3D(0, 0.03, 0.005), -0.05, 0.05, 0.05, 0.05, 0.05);
        var chest = this["chest"];
        Add(chest, chest.SegScale, new Point3D(0, 0.15, 0), -0.12, 0.1, 0.165, 0.1, 0.1);
        var spine = this["spine"];
        Add(spine, spine.SegScale, new Point3D(0, 0.08, 0), -0.08, 0.08, 0.14, 0.09, 0.09);
        Add(Root, Root.SegScale, new Point3D(0, -0.02, -0.01), -0.05, 0.05, 0.16, 0.1, 0.105);
        foreach (var sfx in new[] { "_l", "_r" })
        {
            var sh = this["shoulder" + sfx];
            Add(sh, sh.SegScale, new Point3D(0, -0.13, 0), -0.13, 0.13, 0.05, 0.05, 0.05);
        }
        return list;
    }

    private static Point3D Collide(Point3D p, List<Collider> cols)
    {
        foreach (var c in cols)
        {
            var l = c.ToLocal.Transform(p);
            double y = Math.Clamp(l.Y - c.Center.Y, c.Ya, c.Yb);
            var axis = new Point3D(c.Center.X, c.Center.Y + y, c.Center.Z);
            var v = l - axis;
            var s = new Vector3D(v.X / c.Rx, v.Y / c.Ry, v.Z / c.Rz);
            double len = s.Length;
            if (len >= 1) continue;
            if (len < 1e-6) { v = new Vector3D(0, 0, -c.Rz); len = 1; s = new Vector3D(0, 0, -1); }
            p = c.ToWorld.Transform(axis + v / len);
        }
        return p;
    }

    // ------------------------------------------------------------------ Berechnung

    private void UpdateHair()
    {
        if (_hairPart == null || _strands.Count == 0) return;
        var head = this["head"];
        double us = head.UniformScale;
        var hm = Matrix3D.Identity; hm.Scale(new Vector3D(us, us, us)); hm.Append(head.World);
        double world = new Vector3D(hm.M11, hm.M12, hm.M13).Length; // Meter je Kopf-Einheit (mit Körpergröße)
        double vol = HairStyle.Volume / 100;
        var cols = HairColliders(0.004 * world + 0.006 * (vol - 1) * world);

        // Kräfte in Weltkoordinaten: Schwerkraft, Wind (relativ zur Blickrichtung der Figur), Schwung
        var fwd = Root.World.Transform(new Vector3D(0, 0, 1)); fwd.Y = 0;
        if (fwd.LengthSquared < 1e-9) fwd = new Vector3D(0, 0, 1);
        fwd.Normalize();
        var left = Vector3D.CrossProduct(new Vector3D(0, 1, 0), fwd);
        double wa = HairStyle.WindDirection * Math.PI / 180;
        var windDir = -fwd * Math.Cos(wa) + left * Math.Sin(wa);
        double wind = HairStyle.Wind / 100 * 1.6, lift = HairStyle.Lift / 100 * 0.75;
        var up = new Vector3D(0, 1, 0);
        var (sC, sR) = BodyBuilder.Scalp(Style.IsFemale(), Style.IsAnime());
        var headCenter = hm.Transform(sC);
        var hmInv = hm; hmInv.Invert();
        double lie = 1.02 + 0.03 * (vol - 1); // Abstand der anliegenden Haare von der Kopfhaut (relativ)
        // Kopfhaut-Punkt in Richtung des gegebenen Punkts (Anliegen)
        Point3D OnScalp(Point3D w)
        {
            var l = hmInv.Transform(w); var v = l - sC;
            double k = Math.Sqrt(v.X * v.X / (sR.X * sR.X) + v.Y * v.Y / (sR.Y * sR.Y) + v.Z * v.Z / (sR.Z * sR.Z));
            return k < 1e-9 ? w : hm.Transform(sC + v * (lie / k));
        }

        int count = ((MeshGeometry3D)_hairPart.Model.Geometry).Positions.Count;
        var pos = new Point3DCollection(count);
        var nrm = new Vector3DCollection(count);
        var pts = new Point3D[32]; var smooth = new List<Point3D>(64);
        foreach (var s in _strands)
        {
            int n = s.Segments;
            if (pts.Length < n + 1) pts = new Point3D[n + 1];
            double seg = s.Length * world / n;
            var p = hm.Transform(s.Root);
            var d = hm.Transform(s.Dir); d.Normalize();
            pts[0] = p;
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                var radial = p - headCenter; radial.Y = 0;
                if (radial.LengthSquared > 1e-9) radial.Normalize();
                double gust = wind * s.WindFactor * (0.75 + 0.5 * s.WindJitter);
                var f = new Vector3D(0, -1, 0)
                        + (windDir + up * 0.2) * gust
                        + (up * 1.5 + radial * 0.8) * lift * s.WindFactor
                        + s.Jitter * (0.6 + gust * 0.5);
                // Flattern: seitliche Welle entlang der Strähne, stärker mit Wind und zur Spitze hin
                var side = Vector3D.CrossProduct(d, up);
                if (side.LengthSquared > 1e-9) { side.Normalize(); f += side * Math.Sin(i * 0.9 + s.Phase) * (0.15 + gust * 0.35) * t; }
                double stiff = s.Stiffness * Math.Pow(1 - t, 1.6) + 0.25;
                var nd = d * stiff + f * (0.35 + t);
                nd.Normalize();
                var q = p + nd * seg;
                // Am Kopf anliegen, solange die Strähne noch auf der Kopfhaut verläuft – Wind und Schwung lösen sie davon
                double along = i * seg, hugW = s.Hug * world;
                if (along < hugW)
                {
                    double hold = Math.Clamp(1 - (gust + lift * 2) * 0.4, 0.1, 1) * Math.Clamp((hugW - along) / (0.3 * hugW) + 0.35, 0, 1);
                    q = (Point3D)((Vector3D)q * (1 - hold) + (Vector3D)OnScalp(q) * hold);
                    nd = q - p; if (nd.LengthSquared < 1e-12) nd = d; nd.Normalize();
                    q = p + nd * seg;
                }
                q = Collide(q, cols);
                nd = q - p; if (nd.LengthSquared < 1e-12) nd = d; nd.Normalize();
                q = Collide(p + nd * seg, cols);
                nd = q - p; if (nd.LengthSquared < 1e-12) nd = d; nd.Normalize();
                p += nd * seg;
                d = nd;
                pts[i] = p;
            }
            WriteStrandMesh(s, hm, pts, n, pos, nrm, smooth);
        }
        // Hilfspunkte (Schatten-Farbverlauf) am Ende erhalten
        int dummies = ((MeshGeometry3D)_hairPart.Model.Geometry).Positions.Count - pos.Count;
        for (int i = 0; i < dummies; i++) { pos.Add(pos.Count > 0 ? pos[0] : new Point3D()); nrm.Add(new Vector3D(0, 0, 1)); }
        var mesh = (MeshGeometry3D)_hairPart.Model.Geometry;
        mesh.Positions = pos;
        mesh.Normals = nrm;
    }

    /// <summary>Kette glätten (Catmull-Rom, doppelte Auflösung) und als flaches, spitz zulaufendes Band ausgeben.</summary>
    private static void WriteStrandMesh(Strand s, Matrix3D hm, Point3D[] pts, int n, Point3DCollection pos, Vector3DCollection nrm, List<Point3D> smooth)
    {
        smooth.Clear();
        for (int i = 0; i < n; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(n, i + 2)];
            smooth.Add(p1);
            smooth.Add((Point3D)(0.5 * ((Vector3D)p1 * 2 + ((Vector3D)p2 - (Vector3D)p0) * 0.5 + ((Vector3D)p0 * 2 - (Vector3D)p1 * 5 + (Vector3D)p2 * 4 - (Vector3D)p3) * 0.25
                                        + ((Vector3D)p1 * 3 - (Vector3D)p0 - (Vector3D)p2 * 3 + (Vector3D)p3) * 0.125)));
        }
        smooth.Add(pts[n]);
        int rings = smooth.Count;
        double scale = new Vector3D(hm.M11, hm.M12, hm.M13).Length;
        var width = Vector3D.CrossProduct(hm.Transform(s.Dir), hm.Transform(s.Normal));
        for (int k = 0; k < rings; k++)
        {
            var dir = k < rings - 1 ? smooth[k + 1] - smooth[k] : smooth[k] - smooth[k - 1];
            if (dir.LengthSquared < 1e-12) dir = new Vector3D(0, -1, 0);
            dir.Normalize();
            // Breitenachse mitführen (ohne Verdrehen), senkrecht zur Richtung halten
            width -= dir * Vector3D.DotProduct(width, dir);
            if (width.LengthSquared < 1e-12) width = Perp(dir).U;
            width.Normalize();
            var thick = Vector3D.CrossProduct(width, dir);
            double t = (double)k / (rings - 1);
            // breit und flach, erst im letzten Drittel spitz zulaufend
            double w = s.Width * scale * (0.6 + 0.4 * (1 - t)) * Math.Sqrt(Math.Max(0, 1 - Math.Pow(t, 4))) / 2;
            double th = Math.Max(w * 0.22, 0.0012 * scale * (1 - t));
            for (int j = 0; j < HairSlices; j++)
            {
                double a = 2 * Math.PI * j / HairSlices, ca = Math.Cos(a), sa = Math.Sin(a);
                pos.Add(smooth[k] + width * (w * ca) + thick * (th * sa));
                var nn = width * (ca / Math.Max(w, 1e-5)) + thick * (sa / Math.Max(th, 1e-5));
                if (nn.LengthSquared > 1e-12) nn.Normalize();
                nrm.Add(nn);
            }
        }
    }
}
