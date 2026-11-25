using Microsoft.Maui.Layouts;
using PoseStudio.Swarm;

namespace PoseStudio.Tablet;

/// <summary>Trigger-Wörter einer LoRA auswählen: Tags aus den Metadaten als Chips, Beschreibungen getrennt, eigene Wörter.
/// Die Auswahl gilt für die LoRA überall (SwarmSettings.LoraTagChoices).</summary>
public sealed class LoraTagsPage : ContentPage
{
    private const int ChipsFirst = 40;
    private readonly TaskCompletionSource _done = new();
    private readonly LoraTagChoice _choice;
    private readonly List<string> _tags;
    private readonly List<(string Title, List<string> Tags)> _groups;
    private readonly BooruTags? _db = BooruTags.Default;
    private readonly VerticalStackLayout _chips = new() { Spacing = 2 };
    private readonly Entry _search = new() { Placeholder = "Tags suchen …", ClearButtonVisibility = ClearButtonVisibility.WhileEditing, BackgroundColor = Ui.Panel, TextColor = Colors.White };
    private readonly Entry _own = new() { Placeholder = "z. B. golden jewelry, veil", BackgroundColor = Ui.Panel, TextColor = Colors.White };
    private readonly Label _summary = new() { TextColor = Ui.Accent, FontSize = 13 };
    private bool _showAll;

    private static readonly Color ChipOn = Color.FromArgb("#2B5D96");

