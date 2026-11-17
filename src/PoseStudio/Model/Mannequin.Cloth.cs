using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Kleidung: anliegende Teile baut BodyBuilder (sie biegen sich mit dem Körper), Röcke und Kleider sind fallender Stoff.
/// Der Stoff ist ein Ring aus senkrechten Bahnen, der an der Taille hängt. Bei jeder Pose wird er neu ins Gleichgewicht gerechnet:
/// Schwerkraft, Stoffsteifigkeit (hält die Glockenform), Wind und Schwung wie bei den Haaren; die Bahnen bleiben gleich lang,
/// der Stoff kann sich zusammenschieben (Falten), aber nicht weiter werden als sein Zuschnitt; Becken, Ober- und Unterschenkel
/// und der Boden schieben ihn beiseite.</summary>
public sealed partial class Mannequin
{
    private readonly ShadeBrush _topBrush = new(Color.FromRgb(0xF4, 0xF1, 0xEA));
    private readonly ShadeBrush _bottomBrush = new(Color.FromRgb(0x3E, 0x4C, 0x6E));
    private readonly ShadeBrush _shoeBrush = new(Color.FromRgb(0x4A, 0x3A, 0x32));

    public ClothingSettings Clothing { get; private set; } = new();

    private Part? _skirtPart;
    private Point3D[] _skRing = Array.Empty<Point3D>();     // Taillenring im Becken-System
    private Vector3D[] _skRadial = Array.Empty<Vector3D>(); // nach außen, Becken-System
    private int _skN, _skRows;
    private double _skLen, _skFlare, _skWaistR;

    private void InitClothMaterials(Action<MaterialKind, ShadeBrush, byte> def)
    {
        def(MaterialKind.ClothTop, _topBrush, 0x18);
        def(MaterialKind.ClothBottom, _bottomBrush, 0x18);
        def(MaterialKind.Shoes, _shoeBrush, 0x40);
        def(MaterialKind.Seat, _seatBrush, 0x10);
    }

