namespace PoseStudio.Model;

public enum FigureStyle
{
    Mannequin,
    Male,
    Female
}

public enum MaterialKind
{
    Body,
    Joint,
    Lips,
    EyeWhite,
    Iris,
    Hair,
    PropWood,
    PropMetal,
    PropGold,
    PropGem
}

/// <summary>Körperproportionen. Prozentwerte: 100 = Standard.</summary>
public sealed class Proportions
{
    public double HeightCm { get; set; } = 180;
    public double Arm { get; set; } = 100;
    public double Leg { get; set; } = 100;
    public double Torso { get; set; } = 100;
    public double Shoulder { get; set; } = 100;
    public double Head { get; set; } = 100;
    public double Build { get; set; } = 100;

    public Proportions Clone() => (Proportions)MemberwiseClone();

    public void Clamp()
    {
        HeightCm = Math.Clamp(HeightCm, 50, 230);
        Arm = Math.Clamp(Arm, 70, 130);
        Leg = Math.Clamp(Leg, 70, 130);
        Torso = Math.Clamp(Torso, 75, 125);
        Shoulder = Math.Clamp(Shoulder, 75, 130);
        Head = Math.Clamp(Head, 75, 140);
        Build = Math.Clamp(Build, 70, 150);
    }
}

public sealed class FigureData
{
    public FigureStyle Style { get; set; } = FigureStyle.Mannequin;
    public bool Hair { get; set; } = true;
    public Proportions Proportions { get; set; } = new();
}
