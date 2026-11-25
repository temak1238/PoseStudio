using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Tag-Vorschläge unter einem Textfeld (wie die Wortvorschläge einer Handy-Tastatur): beim Tippen eines Wortes eine Zeile
/// mit den passendsten Danbooru-Tags; Antippen ersetzt das angefangene Wort. Nicht antippen = es passiert nichts.</summary>
public sealed class TagSuggestBar : ContentView
{
    private readonly Editor _box;
    private readonly Func<bool> _enabled;
    private readonly HorizontalStackLayout _row = new() { Spacing = 6 };
    private (int Start, bool Whole, List<BooruSuggestion> Items)? _cur;
    private string? _dismissed;

    public TagSuggestBar(Editor box, Func<bool> enabled)
    {
        _box = box; _enabled = enabled;
        Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _row };
        IsVisible = false;
        box.TextChanged += (_, _) => Dispatcher.Dispatch(Update);   // Cursorstelle ist erst danach aktuell
        box.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Editor.CursorPosition)) Update(); };
        BooruTags.Preload();
    }

    public void Hide()
    {
        _cur = null;
        if (IsVisible) { _row.Children.Clear(); IsVisible = false; }
    }

    public void Update()
    {
        string text = _box.Text ?? "";
        if (!_enabled() || _dismissed == text) { Hide(); return; }
        _dismissed = null;
        int caret = Math.Clamp(_box.CursorPosition, 0, text.Length);
        var res = BooruTags.Default?.SuggestAt(text, caret);
        if (res == null) { Hide(); return; }
        if (_cur is { } old && old.Start == res.Value.Start && old.Items.Select(x => x.Tag).SequenceEqual(res.Value.Items.Select(x => x.Tag))) return;
        _cur = res;
        _row.Children.Clear();
        foreach (var s in res.Value.Items)
        {
            var b = new Button
            {
                Text = s.Tag.Name + (s.Via != null ? $"  ← {s.Via}" : "") + "  · " + s.Tag.CountText,
                FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34,
                BackgroundColor = Ui.PanelLight, TextColor = Colors.White, BorderColor = Ui.Accent, BorderWidth = 1,
            };
            var tag = s.Tag;
            b.Clicked += (_, _) => Accept(tag);
            _row.Children.Add(b);
        }
        IsVisible = true;
    }

    private void Accept(BooruTag tag)
    {
        if (_cur is not { } c) return;
        string text = _box.Text ?? "";
        int caret = Math.Clamp(_box.CursorPosition, c.Start, text.Length);
        var (t, newCaret) = BooruTags.Apply(text, c.Start, caret, tag, c.Whole);
        _dismissed = t;   // nicht gleich denselben Tag wieder vorschlagen
        Hide();
        _box.Text = t;
        _box.Focus();
        Dispatcher.Dispatch(() => _box.CursorPosition = Math.Min(newCaret, (_box.Text ?? "").Length));
    }
}