    /// <summary>Kleidung übernehmen. Andere Teile oder Weite bauen neu auf, Farben wirken sofort.</summary>
    public void SetClothing(ClothingSettings c)
    {
        var n = c.Clone(); n.Clamp();
        bool rebuild = n.Top != Clothing.Top || n.Bottom != Clothing.Bottom || n.Shoes != Clothing.Shoes
                       || Math.Abs(n.Fullness - Clothing.Fullness) > 1e-9;
        Clothing = n;
        _topBrush.Color = Parse(n.TopColor, _topBrush.Color);
        _bottomBrush.Color = Parse(n.BottomColor, _bottomBrush.Color);
        _shoeBrush.Color = Parse(n.ShoeColor, _shoeBrush.Color);
        if (rebuild) BuildParts();
        Update();

        static Color Parse(string s, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(s); } catch { return fallback; }
        }
    }

    private static bool IsCloth(MaterialKind k) => k is MaterialKind.ClothTop or MaterialKind.ClothBottom or MaterialKind.Shoes;

    /// <summary>Anliegende Kleidung und den Rock-Ring anlegen (nach dem Körper).</summary>
    private void BuildClothes(BodyBuilder builder)
    {
        _skirtPart = null;
        if (Style == FigureStyle.Mannequin) return;
        bool female = Style.IsFemale(), anime = Style.IsAnime();
        builder.BuildClothes(Clothing, female, anime);

        var bottom = BottomDef.Find(Clothing.Bottom);
        if (bottom is not { Kind: BottomKind.Skirt or BottomKind.Dress }) return;
        const double yWaist = 0.09;
        var sec = BodyBuilder.Clip(BodyBuilder.TorsoSections(female, anime), yWaist - 0.002, yWaist, 0.012)[^1];
        _skN = 32;
        _skLen = bottom.Length;
        _skRows = Math.Clamp((int)Math.Round(_skLen / 0.045), 5, 22);
        _skWaistR = (sec.Rx + sec.Rz) / 2;
        // Zuschnitt: Winkel, bis zu dem sich der Stoff ausbreiten kann (50 % eng, 100 % A-Linie, 200 % fast Tellerrock)
        _skFlare = Math.Clamp(24 * Math.Pow(Clothing.Fullness / 100, 1.6), 5, 88) * Math.PI / 180;
        _skRing = new Point3D[_skN]; _skRadial = new Vector3D[_skN];
        for (int j = 0; j < _skN; j++)
        {
            double th = 2 * Math.PI * j / _skN, sx = Math.Sin(th), cz = Math.Cos(th);
            _skRing[j] = new Point3D(sec.Rx * sx, yWaist, sec.Cz + sec.Rz * cz);
            var r = new Vector3D(sx / sec.Rx, 0, cz / sec.Rz); r.Normalize();
            _skRadial[j] = r;
        }

        // Gitter (Reihen × Bahnen, rundum geschlossen); Vorderseite zeigt nach außen
        var mesh = new MeshGeometry3D();
        int n = _skN;
        for (int i = 0; i < _skRows; i++)
            for (int j = 0; j < n; j++)
            {
                int a = i * n + j, b = i * n + (j + 1) % n, c = a + n, d = b + n;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
            }
        for (int i = 0; i < (_skRows + 1) * n; i++) { mesh.Positions.Add(new Point3D(0, 1, 0)); mesh.Normals.Add(new Vector3D(0, 0, 1)); }
        AddPart("pelvis", mesh, MaterialKind.ClothBottom, true, dynamic: true);
        _skirtPart = Root.Parts[^1];
    }

    private List<Collider> ClothColliders(double margin)
    {
        var list = new List<Collider>();
        void Add(Joint j, Point3D c, double ya, double yb, double rx, double rz)
        {
            var w = Matrix3D.Identity; w.Scale(j.SegScale); w.Append(j.World);
            var inv = w; if (!inv.HasInverse) return; inv.Invert();
            list.Add(new Collider(inv, w, c, ya, yb, rx + margin, (rx + rz) / 2 + margin, rz + margin));
        }
        bool female = Style.IsFemale();
        double hip = female ? 1.1 : 1.0, w = BodyBuilder.LimbW(female, Style.IsAnime());
        Add(Root, new Point3D(0, -0.01, -0.012), -0.07, 0.06, 0.155 * hip, 0.1 * (female ? 1.08 : 1));
        Add(this["spine"], new Point3D(0, 0.08, 0), -0.08, 0.1, 0.14, 0.09);
        foreach (var sfx in new[] { "_l", "_r" })
        {
            // seitlich etwas breiter als der Oberschenkel: die beiden berühren sich, der Stoff fädelt nicht dazwischen durch
            Add(this["hip" + sfx], new Point3D(0, -0.2, 0.006), -0.19, 0.17, 0.1 * (female ? 1.04 : 1), 0.083);
            Add(this["knee" + sfx], new Point3D(0, -0.2, 0), -0.2, 0.18, 0.056 * w, 0.058 * w);
        }
        return list;
    }


    /// <summary>Test: Punkte der vorderen Rockbahn im System des linken Oberschenkels (x, y entlang, z) und Abstand zur Achse.</summary>
    internal string DebugSkirt()
    {
        if (_skirtPart == null) return "kein Rock";
        var sb = new System.Text.StringBuilder();
        var mesh = (MeshGeometry3D)_skirtPart.Model.Geometry;
        var hip = this["hip_l"];
        var w = Matrix3D.Identity; w.Scale(hip.SegScale); w.Append(hip.World); w.Invert();
        foreach (int j in new[] { 0, 2, 4 })
        {
            sb.Append($"Bahn {j}: ");
            for (int i = 0; i <= _skRows; i++)
            {
                var l = w.Transform(mesh.Positions[i * _skN + j]);
                sb.Append($"({l.X:F2},{l.Y:F2},{l.Z:F2} r{Math.Sqrt(l.X * l.X + l.Z * l.Z):F2}) ");
            }
            sb.AppendLine();
        }
        sb.AppendLine($"Hüfte SegScale {hip.SegScale}, Becken {Root.SegScale}");
        return sb.ToString();
    }

    private void UpdateSkirt()
    {
        if (_skirtPart == null) return;
        int n = _skN, rows = _skRows;
        var pm = Matrix3D.Identity; pm.Scale(Root.SegScale); pm.Append(Root.World);
        double world = new Vector3D(pm.M21, pm.M22, pm.M23).Length;
        double legK = 0.25 + 0.75 * this["hip_l"].SegScale.Y;
        double seg = _skLen * legK * world / rows;
        var cols = ClothColliders(0.008);
        Point3D Col(Point3D p) => CollideSeat(Collide(p, cols), 0.018);

        // Kräfte (wie bei den Haaren): Schwerkraft, Wind relativ zur Figur, Schwung nach oben/außen
        var fwd = Root.World.Transform(new Vector3D(0, 0, 1)); fwd.Y = 0;
        if (fwd.LengthSquared < 1e-9) fwd = new Vector3D(0, 0, 1);
        fwd.Normalize();
        var left = Vector3D.CrossProduct(new Vector3D(0, 1, 0), fwd);
        double wa = HairStyle.WindDirection * Math.PI / 180;
        var windDir = -fwd * Math.Cos(wa) + left * Math.Sin(wa);
        double wind = HairStyle.Wind / 100 * 1.1, lift = HairStyle.Lift / 100;
        var up = new Vector3D(0, 1, 0);
        var down = pm.Transform(new Vector3D(0, -1, 0)); down.Normalize();

        var P = new Point3D[n, rows + 1];
        var radial = new Vector3D[n];
        double r0 = 0;
        for (int j = 0; j < n; j++)
        {
            P[j, 0] = pm.Transform(_skRing[j]);
            radial[j] = pm.Transform(_skRadial[j]); radial[j].Normalize();
        }
        for (int j = 0; j < n; j++) r0 += (P[j, 0] - P[(j + n / 2) % n, 0]).Length / 2;
        r0 /= n;
        // Zuschnitt: größter Abstand benachbarter Bahnen je Reihe (Umfang des Kegels mit dem Zuschnittwinkel)
        var maxS = new double[rows + 1];
        for (int i = 0; i <= rows; i++) maxS[i] = 2 * Math.PI * (r0 + i * seg * Math.Sin(_skFlare)) / n;
        // Ein Bahnschritt von a in Richtung d: in zwei halben Schritten, jeweils aus dem Körper geschoben – so rutscht der
        // Stoff nicht durch dünne Stellen (Oberschenkel beim Sitzen), sondern legt sich darüber.
        var contact = new bool[n, rows + 1];
        Point3D Step(Point3D a, Vector3D d)
        {
            var q1 = Col(a + d * (seg * 0.5));
            var d2 = a + d * seg - q1;
            if (d2.LengthSquared < 1e-12) d2 = d;
            d2.Normalize();
            var q = Col(q1 + d2 * (seg * 0.5));
            var dd = q - a;
            if (dd.LengthSquared > 1e-12) { dd.Normalize(); q = Col(a + dd * seg); }
            if (q.Y < 0.003) q.Y = 0.003;
            return q;
        }
        // Start: Rock in Grundhaltung (Kegel im Becken-System), vorne und seitlich an die Oberschenkel gebunden –
        // so wandert der Stoff beim Sitzen oder Beinheben mit den Beinen nach vorne, statt zwischen ihnen durchzufallen.
        // Danach wird er über den Körper gelegt; das eigentliche Fallen übernimmt die Schleife unten.
        double segRest = _skLen * legK / rows;
        var sc = Root.SegScale;
        Point3D Scaled(Point3D p) => new(p.X * sc.X, p.Y * sc.Y, p.Z * sc.Z);
        var target = new Point3D[n, rows + 1]; var legW = new double[n, rows + 1];
        for (int j = 0; j < n; j++)
        {
            var dl = new Vector3D(0, -Math.Cos(_skFlare * 0.6), 0) + _skRadial[j] * Math.Sin(_skFlare * 0.6);
            for (int i = 1; i <= rows; i++)
            {
                var rest = _skRing[j] + dl * (segRest * i);           // Becken-System (unskaliert)
                var pw = Root.World.Transform(Scaled(rest));
                var sum = (Vector3D)pw; double wsum = 1;
                foreach (var (sfx, sx) in new[] { ("_l", 1.0), ("_r", -1.0) })
                {
                    var hipJ = this["hip" + sfx];
                    var hipRest = new Point3D(hipJ.Offset.X, hipJ.Offset.Y, hipJ.Offset.Z);   // Hüftgelenk im (skalierten) Becken-System
                    var v = Scaled(rest) - hipRest;
                    double below = Math.Clamp(-v.Y / 0.08, 0, 1);                              // erst unterhalb des Hüftgelenks
                    double near = Math.Clamp(1.4 - Math.Abs(v.X) / 0.12, 0, 1);                // nah am eigenen Bein
                    double front = Math.Clamp(0.5 + v.Z / 0.08, 0, 1);                         // nur vorne und seitlich
                    double wt = below * near * front * 2;
                    if (wt <= 0) continue;
                    sum += (Vector3D)hipJ.World.Transform((Point3D)v) * wt; wsum += wt;
                }
                P[j, i] = target[j, i] = (Point3D)(sum / wsum);
                legW[j, i] = Math.Min(1, (wsum - 1) / 1.5);
            }
            for (int i = 1; i <= rows; i++)
            {
                var d = P[j, i] - P[j, i - 1];
                if (d.LengthSquared < 1e-12) d = down;
                d.Normalize();
                P[j, i] = Step(P[j, i - 1], d);
            }
        }

        double h = seg * 0.25;
        for (int it = 0; it < 22; it++)
        {
            // 1. Kräfte
            for (int j = 0; j < n; j++)
            {
                double jit = 0.75 + 0.5 * ((j * 37 % 11) / 10.0);
                for (int i = 1; i <= rows; i++)
                {
                    double t = (double)i / rows;
                    var outward = P[j, i] - P[j, 0]; outward -= down * Vector3D.DotProduct(outward, down);
                    if (outward.LengthSquared > 1e-12) outward.Normalize(); else outward = radial[j];
                    var f = new Vector3D(0, -1 + 0.7 * legW[j, i], 0)
                            + outward * (0.55 * (1 - 0.3 * t))                      // Stoffsteifigkeit: Glockenform
                            + (windDir + up * 0.15) * wind * jit * (0.4 + t)
                            + (outward * 1.6 + up * 0.7) * lift * (0.3 + t);
                    // Haftreibung: aufliegender Stoff (z. B. auf den Oberschenkeln) rutscht nicht seitlich ab
                    P[j, i] += f * (contact[j, i] ? h * 0.15 : h);
                    // Formerinnerung: Stoff an den Beinen bleibt dort, wohin ihn die Beine mitgenommen haben (beim Sitzen auf dem Schoß);
                    // Wind und Schwung lösen ihn davon
                    double keep = legW[j, i] * 0.6 * Math.Clamp(1 - (wind + lift * 1.5) * 0.8, 0, 1);
                    if (keep > 0) P[j, i] += (target[j, i] - P[j, i]) * keep;
                }
            }
            // 2. Glätten entlang der Bahn (Biegesteifigkeit)
            for (int j = 0; j < n; j++)
                for (int i = 1; i < rows; i++)
                    P[j, i] = (Point3D)((Vector3D)P[j, i] * 0.7 + ((Vector3D)P[j, i - 1] + (Vector3D)P[j, i + 1]) * 0.15);
            // 3. Bahnen gleich lang halten, Körper und Boden ausweichen
            for (int j = 0; j < n; j++)
                for (int i = 1; i <= rows; i++)
                {
                    var d = P[j, i] - P[j, i - 1];
                    if (d.LengthSquared < 1e-12) d = down;
                    d.Normalize();
                    var before = P[j, i - 1] + d * seg;
                    P[j, i] = Step(P[j, i - 1], d);
                    contact[j, i] = (P[j, i] - before).LengthSquared > 1e-10;
                }
            // 4. Zuschnitt: Nachbarn nicht weiter auseinander als der Stoff erlaubt (zuletzt, damit der Stoff geschlossen bleibt)
            for (int pass = 0; pass < 6; pass++)
                for (int i = 1; i <= rows; i++)
                    for (int j = 0; j < n; j++)
                    {
                        int k = (j + 1) % n;
                        var d = P[k, i] - P[j, i];
                        double len = d.Length;
                        if (len <= maxS[i] || len < 1e-9) continue;
                        var corr = d * ((len - maxS[i]) / len / 2);
                        P[j, i] += corr; P[k, i] -= corr;
                    }
            for (int j = 0; j < n; j++)
                for (int i = 1; i <= rows; i++)
                {
                    var q = Col(P[j, i]);
                    if (q.Y < 0.003) q.Y = 0.003;
                    P[j, i] = q;
                }
        }

        // ---- Mesh: Punkte und Normalen
        int count = ((MeshGeometry3D)_skirtPart.Model.Geometry).Positions.Count;
        var pos = new Point3DCollection(count);
        var nrm = new Vector3DCollection(count);
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j < n; j++)
            {
                pos.Add(P[j, i]);
                var around = P[(j + 1) % n, i] - P[(j + n - 1) % n, i];
                var along = P[j, Math.Min(rows, i + 1)] - P[j, Math.Max(0, i - 1)];
                var nn = Vector3D.CrossProduct(along, around);
                if (nn.LengthSquared > 1e-12) nn.Normalize(); else nn = radial[j];
                nrm.Add(nn);
            }
        while (pos.Count < count) { pos.Add(pos[0]); nrm.Add(nrm[0]); }
        var mesh = (MeshGeometry3D)_skirtPart.Model.Geometry;
        mesh.Positions = pos;
        mesh.Normals = nrm;
    }
}
