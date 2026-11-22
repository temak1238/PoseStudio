namespace PoseStudio.Tablet;

public enum ViewerAction { None, Refine, UseSeed, UseAsImage, ToggleStar, Remove }

/// <summary>Ergebnis groß anzeigen; mit Wischen oder den Pfeilen am Rand zwischen den Ergebnissen wechseln.
/// Aktionen Nachbessern, Seed, als Bild verwenden, Galerie, Teilen, aus Liste entfernen beziehen sich auf das gerade angezeigte Bild.</summary>
public sealed class ViewerPage : ContentPage
{
    private readonly TaskCompletionSource<(ViewerAction, ResultItem)> _done = new();
    private readonly IReadOnlyList<ResultItem> _items;
    private int _index;
    private readonly Image _img = new() { Aspect = Aspect.AspectFit };
    private readonly ZoomView _zoom = new();
    private readonly Label _caption = new() { TextColor = Ui.Muted, HorizontalOptions = LayoutOptions.Center, FontSize = 13 };
    private readonly Button _star, _prev, _next;

    private ResultItem Item => _items[_index];

    public ViewerPage(IReadOnlyList<ResultItem> items, int index)
    {
        _items = items;
        _index = Math.Clamp(index, 0, items.Count - 1);
        BackgroundColor = Colors.Black;
        _zoom.Content = _img;
        _zoom.Swiped += dir => Go(dir);

        Button B(string text, Func<Task> act, bool accent = false)
        {
            var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White, Margin = new Thickness(4, 0) };
            b.Clicked += async (_, _) => await act();
            return b;
        }
        _star = B("", () => CloseAsync(ViewerAction.ToggleStar));
        var bar = new HorizontalStackLayout
        {
            Spacing = 4, HorizontalOptions = LayoutOptions.Center, Padding = new Thickness(8),
            Children =
            {
                B("Nachbessern", () => CloseAsync(ViewerAction.Refine), true),
                B("Seed übernehmen", () => CloseAsync(ViewerAction.UseSeed)),
                _star,
                B("Als Bild verwenden", () => CloseAsync(ViewerAction.UseAsImage)),
                B("In Galerie", async () =>
                {
                    try
                    {
                        if (!OperatingSystem.IsAndroidVersionAtLeast(29) && await Permissions.RequestAsync<Permissions.StorageWrite>() != PermissionStatus.Granted) return;
                        AndroidStore.SaveToGallery(Item.LocalPath!);
                        await DisplayAlertAsync("Galerie", "Gespeichert unter Bilder/PoseStudio.", "OK");
                    }
                    catch (Exception ex) { await DisplayAlertAsync("Galerie", ex.Message, "OK"); }
                }),
                B("Teilen", () => Share.Default.RequestAsync(new ShareFileRequest { Title = "Bild teilen", File = new ShareFile(Item.LocalPath!) })),
                B("Aus Liste entfernen", () => CloseAsync(ViewerAction.Remove)),
                B("Schließen", () => CloseAsync(ViewerAction.None)),
            }
        };

        Button Arrow(string text, int dir, LayoutOptions side)
        {
            var b = new Button
            {
                Text = text, FontSize = 30, TextColor = Colors.White, BackgroundColor = Color.FromRgba(0, 0, 0, 0.35), CornerRadius = 22,
                WidthRequest = 44, HeightRequest = 64, Padding = 0, Margin = new Thickness(6, 0),
                HorizontalOptions = side, VerticalOptions = LayoutOptions.Center,
            };
            b.Clicked += (_, _) => Go(dir);
            return b;
        }
        _prev = Arrow("‹", -1, LayoutOptions.Start);
        _next = Arrow("›", 1, LayoutOptions.End);

        var stage = new Grid();
        stage.Add(_zoom); stage.Add(_prev); stage.Add(_next);
        var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        root.Add(stage, 0, 0); root.Add(_caption, 0, 1); root.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = bar }, 0, 2);
        Content = root;
        ShowItem();
    }

    /// <summary>dir: +1 nächstes, -1 vorheriges Bild.</summary>
    private void Go(int dir)
    {
        int n = _index + dir;
        if (n < 0 || n >= _items.Count) return;
        _index = n;
        ShowItem();
    }

    private void ShowItem()
    {
        _zoom.Reset();
        _img.Source = ImageSource.FromFile(Item.LocalPath);
        _caption.Text = _items.Count > 1 ? $"{_index + 1} / {_items.Count} · {Item.Caption}" : Item.Caption;
        _star.Text = Item.Starred ? "★ Stern entfernen" : "☆ Stern setzen";
        _prev.IsVisible = _index > 0;
        _next.IsVisible = _index < _items.Count - 1;
    }

    private async Task CloseAsync(ViewerAction a)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult((a, Item));
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(ViewerAction.None); return true; }

    /// <summary>Bild items[index] zeigen; liefert die gewählte Aktion und das Bild, auf das sie sich bezieht.</summary>
    public static async Task<(ViewerAction Action, ResultItem Item)> ShowAsync(INavigation nav, IReadOnlyList<ResultItem> items, int index)
    {
        var p = new ViewerPage(items, index);
        await nav.PushModalAsync(p);
        return await p._done.Task;
    }
}
