namespace PoseStudio.Tablet;

/// <summary>Prompt und Negativ-Prompt bildschirmfüllend bearbeiten (für lange Prompts).</summary>
public sealed class PromptEditorPage : ContentPage
{
    private readonly TaskCompletionSource<(string Prompt, string Negative)?> _done = new();
    private readonly Editor _prompt, _negative;

    public PromptEditorPage(string prompt, string negative, Func<bool> suggest)
    {
        BackgroundColor = Ui.Page;
        _prompt = new Editor { Text = prompt, Placeholder = "Prompt", FontSize = 16, BackgroundColor = Ui.Panel, TextColor = Colors.White };
        _negative = new Editor { Text = negative, Placeholder = "Negativ-Prompt", FontSize = 15, BackgroundColor = Ui.Panel, TextColor = Colors.White };
        Button B(string text, bool accent, Func<Task> act)
        {
            var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White, MinimumWidthRequest = 140 };
            b.Clicked += async (_, _) => await act();
            return b;
        }
        var bar = new HorizontalStackLayout
        {
            Spacing = 8, HorizontalOptions = LayoutOptions.End,
            Children = { B("Abbrechen", false, () => CloseAsync(null)), B("Übernehmen", true, () => CloseAsync((_prompt.Text ?? "", _negative.Text ?? ""))) }
        };
        Label L(string t) => new() { Text = t, TextColor = Ui.Muted, FontSize = 13 };
        var g = new Grid
        {
            Padding = 12, RowSpacing = 6,
            RowDefinitions = { new(GridLength.Auto), new(new GridLength(3, GridUnitType.Star)), new(GridLength.Auto), new(new GridLength(1, GridUnitType.Star)), new(GridLength.Auto) }
        };
        // Tag-Vorschläge ganz unten; bei offener Tastatur schiebt sich die Zeile selbst darüber
        var suggestBar = new TagSuggestBar(suggest).Attach(_prompt).Attach(_negative);
        g.RowDefinitions.Add(new(GridLength.Auto));
        g.Add(L("Prompt"), 0, 0); g.Add(_prompt, 0, 1); g.Add(L("Negativ-Prompt"), 0, 2); g.Add(_negative, 0, 3); g.Add(bar, 0, 4); g.Add(suggestBar, 0, 5);
        Content = g;
        Loaded += (_, _) => _prompt.Focus();
    }

    private async Task CloseAsync((string, string)? result)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult(result);
        await Navigation.PopModalAsync();
    }

    /// <summary>Zurück-Taste übernimmt (wie bei einem Textfeld erwartet) – Abbrechen nur über den Knopf.</summary>
    protected override bool OnBackButtonPressed() { _ = CloseAsync((_prompt.Text ?? "", _negative.Text ?? "")); return true; }

    public static async Task<(string Prompt, string Negative)?> ShowAsync(INavigation nav, string prompt, string negative, Func<bool> suggest)
    {
        var p = new PromptEditorPage(prompt, negative, suggest);
        await nav.PushModalAsync(p);
        return await p._done.Task;
    }
}
