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
    /// <summary>Abstand zum Elterngelenk bei Standard-Proportionen.</summary>
    public Vector3D BaseOffset { get; init; }
    public AxisDef? Bend { get; init; }
    public AxisDef? Side { get; init; }
    public AxisDef? Twist { get; init; }
    public string? MirrorId { get; init; }

    public double BendValue { get; set; }
    public double SideValue { get; set; }
    public double TwistValue { get; set; }

    /// <summary>Tatsächlicher Abstand zum Elterngelenk (mit Proportionen).</summary>
    public Vector3D Offset { get; internal set; }
    /// <summary>Skalierung der Körperteile entlang/quer zum Knochen.</summary>
    internal Vector3D SegScale { get; set; } = new(1, 1, 1);
    /// <summary>Gleichmäßige Skalierung für Kugeln, Kopf, Hände, Füße.</summary>
    internal double UniformScale { get; set; } = 1;

    public Matrix3D World { get; internal set; } = Matrix3D.Identity;
    internal List<Part> Parts { get; } = new();

    public int Depth => Parent == null ? 0 : Parent.Depth + 1;
    public string DisplayName => new string(' ', Depth * 2) + Name;
    public override string ToString() => Name;
}

internal sealed record Part(GeometryModel3D Model, Joint Joint, MaterialKind Kind, bool Uniform);

/// <summary>Posierbare Figur (Gliederpuppe oder Mensch). Koordinaten in Metern, Y nach oben,
/// Figur schaut nach +Z. Links der Figur = +X.</summary>
public sealed class Mannequin
{
    private readonly Dictionary<GeometryModel3D, Part> _parts = new();
    private readonly Dictionary<string, Joint> _byId = new();
    private readonly Dictionary<MaterialKind, Material> _mats = new();
    private readonly SolidColorBrush _bodyBrush = new(Color.FromRgb(0xC9, 0xB2, 0x92));
    private readonly SolidColorBrush _jointBrush = new();
    private readonly SolidColorBrush _lipsBrush = new();
    private readonly SolidColorBrush _hairBrush = new(Color.FromRgb(0x3A, 0x2A, 0x20));
    private readonly Material _highlightMat;
    private Joint? _selected;
    private double _globalScale = 1;

    public List<Joint> Joints { get; } = new();
    public Model3DGroup Group { get; } = new();
    public Vector3D RootPosition { get; set; }
    public Joint Root => Joints[0];
    public Joint? Selected => _selected;

    public FigureStyle Style { get; private set; } = FigureStyle.Mannequin;
    public bool Hair { get; private set; } = true;
    public Proportions Proportions { get; private set; } = new();

