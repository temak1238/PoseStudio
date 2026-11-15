using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Eine Drehachse eines Gelenks. Sign wandelt den angezeigten Wert in den echten Drehwinkel um,
/// damit positive Werte immer die "natürliche" Richtung bedeuten (z. B. Arm nach vorne heben).</summary>
public sealed record AxisDef(string Label, double Min, double Max, double Sign);

public sealed class Joint
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public Joint? Parent { get; init; }
    public Vector3D Offset { get; init; }
    public AxisDef? Bend { get; init; }
    public AxisDef? Side { get; init; }
    public AxisDef? Twist { get; init; }
    public string? MirrorId { get; init; }

    public double BendValue { get; set; }
    public double SideValue { get; set; }
    public double TwistValue { get; set; }

    public Matrix3D World { get; internal set; } = Matrix3D.Identity;
    internal List<GeometryModel3D> Parts { get; } = new();

    public int Depth => Parent == null ? 0 : Parent.Depth + 1;
    public string DisplayName => new string(' ', Depth * 2) + Name;
    public override string ToString() => Name;
}

/// <summary>Posierbare Gliederpuppe. Koordinaten in Metern, Y nach oben, Figur schaut nach +Z.
/// Links der Figur = +X.</summary>
public sealed class Mannequin
{
    public const double BaseHipHeight = 1.0;

    private readonly Dictionary<GeometryModel3D, Joint> _partToJoint = new();
    private readonly Dictionary<string, Joint> _byId = new();
    private readonly SolidColorBrush _bodyBrush = new(Color.FromRgb(0xC9, 0xB2, 0x92));
    private readonly SolidColorBrush _jointBrush = new(Color.FromRgb(0xA8, 0x8E, 0x6C));
    private readonly Material _bodyMat;
    private readonly Material _jointMat;
    private readonly Material _highlightMat;
    private readonly HashSet<GeometryModel3D> _balls = new();
    private Joint? _selected;

    public List<Joint> Joints { get; } = new();
    public Model3DGroup Group { get; } = new();
    public Vector3D RootPosition { get; set; }
    public Joint Root => Joints[0];
    public Joint? Selected => _selected;

