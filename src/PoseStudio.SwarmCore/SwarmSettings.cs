using System.Globalization;
using System.Text.Json.Nodes;

namespace PoseStudio.Swarm;

public sealed class SwarmLora
{
    public string Name { get; set; } = "";
    public double Weight { get; set; } = 0.8;
}

/// <summary>Einstellungen des SwarmUI-Fensters (zugleich Inhalt eines Profils).</summary>
public sealed class SwarmSettings
{
    public string Url { get; set; } = "http://192.168.188.2:7801";
    /// <summary>Aus einem SwarmUI-Bild übernommene Parameter (alles, was das Fenster nicht selbst einstellt, z. B. Segment/FaceRefine).</summary>
    public JsonObject? Base { get; set; }
    public string Model { get; set; } = "";
    public List<SwarmLora> Loras { get; set; } = new();
    public bool AutoTrigger { get; set; } = true;
    /// <summary>Historie: nur Prompt und Negativ-Prompt übernehmen.</summary>
    public bool HistoryPromptsOnly { get; set; }
    public string Prompt { get; set; } = "";
    public string Negative { get; set; } = "";
    /// <summary>Darstellung des Init Image: -1 = wie im Hauptfenster, 0 = Normal, 1 = Cel, 2 = 2D flach, 3 = Strichzeichnung.</summary>
    public int Look { get; set; } = -1;
    /// <summary>Szene als Init Image mitschicken (aus = nur Prompt, Pose ggf. über ControlNet).</summary>
    public bool UseInitImage { get; set; } = true;
    public double Creativity { get; set; } = 0.6;
    public int SideLength { get; set; } = 1280;
    public int Count { get; set; } = 2;
    public long Seed { get; set; } = -1;
    public int Steps { get; set; }
    public double Cfg { get; set; }
    public string Sampler { get; set; } = "";
    public string Scheduler { get; set; } = "";
    public string Vae { get; set; } = "";
    // ControlNet
    public bool UseControlNet { get; set; }
    public bool ControlRegions { get; set; }
    public string ControlModel { get; set; } = "";
    public double ControlStrength { get; set; } = 1.0;
    public double ControlStart { get; set; }
    public double ControlEnd { get; set; } = 1.0;
    // ControlNet mit eigenem Bild: SwarmUI erzeugt das Steuerbild per Preprocessor (Linien, Skizze, Tiefe, Pose)
    /// <summary>Pose Studio (PC): ControlNet-Bild aus einem Bild (Referenzfoto/Datei) statt aus der Szene; Art = ImageControlKind.</summary>
    public bool ControlFromImage { get; set; }
    public bool ImageUseControlNet { get; set; }
    public int ImageControlKind { get; set; }
    public string ImageControlModel { get; set; } = "";
    /// <summary>Tablet, Quelle Posen-Vorlage: Art bei ControlFromImage (Index in ImageControlKinds) und Darstellung des Steuerbilds (0 Normal … 3 Strich).</summary>
    public int TemplateControlKind { get; set; }
    public int ControlLook { get; set; } = 3;
    /// <summary>Preprocessor für diesen Auftrag (nur zur Laufzeit gesetzt); null = keiner (Pose-Studio-Renderbilder).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? ControlPreprocessor { get; set; }

    /// <summary>Arten für ControlNet mit eigenem Bild: Anzeige, SwarmUI-Preprocessor, Namensteile passender Modelle (Anima-LLLite).</summary>
    public static readonly (string Name, string Preprocessor, string[] ModelHints)[] ImageControlKinds =
    {
        ("Linien (Lineart)", "AnimeLineArtPreprocessor", new[] { "any-test", "lineart", "union", "promax" }),
        ("Skizze (Scribble)", "Scribble_PiDiNet_Preprocessor", new[] { "any-test", "scribble", "union", "promax" }),
        ("Tiefe (Depth)", "DepthAnythingV2Preprocessor", new[] { "depth", "union", "promax" }),
        ("Pose (DWPose, schwach)", "DWPreprocessor", new[] { "lllite-pose", "dwpose", "openpose", "union", "promax" }),
    };

