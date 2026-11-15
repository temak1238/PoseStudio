namespace PoseStudio.Model;

public sealed record PosePreset(string Category, string Name, IReadOnlyDictionary<string, (double Bend, double Side, double Twist)> Values)
{
    public PoseData ToPose()
    {
        var p = new PoseData();
        foreach (var (id, v) in Values) p.Joints[id] = new[] { v.Bend, v.Side, v.Twist };
        return p;
    }

    public override string ToString() => Name;
}

/// <summary>Pose-Vorlagen. Werte je Gelenk: (Beugen, Seitlich, Drehen) in Grad, positive Werte = natürliche Richtung
/// (Arm/Bein nach vorne bzw. außen heben, Knie/Ellbogen beugen, Kopf/Brust drehen = zur linken Seite der Figur).
/// Die ersten zehn Einträge behalten ihre Reihenfolge (Selbsttest).</summary>
public static class Presets
{
    private const string Stand = "Stehen", Gest = "Gesten", Sit = "Sitzen & Knien", Lie = "Liegen", Move = "Bewegung";

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
    };
}
