using System.Collections.ObjectModel;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Charakter- und Künstler-Finder über AnimaDex. Antippen fügt den Eintrag in den Prompt ein; die Seite bleibt offen,
/// damit man mehrere Einträge (z. B. Charakter + Künstler) nacheinander übernehmen kann.</summary>
public sealed class AnimaDexPage : ContentPage
{
    private sealed class Item
    {
        public required AnimaDexEntry Entry { get; init; }
        public ImageSource? Image { get; init; }
        public string Label => Entry.Name;
        public string Sub => Entry.IsArtist ? (Entry.Score is { } s ? $"Bewertung {s * 100:0} %" : $"{Entry.Count:N0} Bilder")
                                            : Entry.Sub + (Entry.Loras.Count > 0 ? $" · {Entry.Loras.Count} LoRA" : "");
    }

    private sealed record FacetItem(string Value, string Label) { public override string ToString() => Label; }

    private readonly SwarmSettings _s;
    private readonly Action<string> _insert;
    private AnimaDexClient? _client;
    private bool _artists, _loading;
    private int _page, _pages, _total, _seed = Random.Shared.Next(1, int.MaxValue);
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<Item> _items = new();

    private readonly Entry _url = new() { Placeholder = "AnimaDex-Adresse, z. B. 192.168.188.17", Keyboard = Keyboard.Url };
    private readonly Entry _search = new() { Placeholder = "Name, Serie oder Tag suchen …", ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
    private readonly Picker _mode = new() { ItemsSource = new[] { "Charaktere", "Künstler / Stil" }, SelectedIndex = 0, WidthRequest = 170 };
    private readonly Picker _sort = new() { ItemsSource = AnimaDexClient.Sorts.Select(x => x.Label).ToList(), SelectedIndex = 0, WidthRequest = 150 };
    private readonly Switch _withTags = new() { OnColor = Ui.Accent, VerticalOptions = LayoutOptions.Center };
    private readonly Dictionary<string, Picker> _facets = new();
    private readonly HorizontalStackLayout _facetRow = new() { Spacing = 8 };
    private readonly Label _status = new() { TextColor = Ui.Muted, FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.Single, RemainingItemsThreshold = 12 };
    private CancellationTokenSource? _debounce;

    public AnimaDexPage(SwarmSettings settings, Action<string> insert, bool artists)
    {
        _s = settings; _insert = insert; _artists = artists;
        Title = "AnimaDex"; BackgroundColor = Ui.Page;
        _url.Text = settings.AnimaDexUrl;
        _withTags.IsToggled = settings.AnimaDexWithTags;
        _mode.SelectedIndex = artists ? 1 : 0;

        var connect = new Button { Text = "Verbinden", BackgroundColor = Ui.PanelLight, TextColor = Colors.White };
        connect.Clicked += async (_, _) => await ConnectAsync();
        var close = new Button { Text = "Fertig", BackgroundColor = Ui.Accent, TextColor = Colors.White };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var row1 = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8 };
        row1.Add(_url, 0); row1.Add(connect, 1); row1.Add(close, 2);

        var row2 = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        row2.Add(_mode, 0); row2.Add(_search, 1); row2.Add(_sort, 2);

        foreach (var (key, label) in AnimaDexClient.CharacterFacets)
        {
            var p = new Picker { Title = label, WidthRequest = 170 };
            p.SelectedIndexChanged += async (_, _) => { if (_client != null) await SearchAsync(); };
            _facets[key] = p;
            _facetRow.Children.Add(p);
        }
        var tagsLbl = new Label { Text = "Aussehen-Tags mitnehmen", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        _facetRow.Children.Add(new HorizontalStackLayout { Spacing = 4, Children = { _withTags, tagsLbl } });

        _list.ItemsSource = _items;
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var img = new Image { Aspect = Aspect.AspectFit, HeightRequest = 210, BackgroundColor = Ui.PanelLight };
            img.SetBinding(Image.SourceProperty, nameof(Item.Image));
            var label = new Label { FontSize = 13, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1, Margin = new Thickness(4, 2, 4, 0) };
            label.SetBinding(Label.TextProperty, nameof(Item.Label));
            var sub = new Label { FontSize = 11, TextColor = Ui.Muted, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1, Margin = new Thickness(4, 0) };
            sub.SetBinding(Label.TextProperty, nameof(Item.Sub));
            var g = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) } };
            g.Add(img, 0, 0); g.Add(label, 0, 1); g.Add(sub, 0, 2);
            return new Border { Content = g, BackgroundColor = Ui.Panel, StrokeThickness = 0, Padding = new Thickness(0, 0, 0, 6), Margin = 4 };
        });
        _list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not Item it) return;
            _list.SelectedItem = null;
            string text = it.Entry.PromptText(!it.Entry.IsArtist && _withTags.IsToggled);
            _insert(text);
            _status.Text = "Eingefügt: " + text;
        };
        _list.RemainingItemsThresholdReached += async (_, _) => { if (!_loading && _page < _pages) await LoadPageAsync(_page + 1, true); };
        SizeChanged += (_, _) =>
        {
            int span = Math.Max(2, (int)(Width / 168));
            if (_list.ItemsLayout is not GridItemsLayout gl || gl.Span != span) _list.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical);
        };

        _search.TextChanged += async (_, _) =>
        {
            _debounce?.Cancel();
            var d = _debounce = new CancellationTokenSource();
            try { await Task.Delay(400, d.Token); await SearchAsync(); } catch (OperationCanceledException) { }
        };
        _mode.SelectedIndexChanged += async (_, _) => { _artists = _mode.SelectedIndex == 1; _facetRow.IsVisible = !_artists; await SearchAsync(); };
        _sort.SelectedIndexChanged += async (_, _) => { _seed = Random.Shared.Next(1, int.MaxValue); await SearchAsync(); };
        _withTags.Toggled += (_, e) => _s.AnimaDexWithTags = e.Value;
        _url.Completed += async (_, _) => await ConnectAsync();
        _facetRow.IsVisible = !_artists;

        var head = new VerticalStackLayout
        {
            Spacing = 6, Padding = new Thickness(12, 10, 12, 4),
            Children = { row1, row2, new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _facetRow }, _status },
        };
        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(head, 0, 0); root.Add(_list, 0, 1);
        Content = root;

        Loaded += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_url.Text)) _status.Text = "Adresse der AnimaDex-Instanz eintragen (Port 5000 wird ergänzt) und „Verbinden“ tippen.";
            else await ConnectAsync();
        };
        Unloaded += (_, _) => { _cts?.Cancel(); _client?.Dispose(); };
    }

    private async Task ConnectAsync()
    {
        string url = AnimaDexClient.Normalize(_url.Text ?? "");
        if (url == "") return;
        _url.Text = url;
        _client?.Dispose();
        _client = null;
        var client = new AnimaDexClient(url);
        _status.Text = "Verbinde …";
        try
        {
            int chars = await client.CountAsync(false), arts = await client.CountAsync(true);
            foreach (var (key, label) in AnimaDexClient.CharacterFacets)
            {
                var values = await client.FacetValuesAsync(key);
                var items = new List<FacetItem> { new("", $"{label}: alle") };
                items.AddRange(values.Select(v => new FacetItem(v.Value, $"{v.Label} ({v.Count:N0})")));
                _facets[key].ItemsSource = items;
                _facets[key].SelectedIndex = 0;
            }
            _client = client;
            _s.AnimaDexUrl = url;
            _status.Text = $"Verbunden: {chars:N0} Charaktere, {arts:N0} Künstler.";
            await SearchAsync();
        }
        catch (Exception ex) { client.Dispose(); _status.Text = "Keine Verbindung zu AnimaDex: " + ex.Message; }
    }

    private Task SearchAsync() => LoadPageAsync(1, false);

    private async Task LoadPageAsync(int page, bool append)
    {
        if (_client == null) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _loading = true;
        var filters = _artists ? new Dictionary<string, string>()
            : _facets.Where(f => f.Value.SelectedItem is FacetItem { Value: not "" }).ToDictionary(f => f.Key, f => ((FacetItem)f.Value.SelectedItem).Value);
        try
        {
            var res = await _client.SearchAsync(_artists, _search.Text?.Trim() ?? "", filters, AnimaDexClient.Sorts[Math.Max(0, _sort.SelectedIndex)].Key, page, _seed, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!append) _items.Clear();
            foreach (var e in res.Results) _items.Add(new Item { Entry = e, Image = Ui.Web(e.ThumbUrl) });
            _page = res.Page; _pages = res.Pages; _total = res.Total;
            _status.Text = $"{_items.Count:N0} von {_total:N0} {(_artists ? "Künstlern" : "Charakteren")} – antippen fügt in den Prompt ein.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) _status.Text = "Suche fehlgeschlagen: " + ex.Message; }
        finally { if (_cts == cts) { _cts = null; _loading = false; } }
    }

    public static Task ShowAsync(INavigation nav, SwarmSettings s, Action<string> insert, bool artists) =>
        nav.PushModalAsync(new AnimaDexPage(s, insert, artists));
}
