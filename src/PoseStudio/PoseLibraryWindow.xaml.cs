using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PoseStudio.Model;

namespace PoseStudio;

/// <summary>Posen-Bibliothek: eingebaute und eigene Vorlagen als Kacheln mit Vorschaubild.</summary>
public partial class PoseLibraryWindow : Window
{
    private readonly Func<FigureData> _figure;
    private readonly Func<Color> _bodyColor;
    private readonly Action<LibraryEntry> _apply;
    private readonly Func<string?> _saveCurrent;
    private readonly string _version;
    private List<LibraryEntry> _all = new();
    private readonly Queue<LibraryEntry> _thumbQueue = new();
    private bool _thumbRunning;

    public PoseLibraryWindow(Func<FigureData> figure, Func<Color> bodyColor, Action<LibraryEntry> apply, Func<string?> saveCurrent, string version)
    {
        InitializeComponent();
        _figure = figure; _bodyColor = bodyColor; _apply = apply; _saveCurrent = saveCurrent; _version = version;
        Reload();
    }

    /// <summary>Einträge neu einlesen (nach Speichern/Löschen); Vorschaubilder der eingebauten Vorlagen nach und nach erzeugen.</summary>
    public void Reload()
    {
        _all = PoseLibrary.LoadUser().Concat(PoseLibrary.BuiltIn()).ToList();
        var cats = new List<string> { "Alle" };
        cats.AddRange(_all.Select(e => e.Category).Distinct());
        var sel = CategoryCombo.SelectedItem as string;
        CategoryCombo.ItemsSource = cats;
        CategoryCombo.SelectedItem = sel != null && cats.Contains(sel) ? sel : "Alle";
        ApplyFilter();

        _thumbQueue.Clear();
        foreach (var e in _all.Where(e => e.Image == null)) _thumbQueue.Enqueue(e);
        if (!_thumbRunning) NextThumb();
    }

    private void NextThumb()
    {
        if (_thumbQueue.Count == 0) { _thumbRunning = false; return; }
        _thumbRunning = true;
        Dispatcher.BeginInvoke(() =>
        {
            var e = _thumbQueue.Dequeue();
            try
            {
                e.Image = e.IsUser
                    ? PoseLibrary.RenderThumb(e.Pose, e.Pose.Figure ?? _figure(), _bodyColor())
                    : PoseLibrary.BuiltInThumb(e, _figure(), _bodyColor(), _version);
            }
            catch { /* ohne Bild weiter */ }
            NextThumb();
        }, DispatcherPriority.Background);
    }

    private void ApplyFilter()
    {
        var cat = CategoryCombo.SelectedItem as string ?? "Alle";
        var q = SearchBox.Text?.Trim() ?? "";
        Tiles.ItemsSource = _all.Where(e => (cat == "Alle" || e.Category == cat) &&
                                            (q.Length == 0 || e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Tiles_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryEntry entry) _apply(entry);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saveCurrent() != null) Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryEntry entry || !entry.IsUser) return;
        if (MessageBox.Show(this, $"Vorlage „{entry.Name}“ wirklich löschen?", "Posen-Bibliothek", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        PoseLibrary.DeleteUser(entry);
        Reload();
    }
}
