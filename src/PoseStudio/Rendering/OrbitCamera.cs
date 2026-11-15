using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Kamera, die um einen Blickpunkt kreist. Yaw 0 = Kamera vor der Figur (+Z).</summary>
public sealed class OrbitCamera
{
    public Point3D Target { get; set; } = new(0, 0.95, 0);
    public double Yaw { get; set; } = 0;          // Grad
    public double Pitch { get; set; } = 5;        // Grad
    public double Distance { get; set; } = 4.5;   // Meter (ganze Figur bei 50 mm)
    public double FocalLength { get; set; } = 50; // mm (Kleinbild-äquivalent)

    public const double MinDistance = 0.4, MaxDistance = 20;

    /// <summary>Vertikaler Bildwinkel aus der Brennweite (Sensorhöhe 24 mm).</summary>
    public double VerticalFov => 2 * Math.Atan(12.0 / FocalLength) * 180 / Math.PI;

    public Point3D Position
    {
        get
        {
            double y = Yaw * Math.PI / 180, p = Pitch * Math.PI / 180;
            var dir = new Vector3D(Math.Cos(p) * Math.Sin(y), Math.Sin(p), Math.Cos(p) * Math.Cos(y));
            return Target + dir * Distance;
        }
    }

    public Vector3D LookDirection
    {
        get { var v = Target - Position; v.Normalize(); return v; }
    }

    public Vector3D Right
    {
        get { var r = Vector3D.CrossProduct(LookDirection, new Vector3D(0, 1, 0)); r.Normalize(); return r; }
    }

    public Vector3D Up
    {
        get { var u = Vector3D.CrossProduct(Right, LookDirection); u.Normalize(); return u; }
    }

    /// <summary>WPF-Kamera für ein Bildverhältnis. WPF erwartet den horizontalen Bildwinkel;
    /// der vertikale bleibt konstant, damit die Figur in Quer- und Hochformat gleich groß erscheint.</summary>
    public PerspectiveCamera CreateCamera(double aspect)
    {
        double vf = VerticalFov * Math.PI / 180;
        double hf = 2 * Math.Atan(Math.Tan(vf / 2) * aspect) * 180 / Math.PI;
        return new PerspectiveCamera(Position, LookDirection * Distance, new Vector3D(0, 1, 0), hf)
        {
            NearPlaneDistance = 0.05,
            FarPlaneDistance = 200
        };
    }

    public void ApplyTo(PerspectiveCamera cam, double aspect)
    {
        var c = CreateCamera(aspect);
        cam.Position = c.Position;
        cam.LookDirection = c.LookDirection;
        cam.UpDirection = c.UpDirection;
        cam.FieldOfView = c.FieldOfView;
        cam.NearPlaneDistance = c.NearPlaneDistance;
        cam.FarPlaneDistance = c.FarPlaneDistance;
    }

    public CameraData ToData() => new()
    {
        Yaw = Yaw, Pitch = Pitch, Distance = Distance, FocalLength = FocalLength,
        Target = new[] { Target.X, Target.Y, Target.Z }
    };

    public void FromData(CameraData d)
    {
        Yaw = d.Yaw;
        Pitch = Math.Clamp(d.Pitch, -89, 89);
        Distance = Math.Clamp(d.Distance, MinDistance, MaxDistance);
        FocalLength = Math.Clamp(d.FocalLength <= 0 ? 50 : d.FocalLength, 14, 200);
        if (d.Target is { Length: >= 3 }) Target = new Point3D(d.Target[0], d.Target[1], d.Target[2]);
    }
}
