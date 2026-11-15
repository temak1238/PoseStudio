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

internal sealed record Part(GeometryModel3D Model, Joint Joint, MaterialKind Kind, bool Uniform)
{
    /// <summary>Weiche Übergänge an den Gelenken (nur menschliche Figuren): Ausgangsform und Gewichte je Punkt.</summary>
    public SkinData? Skin { get; set; }
    /// <summary>Ursprüngliche Punkte/Normalen im Gelenk-System (das Mesh selbst enthält nach dem Skinning Weltkoordinaten).</summary>
    public Point3D[] LocalPos { get; init; } = Array.Empty<Point3D>();
    public Vector3D[] LocalNrm { get; init; } = Array.Empty<Vector3D>();
}

internal sealed class SkinData
{
    public required Point3D[] Rest { get; init; }      // Punkte im System des Heimatgelenks (unskaliert)
    public required Vector3D[] RestN { get; init; }
    public required Joint[] Chain { get; init; }        // Gelenkkette entlang des Knochens
    public required Vector3D[] Cum { get; init; }       // Lage jedes Kettengelenks im Heimat-System (unskaliert)
    public required int[] K { get; init; }              // erstes beteiligtes Gelenk je Punkt
    public required double[] T { get; init; }           // Anteil des nächsten Gelenks (0..1)
}
/// <summary>Posierbare Figur (Gliederpuppe oder Mensch). Koordinaten in Metern, Y nach oben,
/// Figur schaut nach +Z. Links der Figur = +X.</summary>
public sealed class Mannequin
{
    private readonly Dictionary<GeometryModel3D, Part> _parts = new();
    private readonly Dictionary<string, Joint> _byId = new();
    private readonly Dictionary<MaterialKind, Material> _mats = new();
    private readonly ShadeBrush _bodyBrush = new(Color.FromRgb(0xC9, 0xB2, 0x92));
    private readonly ShadeBrush _jointBrush = new(Colors.Gray);
    private readonly ShadeBrush _lipsBrush = new(Colors.Gray);
    private readonly ShadeBrush _hairBrush = new(Color.FromRgb(0x3A, 0x2A, 0x20));

