using System.IO;

namespace PoseStudio;

/// <summary>Wo Pose Studio seine Daten (settings.json, eigene Vorlagen) ablegt.
/// Normal: %APPDATA%\PoseStudio. Portabel: Unterordner „Daten“ im Programmordner – dann reicht es, den Programmordner zu kopieren
/// oder zu synchronisieren (z. B. mit Syncthing). Der Modus richtet sich allein danach, ob „Daten“ existiert.</summary>
public static class AppPaths
{
    public const string PortableFolderName = "Daten";

    public static string ProgramDir { get; } = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    public static string PortableDir => Path.Combine(ProgramDir, PortableFolderName);
    public static string RoamingDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoseStudio");

    /// <summary>Beim Start festgelegt (Umschalten wirkt nach einem Neustart vollständig; Einstellungen werden sofort umgezogen).</summary>
    public static bool IsPortable { get; private set; } = Directory.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, PortableFolderName));

    public static string DataDir => IsPortable ? PortableDir : RoamingDir;
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string TemplatesDir => Path.Combine(DataDir, "Vorlagen");
    /// <summary>Zwischenspeicher bleibt immer lokal (nicht synchronisieren).</summary>
    public static string CacheDir => IsPortable
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoseStudio", "Cache")
        : Path.Combine(RoamingDir, "Cache");

    /// <summary>Portabel einschalten: „Daten“ anlegen, Vorlagen hineinkopieren (vorhandene nicht überschreiben). Die Einstellungen
    /// schreibt der Aufrufer danach selbst (AppSettings.Save). Gibt eine Fehlermeldung zurück oder null.</summary>
    public static string? EnablePortable()
    {
        if (IsPortable) return null;
        if (!Update.WindowsUpdater.CanWrite(ProgramDir)) return $"In den Programmordner kann nicht geschrieben werden:\n{ProgramDir}";
        try
        {
            Directory.CreateDirectory(PortableDir);
            CopyMissing(Path.Combine(RoamingDir, "Vorlagen"), Path.Combine(PortableDir, "Vorlagen"));
            IsPortable = true;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Portabel ausschalten: Vorlagen zurück nach %APPDATA% kopieren (vorhandene bleiben), bisherige settings.json dort als .bak sichern,
    /// „Daten“ in „Daten-deaktiviert-…“ umbenennen (nichts wird gelöscht). Einstellungen schreibt der Aufrufer danach (AppSettings.Save).</summary>
    public static string? DisablePortable()
    {
        if (!IsPortable) return null;
        try
        {
            Directory.CreateDirectory(RoamingDir);
            CopyMissing(Path.Combine(PortableDir, "Vorlagen"), Path.Combine(RoamingDir, "Vorlagen"));
            string roamingSettings = Path.Combine(RoamingDir, "settings.json");
            if (File.Exists(roamingSettings)) File.Copy(roamingSettings, roamingSettings + ".bak", true);
            Directory.Move(PortableDir, Path.Combine(ProgramDir, $"{PortableFolderName}-deaktiviert-{DateTime.Now:yyyyMMdd-HHmmss}"));
            IsPortable = false;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static void CopyMissing(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(f, target);
        }
    }
}
