using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoseStudio.Rendering;

/// <summary>Zeichnet das App-Icon (Figur in Jubelpose mit Lichtquelle und Schatten) und schreibt eine .ico-Datei
/// mit PNG-Einträgen in allen üblichen Größen. Aufruf: PoseStudio.exe --make-icon &lt;pfad.ico&gt;</summary>
internal static class IconMaker
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    public static void Write(string icoPath)
    {
        var pngs = Sizes.Select(s => (s, Png(Render(s)))).ToList();
        File.WriteAllBytes(Path.ChangeExtension(icoPath, ".png"), pngs[^1].Item2);

        using var fs = File.Create(icoPath);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        foreach (var (size, data) in pngs)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in pngs) w.Write(data);
    }

    private static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Zeichnung im 256er-Raster, skaliert auf die Zielgröße.</summary>
    private static BitmapSource Render(int size)
    {
        double k = size / 256.0;
        bool small = size <= 24;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));

            // Hintergrund: abgerundetes Quadrat mit Verlauf
            var bg = new LinearGradientBrush(Color.FromRgb(0x3A, 0x8D, 0xE8), Color.FromRgb(0x16, 0x45, 0x8F), new Point(0, 0), new Point(1, 1));
            double inset = small ? 4 : 8;
            dc.DrawRoundedRectangle(bg, null, new Rect(inset, inset, 256 - 2 * inset, 256 - 2 * inset), 52, 52);

            // Lichtquelle (Sonne) mit Strahl
            var sun = new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3A));
            dc.DrawEllipse(sun, null, new Point(200, 52), small ? 24 : 20, small ? 24 : 20);

            // Schatten am Boden
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new Point(132, 222), 62, 11);

            // Figur (Jubelpose)
            var white = Brushes.White;
            var limb = new Pen(white, small ? 26 : 21) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            void Path(params Point[] pts)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(pts[0], false, false);
                    c.PolyLineTo(pts.Skip(1).ToList(), true, true);
                }
                dc.DrawGeometry(null, limb, g);
            }
            Path(new Point(126, 96), new Point(122, 152));                         // Rumpf
            Path(new Point(120, 104), new Point(86, 80), new Point(70, 44));        // Arm hoch links
            Path(new Point(130, 104), new Point(168, 116), new Point(200, 100));    // Arm seitlich rechts
            Path(new Point(118, 150), new Point(100, 186), new Point(86, 218));     // Bein links
            Path(new Point(126, 150), new Point(152, 182), new Point(170, 216));    // Bein rechts
            dc.DrawEllipse(white, null, new Point(128, 66), small ? 25 : 22, small ? 25 : 22); // Kopf
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
