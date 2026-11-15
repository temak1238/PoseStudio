using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

public enum PropType
{
    None,
    Sword,
    Wand,
    Stick,
    Staff,
    Spear
}

public enum HoldMode
{
    Right,
    Left,
    BothRightTop,
    BothLeftTop
}

/// <summary>Gehaltenes Objekt. Position/Drehung beziehen sich auf den Brustkorb (das Objekt bewegt sich mit dem Oberkörper).
/// Der Griffpunkt der Haupthand liegt im Objekt-Ursprung, das Objekt zeigt entlang +Y (Spitze oben).</summary>
public sealed class PropSettings
{
    public PropType Type { get; set; } = PropType.None;
    public HoldMode Hold { get; set; } = HoldMode.Right;
    /// <summary>Position relativ zum Brustkorb in cm: X = zur linken Seite der Figur, Y = hoch, Z = nach vorne.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Spitze nach vorne kippen (°).</summary>
    public double Pitch { get; set; }
    /// <summary>Seitlich kippen (°), positiv = Spitze zur linken Seite der Figur.</summary>
    public double Tilt { get; set; }
    /// <summary>Um die Senkrechte drehen (°).</summary>
    public double Yaw { get; set; }
    /// <summary>Um die eigene Längsachse drehen (°), z. B. Schneide eines Schwerts.</summary>
    public double Roll { get; set; }
    /// <summary>Abstand der beiden Hände beim beidhändigen Halten (cm).</summary>
    public double GripSpacing { get; set; } = 25;

    public bool Active => Type != PropType.None;
    public bool TwoHanded => Hold is HoldMode.BothRightTop or HoldMode.BothLeftTop;
    /// <summary>Seite der Haupthand: +1 links, -1 rechts.</summary>
    public int MainSide => Hold is HoldMode.Left or HoldMode.BothLeftTop ? 1 : -1;

    public PropSettings Clone() => (PropSettings)MemberwiseClone();

    public void Clamp()
    {
        X = Math.Clamp(X, -120, 120); Y = Math.Clamp(Y, -150, 120); Z = Math.Clamp(Z, -100, 120);
        Pitch = Math.Clamp(Pitch, -180, 180); Tilt = Math.Clamp(Tilt, -90, 90);
        Yaw = Math.Clamp(Yaw, -180, 180); Roll = Math.Clamp(Roll, -180, 180);
        GripSpacing = Math.Clamp(GripSpacing, 8, 80);
    }

    /// <summary>Sinnvolle Grundhaltung je Objekt und Haltung (für rechts; links wird gespiegelt).</summary>
    public void ApplyDefaultPlacement()
    {
        (X, Y, Z, Pitch, Tilt, Yaw, Roll, GripSpacing) = (Type, TwoHanded) switch
        {
            (PropType.Sword, false) => (-20, -5, 35, 55, 0, 0, 0, 12),
            (PropType.Sword, true) => (-4, -10, 38, 55, 0, 0, 0, 11),
            (PropType.Wand, _) => (-22, 2, 38, 70, 0, 0, 0, 10),
            (PropType.Stick, false) => (-30, -28, 18, 8, 0, 0, 0, 25),
            (PropType.Stick, true) => (-4, -15, 34, 20, 0, 0, 0, 18),
            (PropType.Staff, false) => (-30, -12, 14, 0, 0, 0, 0, 30),
            (PropType.Staff, true) => (-6, -2, 34, 18, -12, 0, 0, 32),
            (PropType.Spear, false) => (-28, -10, 20, 5, 0, 0, 0, 30),
            (PropType.Spear, true) => (-6, -15, 36, 65, 0, 0, 0, 35),
            _ => (0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 25.0),
        };
        if (MainSide > 0) { X = -X; Tilt = -Tilt; Yaw = -Yaw; }
    }

