using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PoseStudio.Swarm;

/// <summary>Charakter- und Künstler-Finder über AnimaDex: suchen, filtern, Kachel anklicken → Text wird in den Prompt eingefügt.
/// Das Fenster bleibt offen, damit man mehrere Einträge (z. B. Charakter + Künstler) nacheinander übernehmen kann.</summary>
public sealed class AnimaDexWindow : Window
{
    private readonly SwarmSettings _s;
    private readonly Action<string> _insert;
    private AnimaDexClient? _client;
    private bool _artists;
    private int _page, _pages, _seed = Random.Shared.Next(1, int.MaxValue);
    private CancellationTokenSource? _cts;

    private readonly TextBox _url = new() { Width = 230, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly RadioButton _modeChars = new() { Content = "Charaktere", IsChecked = true, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _modeArtists = new() { Content = "Künstler / Stil", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _sort = new() { Width = 110, Margin = new Thickness(6, 0, 0, 0) };
    private readonly CheckBox _withTags = new() { Content = "Aussehen-Tags mitnehmen", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        ToolTip = "Zusätzlich die typischen Tags des Charakters (Haare, Augen, Kleidung) einfügen – hilft, wenn das Modell den Charakter nur schwach kennt." };
    private readonly Dictionary<string, ComboBox> _facets = new();
    private readonly StackPanel _facetRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _more = new() { Content = "Weitere laden", Padding = new Thickness(12, 3, 12, 3), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    public AnimaDexWindow(SwarmSettings settings, Action<string> insert, bool artists)
    {
        _s = settings; _insert = insert; _artists = artists;
        Title = "AnimaDex – Charakter- und Künstler-Finder";
        Width = 1100; Height = 820; MinWidth = 700; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _url.Text = settings.AnimaDexUrl;
        _withTags.IsChecked = settings.AnimaDexWithTags;
        _modeArtists.IsChecked = artists; _modeChars.IsChecked = !artists;
        _sort.ItemsSource = AnimaDexClient.Sorts.Select(x => x.Label).ToList();
        _sort.SelectedIndex = 0;

        var connect = new Button { Content = "Verbinden", Margin = new Thickness(6, 0, 0, 0) };
        connect.Click += async (_, _) => await ConnectAsync();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var addr = new StackPanel { Orientation = Orientation.Horizontal };
        addr.Children.Add(new TextBlock { Text = "AnimaDex", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        addr.Children.Add(_url); addr.Children.Add(connect);
        DockPanel.SetDock(addr, Dock.Left);
        top.Children.Add(addr);
        _status.Margin = new Thickness(10, 0, 0, 0);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
        top.Children.Add(_status);

        var row2 = new DockPanel();
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(_modeChars); modes.Children.Add(_modeArtists);
        DockPanel.SetDock(modes, Dock.Left); row2.Children.Add(modes);
        var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        sortPanel.Children.Add(new TextBlock { Text = "Sortierung", VerticalAlignment = VerticalAlignment.Center });
        sortPanel.Children.Add(_sort);
        DockPanel.SetDock(sortPanel, Dock.Right); row2.Children.Add(sortPanel);
        _search.ToolTip = "Name, Serie oder Tag suchen (z. B. „frieren“, „genshin“, „twintails“)";
        row2.Children.Add(_search);

        foreach (var (key, label) in AnimaDexClient.CharacterFacets)
        {
            var c = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 0), ToolTip = label };
            c.SelectionChanged += (_, _) => { if (IsLoaded) _ = SearchAsync(); };
            _facets[key] = c;
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            p.Children.Add(c);
            _facetRow.Children.Add(p);
        }
        _facetRow.Children.Add(_withTags);

        var hint = new TextBlock { Text = "Klick auf eine Kachel fügt sie an der Cursorstelle in den Prompt ein (Charakter: „name, serie“, Künstler: „@name“). Rechtsklick: Danbooru-Seite öffnen.",
                                   TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), FontSize = 11 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");

        var head = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
        head.Children.Add(top); head.Children.Add(row2); head.Children.Add(_facetRow); head.Children.Add(hint);
        _scroll.Content = new StackPanel { Children = { _tiles, _more } };
        _more.HorizontalAlignment = HorizontalAlignment.Center;
        _more.Margin = new Thickness(0, 8, 0, 12);
        _more.Click += async (_, _) => await LoadPageAsync(_page + 1, append: true);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var listBorder = new Border { Margin = new Thickness(8, 0, 8, 8), BorderThickness = new Thickness(1), Child = _scroll };
        listBorder.SetResourceReference(Border.BackgroundProperty, "ResultBg");
        listBorder.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        root.Children.Add(listBorder);
        Content = root;

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _modeChars.Checked += async (_, _) => { if (IsLoaded) { _artists = false; await SearchAsync(); } };
        _modeArtists.Checked += async (_, _) => { if (IsLoaded) { _artists = true; await SearchAsync(); } };
        _sort.SelectionChanged += async (_, _) => { if (IsLoaded) { _seed = Random.Shared.Next(1, int.MaxValue); await SearchAsync(); } };
        _withTags.Checked += (_, _) => _s.AnimaDexWithTags = true;
        _withTags.Unchecked += (_, _) => _s.AnimaDexWithTags = false;
        _url.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ConnectAsync(); };
        _scroll.ScrollChanged += async (_, e) =>
        {
            // automatisch nachladen, wenn man unten angekommen ist
            if (e.VerticalChange > 0 && _scroll.VerticalOffset + _scroll.ViewportHeight >= _scroll.ExtentHeight - 200 && _page < _pages && _cts == null)
                await LoadPageAsync(_page + 1, append: true);
        };
        Loaded += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_url.Text)) { _status.Text = "Adresse der AnimaDex-Instanz eintragen (z. B. 192.168.188.17 – Port 5000 wird ergänzt) und „Verbinden“."; _url.Focus(); }
            else await ConnectAsync();
        };
        Closed += (_, _) => { _cts?.Cancel(); _client?.Dispose(); };
    }