    /// <summary>Passendes Modell zur Art aus der Liste der ControlNets (erstes nach Reihenfolge der Hinweise).</summary>
    public static string? SuggestImageControlModel(int kind, IEnumerable<string> models)
    {
        var list = models.ToList();
        foreach (var hint in ImageControlKinds[Math.Clamp(kind, 0, ImageControlKinds.Length - 1)].ModelHints)
            if (list.FirstOrDefault(m => m.Contains(hint, StringComparison.OrdinalIgnoreCase)) is { } m) return m;
        return null;
    }
    /// <summary>Zuletzt genutzte VAE und ControlNet-Modelle je Modellfamilie (SwarmModel.Family). Beim Wechsel z. B. von Anima auf
    /// Illustrious werden sie getauscht, damit keine VAE/kein ControlNet der falschen Familie mitgeschickt wird.</summary>
    public Dictionary<string, string> VaeByFamily { get; set; } = new();
    public Dictionary<string, string> ControlModelByFamily { get; set; } = new();
    public Dictionary<string, string> ImageControlModelByFamily { get; set; } = new();

    /// <summary>VAE „None“: die im Modell enthaltene VAE (Standard bei SDXL/Illustrious). „Automatic“ nimmt die SwarmUI-API nicht an.</summary>
    public const string AutoVae = "None";

    /// <summary>Modellfamilie gewechselt: Werte der alten Familie merken, die der neuen holen (neu: VAE None = im Modell enthalten, kein ControlNet).
    /// Gibt true zurück, wenn sich etwas geändert hat.</summary>
    public bool SwitchFamily(string oldFamily, string newFamily)
    {
        if (newFamily == "" || oldFamily == newFamily) return false;
        if (oldFamily != "")
        {
            VaeByFamily[oldFamily] = Vae;
            ControlModelByFamily[oldFamily] = ControlModel;
            ImageControlModelByFamily[oldFamily] = ImageControlModel;
        }
        Vae = VaeByFamily.GetValueOrDefault(newFamily) ?? AutoVae;
        ControlModel = ControlModelByFamily.GetValueOrDefault(newFamily) ?? "";
        ImageControlModel = ImageControlModelByFamily.GetValueOrDefault(newFamily) ?? "";
        return true;
    }

    // Nachbessern (Refiner)
    public double RefinerControl { get; set; } = 0.1;
    public double RefinerUpscale { get; set; } = 2.0;
    public string RefinerUpscaleMethod { get; set; } = "";
    public string RefinerMethod { get; set; } = "PostApply";
    public int RefinerSteps { get; set; }
    public double RefinerCfg { get; set; }

    public Dictionary<string, SwarmSettings> Profiles { get; set; } = new();
    public string? WindowPlacement { get; set; }

    /// <summary>Kopie ohne Server-Adresse und Profile (Inhalt eines Profils).</summary>
    public SwarmSettings CloneProfile()
    {
        var c = (SwarmSettings)MemberwiseClone();
        c.Base = Base?.DeepClone() as JsonObject;
        c.Loras = Loras.Select(l => new SwarmLora { Name = l.Name, Weight = l.Weight }).ToList();
        c.Profiles = new();
        c.VaeByFamily = new(VaeByFamily);
        c.ControlModelByFamily = new(ControlModelByFamily);
        c.ImageControlModelByFamily = new(ImageControlModelByFamily);
        return c;
    }

    /// <summary>Profil anwenden (Server-Adresse und Profilliste bleiben).</summary>
    public void ApplyProfile(SwarmSettings p)
    {
        var url = Url; var profiles = Profiles; var place = WindowPlacement;
        foreach (var prop in typeof(SwarmSettings).GetProperties().Where(x => x.CanWrite))
            prop.SetValue(this, prop.GetValue(p.CloneProfile()));
        Url = url; Profiles = profiles; WindowPlacement = place;
    }

