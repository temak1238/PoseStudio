using System.Windows.Media.Media3D;

namespace PoseStudio.Rendering;

/// <summary>Hauptlicht (Sonne/Studiolampe) mit Schattenwurf.
/// Azimut 0 = Licht kommt von vorne (+Z), 90 = von der linken Seite der Figur (+X).</summary>
public sealed class LightSettings
{
    public double Azimuth { get; set; } = 35;
    public double Elevation { get; set; } = 50;
    public double Intensity { get; set; } = 100;    // %
    public double Ambient { get; set; } = 35;       // %
    public bool FollowCamera { get; set; } = false;
    public bool CastShadow { get; set; } = true;
    public double ShadowStrength { get; set; } = 55; // %
    public double Softness { get; set; } = 30;       // %
    public bool ShowSource { get; set; } = true;

    public LightSettings Clone() => (LightSettings)MemberwiseClone();

    public void Clamp()
    {
        Elevation = Math.Clamp(Elevation, 5, 90);
        Intensity = Math.Clamp(Intensity, 0, 200);
        Ambient = Math.Clamp(Ambient, 0, 100);
        ShadowStrength = Math.Clamp(ShadowStrength, 0, 100);
        Softness = Math.Clamp(Softness, 0, 100);
    }

    /// <summary>Richtung zur Lichtquelle (Einheitsvektor, zeigt vom Objekt zum Licht).</summary>
    public Vector3D FromDirection(OrbitCamera cam)
    {
        double az = (Azimuth + (FollowCamera ? cam.Yaw : 0)) * Math.PI / 180;
        double el = Math.Clamp(Elevation, 5, 90) * Math.PI / 180;
        var v = new Vector3D(Math.Cos(el) * Math.Sin(az), Math.Sin(el), Math.Cos(el) * Math.Cos(az));
        v.Normalize();
        return v;
    }
}
