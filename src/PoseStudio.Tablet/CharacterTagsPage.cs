using Microsoft.Maui.Layouts;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Aussehen-Tags des Charakters auswählen (wie die Tag-Auswahl der LoRAs): Name und Serie fest, übrige Tags als Chips.
/// Ändert direkt den Charaktertext in den Einstellungen.</summary>
public sealed class CharacterTagsPage : ContentPage
{
    private readonly TaskCompletionSource _done = new();
    private readonly SwarmSettings _s;
    private readonly FlexLayout _chips = new() { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start, AlignItems = FlexAlignItems.Start };
    private readonly Label _summary = new() { FontSize = 13 };
    private static readonly Color ChipOn = Color.FromArgb("#2B5D96");

    private CharacterTagsPage(SwarmSettings s)
    {
        _s = s;
        BackgroundColor = Ui.Page;
        Button B(string text, bool accent, Action act)
        {
            var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White, Padding = new Thickness(14, 0) };
            b.Clicked += (_, _) => act();
            return b;
        }
        string name = string.Join(", ", CharacterChips.List(s.Character, s.CharacterTrigger, s.CharacterTags).Where(c => c.Fixed).Select(c => c.Tag));
        var title = new Label { Text = name == "" ? "Charakter" : name, TextColor = Colors.White, FontSize = 18, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        head.Add(title, 0); head.Add(B("Fertig", true, () => _ = CloseAsync()), 1);

        var quick = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
            Children = { B("Alle", false, () => SetAll(true)), B("Nur Name", false, () => SetAll(false)) } };
        var stack = new VerticalStackLayout { Spacing = 4, Children = {
            _summary,
            new Label { Text = "Aussehen-Tags – antippen zum An- und Abwählen. Name und Serie bleiben immer drin.", TextColor = Ui.Muted, FontSize = 13, Margin = new Thickness(0, 8, 0, 2) },
            _chips, quick } };
        var g = new Grid { Padding = 12, RowSpacing = 8, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        g.Add(head, 0, 0); g.Add(new ScrollView { Content = stack }, 0, 1);
        Content = g;
        Fill();
    }

    private void SetAll(bool on)
    {
        foreach (var c in CharacterChips.List(_s.Character, _s.CharacterTrigger, _s.CharacterTags).Where(c => !c.Fixed && c.On != on)) _s.ToggleCharacterTag(c.Tag);
        Fill();
    }

    private void Fill()
    {
        _chips.Children.Clear();
        foreach (var (tag, on, fix) in CharacterChips.List(_s.Character, _s.CharacterTrigger, _s.CharacterTags))
        {
            var chip = new Button { Text = tag, FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34, Margin = new Thickness(0, 0, 6, 6),
                                    BackgroundColor = on ? ChipOn : Ui.PanelLight, TextColor = Colors.White, BorderColor = on ? Ui.Accent : Ui.PanelLight, BorderWidth = 1 };
            if (fix) { chip.FontAttributes = FontAttributes.Bold; chip.InputTransparent = true; }
            else chip.Clicked += (_, _) => { _s.ToggleCharacterTag(tag); Fill(); };
            _chips.Children.Add(chip);
        }
        var any = CharacterChips.List(_s.Character, _s.CharacterTrigger, _s.CharacterTags).Any(c => c.On && !c.Fixed);
        _summary.Text = CharacterChips.Summary(_s.Character, _s.CharacterTrigger, _s.CharacterTags);
        _summary.TextColor = any ? Ui.Accent : Ui.Muted;
    }

    private async Task CloseAsync()
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult();
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }

    public static async Task ShowAsync(INavigation nav, SwarmSettings s)
    {
        var p = new CharacterTagsPage(s);
        await nav.PushModalAsync(p);
        await p._done.Task;
    }
}
