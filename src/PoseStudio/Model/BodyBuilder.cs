using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Erzeugt die Körperteile je Figurenstil. Alle Teile liegen im Koordinatensystem ihres Gelenks.
/// Skelett (Gelenk-Abstände) ist für alle Stile gleich, damit Posen beim Wechsel erhalten bleiben.</summary>
internal sealed class BodyBuilder
{
    /// <summary>Gelenk-Id, Mesh, Material, uniform skalieren (Kugeln/Kopf statt Strecken entlang Knochen).</summary>
    public delegate void AddPartFn(string jointId, MeshGeometry3D mesh, MaterialKind mat, bool uniform = false);

    private readonly AddPartFn _add;
    public BodyBuilder(AddPartFn add) => _add = add;

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

    public void BuildHuman(bool female, bool hair, bool gripL = false, bool gripR = false)
    {
        double w = female ? 0.9 : 1.0;   // Gliedmaßen schlanker
        double hip = female ? 1.1 : 1.0; // Becken breiter
        double sh = female ? 0.92 : 1.0; // Brustkorb schmaler

        // ---- Rumpf als eine durchgehende Form von Schritt bis Hals (im Becken-System).
        // Sie wird über die Kette Becken → Bauch → Brust → Hals → Kopf weich gebogen (Skinning in Mannequin).
        double waist = female ? 0.86 : 1.0;
        _add("pelvis", Loft(
            (-0.115, 0.085 * hip, 0.064, 0.0),
            (-0.07, 0.15 * hip, 0.092, -0.005),
            (0.00, 0.158 * hip, 0.096, 0.0),
            (0.06, 0.145 * (female ? 1.02 : 1), 0.088, 0.005),
            (0.12, 0.135 * (female ? 0.9 : 1), 0.085, 0.008),
            (0.19, 0.13 * waist, 0.084, 0.008),
            (0.25, 0.138 * (female ? 0.9 : 1), 0.088, 0.006),
            (0.31, 0.155 * sh, 0.10, 0.005),
            (0.40, 0.17 * sh, 0.105, 0.0),
            (0.47, 0.18 * sh, 0.098, -0.005),
            (0.51, 0.175 * sh, 0.085, -0.01),
            (0.54, 0.14 * sh, 0.068, -0.01),
            (0.565, 0.08 * sh, 0.056, -0.006),
            (0.59, 0.052 * w, 0.05 * w, 0.0),
            (0.65, 0.046 * w, 0.046 * w, 0.006),
            (0.69, 0.045 * w, 0.045 * w, 0.012)), MaterialKind.Body);
        // Gesäß hängt am Oberschenkel (geht beim Sitzen anteilig mit, siehe Skinning)
        foreach (var s in new[] { 1, -1 })
            _add("hip" + (s > 0 ? "_l" : "_r"), MeshFactory.Ellipsoid(P(s * (0.058 * hip - 0.09), -0.015, -0.045), 0.066 * hip, 0.082, 0.07 * (female ? 1.08 : 1)), MaterialKind.Body);
        if (female)
            foreach (var s in new[] { 1, -1 })
                _add("chest", MeshFactory.Ellipsoid(P(s * 0.066, 0.13, 0.056), 0.068, 0.07, 0.05), MaterialKind.Body);
        BuildHead(female, hair);

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r";

            // ---- Oberarm mit Schulter
            _add("shoulder" + sfx, MeshFactory.Ellipsoid(P(s * 0.002, -0.05, 0), 0.05 * w, 0.065, 0.054 * w), MaterialKind.Body);
            // Ober- und Unterarm als eine Form (Schulter → Ellbogen → Handgelenk)
            _add("shoulder" + sfx, Loft(
                (0.00, 0.05 * w, 0.05 * w, 0.0),
                (-0.06, 0.052 * w, 0.05 * w, 0.003),
                (-0.16, 0.046 * w, 0.047 * w, 0.005),
                (-0.25, 0.039 * w, 0.04 * w, 0.0),
                (-0.29, 0.036 * w, 0.036 * w, 0.0),
                (-0.33, 0.041 * w, 0.038 * w, 0.0),
                (-0.43, 0.035 * w, 0.031 * w, 0.0),
                (-0.52, 0.026 * w, 0.021 * w, 0.0),
                (-0.56, 0.024 * w, 0.019 * w, 0.0)), MaterialKind.Body);
            BuildHand("wrist" + sfx, s, female ? 0.9 : 1.0);

            // ---- Bein als eine Form (Hüfte → Knie → Knöchel), mit Kniescheibe und Wade
            _add("hip" + sfx, Loft(
                (0.015, 0.066, 0.066, 0.0),
                (-0.04, 0.086 * (female ? 1.04 : 1), 0.088, 0.005),
                (-0.20, 0.075, 0.078, 0.008),
                (-0.36, 0.058 * w, 0.06 * w, 0.005),
                (-0.44, 0.051 * w, 0.055 * w, 0.006),
                (-0.50, 0.05 * w, 0.055 * w, -0.008),
                (-0.58, 0.052 * w, 0.06 * w, -0.015),
                (-0.74, 0.038 * w, 0.04 * w, -0.005),
                (-0.86, 0.03 * w, 0.03 * w, 0.0)), MaterialKind.Body);
            // ---- Fuß
            double f = female ? 0.92 : 1.0;
            _add("ankle" + sfx, MeshFactory.Sphere(P(0, 0, 0), 0.027 * f), MaterialKind.Body, true);
            _add("ankle" + sfx, MeshFactory.LoftZ(
                (-0.05 * f, 0.032 * f, 0.03 * f, -0.04),
                (-0.01 * f, 0.04 * f, 0.04 * f, -0.035),
                (0.05 * f, 0.043 * f, 0.035 * f, -0.045),
                (0.11 * f, 0.047 * f, 0.025 * f, -0.055),
                (0.16 * f, 0.042 * f, 0.016 * f, -0.062)), MaterialKind.Body, true);
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

        if (!hair) return;
        // Haarkappe: vorne bis zum Haaransatz, hinten tiefer
        double front = 58 * Math.PI / 180, back = (female ? 150 : 115) * Math.PI / 180;
        _add(H, MeshFactory.Ellipsoid(P(0, 0.128 * k, -0.01 * k), 0.086 * k, 0.107 * k, 0.105 * k, 40, 24,
            th => front + (back - front) * (1 - Math.Cos(th)) / 2), MaterialKind.Hair, true);
        if (female)
            _add(H, MeshFactory.LoftY(k,
                (0.13, 0.082, 0.06, -0.035),
                (0.05, 0.088, 0.055, -0.05),
                (-0.03, 0.082, 0.04, -0.055),
                (-0.08, 0.07, 0.028, -0.055)), MaterialKind.Hair, true);
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
