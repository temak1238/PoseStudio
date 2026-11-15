using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

public static class MeshFactory
{
    /// <summary>Ellipsoid mit Mittelpunkt c und Radien rx/ry/rz, Dreiecke gegen den Uhrzeigersinn (Außenseite).</summary>
    public static MeshGeometry3D Ellipsoid(Point3D c, double rx, double ry, double rz, int slices = 36, int stacks = 22)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= stacks; i++)
        {
            double phi = Math.PI * i / stacks;
            double y = Math.Cos(phi), r = Math.Sin(phi);
            for (int j = 0; j <= slices; j++)
            {
                double th = 2 * Math.PI * j / slices;
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
