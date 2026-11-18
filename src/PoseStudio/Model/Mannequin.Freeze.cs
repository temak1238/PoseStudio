using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Haare und Stoff einfrieren: Statt neu zu rechnen, werden die zuletzt berechneten Punkte starr mit Kopf bzw. Becken
/// mitgeführt (ein paar Tausend Punkte transformieren statt Strähnen und Stoffbahnen auszugleichen).</summary>
public sealed partial class Mannequin
{
    private sealed class Frozen
    {
        public required Point3D[] Pos { get; init; }
        public required Vector3D[] Nrm { get; init; }
    }

    private Frozen? _hairFrozen, _skirtFrozen;
    private bool _forceDynamics;

    /// <summary>Haare und Röcke/Kleider nicht neu berechnen, sondern starr mitbewegen (z. B. während des Ziehens).</summary>
    public bool FreezeDynamics { get; set; }

    /// <summary>Haare und Stoff jetzt neu berechnen, auch wenn sie eingefroren sind.</summary>
    public void RecomputeDynamics()
    {
        _forceDynamics = true;
        try { Update(); }
        finally { _forceDynamics = false; }
    }

    /// <summary>Eingefrorenen Stand anwenden (true) oder false, wenn neu gerechnet werden muss.</summary>
    private bool ApplyFrozen(Part part, Frozen? f, Matrix3D toWorld)
    {
        if (!FreezeDynamics || _forceDynamics || f == null) return false;
        var mesh = (MeshGeometry3D)part.Model.Geometry;
        if (mesh.Positions.Count != f.Pos.Length) return false;
        var pos = new Point3DCollection(f.Pos.Length);
        var nrm = new Vector3DCollection(f.Nrm.Length);
        for (int i = 0; i < f.Pos.Length; i++)
        {
            pos.Add(toWorld.Transform(f.Pos[i]));
            var n = toWorld.Transform(f.Nrm[i]);
            if (n.LengthSquared > 1e-12) n.Normalize();
            nrm.Add(n);
        }
        mesh.Positions = pos;
        mesh.Normals = nrm;
        return true;
    }

    /// <summary>Berechneten Stand im System von Kopf bzw. Becken merken.</summary>
    private static Frozen? Capture(MeshGeometry3D mesh, Matrix3D toWorld)
    {
        var inv = toWorld;
        if (!inv.HasInverse) return null;
        inv.Invert();
        return new Frozen
        {
            Pos = mesh.Positions.Select(p => inv.Transform(p)).ToArray(),
            Nrm = mesh.Normals.Select(n => inv.Transform(n)).ToArray(),
        };
    }
}
