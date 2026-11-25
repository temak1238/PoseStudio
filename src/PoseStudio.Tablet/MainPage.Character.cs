using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Charakter-Chips unter dem Charakterfeld: Name und Serie fest, Aussehen-Tags aus AnimaDex an- und abwählbar.</summary>
public sealed partial class MainPage
{
    /// <summary>Kurzanzeige unter dem Charakterfeld; Antippen öffnet die Auswahl (wie bei den LoRAs).</summary>
    private readonly Label _charChips = new() { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2, IsVisible = false };
    private bool _charChipsHooked, _charChipsBusy;

    private TagSuggestBar? _suggestBar;
    private TagSuggestBar _suggest => _suggestBar ??= new TagSuggestBar(() => S.TagSuggest);

    /// <summary>Schalter „Tag-Vorschläge“ (gilt für alle Profile).</summary>
    private View TagSuggestRow()
    {
        var sw = new Switch { IsToggled = S.TagSuggest, OnColor = Ui.Accent, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += (_, e) => { S.TagSuggest = e.Value; if (!e.Value) _suggest.Hide(); SaveSoon(); };
        return new HorizontalStackLayout { Spacing = 8, Children = { sw, new Label { Text = "Tag-Vorschläge beim Tippen (Danbooru)", TextColor = Ui.Muted, FontSize = 13, VerticalOptions = LayoutOptions.Center } } };
    }

    private void ShowCharacterChips()
    {
        if (!_charChipsHooked)
        {
            _charChipsHooked = true;
            _character.TextChanged += (_, _) => { if (_suppress || _charChipsBusy) return; S.Character = _character.Text ?? ""; ShowCharacterChips(); };
            _character.Unfocused += (_, _) => { S.Character = _character.Text ?? ""; S.CharacterEdited(); ShowCharacterChips(); };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                S.Character = _character.Text ?? "";
                await CharacterTagsPage.ShowAsync(Navigation, S);
                SetCharacterText();
            };
            _charChips.GestureRecognizers.Add(tap);
        }
        var chips = CharacterChips.List(S.Character, S.CharacterTrigger, S.CharacterTags);
        bool show = (S.CharacterTags.Count > 0 || S.CharacterTrigger != "") && chips.Any(c => !c.Fixed);
        _charChips.IsVisible = show;
        if (!show) return;
        _charChips.Text = CharacterChips.Summary(S.Character, S.CharacterTrigger, S.CharacterTags) + "  ✎";
        _charChips.TextColor = chips.Any(c => c.On && !c.Fixed) ? Ui.Accent : Ui.Muted;
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