    // Werte, die beim Übernehmen nicht in die Grundlage gehören: Bildquellen, Größen, Seeds, Refiner (nur für „Nachbessern“)
    private static readonly string[] NotInBase =
    {
        "initimage", "initimagecreativity", "initimage_filename", "initimage_resolution", "initimageresettonorm", "initimagenoise",
        "controlnetimageinput", "controlnetimageinput_filename", "controlnetimageinput_resolution", "controlnetmodel", "controlnetstrength",
        "controlnetstart", "controlnetend", "controlnetpreprocessor", "seed", "wildcardseed", "images", "width", "height", "aspectratio", "sidelength", "swarm_version",
        "prompt", "negativeprompt", "model", "loras", "loraweights", "steps", "cfgscale", "sampler", "scheduler", "vae", "variationseed",
        "variationseedstrength", "date", "generation_time", "prep_time", "original_prompt", "used_wildcards", "unused_parameters",
    };

    /// <summary>Einstellungen eines SwarmUI-Bildes übernehmen: sichtbare Felder setzen, den Rest als Grundlage merken.
    /// Refiner-Werte werden zur Vorgabe für „Nachbessern“.</summary>
    /// <summary>Nur Prompt und Negativ-Prompt aus einem Historienbild übernehmen, alles andere bleibt.</summary>
    public void ImportPromptsFrom(JsonObject p, string? originalPrompt)
    {
        Prompt = originalPrompt ?? p["prompt"]?.ToString() ?? Prompt;
        Negative = p["negativeprompt"]?.ToString() ?? "";
    }

