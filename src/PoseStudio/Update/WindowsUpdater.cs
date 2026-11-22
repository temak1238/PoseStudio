using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace PoseStudio.Update;

/// <summary>Update des Windows-Programms aus dem GitHub-Release: Zip laden, Programmdateien ersetzen
/// (laufende Dateien werden in *.old umbenannt, beim nächsten Start gelöscht), neu starten.
/// %APPDATA%\PoseStudio (Einstellungen, Vorlagen) und zusätzliche Dateien im Programmordner (z. B. RTMW3D-Modelle) bleiben unberührt.</summary>
public static class WindowsUpdater
{
    public const string ExeName = "PoseStudio.exe";

    public static Version CurrentVersion => UpdateChecker.Normalize(typeof(WindowsUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string AppDir => Path.GetDirectoryName(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ExeName))!;

    /// <summary>Reste eines früheren Updates (*.old) entfernen.</summary>
    public static void CleanupOld(string? dir = null)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir ?? AppDir, "*.old", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { /* evtl. noch gesperrt – nächstes Mal */ }
        }
        catch { }
    }

    public static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".update-probe-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Paket in den Programmordner einspielen. Prüft, dass PoseStudio.exe enthalten ist und die erwartete Version hat.
    /// Bei einem Fehler werden alle schon ersetzten Dateien zurückgeholt. Gibt die Zahl der ersetzten Dateien zurück.</summary>
    public static int ApplyZip(string zipPath, string targetDir, Version expected)
    {
        targetDir = Path.GetFullPath(targetDir);
        string stage = Path.Combine(Path.GetTempPath(), "PoseStudioUpdate", "stage-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        var replaced = new List<(string Target, string? Backup)>();
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;   // Ordner-Eintrag
                    string rel = e.FullName.Replace('\\', '/').TrimStart('/');
                    string dst = Path.GetFullPath(Path.Combine(stage, rel));
                    if (!dst.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Ungültiger Pfad im Paket: {e.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    e.ExtractToFile(dst, true);
                }
            }
            string newExe = Path.Combine(stage, ExeName);
            if (!File.Exists(newExe)) throw new InvalidDataException($"{ExeName} fehlt im Update-Paket.");
            var fv = FileVersionInfo.GetVersionInfo(newExe);
            var got = UpdateChecker.ParseVersion(fv.ProductVersion) ?? UpdateChecker.ParseVersion(fv.FileVersion);
            if (got != expected) throw new InvalidDataException($"Das Paket enthält Version {got?.ToString() ?? "?"} statt {expected}.");

            foreach (var src in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(stage, src);
                string dst = Path.Combine(targetDir, rel);
                if (File.Exists(dst) && SameFile(src, dst)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                string? backup = null;
                if (File.Exists(dst))
                {
                    backup = dst + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(dst, backup);   // geht auch bei der laufenden exe
                }
                replaced.Add((dst, backup));
                File.Copy(src, dst);
            }
            return replaced.Count;
        }
        catch
        {
            for (int i = replaced.Count - 1; i >= 0; i--)
            {
                var (t, b) = replaced[i];
                try { if (File.Exists(t)) File.Delete(t); if (b != null && File.Exists(b)) File.Move(b, t); } catch { }
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
        }
    }

    private static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = fa.OpenRead(); using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }

    // ------------------------------------------------------------------ Ablauf mit Dialogen

    /// <summary>Nach Updates suchen. manual = über den Knopf (meldet auch „aktuell“ und Fehler, ignoriert „überspringen“).
    /// restart: Einstellungen speichern und Programm schließen (vom Hauptfenster).</summary>
    public static async Task CheckAsync(Window owner, AppSettings settings, bool manual, Action<string> restart)
    {
        UpdateInfo info;
        try { info = await UpdateChecker.GetLatestAsync(); }
        catch (Exception ex)
        {
            if (manual) MessageBox.Show(owner, "Update-Prüfung fehlgeschlagen: " + ex.Message, "Pose Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!UpdateChecker.IsNewer(info, CurrentVersion))
        {
            if (manual) MessageBox.Show(owner, $"Pose Studio ist aktuell (Version {CurrentVersion.ToString(3)}).", "Pose Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!manual && settings.SkipUpdateVersion == info.Tag) return;

        var choice = AskDialog(owner, info);
        if (choice == Choice.Skip) { settings.SkipUpdateVersion = info.Tag; return; }
        if (choice != Choice.Update) return;

        var asset = info.WindowsZip;
        string dir = AppDir;
        if (asset == null || !CanWrite(dir))
        {
            MessageBox.Show(owner, asset == null ? "Im Release fehlt das Windows-Paket. Die Release-Seite wird geöffnet."
                : $"Kein Schreibrecht im Programmordner\n{dir}\n\nDie Release-Seite wird geöffnet – bitte das Paket von Hand entpacken.", "Pose Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenUrl(info.PageUrl);
            return;
        }

        string zip = Path.Combine(Path.GetTempPath(), "PoseStudioUpdate", asset.Name);
        var progress = new ProgressDialog(owner, $"Lade Pose Studio {info.VersionText} …");
        progress.Show();
        try
        {
            await UpdateChecker.DownloadAsync(asset, zip, new Progress<double>(f => progress.Set(f)), progress.Token);
            progress.SetText("Installiere …");
            int n = await Task.Run(() => ApplyZip(zip, dir, info.Version));
            try { File.Delete(zip); } catch { }
            progress.Close();
            restart($"Pose Studio {info.VersionText} ist installiert ({n} Datei(en) ersetzt). Das Programm startet jetzt neu.");
        }
        catch (OperationCanceledException) { progress.Close(); }
        catch (Exception ex)
        {
            progress.Close();
            MessageBox.Show(owner, "Update fehlgeschlagen, die bisherige Version bleibt erhalten:\n" + ex.Message, "Pose Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private enum Choice { Later, Update, Skip }

    private static Choice AskDialog(Window owner, UpdateInfo info)
    {
        var result = Choice.Later;
        var w = new Window
        {
            Title = "Update verfügbar", Owner = owner, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Pose Studio {info.VersionText} ist verfügbar (installiert: {CurrentVersion.ToString(3)}).",
            FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBox
        {
            Text = UpdateChecker.ShortNotes(info.Notes, 3000), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260, Margin = new Thickness(0, 8, 0, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "„Jetzt aktualisieren“ lädt das Paket von GitHub, ersetzt die Programmdateien und startet Pose Studio neu. Einstellungen und Vorlagen bleiben erhalten.",
            Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button B(string text, Choice c, bool isDefault = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = c == Choice.Later };
            b.Click += (_, _) => { result = c; w.Close(); };
            buttons.Children.Add(b);
            return b;
        }
        B("Diese Version überspringen", Choice.Skip);
        B("Später", Choice.Later);
        B("Jetzt aktualisieren", Choice.Update, true);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.ShowDialog();
        return result;
    }

    private sealed class ProgressDialog : Window
    {
        private readonly ProgressBar _bar = new() { Height = 18, Maximum = 1 };
        private readonly TextBlock _text = new() { Margin = new Thickness(0, 0, 0, 6) };
        private readonly CancellationTokenSource _cts = new();
        public CancellationToken Token => _cts.Token;

        public ProgressDialog(Window owner, string text)
        {
            Title = "Update"; Owner = owner; Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            _text.Text = text;
            var cancel = new Button { Content = "Abbrechen", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 8, 0, 0) };
            cancel.Click += (_, _) => _cts.Cancel();
            var p = new StackPanel { Margin = new Thickness(14) };
            p.Children.Add(_text); p.Children.Add(_bar); p.Children.Add(cancel);
            Content = p;
            Closed += (_, _) => _cts.Cancel();
        }

        public void Set(double f) => _bar.Value = f;
        public void SetText(string t) { _text.Text = t; _bar.IsIndeterminate = true; }
    }
}
