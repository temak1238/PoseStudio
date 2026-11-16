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
    /// <summary>Ganzkörper-Erkennung mit Händen (RTMW3D), falls das Modell vorhanden ist.</summary>
    public WholeBodyPose? Whole { get; set; }
}

/// <summary>RTMW3D-Ergebnis: 133 Punkte nach COCO-WholeBody (0–16 Körper, 17–22 Füße, 23–90 Gesicht,
/// 91–111 linke Hand, 112–132 rechte Hand; links = linke Seite der Person).</summary>
public sealed class WholeBodyPose
{
    public required Point[] Image { get; init; }
    /// <summary>3D in Metern, Kamera-System wie MediaPipe-Welt (x rechts, y unten, z von der Kamera weg), Ursprung Hüftmitte.</summary>
    public required Vector3D[] World { get; init; }
    public required double[] Score { get; init; }
    /// <summary>Bildpixel je Meter (aus Knochenlängen geschätzt), Hüftmitte im Bild, Personenrahmen.</summary>
    public double PixelsPerMeter { get; init; }
    public Point HipImage { get; init; }
    public Rect Box { get; init; }
    public const int LeftHand = 91, RightHand = 112;

    /// <summary>Mittlere Sicherheit einer Hand (21 Punkte).</summary>
    public double HandScore(bool left) => Enumerable.Range(left ? LeftHand : RightHand, 21).Average(i => Score[i]);

    /// <summary>In die 33 MediaPipe-Punkte umsetzen (für die Körperanpassung).</summary>
    public Vector3D[] ToMediaPipe33()
    {
        var p = new Vector3D[33];
        var W = World;
        p[0] = W[0];
        for (int i = 1; i <= 6; i++) p[i] = W[i <= 3 ? 1 : 2];
        p[7] = W[3]; p[8] = W[4]; p[9] = p[10] = W[0];
        p[11] = W[5]; p[12] = W[6]; p[13] = W[7]; p[14] = W[8]; p[15] = W[9]; p[16] = W[10];
        p[17] = W[LeftHand + 17]; p[18] = W[RightHand + 17];
        p[19] = W[LeftHand + 5]; p[20] = W[RightHand + 5];
        p[21] = W[LeftHand + 1]; p[22] = W[RightHand + 1];
        p[23] = W[11]; p[24] = W[12]; p[25] = W[13]; p[26] = W[14]; p[27] = W[15]; p[28] = W[16];
        p[29] = W[19]; p[30] = W[22]; p[31] = W[17]; p[32] = W[20];
        return p;
    }
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
    public string ModelInfo { get; private set; }

    public const string WholeModel = "rtmw3d-x.onnx";
    private InferenceSession? _whole;
    private readonly string _folder;
    private readonly SessionOptions _opt;

    public PoseDetector(string modelFolder)
    {
        _folder = modelFolder;
        _opt = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _det = new InferenceSession(Path.Combine(modelFolder, DetModel), _opt);
        _pose = new InferenceSession(Path.Combine(modelFolder, PoseModel), _opt);
        _anchors = BuildAnchors();
        ModelInfo = $"Detektor: {Describe(_det)}\nPose: {Describe(_pose)}";
    }

    private static string Describe(InferenceSession s) =>
        string.Join("; ", s.InputMetadata.Select(m => $"in {m.Key} [{string.Join(",", m.Value.Dimensions)}]")
            .Concat(s.OutputMetadata.Select(m => $"out {m.Key} [{string.Join(",", m.Value.Dimensions)}]")));

    /// <summary>Ist das Ganzkörper-Modell (RTMW3D, mit Fingern) vorhanden?</summary>
    public bool HasWholeBody => File.Exists(Path.Combine(_folder, WholeModel));

    /// <summary>RTMW3D erst bei Bedarf laden (großes Modell).</summary>
    private InferenceSession? Whole()
    {
        if (_whole == null && HasWholeBody)
        {
            _whole = new InferenceSession(Path.Combine(_folder, WholeModel), _opt);
            ModelInfo += $"\nRTMW3D: {Describe(_whole)}";
        }
        return _whole;
    }