    public void ImportFrom(JsonObject p, string? originalPrompt)
    {
        string? S(string k) => p[k]?.ToString();
        double? D(string k) => double.TryParse(S(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        Prompt = originalPrompt ?? S("prompt") ?? Prompt;
        Negative = S("negativeprompt") ?? "";
        Model = S("model") ?? Model;
        Steps = (int)(D("steps") ?? 0);
        Cfg = D("cfgscale") ?? 0;
        Sampler = S("sampler") ?? "";
        Scheduler = S("scheduler") ?? "";
        Vae = S("vae") ?? "";
        if (D("sidelength") is { } side) SideLength = (int)side;
        Loras.Clear();
        if (p["loras"] is JsonArray la)
        {
            var wa = p["loraweights"] as JsonArray;
            for (int i = 0; i < la.Count; i++)
                Loras.Add(new SwarmLora
                {
                    Name = la[i]?.ToString() ?? "",
                    Weight = wa != null && i < wa.Count && double.TryParse(wa[i]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1
                });
        }
        if (p.ContainsKey("refinerupscale") || p.ContainsKey("refinercontrolpercentage"))
        {
            RefinerControl = D("refinercontrolpercentage") ?? RefinerControl;
            RefinerUpscale = D("refinerupscale") ?? RefinerUpscale;
            RefinerUpscaleMethod = S("refinerupscalemethod") ?? RefinerUpscaleMethod;
            RefinerMethod = S("refinermethod") ?? RefinerMethod;
            RefinerSteps = (int)(D("refinersteps") ?? 0);
            RefinerCfg = D("refinercfgscale") ?? 0;
        }
        var b = new JsonObject();
        foreach (var (k, v) in p)
            if (!NotInBase.Contains(k) && !k.StartsWith("refiner")) b[k] = v?.DeepClone();
        Base = b;
    }

    /// <summary>Breite/Höhe für ein Seitenverhältnis bei gleicher Pixelzahl wie SideLength², auf 16 gerundet.</summary>
    public (int W, int H) SizeFor(double aspect)
    {
        double area = (double)SideLength * SideLength;
        int w = (int)Math.Round(Math.Sqrt(area * aspect) / 16) * 16, h = (int)Math.Round(Math.Sqrt(area / aspect) / 16) * 16;
        return (Math.Max(256, w), Math.Max(256, h));
    }

    /// <summary>Parameter für einen Generierungs-Auftrag.</summary>
    public JsonObject BuildParams(string initDataUrl, int w, int h, string? controlDataUrl, IReadOnlyDictionary<string, string?> triggers)
    {
        var p = Base?.DeepClone() as JsonObject ?? new JsonObject();
        string prompt = Prompt;
        if (AutoTrigger)
            foreach (var l in Loras)
                if (triggers.TryGetValue(l.Name, out var t) && !string.IsNullOrWhiteSpace(t) && !prompt.Contains(t!, StringComparison.OrdinalIgnoreCase))
                    prompt = prompt.TrimEnd().TrimEnd(',') + ", " + t;
        p["prompt"] = prompt;
        p["negativeprompt"] = Negative;
        if (!string.IsNullOrWhiteSpace(Model)) p["model"] = Model;
        p["images"] = Math.Clamp(Count, 1, 16);
        p["seed"] = Seed;
        if (Steps > 0) p["steps"] = Steps;
        if (Cfg > 0) p["cfgscale"] = Cfg;
        if (!string.IsNullOrWhiteSpace(Sampler)) p["sampler"] = Sampler;
        if (!string.IsNullOrWhiteSpace(Scheduler)) p["scheduler"] = Scheduler;
        if (!string.IsNullOrWhiteSpace(Vae) && !Vae.Equals("Automatic", StringComparison.OrdinalIgnoreCase)) p["vae"] = Vae;   // Automatic lehnt die API ab
        if (Loras.Count > 0)
        {
            p["loras"] = new JsonArray(Loras.Select(l => (JsonNode?)JsonValue.Create(l.Name)).ToArray());
            p["loraweights"] = new JsonArray(Loras.Select(l => (JsonNode?)JsonValue.Create(l.Weight.ToString("0.###", CultureInfo.InvariantCulture))).ToArray());
        }
        p["aspectratio"] = "Custom";
        p["width"] = w;
        p["height"] = h;
        if (UseInitImage)
        {
            p["initimage"] = initDataUrl;
            p["initimagecreativity"] = Math.Clamp(Creativity, 0, 1);
        }
        if (UseControlNet && controlDataUrl != null && !string.IsNullOrWhiteSpace(ControlModel))
        {
            p["controlnetimageinput"] = controlDataUrl;
            p["controlnetmodel"] = ControlModel;
            p["controlnetstrength"] = ControlStrength;
            p["controlnetstart"] = ControlStart;
            p["controlnetend"] = ControlEnd;
            // Renderbilder aus Pose Studio nicht vorverarbeiten: sonst macht SwarmUI daraus automatisch ein OpenPose-Skelett und anima_3dpose sieht die Pose nicht.
            // Eigene Bilder: SwarmUI erzeugt das Steuerbild mit dem gewählten Preprocessor.
            p["controlnetpreprocessor"] = string.IsNullOrWhiteSpace(ControlPreprocessor) ? "None" : ControlPreprocessor;
        }
        return p;
    }

    /// <summary>Nachbessern: dieselben Parameter (mit dem tatsächlichen Seed des Bildes) plus Refiner-Durchgang.</summary>
    public JsonObject BuildRefine(JsonObject sent, long seed)
    {
        var p = (JsonObject)sent.DeepClone();
        p["images"] = 1;
        p["seed"] = seed;
        p["refinercontrolpercentage"] = RefinerControl;
        p["refinerupscale"] = RefinerUpscale;
        p["refinermethod"] = string.IsNullOrWhiteSpace(RefinerMethod) ? "PostApply" : RefinerMethod;
        if (!string.IsNullOrWhiteSpace(RefinerUpscaleMethod)) p["refinerupscalemethod"] = RefinerUpscaleMethod;
        if (RefinerSteps > 0) p["refinersteps"] = RefinerSteps;
        if (RefinerCfg > 0) p["refinercfgscale"] = RefinerCfg;
        return p;
    }
}
