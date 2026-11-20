namespace PoseStudio.Model;

public sealed record PosePreset(string Category, string Name, IReadOnlyDictionary<string, (double Bend, double Side, double Twist)> Values)
{
    /// <summary>Vollständige Pose (eigene Vorlagen, inkl. Objekt); sonst wird die Pose aus Values gebaut.</summary>
    public PoseData? Data { get; init; }
    /// <summary>Handhaltung links/rechts (Name aus <see cref="HandGesture.All"/>), null = Hände unverändert entspannt.</summary>
    public string? HandL { get; init; }
    public string? HandR { get; init; }

    public PoseData ToPose()
    {
        if (Data != null) return PoseLibrary.Clone(Data);
        var p = new PoseData();
        foreach (var (id, v) in Values) p.Joints[id] = new[] { v.Bend, v.Side, v.Twist };
        foreach (var (sfx, name) in new[] { ("_l", HandL), ("_r", HandR) })
            if (name != null && GestureJoints(name, sfx) is { } fingers)
                foreach (var (id, v) in fingers) p.Joints[id] = v;
        return p;
    }

    private static readonly Dictionary<string, Dictionary<string, double[]>> _gestureCache = new();

    /// <summary>Fingerwerte einer Handhaltung (einmal an einer Hilfsfigur ausgerechnet, dann zwischengespeichert).</summary>
    private static Dictionary<string, double[]>? GestureJoints(string name, string sfx)
    {
        string key = name + sfx;
        lock (_gestureCache)
        {
            if (_gestureCache.TryGetValue(key, out var hit)) return hit;
            var g = HandGesture.All.FirstOrDefault(x => x.Name == name);
            if (g == null) return null;
            var m = new Mannequin();
            m.ApplyGesture(sfx == "_l" ? 1 : -1, g);
            var d = m.Joints.Where(j => j.IsFinger && j.Id.EndsWith(sfx)).ToDictionary(j => j.Id, j => new[] { j.BendValue, j.SideValue, j.TwistValue });
            _gestureCache[key] = d;
            return d;
        }
    }

    public override string ToString() => Name;
}

/// <summary>Pose-Vorlagen. Werte je Gelenk: (Beugen, Seitlich, Drehen) in Grad, positive Werte = natürliche Richtung
/// (Arm/Bein nach vorne bzw. außen heben, Knie/Ellbogen beugen, Kopf/Brust drehen = zur linken Seite der Figur).
/// Die ersten zehn Einträge behalten ihre Reihenfolge (Selbsttest).</summary>
public static class Presets
{
    private const string Stand = "Stehen", Gest = "Gesten", Sit = "Sitzen & Knien", Lie = "Liegen", Move = "Bewegung",
        Fight = "Kampf & Action", Daily = "Alltag", Expr = "Ausdruck & Anime";

    /// <summary>Vorlage mit Handhaltungen (Namen aus HandGesture.All; null = keine Vorgabe).</summary>
    private static PosePreset MakeH(string cat, string name, string? handL, string? handR, params (string id, double b, double s, double t)[] v) =>
        Make(cat, name, v) with { HandL = handL, HandR = handR };

    private static PosePreset Make(string cat, string name, params (string id, double b, double s, double t)[] v) =>
        new(cat, name, v.ToDictionary(x => x.id, x => (x.b, x.s, x.t)));

    public static IReadOnlyList<PosePreset> All { get; } = new List<PosePreset>
    {
        // ---- 0..9: ursprüngliche Vorlagen
        Make(Stand, "Neutral stehen (A-Pose)",
            ("shoulder_l", 0, 12, 0), ("shoulder_r", 0, 12, 0),
            ("elbow_l", 8, 0, 0), ("elbow_r", 8, 0, 0)),

        Make(Stand, "T-Pose",
            ("shoulder_l", 0, 90, 0), ("shoulder_r", 0, 90, 0)),

        Make(Move, "Gehen",
            ("hip_l", 25, 0, 0), ("knee_l", 8, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -15, 0, 0), ("knee_r", 22, 0, 0), ("ankle_r", -12, 0, 0),
            ("shoulder_l", -22, 8, 0), ("shoulder_r", 25, 8, 0),
            ("elbow_l", 12, 0, 0), ("elbow_r", 25, 0, 0),
            ("chest", 0, 0, -6)),

        Make(Move, "Laufen",
            ("spine", 8, 0, 0), ("chest", 6, 0, -10), ("neck", -8, 0, 0),
            ("hip_l", 65, 0, 0), ("knee_l", 75, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -25, 0, 0), ("knee_r", 45, 0, 0), ("ankle_r", -25, 0, 0),
            ("shoulder_l", -45, 10, 0), ("shoulder_r", 55, 10, 0),
            ("elbow_l", 95, 0, 0), ("elbow_r", 95, 0, 0)),

        Make(Sit, "Sitzen (Stuhl)",
            ("hip_l", 90, 4, 0), ("hip_r", 90, 4, 0),
            ("knee_l", 90, 0, 0), ("knee_r", 90, 0, 0),
            ("shoulder_l", 25, 10, 0), ("shoulder_r", 25, 10, 0),
            ("elbow_l", 40, 0, 0), ("elbow_r", 40, 0, 0)),

        Make(Sit, "Knien (ein Knie)",
            ("hip_l", 90, 0, 0), ("knee_l", 90, 0, 0),
            ("hip_r", 0, 0, 0), ("knee_r", 90, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_l", 10, 10, 0), ("shoulder_r", 10, 10, 0),
            ("elbow_l", 30, 0, 0), ("elbow_r", 20, 0, 0)),

        Make(Gest, "Winken",
            ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0),
            ("shoulder_r", 0, 80, 90), ("elbow_r", 100, 0, 0), ("wrist_r", 0, 0, 0),
            ("head", 0, 8, 0)),

        Make(Gest, "Arme hoch (Jubeln)",
            ("shoulder_l", 0, 155, 0), ("shoulder_r", 0, 155, 0),
            ("elbow_l", 15, 0, 0), ("elbow_r", 15, 0, 0),
            ("neck", -15, 0, 0), ("chest", -8, 0, 0)),

        Make(Stand, "Hände in den Hüften",
            ("shoulder_l", -10, 38, -80), ("shoulder_r", -10, 38, -80),
            ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0),
            ("hip_l", 0, 8, 0), ("hip_r", 0, 8, 0)),