    /// <summary>Lage des Objekts relativ zum Brustkorb (ohne Brustkorb-Transformation).</summary>
    public Matrix3D LocalMatrix()
    {
        var m = Matrix3D.Identity;
        if (Roll != 0) m.Rotate(new Quaternion(new Vector3D(0, 1, 0), Roll));
        if (Pitch != 0) m.Rotate(new Quaternion(new Vector3D(1, 0, 0), Pitch));
        if (Tilt != 0) m.Rotate(new Quaternion(new Vector3D(0, 0, 1), -Tilt));
        if (Yaw != 0) m.Rotate(new Quaternion(new Vector3D(0, 1, 0), Yaw));
        m.Translate(new Vector3D(X / 100, Y / 100, Z / 100));
        return m;
    }
}

/// <summary>Einfache Objektformen. Griff (Haupthand) im Ursprung, Objekt entlang +Y.</summary>
internal static class PropBuilder
{
    public static IEnumerable<(MeshGeometry3D Mesh, MaterialKind Kind)> Build(PropType type)
    {
        static Point3D P(double y) => new(0, y, 0);
        switch (type)
        {
            case PropType.Sword:
                yield return (MeshFactory.Tube(P(-0.13), P(0.1), 0.016, 0.015), MaterialKind.PropWood);           // Griff
                yield return (MeshFactory.Sphere(P(-0.145), 0.026), MaterialKind.PropGold);                       // Knauf
                yield return (MeshFactory.Ellipsoid(P(0.115), 0.1, 0.014, 0.015), MaterialKind.PropGold);         // Parierstange
                yield return (MeshFactory.Loft(new List<Ring>                                                       // Klinge
                {
                    new(P(0.12), new Vector3D(0.024, 0, 0), new Vector3D(0, 0, 0.006)),
                    new(P(0.5), new Vector3D(0.021, 0, 0), new Vector3D(0, 0, 0.0055)),
                    new(P(0.86), new Vector3D(0.015, 0, 0), new Vector3D(0, 0, 0.004)),
                    new(P(0.97), new Vector3D(0.002, 0, 0), new Vector3D(0, 0, 0.002)),
                }, 12, 0.3), MaterialKind.PropMetal);
                break;
            case PropType.Wand:
                yield return (MeshFactory.Tube(P(-0.12), P(0.08), 0.012, 0.01), MaterialKind.PropWood);
                yield return (MeshFactory.Tube(P(0.08), P(0.27), 0.009, 0.004), MaterialKind.PropWood);
                yield return (MeshFactory.Sphere(P(0.285), 0.012), MaterialKind.PropGem);
                yield return (MeshFactory.Sphere(P(-0.125), 0.015), MaterialKind.PropGold);
                break;
            case PropType.Stick:
                yield return (MeshFactory.Tube(P(-0.78), P(0.12), 0.013, 0.016), MaterialKind.PropWood);
                yield return (MeshFactory.Ellipsoid(P(0.13), 0.03, 0.025, 0.03), MaterialKind.PropWood);    // Knauf
                break;
            case PropType.Staff:
                yield return (MeshFactory.Tube(P(-1.05), P(0.75), 0.017, 0.02), MaterialKind.PropWood);
                yield return (MeshFactory.Ellipsoid(P(0.78), 0.035, 0.045, 0.035), MaterialKind.PropWood);
                yield return (MeshFactory.Sphere(P(0.84), 0.028), MaterialKind.PropGem);
                break;
            case PropType.Spear:
                yield return (MeshFactory.Tube(P(-1.25), P(0.75), 0.015, 0.014), MaterialKind.PropWood);
                yield return (MeshFactory.Loft(new List<Ring>
                {
                    new(P(0.74), new Vector3D(0.016, 0, 0), new Vector3D(0, 0, 0.016)),
                    new(P(0.8), new Vector3D(0.03, 0, 0), new Vector3D(0, 0, 0.007)),
                    new(P(0.97), new Vector3D(0.002, 0, 0), new Vector3D(0, 0, 0.002)),
                }, 12, 0.3), MaterialKind.PropMetal);
                break;
        }
    }
}
