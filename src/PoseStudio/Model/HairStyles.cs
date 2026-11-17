using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Frisur-Einstellungen einer Figur (gespeichert in Einstellungen und Pose-Dateien).</summary>
public sealed class HairSettings
{
    /// <summary>Id der Frisur (siehe <see cref="HairStyleDef.All"/>); "auto" = nach Figur (Frau lang, Mann kurz).</summary>
    public string Style { get; set; } = "auto";
    /// <summary>Id des Ponys (siehe <see cref="BangsDef.All"/>).</summary>
    public string Bangs { get; set; } = "gerade";
    /// <summary>Länge in % der Grundlänge der Frisur.</summary>
    public double Length { get; set; } = 100;
    /// <summary>Fülle in %: Abstand vom Kopf und Breite der Strähnen.</summary>
    public double Volume { get; set; } = 100;
    /// <summary>Windstärke in % (0 = Haare hängen nur mit der Schwerkraft).</summary>
    public double Wind { get; set; } = 25;
    /// <summary>Wehrichtung relativ zur Figur in Grad: 0 = nach hinten, 90 = nach links (aus Sicht der Figur), 180 = nach vorne.</summary>
    public double WindDirection { get; set; } = 30;
    /// <summary>Schwung in % (Haare fliegen nach oben/außen wie bei Sprung oder Drehung).</summary>
    public double Lift { get; set; }
    /// <summary>Unordnung in %: Strähnen unterschiedlich lang und unterschiedlich stark bewegt.</summary>
    public double Messiness { get; set; } = 35;
    /// <summary>Zufallsvariante der Strähnen.</summary>
    public int Seed { get; set; } = 1;

    public HairSettings Clone() => (HairSettings)MemberwiseClone();

    public void Clamp()
    {
        Length = Math.Clamp(Length, 40, 180);
        Volume = Math.Clamp(Volume, 50, 200);
        Wind = Math.Clamp(Wind, 0, 100);
        Lift = Math.Clamp(Lift, 0, 100);
        Messiness = Math.Clamp(Messiness, 0, 100);
        if (HairStyleDef.Find(Style) == null && Style != "auto") Style = "auto";
        if (BangsDef.Find(Bangs) == null) Bangs = "gerade";
    }

    /// <summary>Tatsächliche Frisur ("auto" aufgelöst).</summary>
    public HairStyleDef Resolve(bool female) => HairStyleDef.Find(Style) ?? HairStyleDef.Find(female ? "lang" : "kurz")!;
}

/// <summary>Eine Gruppe gleichartiger Strähnen. Winkel auf der Kopfhaut: phi von oben (0 = Scheitel, 90 = Höhe Kopfmitte),
/// theta um die Hochachse (0 = vorne, 90 = links der Figur, 180 = hinten). Längen in Metern bei Kopfgröße 100 %.</summary>
public sealed record StrandGroup
{
    public double PhiFrom { get; init; }
    public double PhiTo { get; init; }
    public double ThetaFrom { get; init; }
    public double ThetaTo { get; init; }
    public int Rows { get; init; } = 1;
    public int Cols { get; init; } = 1;
    /// <summary>Grundlänge; Strähnen weiter oben am Kopf werden automatisch um den Weg über den Kopf verlängert.</summary>
    public double Length { get; init; }
    public double Width { get; init; } = 0.045;
    /// <summary>Steifigkeit am Ansatz (größer = Strähne folgt länger der Kämmrichtung).</summary>
    public double Stiffness { get; init; } = 3;
    /// <summary>Wie stark Wind und Schwung wirken (Pony z. B. weniger).</summary>
    public double WindFactor { get; init; } = 1;
    /// <summary>Kämmrichtung um die Normale gedreht (Grad, + = zur rechten Seite der Figur); 0 = vom Scheitel weg.</summary>
    public double Sweep { get; init; }
    /// <summary>Länge folgt nicht der Ansatzhöhe (Pony: alle enden etwa auf gleicher Höhe über den Augen).</summary>
    public bool FixedLength { get; init; }
    /// <summary>Wächst die Länge mit der Einstellung "Länge"? (Pony nicht.)</summary>
    public bool Scales { get; init; } = true;
    /// <summary>Zusammengefasste Strähnen (Zopf): Ansatz an diesem Punkt der Kopfhaut statt verteilt.</summary>
    public (double Phi, double Theta)? Tie { get; init; }
    /// <summary>Austrittsrichtung am Haargummi (Kopf-System, wird normiert).</summary>
    public Vector3D TieDirection { get; init; } = new(0, -0.3, -1);
    public int Segments { get; init; } = 10;
    /// <summary>Liegt auf ganzer Länge am Kopf an (Kurzhaar).</summary>
    public bool HugAll { get; init; }
}

