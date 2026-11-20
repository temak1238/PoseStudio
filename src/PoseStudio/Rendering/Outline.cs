using System.Windows.Media;
using System.Windows.Media.Media3D;
using PoseStudio.Model;

namespace PoseStudio.Rendering;

/// <summary>Dunkle Konturen für das Cel-Shading ("Inverted Hull"): Jede Form wird entlang ihrer Normalen leicht
/// aufgebläht und nur von innen (Rückseiten) dunkel gezeichnet. Sichtbar bleibt davon nur ein Rand um die Silhouette
/// und dort, wo ein Körperteil vor einem anderen liegt. Die Stärke wächst mit der Entfernung, damit die Linie im Bild
/// überall gleich breit ist.</summary>
public static class Outline
{
    private static readonly Material Ink = CreateInk();

    private static Material CreateInk()
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(Brushes.Black));
        g.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x24, 0x1C, 0x1E))));
        g.Freeze();
        return g;
    }

    /// <summary>Konturen aller Figuren; null, wenn Cel-Shading aus ist oder die Stärke 0 beträgt.</summary>
    public static Model3DGroup? Create(IEnumerable<Mannequin> figures, OrbitCamera cam, LightSettings light)
    {
        if (!light.Toon || light.OutlineWidth <= 0) return null;
        // Breite pro Meter Abstand: Promille der Bildhöhe × sichtbare Höhe je Meter Abstand
        // Strichzeichnung: Linien etwas kräftiger, da sie dort das einzige Gestaltungsmittel sind
        double perMeter = light.OutlineWidth * (light.ToonStyle == 2 ? 1.4 : 1.0) / 1000 * 2 * Math.Tan(cam.VerticalFov * Math.PI / 360);
        var camPos = cam.Position; var look = cam.LookDirection;
        var group = new Model3DGroup();
        foreach (var (mesh, m) in figures.SelectMany(f => f.OutlineMeshes()))
        {
            int n = mesh.Positions.Count - Mannequin.DummyVertices;
            var hull = new MeshGeometry3D();
            var pts = new Point3DCollection(n);
            for (int i = 0; i < n; i++)
            {
                var p = m.Transform(mesh.Positions[i]);
                var nrm = m.Transform(mesh.Normals[i]);
                if (nrm.LengthSquared > 1e-12) nrm.Normalize();
                double dist = Math.Max(0.1, Vector3D.DotProduct(p - camPos, look));
                pts.Add(p + nrm * (dist * perMeter));
            }
            hull.Positions = pts;
            var idx = new Int32Collection(mesh.TriangleIndices.Count);
            foreach (var i in mesh.TriangleIndices) if (i < n) idx.Add(i); else { idx = null; break; }
            if (idx == null) continue;
            hull.TriangleIndices = idx;
            hull.Freeze();
            group.Children.Add(new GeometryModel3D(hull, null) { BackMaterial = Ink });
        }
        return group;
    }
}
