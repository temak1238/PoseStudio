namespace PoseStudio.Model;

public enum FigureStyle
{
    Mannequin,
    Male,
    Female,
    AnimeFemale,
    AnimeMale
}

public static class FigureStyleExtensions
{
    public static bool IsFemale(this FigureStyle s) => s is FigureStyle.Female or FigureStyle.AnimeFemale;
    public static bool IsAnime(this FigureStyle s) => s is FigureStyle.AnimeFemale or FigureStyle.AnimeMale;
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
    PropGem,
    /// <summary>Dunkle Zeichenlinien im Anime-Gesicht (Wimpernlinie).</summary>
    Line,
    /// <summary>Anime-Mund.</summary>
    Mouth,
    /// <summary>Oberteil.</summary>
    ClothTop,
    /// <summary>Hose, Rock oder Kleid.</summary>
    ClothBottom,
    /// <summary>Schuhe, Stiefel, Strümpfe.</summary>
    Shoes,
    /// <summary>Sitzblock.</summary>
    Seat
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
    // Körperform (ab 0.20), Prozent: 100 = Standard
    public double Bust { get; set; } = 100;
    public double Waist { get; set; } = 100;
    public double Hips { get; set; } = 100;
    public double Glutes { get; set; } = 100;
    public double Belly { get; set; } = 100;
    public double Thighs { get; set; } = 100;
    public double Calves { get; set; } = 100;
    public double UpperArms { get; set; } = 100;
    public double Neck { get; set; } = 100;
    public double Hands { get; set; } = 100;

    /// <summary>Werte, die die Form der Körperteile ändern (Neuaufbau der Meshes nötig).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ShapeKey => FormattableString.Invariant($"{Bust:F1}|{Waist:F1}|{Hips:F1}|{Glutes:F1}|{Belly:F1}|{Thighs:F1}|{Calves:F1}|{UpperArms:F1}|{Hands:F1}");

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
        Bust = Math.Clamp(Bust, 30, 220);
        Waist = Math.Clamp(Waist, 70, 140);
        Hips = Math.Clamp(Hips, 75, 150);
        Glutes = Math.Clamp(Glutes, 60, 170);
        Belly = Math.Clamp(Belly, 70, 180);
        Thighs = Math.Clamp(Thighs, 70, 160);
        Calves = Math.Clamp(Calves, 70, 160);
        UpperArms = Math.Clamp(UpperArms, 70, 170);
        Neck = Math.Clamp(Neck, 60, 150);
        Hands = Math.Clamp(Hands, 75, 130);
    }
}

public sealed class FigureData
{
    public FigureStyle Style { get; set; } = FigureStyle.Mannequin;
    public bool Hair { get; set; } = true;
    public Proportions Proportions { get; set; } = new();
    /// <summary>Frisur (ab 0.16); fehlt bei älteren Dateien.</summary>
    public HairSettings? HairStyle { get; set; }
    /// <summary>Kleidung (ab 0.17); fehlt bei älteren Dateien.</summary>
    public ClothingSettings? Clothing { get; set; }
    /// <summary>Sitzblock automatisch unter sitzenden Figuren (ab 0.18).</summary>
    public bool SeatBlock { get; set; } = true;
}
