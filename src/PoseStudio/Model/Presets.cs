namespace PoseStudio.Model;

public sealed record PosePreset(string Name, IReadOnlyDictionary<string, (double Bend, double Side, double Twist)> Values)
{
    public PoseData ToPose()
    {
        var p = new PoseData();
        foreach (var (id, v) in Values) p.Joints[id] = new[] { v.Bend, v.Side, v.Twist };
        return p;
    }

    public override string ToString() => Name;
}

public static class Presets
{
    private static PosePreset Make(string name, params (string id, double b, double s, double t)[] v) =>
        new(name, v.ToDictionary(x => x.id, x => (x.b, x.s, x.t)));

    public static IReadOnlyList<PosePreset> All { get; } = new List<PosePreset>
    {
        Make("Neutral stehen (A-Pose)",
            ("shoulder_l", 0, 12, 0), ("shoulder_r", 0, 12, 0),
            ("elbow_l", 8, 0, 0), ("elbow_r", 8, 0, 0)),

        Make("T-Pose",
            ("shoulder_l", 0, 90, 0), ("shoulder_r", 0, 90, 0)),

        Make("Gehen",
            ("hip_l", 25, 0, 0), ("knee_l", 8, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -15, 0, 0), ("knee_r", 22, 0, 0), ("ankle_r", -12, 0, 0),
            ("shoulder_l", -22, 8, 0), ("shoulder_r", 25, 8, 0),
            ("elbow_l", 12, 0, 0), ("elbow_r", 25, 0, 0),
            ("chest", 0, 0, -6)),

        Make("Laufen",
            ("spine", 8, 0, 0), ("chest", 6, 0, -10), ("neck", -8, 0, 0),
            ("hip_l", 65, 0, 0), ("knee_l", 75, 0, 0), ("ankle_l", 10, 0, 0),
            ("hip_r", -25, 0, 0), ("knee_r", 45, 0, 0), ("ankle_r", -25, 0, 0),
            ("shoulder_l", -45, 10, 0), ("shoulder_r", 55, 10, 0),
            ("elbow_l", 95, 0, 0), ("elbow_r", 95, 0, 0)),

        Make("Sitzen (Stuhl)",
            ("hip_l", 90, 4, 0), ("hip_r", 90, 4, 0),
            ("knee_l", 90, 0, 0), ("knee_r", 90, 0, 0),
            ("shoulder_l", 25, 10, 0), ("shoulder_r", 25, 10, 0),
            ("elbow_l", 40, 0, 0), ("elbow_r", 40, 0, 0)),

        Make("Knien",
            ("hip_l", 90, 0, 0), ("knee_l", 90, 0, 0),
            ("hip_r", 0, 0, 0), ("knee_r", 90, 0, 0), ("ankle_r", -40, 0, 0),
            ("shoulder_l", 10, 10, 0), ("shoulder_r", 10, 10, 0),
            ("elbow_l", 30, 0, 0), ("elbow_r", 20, 0, 0)),

        Make("Winken",
            ("shoulder_l", 0, 10, 0), ("elbow_l", 10, 0, 0),
            ("shoulder_r", 0, 80, 90), ("elbow_r", 100, 0, 0), ("wrist_r", 0, 0, 0),
            ("head", 0, 8, 0)),

        Make("Arme hoch (Jubeln)",
            ("shoulder_l", 0, 155, 0), ("shoulder_r", 0, 155, 0),
            ("elbow_l", 15, 0, 0), ("elbow_r", 15, 0, 0),
            ("neck", -15, 0, 0), ("chest", -8, 0, 0)),

        Make("Hände in den Hüften",
            ("shoulder_l", -10, 38, -80), ("shoulder_r", -10, 38, -80),
            ("elbow_l", 100, 0, 0), ("elbow_r", 100, 0, 0),
            ("hip_l", 0, 8, 0), ("hip_r", 0, 8, 0)),

        Make("Kontrapost",
            ("pelvis", 0, 4, 0), ("chest", 0, -6, 0), ("head", 0, 4, 0),
            ("hip_l", 0, -4, 0), ("hip_r", 6, 4, 10), ("knee_r", 14, 0, 0), ("ankle_r", 4, 0, 0),
            ("shoulder_l", 0, 8, 0), ("shoulder_r", 0, 12, 0),
            ("elbow_l", 10, 0, 0), ("elbow_r", 15, 0, 0)),
    };
}