    public Mannequin()
    {
        _mats[MaterialKind.Body] = MakeMaterial(_bodyBrush, 0x40);
        _mats[MaterialKind.Joint] = MakeMaterial(_jointBrush, 0x40);
        _mats[MaterialKind.Lips] = MakeMaterial(_lipsBrush, 0x30);
        _mats[MaterialKind.EyeWhite] = MakeMaterial(Frozen(Color.FromRgb(0xF4, 0xF1, 0xEC)), 0x70);
        _mats[MaterialKind.Iris] = MakeMaterial(Frozen(Color.FromRgb(0x3B, 0x2C, 0x22)), 0x90);
        _mats[MaterialKind.Hair] = MakeMaterial(_hairBrush, 0x20);
        _highlightMat = MakeMaterial(Frozen(Color.FromRgb(0xFF, 0x8C, 0x1A)), 0x40);
        BodyColor = _bodyBrush.Color;
        BuildSkeleton();
        BuildParts();
        UpdateScales();
        Update();
    }

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Material MakeMaterial(Brush brush, byte specular)
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(brush));
        g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(specular, 0xFF, 0xFF, 0xFF)), 40));
        return g;
    }

    public Color BodyColor
    {
        get => _bodyBrush.Color;
        set
        {
            _bodyBrush.Color = value;
            // Gelenkkugeln etwas dunkler, damit die Gliederung erkennbar bleibt; Lippen rötlicher.
            _jointBrush.Color = Color.FromRgb((byte)(value.R * 0.82), (byte)(value.G * 0.82), (byte)(value.B * 0.82));
            _lipsBrush.Color = Color.FromRgb((byte)Math.Min(255, value.R * 0.85 + 20), (byte)(value.G * 0.62), (byte)(value.B * 0.62));
        }
    }

    public Color HairColor
    {
        get => _hairBrush.Color;
        set => _hairBrush.Color = value;
    }

    public Joint this[string id] => _byId[id];
    public bool TryGetJoint(GeometryModel3D model, out Joint joint)
    {
        if (_parts.TryGetValue(model, out var p)) { joint = p.Joint; return true; }
        joint = null!;
        return false;
    }

    // ------------------------------------------------------------------ Skelett

    private Joint AddJoint(string id, string name, Joint? parent, Vector3D offset,
        AxisDef? bend, AxisDef? side, AxisDef? twist, string? mirror = null)
    {
        var j = new Joint { Id = id, Name = name, Parent = parent, BaseOffset = offset, Offset = offset, Bend = bend, Side = side, Twist = twist, MirrorId = mirror };
        Joints.Add(j);
        _byId[id] = j;
        return j;
    }

    private void BuildSkeleton()
    {
        var pelvis = AddJoint("pelvis", "Becken (ganzer Körper)", null, new Vector3D(0, 1.0, 0),
            new AxisDef("Vor/zurück neigen", -90, 90, 1),
            new AxisDef("Seitlich neigen", -90, 90, -1),
            new AxisDef("Drehen", -180, 180, 1));
        var spine = AddJoint("spine", "Bauch", pelvis, new Vector3D(0, 0.08, 0),
            new AxisDef("Vor/zurück beugen", -30, 70, 1),
            new AxisDef("Seitlich beugen", -40, 40, -1),
            new AxisDef("Drehen", -45, 45, 1));
        var chest = AddJoint("chest", "Brust", spine, new Vector3D(0, 0.17, 0),
            new AxisDef("Vor/zurück beugen", -30, 45, 1),
            new AxisDef("Seitlich beugen", -30, 30, -1),
            new AxisDef("Drehen", -40, 40, 1));
        var neck = AddJoint("neck", "Hals", chest, new Vector3D(0, 0.29, 0),
            new AxisDef("Vor/zurück", -40, 60, 1),
            new AxisDef("Seitlich", -40, 40, -1),
            new AxisDef("Drehen", -60, 60, 1));
        AddJoint("head", "Kopf", neck, new Vector3D(0, 0.09, 0),
            new AxisDef("Nicken", -40, 40, 1),
            new AxisDef("Seitlich neigen", -30, 30, -1),
            new AxisDef("Drehen", -70, 70, 1));

        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r", other = s > 0 ? "_r" : "_l", side = s > 0 ? "links" : "rechts";
            var sh = AddJoint("shoulder" + sfx, "Oberarm " + side, chest, new Vector3D(s * 0.19, 0.25, 0),
                new AxisDef("Vor/zurück heben", -60, 180, -1),
                new AxisDef("Seitlich heben", -30, 180, s),
                new AxisDef("Drehen", -90, 90, s), "shoulder" + other);
            var el = AddJoint("elbow" + sfx, "Unterarm " + side, sh, new Vector3D(0, -0.29, 0),
                new AxisDef("Ellbogen beugen", 0, 150, -1),
                null,
                new AxisDef("Drehen", -90, 90, s), "elbow" + other);
            AddJoint("wrist" + sfx, "Hand " + side, el, new Vector3D(0, -0.27, 0),
                new AxisDef("Beugen", -80, 80, -1),
                new AxisDef("Seitlich", -30, 30, s),
                null, "wrist" + other);
        }
        foreach (var s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? "_l" : "_r", other = s > 0 ? "_r" : "_l", side = s > 0 ? "links" : "rechts";
            var hip = AddJoint("hip" + sfx, "Oberschenkel " + side, pelvis, new Vector3D(s * 0.09, -0.05, 0),
                new AxisDef("Vor/zurück heben", -30, 130, -1),
                new AxisDef("Seitlich spreizen", -30, 90, s),
                new AxisDef("Drehen", -45, 45, s), "hip" + other);
            var knee = AddJoint("knee" + sfx, "Unterschenkel " + side, hip, new Vector3D(0, -0.44, 0),
                new AxisDef("Knie beugen", 0, 150, 1),
                null,
                new AxisDef("Drehen", -30, 30, s), "knee" + other);
            AddJoint("ankle" + sfx, "Fuß " + side, knee, new Vector3D(0, -0.43, 0),
                new AxisDef("Heben/senken", -50, 40, -1),
                new AxisDef("Seitlich kippen", -30, 30, s),
                null, "ankle" + other);
        }
    }

    // ------------------------------------------------------------------ Körperteile

    private void BuildParts()
    {
        var sel = _selected;
        Select(null);
        Group.Children.Clear();
        _parts.Clear();
        foreach (var j in Joints) j.Parts.Clear();

        var builder = new BodyBuilder(AddPart);
        if (Style == FigureStyle.Mannequin) builder.BuildMannequin();
        else builder.BuildHuman(Style == FigureStyle.Female, Hair);
        Select(sel);
    }

    private void AddPart(string jointId, MeshGeometry3D mesh, MaterialKind kind, bool uniform = false)
    {
        var j = _byId[jointId];
        var gm = new GeometryModel3D(mesh, _mats[kind]) { Transform = new MatrixTransform3D(Matrix3D.Identity) };
        if (kind == MaterialKind.Hair) gm.BackMaterial = _mats[kind]; // offene Haarkappe von innen nicht durchsichtig
        var p = new Part(gm, j, kind, uniform);
        j.Parts.Add(p);
        _parts[gm] = p;
        Group.Children.Add(gm);
    }

    /// <summary>Wechselt Figurenstil/Haare/Proportionen. Die Pose bleibt erhalten.</summary>
    public void SetFigure(FigureStyle style, bool hair, Proportions proportions)
    {
        bool rebuild = style != Style || hair != Hair || Group.Children.Count == 0;
        Style = style;
        Hair = hair;
        Proportions = proportions.Clone();
        Proportions.Clamp();
        if (rebuild) BuildParts();
        UpdateScales();
        Update();
    }

    public FigureData GetFigure() => new() { Style = Style, Hair = Hair, Proportions = Proportions.Clone() };

    /// <summary>Berechnet Segment-Skalierungen und Gelenkabstände aus den Proportionen
    /// und die Gesamtskalierung für die gewünschte Körpergröße.</summary>
    private void UpdateScales()
    {
        var p = Proportions;
        double a = p.Arm / 100, l = p.Leg / 100, t = p.Torso / 100, s = p.Shoulder / 100, h = p.Head / 100, b = p.Build / 100;
        foreach (var j in Joints)
        {
            (j.SegScale, j.UniformScale) = j.Id switch
            {
                "pelvis" => (new Vector3D(b, 1, b), b),
                "spine" => (new Vector3D(b, t, b), b),
                "chest" => (new Vector3D(s * b, t, b), b),
                "neck" => (new Vector3D(b, 1, b), b),
                "head" => (new Vector3D(h, h, h), h),
                _ when j.Id.StartsWith("shoulder") || j.Id.StartsWith("elbow") => (new Vector3D(b, a, b), b),
                _ when j.Id.StartsWith("wrist") => (new Vector3D(1, 1, 1), (a + 1) / 2),
                _ when j.Id.StartsWith("hip") || j.Id.StartsWith("knee") => (new Vector3D(b, l, b), b),
                _ when j.Id.StartsWith("ankle") => (new Vector3D(1, 1, 1), (l + 1) / 2),
                _ => (new Vector3D(1, 1, 1), 1.0)
            };
        }
        foreach (var j in Joints)
        {
            if (j.Parent == null) { j.Offset = j.BaseOffset; continue; }
            var ps = j.Parent.SegScale;
            j.Offset = new Vector3D(j.BaseOffset.X * ps.X, j.BaseOffset.Y * ps.Y, j.BaseOffset.Z * ps.Z);
        }

        // Körpergröße: unskalierte Höhe in Grundstellung messen
        var pose = GetPose();
        foreach (var j in Joints) j.BendValue = j.SideValue = j.TwistValue = 0;
        _globalScale = 1;
        Update();
        double natural = ComputeMaxY() - ComputeMinY();
        SetPoseValues(pose);
        _globalScale = natural > 0.1 ? p.HeightCm / 100 / natural : 1;
    }

    // ------------------------------------------------------------------ Kinematik

    private Matrix3D LocalMatrix(Joint j)
    {
        var m = Matrix3D.Identity;
        if (j.Parent == null && Math.Abs(_globalScale - 1) > 1e-9) m.Scale(new Vector3D(_globalScale, _globalScale, _globalScale));
        // Reihenfolge (Zeilenvektoren): erst um Knochenachse drehen, dann seitlich, dann beugen, dann versetzen.
        if (j.Twist != null && j.TwistValue != 0) m.Rotate(new Quaternion(new Vector3D(0, 1, 0), j.Twist.Sign * j.TwistValue));
        if (j.Side != null && j.SideValue != 0) m.Rotate(new Quaternion(new Vector3D(0, 0, 1), j.Side.Sign * j.SideValue));
        if (j.Bend != null && j.BendValue != 0) m.Rotate(new Quaternion(new Vector3D(1, 0, 0), j.Bend.Sign * j.BendValue));
        m.Translate(j.Parent == null ? j.Offset + RootPosition : j.Offset);
        return m;
    }

    public void Update()
    {
        foreach (var j in Joints)
        {
            var local = LocalMatrix(j);
            j.World = j.Parent == null ? local : local * j.Parent.World;
            foreach (var p in j.Parts)
            {
                var sc = p.Uniform ? new Vector3D(j.UniformScale, j.UniformScale, j.UniformScale) : j.SegScale;
                var m = Matrix3D.Identity;
                m.Scale(sc);
                ((MatrixTransform3D)p.Model.Transform).Matrix = m * j.World;
            }
        }
    }

    private IEnumerable<Point3D> AllWorldPoints()
    {
        foreach (var p in _parts.Values)
        {
            var m = ((MatrixTransform3D)p.Model.Transform).Matrix;
            var mesh = (MeshGeometry3D)p.Model.Geometry;
            // jeder zweite Punkt genügt für die Bodenhöhe
            for (int i = 0; i < mesh.Positions.Count; i += 2)
                yield return m.Transform(mesh.Positions[i]);
        }
    }

    /// <summary>Alle Meshes mit ihrer aktuellen Welt-Transformation (für Schattenwurf).</summary>
    public IEnumerable<(MeshGeometry3D Mesh, Matrix3D Transform)> WorldMeshes() =>
        _parts.Values.Select(p => ((MeshGeometry3D)p.Model.Geometry, ((MatrixTransform3D)p.Model.Transform).Matrix));

    public double ComputeMinY() => AllWorldPoints().Min(p => p.Y);
    public double ComputeMaxY() => AllWorldPoints().Max(p => p.Y);

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
            foreach (var p in _selected.Parts) p.Model.Material = _mats[p.Kind];
        _selected = joint;
        if (_selected != null)
            foreach (var p in _selected.Parts) p.Model.Material = _highlightMat;
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

    private void SetPoseValues(PoseData pose)
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
    }

    public void SetPose(PoseData pose)
    {
        SetPoseValues(pose);
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
