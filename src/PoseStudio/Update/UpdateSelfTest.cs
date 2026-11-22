using System.IO;
using System.IO.Compression;
using System.Text;

namespace PoseStudio.Update;

/// <summary>Testmodus --update-test: Release abfragen, Paket laden und in einen Testordner einspielen.
/// Der Testordner enthält eine „alte“ PoseStudio.exe, die während des Einspielens offen ist (wie das laufende Programm).</summary>
internal static class UpdateSelfTest
{
    public static async Task<string> RunAsync(string testDir)
    {
        var sb = new StringBuilder();
        var info = await UpdateChecker.GetLatestAsync();
        sb.AppendLine($"Neuestes Release: {info.Tag} ({info.VersionText}), installiert {WindowsUpdater.CurrentVersion.ToString(3)}, neuer: {UpdateChecker.IsNewer(info, WindowsUpdater.CurrentVersion)}");
        sb.AppendLine($"Dateien: {string.Join(", ", info.Assets.Select(a => $"{a.Name} {a.Size / 1048576.0:0.0} MB"))}");
        sb.AppendLine($"Windows-Paket: {info.WindowsZip?.Name ?? "FEHLER fehlt"}, APK: {info.AndroidApk?.Name ?? "FEHLER fehlt"}");
        sb.AppendLine($"Hinweise: {UpdateChecker.ShortNotes(info.Notes, 120).Replace('\n', ' ')}");
        sb.AppendLine($"ParseVersion: v1.2.3 → {UpdateChecker.ParseVersion("v1.2.3")}, 0.22.18-beta → {UpdateChecker.ParseVersion("0.22.18-beta")}, x → {UpdateChecker.ParseVersion("x")?.ToString() ?? "null"}");

        // Testordner: alte exe (offen gehalten), Zusatzdatei, die bleiben muss
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        Directory.CreateDirectory(Path.Combine(testDir, "Models"));
        string exe = Path.Combine(testDir, WindowsUpdater.ExeName);
        File.WriteAllText(exe, "alte Version");
        File.WriteAllText(Path.Combine(testDir, "Models", "rtmw3d-extra.onnx"), "zusatz");
        string zip = Path.Combine(Path.GetTempPath(), "PoseStudioUpdate", "test-" + info.WindowsZip!.Name);
        var t0 = DateTime.Now;
        await UpdateChecker.DownloadAsync(info.WindowsZip, zip);
        sb.AppendLine($"Download: {new FileInfo(zip).Length / 1048576.0:0.0} MB in {(DateTime.Now - t0).TotalSeconds:0} s");

        // falsche Version muss abgelehnt werden und nichts verändern
        try { WindowsUpdater.ApplyZip(zip, testDir, new Version(9, 9, 9)); sb.AppendLine("FEHLER: falsche Version nicht erkannt"); }
        catch (InvalidDataException ex) { sb.AppendLine($"Falsche Version abgelehnt: {ex.Message} | exe unverändert: {File.ReadAllText(exe) == "alte Version"}"); }

        int n;
        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))   // wie die laufende exe
            n = WindowsUpdater.ApplyZip(zip, testDir, info.Version);
        var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
        bool ok = UpdateChecker.ParseVersion(v.ProductVersion) == info.Version && File.Exists(exe + ".old")
                  && File.Exists(Path.Combine(testDir, "Models", "rtmw3d-extra.onnx"));
        sb.AppendLine($"Eingespielt: {n} Datei(en), exe-Version {v.ProductVersion}, .old vorhanden {File.Exists(exe + ".old")}, Zusatzdatei erhalten {File.Exists(Path.Combine(testDir, "Models", "rtmw3d-extra.onnx"))}{(ok ? "" : " FEHLER")}");
        int again = WindowsUpdater.ApplyZip(zip, testDir, info.Version);
        sb.AppendLine($"Zweites Einspielen (gleiche Dateien): {again} ersetzt{(again == 0 ? "" : " FEHLER")}");
        WindowsUpdater.CleanupOld(testDir);
        sb.AppendLine($"Aufgeräumt: .old übrig {Directory.GetFiles(testDir, "*.old", SearchOption.AllDirectories).Length}");
        sb.AppendLine($"Inhalt: {string.Join(", ", Directory.GetFiles(testDir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(testDir, f)))}");
        try { File.Delete(zip); } catch { }
        return sb.ToString();
    }
}
