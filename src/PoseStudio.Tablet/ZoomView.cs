namespace PoseStudio.Tablet;

/// <summary>Inhalt mit Pinch-to-Zoom (zwei Finger), Verschieben (ein Finger, nur vergrößert) und Doppeltippen (2,5× bzw. zurück).</summary>
public sealed class ZoomView : ContentView
{
    private const double MaxScale = 8;
    private double _scale = 1, _startScale = 1, _x, _y, _panX, _panY;

    public ZoomView()
    {
        IsClippedToBounds = true;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinch;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        var dbl = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        dbl.Tapped += (_, e) => OnDoubleTap(e.GetPosition(this));
        GestureRecognizers.Add(pinch); GestureRecognizers.Add(pan); GestureRecognizers.Add(dbl);
    }

    public bool IsZoomed => _scale > 1.01;

    private void Apply(double scale, double tx, double ty)
    {
        if (Content == null) return;
        _scale = Math.Clamp(scale, 1, MaxScale);
        Content.AnchorX = 0; Content.AnchorY = 0;
        Content.Scale = _scale;
        Content.TranslationX = _x = Math.Clamp(tx, -Content.Width * (_scale - 1), 0);
        Content.TranslationY = _y = Math.Clamp(ty, -Content.Height * (_scale - 1), 0);
    }

    public void Reset() => Apply(1, 0, 0);

    private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (Content == null || Content.Width <= 0) return;
        switch (e.Status)
        {
            case GestureStatus.Started:
                _startScale = _scale;
                break;
            case GestureStatus.Running:
                // Punkt unter den Fingern bleibt stehen: Bildkoordinate vor und nach der Skalierung gleich halten.
                double ox = e.ScaleOrigin.X * Width, oy = e.ScaleOrigin.Y * Height;
                double ns = Math.Clamp(_scale * e.Scale, 1, MaxScale);
                double cx = (ox - _x) / _scale, cy = (oy - _y) / _scale;
                Apply(ns, ox - cx * ns, oy - cy * ns);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_scale < 1.05) Reset();
                break;
        }
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (!IsZoomed) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started: _panX = _x; _panY = _y; break;
            case GestureStatus.Running: Apply(_scale, _panX + e.TotalX, _panY + e.TotalY); break;
        }
    }

    private void OnDoubleTap(Point? p)
    {
        if (IsZoomed) { Reset(); return; }
        double ns = 2.5, ox = p?.X ?? Width / 2, oy = p?.Y ?? Height / 2;
        Apply(ns, ox - ox * ns, oy - oy * ns);
    }
}
