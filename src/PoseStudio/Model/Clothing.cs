namespace PoseStudio.Model;

/// <summary>Kleidung einer Figur (gespeichert in Einstellungen und Pose-Dateien).</summary>
public sealed class ClothingSettings
{
    public string Top { get; set; } = "none";
    public string Bottom { get; set; } = "none";
    public string Shoes { get; set; } = "none";
    public string TopColor { get; set; } = "#F4F1EA";
    /// <summary>Farbe von Hose, Rock bzw. ganzem Kleid.</summary>
    public string BottomColor { get; set; } = "#3E4C6E";
    public string ShoeColor { get; set; } = "#4A3A32";
    /// <summary>Weite von Röcken und Kleidern in % (50 = eng, 100 = A-Linie, 200 = Tellerrock).</summary>
    public double Fullness { get; set; } = 100;

    public ClothingSettings Clone() => (ClothingSettings)MemberwiseClone();

    public void Clamp()
    {
        Fullness = Math.Clamp(Fullness, 40, 220);
        if (TopDef.Find(Top) == null) Top = "none";
        if (BottomDef.Find(Bottom) == null) Bottom = "none";
        if (ShoeDef.Find(Shoes) == null) Shoes = "none";
    }

    public bool Any => Top != "none" || Bottom != "none" || Shoes != "none";
}

/// <summary>Oberteil: anliegende Hülle um den Rumpf von Unterkante bis Oberkante (Höhe im Becken-System), dazu Ärmel bis zur
/// angegebenen Länge (0 = ärmellos; Werte im Schulter-System, negativ = Richtung Hand).</summary>
public sealed record TopDef(string Id, string Name, double Upper, double Sleeve)
{
    public static readonly IReadOnlyList<TopDef> All = new[]
    {
        new TopDef("none", "Kein Oberteil", 0, 0),
        new TopDef("bandeau", "Schulterfrei", 0.455, 0),
        new TopDef("tanktop", "Ärmellos", 0.572, 0),
        new TopDef("tshirt", "T-Shirt", 0.572, -0.15),
        new TopDef("langarm", "Langarm", 0.572, -0.53),
    };
    public static TopDef? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

public enum BottomKind { None, Pants, Skirt, Dress }

/// <summary>Unterteil: Hose (Hosenbein bis Hem im Hüft-System), Rock oder Kleid (fallender Stoff ab der Taille, Länge in Metern
/// bei normaler Beinlänge). Ein Kleid nimmt Ausschnitt und Ärmel vom gewählten Oberteil, aber die Kleidfarbe.</summary>
public sealed record BottomDef(string Id, string Name, BottomKind Kind, double Hem, double Length)
{
    public static readonly IReadOnlyList<BottomDef> All = new[]
    {
        new BottomDef("none", "Kein Unterteil", BottomKind.None, 0, 0),
        new BottomDef("shorts", "Shorts", BottomKind.Pants, -0.2, 0),
        new BottomDef("hose", "Lange Hose", BottomKind.Pants, -0.84, 0),
        new BottomDef("minirock", "Minirock", BottomKind.Skirt, 0, 0.3),
        new BottomDef("rock", "Rock (knielang)", BottomKind.Skirt, 0, 0.56),
        new BottomDef("langrock", "Langer Rock", BottomKind.Skirt, 0, 0.98),
        new BottomDef("minikleid", "Kleid kurz", BottomKind.Dress, 0, 0.3),
        new BottomDef("kleid", "Kleid knielang", BottomKind.Dress, 0, 0.56),
        new BottomDef("langkleid", "Kleid lang", BottomKind.Dress, 0, 0.98),
    };
    public static BottomDef? Find(string id) => All.FirstOrDefault(b => b.Id == id);
}

/// <summary>Schuhe/Strümpfe: Schaft bis zu dieser Höhe im Hüft-System (0 = nur Fuß).</summary>
public sealed record ShoeDef(string Id, string Name, double Shaft, double Offset, bool Sole)
{
    public static readonly IReadOnlyList<ShoeDef> All = new[]
    {
        new ShoeDef("none", "Barfuß", 0, 0, false),
        new ShoeDef("schuhe", "Schuhe", 0, 0.007, true),
        new ShoeDef("stiefel", "Stiefel (kniehoch)", -0.5, 0.009, true),
        new ShoeDef("socken", "Kniestrümpfe", -0.46, 0.0025, false),
    };
    public static ShoeDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}
