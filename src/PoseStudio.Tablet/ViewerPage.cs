namespace PoseStudio.Tablet;

public enum ViewerAction { None, Refine, UseSeed, UseAsImage, ToggleStar, Remove }

/// <summary>Ergebnis groß anzeigen; Aktionen Nachbessern, Seed, als Bild verwenden, Galerie, Teilen, aus Liste entfernen.</summary>
public sealed class ViewerPage : ContentPage
{
    private readonly TaskCompletionSource<ViewerAction> _done = new();

    public ViewerPage(ResultItem item)
    {
        BackgroundColor = Colors.Black;
        var img = new Image { Source = ImageSource.FromFile(item.LocalPath), Aspect = Aspect.AspectFit };
        var zoom = new ZoomView { Content = img };

        Button B(string text, Func<Task> act, bool accent = false)
        {
            var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White, Margin = new Thickness(4, 0) };
            b.Clicked += async (_, _) => await act();
            return b;
        }
        var bar = new HorizontalStackLayout
        {
            Spacing = 4, HorizontalOptions = LayoutOptions.Center, Padding = new Thickness(8),
            Children =
            {
                B("Nachbessern", () => CloseAsync(ViewerAction.Refine), true),
                B("Seed übernehmen", () => CloseAsync(ViewerAction.UseSeed)),
                B(item.Starred ? "★ Stern entfernen" : "☆ Stern setzen", () => CloseAsync(ViewerAction.ToggleStar)),
                B("Als Bild verwenden", () => CloseAsync(ViewerAction.UseAsImage)),
                B("In Galerie", async () =>
                {
                    try
                    {
                        if (!OperatingSystem.IsAndroidVersionAtLeast(29) && await Permissions.RequestAsync<Permissions.StorageWrite>() != PermissionStatus.Granted) return;
                        AndroidStore.SaveToGallery(item.LocalPath!);
                        await DisplayAlertAsync("Galerie", "Gespeichert unter Bilder/PoseStudio.", "OK");
                    }
                    catch (Exception ex) { await DisplayAlertAsync("Galerie", ex.Message, "OK"); }
                }),
                B("Teilen", () => Share.Default.RequestAsync(new ShareFileRequest { Title = "Bild teilen", File = new ShareFile(item.LocalPath!) })),
                B("Aus Liste entfernen", () => CloseAsync(ViewerAction.Remove)),
                B("Schließen", () => CloseAsync(ViewerAction.None)),
            }
        };
        var caption = new Label { Text = item.Caption, TextColor = Ui.Muted, HorizontalOptions = LayoutOptions.Center, FontSize = 13 };
        var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        root.Add(zoom, 0, 0); root.Add(caption, 0, 1); root.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = bar }, 0, 2);
        Content = root;
    }

    private async Task CloseAsync(ViewerAction a)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult(a);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(ViewerAction.None); return true; }

    public static async Task<ViewerAction> ShowAsync(INavigation nav, ResultItem item)
    {
        var p = new ViewerPage(item);
        await nav.PushModalAsync(p);
        return await p._done.Task;
    }
}