    private async Task ConnectAsync()
    {
        string url = AnimaDexClient.Normalize(_url.Text);
        if (url == "") return;
        _url.Text = url;
        _client?.Dispose();
        _client = new AnimaDexClient(url);
        _status.Text = "Verbinde …";
        try
        {
            int chars = await _client.CountAsync(false), arts = await _client.CountAsync(true);
            _s.AnimaDexUrl = url;
            foreach (var (key, _) in AnimaDexClient.CharacterFacets)
            {
                var values = await _client.FacetValuesAsync(key);
                var items = new List<object> { "(alle)" };
                items.AddRange(values.Select(v => new FacetItem(v.Value, $"{v.Label} ({v.Count:N0})")));
                _facets[key].ItemsSource = items;
                _facets[key].SelectedIndex = 0;
            }
            _status.Text = $"Verbunden: {chars:N0} Charaktere, {arts:N0} Künstler.";
            await SearchAsync();
        }
        catch (Exception ex) { _status.Text = "Keine Verbindung zu AnimaDex: " + ex.Message; }
    }

    private sealed record FacetItem(string Value, string Label) { public override string ToString() => Label; }

    private Task SearchAsync() => LoadPageAsync(1, append: false);

    private async Task LoadPageAsync(int page, bool append)
    {
        if (_client == null) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _facetRow.Visibility = _artists ? Visibility.Collapsed : Visibility.Visible;
        var filters = _artists ? new Dictionary<string, string>()
            : _facets.Where(f => f.Value.SelectedItem is FacetItem).ToDictionary(f => f.Key, f => ((FacetItem)f.Value.SelectedItem).Value);
        string sort = AnimaDexClient.Sorts[Math.Max(0, _sort.SelectedIndex)].Key;
        try
        {
            var res = await _client.SearchAsync(_artists, _search.Text.Trim(), filters, sort, page, _seed, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!append) { _tiles.Children.Clear(); _scroll.ScrollToTop(); }
            foreach (var e in res.Results) _tiles.Children.Add(Tile(e));
            _page = res.Page; _pages = res.Pages;
            _more.Visibility = _page < _pages ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = $"{_tiles.Children.Count:N0} von {res.Total:N0} {(_artists ? "Künstlern" : "Charakteren")}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) _status.Text = "Suche fehlgeschlagen: " + ex.Message; }
        finally { if (_cts == cts) _cts = null; }
    }

    private FrameworkElement Tile(AnimaDexEntry e)
    {
        var img = new Image { Stretch = Stretch.Uniform };
        var imgBox = new Border { Height = 190, Child = img };
        imgBox.SetResourceReference(Border.BackgroundProperty, "TileBg");
        var name = new TextBlock { Text = e.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 3, 2, 0) };
        var sub = new TextBlock
        {
            Text = e.IsArtist ? (e.Score is { } sc ? $"Bewertung {sc * 100:0} %" : $"{e.Count:N0} Bilder") : e.Sub + (e.Loras.Count > 0 ? $" · {e.Loras.Count} LoRA" : ""),
            FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 2),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
        var tile = new Border
        {
            Width = 142, Margin = new Thickness(4), Padding = new Thickness(3), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            Child = new StackPanel { Children = { imgBox, name, sub } },
        };
        tile.SetResourceReference(Border.BackgroundProperty, "CardBg");
        tile.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        string tip = e.PromptText(true);
        if (e.Loras.Count > 0) tip += "\n\nLoRAs auf CivitAI: " + string.Join(", ", e.Loras.Select(l => l.Name));
        tile.ToolTip = tip;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            string text = e.PromptText(!e.IsArtist && _withTags.IsChecked == true);
            _insert(text);
            _status.Text = "Eingefügt: " + text;
        };
        tile.MouseRightButtonUp += (_, _) =>
        {
            if (string.IsNullOrEmpty(e.DanbooruUrl)) return;
            try { Process.Start(new ProcessStartInfo(e.DanbooruUrl) { UseShellExecute = true }); } catch { }
        };
        if (e.ThumbUrl != null) _ = LoadThumbAsync(img, e.ThumbUrl);
        return tile;
    }

    private static async Task LoadThumbAsync(Image target, string url) => target.Source = await ThumbCache.LoadAsync(url, 280);

    /// <summary>Testmodus: Verbindung, Filter und eine Suche (ohne Fenster anzuzeigen).</summary>
    internal static async Task<string> SelfTestAsync(string url)
    {
        using var c = new AnimaDexClient(url);
        int chars = await c.CountAsync(false), arts = await c.CountAsync(true);
        var hair = await c.FacetValuesAsync("hair_color");
        var page = await c.SearchAsync(false, "frieren");
        var first = page.Results.FirstOrDefault();
        var blonde = await c.SearchAsync(false, "", new Dictionary<string, string> { ["hair_color"] = "blonde hair", ["gender"] = "1girl" });
        var artists = await c.SearchAsync(true, "", sort: "count");
        string thumb = "";
        if (first?.ThumbUrl != null)
        {
            var src = await ThumbCache.LoadAsync(first.ThumbUrl, 280) as System.Windows.Media.Imaging.BitmapSource;
            thumb = src == null ? "FEHLER Vorschaubild nicht lesbar" : $"Vorschaubild {src.PixelWidth}×{src.PixelHeight}";
        }
        const string refine = "1girl, standing, <segment:face> detailed face";
        var ins = AnimaDexClient.InsertIntoPrompt(refine, AnimaDexClient.SmartInsertPos(refine, refine.Length), "frieren, sousou no frieren");
        var mid = AnimaDexClient.InsertIntoPrompt(refine, AnimaDexClient.SmartInsertPos(refine, 5), "@artist");
        return $"{AnimaDexClient.Normalize(url)}: {chars:N0} Charaktere, {arts:N0} Künstler | Haarfarben: {string.Join(", ", hair.Take(4).Select(h => $"{h.Label} {h.Count}"))}\n"
             + $"Suche „frieren“: {page.Total} Treffer, erster: {first?.Name} → „{first?.PromptText(true)}“ | {thumb}\n"
             + $"Blond + weiblich: {blonde.Total:N0} | Künstler Top: {artists.Results.FirstOrDefault()?.PromptText(false)} | LoRAs beim ersten: {first?.Loras.Count}\n"
             + $"Einfügen: „{ins.Text}“ (Cursor {ins.Caret}) | Mitte: „{mid.Text}“";
    }
}