    /// <summary>MediaPipe-Weltpunkte (Kamera-System: x rechts, y unten, z von der Kamera weg) in die Szene übertragen –
    /// so, als wäre das Foto mit der aktuellen Kamera aufgenommen.</summary>
    public static Vector3D[] ToScene(DetectedPose pose, Rendering.OrbitCamera cam) => ToScene(pose.World, cam);

    public static Vector3D[] ToScene(IEnumerable<Vector3D> world, Rendering.OrbitCamera cam)
    {
        var right = cam.Right; var up = cam.Up; var look = cam.LookDirection;
        return world.Select(v => right * v.X - up * v.Y + look * v.Z).ToArray();
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
        _whole?.Dispose();
        _yolox?.Dispose();
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
        var pose = person == null ? null : EstimatePose(px, person.Value.hip, person.Value.full, person.Value.score);
        if (Whole() == null) return pose;
        Rect? box = null;
        if (Boxes == BoxSource.Yolox && Yolox() != null)
        {
            var people = DetectPeople(px);
            if (people.Count > 0) box = people[0].Box;
        }
        else if (pose != null) box = BoxFromLandmarks(pose);
        if (box == null) return pose;
        var wb = EstimateWhole(px, box.Value);
        if (wb == null) return pose;
        pose ??= FromWhole(wb);
        pose.Whole = wb;
        return pose;
    }

    /// <summary>Alle Personen im Bild (YOLOX-Rahmen + RTMW3D je Person), größte zuerst. Leer ohne die Modelle.</summary>
    public List<WholeBodyPose> DetectAll(BitmapSource image)
    {
        var list = new List<WholeBodyPose>();
        if (Whole() == null || Yolox() == null) return list;
        var px = Pixels.From(image);
        foreach (var (box, _) in DetectPeople(px))
            if (EstimateWhole(px, box) is { } wb) list.Add(wb);
        return list;
    }

    public enum BoxSource { MediaPipe, Yolox }
    /// <summary>Woher der Personenrahmen für RTMW3D kommt.</summary>
    public static BoxSource Boxes { get; set; } = BoxSource.Yolox;

    private static Rect BoxFromLandmarks(DetectedPose pose)
    {
        // Rahmen um die erkannten Punkte, etwas größer (Kopf oben, Fingerspitzen)
        double x0 = pose.Image.Min(p => p.X), x1 = pose.Image.Max(p => p.X), y0 = pose.Image.Min(p => p.Y), y1 = pose.Image.Max(p => p.Y);
        double mx = (x1 - x0) * 0.08 + 8, my = (y1 - y0) * 0.08 + 8;
        return new Rect(new Point(x0 - mx, y0 - my), new Point(x1 + mx, y1 + my));
    }

    /// <summary>MediaPipe fand niemanden: Ergebnis aus RTMW3D in die 33-Punkte-Form bringen.</summary>
    private static DetectedPose FromWhole(WholeBodyPose wb)
    {
        int[] map = { 0, 1, 1, 1, 2, 2, 2, 3, 4, 0, 0, 5, 6, 7, 8, 9, 10, 108, 129, 96, 117, 92, 113, 11, 12, 13, 14, 15, 16, 19, 22, 17, 20 };
        return new DetectedPose
        {
            Image = map.Select(i => wb.Image[i]).ToArray(),
            World = wb.ToMediaPipe33(),
            Visibility = map.Select(i => Math.Clamp(wb.Score[i], 0, 1)).ToArray(),
            Confidence = Enumerable.Range(0, 17).Average(i => Math.Clamp(wb.Score[i], 0, 1)),
        };
    }

    // ------------------------------------------------------------ YOLOX (Personen-Rahmen, mehrere Personen)

    public const string YoloxModel = "yolox-m-humanart.onnx";
    private InferenceSession? _yolox;
    public bool HasYolox => File.Exists(Path.Combine(_folder, YoloxModel));