    private LoraTagsPage(SwarmSettings s, string lora, SwarmModel? info)
    {
        BackgroundColor = Ui.Page;
        var (tags, descs) = LoraTags.Parse(info?.Trigger);
        _choice = s.TagChoice(lora, true)!;
        _tags = _choice.Tags.Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase)).Concat(tags).ToList();
        // mit der Danbooru-Liste ordnen: eigene Wörter der LoRA zuerst, dann nach Kategorie; Satzreste zu den Beschreibungen
        _groups = new() { ("", _tags) };
        if (_db != null)
        {
            var (grp, junk) = _db.Group(_tags, descs.Count);
            var keep = junk.Where(j => _choice.Tags.Contains(j, StringComparer.OrdinalIgnoreCase)).ToList();
            junk = junk.Except(keep, StringComparer.OrdinalIgnoreCase).ToList();
            if (keep.Count > 0) grp.Insert(0, ("Gewählt", keep));
            _groups = grp;
            _tags = _groups.SelectMany(x => x.Tags).ToList();
            if (junk.Count > 0) descs = descs.Append("Reste ohne Buchstaben: " + string.Join(" · ", junk)).ToList();
        }
        _showAll = _tags.Count <= ChipsFirst + 10;
        _own.Text = _choice.Own;
        _own.TextChanged += (_, _) => { _choice.Own = (_own.Text ?? "").Trim(); ShowSummary(); };
        _search.TextChanged += (_, _) => FillChips();

        Label L(string t) => new() { Text = t, TextColor = Ui.Muted, FontSize = 13, Margin = new Thickness(0, 8, 0, 2) };
        Button B(string text, bool accent, Action act)
        {
            var b = new Button { Text = text, BackgroundColor = accent ? Ui.Accent : Ui.PanelLight, TextColor = Colors.White, Padding = new Thickness(14, 0) };
            b.Clicked += (_, _) => act();
            return b;
        }

        var title = new Label { Text = info?.Short ?? lora, TextColor = Colors.White, FontSize = 18, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        head.Add(title, 0); head.Add(B("Fertig", true, () => _ = CloseAsync()), 1);

        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Add(_summary);
        stack.Add(L(_tags.Count == 0 ? (info == null ? "Keine Metadaten – erst mit SwarmUI verbinden. Eigene Trigger-Wörter gehen trotzdem." : "In den Metadaten stehen keine Tags.")
                                     : $"Tags ({_tags.Count}) – antippen zum An- und Abwählen"));
        if (_tags.Count > 30) stack.Add(_search);
        stack.Add(_chips);
        if (descs.Count > 0)
        {
            var list = new VerticalStackLayout { Spacing = 6, IsVisible = false };
            foreach (var d in descs) list.Add(new Label { Text = d, TextColor = Ui.Muted, FontSize = 12, BackgroundColor = Ui.Panel, Padding = new Thickness(8, 4) });
            var toggle = new Button { Text = $"▸ Beschreibungen ({descs.Count}) – werden nie automatisch angehängt", BackgroundColor = Colors.Transparent, TextColor = Ui.Muted,
                                      FontSize = 13, HorizontalOptions = LayoutOptions.Start, Padding = 0 };
            toggle.Clicked += (_, _) =>
            {
                list.IsVisible = !list.IsVisible;
                toggle.Text = (list.IsVisible ? "▾" : "▸") + toggle.Text[1..];
            };
            stack.Add(toggle); stack.Add(list);
        }
        stack.Add(L("Eigene Trigger-Wörter (kommagetrennt)"));
        stack.Add(_own);
        var quick = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        if (_tags.Count <= 30) quick.Add(B("Alle", false, () => { _choice.Tags = _tags.ToList(); FillChips(); }));
        quick.Add(B("Nur erstes", false, () => { _choice.Tags = _tags.Take(1).ToList(); FillChips(); }));
        quick.Add(B("Keine", false, () => { _choice.Tags = new(); FillChips(); }));
        stack.Add(quick);

        var g = new Grid { Padding = 12, RowSpacing = 8, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        g.Add(head, 0, 0); g.Add(new ScrollView { Content = stack }, 0, 1);
        Content = g;
        FillChips();
    }

    private void ShowSummary()
    {
        var all = _choice.All();
        _summary.Text = all.Count == 0 ? "Nichts gewählt – es wird nichts angehängt." : "Wird angehängt: " + string.Join(", ", all);
        _summary.TextColor = all.Count == 0 ? Ui.Muted : Ui.Accent;
    }

    private void FillChips()
    {
        _chips.Children.Clear();
        string q = (_search.Text ?? "").Trim();
        int total = _tags.Count(t => q == "" || t.Contains(q, StringComparison.OrdinalIgnoreCase));
        int max = _showAll || q != "" ? total : ChipsFirst, done = 0;
        FlexLayout? row = null;
        foreach (var (title, gtags) in _groups)
        {
            var shown = gtags.Where(t => q == "" || t.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(Math.Max(0, max - done)).ToList();
            if (shown.Count == 0) continue;
            if (title != "" && _groups.Count > 1) _chips.Children.Add(new Label { Text = title, TextColor = Ui.Muted, FontSize = 12, Margin = new Thickness(0, 4, 0, 2) });
            row = new FlexLayout { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start, AlignItems = FlexAlignItems.Start };
            _chips.Children.Add(row);
            done += shown.Count;
            foreach (var t in shown)
            {
                bool on = _choice.Tags.Contains(t, StringComparer.OrdinalIgnoreCase);
                var chip = new Button { Text = t, FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34, Margin = new Thickness(0, 0, 6, 6),
                                        BackgroundColor = on ? ChipOn : Ui.PanelLight, TextColor = Colors.White, BorderColor = on ? Ui.Accent : Ui.PanelLight, BorderWidth = 1 };
                chip.Clicked += (_, _) =>
                {
                    if (_choice.Tags.RemoveAll(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)) == 0) _choice.Tags.Add(t);
                    _choice.Tags = _choice.Tags.OrderBy(x => { int i = _tags.FindIndex(y => y.Equals(x, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).ToList();
                    bool now = _choice.Tags.Contains(t, StringComparer.OrdinalIgnoreCase);
                    chip.BackgroundColor = now ? ChipOn : Ui.PanelLight; chip.BorderColor = now ? Ui.Accent : Ui.PanelLight;
                    ShowSummary();
                };
                row.Children.Add(chip);
            }
        }
        if (total > max && row != null)
        {
            var more = new Button { Text = $"+ {total - max} weitere …", FontSize = 13, CornerRadius = 16, Padding = new Thickness(12, 0), HeightRequest = 34,
                                    BackgroundColor = Colors.Transparent, TextColor = Ui.Muted, BorderColor = Ui.Muted, BorderWidth = 1 };
            more.Clicked += (_, _) => { _showAll = true; FillChips(); };
            row.Children.Add(more);
        }
        ShowSummary();
    }

    private async Task CloseAsync()
    {
        if (_done.Task.IsCompleted) return;
        _choice.Own = (_own.Text ?? "").Trim();
        _done.TrySetResult();
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }

    public static async Task ShowAsync(INavigation nav, SwarmSettings s, string lora, SwarmModel? info)
    {
        var p = new LoraTagsPage(s, lora, info);
        await nav.PushModalAsync(p);
        await p._done.Task;
    }
}
