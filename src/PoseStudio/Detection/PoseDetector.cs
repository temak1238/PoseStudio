using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PoseStudio.Detection;

/// <summary>Ergebnis der Posenerkennung (MediaPipe BlazePose, 33 Punkte).</summary>
public sealed class DetectedPose
{
    /// <summary>Punkte im Bild (Pixel des Originalbilds).</summary>
    public required Point[] Image { get; init; }
    /// <summary>3D-Punkte in Metern (MediaPipe-Weltkoordinaten: x rechts, y unten, z von der Kamera weg; Ursprung Hüftmitte).</summary>
    public required Vector3D[] World { get; init; }
    /// <summary>Sichtbarkeit 0..1 je Punkt.</summary>
    public required double[] Visibility { get; init; }
    public double Confidence { get; init; }
    public double DetectionScore { get; init; }
}

/// <summary>Zwei ONNX-Modelle aus dem OpenCV Model Zoo (Apache 2.0): Personen-Detektor (224×224, SSD-Anker)
/// und Pose-Landmarken (256×256). Vor-/Nachbearbeitung nach opencv_zoo mp_persondet.py / mp_pose.py.</summary>
public sealed class PoseDetector : IDisposable
{
    public const string DetModel = "person_detection_mediapipe_2023mar.onnx";
    public const string PoseModel = "pose_estimation_mediapipe_2023mar.onnx";

    private readonly InferenceSession _det;
    private readonly InferenceSession _pose;
    private readonly float[] _anchors; // (x, y) je Anker, normiert
    public string ModelInfo { get; }

    public PoseDetector(string modelFolder)
    {
        var opt = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _det = new InferenceSession(Path.Combine(modelFolder, DetModel), opt);
        _pose = new InferenceSession(Path.Combine(modelFolder, PoseModel), opt);
        _anchors = BuildAnchors();
        string Describe(InferenceSession s) =>
            string.Join("; ", s.InputMetadata.Select(m => $"in {m.Key} [{string.Join(",", m.Value.Dimensions)}]")
                .Concat(s.OutputMetadata.Select(m => $"out {m.Key} [{string.Join(",", m.Value.Dimensions)}]")));
        ModelInfo = $"Detektor: {Describe(_det)}\nPose: {Describe(_pose)}";
    }

    /// <summary>MediaPipe-Weltpunkte (Kamera-System: x rechts, y unten, z von der Kamera weg) in die Szene übertragen –
    /// so, als wäre das Foto mit der aktuellen Kamera aufgenommen.</summary>
    public static Vector3D[] ToScene(DetectedPose pose, Rendering.OrbitCamera cam)
    {
        var right = cam.Right; var up = cam.Up; var look = cam.LookDirection;
        return pose.World.Select(v => right * v.X - up * v.Y + look * v.Z).ToArray();
    }

    /// <summary>Verbindungen für die Skelett-Anzeige (MediaPipe-Indizes).</summary>
    public static readonly (int A, int B)[] Bones =
    {
        (11, 12), (11, 13), (13, 15), (12, 14), (14, 16), (11, 23), (12, 24), (23, 24),
        (23, 25), (25, 27), (27, 29), (29, 31), (27, 31), (24, 26), (26, 28), (28, 30), (30, 32), (28, 32),
        (15, 19), (16, 20), (0, 7), (0, 8)
    };

    public void Dispose()
    {
        _det.Dispose();
        _pose.Dispose();
    }

    /// <summary>SSD-Anker des BlazePose-Detektors: Schritte 8, 16, 32 (3 Ebenen zusammengefasst), 2 bzw. 6 Anker je Zelle → 2254.</summary>
    private static float[] BuildAnchors()
    {
        var list = new List<float>();
        foreach (var (grid, perCell) in new[] { (28, 2), (14, 2), (7, 6) })
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                    for (int a = 0; a < perCell; a++)
                    {
                        list.Add((x + 0.5f) / grid);
                        list.Add((y + 0.5f) / grid);
                    }
        return list.ToArray();
    }

    /// <summary>Bild als RGB-Bytes (Zeilen, je Pixel 4 Byte BGRA).</summary>
    private sealed class Pixels
    {
        public int W, H;
        public byte[] Bgra = Array.Empty<byte>();

        public static Pixels From(BitmapSource src)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var p = new Pixels { W = conv.PixelWidth, H = conv.PixelHeight };
            p.Bgra = new byte[p.W * p.H * 4];
            conv.CopyPixels(p.Bgra, p.W * 4, 0);
            return p;
        }

