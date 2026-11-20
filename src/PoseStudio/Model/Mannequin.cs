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
    /// <summary>Fingergelenk (nicht in der Gelenkliste, Bedienung über „Hände und Finger“ oder Anklicken).</summary>
    public bool IsFinger { get; init; }
    /// <summary>Ruhe-Ausrichtung relativ zum Elterngelenk (Finger: natürliche Krümmung, Daumen-Richtung).</summary>
    internal Matrix3D Rest { get; init; } = Matrix3D.Identity;
    /// <summary>Länge des Fingerglieds bis zum nächsten Gelenk bzw. zur Spitze (unskaliert).</summary>
    internal double BoneLength { get; init; }

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
    /// <summary>Sehr kleines Teil (z. B. Augenbraue, Nase) – bekommt im Cel-Shading keine Kontur.</summary>
    public bool Small { get; init; }
    /// <summary>Punkte werden jedes Mal neu berechnet (Haarsträhnen) – keine Gelenk-Transformation.</summary>
    public bool Dynamic { get; init; }
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
public sealed partial class Mannequin
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
        void Def(MaterialKind kind, ShadeBrush b, byte spec) { _shades[kind] = (b, spec); _mats[kind] = MakeMaterial(b.Brush, spec, false); }
        Def(MaterialKind.Body, _bodyBrush, 0x40);
        Def(MaterialKind.Joint, _jointBrush, 0x40);
        Def(MaterialKind.Lips, _lipsBrush, 0x30);
        Def(MaterialKind.EyeWhite, new ShadeBrush(Color.FromRgb(0xF4, 0xF1, 0xEC)), 0x70);
        Def(MaterialKind.Iris, new ShadeBrush(Color.FromRgb(0x3B, 0x2C, 0x22)), 0x90);
        Def(MaterialKind.Hair, _hairBrush, 0x20);
        Def(MaterialKind.PropWood, new ShadeBrush(Color.FromRgb(0x7A, 0x52, 0x32)), 0x30);
        Def(MaterialKind.PropMetal, new ShadeBrush(Color.FromRgb(0xC4, 0xC8, 0xCC)), 0xC0);
        Def(MaterialKind.PropGold, new ShadeBrush(Color.FromRgb(0xC9, 0xA2, 0x3E)), 0xA0);
        Def(MaterialKind.PropGem, new ShadeBrush(Color.FromRgb(0x4F, 0x9C, 0xE0)), 0xC0);
        Def(MaterialKind.Line, new ShadeBrush(Color.FromRgb(0x2A, 0x1E, 0x1E)), 0x00);
        Def(MaterialKind.Mouth, new ShadeBrush(Color.FromRgb(0x8A, 0x3E, 0x3E)), 0x00);
        InitClothMaterials(Def);
        _byId["prop"] = _propJoint;
        _highlightMat = MakeMaterial(new ShadeBrush(Color.FromRgb(0xFF, 0x8C, 0x1A)).Brush, 0x40, false);
        BodyColor = _bodyBrush.Color;
        BuildSkeleton();
        BuildParts();
        UpdateScales();
        Update();
    }

    /// <summary>Normal: Farbverlauf Grundfarbe → Schwarz; die Texturkoordinate u (0..1) eines Punktes bestimmt,
    /// wie stark er abgedunkelt wird – so entstehen die Eigenschatten.
    /// Cel-Shading: zwei harte Stufen (u &lt; 0,5 = Grundfarbe, sonst Schattenfarbe).</summary>
    private sealed class ShadeBrush
    {
        private Color _color;
        private bool _toon;
        private double _shadow = 0.74;
        public LinearGradientBrush Brush { get; } = new() { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
        public ShadeBrush(Color c) { _color = c; Rebuild(); }
        public Color Color { get => _color; set { _color = value; Rebuild(); } }

        public void SetToon(bool on, double shadow, bool white = false)
        {
            _toon = on; _shadow = shadow; _white = white;
            Rebuild();
        }
        private bool _white;

        private void Rebuild()
        {
            var stops = new GradientStopCollection();
            var baseColor = _white ? Colors.White : _color; // Strichzeichnung: alle Flächen weiß
            if (_toon)
            {
                // Anime-Schatten: dunkler und leicht ins Rötlich-Violette verschoben
                byte S(double v) => (byte)Math.Clamp(v, 0, 255);
                var sh = Color.FromRgb(S(baseColor.R * _shadow), S(baseColor.G * _shadow * 0.9), S(baseColor.B * _shadow * 0.97));
                stops.Add(new GradientStop(baseColor, 0)); stops.Add(new GradientStop(baseColor, 0.5));
                stops.Add(new GradientStop(sh, 0.5)); stops.Add(new GradientStop(sh, 1));
            }
            else
            {
                stops.Add(new GradientStop(_color, 0)); stops.Add(new GradientStop(Colors.Black, 1));
            }
            Brush.GradientStops = stops;
        }
    }

    private static Material MakeMaterial(Brush brush, byte specular, bool toon)
    {
        var g = new MaterialGroup();
        if (toon)
        {
            // Flache Farbe unabhängig vom Licht; die Schattierung steckt ganz in der Texturkoordinate
            g.Children.Add(new DiffuseMaterial(Brushes.Black));
            g.Children.Add(new EmissiveMaterial(brush));
            return g;
        }
        g.Children.Add(new DiffuseMaterial(brush));
        g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(specular, 0xFF, 0xFF, 0xFF)), 40));
        return g;
    }
    /// <summary>Cel-Shading ein/aus. shadow = Helligkeit der Schattenstufe (0..1); lineart = Strichzeichnung (alle Flächen weiß,
    /// nur Augen, Mund und Linien behalten ihre Farbe).</summary>
    public void SetToon(bool on, double shadow, bool lineart = false)
    {
        if (on == Toon && Math.Abs(shadow - _toonShadow) < 1e-6 && lineart == _lineart) return;
        bool switchMat = on != Toon;
        Toon = on; _toonShadow = shadow; _lineart = lineart;
        foreach (var (kind, (brush, spec)) in _shades)
        {
            brush.SetToon(on, shadow, on && lineart && kind is not (MaterialKind.Line or MaterialKind.Iris or MaterialKind.Mouth or MaterialKind.EyeWhite));
            if (switchMat) _mats[kind] = MakeMaterial(brush.Brush, spec, on);
        }
        if (!switchMat) return;
        var sel = _selected; bool propSel = _propSelected;
        Select(null); SelectProp(false);
        foreach (var p in _parts.Values)
        {
            p.Model.Material = _mats[p.Kind];
            if (p.Model.BackMaterial != null) p.Model.BackMaterial = _mats[p.Kind];
        }
        Select(sel); SelectProp(propSel);
    }

    public bool Toon { get; private set; }
    private double _toonShadow = 0.74;
    private bool _lineart;
    private readonly Dictionary<MaterialKind, (ShadeBrush Brush, byte Spec)> _shades = new();

    /// <summary>Teile mit Kontur im Cel-Shading: keine Gesichtsdetails und keine sehr kleinen Teile.</summary>
    public IEnumerable<(MeshGeometry3D Mesh, Matrix3D Transform)> OutlineMeshes() =>
        _parts.Values.Where(p => p.Kind is not (MaterialKind.EyeWhite or MaterialKind.Iris or MaterialKind.Line or MaterialKind.Mouth or MaterialKind.Lips) && !p.Small)
            .Select(p => ((MeshGeometry3D)p.Model.Geometry, ((MatrixTransform3D)p.Model.Transform).Matrix));

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
        AxisDef? bend, AxisDef? side, AxisDef? twist, string? mirror = null,
        bool finger = false, Matrix3D? rest = null, double boneLength = 0)
    {
        var j = new Joint
        {
            Id = id, Name = name, Parent = parent, BaseOffset = offset, Offset = offset, Bend = bend, Side = side, Twist = twist, MirrorId = mirror,
            IsFinger = finger, Rest = rest ?? Matrix3D.Identity, BoneLength = boneLength
        };
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
                new AxisDef("Seitlich heben", -45, 180, s),
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
        foreach (var s in new[] { 1, -1 }) BuildHandSkeleton(s);
    }

    private static Matrix3D Rot(Vector3D axis, double deg)
    {
        var m = Matrix3D.Identity;
        if (deg != 0) m.Rotate(new Quaternion(axis, deg));
        return m;
    }

    /// <summary>Je Hand 15 Fingergelenke. Beugen = Drehung um das lokale Z (Spitze zur Handfläche), Spreizen = um das lokale X.
    /// Wert 0 = entspannte, leicht gekrümmte Ruhehaltung.</summary>
    private void BuildHandSkeleton(int s)
    {
        string sfx = s > 0 ? "_l" : "_r", other = s > 0 ? "_r" : "_l", side = s > 0 ? "links" : "rechts";
        var wrist = this["wrist" + sfx];
        var Z = new Vector3D(0, 0, 1); var X = new Vector3D(1, 0, 0);
        double[] spreadSign = { -1, -1, 1, 1 }; // + = weg vom Mittelfinger (Mittelfinger: Richtung Zeigefinger)
        string[] seg = { "Grundgelenk", "Mittelgelenk", "Endgelenk" };
        double[] restCurl = { 12, 18, 10 };
        (double min, double max)[] curlLim = { (-30, 85), (-20, 100), (-12, 75) };
        for (int f = 1; f <= 4; f++)
        {
            int i = f - 1;
            string id = HandGeometry.Ids[f], nm = HandGeometry.Names[f];
            double len = HandGeometry.Length[i];
            Joint parent = wrist;
            for (int k = 0; k < 3; k++)
            {
                var offset = k == 0 ? new Vector3D(0, HandGeometry.BaseY[i], HandGeometry.BaseZ[i])
                                    : new Vector3D(0, -len * HandGeometry.SegFrac[k - 1], 0);
                var rest = Rot(Z, -s * restCurl[k]) * (k == 0 ? Rot(X, -HandGeometry.Fan[i]) : Matrix3D.Identity);
                parent = AddJoint($"{id}{k + 1}{sfx}", $"{nm} {side} – {seg[k]}", parent, offset,
                    k == 0 ? new AxisDef("Spreizen", -15, 25, spreadSign[i]) : null,
                    new AxisDef("Beugen", curlLim[k].min, curlLim[k].max, -s),
                    null, $"{id}{k + 1}{other}", true, rest, len * HandGeometry.SegFrac[k]);
            }
        }
        string[] tseg = { "Sattelgelenk", "Grundgelenk", "Endgelenk" };
        Joint tp = wrist;
        for (int k = 0; k < 3; k++)
        {
            var offset = k == 0 ? HandGeometry.ThumbBase(s) : new Vector3D(0, -HandGeometry.ThumbLen[k - 1], 0);
            var rest = k == 0 ? HandGeometry.ThumbRest(s) : Rot(Z, -s * (k == 1 ? 8 : 10));
            tp = AddJoint($"thumb{k + 1}{sfx}", $"Daumen {side} – {tseg[k]}", tp, offset,
                k == 0 ? new AxisDef("Abspreizen", -30, 60, -1) : null,
                new AxisDef(k == 0 ? "Zur Handfläche" : "Beugen", k == 0 ? -25 : -20, k == 0 ? 60 : (k == 1 ? 70 : 85), -s),
                null, $"thumb{k + 1}{other}", true, rest, HandGeometry.ThumbLen[k]);
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
        var builder = new BodyBuilder((id, m, k, u) => AddPart(id, m, k, u), Proportions);
        if (Style == FigureStyle.Mannequin) builder.BuildMannequin();
        else builder.BuildHuman(Style.IsFemale(), Hair, Style.IsAnime(), gripL, gripR);
        BuildClothes(builder);
        BuildHair();
        BuildSeat();
        foreach (var (mesh, kind) in PropBuilder.Build(_prop.Type)) AddPart("prop", mesh, kind, true);
        _skinDirty = true;
        Select(sel);
        SelectProp(_propSelected);
    }

    private void AddPart(string jointId, MeshGeometry3D mesh, MaterialKind kind, bool uniform = false, bool dynamic = false)
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
        if (kind == MaterialKind.Hair || dynamic) gm.BackMaterial = _mats[kind]; // offene Haarkappe von innen nicht durchsichtig
        var b = mesh.Bounds;
        var p = new Part(gm, j, kind, uniform) { LocalPos = m.Positions.ToArray(), LocalNrm = m.Normals.ToArray(), Dynamic = dynamic,
            Small = !dynamic && Math.Max(b.SizeX, Math.Max(b.SizeY, b.SizeZ)) < 0.025 };
        j.Parts.Add(p);
        _parts[gm] = p;
        Group.Children.Add(gm);
    }

    /// <summary>Wechselt Figurenstil/Haare/Proportionen. Die Pose bleibt erhalten.</summary>
    public void SetFigure(FigureStyle style, bool hair, Proportions proportions)
    {
        bool rebuild = style != Style || hair != Hair || Group.Children.Count == 0 || proportions.ShapeKey != Proportions.ShapeKey;
        Style = style;
        Hair = hair;
        Proportions = proportions.Clone();
        Proportions.Clamp();
        if (rebuild) BuildParts();
        UpdateScales();
        Update();
    }

    public FigureData GetFigure() => new() { Style = Style, Hair = Hair, Proportions = Proportions.Clone(), HairStyle = HairStyle.Clone(), Clothing = Clothing.Clone(), SeatBlock = SeatBlock };

    /// <summary>Berechnet Segment-Skalierungen und Gelenkabstände aus den Proportionen
    /// und die Gesamtskalierung für die gewünschte Körpergröße.</summary>
    private void UpdateScales()
    {
        var p = Proportions;
        double a = p.Arm / 100, l = p.Leg / 100, t = p.Torso / 100, s = p.Shoulder / 100, h = p.Head / 100, b = p.Build / 100;
        // Anime-Proportionen: größerer Kopf, längere Beine, schmalere Schultern, etwas kürzerer Rumpf
        double neckLen = p.Neck / 100, hSeg = h;
        if (Style.IsAnime()) { h *= 1.32; l *= 1.06; s *= Style.IsFemale() ? 0.92 : 0.97; t *= 0.96; neckLen *= 0.85; }
        foreach (var j in Joints)
        {
            (j.SegScale, j.UniformScale) = j.Id switch
            {
                "pelvis" => (new Vector3D(b, 1, b), b),
                "spine" => (new Vector3D(b, t, b), b),
                "chest" => (new Vector3D(s * b, t, b), b),
                "neck" => (new Vector3D(b, neckLen, b), b),
                // Anime: nur die Kopfteile werden größer, nicht der Halsansatz im Kopf-System (sonst wird der Hals mitgedehnt)
                "head" => (new Vector3D(hSeg, hSeg, hSeg), h),
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
        // Finger: Hand wird gleichmäßig skaliert (Armlänge, Frauenhand etwas kleiner)
        foreach (var sfx in new[] { "_l", "_r" })
        {
            double hs = HandScale(sfx);
            foreach (var j in Joints.Where(j => j.IsFinger && j.Id.EndsWith(sfx)))
            {
                j.SegScale = new Vector3D(1, 1, 1);
                j.UniformScale = hs;
                j.Offset = j.BaseOffset * hs;
            }
        }
        _gripCache.Clear();

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
        if (j.IsFinger) m.Append(j.Rest);
        m.Translate(j.Parent == null ? j.Offset + RootPosition : j.Offset);
        return m;
    }

    /// <summary>Skalierung der Hand (Handfläche ist im Mesh bereits mit dem Stilfaktor gebaut, Finger nicht).</summary>
    internal double HandScale(string sfx) => this["wrist" + sfx].UniformScale * BodyBuilder.HandK(Style.IsFemale(), Style.IsAnime(), Proportions);

    private Vector3D _updatedRoot = new(double.NaN, 0, 0);

    public void Update()
    {
        _updatedRoot = RootPosition;
        if (_prop.Active) ApplyGripFingers();
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
                if (p.Dynamic) continue;
                if (p.Skin != null) { ApplySkin(p, j); continue; }
                var sc = p.Uniform ? new Vector3D(j.UniformScale, j.UniformScale, j.UniformScale) : j.SegScale;
                var m = Matrix3D.Identity;
                m.Scale(sc);
                ((MatrixTransform3D)p.Model.Transform).Matrix = m * j.World;
            }
        }
        var pw = PropWorld;
        foreach (var p in _propJoint.Parts) ((MatrixTransform3D)p.Model.Transform).Matrix = pw;
        UpdateSeat();
        UpdateHair();
        UpdateSkirt();
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
        bool[] gripped = { Grips(1), Grips(-1) };
        _prop = p.Clone();
        _prop.Clamp();
        // Hand lässt los: Finger wieder entspannt
        if (gripped[0] && !Grips(1)) ApplyGesture(1, HandGesture.All[0]);
        if (gripped[1] && !Grips(-1)) ApplyGesture(-1, HandGesture.All[0]);
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

        double hk = HandScale(sfx);
        var g0 = HandGeometry.Grip(s);
        var grip = new Point3D(g0.X * hk, g0.Y * hk, 0);
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

    // ------------------------------------------------------------------ Hände und Finger

    private bool Grips(int s) => _prop.Active && (_prop.TwoHanded || _prop.MainSide == s);

    private readonly Dictionary<string, (double Bend, double Side)> _gripCache = new();

    /// <summary>Greifende Hände: Finger legen sich im Bogen um den Griff, der Daumen darüber.</summary>
    private void ApplyGripFingers()
    {
        foreach (var s in new[] { 1, -1 })
        {
            if (!Grips(s)) continue;
            string sfx = s > 0 ? "_l" : "_r";
            if (!_gripCache.ContainsKey("index1" + sfx)) ComputeGrip(s);
            foreach (var j in Joints)
                if (j.IsFinger && _gripCache.TryGetValue(j.Id, out var v)) { j.BendValue = v.Bend; j.SideValue = v.Side; j.TwistValue = 0; }
        }
    }

    /// <summary>Lage eines Fingerpunktes im Handgelenk-System (Kette ab dem Handgelenk, Werte q je Kettengelenk als (Spreizen, Beugen)).</summary>
    private static Point3D FingerPoint(Joint[] chain, double[] q, int upTo, bool tip)
    {
        var m = Matrix3D.Identity;
        int n = Math.Min(upTo, chain.Length - 1);
        Matrix3D last = Matrix3D.Identity;
        for (int k = 0; k <= n; k++)
        {
            var j = chain[k];
            var l = Matrix3D.Identity;
            if (j.Side != null && q[2 * k + 1] != 0) l.Rotate(new Quaternion(new Vector3D(0, 0, 1), j.Side.Sign * q[2 * k + 1]));
            if (j.Bend != null && q[2 * k] != 0) l.Rotate(new Quaternion(new Vector3D(1, 0, 0), j.Bend.Sign * q[2 * k]));
            l.Append(j.Rest);
            l.Translate(j.Offset);
            m = l * m;
            last = m;
        }
        return tip ? last.Transform(new Point3D(0, -chain[n].BoneLength * chain[n].UniformScale, 0)) : last.Transform(new Point3D());
    }

    private void ComputeGrip(int s)
    {
        string sfx = s > 0 ? "_l" : "_r";
        double hs = HandScale(sfx);
        var c = HandGeometry.Grip(s);
        double cx = c.X * hs, cy = c.Y * hs, R = HandGeometry.GripRadius * hs;
        for (int f = 1; f <= 4; f++)
        {
            var chain = Enumerable.Range(1, 3).Select(k => this[$"{HandGeometry.Ids[f]}{k}{sfx}"]).ToArray();
            var defs = chain.Select(j => j.Side!).ToArray();
            double[] Res(double[] cur)
            {
                var q = new double[] { 0, cur[0], 0, cur[1], 0, cur[2] };
                var r = new List<double>();
                foreach (var (k, tip) in new[] { (1, false), (2, false), (2, true) })
                {
                    var p = FingerPoint(chain, q, k, tip);
                    r.Add((Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - R) / hs);
                }
                r.Add(0.0004 * (cur[0] - cur[1]));
                return r.ToArray();
            }
            var sol = SolveDls(Res, new double[] { 50, 60, 40 }, defs, 80);
            for (int k = 0; k < 3; k++) _gripCache[chain[k].Id] = (0, sol[k]);
        }
        // Daumen über die Finger
        var th = Enumerable.Range(1, 3).Select(k => this[$"thumb{k}{sfx}"]).ToArray();
        var t1 = new Point3D(-s * 0.033 * hs, -0.054 * hs, 0.043 * hs);
        var t2 = new Point3D(-s * 0.057 * hs, -0.08 * hs, 0.022 * hs);
        var tdefs = new[] { th[0].Bend!, th[0].Side!, th[1].Side!, th[2].Side! };
        double[] TRes(double[] cur)
        {
            var q = new double[] { cur[0], cur[1], 0, cur[2], 0, cur[3] };
            var a = FingerPoint(th, q, 1, false); var b = FingerPoint(th, q, 2, true);
            return new[] { (a.X - t1.X) / hs, (a.Y - t1.Y) / hs, (a.Z - t1.Z) / hs, (b.X - t2.X) / hs, (b.Y - t2.Y) / hs, (b.Z - t2.Z) / hs,
                           0.0003 * cur[2], 0.0003 * cur[3] };
        }
        var ts = SolveDls(TRes, new double[] { 10, 30, 30, 30 }, tdefs, 80);
        _gripCache[th[0].Id] = (ts[0], ts[1]);
        _gripCache[th[1].Id] = (0, ts[2]);
        _gripCache[th[2].Id] = (0, ts[3]);
    }

    private IEnumerable<Joint> FingerChain(int s, int finger) =>
        Enumerable.Range(1, 3).Select(k => this[$"{HandGeometry.Ids[finger]}{k}{(s > 0 ? "_l" : "_r")}"]);

    private static readonly double[] CurlFactor = { 0.85, 0.95, 0.65 };
    private static readonly double[] ThumbCurlFactor = { 0.45, 0.6, 0.8 };
    private static readonly double[] SpreadFactor = { 0.15, 0.03, 0.10, 0.20 };

    /// <summary>Krümmung eines Fingers in Prozent (0 = entspannt, 100 = eingerollt, negativ = gestreckt).</summary>
    public double GetFingerCurl(int s, int finger)
    {
        var ch = FingerChain(s, finger).ToArray();
        return finger == 0 ? ch[2].SideValue / ThumbCurlFactor[2] : ch[1].SideValue / CurlFactor[1];
    }

    public void SetFingerCurl(int s, int finger, double percent)
    {
        var f = finger == 0 ? ThumbCurlFactor : CurlFactor;
        int k = 0;
        foreach (var j in FingerChain(s, finger)) { j.SideValue = Clamp(j.Side, percent * f[k]); k++; }
    }

    /// <summary>Daumen abspreizen in Prozent (100 = 60°).</summary>
    public double GetThumbSpread(int s) => FingerChain(s, 0).First().BendValue / 0.6;
    public void SetThumbSpread(int s, double percent) { var j = FingerChain(s, 0).First(); j.BendValue = Clamp(j.Bend, percent * 0.6); }

    /// <summary>Finger spreizen in Prozent (negativ = zusammen).</summary>
    public double GetFingerSpread(int s) => FingerChain(s, 1).First().BendValue / SpreadFactor[0];
    public void SetFingerSpread(int s, double percent)
    {
        for (int f = 1; f <= 4; f++) { var j = FingerChain(s, f).First(); j.BendValue = Clamp(j.Bend, percent * SpreadFactor[f - 1]); }
    }

    /// <summary>Handhaltung auf eine Hand anwenden (s = 1 links, −1 rechts).</summary>
    public void ApplyGesture(int s, HandGesture g)
    {
        for (int f = 0; f < 5; f++)
            foreach (var j in FingerChain(s, f)) j.BendValue = j.SideValue = j.TwistValue = 0;
        for (int f = 0; f < 5; f++) SetFingerCurl(s, f, g.Curl(f));
        SetThumbSpread(s, g.ThumbSpread);
        SetFingerSpread(s, g.Spread);
    }

    /// <summary>Hält diese Hand ein Objekt (dann formt der Griff die Finger)?</summary>
    public bool HandGrips(int s) => Grips(s);

    // ------------------------------------------------------------------ Arm-/Bein-IK (Zweigelenk mit Ellbogen-/Kniestellung)

    private (Joint Upper, Joint Lower, Joint End, bool Arm)? LimbOf(Joint j)
    {
        if (j.Id.Length < 3) return null;
        string sfx = j.Id[^2..];
        if (j.Id.StartsWith("wrist") || j.Id.StartsWith("elbow") || j.Id.StartsWith("shoulder"))
            return (this["shoulder" + sfx], this["elbow" + sfx], this["wrist" + sfx], true);
        if (j.Id.StartsWith("ankle") || j.Id.StartsWith("knee") || j.Id.StartsWith("hip"))
            return (this["hip" + sfx], this["knee" + sfx], this["ankle" + sfx], false);
        return null;
    }

    /// <summary>Gehört das Gelenk zu Arm oder Bein (für das Schwenken von Ellbogen/Knie)?</summary>
    public bool IsLimbJoint(Joint j) => LimbOf(j) != null;

    private static Point3D Origin(Joint j) => j.World.Transform(new Point3D());

    private static Vector3D RotateAbout(Vector3D v, Vector3D axis, double deg)
    {
        var m = Matrix3D.Identity;
        m.Rotate(new Quaternion(axis, deg));
        return m.Transform(v);
    }

    private static double DistToSegment(Point3D p, Point3D a, Point3D b, out Point3D closest)
    {
        var ab = b - a;
        double t = Math.Clamp(Vector3D.DotProduct(p - a, ab) / Math.Max(ab.LengthSquared, 1e-9), 0, 1);
        closest = a + ab * t;
        return (p - closest).Length;
    }

    /// <summary>Rumpf als Kapsel (Becken bis obere Brust) für die Kollisionsprüfung der Arme.</summary>
    private (Point3D A, Point3D B, double R) TorsoCapsule()
    {
        var chest = this["chest"];
        var a = Origin(this["pelvis"]);
        var b = chest.World.Transform(new Point3D(0, 0.22, 0));
        double r = 0.12 * _globalScale * chest.SegScale.X * (Style == FigureStyle.Mannequin ? 1.15 : 1.0);
        return (a, b, r);
    }

    public string LastLimbDebug { get; private set; } = "";

    /// <summary>Hand (Handgelenk) oder Fuß (Knöchel) an einen Zielpunkt führen – Arm bzw. Bein folgen.
    /// Rückgabe: verbleibender Abstand zum Ziel in Metern.</summary>
    public double ReachTo(Joint end, Point3D target) => SolveLimb(end, target, 0);

    /// <summary>Ellbogen bzw. Knie um die Achse Schulter–Hand (Hüfte–Fuß) schwenken; Hand/Fuß bleiben stehen.</summary>
    public double SwivelLimb(Joint anyLimbJoint, double degrees)
    {
        if (LimbOf(anyLimbJoint) is not { } limb) return double.NaN;
        return SolveLimb(limb.End, Origin(limb.End), degrees);
    }

    /// <summary>Zweigelenk-IK wie in Animationsprogrammen:
    /// 1. Ellbogen/Knie liegt auf dem Kreis, der sich aus Ober-/Unterarmlänge ergibt; seine Richtung bleibt die bisherige
    ///    (kein Springen), optional um 'swivel' Grad geschwenkt.
    /// 2. Arme: Ellbogen und Unterarm weichen dem Rumpf aus, ein Handziel im Rumpf wird an die Oberfläche geschoben.
    /// 3. Gelenkwinkel werden so gelöst, dass Ellbogen und Hand exakt dort liegen.
    /// 4. Hand bzw. Fuß behalten ihre Ausrichtung im Raum (Unterarmdrehung und Handgelenk gleichen aus).</summary>
    private double SolveLimb(Joint end, Point3D target, double swivel)
    {
        if (LimbOf(end) is not { } limb) return double.NaN;
        var (upper, lower, _, arm) = limb;
        ComputeWorlds();
        var S = Origin(upper); var E0 = Origin(lower); var W0 = Origin(end);
        double l1 = (E0 - S).Length, l2 = (W0 - E0).Length;
        var endX0 = Axis(end.World, new Vector3D(1, 0, 0));
        var endY0 = Axis(end.World, new Vector3D(0, 1, 0));

        // Ziel aus dem Rumpf schieben (Hand liegt dann auf dem Körper statt darin)
        var cap = TorsoCapsule();
        if (arm && DistToSegment(target, cap.A, cap.B, out var cp) < cap.R + 0.04)
        {
            var outDir = target - cp;
            if (outDir.LengthSquared < 1e-8) outDir = Axis(this["chest"].World, new Vector3D(0, 0, 1));
            outDir.Normalize();
            target = cp + outDir * (cap.R + 0.04);
        }

        var D = target - S;
        double d = Math.Clamp(D.Length, Math.Abs(l1 - l2) + 0.01, l1 + l2 - 0.002);
        var a = D.LengthSquared > 1e-10 ? D / D.Length : new Vector3D(0, -1, 0);
        var T = S + a * d;

        // bisherige Ellbogenrichtung (senkrecht zur Achse) beibehalten
        var u = E0 - S;
        u -= a * Vector3D.DotProduct(u, a);
        if (u.Length < 1e-4)
        {
            var pole = arm
                ? -Axis(this["chest"].World, new Vector3D(0, 1, 0)) - Axis(this["chest"].World, new Vector3D(0, 0, 1)) * 0.3
                : Axis(this["pelvis"].World, new Vector3D(0, 0, 1));
            u = pole - a * Vector3D.DotProduct(pole, a);
        }
        u.Normalize();
        if (swivel != 0) u = RotateAbout(u, a, swivel);

        double cosA = Math.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1);
        double sinA = Math.Sqrt(1 - cosA * cosA);
        Point3D Elbow(Vector3D dir) => S + a * (l1 * cosA) + dir * (l1 * sinA);
        bool Clear(Point3D e) => !arm ||
            (DistToSegment(e, cap.A, cap.B, out _) > cap.R + 0.035 &&
             DistToSegment(e + (T - e) * 0.5, cap.A, cap.B, out _) > cap.R + 0.02);

        var E = Elbow(u);
        if (!Clear(E))
        {
            // nächstgelegene freie Ellbogenstellung suchen
            for (int k = 1; k <= 18; k++)
            {
                bool found = false;
                foreach (var sgn in new[] { 1, -1 })
                {
                    var cand = RotateAbout(u, a, sgn * k * 10);
                    if (Clear(Elbow(cand))) { u = cand; E = Elbow(cand); found = true; break; }
                }
                if (found) break;
            }
        }

        // Gelenkwinkel für Ellbogen- und Handposition
        var parentW = upper.Parent!.World;
        double keepTwist = lower.TwistValue;
        var posDefs = new[] { upper.Bend!, upper.Side!, upper.Twist!, lower.Bend! };
        var startQ = new[] { upper.BendValue, upper.SideValue, upper.TwistValue, lower.BendValue };
        Point3D eTarget = E;
        double[] PosRes(double[] q)
        {
            var wu = LocalMatrix(upper, q[0], q[1], q[2]) * parentW;
            var wl = LocalMatrix(lower, q[3], 0, keepTwist) * wu;
            var e = wl.Transform(new Point3D());
            var w = wl.Transform((Point3D)end.Offset);
            // Hand/Fuß hat Vorrang (Gewicht 4): bleibt exakt stehen, der Ellbogen folgt so gut es die Gelenke erlauben
            return new[] { eTarget.X - e.X, eTarget.Y - e.Y, eTarget.Z - e.Z, 4 * (T.X - w.X), 4 * (T.Y - w.Y), 4 * (T.Z - w.Z) };
        }
        double HandErr(double[] q) { var r = PosRes(q); return Math.Sqrt(r[3] * r[3] + r[4] * r[4] + r[5] * r[5]) / 4; }
        (double[] q, double hand) SolveFor(Vector3D dir)
        {
            eTarget = Elbow(dir);
            var best = SolveDls(PosRes, startQ, posDefs, 60);
            if (HandErr(best) > 0.005)
                foreach (var seed in new[] { new double[] { 40, 20, 40, 60 }, new double[] { 40, 20, -40, 60 }, new double[] { 90, 30, 0, 90 }, new double[] { 0, 60, 60, 90 } })
                {
                    var alt = SolveDls(PosRes, seed, posDefs, 60);
                    if (SqErr(PosRes(alt)) < SqErr(PosRes(best))) best = alt;
                }
            return (best, HandErr(best));
        }

        var sol = SolveFor(u);
        if (sol.hand > 0.005)
        {
            if (swivel != 0)
            {
                // gewünschter Schwenk ist anatomisch nicht erreichbar: größten möglichen Schwenk suchen (Hand bleibt stehen)
                var u0 = RotateAbout(u, a, -swivel);
                double lo = 0, hi = 1;
                var bestSol = SolveFor(u0);
                for (int i = 0; i < 7; i++)
                {
                    double mid = (lo + hi) / 2;
                    var trial = SolveFor(RotateAbout(u0, a, swivel * mid));
                    if (trial.hand <= 0.005) { lo = mid; bestSol = trial; } else hi = mid;
                }
                sol = bestSol;
            }
            else
            {
                // Hand ziehen: nächstgelegene erreichbare Ellbogenstellung suchen
                for (int k = 1; k <= 18 && sol.hand > 0.005; k++)
                    foreach (var sgn in new[] { 1, -1 })
                    {
                        var cand = RotateAbout(u, a, sgn * k * 10);
                        if (!Clear(Elbow(cand))) continue;
                        var trial = SolveFor(cand);
                        if (trial.hand < sol.hand) sol = trial;
                        if (sol.hand <= 0.005) break;
                    }
            }
        }
        var q = sol.q;
        (upper.BendValue, upper.SideValue, upper.TwistValue, lower.BendValue) = (q[0], q[1], q[2], q[3]);
        var rr = PosRes(q);
        LastLimbDebug = $"Ellbogen {Math.Sqrt(rr[0] * rr[0] + rr[1] * rr[1] + rr[2] * rr[2]) * 100:F1} cm, Hand {HandErr(q) * 100:F1} cm, " +
                        $"Schulter b/s/t {q[0]:F0}/{q[1]:F0}/{q[2]:F0}, Ellbogen {q[3]:F0}";
        ComputeWorlds();
        // Ausrichtung von Hand/Fuß wiederherstellen (Unterarm-/Unterschenkeldrehung + Hand-/Fußgelenk)
        var upperW = upper.World;
        var oriDefs = new[] { lower.Twist!, end.Bend!, end.Side! };
        double[] OriRes(double[] p)
        {
            var wl = LocalMatrix(lower, lower.BendValue, 0, p[0]) * upperW;
            var we = LocalMatrix(end, p[1], p[2], 0) * wl;
            var x = Axis(we, new Vector3D(1, 0, 0)); var y = Axis(we, new Vector3D(0, 1, 0));
            return new[] { x.X - endX0.X, x.Y - endX0.Y, x.Z - endX0.Z, y.X - endY0.X, y.Y - endY0.Y, y.Z - endY0.Z,
                           0.0015 * p[1], 0.0015 * p[2] };
        }
        var o = SolveDls(OriRes, new[] { lower.TwistValue, end.BendValue, end.SideValue }, oriDefs, 40);
        (lower.TwistValue, end.BendValue, end.SideValue) = (o[0], o[1], o[2]);

        Update();
        return (Origin(end) - target).Length;
    }
    // ------------------------------------------------------------------ Pose aus erkannten 3D-Punkten

    /// <summary>Gedämpfte Gauß-Newton-Iteration mit numerischer Jacobi-Matrix und Gelenkgrenzen.</summary>
    private static double[] SolveDls(Func<double[], double[]> residual, double[] start, AxisDef[] defs, int iterations = 60)
    {
        var q = (double[])start.Clone();
        int n = q.Length;
        for (int it = 0; it < iterations; it++)
        {
            var r = residual(q);
            int rows = r.Length;
            var J = new double[rows, n];
            for (int k = 0; k < n; k++)
            {
                var qq = (double[])q.Clone();
                qq[k] += 0.5;
                var r2 = residual(qq);
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
            double maxD = 0;
            for (int k = 0; k < n; k++)
            {
                double step = Math.Clamp(d[k], -10, 10);
                maxD = Math.Max(maxD, Math.Abs(step));
                q[k] = Math.Clamp(q[k] + step, defs[k].Min, defs[k].Max);
            }
            if (maxD < 0.01) break;
        }
        return q;
    }

    private static double SqErr(double[] r) => r.Sum(x => x * x);

    private static Vector3D Axis(Matrix3D m, Vector3D v) { var a = m.Transform(v); a.Normalize(); return a; }
    private static Vector3D Dir(Vector3D from, Vector3D to) { var d = to - from; if (d.LengthSquared > 1e-12) d.Normalize(); return d; }
    private static Vector3D Mid(Vector3D a, Vector3D b) => (a + b) / 2;

    /// <summary>Pose aus 33 erkannten Punkten (MediaPipe-Reihenfolge) übernehmen. Die Punkte müssen bereits
    /// in den Achsen dieser Szene liegen (X = linke Seite der Figur, Y oben, Z zur Kamera); verwendet werden nur Richtungen.
    /// Position der Figur bleibt erhalten, gehaltene Objekte steuern weiterhin ihre Hand.</summary>
    public List<string> FitLandmarks(Vector3D[] P, bool[]? visible = null)
    {
        // Nicht sichtbare Körperteile (außerhalb des Fotos oder unsicher) behalten ihre bisherige Haltung.
        bool V(int i) => visible == null || visible[i];
        var before = GetPose();
        var prevUp = Axis(this["pelvis"].World, new Vector3D(0, 1, 0));
        var kept = new List<string>();
        void Restore(params string[] ids)
        {
            foreach (var id in ids)
                if (before.Joints.TryGetValue(id, out var v)) { var j = this[id]; j.BendValue = v[0]; j.SideValue = v[1]; j.TwistValue = v[2]; }
        }
        bool hipsVis = V(23) && V(24), shVis = V(11) && V(12);
        if (!hipsVis && !shVis) { kept.Add("alles (Schultern und Hüften nicht sichtbar)"); return kept; }

        foreach (var j in Joints) j.BendValue = j.SideValue = j.TwistValue = 0;
        _ikEnabled = false;
        Update();

        // ---- Becken, Bauch, Brust (ohne Hüften: Becken aufrecht wie bisher, Drehung aus den Schultern)
        var pelvis = this["pelvis"]; var spine = this["spine"]; var chest = this["chest"];
        var shAcross = shVis ? Dir(P[12], P[11]) : Dir(P[24], P[23]);
        var hipAcross = hipsVis ? Dir(P[24], P[23]) : shAcross;
        var torsoUp = hipsVis && shVis ? Dir(Mid(P[23], P[24]), Mid(P[11], P[12])) : prevUp;
        if (!hipsVis) kept.Add("Becken (Hüften nicht sichtbar)");
        var torsoDefs = new[] { pelvis.Bend!, pelvis.Side!, pelvis.Twist!, spine.Bend!, spine.Side!, spine.Twist!, chest.Bend!, chest.Side!, chest.Twist! };
        double[] TorsoRes(double[] q)
        {
            var pw = LocalMatrix(pelvis, q[0], q[1], q[2]);
            var sw = LocalMatrix(spine, q[3], q[4], q[5]) * pw;
            var cw = LocalMatrix(chest, q[6], q[7], q[8]) * sw;
            var px = Axis(pw, new Vector3D(1, 0, 0)); var py = Axis(pw, new Vector3D(0, 1, 0));
            var cx = Axis(cw, new Vector3D(1, 0, 0)); var cy = Axis(cw, new Vector3D(0, 1, 0));
            const double wr = 0.004;
            return new[]
            {
                px.X - hipAcross.X, px.Y - hipAcross.Y, px.Z - hipAcross.Z,
                cx.X - shAcross.X, cx.Y - shAcross.Y, cx.Z - shAcross.Z,
                cy.X - torsoUp.X, cy.Y - torsoUp.Y, cy.Z - torsoUp.Z,
                0.35 * (py.X - torsoUp.X), 0.35 * (py.Y - torsoUp.Y), 0.35 * (py.Z - torsoUp.Z),
                wr * (q[3] - q[6]), wr * (q[4] - q[7]), wr * (q[5] - q[8])
            };
        }
        // Becken-Drehung grob vorbelegen (Blickrichtung aus der Hüftlinie), dann gemeinsam lösen
        double yaw0 = Math.Atan2(-hipAcross.Z, hipAcross.X) * 180 / Math.PI;
        var best = SolveDls(TorsoRes, new double[] { 0, 0, yaw0, 0, 0, 0, 0, 0, 0 }, torsoDefs, 80);
        foreach (var y0 in new[] { yaw0 + 180, yaw0 - 180 })
        {
            if (Math.Abs(y0) > 180) continue;
            var alt = SolveDls(TorsoRes, new double[] { 0, 0, y0, 0, 0, 0, 0, 0, 0 }, torsoDefs, 80);
            if (SqErr(TorsoRes(alt)) < SqErr(TorsoRes(best))) best = alt;
        }
        (pelvis.BendValue, pelvis.SideValue, pelvis.TwistValue) = (best[0], best[1], best[2]);
        (spine.BendValue, spine.SideValue, spine.TwistValue) = (best[3], best[4], best[5]);
        (chest.BendValue, chest.SideValue, chest.TwistValue) = (best[6], best[7], best[8]);
        ComputeWorlds();

        // ---- Hals und Kopf
        var neck = this["neck"]; var head = this["head"];
        bool headVis = V(0) && (V(7) || V(8));
        var earAcross = Dir(P[8], P[7]);
        var faceFwd = Dir(Mid(P[7], P[8]), P[0]);
        var cwFix = chest.World;
        var headDefs = new[] { neck.Bend!, neck.Side!, neck.Twist!, head.Bend!, head.Side!, head.Twist! };
        double[] HeadRes(double[] q)
        {
            var nw = LocalMatrix(neck, q[0], q[1], q[2]) * cwFix;
            var hw = LocalMatrix(head, q[3], q[4], q[5]) * nw;
            var hx = Axis(hw, new Vector3D(1, 0, 0)); var hz = Axis(hw, new Vector3D(0, 0, 1));
            const double wr = 0.004;
            return new[]
            {
                hx.X - earAcross.X, hx.Y - earAcross.Y, hx.Z - earAcross.Z,
                hz.X - faceFwd.X, hz.Y - faceFwd.Y, hz.Z - faceFwd.Z,
                wr * (q[0] - q[3]), wr * (q[1] - q[4]), wr * (q[2] - q[5])
            };
        }
        if (headVis)
        {
            var hq = SolveDls(HeadRes, new double[6], headDefs);
            (neck.BendValue, neck.SideValue, neck.TwistValue) = (hq[0], hq[1], hq[2]);
            (head.BendValue, head.SideValue, head.TwistValue) = (hq[3], hq[4], hq[5]);
        }
        else { Restore("neck", "head"); kept.Add("Kopf"); }
        ComputeWorlds();

        // ---- Arme und Beine: nur wenn das ganze Glied zu sehen ist
        foreach (var (s, a, b, c, t1, t2) in new[] { ("_l", 11, 13, 15, 17, 19), ("_r", 12, 14, 16, 18, 20) })
        {
            if (V(a) && V(b) && V(c))
            {
                FitLimb("shoulder" + s, "elbow" + s, "wrist" + s, P[a], P[b], P[c], Mid(P[t1], P[t2]), hand: true);
                if (!V(t1) && !V(t2)) Restore("wrist" + s);
            }
            else { Restore("shoulder" + s, "elbow" + s, "wrist" + s); kept.Add(s == "_l" ? "linker Arm" : "rechter Arm"); }
        }
        foreach (var (s, a, b, c, toe, heel) in new[] { ("_l", 23, 25, 27, 31, 29), ("_r", 24, 26, 28, 32, 30) })
        {
            if (V(a) && V(b) && V(c))
            {
                FitLimb("hip" + s, "knee" + s, "ankle" + s, P[a], P[b], P[c], P[toe], hand: false, heel: P[heel]);
                if (!V(toe)) Restore("ankle" + s);
            }
            else { Restore("hip" + s, "knee" + s, "ankle" + s); kept.Add(s == "_l" ? "linkes Bein" : "rechtes Bein"); }
        }
        // Finger übernimmt die Erkennung getrennt (FitHand); bis dahin bleiben sie, wie sie waren
        Restore(Joints.Where(j => j.IsFinger).Select(j => j.Id).ToArray());

        _ikEnabled = true;
        Update();
        return kept;
    }

    /// <summary>Ein Glied anpassen: mittleres Gelenk und Endgelenk auf die erkannten Richtungen (mit eigenen Knochenlängen),
    /// danach Hand- bzw. Fußrichtung.</summary>
    private void FitLimb(string upperId, string lowerId, string endId, Vector3D pA, Vector3D pB, Vector3D pC, Vector3D pTip,
        bool hand, Vector3D heel = default)
    {
        var upper = this[upperId]; var lower = this[lowerId]; var end = this[endId];
        var parentW = upper.Parent!.World;
        var a = upper.World.Transform(new Point3D());
        double l1 = (lower.World.Transform(new Point3D()) - a).Length;
        double l2 = (end.World.Transform(new Point3D()) - lower.World.Transform(new Point3D())).Length;
        var tB = a + Dir(pA, pB) * l1;
        var tC = tB + Dir(pB, pC) * l2;
        var defs = new[] { upper.Bend!, upper.Side!, upper.Twist!, lower.Bend!, lower.Twist! };
        double[] Res(double[] q)
        {
            var wu = LocalMatrix(upper, q[0], q[1], q[2]) * parentW;
            var wl = LocalMatrix(lower, q[3], 0, q[4]) * wu;
            var b = wl.Transform(new Point3D());
            var c = wl.Transform((Point3D)end.Offset);
            const double wp = 0.0005;
            return new[] { tB.X - b.X, tB.Y - b.Y, tB.Z - b.Z, tC.X - c.X, tC.Y - c.Y, tC.Z - c.Z, wp * q[2], wp * q[4] };
        }
        var best = SolveDls(Res, new double[5], defs);
        foreach (var seed in new[] { new double[] { 40, 20, 40, 60, 0 }, new double[] { 40, 20, -40, 60, 0 }, new double[] { 90, 10, 0, 30, 0 } })
        {
            if (SqErr(Res(best)) < 1e-4) break;
            var alt = SolveDls(Res, seed, defs);
            if (SqErr(Res(alt)) < SqErr(Res(best))) best = alt;
        }
        (upper.BendValue, upper.SideValue, upper.TwistValue) = (best[0], best[1], best[2]);
        (lower.BendValue, lower.TwistValue) = (best[3], best[4]);
        ComputeWorlds();

        // Hand: Richtung Handgelenk → Fingermitte = lokale −Y-Achse; Fuß: Ferse → Zehen = lokale +Z-Achse
        var target = hand ? Dir(pC, pTip) : Dir(heel, pTip);
        var lowerW = lower.World;
        var endDefs = new[] { end.Bend!, end.Side! };
        double[] EndRes(double[] q)
        {
            var we = LocalMatrix(end, q[0], q[1], 0) * lowerW;
            var ax = Axis(we, hand ? new Vector3D(0, -1, 0) : new Vector3D(0, 0, 1));
            return new[] { ax.X - target.X, ax.Y - target.Y, ax.Z - target.Z, 0.002 * q[0], 0.002 * q[1] };
        }
        var eq = SolveDls(EndRes, new double[2], endDefs, 40);
        (end.BendValue, end.SideValue) = (eq[0], eq[1]);
        ComputeWorlds();
    }

    /// <summary>Hand an 21 erkannte Punkte anpassen (Reihenfolge COCO-WholeBody: 0 Handgelenk, 1–4 Daumen, 5–8 Zeige-, 9–12 Mittel-,
    /// 13–16 Ring-, 17–20 kleiner Finger; jeweils Basis → Spitze), Punkte in Szenen-Achsen. Erst die Handausrichtung
    /// (Unterarmdrehung + Handgelenk), dann je Finger Spreizen und Beugen aus den Gliedrichtungen.
    /// Rückgabe: Anzahl angepasster Finger (0 = Hand zu unsicher oder hält ein Objekt).</summary>
    public int FitHand(int s, Vector3D[] P, double[] score, double minScore = 0.3)
    {
        if (Grips(s) || P.Length < 21 || score.Average() < minScore) return 0;
        string sfx = s > 0 ? "_l" : "_r";
        var wr = this["wrist" + sfx]; var el = this["elbow" + sfx];
        ComputeWorlds();

        // ---- Ausrichtung: −Y zur Mittelfinger-Basis, +Z vom kleinen Finger zum Zeigefinger
        var yT = -Dir(P[0], P[9]);
        var zT = Dir(P[17], P[5]);
        zT -= yT * Vector3D.DotProduct(zT, yT);
        if (zT.LengthSquared > 1e-8)
        {
            zT.Normalize();
            var upperW = el.Parent!.World;
            var defs = new[] { el.Twist!, wr.Bend!, wr.Side! };
            double[] OriRes(double[] q)
            {
                var wl = LocalMatrix(el, el.BendValue, 0, q[0]) * upperW;
                var we = LocalMatrix(wr, q[1], q[2], 0) * wl;
                var y = Axis(we, new Vector3D(0, 1, 0)); var z = Axis(we, new Vector3D(0, 0, 1));
                return new[] { y.X - yT.X, y.Y - yT.Y, y.Z - yT.Z, 0.7 * (z.X - zT.X), 0.7 * (z.Y - zT.Y), 0.7 * (z.Z - zT.Z),
                               0.002 * q[1], 0.002 * q[2] };
            }
            var best = SolveDls(OriRes, new[] { el.TwistValue, wr.BendValue, wr.SideValue }, defs, 60);
            foreach (var seed in new[] { new double[] { 60, 0, 0 }, new double[] { -60, 0, 0 } })
            {
                var alt = SolveDls(OriRes, seed, defs, 60);
                if (SqErr(OriRes(alt)) < SqErr(OriRes(best))) best = alt;
            }
            (el.TwistValue, wr.BendValue, wr.SideValue) = (best[0], best[1], best[2]);
            ComputeWorlds();
        }

        // ---- Finger: Gliedrichtungen im Handgelenk-System
        var inv = wr.World; inv.Invert();
        Vector3D L(int a, int b) { var d = inv.Transform(P[b] - P[a]); if (d.LengthSquared > 1e-14) d.Normalize(); return d; }
        static Vector3D D(Point3D a, Point3D b) { var d = b - a; if (d.LengthSquared > 1e-14) d.Normalize(); return d; }
        int fitted = 0;
        for (int f = 0; f < 5; f++)
        {
            int b = 1 + 4 * f;
            if (Enumerable.Range(b, 4).Min(i => score[i]) < minScore * 0.7) continue;
            var chain = FingerChain(s, f).ToArray();
            var t = new[] { L(b, b + 1), L(b + 1, b + 2), L(b + 2, b + 3) };
            var defs = new[] { chain[0].Bend!, chain[0].Side!, chain[1].Side!, chain[2].Side! };
            double[] Res(double[] q)
            {
                var qq = new[] { q[0], q[1], 0, q[2], 0, q[3] };
                var p0 = FingerPoint(chain, qq, 0, false); var p1 = FingerPoint(chain, qq, 1, false);
                var p2 = FingerPoint(chain, qq, 2, false); var p3 = FingerPoint(chain, qq, 2, true);
                var d = new[] { D(p0, p1), D(p1, p2), D(p2, p3) };
                var r = new List<double>();
                for (int k = 0; k < 3; k++) { r.Add(d[k].X - t[k].X); r.Add(d[k].Y - t[k].Y); r.Add(d[k].Z - t[k].Z); }
                foreach (var v in q) r.Add(0.002 * v);
                return r.ToArray();
            }
            var sol = SolveDls(Res, new[] { chain[0].BendValue, chain[0].SideValue, chain[1].SideValue, chain[2].SideValue }, defs, 60);
            var alt2 = SolveDls(Res, new double[] { 0, 40, 50, 30 }, defs, 60);
            if (SqErr(Res(alt2)) < SqErr(Res(sol))) sol = alt2;
            chain[0].BendValue = sol[0]; chain[0].SideValue = sol[1]; chain[1].SideValue = sol[2]; chain[2].SideValue = sol[3];
            fitted++;
        }
        Update();
        return fitted;
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
            if (p.Dynamic) continue; // Haarsträhnen zählen weder für Körpergröße noch Bodenhöhe
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
        if (RootPosition != _updatedRoot) Update(); // Position wurde ohne Neuberechnung geändert
        double minY = ComputeMinY();
        if (Math.Abs(minY) < 1e-6) return; // steht schon auf dem Boden: nicht noch einmal alles neu berechnen
        RootPosition = new Vector3D(RootPosition.X, RootPosition.Y - minY, RootPosition.Z);
        Update();
    }

    public Point3D PelvisWorld => Root.World.Transform(new Point3D(0, 0, 0));

    /// <summary>Nasenspitze in Weltkoordinaten (Anime-Kopf ist größer und sitzt tiefer auf dem Drehpunkt).</summary>
    public Point3D NoseWorld => Style.IsAnime()
        ? this["head"].World.Transform(new Point3D(0, (0.088 - BodyBuilder.AnimeHeadDrop) * this["head"].UniformScale, 0.09 * this["head"].UniformScale))
        : this["head"].World.Transform(new Point3D(0, 0.085, 0.1));

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
            if (p.Kind is MaterialKind.Hair or MaterialKind.Seat || IsCloth(p.Kind)) continue; // ControlNet: nackte Puppe wie im Trainingsmaterial
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
        if (pose.Prop != null) SetProp(pose.Prop, false); // zuerst: Loslassen setzt sonst die geladenen Finger zurück
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
