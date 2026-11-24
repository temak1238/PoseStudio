namespace PoseStudio.Swarm;

/// <summary>Rauschen fürs Init Image, getrennt für Hintergrund (über dem Horizont) und Boden (darunter).</summary>
/// <param name="Background">Deckkraft 0…1 über dem Horizont.</param>
/// <param name="BackgroundGrain">Körnung in Pixeln (1 = Pixelrauschen, größer = weiche Flecken).</param>
/// <param name="Floor">Deckkraft 0…1 unter dem Horizont.</param>
/// <param name="FloorGrain">Körnung unter dem Horizont.</param>
/// <param name="Mono">Schwarz-Weiß statt bunt.</param>
public sealed record NoiseSettings(double Background, int BackgroundGrain, double Floor, int FloorGrain, bool Mono)
{
    public bool Active => Background > 0 || Floor > 0;
}

/// <summary>Mischt Rauschen in ein Bild (rein rechnerisch, für PC und Tablet gleich). Figuren bleiben über eine
/// Alpha-Maske frei; der Übergang zwischen Hintergrund und Boden liegt auf der Horizontlinie.</summary>
public static class NoiseMixer
{
    /// <summary>px: 4 Byte je Pixel, Farbkanäle an 0…2 (BGRA oder RGBA – egal), Kanal 3 bleibt unberührt.
    /// figure: Deckung der Figuren je Pixel (0 = frei, 255 = Figur) oder null. horizon: Zeile des Horizonts in Pixeln (darf außerhalb liegen).</summary>
    public static void Apply(byte[] px, int w, int h, byte[]? figure, double horizon, NoiseSettings s, int? seed = null)
    {
        if (!s.Active) return;
        var rnd = seed is int sd ? new Random(sd) : new Random();
        var bg = new Field(w, h, Math.Max(1, s.BackgroundGrain), s.Mono, rnd);
        var fl = new Field(w, h, Math.Max(1, s.FloorGrain), s.Mono, rnd);
        double band = Math.Max(2, h * 0.015);   // weicher Übergang am Horizont
        double ob = Math.Clamp(s.Background, 0, 1), of = Math.Clamp(s.Floor, 0, 1);
        for (int y = 0; y < h; y++)
        {
            double wf = Math.Clamp((y - horizon) / band + 0.5, 0, 1);   // Anteil Boden
            double op = ob * (1 - wf) + of * wf;
            if (op <= 0) continue;
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                double k = op * (figure == null ? 1 : 1 - figure[y * w + x] / 255.0);
                if (k <= 0) continue;
                for (int c = 0; c < 3; c++)
                {
                    double n = wf <= 0 ? bg.Get(x, y, c) : wf >= 1 ? fl.Get(x, y, c) : bg.Get(x, y, c) * (1 - wf) + fl.Get(x, y, c) * wf;
                    px[i + c] = (byte)Math.Clamp(Math.Round(px[i + c] + (n - px[i + c]) * k), 0, 255);
                }
            }
        }
    }

    /// <summary>Zeile des Horizonts für eine Kamera ohne Rollen: Neigung der Blickrichtung (Grad, negativ = nach unten)
    /// und vertikaler Bildwinkel (Grad).</summary>
    public static double HorizonRow(int height, double lookElevationDeg, double verticalFovDeg)
    {
        double e = lookElevationDeg * Math.PI / 180, half = verticalFovDeg * Math.PI / 360;
        return height / 2.0 * (1 + Math.Tan(e) / Math.Tan(half));
    }

    /// <summary>Zufallswerte auf einem groben Gitter, bilinear auf Pixel interpoliert (Körnung 1 = jedes Pixel eigener Wert).</summary>
    private sealed class Field
    {
        private readonly byte[] _v; private readonly int _gw, _gh, _g; private readonly bool _mono;
        public Field(int w, int h, int grain, bool mono, Random rnd)
        {
            _g = grain; _mono = mono;
            _gw = w / grain + 2; _gh = h / grain + 2;
            _v = new byte[_gw * _gh * 3];
            rnd.NextBytes(_v);
        }
        private byte At(int gx, int gy, int c) => _v[(gy * _gw + gx) * 3 + (_mono ? 0 : c)];
        public double Get(int x, int y, int c)
        {
            if (_g == 1) return At(x, y, c);
            double fx = (x + 0.5) / _g, fy = (y + 0.5) / _g;
            int x0 = (int)fx, y0 = (int)fy; double tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);   // weiche Flecken statt Rautenmuster
            double a = At(x0, y0, c) + (At(x0 + 1, y0, c) - At(x0, y0, c)) * tx;
            double b = At(x0, y0 + 1, c) + (At(x0 + 1, y0 + 1, c) - At(x0, y0 + 1, c)) * tx;
            return a + (b - a) * ty;
        }
    }
}