        Make(Stand, "Kontrapost",
            ("pelvis", 0, 4, 0), ("chest", 0, -6, 0), ("head", 0, 4, 0),
            ("hip_l", 0, -4, 0), ("hip_r", 6, 4, 10), ("knee_r", 14, 0, 0), ("ankle_r", 4, 0, 0),
            ("shoulder_l", 0, 8, 0), ("shoulder_r", 0, 12, 0),
            ("elbow_l", 10, 0, 0), ("elbow_r", 15, 0, 0)),

        // ---- Stehen
        Make(Stand, "Arme verschränkt",
            ("shoulder_l", 30, 12, -70), ("shoulder_r", 30, 12, -70),
            ("elbow_l", 125, 0, 0), ("elbow_r", 118, 0, 0),
            ("hip_l", 0, 5, 0), ("hip_r", 0, 5, 0), ("head", -4, 0, 0)),

        Make(Stand, "Hände hinter dem Kopf",
            ("shoulder_l", -40, 115, 90), ("shoulder_r", -40, 115, 90),
            ("elbow_l", 145, 0, 0), ("elbow_r", 145, 0, 0),
            ("chest", -6, 0, 0), ("head", -5, 0, 0), ("hip_l", 0, 4, 0), ("hip_r", 0, 4, 0)),

        Make(Stand, "Schulterblick",
            ("pelvis", 0, 0, -10), ("chest", 0, 0, 25), ("neck", 0, 0, 40), ("head", 5, 0, 35),
            ("shoulder_l", 0, 8, 0), ("shoulder_r", 0, 8, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0),
            ("hip_r", 5, 0, 0), ("knee_r", 10, 0, 0)),

        Make(Stand, "Lässig (Hand in der Tasche)",
            ("pelvis", 0, 5, 0), ("chest", 0, -5, 0), ("head", 0, 5, 0),
            ("hip_r", 8, 5, 15), ("knee_r", 15, 0, 0),
            ("shoulder_l", -12, 8, 0), ("elbow_l", 40, 0, 0), ("wrist_l", 20, 0, 0),
            ("shoulder_r", 0, 10, 0), ("elbow_r", 10, 0, 0)),

