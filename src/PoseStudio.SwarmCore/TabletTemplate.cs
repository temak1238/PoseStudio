namespace PoseStudio.Swarm;

/// <summary>Posen-Vorlage für die Tablet-App: ein Unterordner im Export-Ordner (…\Tablet\&lt;Name&gt;_&lt;Zeit&gt;) mit
/// dem Init Image in allen Darstellungen und den ControlNet-Bildern. Das Tablet liest den Ordner über die Synchronisation.</summary>
public static class TabletTemplate
{
    /// <summary>Unterordner im Export-Ordner, in den Pose Studio die Vorlagen legt.</summary>
    public const string FolderName = "Tablet";

    /// <summary>Init Image je Darstellung (Index = SwarmSettings.Look 0…3).</summary>
    public static readonly string[] InitFiles = { "normal.jpg", "cel.jpg", "flach.jpg", "strich.jpg" };
    public static readonly string[] LookNames = { "Normal (3D)", "Cel-Shading", "2D flach", "Strichzeichnung" };

    /// <summary>ControlNet-Bild für anima_3dpose.</summary>
    public const string PoseFile = "pose.png";
    /// <summary>ControlNet-Bild für anima_regional.</summary>
    public const string RegionsFile = "regionen.png";

    /// <summary>Vorschaubild der Vorlage (bevorzugt Cel-Shading).</summary>
    public static string PreviewFile(ICollection<string> present) =>
        new[] { "cel.jpg", "normal.jpg", "flach.jpg", "strich.jpg", PoseFile }.FirstOrDefault(present.Contains) ?? "";

    /// <summary>Anzeigename aus dem Ordnernamen (&lt;Name&gt;_yyyyMMdd_HHmmss).</summary>
    public static string DisplayName(string folder)
    {
        var parts = folder.Split('_');
        if (parts.Length >= 3 && parts[^2].Length == 8 && parts[^1].Length == 6 && parts[^2].All(char.IsDigit) && parts[^1].All(char.IsDigit))
            return string.Join("_", parts[..^2]) + $" ({parts[^2][6..8]}.{parts[^2][4..6]}. {parts[^1][..2]}:{parts[^1][2..4]})";
        return folder;
    }
}