/// <summary>Frisur = Haarkappe + Strähnen-Gruppen + Extras. Neue Frisuren: einfach einen Eintrag in <see cref="All"/> ergänzen.</summary>
public sealed record HairStyleDef(string Id, string Name, double CapBack, IReadOnlyList<StrandGroup> Groups)
{
    /// <summary>Haarknoten (Dutt) an dieser Stelle der Kopfhaut (phi, theta, Radius).</summary>
    public (double Phi, double Theta, double Radius)? Bun { get; init; }
    /// <summary>Seitensträhnen vor den Ohren (typisch Anime) mit dieser Länge; 0 = keine.</summary>
    public double SideLocks { get; init; }

    // Deckhaar: Seiten und Hinterkopf, Ansätze verteilt; vorne bleibt das Gesicht frei (Pony kommt extra)
    private static StrandGroup Cover(double length, int segments = 10, double stiffness = 3, double width = 0.072) => new()
    {
        PhiFrom = 20, PhiTo = 78, ThetaFrom = 66, ThetaTo = 294, Rows = 3, Cols = 15,
        Length = length, Width = width, Stiffness = stiffness, Segments = segments
    };
    // Oberkopf vorne nach hinten gekämmt (unter dem Pony bzw. bei offener Stirn sichtbar)
    private static StrandGroup Top(double length, int segments = 10) => new()
    {
        PhiFrom = 8, PhiTo = 26, ThetaFrom = -50, ThetaTo = 50, Rows = 1, Cols = 5,
        Length = length, Width = 0.075, Stiffness = 4, Sweep = 180, Segments = segments
    };

    public static readonly IReadOnlyList<HairStyleDef> All = new[]
    {
        new HairStyleDef("kurz", "Kurz", 118, new[]
        {
            Cover(0.075, 4, 5, 0.06) with { Rows = 4, Cols = 15, FixedLength = true, HugAll = true },
            Top(0.11, 5) with { Stiffness = 5, FixedLength = true, HugAll = true },
        }),
        new HairStyleDef("bob", "Bob (Kinnlänge)", 125, new[] { Cover(0.10, 8, 3.5), Top(0.12, 8) }) { SideLocks = 0.12 },
        new HairStyleDef("schulter", "Schulterlang", 128, new[] { Cover(0.20), Top(0.22) }) { SideLocks = 0.2 },
        new HairStyleDef("lang", "Lang (Rücken)", 128, new[] { Cover(0.38, 12), Top(0.4, 12) }) { SideLocks = 0.3 },
        new HairStyleDef("sehrlang", "Sehr lang (Hüfte)", 128, new[] { Cover(0.58, 14), Top(0.6, 14) }) { SideLocks = 0.42 },
        new HairStyleDef("zopf", "Pferdeschwanz", 132, new[]
        {
            new StrandGroup { Tie = (62, 180), TieDirection = new Vector3D(0, 0.15, -1), Cols = 11, Length = 0.32, Width = 0.05, Stiffness = 4, Segments = 12 },
        }) { SideLocks = 0.14 },
        new HairStyleDef("zoepfe", "Zwei Zöpfe (Twintails)", 132, new[]
        {
            new StrandGroup { Tie = (58, 105), TieDirection = new Vector3D(1, 0.1, -0.35), Cols = 9, Length = 0.34, Width = 0.046, Stiffness = 4, Segments = 12 },
            new StrandGroup { Tie = (58, -105), TieDirection = new Vector3D(-1, 0.1, -0.35), Cols = 9, Length = 0.34, Width = 0.046, Stiffness = 4, Segments = 12 },
        }) { SideLocks = 0.14 },
        new HairStyleDef("dutt", "Dutt", 132, Array.Empty<StrandGroup>()) { Bun = (48, 180, 0.048), SideLocks = 0.12 },
    };

    public static HairStyleDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}

/// <summary>Pony-Varianten (unabhängig von der Frisur kombinierbar).</summary>
public sealed record BangsDef(string Id, string Name, double CapFront, IReadOnlyList<StrandGroup> Groups)
{
    private static StrandGroup Fringe(double from, double to, int cols, double length, double sweep) => new()
    {
        PhiFrom = 14, PhiTo = 34, ThetaFrom = from, ThetaTo = to, Rows = 2, Cols = cols,
        Length = length, Width = 0.05, Stiffness = 5, WindFactor = 0.3, Sweep = sweep, FixedLength = true, Scales = false, Segments = 6
    };

    public static readonly IReadOnlyList<BangsDef> All = new[]
    {
        new BangsDef("gerade", "Gerader Pony", 40, new[] { Fringe(-52, 52, 8, 0.105, 0) }),
        new BangsDef("seitlich", "Seitlicher Pony", 40, new[] { Fringe(-52, 52, 8, 0.12, 38) }),
        new BangsDef("scheitel", "Mittelscheitel", 40, new[] { Fringe(-55, -6, 4, 0.13, 32), Fringe(6, 55, 4, 0.13, -32) }),
        new BangsDef("offen", "Stirn frei", 58, Array.Empty<StrandGroup>()),
    };

    public static BangsDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}
