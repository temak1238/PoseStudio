using Android.App;
using Android.Content;
using Android.Content.PM;

namespace PoseStudio.Tablet;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int PickTreeRequest = 4711;
    private static TaskCompletionSource<Android.Net.Uri?>? _pickTree;

    /// <summary>Ordner über den System-Dialog wählen (Storage Access Framework); Leserecht bleibt über Neustarts erhalten.</summary>
    public static Task<Android.Net.Uri?> PickTreeAsync()
    {
        var act = Platform.CurrentActivity as MainActivity ?? throw new InvalidOperationException("Keine Activity.");
        _pickTree?.TrySetResult(null);
        _pickTree = new TaskCompletionSource<Android.Net.Uri?>();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        act.StartActivityForResult(intent, PickTreeRequest);
        return _pickTree.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickTreeRequest) return;
        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri != null)
            try { ContentResolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission); } catch { }
        _pickTree?.TrySetResult(uri);
        _pickTree = null;
    }
}