    private InferenceSession? Yolox()
    {
        if (_yolox == null && HasYolox)
        {
            _yolox = new InferenceSession(Path.Combine(_folder, YoloxModel), _opt);
            ModelInfo += $"\nYOLOX: {Describe(_yolox)}";
        }
        return _yolox;
    }

    /// <summary>mmdeploy-Export (end2end.onnx, NMS enthalten): 640×640, Seitenverhältnis erhalten, rechts/unten mit 114 aufgefüllt,
    /// BGR 0..255 ohne Normierung. Ausgabe dets [1, N, 5] = x1, y1, x2, y2, Wert.</summary>
    private List<(Rect Box, double Score)> DetectPeople(Pixels px)
    {
        const int S = 640;
        double ratio = Math.Min((double)S / px.H, (double)S / px.W);
        int rw = (int)(px.W * ratio), rh = (int)(px.H * ratio);
        var input = new DenseTensor<float>(new[] { 1, 3, S, S });
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                if (x >= rw || y >= rh) { input[0, 0, y, x] = input[0, 1, y, x] = input[0, 2, y, x] = 114; continue; }
                var (r, g, b) = px.Sample((x + 0.5) / ratio, (y + 0.5) / ratio);
                input[0, 0, y, x] = b; input[0, 1, y, x] = g; input[0, 2, y, x] = r;
            }
        var s = _yolox!;
        using var results = s.Run(new[] { NamedOnnxValue.CreateFromTensor(s.InputMetadata.Keys.First(), input) });
        var dets = results.Where(r => r.ElementType == TensorElementType.Float).Select(r => r.AsTensor<float>()).First(t => t.Dimensions[^1] == 5);
        var list = new List<(Rect, double)>();
        for (int i = 0; i < dets.Dimensions[1]; i++)
        {
            double sc = dets[0, i, 4];
            if (sc < 0.3) continue;
            var box = new Rect(new Point(dets[0, i, 0] / ratio, dets[0, i, 1] / ratio), new Point(dets[0, i, 2] / ratio, dets[0, i, 3] / ratio));
            if (box.Width < 8 || box.Height < 8) continue;
            list.Add((box, sc));
        }
        return list.OrderByDescending(b => b.Item1.Width * b.Item1.Height).ToList();
    }

    // ------------------------------------------------------------ RTMW3D (Ganzkörper mit Händen, 3D)

    /// <summary>Kalibrierung (Testmodus --rtmw3d): Vorzeichen der Tiefe, Farbreihenfolge.</summary>
    public static double ZSign { get; set; } = 1;
    public static bool FeedBgr { get; set; }

    /// <summary>Nach rtmlib RTMPose3d: Rahmen ×1,25 auf Seitenverhältnis 288:384, normieren (ImageNet-Mittel), SimCC-Auswertung.
    /// x/y in Pixeln des Ausschnitts, z = (Bin/192 − 1) · 2,1745 m relativ zur Hüfte.</summary>
    private (Point[] Img, Vector3D[] Inp, double[] Score, double K)? RunWhole(Pixels px, Rect box)
    {
        const int IW = 288, IH = 384;
        double cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
        double bw = box.Width * 1.25, bh = box.Height * 1.25;
        if (bw > bh * IW / IH) bh = bw * IH / IW; else bw = bh * IW / IH;
        double k = bw / IW; // Originalpixel je Eingabepixel
        float[] mean = { 123.675f, 116.28f, 103.53f }, std = { 58.395f, 57.12f, 57.375f };
        var input = new DenseTensor<float>(new[] { 1, 3, IH, IW });
        for (int v = 0; v < IH; v++)
            for (int u = 0; u < IW; u++)
            {
                double sx = cx + (u - IW / 2.0) * k + 0.5, sy = cy + (v - IH / 2.0) * k + 0.5;
                var (r, g, b) = px.Sample(sx, sy);
                if (FeedBgr) (r, b) = (b, r);
                input[0, 0, v, u] = (r - mean[0]) / std[0];
                input[0, 1, v, u] = (g - mean[1]) / std[1];
                input[0, 2, v, u] = (b - mean[2]) / std[2];
            }
        var s = _whole!;
        using var results = s.Run(new[] { NamedOnnxValue.CreateFromTensor(s.InputMetadata.Keys.First(), input) });
        var outs = results.Select(r => r.AsTensor<float>()).ToList();
        if (outs.Count < 3) return null;
        var tx = outs[0]; var ty = outs[1]; var tz = outs[2];
        int K = tx.Dimensions[1];

        (double loc, double val) Peak(Tensor<float> t, int kp)
        {
            int n = t.Dimensions[2];
            int best = 0; float bv = float.MinValue;
            for (int i = 0; i < n; i++) { float v = t[0, kp, i]; if (v > bv) { bv = v; best = i; } }
            // Feinlage: gewichtetes Mittel um das Maximum (Werte ≥ 0)
            double sw = 0, sp = 0;
            for (int i = Math.Max(0, best - 2); i <= Math.Min(n - 1, best + 2); i++)
            {
                double w = Math.Max(0, t[0, kp, i]);
                sw += w; sp += w * i;
            }
            return (sw > 0 ? sp / sw : best, bv);
        }

        var img = new Point[K]; var inp = new Vector3D[K]; var score = new double[K];
        for (int kp = 0; kp < K; kp++)
        {
            var (xl, xv) = Peak(tx, kp); var (yl, yv) = Peak(ty, kp); var (zl, _) = Peak(tz, kp);
            double x = xl / 2, y = yl / 2, z = (zl / 2 / (IW / 2.0) - 1) * 2.1744869; // mmpose: input_size[2] = 288 (rtmlib teilt fälschlich durch 384/2)
            score[kp] = Math.Min(xv, yv);
            inp[kp] = new Vector3D(x, y, z * ZSign);
            img[kp] = new Point(cx + (x - IW / 2.0) * k, cy + (y - IH / 2.0) * k);
        }

        return (img, inp, score, k);
    }

    private WholeBodyPose? EstimateWhole(Pixels px, Rect box)
    {
        if (RunWhole(px, box) is not { } r) return null;
        var (img, inp, score, k) = r;
        // Maßstab Bildpixel ↔ Meter aus Knochenlängen (Bildlänge + Tiefenunterschied ergeben die echte Länge)
        var bones = new (int a, int b, double len)[] { (5, 7, 0.29), (6, 8, 0.29), (7, 9, 0.26), (8, 10, 0.26), (11, 13, 0.44), (12, 14, 0.44),
                                                        (13, 15, 0.42), (14, 16, 0.42), (5, 11, 0.50), (6, 12, 0.50), (5, 6, 0.36), (11, 12, 0.21) };
        var est = new List<double>();
        foreach (var (a, b, len) in bones)
        {
            if (score[a] < 0.3 || score[b] < 0.3) continue;
            double d2 = (img[a] - img[b]).Length;
            double dz = inp[a].Z - inp[b].Z;
            est.Add(d2 / Math.Sqrt(Math.Max(len * len - dz * dz, 0.09 * len * len)));
        }
        if (est.Count == 0) return null;
        est.Sort();
        double ppm = est[est.Count / 2] * PixelScale;
        var hipImg = new Point((img[11].X + img[12].X) / 2, (img[11].Y + img[12].Y) / 2);
        double hipZ = (inp[11].Z + inp[12].Z) / 2;
        var world = new Vector3D[img.Length];
        for (int i = 0; i < img.Length; i++)
            world[i] = new Vector3D((img[i].X - hipImg.X) / ppm, (img[i].Y - hipImg.Y) / ppm, inp[i].Z - hipZ);
        var wb = new WholeBodyPose { Image = img, World = world, Score = score, PixelsPerMeter = ppm, HipImage = hipImg, Box = box };
        return wb;
    }


    /// <summary>Korrekturfaktor für den Pixel-Meter-Maßstab (Kalibrierung).</summary>
    public static double PixelScale { get; set; } = 1;

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
