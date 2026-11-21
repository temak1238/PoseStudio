namespace PoseStudio.Tablet;

/// <summary>Auswahl aus Kacheln mit Vorschaubild, Suche und optionalem Ordnerfilter (Modelle, LoRAs, Historie, Vorlagen).</summary>
public sealed class TilePickerPage : ContentPage
{
    private readonly List<Tile> _all;
    private readonly Func<Tile, string>? _folderOf;
    private readonly bool _multi;
    private readonly Entry _search = new() { Placeholder = "Suchen …", ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
    private readonly Picker _folder = new() { Title = "Ordner", WidthRequest = 260 };
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.Single };
    private readonly TaskCompletionSource<Tile?> _done = new();
    private readonly Action<Tile>? _onPick;
    private readonly CheckBox _option = new() { Color = Ui.Accent, VerticalOptions = LayoutOptions.Center };

    /// <summary>Zustand des optionalen Hakens (z. B. „Nur Prompts übernehmen“).</summary>
    public bool OptionChecked => _option.IsChecked;

    /// <param name="multi">Mehrfachauswahl: Seite bleibt offen, jede Auswahl ruft onPick auf.</param>
    public TilePickerPage(string title, List<Tile> tiles, Func<Tile, string>? folderOf = null, bool multi = false, Action<Tile>? onPick = null, double tileWidth = 170, double imageHeight = 170, string? option = null, bool optionValue = false)
    {
        Title = title; BackgroundColor = Ui.Page;
        _all = tiles; _folderOf = folderOf; _multi = multi; _onPick = onPick;

        var close = new Button { Text = multi ? "Fertig" : "Abbrechen", BackgroundColor = multi ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White };
        close.Clicked += async (_, _) => await CloseAsync(null);
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10, Padding = new Thickness(12, 10) };
        top.Add(_search, 0);
        if (folderOf != null)
        {
            _folder.ItemsSource = new[] { "Alle Ordner" }.Concat(_all.Select(folderOf).Where(f => f != "").Distinct().OrderBy(f => f)).ToList();
            _folder.SelectedIndex = 0;
            _folder.SelectedIndexChanged += (_, _) => Fill();
            top.Add(_folder, 1);
        }
        if (option != null)
        {
            _option.IsChecked = optionValue;
            var lbl = new Label { Text = option, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            var lt = new TapGestureRecognizer(); lt.Tapped += (_, _) => _option.IsChecked = !_option.IsChecked; lbl.GestureRecognizers.Add(lt);
            top.Add(new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { _option, lbl } }, 2);
        }
        top.Add(close, 3);
        _search.TextChanged += (_, _) => Fill();

        _list.ItemTemplate = new DataTemplate(() =>
        {
            var img = new Image { Aspect = Aspect.AspectFill, HeightRequest = imageHeight, BackgroundColor = Ui.PanelLight };
            img.SetBinding(Image.SourceProperty, nameof(Tile.Image));
            var label = new Label { FontSize = 13, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            label.SetBinding(Label.TextProperty, nameof(Tile.Label));
            var sub = new Label { FontSize = 11, TextColor = Ui.Muted, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            sub.SetBinding(Label.TextProperty, nameof(Tile.Sub));
            var check = new Label { Text = "✓", FontSize = 22, TextColor = Colors.White, BackgroundColor = Ui.Accent, Padding = new Thickness(8, 0), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
            check.SetBinding(IsVisibleProperty, nameof(Tile.Marked));
            var g = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, Padding = 0 };
            g.Add(img, 0, 0); g.Add(check, 0, 0); g.Add(label, 0, 1); g.Add(sub, 0, 2);
            return new Border { Content = g, BackgroundColor = Ui.Panel, StrokeThickness = 0, Padding = new Thickness(0, 0, 0, 6), Margin = 4, WidthRequest = tileWidth };
        });
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not Tile t) return;
            _list.SelectedItem = null;
            if (_multi) { _onPick?.Invoke(t); t.Marked = true; }
            else await CloseAsync(t);
        };
        SizeChanged += (_, _) =>
        {
            int span = Math.Max(2, (int)(Width / (tileWidth + 8)));
            if (_list.ItemsLayout is not GridItemsLayout gl || gl.Span != span) _list.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical);
        };
        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(top, 0, 0); root.Add(_list, 0, 1);
        Content = root;
        Fill();
    }

    private void Fill()
    {
        string q = _search.Text?.Trim() ?? "";
        string? folder = _folderOf != null && _folder.SelectedIndex > 0 ? _folder.SelectedItem as string : null;
        _list.ItemsSource = _all.Where(t => (folder == null || _folderOf!(t) == folder)
                                            && (q == "" || t.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || (t.Sub?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
    }

    private async Task CloseAsync(Tile? t)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult(t);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    /// <summary>Seite modal zeigen und auf die Auswahl warten (null = abgebrochen bzw. bei Mehrfachauswahl „Fertig“).</summary>
    public static async Task<Tile?> ShowAsync(INavigation nav, TilePickerPage page)
    {
        await nav.PushModalAsync(page);
        return await page._done.Task;
    }
}