    public Mannequin()
    {
        _bodyMat = MakeMaterial(_bodyBrush);
        _jointMat = MakeMaterial(_jointBrush);
        var hl = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x1A));
        hl.Freeze();
        _highlightMat = MakeMaterial(hl);
        Build();
        Update();
    }

    private static Material MakeMaterial(Brush brush)
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(brush));
        g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 40));
        return g;
    }

    public Color BodyColor
    {
        get => _bodyBrush.Color;
        set
        {
            _bodyBrush.Color = value;
            // Gelenkkugeln etwas dunkler, damit die Gliederung erkennbar bleibt.
            _jointBrush.Color = Color.FromRgb((byte)(value.R * 0.82), (byte)(value.G * 0.82), (byte)(value.B * 0.82));
        }
    }

    public Joint this[string id] => _byId[id];
    public bool TryGetJoint(GeometryModel3D part, out Joint joint) => _partToJoint.TryGetValue(part, out joint!);

    // ------------------------------------------------------------------ Aufbau

    private Joint AddJoint(string id, string name, Joint? parent, Vector3D offset,
        AxisDef? bend, AxisDef? side, AxisDef? twist, string? mirror = null)
    {
        var j = new Joint { Id = id, Name = name, Parent = parent, Offset = offset, Bend = bend, Side = side, Twist = twist, MirrorId = mirror };
        Joints.Add(j);
        _byId[id] = j;
        return j;
    }

    private void AddPart(Joint j, MeshGeometry3D mesh, bool ball = false)
    {
        var gm = new GeometryModel3D(mesh, ball ? _jointMat : _bodyMat)
        {
            BackMaterial = null,
            Transform = new MatrixTransform3D(Matrix3D.Identity)
        };
        j.Parts.Add(gm);
        _partToJoint[gm] = j;
        if (ball) _balls.Add(gm);
        Group.Children.Add(gm);
    }

    private static Point3D P(double x, double y, double z) => new(x, y, z);

    private void Build()
    {
        var pelvis = AddJoint("pelvis", "Becken (ganzer Körper)", null, new Vector3D(0, BaseHipHeight, 0),
            new AxisDef("Vor/zurück neigen", -90, 90, 1),
            new AxisDef("Seitlich neigen", -90, 90, -1),
            new AxisDef("Drehen", -180, 180, 1));
        AddPart(pelvis, MeshFactory.Ellipsoid(P(0, -0.02, 0), 0.16, 0.11, 0.10));

        var spine = AddJoint("spine", "Bauch", pelvis, new Vector3D(0, 0.08, 0),
            new AxisDef("Vor/zurück beugen", -30, 70, 1),
            new AxisDef("Seitlich beugen", -40, 40, -1),
            new AxisDef("Drehen", -45, 45, 1));
        AddPart(spine, MeshFactory.Ellipsoid(P(0, 0.08, 0), 0.13, 0.11, 0.09));

        var chest = AddJoint("chest", "Brust", spine, new Vector3D(0, 0.17, 0),
            new AxisDef("Vor/zurück beugen", -30, 45, 1),
            new AxisDef("Seitlich beugen", -30, 30, -1),
            new AxisDef("Drehen", -40, 40, 1));
        AddPart(chest, MeshFactory.Ellipsoid(P(0, 0.14, 0), 0.17, 0.16, 0.10));

        var neck = AddJoint("neck", "Hals", chest, new Vector3D(0, 0.29, 0),
            new AxisDef("Vor/zurück", -40, 60, 1),
            new AxisDef("Seitlich", -40, 40, -1),
            new AxisDef("Drehen", -60, 60, 1));
        AddPart(neck, MeshFactory.Ellipsoid(P(0, 0.04, 0), 0.05, 0.07, 0.05));

        var head = AddJoint("head", "Kopf", neck, new Vector3D(0, 0.09, 0),
            new AxisDef("Nicken", -40, 40, 1),
            new AxisDef("Seitlich neigen", -30, 30, -1),
            new AxisDef("Drehen", -70, 70, 1));
        AddPart(head, MeshFactory.Ellipsoid(P(0, 0.11, 0.01), 0.085, 0.115, 0.10));
        AddPart(head, MeshFactory.Ellipsoid(P(0, 0.09, 0.105), 0.018, 0.026, 0.022)); // Nase = Blickrichtung

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r", other = s > 0 ? "_r" : "_l", side = s > 0 ? "links" : "rechts";

            var sh = AddJoint("shoulder" + sfx, "Oberarm " + side, chest, new Vector3D(s * 0.19, 0.25, 0),
                new AxisDef("Vor/zurück heben", -60, 180, -1),
                new AxisDef("Seitlich heben", -30, 180, s),
                new AxisDef("Drehen", -90, 90, s), "shoulder" + other);
            AddPart(sh, MeshFactory.Sphere(P(0, 0, 0), 0.06), true);
            AddPart(sh, MeshFactory.Ellipsoid(P(0, -0.145, 0), 0.05, 0.15, 0.05));

            var el = AddJoint("elbow" + sfx, "Unterarm " + side, sh, new Vector3D(0, -0.29, 0),
                new AxisDef("Ellbogen beugen", 0, 150, -1),
                null,
                new AxisDef("Drehen", -90, 90, s), "elbow" + other);
            AddPart(el, MeshFactory.Sphere(P(0, 0, 0), 0.045), true);
            AddPart(el, MeshFactory.Ellipsoid(P(0, -0.13, 0), 0.04, 0.14, 0.04));

            var wr = AddJoint("wrist" + sfx, "Hand " + side, el, new Vector3D(0, -0.27, 0),
                new AxisDef("Beugen", -80, 80, -1),
                new AxisDef("Seitlich", -30, 30, s),
                null, "wrist" + other);
            AddPart(wr, MeshFactory.Ellipsoid(P(0, -0.085, 0), 0.022, 0.085, 0.045));
            AddPart(wr, MeshFactory.Ellipsoid(P(-s * 0.006, -0.045, 0.042), 0.016, 0.036, 0.016)); // Daumen
        }

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r", other = s > 0 ? "_r" : "_l", side = s > 0 ? "links" : "rechts";

            var hip = AddJoint("hip" + sfx, "Oberschenkel " + side, pelvis, new Vector3D(s * 0.09, -0.05, 0),
                new AxisDef("Vor/zurück heben", -30, 130, -1),
                new AxisDef("Seitlich spreizen", -30, 90, s),
                new AxisDef("Drehen", -45, 45, s), "hip" + other);
            AddPart(hip, MeshFactory.Ellipsoid(P(0, -0.22, 0), 0.075, 0.23, 0.075));

            var knee = AddJoint("knee" + sfx, "Unterschenkel " + side, hip, new Vector3D(0, -0.44, 0),
                new AxisDef("Knie beugen", 0, 150, 1),
                null,
                new AxisDef("Drehen", -30, 30, s), "knee" + other);
            AddPart(knee, MeshFactory.Sphere(P(0, 0, 0), 0.058), true);
            AddPart(knee, MeshFactory.Ellipsoid(P(0, -0.21, 0), 0.055, 0.22, 0.055));

            var ankle = AddJoint("ankle" + sfx, "Fuß " + side, knee, new Vector3D(0, -0.43, 0),
                new AxisDef("Heben/senken", -50, 40, -1),
                new AxisDef("Seitlich kippen", -30, 30, s),
                null, "ankle" + other);
            AddPart(ankle, MeshFactory.Sphere(P(0, 0, 0), 0.045), true);
            AddPart(ankle, MeshFactory.Ellipsoid(P(0, -0.045, 0.05), 0.05, 0.04, 0.12));
        }
    }

    // ------------------------------------------------------------------ Kinematik

    private static Matrix3D LocalMatrix(Joint j, Vector3D offset)
    {
        var m = Matrix3D.Identity;
        // Reihenfolge (Zeilenvektoren): erst um Knochenachse drehen, dann seitlich, dann beugen, dann versetzen.
        if (j.Twist != null && j.TwistValue != 0) m.Rotate(new Quaternion(new Vector3D(0, 1, 0), j.Twist.Sign * j.TwistValue));
        if (j.Side != null && j.SideValue != 0) m.Rotate(new Quaternion(new Vector3D(0, 0, 1), j.Side.Sign * j.SideValue));
        if (j.Bend != null && j.BendValue != 0) m.Rotate(new Quaternion(new Vector3D(1, 0, 0), j.Bend.Sign * j.BendValue));
        m.Translate(offset);
        return m;
    }

    public void Update()
    {
        foreach (var j in Joints)
        {
            var offset = j.Parent == null ? j.Offset + RootPosition : j.Offset;
            var local = LocalMatrix(j, offset);
            j.World = j.Parent == null ? local : local * j.Parent.World;
            foreach (var p in j.Parts)
                ((MatrixTransform3D)p.Transform).Matrix = j.World;
        }
    }

    public double ComputeMinY()
    {
        double min = double.MaxValue;
        foreach (var j in Joints)
            foreach (var p in j.Parts)
            {
                var mesh = (MeshGeometry3D)p.Geometry;
                var w = j.World;
                foreach (var pt in mesh.Positions)
                {
                    double y = w.Transform(pt).Y;
                    if (y < min) min = y;
                }
            }
        return min;
    }

    /// <summary>Verschiebt die Figur senkrecht, so dass der tiefste Punkt genau auf dem Boden (y = 0) steht.</summary>
    public void GroundToFloor()
    {
        double minY = ComputeMinY();
        RootPosition = new Vector3D(RootPosition.X, RootPosition.Y - minY, RootPosition.Z);
        Update();
    }

    public Point3D PelvisWorld => Root.World.Transform(new Point3D(0, 0, 0));

    // ------------------------------------------------------------------ Auswahl

    public void Select(Joint? joint)
    {
        if (_selected != null)
            foreach (var p in _selected.Parts) p.Material = _balls.Contains(p) ? _jointMat : _bodyMat;
        _selected = joint;
        if (_selected != null)
            foreach (var p in _selected.Parts) p.Material = _highlightMat;
    }

    /// <summary>Tiefe Kopie der Figur ohne Auswahl-Markierung (für den Export).</summary>
    public Model3DGroup CreateSnapshot()
    {
        var sel = _selected;
        Select(null);
        var clone = Group.Clone();
        Select(sel);
        return clone;
    }

    // ------------------------------------------------------------------ Posen

    public PoseData GetPose()
    {
        var pose = new PoseData { RootPosition = new[] { RootPosition.X, RootPosition.Y, RootPosition.Z } };
        foreach (var j in Joints)
            pose.Joints[j.Id] = new[] { j.BendValue, j.SideValue, j.TwistValue };
        return pose;
    }

    public void SetPose(PoseData pose)
    {
        foreach (var j in Joints)
        {
            if (pose.Joints.TryGetValue(j.Id, out var v) && v.Length >= 3)
            {
                j.BendValue = Clamp(j.Bend, v[0]);
                j.SideValue = Clamp(j.Side, v[1]);
                j.TwistValue = Clamp(j.Twist, v[2]);
            }
            else
            {
                j.BendValue = j.SideValue = j.TwistValue = 0;
            }
        }
        var r = pose.RootPosition;
        RootPosition = r is { Length: >= 3 } ? new Vector3D(r[0], r[1], r[2]) : new Vector3D();
        Update();
    }

    private static double Clamp(AxisDef? a, double v) => a == null ? 0 : Math.Clamp(v, a.Min, a.Max);

    public void ResetJoint(Joint j)
    {
        j.BendValue = j.SideValue = j.TwistValue = 0;
        Update();
    }

    public void ResetAll()
    {
        foreach (var j in Joints) j.BendValue = j.SideValue = j.TwistValue = 0;
        RootPosition = new Vector3D();
        Update();
    }

    /// <summary>Spiegelt die Pose links/rechts.</summary>
    public void Mirror()
    {
        var pose = GetPose();
        foreach (var j in Joints)
        {
            if (j.MirrorId != null)
            {
                var v = pose.Joints[j.MirrorId];
                j.BendValue = v[0]; j.SideValue = v[1]; j.TwistValue = v[2];
            }
            else
            {
                j.SideValue = -j.SideValue;
                j.TwistValue = -j.TwistValue;
            }
        }
        RootPosition = new Vector3D(-RootPosition.X, RootPosition.Y, RootPosition.Z);
        Update();
    }
}
