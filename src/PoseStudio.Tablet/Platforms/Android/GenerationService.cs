using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;

namespace PoseStudio.Tablet;

/// <summary>Vordergrund-Dienst, solange Aufträge laufen oder warten: Android friert die App im Hintergrund sonst ein
/// bzw. kappt die Verbindung (Energiesparen/Doze) – dann gingen laufende Generierungen und die Warteschlange verloren.
/// Hält zusätzlich CPU und WLAN wach und zeigt den Fortschritt als Benachrichtigung.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class GenerationService : Service
{
    private const int NotificationId = 4711;
    private const string ChannelId = "generation";
    private static PowerManager.WakeLock? _wake;
    private static WifiManager.WifiLock? _wifi;
    private static bool _running;

    private static Context Ctx => Android.App.Application.Context;

    /// <summary>Dienst starten (nur aus dem Vordergrund aufrufen, also beim Tippen auf „Generieren“) bzw. Text aktualisieren.</summary>
    public static void Show(string text)
    {
        try
        {
            if (_running) { Notify(text); return; }
            var intent = new Intent(Ctx, typeof(GenerationService)).PutExtra("text", text);
            Ctx.StartForegroundService(intent);
            _running = true;
        }
        catch { /* ohne Dienst läuft es trotzdem, nur nicht im Hintergrund abgesichert */ }
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        try { Ctx.StopService(new Intent(Ctx, typeof(GenerationService))); } catch { }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var n = Build(intent?.GetStringExtra("text") ?? "Generiere …");
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NotificationId, n, ForegroundService.TypeDataSync);
        else StartForeground(NotificationId, n);
        Acquire();
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        Release();
        _running = false;
        base.OnDestroy();
    }

    /// <summary>Android 15 begrenzt dataSync-Dienste auf 6 h am Tag – dann sauber beenden statt abgeschossen zu werden.</summary>
    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        Stop();
        StopSelf();
    }

    private static void Notify(string text)
    {
        try { (Ctx.GetSystemService(NotificationService) as NotificationManager)?.Notify(NotificationId, Build(text)); } catch { }
    }

    private static Notification Build(string text)
    {
        var nm = (NotificationManager)Ctx.GetSystemService(NotificationService)!;
        if (nm.GetNotificationChannel(ChannelId) == null)
            nm.CreateNotificationChannel(new NotificationChannel(ChannelId, "Generierung", NotificationImportance.Low) { Description = "Läuft, solange Bilder generiert werden" });
        var open = PendingIntent.GetActivity(Ctx, 0, new Intent(Ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
#pragma warning disable CA1416, CA1422
        return new Notification.Builder(Ctx, ChannelId)
            .SetContentTitle("Pose Studio Swarm")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownload)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(open)
            .Build();
#pragma warning restore CA1416, CA1422
    }

    private void Acquire()
    {
        try
        {
            if (_wake == null)
            {
                _wake = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "PoseStudioSwarm:generation")!;
                _wake.SetReferenceCounted(false);
            }
            _wake.Acquire(6 * 60 * 60 * 1000L);
#pragma warning disable CA1422 // FullHighPerf ist ab Android 14 veraltet, schadet aber nicht
            _wifi ??= ((WifiManager)GetSystemService(WifiService)!).CreateWifiLock(Android.Net.WifiMode.FullHighPerf, "PoseStudioSwarm:generation")!;
#pragma warning restore CA1422
            _wifi.SetReferenceCounted(false);
            _wifi.Acquire();
        }
        catch { }
    }

    private static void Release()
    {
        try { if (_wake?.IsHeld == true) _wake.Release(); } catch { }
        try { if (_wifi?.IsHeld == true) _wifi.Release(); } catch { }
    }
}