    /// <summary>Anzahl Hilfspunkte am Ende jedes Meshes (Texturkoordinaten (0,0) und (1,1)),
    /// damit der Schatten-Farbverlauf immer auf 0..1 abgebildet wird.</summary>
    internal const int DummyVertices = 2;
    private readonly Material _highlightMat;
    private Joint? _selected;
    private double _globalScale = 1;
    private bool _ikEnabled = true;
    private PropSettings _prop = new();
    private bool _propSelected;
    /// <summary>Unsichtbarer Träger für die Objekt-Teile (nicht in der Gelenkliste).</summary>
    private readonly Joint _propJoint = new() { Id = "prop", Name = "Objekt" };

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
        _mats[MaterialKind.Body] = MakeMaterial(_bodyBrush.Brush, 0x40);
        _mats[MaterialKind.Joint] = MakeMaterial(_jointBrush.Brush, 0x40);
        _mats[MaterialKind.Lips] = MakeMaterial(_lipsBrush.Brush, 0x30);
        _mats[MaterialKind.EyeWhite] = MakeMaterial(new ShadeBrush(Color.FromRgb(0xF4, 0xF1, 0xEC)).Brush, 0x70);
        _mats[MaterialKind.Iris] = MakeMaterial(new ShadeBrush(Color.FromRgb(0x3B, 0x2C, 0x22)).Brush, 0x90);
        _mats[MaterialKind.Hair] = MakeMaterial(_hairBrush.Brush, 0x20);
        _mats[MaterialKind.PropWood] = MakeMaterial(new ShadeBrush(Color.FromRgb(0x7A, 0x52, 0x32)).Brush, 0x30);
        _mats[MaterialKind.PropMetal] = MakeMaterial(new ShadeBrush(Color.FromRgb(0xC4, 0xC8, 0xCC)).Brush, 0xC0);
        _mats[MaterialKind.PropGold] = MakeMaterial(new ShadeBrush(Color.FromRgb(0xC9, 0xA2, 0x3E)).Brush, 0xA0);
        _mats[MaterialKind.PropGem] = MakeMaterial(new ShadeBrush(Color.FromRgb(0x4F, 0x9C, 0xE0)).Brush, 0xC0);
        _byId["prop"] = _propJoint;
        _highlightMat = MakeMaterial(new ShadeBrush(Color.FromRgb(0xFF, 0x8C, 0x1A)).Brush, 0x40);
        BodyColor = _bodyBrush.Color;
        BuildSkeleton();
        BuildParts();
        UpdateScales();
        Update();
    }

    /// <summary>Farbverlauf Grundfarbe → Schwarz. Die Texturkoordinate u (0..1) eines Punktes bestimmt,
    /// wie stark er abgedunkelt wird – so entstehen die Eigenschatten.</summary>
    private sealed class ShadeBrush
    {
        private readonly GradientStop _stop;
        public LinearGradientBrush Brush { get; }
        public ShadeBrush(Color c)
        {
            _stop = new GradientStop(c, 0);
            Brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            Brush.GradientStops.Add(_stop);
            Brush.GradientStops.Add(new GradientStop(Colors.Black, 1));
        }
        public Color Color { get => _stop.Color; set => _stop.Color = value; }
    }

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
        _propJoint.Parts.Clear();

        // Greifende Hände bekommen eine geschlossene Hand (Faust um den Griff)
        bool gripL = _prop.Active && (_prop.TwoHanded || _prop.MainSide > 0);
        bool gripR = _prop.Active && (_prop.TwoHanded || _prop.MainSide < 0);
        var builder = new BodyBuilder(AddPart);
        if (Style == FigureStyle.Mannequin) builder.BuildMannequin();
        else builder.BuildHuman(Style == FigureStyle.Female, Hair, gripL, gripR);
        foreach (var (mesh, kind) in PropBuilder.Build(_prop.Type)) AddPart("prop", mesh, kind, true);
        _skinDirty = true;
        Select(sel);
        SelectProp(_propSelected);
    }

    private void AddPart(string jointId, MeshGeometry3D mesh, MaterialKind kind, bool uniform = false)
    {
        var j = _byId[jointId];
        // Eigene, veränderbare Kopie mit Texturkoordinaten für den Eigenschatten
        var m = mesh.Clone();
        m.Positions.Add(m.Positions[0]); m.Positions.Add(m.Positions[0]);
        m.Normals.Add(m.Normals[0]); m.Normals.Add(m.Normals[0]);
        var tc = new PointCollection(m.Positions.Count);
        for (int i = 0; i < m.Positions.Count - DummyVertices; i++) tc.Add(new System.Windows.Point(0, 0.5));
        tc.Add(new System.Windows.Point(0, 0)); tc.Add(new System.Windows.Point(1, 1));
        m.TextureCoordinates = tc;
        var gm = new GeometryModel3D(m, _mats[kind]) { Transform = new MatrixTransform3D(Matrix3D.Identity) };
        if (kind == MaterialKind.Hair) gm.BackMaterial = _mats[kind]; // offene Haarkappe von innen nicht durchsichtig
        var p = new Part(gm, j, kind, uniform) { LocalPos = m.Positions.ToArray(), LocalNrm = m.Normals.ToArray() };
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
        _ikEnabled = false; // Messung in Grundstellung, Arme nicht vom Objekt verbiegen lassen
        Update();
        double natural = ComputeMaxY() - ComputeMinY();
        _ikEnabled = true;
        SetPoseValues(pose);
        _globalScale = natural > 0.1 ? p.HeightCm / 100 / natural : 1;
        _skinDirty = true;
    }

    // ------------------------------------------------------------------ Kinematik

    private Matrix3D LocalMatrix(Joint j) => LocalMatrix(j, j.BendValue, j.SideValue, j.TwistValue);

    private Matrix3D LocalMatrix(Joint j, double bend, double side, double twist)
    {
        var m = Matrix3D.Identity;
        if (j.Parent == null && Math.Abs(_globalScale - 1) > 1e-9) m.Scale(new Vector3D(_globalScale, _globalScale, _globalScale));
        // Reihenfolge (Zeilenvektoren): erst um Knochenachse drehen, dann seitlich, dann beugen, dann versetzen.
        if (j.Twist != null && twist != 0) m.Rotate(new Quaternion(new Vector3D(0, 1, 0), j.Twist.Sign * twist));
        if (j.Side != null && side != 0) m.Rotate(new Quaternion(new Vector3D(0, 0, 1), j.Side.Sign * side));
        if (j.Bend != null && bend != 0) m.Rotate(new Quaternion(new Vector3D(1, 0, 0), j.Bend.Sign * bend));
        m.Translate(j.Parent == null ? j.Offset + RootPosition : j.Offset);
        return m;
    }

    public void Update()
    {
        ComputeWorlds();
        if (_prop.Active && _ikEnabled)
        {
            SolveProp();
            ComputeWorlds();
        }
        if (_skinDirty) BuildSkin();
        foreach (var j in Joints)
        {
            foreach (var p in j.Parts)
            {
                if (p.Skin != null) { ApplySkin(p, j); continue; }
                var sc = p.Uniform ? new Vector3D(j.UniformScale, j.UniformScale, j.UniformScale) : j.SegScale;
                var m = Matrix3D.Identity;
                m.Scale(sc);
                ((MatrixTransform3D)p.Model.Transform).Matrix = m * j.World;
            }
        }
        var pw = PropWorld;
        foreach (var p in _propJoint.Parts) ((MatrixTransform3D)p.Model.Transform).Matrix = pw;
    }

    // ------------------------------------------------------------------ Weiche Gelenkübergänge (Skinning)

    private bool _skinDirty = true;

    /// <summary>Kind-Gelenk entlang des Knochens (für den Übergang am Segmentende).</summary>
    private Joint? BoneChild(Joint j) => j.Id switch
    {
        "pelvis" => this["spine"], "spine" => this["chest"], "chest" => this["neck"], "neck" => this["head"],
        _ when j.Id.StartsWith("shoulder") => this["elbow" + j.Id[^2..]],
        _ when j.Id.StartsWith("elbow") => this["wrist" + j.Id[^2..]],
        _ when j.Id.StartsWith("hip") => this["knee" + j.Id[^2..]],
        _ when j.Id.StartsWith("knee") => this["ankle" + j.Id[^2..]],
        _ => null
    };

    /// <summary>Gewichte für die weichen Übergänge: Jeder Punkt gehört entlang des Knochens zu einem Kettengelenk;
    /// in einer Zone um jedes Gelenk wird zwischen beiden Gelenken überblendet. Durchgehende Formen (Bein, Arm, Rumpf)
    /// biegen sich so ohne Naht und ohne Knick.</summary>
    private void BuildSkin()
    {
        _skinDirty = false;
        foreach (var j in Joints)
            foreach (var p in j.Parts)
            {
                p.Skin = null;
                if (Style == FigureStyle.Mannequin || p.Uniform) continue; // Gliederpuppe bleibt bewusst gegliedert
                var chain = new List<Joint>(); var cum = new List<Vector3D>();
                if (j.Parent != null) { chain.Add(j.Parent); cum.Add(-j.BaseOffset); }
                chain.Add(j); cum.Add(new Vector3D());
                var acc = new Vector3D();
                for (var c = BoneChild(j); c != null; c = BoneChild(c)) { acc += c.BaseOffset; chain.Add(c); cum.Add(acc); }
                var first = BoneChild(j);
                var dir = first != null ? first.BaseOffset : new Vector3D(0, 1, 0);
                dir.Normalize();
                double zone = j.Id is "pelvis" or "spine" or "chest" or "neck" ? 0.035 : 0.06;
                var a = cum.Select(v => Vector3D.DotProduct(v, dir)).ToArray();

                int n = p.LocalPos.Length;
                var kk = new int[n]; var tt = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double x = Vector3D.DotProduct((Vector3D)p.LocalPos[i], dir);
                    double ci = 0;
                    for (int b = 1; b < a.Length; b++)
                    {
                        double s = Math.Clamp((x - a[b]) / (2 * zone) + 0.5, 0, 1);
                        ci += s * s * (3 - 2 * s);
                    }
                    int k = Math.Min((int)Math.Floor(ci), chain.Count - 1);
                    kk[i] = k;
                    tt[i] = k >= chain.Count - 1 ? 0 : ci - k;
                }
                p.Skin = new SkinData { Rest = p.LocalPos, RestN = p.LocalNrm, Chain = chain.ToArray(), Cum = cum.ToArray(), K = kk, T = tt };
                ((MatrixTransform3D)p.Model.Transform).Matrix = Matrix3D.Identity;
            }
    }

    /// <summary>Linear Blend Skinning: Punkt im System des jeweiligen Kettengelenks (mit dessen Proportionen) auswerten
    /// und zwischen den beiden beteiligten Gelenken überblenden.</summary>
    private static void ApplySkin(Part p, Joint j)
    {
        var s = p.Skin!;
        var mesh = (MeshGeometry3D)p.Model.Geometry;
        int n = s.Rest.Length;
        var pos = new Point3DCollection(n);
        var nrm = new Vector3DCollection(n);
        for (int i = 0; i < n; i++)
        {
            int k = s.K[i]; double t = s.T[i];
            var r = s.Rest[i]; var rn = s.RestN[i];
            var (pk, nk) = Eval(s.Chain[k], r - s.Cum[k], rn);
            if (t > 0)
            {
                var (p2, n2) = Eval(s.Chain[k + 1], r - s.Cum[k + 1], rn);
                pk = (Point3D)((Vector3D)pk * (1 - t) + (Vector3D)p2 * t);
                nk = nk * (1 - t) + n2 * t;
            }
            if (nk.LengthSquared > 0) nk.Normalize();
            pos.Add(pk); nrm.Add(nk);
        }
        mesh.Positions = pos;
        mesh.Normals = nrm;

        static (Point3D, Vector3D) Eval(Joint jt, Point3D local, Vector3D n)
        {
            var sc = jt.SegScale;
            var q = new Point3D(local.X * sc.X, local.Y * sc.Y, local.Z * sc.Z);
            var nq = new Vector3D(n.X / sc.X, n.Y / sc.Y, n.Z / sc.Z);
            return (jt.World.Transform(q), jt.World.Transform(nq));
        }
    }
    private void ComputeWorlds()
    {
        foreach (var j in Joints)
        {
            var local = LocalMatrix(j);
            j.World = j.Parent == null ? local : local * j.Parent.World;
        }
    }

    // ------------------------------------------------------------------ Objekt in der Hand

    public PropSettings Prop => _prop.Clone();
    public bool PropSelected => _propSelected;
    public bool IsPropPart(GeometryModel3D model) => _parts.TryGetValue(model, out var p) && ReferenceEquals(p.Joint, _propJoint);

    /// <summary>Welt-Lage des Objekts (hängt am Brustkorb).</summary>
    public Matrix3D PropWorld => _prop.LocalMatrix() * this["chest"].World;

    /// <summary>Bewegungsrahmen des Brustkorbs (für das Umrechnen von Mausbewegungen).</summary>
    public Matrix3D ChestWorld => this["chest"].World;

    /// <summary>Objekt setzen/ändern. Bei neuem Objekt oder anderer Haltung werden die Hände neu aufgebaut.</summary>
    public void SetProp(PropSettings p, bool update = true)
    {
        var old = _prop;
        _prop = p.Clone();
        _prop.Clamp();
        if (old.Type != _prop.Type || old.Hold != _prop.Hold) BuildParts();
        if (update) Update();
    }

    public void SelectProp(bool on)
    {
        _propSelected = on && _prop.Active;
        foreach (var p in _propJoint.Parts) p.Model.Material = _propSelected ? _highlightMat : _mats[p.Kind];
    }

    /// <summary>Die greifenden Arme per inverser Kinematik an die Griffpunkte des Objekts führen.</summary>
    private void SolveProp()
    {
        LastGripError = 0;
        var pw = PropWorld;
        var axis = pw.Transform(new Vector3D(0, 1, 0));
        axis.Normalize();
        int s = _prop.MainSide;
        SolveArm(s, pw.Transform(new Point3D(0, 0, 0)), axis);
        if (_prop.TwoHanded)
            SolveArm(-s, pw.Transform(new Point3D(0, -_prop.GripSpacing / 100, 0)), axis);
    }

    /// <summary>Arm-IK (gedämpfte Gauß-Newton-Iteration über 7 Gelenkwinkel): Griffpunkt der Hand auf den Zielpunkt,
    /// Daumenseite der Hand entlang der Objektachse. Gelenkgrenzen werden eingehalten, das Handgelenk bleibt möglichst gerade.</summary>
    private void SolveArm(int s, Point3D target, Vector3D axis)
    {
        string sfx = s > 0 ? "_l" : "_r";
        var sh = this["shoulder" + sfx]; var el = this["elbow" + sfx]; var wr = this["wrist" + sfx];
        var chestW = sh.Parent!.World;
        double u = wr.UniformScale;
        var grip = Style == FigureStyle.Mannequin ? new Point3D(-s * 0.028 * u, -0.088 * u, 0) : new Point3D(-s * 0.03 * u, -0.092 * u, 0);
        var defs = new[] { sh.Bend!, sh.Side!, sh.Twist!, el.Bend!, el.Twist!, wr.Bend!, wr.Side! };
        const int n = 7, rows = 11;

        double[] Residual(double[] q)
        {
            var ws = LocalMatrix(sh, q[0], q[1], q[2]) * chestW;
            var we = LocalMatrix(el, q[3], 0, q[4]) * ws;
            var ww = LocalMatrix(wr, q[5], q[6], 0) * we;
            var g = ww.Transform(grip);
            var z = ww.Transform(new Vector3D(0, 0, 1)); z.Normalize();
            // Zielpunkt und Achse haben Vorrang; schwache Vorlieben für eine natürliche Haltung
            // (Handgelenk gerade, Schulter wenig gedreht und nicht weit abgespreizt, Unterarm wenig verdreht)
            const double wAxis = 0.12, wWrist = 0.0008, wPref = 0.0004;
            return new[]
            {
                target.X - g.X, target.Y - g.Y, target.Z - g.Z,
                wAxis * (axis.X - z.X), wAxis * (axis.Y - z.Y), wAxis * (axis.Z - z.Z),
                -wWrist * q[5], -wWrist * q[6],
                -wPref * q[2], -wPref * Math.Max(0, q[1] - 25), -wPref * q[4]
            };
        }
        static double Norm(double[] r) => Math.Sqrt(r.Sum(x => x * x));

        double[] Solve(double[] q)
        {
            q = (double[])q.Clone();
            for (int it = 0; it < 60; it++)
            {
                var r = Residual(q);
                if (Norm(r) < 0.0015) break;
                var J = new double[rows, n];
                for (int k = 0; k < n; k++)
                {
                    var qq = (double[])q.Clone();
                    qq[k] += 0.5;
                    var r2 = Residual(qq);
                    for (int i = 0; i < rows; i++) J[i, k] = (r2[i] - r[i]) / 0.5;
                }
                // (JᵀJ + λI) Δ = -Jᵀ r
                var A = new double[n, n + 1];
                for (int a = 0; a < n; a++)
                {
                    for (int b = 0; b < n; b++)
                    {
                        double sum = 0;
                        for (int i = 0; i < rows; i++) sum += J[i, a] * J[i, b];
                        A[a, b] = sum + (a == b ? 2e-6 : 0);
                    }
                    double rb = 0;
                    for (int i = 0; i < rows; i++) rb += J[i, a] * r[i];
                    A[a, n] = -rb;
                }
                var d = GaussSolve(A, n);
                if (d == null) break;
                for (int k = 0; k < n; k++)
                    q[k] = Math.Clamp(q[k] + Math.Clamp(d[k], -12, 12), defs[k].Min, defs[k].Max);
            }
            return q;
        }

        var start = new[] { sh.BendValue, sh.SideValue, sh.TwistValue, el.BendValue, el.TwistValue, wr.BendValue, wr.SideValue };
        var best = Solve(start);
        double bestErr = Norm(Residual(best));
        if (bestErr > 0.02)
        {
            // schlechte Ausgangslage: mit typischen Armhaltungen neu versuchen
            foreach (var seed in new[] { new double[] { 40, 15, 0, 70, 0, 0, 0 }, new double[] { 80, 10, 0, 30, 0, 0, 0 },
                                         new double[] { 20, 40, -40, 90, 30, 0, 0 }, new double[] { 0, 20, 0, 20, 0, 0, 0 },
                                         new double[] { 120, 20, 0, 40, 0, 0, 0 } })
            {
                var q = Solve(seed);
                double e = Norm(Residual(q));
                if (e < bestErr) { best = q; bestErr = e; }
            }
        }
        sh.BendValue = best[0]; sh.SideValue = best[1]; sh.TwistValue = best[2];
        el.BendValue = best[3]; el.TwistValue = best[4];
        wr.BendValue = best[5]; wr.SideValue = best[6];
        LastGripError = Math.Max(LastGripError, Math.Sqrt(Math.Pow(Residual(best)[0], 2) + Math.Pow(Residual(best)[1], 2) + Math.Pow(Residual(best)[2], 2)));
    }

    /// <summary>Hält die Hand dieser Seite gerade ein Objekt (dann steuert das Objekt den Arm)?</summary>
    public bool HandHoldsProp(Joint wrist) =>
        _prop.Active && wrist.Id.StartsWith("wrist") && (_prop.TwoHanded || (_prop.MainSide > 0) == wrist.Id.EndsWith("_l"));

    /// <summary>Hand (Handgelenk) oder Fuß (Knöchel) an einen Zielpunkt führen – Arm bzw. Bein folgen per IK.
    /// Freiheitsgrade: Schulter/Hüfte (beugen, seitlich, drehen) und Ellbogen/Knie (beugen, drehen).
    /// Rückgabe: verbleibender Abstand zum Ziel in Metern.</summary>
    public double ReachTo(Joint end, Point3D target)
    {
        string sfx = end.Id[^2..];
        Joint upper, lower;
        if (end.Id.StartsWith("wrist")) { upper = this["shoulder" + sfx]; lower = this["elbow" + sfx]; }
        else if (end.Id.StartsWith("ankle")) { upper = this["hip" + sfx]; lower = this["knee" + sfx]; }
        else return double.NaN;

        var parentW = upper.Parent!.World;
        var defs = new[] { upper.Bend!, upper.Side!, upper.Twist!, lower.Bend!, lower.Twist! };
        const int n = 5, rows = 6;
        double[] Residual(double[] q)
        {
            var wu = LocalMatrix(upper, q[0], q[1], q[2]) * parentW;
            var wl = LocalMatrix(lower, q[3], 0, q[4]) * wu;
            var p = wl.Transform((Point3D)end.Offset);
            const double wPref = 0.0005;
            return new[] { target.X - p.X, target.Y - p.Y, target.Z - p.Z, -wPref * q[2], -wPref * q[4], -wPref * Math.Max(0, Math.Abs(q[1]) - 30) };
        }
        static double Norm(double[] r) => Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);

        var q = new[] { upper.BendValue, upper.SideValue, upper.TwistValue, lower.BendValue, lower.TwistValue };
        for (int it = 0; it < 40; it++)
        {
            var r = Residual(q);
            if (Norm(r) < 0.001) break;
            var J = new double[rows, n];
            for (int k = 0; k < n; k++)
            {
                var qq = (double[])q.Clone();
                qq[k] += 0.5;
                var r2 = Residual(qq);
                for (int i = 0; i < rows; i++) J[i, k] = (r2[i] - r[i]) / 0.5;
            }
            var A = new double[n, n + 1];
            for (int a = 0; a < n; a++)
            {
                for (int b = 0; b < n; b++)
                {
                    double sum = 0;
                    for (int i = 0; i < rows; i++) sum += J[i, a] * J[i, b];
                    A[a, b] = sum + (a == b ? 3e-6 : 0);
                }
                double rb = 0;
                for (int i = 0; i < rows; i++) rb += J[i, a] * r[i];
                A[a, n] = -rb;
            }
            var d = GaussSolve(A, n);
            if (d == null) break;
            for (int k = 0; k < n; k++)
                q[k] = Math.Clamp(q[k] + Math.Clamp(d[k], -10, 10), defs[k].Min, defs[k].Max);
        }
        upper.BendValue = q[0]; upper.SideValue = q[1]; upper.TwistValue = q[2];
        lower.BendValue = q[3]; lower.TwistValue = q[4];
        double err = Norm(Residual(q));
        Update();
        return err;
    }

    /// <summary>Größter Abstand Hand ↔ Griffpunkt der letzten Berechnung (m) – zeigt, ob das Objekt erreichbar ist.</summary>
    public double LastGripError { get; private set; }

    private static double[]? GaussSolve(double[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            if (Math.Abs(a[piv, c]) < 1e-14) return null;
            if (piv != c) for (int k = 0; k <= n; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = a[r, c] / a[c, c];
                for (int k = c; k <= n; k++) a[r, k] -= f * a[c, k];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = a[i, n] / a[i, i];
        return x;
    }

    private IEnumerable<Point3D> AllWorldPoints()
    {
        foreach (var p in _parts.Values)
        {
            if (ReferenceEquals(p.Joint, _propJoint)) continue; // Objekt zählt nicht für die Bodenhöhe (Stab darf den Boden berühren)
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
        bool propSel = _propSelected;
        Select(null);
        SelectProp(false);
        var clone = Group.Clone();
        Select(sel);
        SelectProp(propSel);
        return clone;
    }

    /// <summary>Einfarbige Kopie für ControlNet-Bilder: flat = nur Farbfläche (Regionen-Karte),
    /// sonst glänzend schattiert. Haare/Augenbrauen fallen weg (glatter Kopf wie im Trainingsmaterial).</summary>
    public Model3DGroup CreateControlSnapshot(Color color, bool flat, bool includeProps)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var mg = new MaterialGroup();
        if (flat)
        {
            mg.Children.Add(new DiffuseMaterial(Brushes.Black));
            mg.Children.Add(new EmissiveMaterial(brush));
        }
        else
        {
            mg.Children.Add(new DiffuseMaterial(brush));
            mg.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), 22));
        }
        mg.Freeze();
        var group = new Model3DGroup();
        foreach (var p in _parts.Values)
        {
            if (p.Kind == MaterialKind.Hair) continue;
            if (ReferenceEquals(p.Joint, _propJoint) && !includeProps) continue;
            group.Children.Add(new GeometryModel3D(p.Model.Geometry.CloneCurrentValue(), mg)
            {
                Transform = new MatrixTransform3D(((MatrixTransform3D)p.Model.Transform).Matrix)
            });
        }
        return group;
    }

    // ------------------------------------------------------------------ Posen

    public PoseData GetPose()
    {
        var pose = new PoseData { RootPosition = new[] { RootPosition.X, RootPosition.Y, RootPosition.Z } };
        foreach (var j in Joints)
            pose.Joints[j.Id] = new[] { j.BendValue, j.SideValue, j.TwistValue };
        pose.Prop = _prop.Clone();
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
        if (pose.Prop != null) SetProp(pose.Prop, false);
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
        if (_prop.Active)
        {
            var p = _prop.Clone();
            p.X = -p.X; p.Tilt = -p.Tilt; p.Yaw = -p.Yaw; p.Roll = -p.Roll;
            p.Hold = p.Hold switch
            {
                HoldMode.Right => HoldMode.Left, HoldMode.Left => HoldMode.Right,
                HoldMode.BothRightTop => HoldMode.BothLeftTop, _ => HoldMode.BothRightTop
            };
            SetProp(p, false);
        }
        Update();
    }
}
