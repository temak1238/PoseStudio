using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PoseStudio.Swarm;

/// <summary>Charakter-Chips unter dem Charakterfeld: Name und Serie fest, Aussehen-Tags aus AnimaDex an- und abwählbar.</summary>
public partial class SwarmWindow
{
    private bool _charChipsBusy;

    private void BuildCharacterChips()
    {
        var p = CharacterChipsPanel;
        p.Children.Clear();
        var chips = CharacterChips.List(_s.Character, _s.CharacterTrigger, _s.CharacterTags);
        // nur zeigen, wenn es etwas zu wählen gibt (AnimaDex-Charakter); reiner Freitext braucht keine Chips
        bool show = _s.CharacterTags.Count > 0 || _s.CharacterTrigger != "";
        p.Visibility = show && chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        foreach (var (tag, on, fix) in chips)
        {
            var chip = new ToggleButton { Content = tag, IsChecked = on, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 1, 8, 2), FontSize = 11 };
            if (fix)
            {
                chip.FontWeight = FontWeights.SemiBold;
                chip.IsHitTestVisible = false;
                chip.Focusable = false;
            }
            else
            {
                chip.ToolTip = on ? "Klicken: Tag weglassen" : "Klicken: Tag übernehmen";
                chip.Click += (_, _) =>
                {
                    _s.Character = CharacterBox.Text;
                    _s.ToggleCharacterTag(tag);
                    _charChipsBusy = true;
                    CharacterBox.Text = _s.Character;
                    _charChipsBusy = false;
                    BuildCharacterChips();
                };
            }
            p.Children.Add(chip);
        }
        int off = chips.Count(c => !c.On && !c.Fixed);
        if (off > 0 && chips.Any(c => c.On && !c.Fixed))
        {
            var none = new Button { Content = "Nur Name", Margin = new Thickness(6, 0, 4, 4), Padding = new Thickness(8, 1, 8, 2), FontSize = 11, ToolTip = "Alle Aussehen-Tags weglassen" };
            none.Click += (_, _) => SetAllCharacterTags(false);
            p.Children.Add(none);
        }
        if (off > 0)
        {
            var all = new Button { Content = "Alle", Margin = new Thickness(chips.Any(c => c.On && !c.Fixed) ? 0 : 6, 0, 4, 4), Padding = new Thickness(8, 1, 8, 2), FontSize = 11, ToolTip = "Alle Aussehen-Tags übernehmen" };
            all.Click += (_, _) => SetAllCharacterTags(true);
            p.Children.Add(all);
        }
    }

    private void SetAllCharacterTags(bool on)
    {
        _s.Character = CharacterBox.Text;
        foreach (var c in CharacterChips.List(_s.Character, _s.CharacterTrigger, _s.CharacterTags).Where(c => !c.Fixed && c.On != on))
            _s.ToggleCharacterTag(c.Tag);
        _charChipsBusy = true;
        CharacterBox.Text = _s.Character;
        _charChipsBusy = false;
        BuildCharacterChips();
    }

    private void CharacterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _charChipsBusy || !IsLoaded) return;
        _s.Character = CharacterBox.Text;
        BuildCharacterChips();
    }

    private void CharacterBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _s.Character = CharacterBox.Text;
        _s.CharacterEdited();
        BuildCharacterChips();
    }

    /// <summary>Für Bildschirmfotos: Beispiel-Charakter mit teils gewählten Tags.</summary>
    internal void ShowCharacterForShot()
    {
        _s.CharacterTrigger = "frieren, sousou no frieren";
        _s.CharacterTags = new() { "white hair", "long hair", "twintails", "green eyes", "pointy ears", "elf", "earrings", "white capelet", "jewelry", "thick eyebrows" };
        _s.Character = "frieren, sousou no frieren, white hair, twintails, green eyes, pointy ears";
        CharacterBox.Text = _s.Character;
        BuildCharacterChips();
        CharacterChipsPanel.BringIntoView();
    }
}