        /// <summary>Bilinear abtasten; außerhalb schwarz. Rückgabe RGB 0..255.</summary>
        public (float r, float g, float b) Sample(double x, double y)
        {
            x -= 0.5; y -= 0.5;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            float r = 0, g = 0, b = 0;
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int xx = x0 + dx, yy = y0 + dy;
                    if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                    double w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                    int i = (yy * W + xx) * 4;
                    b += (float)(Bgra[i] * w); g += (float)(Bgra[i + 1] * w); r += (float)(Bgra[i + 2] * w);
                }
            return (r, g, b);
        }
    }

    public DetectedPose? Detect(BitmapSource image)
    {
        var px = Pixels.From(image);
        var person = DetectPerson(px);
        if (person == null) return null;
        return EstimatePose(px, person.Value.hip, person.Value.full, person.Value.score);
    }

    // ------------------------------------------------------------ Personen-Detektor

    private (Point hip, Point full, double score)? DetectPerson(Pixels px)
    {
        const int S = 224;
        double ratio = Math.Min((double)S / px.H, (double)S / px.W);
        int rw = (int)(px.W * ratio), rh = (int)(px.H * ratio);
        int padL = (S - rw) / 2, padT = (S - rh) / 2;
        var input = new DenseTensor<float>(new[] { 1, 3, S, S });
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                double sx = (x - padL + 0.5) / ratio, sy = (y - padT + 0.5) / ratio;
                if (x < padL || y < padT || x >= padL + rw || y >= padT + rh) continue; // Rand = 0 (nach Normierung)
                var (r, g, b) = px.Sample(sx, sy);
                input[0, 0, y, x] = (r / 255f - 0.5f) * 2;
                input[0, 1, y, x] = (g / 255f - 0.5f) * 2;
                input[0, 2, y, x] = (b / 255f - 0.5f) * 2;
            }
        using var results = _det.Run(new[] { NamedOnnxValue.CreateFromTensor(_det.InputMetadata.Keys.First(), input) });
        var tensors = results.Select(r => r.AsTensor<float>()).ToList();
        var reg = tensors.First(t => t.Dimensions[^1] >= 12);
        var sc = tensors.First(t => t.Dimensions[^1] == 1);
        int n = reg.Dimensions[1];

        int best = -1; double bestScore = 0;
        for (int i = 0; i < n; i++)
        {
            double s = 1 / (1 + Math.Exp(-Math.Clamp(sc[0, i, 0], -100, 100)));
            if (s > bestScore) { bestScore = s; best = i; }
        }
        if (best < 0 || bestScore < 0.5) return null;

        double scale = Math.Max(px.W, px.H);
        double padX = padL / ratio, padY = padT / ratio;
        Point Kp(int k) => new(
            (reg[0, best, 4 + 2 * k] / S + _anchors[2 * best]) * scale - padX,
            (reg[0, best, 5 + 2 * k] / S + _anchors[2 * best + 1]) * scale - padY);
        return (Kp(0), Kp(1), bestScore); // Hüftmitte, Ganzkörper-Punkt
    }

    // ------------------------------------------------------------ Pose-Landmarken

    private DetectedPose? EstimatePose(Pixels px, Point hip, Point full, double detScore)
    {
        const int S = 256;
        double dist = (full - hip).Length;
        double side = 2 * dist;                               // quadratischer Ausschnitt um die Hüftmitte
        double radians = Math.PI / 2 - Math.Atan2(-(full.Y - hip.Y), full.X - hip.X);
        radians -= 2 * Math.PI * Math.Floor((radians + Math.PI) / (2 * Math.PI));
        double ca = Math.Cos(radians), sa = Math.Sin(radians);

        // Ausschnitt drehen (Person aufrecht) und auf 256×256 abtasten
        var input = new DenseTensor<float>(new[] { 1, S, S, 3 });
        for (int v = 0; v < S; v++)
            for (int u = 0; u < S; u++)
            {
                double dx = (u + 0.5) / S * side - side / 2, dy = (v + 0.5) / S * side - side / 2;
                double sx = hip.X + ca * dx - sa * dy, sy = hip.Y + sa * dx + ca * dy;
                var (r, g, b) = px.Sample(sx, sy);
                input[0, v, u, 0] = r / 255f;
                input[0, v, u, 1] = g / 255f;
                input[0, v, u, 2] = b / 255f;
            }
        using var results = _pose.Run(new[] { NamedOnnxValue.CreateFromTensor(_pose.InputMetadata.Keys.First(), input) });
        var tensors = results.Select(r => r.AsTensor<float>()).ToList();
        var lm = tensors.First(t => t.Length == 195);
        var world = tensors.First(t => t.Length == 117);
        var conf = tensors.First(t => t.Length == 1);
        double c = conf.GetValue(0);
        if (c < 0.5) return null;

        var img = new Point[33]; var w3 = new Vector3D[33]; var vis = new double[33];
        double k = side / S;
        for (int i = 0; i < 33; i++)
        {
            double x = (lm.GetValue(i * 5) - S / 2.0) * k, y = (lm.GetValue(i * 5 + 1) - S / 2.0) * k;
            img[i] = new Point(hip.X + ca * x - sa * y, hip.Y + sa * x + ca * y);
            vis[i] = 1 / (1 + Math.Exp(-lm.GetValue(i * 5 + 3)));
            double wx = world.GetValue(i * 3), wy = world.GetValue(i * 3 + 1), wz = world.GetValue(i * 3 + 2);
            w3[i] = new Vector3D(ca * wx - sa * wy, sa * wx + ca * wy, wz);
        }
        return new DetectedPose { Image = img, World = w3, Visibility = vis, Confidence = c, DetectionScore = detScore };
    }
}
