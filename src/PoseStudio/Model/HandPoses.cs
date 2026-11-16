using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Maße der Hand (unskaliert, im System des Handgelenks: Finger zeigen nach −Y, Handfläche zur Körpermitte (−s·X),
/// Daumen nach vorne (+Z)). Gemeinsam genutzt von Skelett (Mannequin) und Körperteilen (BodyBuilder).</summary>
internal static class HandGeometry
{
    public static readonly string[] Ids = { "thumb", "index", "middle", "ring", "pinky" };
    public static readonly string[] Names = { "Daumen", "Zeigefinger", "Mittelfinger", "Ringfinger", "Kleiner Finger" };

    /// <summary>Lage der Fingergrundgelenke quer zur Hand (Zeige- bis kleiner Finger).</summary>
    public static readonly double[] BaseZ = { 0.027, 0.009, -0.009, -0.026 };
    public static readonly double[] BaseY = { -0.084, -0.086, -0.085, -0.081 };
    /// <summary>Fingerlänge ab Grundgelenk.</summary>
    public static readonly double[] Length = { 0.077, 0.085, 0.080, 0.064 };
    public static readonly double[] Radius = { 0.0092, 0.0095, 0.009, 0.0078 };
    /// <summary>Anteile Grund-, Mittel-, Endglied.</summary>
    public static readonly double[] SegFrac = { 0.45, 0.30, 0.25 };
    /// <summary>Fächer in Ruhe (Grad, + = Richtung Daumen).</summary>
    public static readonly double[] Fan = { 5, 1, -3, -8 };

    public static readonly double[] ThumbLen = { 0.044, 0.032, 0.027 };
    public static readonly double[] ThumbRad = { 0.0135, 0.0108, 0.0095 };

    public static Vector3D ThumbBase(int s) => new(-s * 0.007, -0.022, 0.026);

    /// <summary>Ruhe-Ausrichtung des Daumens (Mittelhandknochen): Zeilen = lokale Achsen im Handgelenk-System.
    /// Lokales −Y = Knochenrichtung, Beugen (Drehung um lokales Z) führt die Spitze Richtung Handfläche/Finger.</summary>
    public static Matrix3D ThumbRest(int s)
    {
        var d = new Vector3D(-s * 0.016, -0.032, 0.021); d.Normalize();
        var f = new Vector3D(-s * 0.75, 0, -1);
        f -= d * Vector3D.DotProduct(f, d); f.Normalize();
        var x = -s * f; var y = -d;
        var z = Vector3D.CrossProduct(x, y);
        return new Matrix3D(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>Griff (Mittelpunkt der Faust) und Radius der Fingerbahn um den Griff.</summary>
    public static Point3D Grip(int s) => new(-s * 0.03, -0.092, 0);
    public const double GripRadius = 0.027;
}

/// <summary>Handhaltung: Krümmung je Finger in Prozent (0 = entspannt, 100 = ganz eingerollt, negativ = gestreckt),
/// Daumen abspreizen und Finger spreizen in Prozent.</summary>
public sealed record HandGesture(string Name, double Thumb, double ThumbSpread, double Index, double Middle, double Ring, double Pinky, double Spread)
{
    public override string ToString() => Name;

    public double Curl(int finger) => finger switch { 0 => Thumb, 1 => Index, 2 => Middle, 3 => Ring, _ => Pinky };

    public static IReadOnlyList<HandGesture> All { get; } = new List<HandGesture>
    {
        new("Entspannt", 0, 0, 0, 0, 0, 0, 0),
        new("Flach (gestreckt)", -15, 10, -19, -19, -19, -19, 0),
        new("Gespreizt", -20, 35, -19, -19, -19, -19, 100),
        new("Leicht gekrümmt", 15, 0, 15, 20, 25, 30, 10),
        new("Kralle", 40, 20, 60, 60, 60, 60, 70),
        new("Faust", 85, 10, 100, 100, 100, 100, 0),
        new("Zeigen", 80, 10, -19, 100, 100, 100, 0),
        new("Peace / Victory", 85, 10, -19, -19, 100, 100, 90),
        new("Daumen hoch", -20, 20, 100, 100, 100, 100, 0),
        new("Hörner", 85, 10, -19, 100, 100, -19, 40),
        new("Pistole", -15, 40, -19, -19, 100, 100, 0),
        new("Drei Finger", 85, 10, -19, -19, -19, 100, 60),
        new("Locker greifen", 25, 0, 30, 35, 40, 45, 0),
    };
}
