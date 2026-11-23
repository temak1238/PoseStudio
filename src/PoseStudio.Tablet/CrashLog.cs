namespace PoseStudio.Tablet;

/// <summary>Absturzprotokoll: unbehandelte Fehler landen in crash.txt im App-Speicher; beim nächsten Start zeigt die App sie an
/// (mit „Kopieren“), damit sich die Ursache finden lässt.</summary>
public static class CrashLog
{
    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "crash.txt");

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, "Unbehandelt");
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "Task");
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write(e.Exception, "Android");
#endif
    }

    public static void Write(Exception? ex, string where)
    {
        if (ex == null) return;
        try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] Version {AppInfo.VersionString}\n{ex}\n\n"); } catch { }
    }

    /// <summary>Gespeichertes Protokoll holen und zurücksetzen (null = kein Absturz seit dem letzten Start).</summary>
    public static string? Take()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            string text = File.ReadAllText(FilePath);
            File.Move(FilePath, Path.Combine(FileSystem.AppDataDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), true);
            return text;
        }
        catch { return null; }
    }
}
