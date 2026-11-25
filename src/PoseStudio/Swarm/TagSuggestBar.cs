using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoseStudio.Swarm;

/// <summary>Tag-Vorschläge unter einem Textfeld: beim Tippen eines Wortes die passendsten Danbooru-Tags als Knöpfe.
/// Anklicken (oder Tab für den ersten) ersetzt das angefangene Wort; Esc blendet aus. Wer weiterschreibt, merkt nichts davon.</summary>
internal sealed class TagSuggestBar
{
    private readonly TextBox _box;
    private readonly WrapPanel _bar;
    private readonly Func<bool> _enabled;
    private (int Start, bool Whole, List<BooruSuggestion> Items)? _cur;
    private string? _dismissed;   // nach Esc: aus, bis sich der Text ändert

    public TagSuggestBar(TextBox box, WrapPanel bar, Func<bool> enabled)
    {
        _box = box; _bar = bar; _enabled = enabled;
        _bar.Visibility = Visibility.Collapsed;
        _bar.ToolTip = "Klick übernimmt den Tag · Tab = erster Vorschlag · Esc blendet aus";
        box.SelectionChanged += (_, _) => Update();
        box.LostKeyboardFocus += (_, _) => Hide();
        box.PreviewKeyDown += (_, e) =>
        {
            if (_cur == null) return;
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) { Accept(_cur.Value.Items[0].Tag); e.Handled = true; }
            else if (e.Key == Key.Escape) { _dismissed = _box.Text; Hide(); e.Handled = true; }
        };
        BooruTags.Preload();
    }

    public void Hide()
    {
        _cur = null;
        if (_bar.Visibility != Visibility.Collapsed) { _bar.Children.Clear(); _bar.Visibility = Visibility.Collapsed; }
    }

    private void Update()
    {
        if (!_enabled() || !_box.IsKeyboardFocusWithin || _box.SelectionLength > 0 || _dismissed == _box.Text) { Hide(); return; }
        _dismissed = null;
        var res = BooruTags.Default?.SuggestAt(_box.Text, _box.CaretIndex);
        if (res == null) { Hide(); return; }
        if (_cur is { } old && old.Start == res.Value.Start && old.Items.Select(x => x.Tag).SequenceEqual(res.Value.Items.Select(x => x.Tag))) return;
        _cur = res;
        _bar.Children.Clear();
        foreach (var s in res.Value.Items)
        {
            var text = new TextBlock();
            text.Inlines.Add(new System.Windows.Documents.Run(s.Tag.Name));
            var meta = new System.Windows.Documents.Run((s.Via != null ? $"  ← {s.Via}" : "") + "  " + s.Tag.CountText) { FontSize = 10 };
            meta.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "MutedText");
            text.Inlines.Add(meta);
            var b = new Button { Content = text, Focusable = false, Margin = new Thickness(0, 0, 4, 3), Padding = new Thickness(7, 1, 7, 1), FontSize = 11,
                                 ToolTip = $"{s.Tag.CategoryName} · {s.Tag.CountText} Bilder" + (s.Via != null ? $" · Alias „{s.Via}“" : "") };
            var tag = s.Tag;
            b.Click += (_, _) => Accept(tag);
            _bar.Children.Add(b);
        }
        _bar.Visibility = Visibility.Visible;
    }

    private void Accept(BooruTag tag)
    {
        if (_cur is not { } c) return;
        var (t, caret) = BooruTags.Apply(_box.Text, c.Start, _box.CaretIndex, tag, c.Whole);
        Hide();
        _box.Text = t;
        _box.CaretIndex = caret;
        _dismissed = _box.Text;   // nicht gleich denselben Tag wieder vorschlagen
        Hide();
        _box.Focus();
    }
}
