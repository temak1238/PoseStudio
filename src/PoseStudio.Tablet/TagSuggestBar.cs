using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Tag-Vorschläge wie die Wortvorschläge einer Handy-Tastatur: eine Zeile ganz unten auf der Seite – bei offener Tastatur
/// sitzt sie direkt darüber (Fenster wird verkleinert statt verschoben). Gilt für mehrere Textfelder; es zählt das, in dem gerade
/// getippt wird. Antippen ersetzt das angefangene Wort; nicht antippen = es passiert nichts.</summary>
public sealed class TagSuggestBar : ContentView
{
    private readonly Func<bool> _enabled;
    private readonly HorizontalStackLayout _row = new() { Spacing = 6, Padding = new Thickness(8, 6) };
    private Editor? _box;
    private (int Start, bool Whole, List<BooruSuggestion> Items)? _cur;
    private string? _dismissed;

    public TagSuggestBar(Func<bool> enabled)
    {
        _enabled = enabled;
        BackgroundColor = Ui.Panel;
        Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _row };
        IsVisible = false;
        BooruTags.Preload();
    }

    /// <summary>Textfeld anmelden.</summary>
    public TagSuggestBar Attach(Editor box)
    {
        box.Focused += (_, _) => { _box = box; Update(); };
        box.Unfocused += (_, _) => Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => { if (_box == box && !box.IsFocused) { _box = null; Hide(); } });
        box.TextChanged += (_, _) => { if (box.IsFocused) { _box = box; Dispatcher.Dispatch(Update); } };   // Cursorstelle ist erst danach aktuell
        box.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Editor.CursorPosition) && box.IsFocused) { _box = box; Update(); } };
        return this;
    }

    public void Hide()
    {
        _cur = null;
        if (IsVisible) { _row.Children.Clear(); IsVisible = false; }
    }

    public void Update()
    {
        var box = _box;
        string text = box?.Text ?? "";
        if (box == null || !_enabled() || _dismissed == text) { Hide(); return; }
        _dismissed = null;
        int caret = Math.Clamp(box.CursorPosition, 0, text.Length);
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
                FontSize = 14, CornerRadius = 18, Padding = new Thickness(14, 0), HeightRequest = 38,
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
        if (_cur is not { } c || _box is not { } box) return;
        string text = box.Text ?? "";
        int caret = Math.Clamp(box.CursorPosition, c.Start, text.Length);
        var (t, newCaret) = BooruTags.Apply(text, c.Start, caret, tag, c.Whole);
        _dismissed = t;   // nicht gleich denselben Tag wieder vorschlagen
        Hide();
        box.Text = t;
        box.Focus();
        Dispatcher.Dispatch(() => box.CursorPosition = Math.Min(newCaret, (box.Text ?? "").Length));
    }
}
