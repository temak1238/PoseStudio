using Microsoft.Maui.Layouts;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Charakter-Chips unter dem Charakterfeld: Name und Serie fest, Aussehen-Tags aus AnimaDex an- und abwählbar.</summary>
public sealed partial class MainPage
{
    private readonly FlexLayout _charChips = new() { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start, AlignItems = FlexAlignItems.Start, IsVisible = false };
    private bool _charChipsHooked, _charChipsBusy;
    private static readonly Color CharChipOn = Color.FromArgb("#2B5D96");

    private Button CharChip(string text, bool on, bool fix)
    {
        var b = new Button { Text = text, FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34, Margin = new Thickness(0, 0, 6, 6),
                             BackgroundColor = on ? CharChipOn : Ui.PanelLight, TextColor = Colors.White, BorderColor = on ? Ui.Accent : Ui.PanelLight, BorderWidth = 1 };
        if (fix) { b.FontAttributes = FontAttributes.Bold; b.InputTransparent = true; }
        return b;
    }

    private void ShowCharacterChips()
    {
        if (!_charChipsHooked)
        {
            _charChipsHooked = true;
            _character.TextChanged += (_, _) => { if (_suppress || _charChipsBusy) return; S.Character = _character.Text ?? ""; ShowCharacterChips(); };
            _character.Unfocused += (_, _) => { S.Character = _character.Text ?? ""; S.CharacterEdited(); ShowCharacterChips(); };
        }
        _charChips.Children.Clear();
        var chips = CharacterChips.List(S.Character, S.CharacterTrigger, S.CharacterTags);
        bool show = (S.CharacterTags.Count > 0 || S.CharacterTrigger != "") && chips.Count > 0;
        _charChips.IsVisible = show;
        if (!show) return;
        foreach (var (tag, on, fix) in chips)
        {
            var chip = CharChip(tag, on, fix);
            if (!fix) chip.Clicked += (_, _) => { S.ToggleCharacterTag(tag); SetCharacterText(); };
            _charChips.Children.Add(chip);
        }
        bool anyOn = chips.Any(c => c.On && !c.Fixed), anyOff = chips.Any(c => !c.On && !c.Fixed);
        void Quick(string text, bool on)
        {
            var b = new Button { Text = text, FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34, Margin = new Thickness(0, 0, 6, 6),
                                 BackgroundColor = Colors.Transparent, TextColor = Ui.Muted, BorderColor = Ui.Muted, BorderWidth = 1 };
            b.Clicked += (_, _) =>
            {
                foreach (var c in CharacterChips.List(S.Character, S.CharacterTrigger, S.CharacterTags).Where(c => !c.Fixed && c.On != on)) S.ToggleCharacterTag(c.Tag);
                SetCharacterText();
            };
            _charChips.Children.Add(b);
        }
        if (anyOn && anyOff) Quick("Nur Name", false);
        if (anyOff) Quick("Alle", true);
    }

    private void SetCharacterText()
    {
        _charChipsBusy = true;
        _character.Text = S.Character;
        _charChipsBusy = false;
        ShowCharacterChips();
        SaveSoon();
    }
}