        Make(Stand, "Heldenpose",
            ("hip_l", 0, 18, 0), ("hip_r", 0, 18, 0),
            ("shoulder_l", -10, 38, -80), ("shoulder_r", -10, 38, -80),
            ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0),
            ("chest", -8, 0, 0), ("neck", -8, 0, 0), ("head", 0, 0, 15)),

        // ---- Gesten
        Make(Gest, "Zeigen",
            ("shoulder_r", 85, 10, 0), ("elbow_r", 5, 0, 0), ("wrist_r", 0, 0, 0),
            ("chest", 0, 0, -10), ("head", 0, 0, -5),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0)),

        Make(Gest, "Melden (Hand hoch)",
            ("shoulder_r", 10, 165, 0), ("elbow_r", 15, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0), ("head", -5, 0, 0)),

        Make(Gest, "Telefonieren",
            ("shoulder_r", 20, 40, -15), ("elbow_r", 150, 0, 0), ("wrist_r", 10, 0, 0), ("head", 5, -12, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 15, 0, 0)),

        Make(Gest, "Nachdenken (Hand am Kinn)",
            ("shoulder_r", 30, 8, -10), ("elbow_r", 140, 0, 0), ("wrist_r", -20, 0, 0),
            ("shoulder_l", 30, 8, -65), ("elbow_l", 95, 0, 0),
            ("head", 10, 6, 0), ("hip_l", 0, 4, 0), ("hip_r", 0, 4, 0)),

        Make(Gest, "Selfie",
            ("shoulder_r", 105, 12, 0), ("elbow_r", 15, 0, 0), ("wrist_r", 20, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 10, 0, 0), ("head", -5, 8, -10), ("chest", 0, 0, -10)),

        Make(Gest, "Verbeugung",
            ("spine", 35, 0, 0), ("chest", 20, 0, 0), ("neck", 10, 0, 0),
            ("shoulder_l", 25, 5, 0), ("shoulder_r", 25, 5, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0)),

        Make(Gest, "Kampfhaltung (Boxen)",
            ("pelvis", 0, 0, -25), ("chest", 5, 0, 15), ("head", 10, 0, 10),
            ("hip_l", 20, 8, 0), ("knee_l", 20, 0, 0),
            ("hip_r", -10, 10, 20), ("knee_r", 25, 0, 0),
            ("shoulder_l", 45, 25, -40), ("elbow_l", 120, 0, 0),
            ("shoulder_r", 30, 30, -40), ("elbow_r", 135, 0, 0)),

        // ---- Sitzen & Knien
        Make(Sit, "Sitzen am Boden (Knie angezogen)",
            ("hip_l", 110, 6, 0), ("hip_r", 110, 6, 0), ("knee_l", 125, 0, 0), ("knee_r", 125, 0, 0),
            ("spine", 15, 0, 0), ("chest", 10, 0, 0),
            ("shoulder_l", 55, 10, 0), ("shoulder_r", 55, 10, 0), ("elbow_l", 35, 0, 0), ("elbow_r", 35, 0, 0)),

        Make(Sit, "Schneidersitz",
            ("hip_l", 75, 40, 45), ("hip_r", 75, 40, 45), ("knee_l", 140, 0, 0), ("knee_r", 140, 0, 0),
            ("spine", 5, 0, 0),
            ("shoulder_l", 25, 12, 0), ("shoulder_r", 25, 12, 0), ("elbow_l", 50, 0, 0), ("elbow_r", 50, 0, 0)),

        Make(Sit, "Fersensitz",
            ("hip_l", 90, 4, 0), ("hip_r", 90, 4, 0), ("knee_l", 150, 0, 0), ("knee_r", 150, 0, 0),
            ("ankle_l", -40, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_l", 25, 8, 0), ("shoulder_r", 25, 8, 0), ("elbow_l", 45, 0, 0), ("elbow_r", 45, 0, 0)),

        Make(Sit, "Knien (beide Knie)",
            ("knee_l", 90, 0, 0), ("knee_r", 90, 0, 0), ("ankle_l", -40, 0, 0), ("ankle_r", -40, 0, 0),
            ("hip_l", 0, 4, 0), ("hip_r", 0, 4, 0),
            ("shoulder_l", 5, 10, 0), ("shoulder_r", 5, 10, 0), ("elbow_l", 15, 0, 0), ("elbow_r", 15, 0, 0)),

        Make(Sit, "Hocke",
            ("hip_l", 115, 15, 15), ("hip_r", 115, 15, 15), ("knee_l", 140, 0, 0), ("knee_r", 140, 0, 0),
            ("ankle_l", 30, 0, 0), ("ankle_r", 30, 0, 0),
            ("spine", 25, 0, 0), ("chest", 10, 0, 0), ("neck", -15, 0, 0),
            ("shoulder_l", 45, 10, 0), ("shoulder_r", 45, 10, 0), ("elbow_l", 30, 0, 0), ("elbow_r", 30, 0, 0)),

        Make(Sit, "Lässig sitzen (Bein überschlagen)",
            ("hip_l", 95, 0, 0), ("knee_l", 90, 0, 0),
            ("hip_r", 100, -18, 0), ("knee_r", 75, 0, 0),
            ("spine", -5, 0, 0),
            ("shoulder_l", 20, 15, 0), ("shoulder_r", 30, 5, 0), ("elbow_l", 50, 0, 0), ("elbow_r", 45, 0, 0)),

        // ---- Liegen
        Make(Lie, "Auf dem Rücken liegen",
            ("pelvis", -90, 0, 0),
            ("shoulder_l", 0, 20, 0), ("shoulder_r", 0, 20, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0),
            ("ankle_l", -20, 0, 0), ("ankle_r", -20, 0, 0)),

        Make(Lie, "Bauchlage (auf Ellbogen)",
            ("pelvis", 90, 0, 0), ("spine", -15, 0, 0), ("chest", -25, 0, 0), ("neck", -35, 0, 0), ("head", -10, 0, 0),
            ("shoulder_l", 70, 12, 0), ("shoulder_r", 70, 12, 0), ("elbow_l", 85, 0, 0), ("elbow_r", 85, 0, 0),
            ("ankle_l", -40, 0, 0), ("ankle_r", -40, 0, 0)),

        Make(Lie, "Seitenlage",
            ("pelvis", 0, 90, 0),
            ("hip_l", 25, 0, 0), ("knee_l", 40, 0, 0), ("hip_r", 40, 0, 0), ("knee_r", 60, 0, 0),
            ("shoulder_l", 80, 0, 0), ("elbow_l", 60, 0, 0), ("shoulder_r", 25, 5, 0), ("elbow_r", 40, 0, 0),
            ("neck", 0, 20, 0)),

        Make(Lie, "Zurückgelehnt (auf Ellbogen)",
            ("pelvis", -25, 0, 0), ("hip_l", 65, 0, 0), ("hip_r", 65, 0, 0),
            ("spine", -20, 0, 0), ("chest", -10, 0, 0), ("neck", 30, 0, 0), ("head", 10, 0, 0),
            ("shoulder_l", -60, 15, 0), ("shoulder_r", -60, 15, 0), ("elbow_l", 40, 0, 0), ("elbow_r", 40, 0, 0)),

        // ---- Bewegung
        Make(Move, "Ausfallschritt",
            ("hip_l", 80, 0, 0), ("knee_l", 85, 0, 0),
            ("hip_r", -25, 0, 0), ("knee_r", 70, 0, 0), ("ankle_r", -35, 0, 0),
            ("shoulder_l", -10, 30, -70), ("shoulder_r", -10, 30, -70), ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0)),

        Make(Move, "Sprint",
            ("pelvis", 15, 0, 0), ("spine", 10, 0, 0), ("chest", 5, 0, -12), ("neck", -20, 0, 0),
            ("hip_l", 85, 0, 0), ("knee_l", 100, 0, 0), ("ankle_l", 15, 0, 0),
            ("hip_r", -30, 0, 0), ("knee_r", 30, 0, 0), ("ankle_r", -35, 0, 0),
            ("shoulder_l", -60, 8, 0), ("shoulder_r", 70, 8, 0), ("elbow_l", 90, 0, 0), ("elbow_r", 90, 0, 0)),

        Make(Move, "Treppe steigen",
            ("hip_l", 55, 0, 0), ("knee_l", 70, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -5, 0, 0), ("knee_r", 10, 0, 0), ("ankle_r", -15, 0, 0),
            ("spine", 8, 0, 0), ("shoulder_l", -15, 8, 0), ("shoulder_r", 20, 8, 0), ("elbow_l", 15, 0, 0), ("elbow_r", 20, 0, 0)),

        Make(Move, "Tanzen",
            ("pelvis", 0, 8, 10), ("chest", 0, -10, -10), ("head", 0, 10, 10),
            ("hip_r", 30, 25, 20), ("knee_r", 40, 0, 0),
            ("shoulder_l", 20, 130, 30), ("elbow_l", 40, 0, 0),
            ("shoulder_r", 40, 60, 60), ("elbow_r", 70, 0, 0)),

        Make(Move, "Yoga – Baum",
            ("hip_r", 40, 65, 45), ("knee_r", 145, 0, 0), ("ankle_r", 10, 0, 0),
            ("shoulder_l", 0, 165, 0), ("shoulder_r", 0, 165, 0), ("elbow_l", 15, 0, 0), ("elbow_r", 15, 0, 0)),

        Make(Move, "Werfen (Ausholen)",
            ("pelvis", 0, 0, 30), ("chest", -5, 0, 25),
            ("hip_l", 30, 10, 0), ("knee_l", 15, 0, 0), ("hip_r", -10, 15, 25), ("knee_r", 20, 0, 0),
            ("shoulder_r", -40, 95, 90), ("elbow_r", 90, 0, 0),
            ("shoulder_l", 70, 20, 0), ("elbow_l", 20, 0, 0), ("head", 0, 0, -30)),

        // =========================================================== Erweiterung 0.20

        // ---- Stehen
        Make(Stand, "Hände vor dem Körper",
            ("shoulder_l", 25, 6, -50), ("shoulder_r", 25, 6, -50), ("elbow_l", 55, 0, 0), ("elbow_r", 55, 0, 0),
            ("hip_l", 0, 3, 0), ("hip_r", 0, 3, 0), ("head", 4, 0, 0)),

        Make(Stand, "Hände hinter dem Rücken",
            ("shoulder_l", -25, 14, 60), ("shoulder_r", -25, 14, 60), ("elbow_l", 70, 0, 0), ("elbow_r", 70, 0, 0),
            ("chest", -6, 0, 0), ("head", -3, 0, 0)),

        Make(Stand, "Seitlich gedreht, Blick zur Kamera",
            ("pelvis", 0, 0, 40), ("chest", 0, 0, -12), ("neck", 0, 0, -22), ("head", 3, 4, -12),
            ("hip_r", 5, 0, 0), ("knee_r", 12, 0, 0), ("shoulder_l", 0, 10, 0), ("shoulder_r", 0, 10, 0),
            ("elbow_l", 12, 0, 0), ("elbow_r", 12, 0, 0)),

        MakeH(Stand, "Model-Pose (Hand an der Hüfte)", "Flach (gestreckt)", null,
            ("pelvis", 0, 6, 10), ("chest", 0, -8, -6), ("head", 0, 6, -8),
            ("hip_l", 0, -4, 0), ("hip_r", 10, 6, 15), ("knee_r", 18, 0, 0),
            ("shoulder_l", -10, 38, -80), ("elbow_l", 100, 0, 0),
            ("shoulder_r", 0, 14, 0), ("elbow_r", 15, 0, 0)),

        Make(Stand, "Hand im Haar",
            ("pelvis", 0, 4, 0), ("chest", 0, -5, 0), ("head", 0, 10, 0),
            ("shoulder_r", -35, 112, 85), ("elbow_r", 140, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 15, 0, 0), ("hip_r", 6, 4, 10), ("knee_r", 14, 0, 0)),

        Make(Stand, "Strecken (Arme über dem Kopf)",
            ("shoulder_l", 0, 170, 0), ("shoulder_r", 0, 170, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0),
            ("chest", -8, 0, 0), ("neck", -12, 0, 0), ("ankle_l", -20, 0, 0), ("ankle_r", -20, 0, 0)),

        Make(Stand, "Rückenansicht – Blick über die Schulter",
            ("pelvis", 0, 0, 160), ("chest", 0, 0, 25), ("neck", 0, 0, 40), ("head", 5, 0, 30),
            ("hip_l", 6, 4, 10), ("knee_l", 12, 0, 0), ("shoulder_l", 0, 10, 0), ("shoulder_r", 0, 10, 0),
            ("elbow_l", 12, 0, 0), ("elbow_r", 12, 0, 0)),

        // ---- Gesten
        MakeH(Gest, "Peace-Zeichen", null, "Peace / Victory",
            ("shoulder_r", 25, 40, -20), ("elbow_r", 125, 0, 0), ("wrist_r", -10, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 15, 0, 0), ("head", 0, -8, 0)),

        MakeH(Gest, "Daumen hoch", null, "Daumen hoch",
            ("shoulder_r", 40, 15, -10), ("elbow_r", 80, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 12, 0, 0), ("head", -4, 0, 0)),

        MakeH(Gest, "Halt! (Hand nach vorne)", null, "Flach (gestreckt)",
            ("shoulder_r", 88, 8, 0), ("elbow_r", 5, 0, 0), ("wrist_r", -65, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 12, 0, 0), ("chest", 0, 0, -8)),

        MakeH(Gest, "Hand aufs Herz", null, "Flach (gestreckt)",
            ("shoulder_r", 30, 10, -75), ("elbow_r", 125, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 12, 0, 0), ("head", 6, 0, 0)),

        MakeH(Gest, "Mit beiden Händen winken", "Gespreizt", "Gespreizt",
            ("shoulder_l", 0, 110, 90), ("shoulder_r", 0, 110, 90), ("elbow_l", 75, 0, 0), ("elbow_r", 75, 0, 0),
            ("head", 0, 6, 0)),

        MakeH(Gest, "Klatschen", "Flach (gestreckt)", "Flach (gestreckt)",
            ("shoulder_l", 55, 10, -50), ("shoulder_r", 55, 10, -50), ("elbow_l", 70, 0, 0), ("elbow_r", 70, 0, 0)),

        MakeH(Gest, "Facepalm", null, "Flach (gestreckt)",
            ("shoulder_r", 35, 10, -20), ("elbow_r", 150, 0, 0), ("wrist_r", -15, 0, 0),
            ("neck", 15, 0, 0), ("head", 15, 0, 0), ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0)),

        MakeH(Gest, "Achselzucken", "Flach (gestreckt)", "Flach (gestreckt)",
            ("shoulder_l", 10, 25, 45), ("shoulder_r", 10, 25, 45), ("elbow_l", 90, 0, 0), ("elbow_r", 90, 0, 0),
            ("head", 0, 10, 0)),

        MakeH(Gest, "Nach oben zeigen", null, "Zeigen",
            ("shoulder_r", 20, 150, 0), ("elbow_r", 10, 0, 0),
            ("shoulder_l", 0, 12, 0), ("elbow_l", 12, 0, 0), ("neck", -15, 0, 0), ("head", -10, 0, 0)),

        MakeH(Gest, "Auf den Betrachter zeigen", null, "Zeigen",
            ("shoulder_r", 85, 8, 0), ("elbow_r", 8, 0, 0),
            ("shoulder_l", -10, 38, -80), ("elbow_l", 100, 0, 0), ("head", -4, 0, 0)),

        MakeH(Gest, "Salutieren", null, "Flach (gestreckt)",
            ("shoulder_r", 15, 80, 60), ("elbow_r", 130, 0, 0), ("wrist_r", 10, 0, 0),
            ("shoulder_l", 0, 6, 0), ("elbow_l", 5, 0, 0), ("chest", -5, 0, 0)),

        MakeH(Gest, "Fäuste geballt (entschlossen)", "Faust", "Faust",
            ("shoulder_l", 20, 15, -30), ("shoulder_r", 20, 15, -30), ("elbow_l", 110, 0, 0), ("elbow_r", 110, 0, 0),
            ("chest", -5, 0, 0), ("hip_l", 0, 10, 0), ("hip_r", 0, 10, 0)),

        // ---- Sitzen & Knien
        Make(Sit, "Sitzen – Beine übereinander (elegant)",
            ("hip_l", 90, 2, 0), ("knee_l", 85, 0, 0),
            ("hip_r", 98, -16, 0), ("knee_r", 80, 0, 0),
            ("shoulder_l", 30, 10, -30), ("shoulder_r", 30, 10, -30), ("elbow_l", 50, 0, 0), ("elbow_r", 50, 0, 0)),

        Make(Sit, "Sitzen – vorgebeugt (Ellbogen auf den Knien)",
            ("hip_l", 92, 8, 0), ("hip_r", 92, 8, 0), ("knee_l", 95, 0, 0), ("knee_r", 95, 0, 0),
            ("spine", 25, 0, 0), ("chest", 15, 0, 0), ("neck", -15, 0, 0),
            ("shoulder_l", 60, 10, -20), ("shoulder_r", 60, 10, -20), ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0)),

        Make(Sit, "Sitzen – zurückgelehnt",
            ("hip_l", 80, 6, 0), ("hip_r", 80, 6, 0), ("knee_l", 70, 0, 0), ("knee_r", 70, 0, 0),
            ("spine", -10, 0, 0), ("chest", -8, 0, 0), ("neck", 8, 0, 0),
            ("shoulder_l", -25, 25, 0), ("shoulder_r", -25, 25, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0)),

        Make(Sit, "Sitzen am Boden – Beine ausgestreckt",
            ("hip_l", 88, 4, 0), ("hip_r", 88, 4, 0), ("knee_l", 5, 0, 0), ("knee_r", 5, 0, 0),
            ("spine", 8, 0, 0), ("shoulder_l", -30, 20, 0), ("shoulder_r", -30, 20, 0), ("elbow_l", 8, 0, 0), ("elbow_r", 8, 0, 0)),

        Make(Sit, "Seitsitz (Beine zur Seite)",
            ("pelvis", 0, 8, 0), ("hip_l", 70, 25, 45), ("knee_l", 140, 0, 0), ("hip_r", 75, 10, 50), ("knee_r", 140, 0, 0),
            ("chest", 0, -8, 0), ("shoulder_r", 0, 25, 0), ("elbow_r", 5, 0, 0),
            ("shoulder_l", 30, 10, -40), ("elbow_l", 60, 0, 0), ("head", 0, 8, 0)),

        Make(Sit, "Sitzen – Knie umarmt",
            ("hip_l", 120, 4, 0), ("hip_r", 120, 4, 0), ("knee_l", 140, 0, 0), ("knee_r", 140, 0, 0),
            ("spine", 25, 0, 0), ("chest", 15, 0, 0), ("head", 15, 0, 0),
            ("shoulder_l", 70, 20, -40), ("shoulder_r", 70, 20, -40), ("elbow_l", 90, 0, 0), ("elbow_r", 90, 0, 0)),

        Make(Sit, "Sitzen – Kinn auf die Hand",
            ("hip_l", 90, 4, 0), ("hip_r", 90, 4, 0), ("knee_l", 90, 0, 0), ("knee_r", 90, 0, 0),
            ("spine", 15, 0, 0), ("chest", 8, 0, 0), ("head", 8, 8, 0),
            ("shoulder_r", 50, 10, -20), ("elbow_r", 140, 0, 0), ("wrist_r", -25, 0, 0),
            ("shoulder_l", 35, 10, -40), ("elbow_l", 70, 0, 0)),

        Make(Sit, "Knien – aufrecht, Hände auf den Oberschenkeln",
            ("hip_l", 85, 4, 0), ("hip_r", 85, 4, 0), ("knee_l", 150, 0, 0), ("knee_r", 150, 0, 0),
            ("ankle_l", -40, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_l", 15, 8, -30), ("shoulder_r", 15, 8, -30), ("elbow_l", 45, 0, 0), ("elbow_r", 45, 0, 0)),

        MakeH(Sit, "Auf einem Knie – Hand reichen", null, "Flach (gestreckt)",
            ("hip_l", 90, 0, 0), ("knee_l", 90, 0, 0),
            ("hip_r", 0, 0, 0), ("knee_r", 90, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_r", 70, 10, 30), ("elbow_r", 15, 0, 0), ("wrist_r", -20, 0, 0),
            ("shoulder_l", 10, 10, 0), ("elbow_l", 30, 0, 0), ("head", -6, 0, 0)),

        Make(Sit, "Hocke – Arme auf den Knien",
            ("hip_l", 115, 18, 15), ("hip_r", 115, 18, 15), ("knee_l", 140, 0, 0), ("knee_r", 140, 0, 0),
            ("ankle_l", 30, 0, 0), ("ankle_r", 30, 0, 0), ("spine", 20, 0, 0), ("neck", -15, 0, 0),
            ("shoulder_l", 60, 20, -30), ("shoulder_r", 60, 20, -30), ("elbow_l", 60, 0, 0), ("elbow_r", 60, 0, 0)),

        MakeH(Sit, "Knien – Gebet", "Flach (gestreckt)", "Flach (gestreckt)",
            ("knee_l", 90, 0, 0), ("knee_r", 90, 0, 0), ("ankle_l", -40, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_l", 40, 5, -50), ("shoulder_r", 40, 5, -50), ("elbow_l", 110, 0, 0), ("elbow_r", 110, 0, 0),
            ("neck", 15, 0, 0), ("head", 10, 0, 0)),

        // ---- Liegen
        Make(Lie, "Rückenlage – Arme hinter dem Kopf",
            ("pelvis", -90, 0, 0), ("shoulder_l", -40, 115, 90), ("shoulder_r", -40, 115, 90),
            ("elbow_l", 145, 0, 0), ("elbow_r", 145, 0, 0), ("hip_r", 35, 0, 0), ("knee_r", 70, 0, 0)),

        Make(Lie, "Rückenlage – Arme ausgebreitet",
            ("pelvis", -90, 0, 0), ("shoulder_l", 0, 95, 0), ("shoulder_r", 0, 95, 0),
            ("hip_l", 0, 18, 0), ("hip_r", 0, 18, 0), ("ankle_l", -25, 0, 0), ("ankle_r", -25, 0, 0)),

        Make(Lie, "Bauchlage – Kinn auf den Händen, Beine hoch",
            ("pelvis", 90, 0, 0), ("spine", -15, 0, 0), ("chest", -25, 0, 0), ("neck", -30, 0, 0), ("head", -10, 0, 0),
            ("shoulder_l", 110, 20, -40), ("shoulder_r", 110, 20, -40), ("elbow_l", 135, 0, 0), ("elbow_r", 135, 0, 0),
            ("knee_l", 100, 0, 0), ("knee_r", 70, 0, 0), ("ankle_l", -30, 0, 0), ("ankle_r", -30, 0, 0)),

        Make(Lie, "Seitenlage – Kopf auf der Hand",
            ("pelvis", 0, 90, 0), ("chest", 0, -10, 0),
            ("shoulder_r", 0, 165, 0), ("elbow_r", 140, 0, 0),
            ("shoulder_l", 30, 15, 0), ("elbow_l", 30, 0, 0),
            ("hip_l", 40, 0, 0), ("knee_l", 60, 0, 0), ("hip_r", 10, 0, 0), ("knee_r", 15, 0, 0)),

        Make(Lie, "Zusammengerollt",
            ("pelvis", 0, 90, 0), ("hip_l", 100, 0, 0), ("hip_r", 95, 0, 0), ("knee_l", 120, 0, 0), ("knee_r", 115, 0, 0),
            ("spine", 25, 0, 0), ("chest", 20, 0, 0), ("neck", 20, 0, 0),
            ("shoulder_l", 70, 10, -30), ("shoulder_r", 70, 10, -30), ("elbow_l", 110, 0, 0), ("elbow_r", 110, 0, 0)),

        // ---- Bewegung
        Make(Move, "Freudensprung",
            ("hip_l", -10, 4, 0), ("hip_r", -10, 4, 0), ("knee_l", 100, 0, 0), ("knee_r", 90, 0, 0),
            ("ankle_l", -30, 0, 0), ("ankle_r", -30, 0, 0),
            ("shoulder_l", 0, 150, 0), ("shoulder_r", 0, 150, 0), ("elbow_l", 15, 0, 0), ("elbow_r", 15, 0, 0),
            ("chest", -10, 0, 0), ("neck", -10, 0, 0)),

        Make(Move, "Sprung – Beine gespreizt",
            ("hip_l", 10, 45, 0), ("hip_r", 10, 45, 0), ("knee_l", 10, 0, 0), ("knee_r", 10, 0, 0),
            ("shoulder_l", 0, 125, 0), ("shoulder_r", 0, 125, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0),
            ("ankle_l", -30, 0, 0), ("ankle_r", -30, 0, 0)),

        Make(Move, "Pirouette",
            ("hip_r", 45, 45, 40), ("knee_r", 110, 0, 0), ("ankle_l", -25, 0, 0),
            ("shoulder_l", 60, 45, -40), ("shoulder_r", 60, 45, -40), ("elbow_l", 60, 0, 0), ("elbow_r", 60, 0, 0),
            ("chest", -5, 0, 0)),

        Make(Move, "Arabesque",
            ("pelvis", 20, 0, 0), ("spine", -10, 0, 0), ("chest", -15, 0, 0), ("neck", -15, 0, 0),
            ("hip_r", -65, 0, 0), ("knee_r", 5, 0, 0), ("ankle_r", -30, 0, 0),
            ("shoulder_l", 95, 10, 0), ("elbow_l", 5, 0, 0), ("shoulder_r", 0, 95, 0), ("elbow_r", 5, 0, 0)),

        Make(Move, "Laufen – Blick zurück",
            ("spine", 8, 0, 0), ("chest", 6, 0, 20), ("neck", -8, 0, 40), ("head", 0, 0, 30),
            ("hip_l", 65, 0, 0), ("knee_l", 75, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -25, 0, 0), ("knee_r", 45, 0, 0), ("ankle_r", -25, 0, 0),
            ("shoulder_l", -45, 10, 0), ("shoulder_r", 55, 10, 0), ("elbow_l", 95, 0, 0), ("elbow_r", 95, 0, 0)),

        Make(Move, "Gehen und winken",
            ("hip_l", 25, 0, 0), ("knee_l", 8, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -15, 0, 0), ("knee_r", 22, 0, 0), ("ankle_r", -12, 0, 0),
            ("shoulder_l", -20, 8, 0), ("elbow_l", 12, 0, 0),
            ("shoulder_r", 0, 80, 90), ("elbow_r", 100, 0, 0), ("head", 0, 0, -10)),

        Make(Move, "Hüpfen auf einem Bein",
            ("hip_r", 40, 0, 0), ("knee_r", 100, 0, 0), ("ankle_r", -20, 0, 0), ("knee_l", 10, 0, 0), ("ankle_l", -15, 0, 0),
            ("shoulder_l", 30, 35, 0), ("shoulder_r", 30, 35, 0), ("elbow_l", 30, 0, 0), ("elbow_r", 30, 0, 0)),

        Make(Move, "Stolpern",
            ("pelvis", 20, 0, 0), ("spine", 10, 0, 0), ("neck", -20, 0, 0),
            ("hip_l", 40, 0, 0), ("knee_l", 30, 0, 0), ("hip_r", -30, 0, 0), ("knee_r", 35, 0, 0),
            ("shoulder_l", 75, 25, 0), ("shoulder_r", 70, 25, 0), ("elbow_l", 20, 0, 0), ("elbow_r", 25, 0, 0)),

        Make(Move, "Ninja-Lauf (Arme nach hinten)",
            ("pelvis", 25, 0, 0), ("spine", 15, 0, 0), ("neck", -30, 0, 0),
            ("hip_l", 80, 0, 0), ("knee_l", 95, 0, 0), ("hip_r", -30, 0, 0), ("knee_r", 40, 0, 0), ("ankle_r", -35, 0, 0),
            ("shoulder_l", -60, 12, 0), ("shoulder_r", -60, 12, 0), ("elbow_l", 10, 0, 0), ("elbow_r", 10, 0, 0)),

        // ---- Kampf & Action
        MakeH(Fight, "Kampfstellung (Fäuste)", "Faust", "Faust",
            ("pelvis", 0, 0, -30), ("chest", 3, 0, 20), ("head", 8, 0, 12),
            ("hip_l", 25, 8, 0), ("knee_l", 25, 0, 0), ("hip_r", -12, 12, 20), ("knee_r", 28, 0, 0),
            ("shoulder_l", 50, 25, -40), ("elbow_l", 115, 0, 0), ("shoulder_r", 35, 30, -40), ("elbow_r", 130, 0, 0)),

        MakeH(Fight, "Faustschlag", "Faust", "Faust",
            ("pelvis", 0, 0, -20), ("chest", 5, 0, -25), ("head", 5, 0, 15),
            ("hip_l", 40, 8, 0), ("knee_l", 35, 0, 0), ("hip_r", -20, 10, 15), ("knee_r", 15, 0, 0),
            ("shoulder_r", 88, 5, 0), ("elbow_r", 5, 0, 0), ("shoulder_l", 40, 25, -40), ("elbow_l", 120, 0, 0)),

        MakeH(Fight, "Tritt nach vorne", "Faust", "Faust",
            ("pelvis", -10, 0, 0), ("chest", 10, 0, 0),
            ("hip_r", 100, 0, 0), ("knee_r", 10, 0, 0), ("ankle_r", 25, 0, 0), ("knee_l", 10, 0, 0),
            ("shoulder_l", 45, 25, -40), ("elbow_l", 110, 0, 0), ("shoulder_r", 40, 25, -40), ("elbow_r", 110, 0, 0)),

        MakeH(Fight, "Seitwärtstritt", "Faust", "Faust",
            ("pelvis", 0, -25, 0), ("chest", 0, 20, 0), ("head", 0, 10, 0),
            ("hip_r", 15, 85, 0), ("knee_r", 10, 0, 0), ("knee_l", 15, 0, 0),
            ("shoulder_l", 45, 25, -40), ("elbow_l", 110, 0, 0), ("shoulder_r", 30, 30, -40), ("elbow_r", 120, 0, 0)),

        MakeH(Fight, "Abwehr (Arme vor dem Gesicht)", "Faust", "Faust",
            ("shoulder_l", 80, 20, -60), ("shoulder_r", 80, 20, -60), ("elbow_l", 110, 0, 0), ("elbow_r", 110, 0, 0),
            ("spine", 8, 0, 0), ("neck", 10, 0, 0), ("hip_l", 15, 8, 0), ("knee_l", 20, 0, 0), ("knee_r", 15, 0, 0)),

        MakeH(Fight, "Magie wirken (Hand nach vorne)", "Kralle", "Gespreizt",
            ("pelvis", 0, 0, -15), ("chest", 0, 0, -10), ("head", 0, 0, 10),
            ("hip_l", 25, 10, 0), ("knee_l", 20, 0, 0), ("hip_r", -10, 12, 0), ("knee_r", 15, 0, 0),
            ("shoulder_r", 90, 10, 0), ("elbow_r", 5, 0, 0), ("wrist_r", -60, 0, 0),
            ("shoulder_l", 30, 20, -30), ("elbow_l", 90, 0, 0)),

        MakeH(Fight, "Magie – Hände über dem Kopf", "Gespreizt", "Gespreizt",
            ("shoulder_l", 0, 160, 0), ("shoulder_r", 0, 160, 0), ("elbow_l", 20, 0, 0), ("elbow_r", 20, 0, 0),
            ("chest", -10, 0, 0), ("neck", -15, 0, 0), ("hip_l", 0, 10, 0), ("hip_r", 0, 10, 0)),

        MakeH(Fight, "Energiekugel (Hände an der Seite)", "Kralle", "Kralle",
            ("pelvis", 0, 0, 30), ("chest", 0, 0, 15), ("head", 0, 0, -35),
            ("hip_l", 20, 15, 0), ("knee_l", 40, 0, 0), ("hip_r", 15, 15, 0), ("knee_r", 40, 0, 0),
            ("shoulder_l", -20, 30, -60), ("elbow_l", 100, 0, 0), ("shoulder_r", -15, 25, -60), ("elbow_r", 105, 0, 0)),

        MakeH(Fight, "Landung (Hand am Boden)", "Flach (gestreckt)", "Faust",
            ("hip_l", 110, 15, 0), ("knee_l", 140, 0, 0), ("ankle_l", 30, 0, 0),
            ("hip_r", 35, 35, 0), ("knee_r", 120, 0, 0), ("ankle_r", -20, 0, 0),
            ("spine", 30, 0, 0), ("chest", 10, 0, 0), ("neck", -25, 0, 0),
            ("shoulder_l", 70, 15, 0), ("elbow_l", 10, 0, 0), ("shoulder_r", -40, 45, 0), ("elbow_r", 20, 0, 0)),

        MakeH(Fight, "Siegerpose (Faust in die Luft)", "Flach (gestreckt)", "Faust",
            ("shoulder_r", 0, 165, 0), ("elbow_r", 10, 0, 0),
            ("shoulder_l", -10, 38, -80), ("elbow_l", 100, 0, 0),
            ("hip_l", 0, 12, 0), ("hip_r", 0, 12, 0), ("chest", -8, 0, 0), ("neck", -10, 0, 0)),

        MakeH(Fight, "Bogenschießen", "Faust", "Locker greifen",
            ("pelvis", 0, 0, -80), ("head", 0, 0, 75), ("neck", 0, 0, 10),
            ("hip_l", 0, 15, 0), ("hip_r", 0, 15, 0),
            ("shoulder_l", 0, 90, 0), ("elbow_l", 0, 0, 0),
            ("shoulder_r", 0, 90, -80), ("elbow_r", 145, 0, 0)),

        MakeH(Fight, "Zielen (beidhändig)", "Locker greifen", "Pistole",
            ("shoulder_l", 88, -10, 0), ("shoulder_r", 88, -6, 0), ("elbow_l", 12, 0, 0), ("elbow_r", 5, 0, 0),
            ("pelvis", 0, 0, -10), ("hip_l", 15, 10, 0), ("knee_l", 15, 0, 0), ("hip_r", -5, 10, 0), ("knee_r", 10, 0, 0)),

        MakeH(Fight, "Ausholen über dem Kopf", "Faust", "Faust",
            ("shoulder_l", 160, 10, 0), ("shoulder_r", 160, 10, 0), ("elbow_l", 25, 0, 0), ("elbow_r", 25, 0, 0),
            ("spine", -10, 0, 0), ("chest", -10, 0, 0),
            ("hip_l", 30, 10, 0), ("knee_l", 30, 0, 0), ("hip_r", -15, 10, 0), ("knee_r", 15, 0, 0)),

        // ---- Alltag
        MakeH(Daily, "Aufs Handy schauen", null, "Locker greifen",
            ("shoulder_r", 35, 5, -30), ("elbow_r", 110, 0, 0), ("wrist_r", -10, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 12, 0, 0), ("neck", 15, 0, 0), ("head", 15, 0, 0)),

        MakeH(Daily, "Trinken", null, "Locker greifen",
            ("shoulder_r", 30, 15, -20), ("elbow_r", 135, 0, 0), ("wrist_r", 10, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 12, 0, 0), ("head", -10, 0, 0)),

        MakeH(Daily, "Lesen (Buch in beiden Händen)", "Locker greifen", "Locker greifen",
            ("shoulder_l", 40, 8, -40), ("shoulder_r", 40, 8, -40), ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0),
            ("neck", 15, 0, 0), ("head", 12, 0, 0)),

        MakeH(Daily, "Tasche über der Schulter", null, "Faust",
            ("shoulder_r", -5, 10, -20), ("elbow_r", 120, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 12, 0, 0), ("hip_r", 6, 4, 10), ("knee_r", 12, 0, 0)),

        Make(Daily, "Gähnen und strecken",
            ("shoulder_l", 0, 170, 0), ("elbow_l", 30, 0, 0),
            ("shoulder_r", 60, 30, -60), ("elbow_r", 140, 0, 0), ("neck", -15, 0, 0), ("chest", -5, 0, 0)),

        Make(Daily, "Haare richten (beide Hände)",
            ("shoulder_l", -10, 110, 70), ("shoulder_r", -10, 110, 70), ("elbow_l", 130, 0, 0), ("elbow_r", 130, 0, 0),
            ("head", 5, 6, 0), ("hip_r", 6, 4, 10), ("knee_r", 12, 0, 0)),

        Make(Daily, "Etwas aufheben (bücken)",
            ("hip_l", 70, 6, 0), ("hip_r", 60, 6, 0), ("knee_l", 40, 0, 0), ("knee_r", 25, 0, 0),
            ("spine", 40, 0, 0), ("chest", 20, 0, 0), ("neck", -20, 0, 0),
            ("shoulder_r", 70, 10, 0), ("elbow_r", 10, 0, 0), ("shoulder_l", 40, 10, 0), ("elbow_l", 30, 0, 0)),

        MakeH(Daily, "Etwas an die Brust drücken", "Locker greifen", "Locker greifen",
            ("shoulder_l", 40, 15, -60), ("shoulder_r", 40, 15, -60), ("elbow_l", 120, 0, 0), ("elbow_r", 120, 0, 0),
            ("head", 10, 6, 0)),

        MakeH(Daily, "Regenschirm halten", null, "Faust",
            ("shoulder_r", 30, 20, -10), ("elbow_r", 100, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 12, 0, 0), ("head", -8, 0, 0)),

        Make(Daily, "Gehen mit Händen in den Taschen",
            ("hip_l", 22, 0, 0), ("knee_l", 8, 0, 0), ("hip_r", -12, 0, 0), ("knee_r", 20, 0, 0), ("ankle_r", -10, 0, 0),
            ("shoulder_l", -12, 10, 0), ("elbow_l", 40, 0, 0), ("wrist_l", 20, 0, 0),
            ("shoulder_r", -12, 10, 0), ("elbow_r", 40, 0, 0), ("wrist_r", 20, 0, 0), ("head", 6, 0, 0)),

        // ---- Ausdruck & Anime
        MakeH(Expr, "Niedlich – Hände vor dem Mund", "Leicht gekrümmt", "Leicht gekrümmt",
            ("shoulder_l", 40, 10, -50), ("shoulder_r", 40, 10, -50), ("elbow_l", 140, 0, 0), ("elbow_r", 140, 0, 0),
            ("head", 10, 10, 0), ("hip_l", 0, -6, 0), ("hip_r", 0, -6, 0), ("knee_l", 12, 0, 0), ("knee_r", 12, 0, 0)),

        MakeH(Expr, "Katzenpose", "Kralle", "Kralle",
            ("shoulder_l", 45, 20, -40), ("shoulder_r", 45, 20, -40), ("elbow_l", 120, 0, 0), ("elbow_r", 120, 0, 0),
            ("wrist_l", 50, 0, 0), ("wrist_r", 50, 0, 0), ("head", 0, 15, 0), ("hip_r", 10, 4, 10), ("knee_r", 20, 0, 0)),

        MakeH(Expr, "Peace am Auge (zwinkern)", "Flach (gestreckt)", "Peace / Victory",
            ("shoulder_r", 30, 45, -20), ("elbow_r", 140, 0, 0),
            ("shoulder_l", -10, 38, -80), ("elbow_l", 100, 0, 0),
            ("head", 0, -12, 0), ("pelvis", 0, 5, 0), ("hip_r", 8, 4, 10), ("knee_r", 15, 0, 0)),

        MakeH(Expr, "Überrascht (Hände hoch)", "Gespreizt", "Gespreizt",
            ("shoulder_l", 30, 60, 30), ("shoulder_r", 30, 60, 30), ("elbow_l", 90, 0, 0), ("elbow_r", 90, 0, 0),
            ("chest", -10, 0, 0), ("neck", -5, 0, 0)),

        MakeH(Expr, "Wütend (Fäuste nach unten)", "Faust", "Faust",
            ("shoulder_l", -8, 18, 0), ("shoulder_r", -8, 18, 0), ("elbow_l", 30, 0, 0), ("elbow_r", 30, 0, 0),
            ("chest", 8, 0, 0), ("head", 10, 0, 0), ("hip_l", 0, 8, 0), ("hip_r", 0, 8, 0)),

        Make(Expr, "Traurig (Kopf gesenkt)",
            ("spine", 10, 0, 0), ("chest", 10, 0, 0), ("neck", 25, 0, 0), ("head", 15, 0, 0),
            ("shoulder_l", 5, 5, 0), ("shoulder_r", 5, 5, 0), ("elbow_l", 5, 0, 0), ("elbow_r", 5, 0, 0)),

        MakeH(Expr, "Verlegen (Hand am Hinterkopf)", null, "Leicht gekrümmt",
            ("shoulder_r", -30, 110, 90), ("elbow_r", 140, 0, 0),
            ("shoulder_l", 0, 10, 0), ("elbow_l", 12, 0, 0), ("head", 5, 10, 0), ("hip_l", 0, -4, 0), ("knee_l", 10, 0, 0)),

        Make(Expr, "Selbstbewusst (Arme verschränkt, Kontrapost)",
            ("pelvis", 0, 5, 0), ("chest", 0, -6, 0), ("head", -6, 5, 0),
            ("hip_r", 8, 6, 12), ("knee_r", 15, 0, 0),
            ("shoulder_l", 30, 12, -70), ("shoulder_r", 30, 12, -70), ("elbow_l", 125, 0, 0), ("elbow_r", 118, 0, 0)),

        MakeH(Expr, "Herz mit den Händen", "Leicht gekrümmt", "Leicht gekrümmt",
            ("shoulder_l", 70, 15, -60), ("shoulder_r", 70, 15, -60), ("elbow_l", 90, 0, 0), ("elbow_r", 90, 0, 0),
            ("head", 0, 10, 0)),

        MakeH(Expr, "Gähnend müde (Hand vor dem Mund)", null, "Flach (gestreckt)",
            ("shoulder_r", 35, 10, -40), ("elbow_r", 145, 0, 0), ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0),
            ("neck", -12, 0, 0), ("head", -8, 6, 0)),
    };
}
