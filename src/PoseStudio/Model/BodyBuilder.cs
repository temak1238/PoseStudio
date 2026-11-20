using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Erzeugt die Körperteile je Figurenstil. Alle Teile liegen im Koordinatensystem ihres Gelenks.
/// Skelett (Gelenk-Abstände) ist für alle Stile gleich, damit Posen beim Wechsel erhalten bleiben.</summary>
internal sealed class BodyBuilder
{
    /// <summary>Gelenk-Id, Mesh, Material, uniform skalieren (Kugeln/Kopf statt Strecken entlang Knochen).</summary>
    public delegate void AddPartFn(string jointId, MeshGeometry3D mesh, MaterialKind mat, bool uniform = false);

    private readonly AddPartFn _add;
    private readonly Proportions _shape;
    public BodyBuilder(AddPartFn add, Proportions? shape = null) { _add = add; _shape = shape ?? new Proportions(); }

    private static Point3D P(double x, double y, double z) => new(x, y, z);

    // ================================================================= Gliederpuppe

    public void BuildMannequin()
    {
        _add("pelvis", MeshFactory.Ellipsoid(P(0, -0.02, 0), 0.16, 0.11, 0.10), MaterialKind.Body);
        _add("spine", MeshFactory.Ellipsoid(P(0, 0.08, 0), 0.13, 0.11, 0.09), MaterialKind.Body);
        _add("chest", MeshFactory.Ellipsoid(P(0, 0.14, 0), 0.17, 0.16, 0.10), MaterialKind.Body);
        _add("neck", MeshFactory.Ellipsoid(P(0, 0.04, 0), 0.05, 0.07, 0.05), MaterialKind.Body);
        _add("head", MeshFactory.Ellipsoid(P(0, 0.11, 0.01), 0.085, 0.115, 0.10), MaterialKind.Body, true);
        _add("head", MeshFactory.Ellipsoid(P(0, 0.09, 0.105), 0.018, 0.026, 0.022), MaterialKind.Body, true);

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r";
            _add("shoulder" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.06), MaterialKind.Joint, true);
            _add("shoulder" + sfx, MeshFactory.Ellipsoid(P(0, -0.145, 0), 0.05, 0.15, 0.05), MaterialKind.Body);
            _add("elbow" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.045), MaterialKind.Joint, true);
            _add("elbow" + sfx, MeshFactory.Ellipsoid(P(0, -0.13, 0), 0.04, 0.14, 0.04), MaterialKind.Body);
            _add("wrist" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.032), MaterialKind.Joint, true);
            _add("wrist" + sfx, MeshFactory.Ellipsoid(P(0, -0.048, 0), 0.02, 0.05, 0.042), MaterialKind.Body, true);
            BuildFingers(s, 1.1);
            _add("hip" + sfx, MeshFactory.Ellipsoid(P(0, -0.22, 0), 0.075, 0.23, 0.075), MaterialKind.Body);
            _add("knee" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.058), MaterialKind.Joint, true);
            _add("knee" + sfx, MeshFactory.Ellipsoid(P(0, -0.21, 0), 0.055, 0.22, 0.055), MaterialKind.Body);
            _add("ankle" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.045), MaterialKind.Joint, true);
            _add("ankle" + sfx, MeshFactory.Ellipsoid(P(0, -0.045, 0.05), 0.05, 0.04, 0.12), MaterialKind.Body, true);
        }
    }

    // ================================================================= Mensch

    /// <summary>Größe der Hand (Handfläche im Mesh; Finger werden im Skelett mit demselben Faktor skaliert).</summary>
    public static double HandK(bool female, bool anime, Proportions? p = null) => (female ? 0.9 : 1.0) * (anime ? 0.92 : 1.0) * (p?.Hands ?? 100) / 100;

    /// <summary>Querschnitt eines Loft-Körpers: Höhe, halbe Breite, halbe Tiefe, Versatz nach vorne.</summary>
    public readonly record struct Sec(double Y, double Rx, double Rz, double Cz);

    // ---- Körperform-Regler: Querschnitte gezielt in einzelnen Höhenbereichen verändern

    /// <summary>Dreiecks-Gewicht: 1 bei c, 0 ab Abstand r.</summary>
    private static double Tri(double y, double c, double r) => Math.Max(0, 1 - Math.Abs(y - c) / r);

    /// <summary>Breite und Tiefe je Höhe um (1 + grow(y)) skalieren, Mitte um shift(y)·Tiefe nach vorne schieben.</summary>
    private static Sec[] Shape(Sec[] s, Func<double, double> grow, Func<double, double> shift) =>
        s.Select(q => { double g = 1 + grow(q.Y); return new Sec(q.Y, q.Rx * g, q.Rz * g, q.Cz + q.Rz * shift(q.Y)); }).ToArray();

    private static Sec[] ShapeTorso(Sec[] s, bool female, Proportions? p)
    {
        if (p == null) return s;
        double waist = p.Waist / 100 - 1, hips = p.Hips / 100 - 1, glutes = p.Glutes / 100 - 1, belly = p.Belly / 100 - 1, bust = p.Bust / 100 - 1;
        return s.Select(q =>
        {
            double y = q.Y;
            double wWaist = Tri(y, 0.19, 0.09), wHip = Tri(y, -0.035, 0.095), wGlute = Tri(y, -0.05, 0.065), wBelly = Tri(y, 0.14, 0.11);
            double wChest = female ? 0 : Tri(y, 0.41, 0.1); // Männer: Brustkorb tiefer
            double fx = 1 + waist * wWaist + hips * wHip;
            double fz = 1 + waist * wWaist + belly * wBelly + glutes * wGlute * 0.7 + bust * wChest * 0.35;
            double rz = q.Rz * fz;
            // Bauch wächst nach vorne, Gesäß nach hinten
            double cz = q.Cz + q.Rz * (belly * wBelly * 0.5 - glutes * wGlute * 0.45 + bust * wChest * 0.15);
            return new Sec(y, q.Rx * fx, rz, cz);
        }).ToArray();
    }

    /// <summary>Brust (Frau) als Ellipsoid je Seite: Größe nach Regler, Ansatz bleibt am Brustkorb.</summary>
    internal static (Point3D C, double Rx, double Ry, double Rz) Breast(int s, Proportions? p, double extra = 0)
    {
        double b = (p?.Bust ?? 100) / 100, k = 0.75 + 0.25 * b;
        double rz = 0.05 * b + extra;
        return (new Point3D(s * 0.066, 0.13 - 0.01 * (b - 1), 0.006 + 0.05 * b + extra * 0.4), 0.068 * k + extra, 0.07 * k + extra, rz);
    }

    /// <summary>Rumpf von Schritt bis Hals (Becken-System).</summary>
    internal static Sec[] TorsoSections(bool female, bool anime, Proportions? p = null) => ShapeTorso(TorsoBase(female, anime), female, p);

    private static Sec[] TorsoBase(bool female, bool anime)
    {
        double w = (female ? 0.9 : 1.0) * (anime ? 0.92 : 1.0);
        double hip = female ? 1.1 : 1.0, sh = female ? 0.92 : 1.0, waist = female ? 0.86 : 1.0, glute = female ? 1.08 : 1.0;
        // Unterleib geschlossen und glatt (Schaufensterpuppe): schmaler, flacher Schritt mit kleiner Kappe,
        // Gesäß als nach hinten verschobener, vollerer Querschnitt (bleibt beim Sitzen am Becken).
        return new Sec[]
        {
            new(-0.135, 0.03, 0.026, -0.004),
            new(-0.112, 0.072 * hip, 0.058, -0.012),
            new(-0.075, 0.138 * hip, 0.088 * glute, -0.022 * glute),
            new(-0.025, 0.157 * hip, 0.1 * glute, -0.02 * glute),
            new(0.025, 0.155 * hip, 0.095, -0.01),
            new(0.06, 0.145 * (female ? 1.02 : 1), 0.088, 0.005),
            new(0.12, 0.135 * (female ? 0.9 : 1), 0.085, 0.008),
            new(0.19, 0.13 * waist, 0.084, 0.008),
            new(0.25, 0.138 * (female ? 0.9 : 1), 0.088, 0.006),
            new(0.31, 0.155 * sh, 0.10, 0.005),
            new(0.40, 0.17 * sh, 0.105, 0.0),
            new(0.47, 0.18 * sh, 0.098, -0.005),
            new(0.51, 0.175 * sh, 0.085, -0.01),
            new(0.54, 0.14 * sh, 0.068, -0.01),
            new(0.565, 0.08 * sh, 0.056, -0.006),
            new(0.59, 0.052 * w, 0.05 * w, 0.0),
            new(0.65, 0.046 * w, 0.046 * w, 0.006),
            new(0.69, 0.045 * w, 0.045 * w, 0.012),
            // Anime: Hals reicht weiter in den (größeren) Kopf, damit unter dem Hinterkopf keine Lücke bleibt
            new(anime ? 0.72 : 0.70, 0.043 * w, 0.043 * w, anime ? 0.018 : 0.014),
        };
    }

    /// <summary>Ober- und Unterarm (Schulter-System, nach unten).</summary>
    internal static Sec[] ArmSections(double w, Proportions? p = null) => Shape(ArmBase(w), y => Tri(y, -0.1, 0.17) * ((p?.UpperArms ?? 100) / 100 - 1), y => 0);

    private static Sec[] ArmBase(double w) => new Sec[]
    {
        new(0.00, 0.05 * w, 0.05 * w, 0.0), new(-0.06, 0.052 * w, 0.05 * w, 0.003), new(-0.16, 0.046 * w, 0.047 * w, 0.005),
        new(-0.25, 0.039 * w, 0.04 * w, 0.0), new(-0.29, 0.036 * w, 0.036 * w, 0.0), new(-0.33, 0.041 * w, 0.038 * w, 0.0),
        new(-0.43, 0.035 * w, 0.031 * w, 0.0), new(-0.52, 0.026 * w, 0.021 * w, 0.0), new(-0.56, 0.024 * w, 0.019 * w, 0.0),
    };

    /// <summary>Bein mit Kniescheibe und Wade (Hüft-System, nach unten).</summary>
    internal static Sec[] LegSections(bool female, double w, Proportions? p = null) => Shape(LegBase(female, w),
        y => Tri(y, -0.13, 0.26) * ((p?.Thighs ?? 100) / 100 - 1) + Tri(y, -0.6, 0.16) * ((p?.Calves ?? 100) / 100 - 1), y => 0);

    private static Sec[] LegBase(bool female, double w) => new Sec[]
    {
        new(0.015, 0.066, 0.066, 0.0), new(-0.04, 0.086 * (female ? 1.04 : 1), 0.088, 0.005), new(-0.20, 0.075, 0.078, 0.008),
        new(-0.36, 0.058 * w, 0.06 * w, 0.005), new(-0.44, 0.051 * w, 0.055 * w, 0.006), new(-0.50, 0.05 * w, 0.055 * w, -0.008),
        new(-0.58, 0.052 * w, 0.06 * w, -0.015), new(-0.74, 0.038 * w, 0.04 * w, -0.005), new(-0.86, 0.03 * w, 0.03 * w, 0.0),
    };

    /// <summary>Fuß entlang Z (Knöchel-System): (z, halbe Breite, halbe Höhe, Versatz Y).</summary>
    internal static (double z, double rx, double ry, double cy)[] FootSections(double f) => new[]
    {
        (-0.05 * f, 0.032 * f, 0.03 * f, -0.04), (-0.01 * f, 0.04 * f, 0.04 * f, -0.035), (0.05 * f, 0.043 * f, 0.035 * f, -0.045),
        (0.11 * f, 0.047 * f, 0.025 * f, -0.055), (0.16 * f, 0.042 * f, 0.016 * f, -0.062),
    };

    internal static double LimbW(bool female, bool anime) => (female ? 0.9 : 1.0) * (anime ? 0.92 : 1.0);
    private static double FootK(bool female, bool anime) => (female ? 0.92 : 1.0) * (anime ? 0.92 : 1.0);

    private static MeshGeometry3D LoftOf(IEnumerable<Sec> s, double cap = 0.8) =>
        MeshFactory.LoftY(1.0, cap, s.Select(q => (q.Y, q.Rx, q.Rz, q.Cz)).ToArray());

    public void BuildHuman(bool female, bool hair, bool anime = false, bool gripL = false, bool gripR = false)
    {
        double w = LimbW(female, anime);

        // ---- Rumpf als eine durchgehende Form von Schritt bis Hals (im Becken-System).
        // Sie wird über die Kette Becken → Bauch → Brust → Hals → Kopf weich gebogen (Skinning in Mannequin).
        _add("pelvis", LoftOf(TorsoSections(female, anime, _shape), 0.35), MaterialKind.Body);
        if (female)
            foreach (var s in new[] { 1, -1 })
                { var br = Breast(s, _shape); _add("chest", MeshFactory.Ellipsoid(br.C, br.Rx, br.Ry, br.Rz), MaterialKind.Body); }
        if (anime) BuildAnimeHead(female, hair);
        else BuildHead(female, hair);

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r";
            // ---- Oberarm mit Schulter; Ober- und Unterarm als eine Form (Schulter → Ellbogen → Handgelenk)
            _add("shoulder" + sfx, MeshFactory.Ellipsoid(P(s * 0.002, -0.05, 0), 0.05 * w, 0.065, 0.054 * w), MaterialKind.Body);
            _add("shoulder" + sfx, LoftOf(ArmSections(w, _shape)), MaterialKind.Body);
            BuildHand("wrist" + sfx, s, HandK(female, anime, _shape));
            // ---- Bein als eine Form (Hüfte → Knie → Knöchel)
            _add("hip" + sfx, LoftOf(LegSections(female, w, _shape)), MaterialKind.Body);
            // ---- Fuß
            double f = FootK(female, anime);
            _add("ankle" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.027 * f), MaterialKind.Body, true);
            _add("ankle" + sfx, MeshFactory.LoftZ(FootSections(f)), MaterialKind.Body, true);
        }
    }

    // ================================================================= Kleidung (anliegend)

    /// <summary>Querschnitte zwischen y0 und y1 (Reihenfolge wie in der Liste), Enden linear interpoliert, um off
    /// (am Ende offEnd) aufgeweitet.</summary>
    internal static List<Sec> Clip(Sec[] s, double y0, double y1, double off, double offEnd = double.NaN)
    {
        double lo = Math.Min(y0, y1), hi = Math.Max(y0, y1);
        Sec At(double y)
        {
            for (int i = 0; i + 1 < s.Length; i++)
            {
                var a = s[i]; var b = s[i + 1];
                if ((y - a.Y) * (y - b.Y) <= 0 && a.Y != b.Y)
                {
                    double t = (y - a.Y) / (b.Y - a.Y);
                    return new Sec(y, a.Rx + (b.Rx - a.Rx) * t, a.Rz + (b.Rz - a.Rz) * t, a.Cz + (b.Cz - a.Cz) * t);
                }
            }
            return Math.Abs(y - s[0].Y) < Math.Abs(y - s[^1].Y) ? s[0] with { Y = y } : s[^1] with { Y = y };
        }
        bool asc = s[^1].Y > s[0].Y;
        var list = new List<Sec> { At(asc ? lo : hi) };
        list.AddRange(s.Where(q => q.Y > lo + 0.005 && q.Y < hi - 0.005));
        list.Add(At(asc ? hi : lo));
        if (double.IsNaN(offEnd)) offEnd = off;
        double first = list[0].Y, span = list[^1].Y - first;
        return list.Select(q =>
        {
            double t = span == 0 ? 0 : (q.Y - first) / span, o = off + (offEnd - off) * t;
            return new Sec(q.Y, q.Rx + o, q.Rz + o, q.Cz);
        }).ToList();
    }

    /// <summary>Anliegende Kleidung als leicht größere Hüllen um Rumpf, Arme, Beine und Füße. Sie hängen an denselben Gelenken
    /// wie der Körper und biegen sich daher mit (Skinning). Röcke und Kleider (fallender Stoff) berechnet Mannequin.</summary>
    public void BuildClothes(ClothingSettings c, bool female, bool anime)
    {
        var top = TopDef.Find(c.Top) ?? TopDef.All[0];
        var bottom = BottomDef.Find(c.Bottom) ?? BottomDef.All[0];
        var shoes = ShoeDef.Find(c.Shoes) ?? ShoeDef.All[0];
        var torso = TorsoSections(female, anime, _shape);
        double w = LimbW(female, anime);
        bool dress = bottom.Kind == BottomKind.Dress;
        // Kleid: Ausschnitt und Ärmel vom Oberteil (mindestens ärmellos), Farbe vom Kleid
        var bodice = dress && top.Id == "none" ? TopDef.Find("tanktop")! : top;
        var topKind = dress ? MaterialKind.ClothBottom : MaterialKind.ClothTop;

        if (bodice.Id != "none")
        {
            // Unterkante: Kleid ab Taille (dort setzt der Rock an), sonst bis über den Hosenbund
            double lower = dress ? 0.08 : 0.0;
            _add("pelvis", LoftOf(Clip(torso, lower, bodice.Upper, 0.006, 0.01), 0.05), topKind);
            if (female)
                foreach (var s in new[] { 1, -1 })
                { var br = Breast(s, _shape, 0.006); _add("chest", MeshFactory.Ellipsoid(br.C, br.Rx, br.Ry, br.Rz), topKind); }
            if (bodice.Sleeve < 0)
                foreach (var s in new[] { 1, -1 })
                {
                    string sfx = s > 0 ? "_l" : "_r";
                    _add("shoulder" + sfx, MeshFactory.Ellipsoid(P(s * 0.002, -0.045, 0), 0.05 * w + 0.013, 0.082, 0.054 * w + 0.013), topKind);
                    _add("shoulder" + sfx, LoftOf(Clip(ArmSections(w, _shape), 0.0, bodice.Sleeve, 0.009, bodice.Sleeve < -0.3 ? 0.006 : 0.011), 0.05), topKind);
                }
        }

        if (bottom.Kind == BottomKind.Pants)
        {
            _add("pelvis", LoftOf(Clip(torso, -0.135, 0.11, 0.007), 0.35), MaterialKind.ClothBottom);
            foreach (var sfx in new[] { "_l", "_r" })
                _add("hip" + sfx, LoftOf(Clip(LegSections(female, w, _shape), 0.015, bottom.Hem, 0.008, bottom.Hem < -0.5 ? 0.014 : 0.012), 0.05), MaterialKind.ClothBottom);
        }

        if (shoes.Id != "none")
        {
            double f = FootK(female, anime), o = shoes.Offset;
            foreach (var sfx in new[] { "_l", "_r" })
            {
                if (shoes.Shaft < 0)
                    _add("hip" + sfx, LoftOf(Clip(LegSections(female, w, _shape), shoes.Shaft, -0.86, o), 0.05), MaterialKind.Shoes);
                _add("ankle" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.027 * f + o), MaterialKind.Shoes, true);
                var foot = FootSections(f).Select(q => (z: q.z, rx: q.rx + o, ry: q.ry + o + (shoes.Sole ? 0.003 : 0), cy: q.cy - (shoes.Sole ? 0.004 : 0))).ToList();
                if (shoes.Sole) { var first = foot[0]; foot.Insert(0, (first.z - 0.012, first.rx * 0.9, first.ry * 0.8, first.cy)); }
                _add("ankle" + sfx, MeshFactory.LoftZ(foot.ToArray()), MaterialKind.Shoes, true);
            }
        }
    }

    private static MeshGeometry3D Loft(params (double y, double rx, double rz, double cz)[] p) => MeshFactory.LoftY(1.0, p);

    private void BuildHead(bool female, bool hair)
    {
        const string H = "head";
        double k = female ? 0.95 : 1.0;
        MeshGeometry3D E(double x, double y, double z, double rx, double ry, double rz) =>
            MeshFactory.Ellipsoid(P(x * k, y * k, z * k), rx * k, ry * k, rz * k, 28, 18);

        _add(H, E(0, 0.125, -0.005, 0.079, 0.1, 0.098), MaterialKind.Body, true);               // Schädel
        _add(H, E(0, 0.07, 0.025, female ? 0.058 : 0.064, 0.07, 0.07), MaterialKind.Body, true); // Kiefer/Wangen
        _add(H, E(0, 0.032, 0.058, female ? 0.024 : 0.03, 0.024, 0.024), MaterialKind.Body, true); // Kinn
        foreach (var s in new[] { 1, -1 })
        {
            _add(H, E(s * 0.031, 0.118, 0.075, 0.0135, 0.011, 0.0135), MaterialKind.EyeWhite, true);
            _add(H, E(s * 0.031, 0.118, 0.0865, 0.0062, 0.0062, 0.003), MaterialKind.Iris, true);
            _add(H, E(s * 0.032, 0.134, 0.088, 0.019, female ? 0.0035 : 0.0045, 0.007), MaterialKind.Hair, true); // Augenbraue
            _add(H, E(s * 0.0095, 0.08, 0.095, 0.0065, 0.006, 0.007), MaterialKind.Body, true); // Nasenflügel
            _add(H, E(s * 0.079, 0.11, -0.005, 0.011, 0.026, 0.016), MaterialKind.Body, true);  // Ohr
        }
        _add(H, E(0, 0.1, 0.09, 0.0095, 0.027, 0.016), MaterialKind.Body, true);    // Nasenrücken
        _add(H, E(0, 0.083, 0.1, 0.0115, 0.0105, 0.012), MaterialKind.Body, true);  // Nasenspitze
        _add(H, E(0, 0.06, 0.088, 0.022, female ? 0.0075 : 0.0065, 0.01), MaterialKind.Lips, true);  // Oberlippe
        _add(H, E(0, 0.049, 0.086, 0.019, female ? 0.008 : 0.007, 0.01), MaterialKind.Lips, true);   // Unterlippe

    }

    // ================================================================= Anime-Kopf

    /// <summary>Wie weit der Anime-Kopf unter den Kopf-Drehpunkt gesetzt ist (unskaliert).</summary>
    public const double AnimeHeadDrop = 0.05;

    /// <summary>Kopfhaut als Ellipsoid im Kopf-System (vor der Kopfskalierung): Ansatz der Haare und Kollision.</summary>
    public static (Point3D C, Vector3D R) Scalp(bool female, bool anime)
    {
        if (anime)
        {
            double k = female ? 0.96 : 1.0;
            return (new Point3D(0, (0.142 - AnimeHeadDrop) * k, -0.004 * k), new Vector3D(0.085 * k, 0.104 * k, 0.093 * k));
        }
        double h = female ? 0.95 : 1.0;
        return (new Point3D(0, 0.125 * h, -0.006 * h), new Vector3D(0.08 * h, 0.1 * h, 0.099 * h));
    }

    /// <summary>Anime-Kopf: großer runder Schädel, schmales Gesicht mit kleinem spitzem Kinn, keine ausmodellierte Nase.
    /// Augen, Brauen und Mund liegen als flache, der Oberfläche angepasste Flächen auf dem Gesicht.</summary>
    private void BuildAnimeHead(bool female, bool hair)
    {
        const string H = "head";
        double k = female ? 0.96 : 1.0;
        // Der ganze Kopf sitzt tiefer auf dem Drehpunkt (etwa Höhe Mund statt Kinnunterkante): kürzerer sichtbarer Hals,
        // und beim Nicken/Drehen schwingt der große Kopf nicht weit aus.
        var drop = new TranslateTransform3D(0, -AnimeHeadDrop * k, 0);
        void Add(MeshGeometry3D m, MaterialKind kind, bool uniform)
        {
            var c = m.Clone();
            var pts = new Point3DCollection(c.Positions.Count);
            foreach (var p in c.Positions) pts.Add(drop.Transform(p));
            c.Positions = pts;
            _add(H, c, kind, uniform);
        }
        // Querschnitte (Höhe, halbe Breite, halbe Tiefe, Versatz nach vorne)
        var head = MeshFactory.LoftY(k, 0.55,
            (0.034, 0.010, 0.010, 0.052),
            (0.046, 0.030, 0.028, 0.036),
            (0.066, 0.052, 0.050, 0.022),
            (0.090, 0.067, 0.072, 0.012),
            (0.115, 0.077, 0.085, 0.005),
            (0.145, 0.083, 0.090, -0.001),
            (0.175, 0.083, 0.088, -0.005),
            (0.203, 0.073, 0.077, -0.007),
            (0.226, 0.050, 0.054, -0.007),
            (0.238, 0.020, 0.022, -0.006));
        Add(head, MaterialKind.Body, true);
        foreach (var s in new[] { 1, -1 })
            Add(MeshFactory.Ellipsoid(P(s * 0.074 * k, 0.112 * k, -0.012 * k), 0.009 * k, 0.019 * k, 0.013 * k, 16, 10), MaterialKind.Body, true); // Ohr

        var face = new Decals(head);
        foreach (var s in new[] { 1, -1 })
        {
            double ex = s * 0.031 * k, ey = 0.113 * k, eh = female ? 1.0 : 0.85; // Männer: etwas schmalere Augen
            Add(face.Ellipse(ex, ey, 0.0125 * k, 0.0145 * k * eh, 0, 0.0005), MaterialKind.EyeWhite, true);          // Augenweiß
            Add(face.Ellipse(ex - s * 0.0012 * k, ey - 0.0012 * k, 0.0094 * k, 0.0128 * k * eh, 0, 0.0009), MaterialKind.Iris, true); // Iris
            Add(face.Ellipse(ex + s * 0.0035 * k, ey + 0.0055 * k, 0.0026 * k, 0.0034 * k, 0, 0.0013), MaterialKind.EyeWhite, true); // Glanzpunkt
            // Obere Wimpernlinie: kräftiger Bogen, außen dicker und leicht nach außen gezogen
            Add(face.Arc(ex, ey - 0.001 * k, 0.0142 * k, 0.0158 * k * eh, s > 0 ? 8 : 172, s > 0 ? 172 : 8, 0.0032 * k, 0.0014 * k, 0.0012), MaterialKind.Line, true);
            // Untere Linie: kurzer, feiner Strich außen
            Add(face.Arc(ex, ey + 0.001 * k, 0.0128 * k, 0.0150 * k * eh, s > 0 ? -20 : 200, s > 0 ? -60 : 240, 0.0012 * k, 0.0006 * k, 0.0012), MaterialKind.Line, true);
            // Augenbraue: dünner, flacher Bogen in Haarfarbe
            Add(face.Arc(s * 0.033 * k, 0.128 * k, 0.017 * k, 0.007 * k, s > 0 ? 25 : 155, s > 0 ? 155 : 25, 0.0018 * k, 0.0009 * k, 0.0008), MaterialKind.Hair, true);
        }
        // Mund: kleiner, leicht lächelnder Strich
        Add(face.Arc(0, 0.068 * k, 0.0085 * k, 0.0035 * k, 200, 340, 0.0013 * k, 0.0008 * k, 0.0008), MaterialKind.Mouth, true);
        // Nase: nur eine winzige Erhebung (wirft im Cel-Shading einen kleinen Schatten)
        if (face.Hit(0, 0.088 * k) is { } nose)
            Add(MeshFactory.Ellipsoid(nose.P - nose.N * 0.001, 0.0032 * k, 0.0055 * k, 0.0035 * k, 14, 8), MaterialKind.Body, true);

    }

    /// <summary>Flache Gesichtsdetails, die sich der Kopfoberfläche anpassen: Jeder Punkt wird entlang der Flächennormale
    /// auf das Kopf-Mesh projiziert und minimal angehoben (Ebenen über die Höhe gestaffelt).</summary>
    private sealed class Decals
    {
        private readonly Point3D[] _p;
        private readonly int[] _t;
        public Decals(MeshGeometry3D mesh) { _p = mesh.Positions.ToArray(); _t = mesh.TriangleIndices.ToArray(); }

        /// <summary>Strahl von vorne (+Z) auf den Punkt (x, y): Treffer und geglättete Normale.</summary>
        public (Point3D P, Vector3D N)? Hit(double x, double y)
        {
            Point3D? H(double hx, double hy) => Cast(new Point3D(hx, hy, 1), new Vector3D(0, 0, -1));
            if (H(x, y) is not { } c) return null;
            const double d = 0.003;
            if (H(x + d, y) is not { } px || H(x - d, y) is not { } mx || H(x, y + d) is not { } py || H(x, y - d) is not { } my) return null;
            var n = Vector3D.CrossProduct(px - mx, py - my);
            n.Normalize();
            if (n.Z < 0) n = -n;
            return (c, n);
        }

        /// <summary>Nächster Schnittpunkt eines Strahls mit dem Mesh (Möller-Trumbore).</summary>
        private Point3D? Cast(Point3D o, Vector3D dir)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 2 < _t.Length; i += 3)
            {
                var a = _p[_t[i]]; var e1 = _p[_t[i + 1]] - a; var e2 = _p[_t[i + 2]] - a;
                var h = Vector3D.CrossProduct(dir, e2);
                double det = Vector3D.DotProduct(e1, h);
                if (Math.Abs(det) < 1e-12) continue;
                double f = 1 / det;
                var s = o - a;
                double u = f * Vector3D.DotProduct(s, h);
                if (u < 0 || u > 1) continue;
                var q = Vector3D.CrossProduct(s, e1);
                double v = f * Vector3D.DotProduct(dir, q);
                if (v < 0 || u + v > 1) continue;
                double t = f * Vector3D.DotProduct(e2, q);
                if (t > 0 && t < best) best = t;
            }
            return best < double.MaxValue ? o + dir * best : null;
        }

        /// <summary>Gefüllte Ellipse um (x, y), Halbachsen rx/ry, gedreht um rot Grad; lift = Abstand über der Haut.</summary>
        public MeshGeometry3D Ellipse(double x, double y, double rx, double ry, double rot, double lift)
        {
            const int seg = 28, rings = 4;
            var local = new List<(double u, double v)> { (0, 0) };
            for (int r = 1; r <= rings; r++)
                for (int j = 0; j < seg; j++)
                {
                    double a = 2 * Math.PI * j / seg, f = (double)r / rings;
                    local.Add((rx * f * Math.Cos(a), ry * f * Math.Sin(a)));
                }
            var tri = new List<int>();
            for (int j = 0; j < seg; j++) tri.AddRange(new[] { 0, 1 + j, 1 + (j + 1) % seg });
            for (int r = 1; r < rings; r++)
                for (int j = 0; j < seg; j++)
                {
                    int a = 1 + (r - 1) * seg + j, b = 1 + (r - 1) * seg + (j + 1) % seg;
                    int c = 1 + r * seg + j, d = 1 + r * seg + (j + 1) % seg;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                }
            return Project(x, y, rot, lift, local, tri);
        }

        /// <summary>Bogen auf der Ellipse (rx, ry) um (x, y) von Winkel a0 bis a1 (Grad, 0 = rechts/+X, 90 = oben);
        /// Strichstärke w0 am Anfang, w1 am Ende, an beiden Enden spitz auslaufend.</summary>
        public MeshGeometry3D Arc(double x, double y, double rx, double ry, double a0, double a1, double w0, double w1, double lift)
        {
            const int seg = 24;
            var local = new List<(double u, double v)>();
            for (int i = 0; i <= seg; i++)
            {
                double t = (double)i / seg, a = (a0 + (a1 - a0) * t) * Math.PI / 180;
                double w = (w0 + (w1 - w0) * t) * Math.Pow(Math.Sin(Math.PI * Math.Clamp(t * 0.9 + 0.05, 0, 1)), 0.6);
                var c = (u: rx * Math.Cos(a), v: ry * Math.Sin(a));
                var n = (u: Math.Cos(a) / rx, v: Math.Sin(a) / ry);
                double len = Math.Sqrt(n.u * n.u + n.v * n.v);
                n = (n.u / len, n.v / len);
                local.Add((c.u + n.u * w / 2, c.v + n.v * w / 2));
                local.Add((c.u - n.u * w / 2, c.v - n.v * w / 2));
            }
            var tri = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                int a = 2 * i, b = a + 1, c = a + 2, d = a + 3;
                tri.AddRange(new[] { a, b, c, c, b, d });
            }
            return Project(x, y, 0, lift, local, tri);
        }

        private MeshGeometry3D Project(double x, double y, double rot, double lift, List<(double u, double v)> local, List<int> tri)
        {
            var (c, n) = Hit(x, y) ?? (new Point3D(x, y, 0.09), new Vector3D(0, 0, 1));
            var eu = Vector3D.CrossProduct(new Vector3D(0, 1, 0), n); eu.Normalize();
            var ev = Vector3D.CrossProduct(n, eu); ev.Normalize();
            double cr = Math.Cos(rot * Math.PI / 180), sr = Math.Sin(rot * Math.PI / 180);
            var mesh = new MeshGeometry3D();
            foreach (var (u0, v0) in local)
            {
                double u = u0 * cr - v0 * sr, v = u0 * sr + v0 * cr;
                var q = c + eu * u + ev * v;
                var p = Cast(q + n * 0.02, -n) ?? q;
                mesh.Positions.Add(p + n * lift);
                mesh.Normals.Add(n);
            }
            // Dreiecke so ausrichten, dass die Vorderseite nach außen (n) zeigt
            var pts = mesh.Positions;
            for (int i = 0; i + 2 < tri.Count; i += 3)
            {
                var fn = Vector3D.CrossProduct(pts[tri[i + 1]] - pts[tri[i]], pts[tri[i + 2]] - pts[tri[i]]);
                if (Vector3D.DotProduct(fn, n) < 0) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
                mesh.TriangleIndices.Add(tri[i]); mesh.TriangleIndices.Add(tri[i + 1]); mesh.TriangleIndices.Add(tri[i + 2]);
            }
            mesh.Freeze();
            return mesh;
        }
    }

    private void BuildHand(string joint, int s, double k)
    {
        // Handfläche zeigt zum Körper (-s·X), Daumen nach vorne (+Z). Finger hängen an eigenen Gelenken.
        _add(joint, MeshFactory.Sphere(P(0, 0, 0), 0.021 * k), MaterialKind.Body, true);
        _add(joint, MeshFactory.LoftY(k,
            (0.0, 0.018, 0.03, 0.0),
            (-0.03, 0.017, 0.04, 0.002),
            (-0.07, 0.015, 0.042, 0.0),
            (-0.09, 0.014, 0.039, 0.0)), MaterialKind.Body, true);
        // Daumenballen
        _add(joint, MeshFactory.Ellipsoid(P(-s * 0.008 * k, -0.035 * k, 0.022 * k), 0.013 * k, 0.026 * k, 0.016 * k, 16, 10), MaterialKind.Body, true);
        BuildFingers(s, 1.0);
    }

    /// <summary>Fingerglieder als Röhren mit runden Enden an den Fingergelenken (unskaliert, Skalierung über das Gelenk).</summary>
    public void BuildFingers(int s, double thick)
    {
        string sfx = s > 0 ? "_l" : "_r";
        for (int f = 1; f <= 4; f++)
        {
            int i = f - 1;
            double r = HandGeometry.Radius[i] * thick;
            for (int k = 0; k < 3; k++)
            {
                double len = HandGeometry.Length[i] * HandGeometry.SegFrac[k];
                double r0 = r * (1 - 0.08 * k), r1 = r * (0.92 - 0.08 * k);
                _add($"{HandGeometry.Ids[f]}{k + 1}{sfx}", MeshFactory.Tube(P(0, 0, 0), P(0, -len, 0), r0, r1, 10), MaterialKind.Body, true);
            }
        }
        for (int k = 0; k < 3; k++)
        {
            double len = HandGeometry.ThumbLen[k];
            double r0 = HandGeometry.ThumbRad[k] * thick, r1 = (k < 2 ? HandGeometry.ThumbRad[k + 1] : HandGeometry.ThumbRad[k] * 0.85) * thick;
            _add($"thumb{k + 1}{sfx}", MeshFactory.Tube(P(0, 0, 0), P(0, -len, 0), r0, r1, 10), MaterialKind.Body, true);
        }
    }
}
