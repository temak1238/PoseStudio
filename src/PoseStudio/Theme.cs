using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PoseStudio;

/// <summary>Helles/dunkles Design. Hell = klassisches WPF-Aussehen; Dunkel = eigene Vorlagen aus Themes/Dark.xaml in gleicher Größe.
/// Eigene Farben liegen als Pinsel in den App-Ressourcen (per DynamicResource, z. B. „MutedText“). Umschalten geht im laufenden Betrieb.</summary>
public static class Theme
{
    public static readonly string[] Modes = { "System", "Hell", "Dunkel" };

    public static bool IsDark { get; private set; }
    public static string Mode { get; private set; } = "System";

    /// <summary>Nach jedem Umschalten (z. B. für fensterlokale Stile).</summary>
    public static event Action? Changed;

    /// <summary>Testmodus: Design fest vorgegeben, Fenster übernehmen nicht das gespeicherte.</summary>
    public static bool Locked { get; set; }

    private static ResourceDictionary? _dark;
    private static Dictionary<object, object?>? _original;
    private static bool _hooked;
    private static readonly string[] DerivedStyles = { "ValueBox" };
    private static readonly Dictionary<string, Style?> _baseStyles = new();

    /// <summary>Windows-Einstellung „App-Modus“ (Personalisierung → Farben).</summary>
    public static bool SystemIsDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0; }
        catch { return false; }
    }

    /// <summary>Design setzen: „System“, „Hell“ oder „Dunkel“.</summary>
    public static void Apply(string? mode)
    {
        Mode = Modes.Contains(mode) ? mode! : "System";
        IsDark = Mode switch { "Dunkel" => true, "Hell" => false, _ => SystemIsDark() };
        var app = Application.Current;
        var res = app.Resources;

        _dark ??= new ResourceDictionary { Source = new Uri("/PoseStudio;component/Themes/Dark.xaml", UriKind.Relative) };
        if (_original == null)
        {
            _original = new();
            foreach (var k in _dark.Keys) _original[k] = res.Contains(k) ? res[k] : null;
        }
        foreach (var k in _dark.Keys)
        {
            if (IsDark) res[k] = _dark[k];
            else if (_original[k] is { } o) res[k] = o;
            else res.Remove(k);
        }
        foreach (var (key, light, dark) in Palette) res[key] = Frozen(IsDark ? dark : light);
        // Benannte Stile (z. B. ValueBox) auf den Typ-Stil des Designs aufbauen; Elemente binden sie per DynamicResource
        foreach (var name in DerivedStyles)
        {
            _baseStyles.TryAdd(name, res[name] as Style);
            if (_baseStyles[name] is not { } orig) continue;
            var s = new Style(orig.TargetType, IsDark ? res[orig.TargetType] as Style : null);
            foreach (var setter in orig.Setters) s.Setters.Add(setter);
            res[name] = s;
        }

        if (!_hooked)
        {
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Prepare((Window)s)));
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && Mode == "System" && SystemIsDark() != IsDark)
                    app.Dispatcher.BeginInvoke(() => Apply("System"));
            };
        }
        foreach (Window w in app.Windows) Prepare(w);
        Changed?.Invoke();
    }

    /// <summary>Fenster ohne eigene Hintergrundfarbe an das Design hängen, Titelleiste passend färben.</summary>
    private static void Prepare(Window w)
    {
        if (w.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue)
            w.SetResourceReference(Control.BackgroundProperty, "WindowBg");
        if (w.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue)
            w.SetResourceReference(Control.ForegroundProperty, "WindowFg");
        SetDarkTitleBar(w, IsDark);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void SetDarkTitleBar(Window w, bool dark)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(h, 20, ref v, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref v, sizeof(int));   // 19 = ältere Windows-10-Builds
        }
        catch { }
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // Schlüssel, Hell, Dunkel
    private static readonly (string Key, string Light, string Dark)[] Palette =
    {
        ("WindowBg", "#F3F3F3", "#202020"),
        ("WindowFg", "#000000", "#E8E8E8"),
        ("MutedText", "#666666", "#A8A8A8"),     // Hinweise, Zusatztexte
        ("SubtleText", "#888888", "#8A8A8A"),
        ("FaintText", "#AAAAAA", "#707070"),
        ("StatusText", "#444444", "#C8C8C8"),
        ("WarnText", "#A05000", "#FFB060"),
        ("GoldText", "#B8860B", "#E8C050"),
        ("LinkText", "#2B7BD6", "#6CB0FF"),
        ("PanelBg2", "#ECECEC", "#2C2C2C"),
        ("ListBg", "#FAFAFA", "#262626"),        // Kacheln
        ("TileBg", "#EEEEEE", "#333333"),        // Platz für Vorschaubilder
        ("TileBg2", "#E0E0E0", "#333333"),
        ("CardBg", "#FFFFFF", "#2A2A2A"),
        ("ResultBg", "#FFFFFF", "#1B1B1B"),      // Listenflächen
        ("LineBrush", "#D0D0D0", "#454545"),
    };
}
