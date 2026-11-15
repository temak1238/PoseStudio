namespace PoseStudio.Rendering;

public enum SetType
{
    None,
    Studio,
    Meadow,
    Beach,
    Desert,
    Snow,
    Room,
    Street
}

/// <summary>Einstellungen der 3D-Kulisse.</summary>
public sealed class SetSettings
{
    public SetType Type { get; set; } = SetType.None;
    /// <summary>Studiofarbe bzw. Wandfarbe im Innenraum.</summary>
    public string Color { get; set; } = "#D9D5CE";
    /// <summary>Bodenbelag im Innenraum: Wood, Tiles, Concrete, Carpet.</summary>
    public string Floor { get; set; } = "Wood";
    public double RoomWidth { get; set; } = 6;
    /// <summary>Abstand der Rückwand hinter der Figur (Meter).</summary>
    public double WallDistance { get; set; } = 3;
    public bool HorizonLine { get; set; } = false;

    public SetSettings Clone() => (SetSettings)MemberwiseClone();

    public void Clamp()
    {
        RoomWidth = Math.Clamp(RoomWidth, 3, 14);
        WallDistance = Math.Clamp(WallDistance, 1.2, 12);
    }

    public string CacheKey => $"{Type}|{Color}|{Floor}|{RoomWidth:F2}|{WallDistance:F2}";
}
