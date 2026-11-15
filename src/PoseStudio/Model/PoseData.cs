using System.IO;
using System.Text.Json;

namespace PoseStudio.Model;

public sealed class CameraData
{
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double Distance { get; set; }
    public double FocalLength { get; set; }
    public double[] Target { get; set; } = new double[3];
}

public sealed class PoseData
{
    public string App { get; set; } = "PoseStudio";
    public int Version { get; set; } = 1;
    public Dictionary<string, double[]> Joints { get; set; } = new();
    public double[] RootPosition { get; set; } = new double[3];
    public CameraData? Camera { get; set; }
    public FigureData? Figure { get; set; }
    public PoseStudio.Rendering.LightSettings? Light { get; set; }
    public PoseStudio.Rendering.SetSettings? Set { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static PoseData Load(string path) =>
        JsonSerializer.Deserialize<PoseData>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Die Pose-Datei ist leer oder ungültig.");
}
