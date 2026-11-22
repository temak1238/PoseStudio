using Android.Content;
using Android.OS;

namespace PoseStudio.Tablet;

/// <summary>Heruntergeladene APK an den Paket-Installer von Android übergeben (die Installation bestätigt der Benutzer dort).</summary>
public static class ApkInstaller
{
    private static Context Ctx => Platform.CurrentActivity ?? Android.App.Application.Context;

    /// <summary>Ordner für die APK: Cache der App (über den FileProvider von MAUI Essentials freigegeben).</summary>
    public static string DownloadDir => Path.Combine(FileSystem.CacheDirectory, "update");

    /// <summary>Darf die App Pakete installieren („Unbekannte Apps installieren“)?</summary>
    public static bool CanInstall() => Build.VERSION.SdkInt < BuildVersionCodes.O || Ctx.PackageManager!.CanRequestPackageInstalls();

    /// <summary>Einstellungsseite „Unbekannte Apps installieren“ für diese App öffnen.</summary>
    public static void OpenInstallPermission()
    {
        var i = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + Ctx.PackageName));
        i.AddFlags(ActivityFlags.NewTask);
        Ctx.StartActivity(i);
    }

    public static void Install(string apkPath)
    {
        var ctx = Ctx;
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, ctx.PackageName + ".fileProvider", new Java.IO.File(apkPath));
        var i = new Intent(Intent.ActionView);
        i.SetDataAndType(uri, "application/vnd.android.package-archive");
        i.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        ctx.StartActivity(i);
    }

    /// <summary>Alte Update-Dateien löschen.</summary>
    public static void Cleanup()
    {
        try { if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, true); } catch { }
    }
}
