using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PoseStudio;

public sealed record AspectFormat(string Key, string Name, double W, double H)
{
    public bool IsCustom => Key == "custom";
    public override string ToString() => Name;

    public static IReadOnlyList<AspectFormat> All { get; } = new List<AspectFormat>
    {
        new("16x9", "16:9 Querformat", 16, 9),
        new("9x16", "9:16 Hochformat", 9, 16),
        new("1x1", "1:1 Quadrat", 1, 1),
        new("4x3", "4:3 Querformat", 4, 3),
        new("3x4", "3:4 Hochformat", 3, 4),
        new("3x2", "3:2 Querformat", 3, 2),
        new("2x3", "2:3 Hochformat", 2, 3),
        new("4x5", "4:5 Hochformat", 4, 5),
        new("21x9", "21:9 Kino", 21, 9),
        new("custom", "Benutzerdefiniert", 0, 0),
    };

    /// <summary>Pixelgröße: lange Seite = longSide, kurze Seite auf gerade Zahl gerundet.</summary>
    public (int W, int H) PixelSize(int longSide, int customW, int customH)
    {
        if (IsCustom) return (Math.Max(16, customW), Math.Max(16, customH));
        if (W >= H)
            return (longSide, Even(longSide * H / W));
        return (Even(longSide * W / H), longSide);
    }

    public double Aspect(int customW, int customH) => IsCustom ? (double)Math.Max(1, customW) / Math.Max(1, customH) : W / H;

    private static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
}

public sealed class FormatOption : INotifyPropertyChanged
{
    private bool _isChecked;
    public required AspectFormat Format { get; init; }
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Ein Regler für eine Gelenkachse im Panel "Ausgewähltes Gelenk".</summary>
public sealed class AxisViewModel : INotifyPropertyChanged
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;

    public AxisViewModel(string label, double min, double max, Func<double> get, Action<double> set)
    {
        Label = label; Min = min; Max = max; _get = get; _set = set;
    }

    public string Label { get; }
    public double Min { get; }
    public double Max { get; }

    public double Value
    {
        get => _get();
        set
        {
            var v = Math.Clamp(Math.Round(value, 1), Min, Max);
            if (Math.Abs(v - _get()) < 1e-9) return;
            _set(v);
            OnChanged();
        }
    }

    public void Refresh() => OnChanged(nameof(Value));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
