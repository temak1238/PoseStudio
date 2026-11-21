using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoseStudio.Swarm;

/// <summary>Was das SwarmUI-Fenster vom Hauptfenster braucht.</summary>
public interface ISwarmScene
{
    /// <summary>Seitenverhältnis der Vorschau (Breite/Höhe).</summary>
    double PreviewAspect { get; }
    /// <summary>Szene rendern; look = -1 wie im Hauptfenster, 0 Normal, 1 Cel, 2 2D flach, 3 Strichzeichnung.</summary>
    BitmapSource RenderInit(int w, int h, int look);
    BitmapSource RenderControl(int w, int h, bool regions);
    void UseAsReference(string path);
    /// <summary>Referenzfoto der aktiven Figur (Pfad) oder null.</summary>
    string? ReferenceImagePath { get; }
    /// <summary>Export-Ordner des Hauptfensters (SwarmUI-Ergebnisse landen im Unterordner „SwarmUI“).</summary>
    string ExportFolder { get; }
}

/// <summary>Ein Ergebnis (oder Platzhalter während der Generierung).</summary>
public sealed class SwarmResultItem : INotifyPropertyChanged
{
    private ImageSource? _image; private string _caption = ""; private bool _done;
    public ImageSource? Image { get => _image; set { _image = value; Changed(nameof(Image)); } }
    public string Caption { get => _caption; set { _caption = value; Changed(nameof(Caption)); } }
    public bool Done { get => _done; set { _done = value; Changed(nameof(Done)); } }
    public string? LocalPath { get; set; }
    public JsonObject? Sent { get; set; }
    public long Seed { get; set; } = -1;
    private string? _serverPath; private bool _starred;
    /// <summary>Pfad in SwarmUI (für den Stern); fehlt bei Ergebnissen vor 0.22.11.</summary>
    public string? ServerPath { get => _serverPath; set { _serverPath = value; Changed(nameof(CanStar)); } }
    public bool Starred { get => _starred; set { _starred = value; Changed(nameof(Starred)); Changed(nameof(StarText)); } }
    public bool CanStar => ServerPath != null;
    public string StarText => Starred ? "★ Stern" : "☆ Stern";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class SwarmWindow : Window
{
    private readonly SwarmSettings _s;
    private readonly ISwarmScene _scene;
    private SwarmClient? _client;
    private CancellationTokenSource? _cts;
    private List<SwarmModel> _models = new(), _loras = new();
    private readonly ObservableCollection<SwarmLora> _selectedLoras = new();
    private readonly ObservableCollection<SwarmResultItem> _results = new();
    private bool _suppress;
    private static readonly string[] LookNames = { "Wie im Hauptfenster", "Normal (3D)", "Cel-Shading", "2D flach", "Strichzeichnung" };
    /// <summary>Nur für Tests: anderer Ordner für Ergebnisse und Liste.</summary>
    internal static string? OutputOverride;
    /// <summary>Ordner für Bilder, Begleitdateien und Liste: Unterordner „SwarmUI“ im Export-Ordner (beim Öffnen festgelegt).</summary>
    public string OutputFolder { get; }
    public static string FolderFor(string? exportFolder) => Path.Combine(
        string.IsNullOrWhiteSpace(exportFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PoseStudio") : exportFolder.Trim(), "SwarmUI");
    private static string LegacyFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoseStudio", "SwarmOutput");

    /// <summary>Ergebnisse aus dem alten Ordner (%LOCALAPPDATA%\PoseStudio\SwarmOutput, bis 0.21.3) in den neuen verschieben –
    /// nur wenn der neue noch keine Liste hat. Vorhandene Dateien im Ziel werden nicht überschrieben.</summary>
    public static string? MigrateLegacy(string target)
    {
        try
        {
            if (!Directory.Exists(LegacyFolder) || File.Exists(Path.Combine(target, "results.json"))) return null;
            Directory.CreateDirectory(target);
            int moved = 0;
            foreach (var f in Directory.GetFiles(LegacyFolder))
            {
                string dst = Path.Combine(target, Path.GetFileName(f));
                if (File.Exists(dst)) continue;
                File.Move(f, dst); moved++;
            }
            if (Directory.GetFileSystemEntries(LegacyFolder).Length == 0) Directory.Delete(LegacyFolder);
            return moved > 0 ? $"{moved} Datei(en) aus dem alten Ergebnisordner nach {target} verschoben." : null;
        }
        catch (Exception ex) { return "Alte Ergebnisse konnten nicht verschoben werden: " + ex.Message; }
    }

    internal void ShowStatus(string text) => StatusText.Text = text;

    /// <summary>Nur für Tests: fertiges Ergebnis hinzufügen (wie nach einer Generierung).</summary>
    internal void AddResultForTest(string path, string caption, long seed, JsonObject sent)
    {
        var item = new SwarmResultItem { LocalPath = path, Caption = caption, Seed = seed, Sent = sent, ServerPath = "View/local/raw/test.png", Starred = true, Done = true, Image = ThumbCache.Decode(File.ReadAllBytes(path), 200) };
        _results.Insert(0, item); SaveResult(item); SaveResultIndex();
    }
    internal int ResultCount => _results.Count;
    internal (string Caption, long Seed, bool HasSent, string? Server, bool Starred) FirstResult => (_results[0].Caption, _results[0].Seed, _results[0].Sent != null, _results[0].ServerPath, _results[0].Starred);

    public SwarmWindow(SwarmSettings settings, ISwarmScene scene)
    {
        InitializeComponent();
        // nie größer als der Bildschirm (sonst liegt „Generieren“ außerhalb)
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(Width, wa.Width - 20); Height = Math.Min(Height, wa.Height - 20);
        Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _s = settings; _scene = scene;
        OutputFolder = OutputOverride ?? FolderFor(scene.ExportFolder);
        LookCombo.ItemsSource = LookNames;
        SideCombo.ItemsSource = new[] { "1024", "1152", "1280", "1408", "1536" };
        CnKindCombo.ItemsSource = new[] { "3D-Pose (anima_3dpose)", "Regionen (anima_regional)" }.Concat(SwarmSettings.ImageControlKinds.Select(k => "Bild: " + k.Name)).ToList();
        LoraSelected.ItemsSource = _selectedLoras;
        ResultList.ItemsSource = _results;
        LoadResults();
        ShowSettings();
        RefreshProfiles();
        Loaded += async (_, _) => await ConnectAsync();
        // erst nach ShowSettings anhängen (sonst feuert der Handler beim Füllen)
        CnKindCombo.SelectionChanged += CnKind_Changed;
        Activated += (_, _) => ShowCnImage();
        Closing += (_, _) => { ReadUi(); _cts?.Cancel(); };
    }

    // ------------------------------------------------------------------ Einstellungen ↔ Oberfläche

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static double PD(string s, double fallback) => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void ShowSettings()
    {
        _suppress = true;
        UrlBox.Text = _s.Url;
        PromptBox.Text = _s.Prompt;
        NegativeBox.Text = _s.Negative;
        AutoTriggerCheck.IsChecked = _s.AutoTrigger;
        PromptsOnlyCheck.IsChecked = _s.HistoryPromptsOnly;
        LookCombo.SelectedIndex = Math.Clamp(_s.Look + 1, 0, LookNames.Length - 1);
        InitCheck.IsChecked = _s.UseInitImage;
        CreativitySlider.Value = _s.Creativity;
        SideCombo.Text = _s.SideLength.ToString(CultureInfo.InvariantCulture);
        CountBox.Text = _s.Count.ToString(CultureInfo.InvariantCulture);
        SeedBox.Text = _s.Seed.ToString(CultureInfo.InvariantCulture);
        StepsBox.Text = _s.Steps.ToString(CultureInfo.InvariantCulture);
        CfgBox.Text = F(_s.Cfg);
        SamplerCombo.Text = _s.Sampler;
        SchedulerCombo.Text = _s.Scheduler;
        VaeCombo.Text = _s.Vae;
        CnCheck.IsChecked = _s.UseControlNet;
        CnKindCombo.SelectedIndex = _s.ControlFromImage ? 2 + Math.Clamp(_s.ImageControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1) : _s.ControlRegions ? 1 : 0;
        ShowCnImage();
        CnModelCombo.Text = _s.ControlModel;
        CnStrengthBox.Text = F(_s.ControlStrength); CnStartBox.Text = F(_s.ControlStart); CnEndBox.Text = F(_s.ControlEnd);
        RefControlBox.Text = F(_s.RefinerControl); RefUpscaleBox.Text = F(_s.RefinerUpscale);
        RefUpscaleMethodCombo.Text = _s.RefinerUpscaleMethod; RefMethodCombo.Text = _s.RefinerMethod;
        RefStepsBox.Text = _s.RefinerSteps.ToString(CultureInfo.InvariantCulture); RefCfgBox.Text = F(_s.RefinerCfg);
        _selectedLoras.Clear();
        foreach (var l in _s.Loras) _selectedLoras.Add(new SwarmLora { Name = l.Name, Weight = l.Weight });
        _suppress = false;
        ShowModel();
        UpdateSizeText();
        BaseText.Text = _s.Base is { Count: > 0 } b
            ? $"Übernommene weitere Einstellungen: {b.Count} ({string.Join(", ", b.Select(x => x.Key).Take(8))}{(b.Count > 8 ? " …" : "")})"
            : "Keine weiteren Einstellungen übernommen (SwarmUI-Standardwerte).";
    }

    private void ReadUi()
    {
        _s.Url = UrlBox.Text.Trim();
        _s.Prompt = PromptBox.Text;
        _s.Negative = NegativeBox.Text;
        _s.AutoTrigger = AutoTriggerCheck.IsChecked == true;
        _s.HistoryPromptsOnly = PromptsOnlyCheck.IsChecked == true;
        _s.Look = LookCombo.SelectedIndex - 1;
        _s.UseInitImage = InitCheck.IsChecked == true;
        _s.Creativity = CreativitySlider.Value;
        _s.SideLength = Math.Clamp((int)PD(SideCombo.Text, _s.SideLength), 256, 4096);
        _s.Count = Math.Clamp((int)PD(CountBox.Text, _s.Count), 1, 16);
        _s.Seed = long.TryParse(SeedBox.Text.Trim(), out var seed) ? seed : -1;
        _s.Steps = Math.Max(0, (int)PD(StepsBox.Text, 0));
        _s.Cfg = Math.Max(0, PD(CfgBox.Text, 0));
        _s.Sampler = SamplerCombo.Text.Trim(); _s.Scheduler = SchedulerCombo.Text.Trim(); _s.Vae = VaeCombo.Text.Trim();
        _s.UseControlNet = CnCheck.IsChecked == true;
        int cnKind = Math.Max(0, CnKindCombo.SelectedIndex);
        _s.ControlRegions = cnKind == 1;
        _s.ControlFromImage = cnKind >= 2;
        if (cnKind >= 2) _s.ImageControlKind = cnKind - 2;
        _s.ControlModel = CnModelCombo.Text.Trim();
        _s.ControlStrength = PD(CnStrengthBox.Text, 1); _s.ControlStart = PD(CnStartBox.Text, 0); _s.ControlEnd = PD(CnEndBox.Text, 1);
        _s.RefinerControl = PD(RefControlBox.Text, 0.1); _s.RefinerUpscale = PD(RefUpscaleBox.Text, 2);
        _s.RefinerUpscaleMethod = RefUpscaleMethodCombo.Text.Trim(); _s.RefinerMethod = RefMethodCombo.Text.Trim();
        _s.RefinerSteps = Math.Max(0, (int)PD(RefStepsBox.Text, 0)); _s.RefinerCfg = Math.Max(0, PD(RefCfgBox.Text, 0));
        _s.Loras = _selectedLoras.Select(l => new SwarmLora { Name = l.Name, Weight = l.Weight }).ToList();
    }

    private void UpdateSizeText()
    {
        int side = Math.Clamp((int)PD(SideCombo.Text, _s.SideLength), 256, 4096);
        var probe = new SwarmSettings { SideLength = side };
        var (w, h) = probe.SizeFor(_scene.PreviewAspect);
        SizeText.Text = $"→ {w} × {h} px";
    }

    private void Side_Changed(object sender, RoutedEventArgs e) { if (!_suppress) Dispatcher.BeginInvoke(UpdateSizeText); }

    private void ShowModel()
    {
        var m = _models.FirstOrDefault(x => x.Id.Equals(_s.Model, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(_s.Model, StringComparison.OrdinalIgnoreCase)
                                             || Path.GetFileNameWithoutExtension(x.Name).Equals(_s.Model, StringComparison.OrdinalIgnoreCase));
        ModelText.Text = string.IsNullOrWhiteSpace(_s.Model) ? "(Standardmodell von SwarmUI)" : m != null ? Path.GetFileNameWithoutExtension(m.Name) : _s.Model;
        ModelImage.Source = null;
        if (m?.PreviewUrl != null) _ = SetImageAsync(ModelImage, m.PreviewUrl);
    }

    private static async Task SetImageAsync(Image target, string url) => target.Source = await ThumbCache.LoadAsync(url, 112);

    // ------------------------------------------------------------------ Verbindung, Listen

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        ReadUi();
        _client?.Dispose();
        _client = new SwarmClient(_s.Url);
        ServerText.Text = "Verbinde …";
        try
        {
            string ver = await _client.ConnectAsync();
            _models = await _client.ListModelsAsync("Stable-Diffusion");
            _loras = await _client.ListModelsAsync("LoRA");
            var vaes = await _client.ListModelsAsync("VAE");
            var cns = await _client.ListModelsAsync("ControlNet");
            var values = await _client.ListParamValuesAsync(new[] { "sampler", "scheduler", "refinerupscalemethod", "refinermethod" });
            ServerText.Text = $"Verbunden mit SwarmUI {ver}: {_models.Count} Modelle, {_loras.Count} LoRAs.";
            KeepText(SamplerCombo, values.GetValueOrDefault("sampler"));
            KeepText(SchedulerCombo, values.GetValueOrDefault("scheduler"));
            KeepText(RefUpscaleMethodCombo, values.GetValueOrDefault("refinerupscalemethod"));
            KeepText(RefMethodCombo, values.GetValueOrDefault("refinermethod"));
            KeepText(VaeCombo, vaes.Select(v => v.Id).ToList());
            KeepText(CnModelCombo, cns.Select(v => v.Id).ToList());
            FillModelList();
            LoraFolderCombo.ItemsSource = new[] { "Alle Ordner" }.Concat(_loras.Select(l => l.Folder).Where(f => f != "").Distinct().OrderBy(f => f)).ToList();
            LoraFolderCombo.SelectedIndex = 0;
            FillLoraList();
            ShowModel();
            _ = SyncStarsAsync();
        }
        catch (Exception ex)
        {
            ServerText.Text = "Keine Verbindung: " + ex.Message;
        }
    }

    /// <summary>Auswahlliste setzen, den eingetragenen Text dabei behalten.</summary>
    private static void KeepText(ComboBox c, List<string>? items)
    {
        string t = c.Text;
        c.ItemsSource = items;
        c.Text = t;
    }

    private void FillModelList()
    {
        string q = ModelSearch.Text.Trim();
        ModelList.ItemsSource = _models.Where(m => q == "" || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(m => new ThumbItem { Key = m.Id, Label = Path.GetFileNameWithoutExtension(m.Name), ToolTip = m.Name + (string.IsNullOrWhiteSpace(m.Title) ? "" : Environment.NewLine + m.Title), Url = m.PreviewUrl, Tag = m }).ToList();
    }

    private void FillLoraList()
    {
        string q = LoraSearch.Text.Trim();
        string? folder = LoraFolderCombo.SelectedIndex > 0 ? LoraFolderCombo.SelectedItem as string : null;
        LoraList.ItemsSource = _loras.Where(l => (folder == null || l.Folder == folder)
                                                 && (q == "" || l.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || l.Title.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Select(l => new ThumbItem
            {
                Key = l.Id, Label = l.Short, Url = l.PreviewUrl, Tag = l,
                ToolTip = l.Name + (string.IsNullOrWhiteSpace(l.Trigger) ? "" : "\nTrigger: " + l.Trigger)
            }).ToList();
    }

    private void ModelPick_Click(object sender, RoutedEventArgs e) => ModelPicker.Visibility = ModelPickToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    private void LoraPick_Click(object sender, RoutedEventArgs e) => LoraPicker.Visibility = LoraPickToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    private void ModelSearch_Changed(object sender, TextChangedEventArgs e) => FillModelList();
    private void LoraFilter_Changed(object sender, EventArgs e) { if (IsLoaded) FillLoraList(); }

    private void ModelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelList.SelectedItem is not ThumbItem t) return;
        _s.Model = t.Key;
        ShowModel();
        ModelPickToggle.IsChecked = false;
        ModelPicker.Visibility = Visibility.Collapsed;
        ModelList.SelectedItem = null;
    }

    private void LoraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoraList.SelectedItem is not ThumbItem t) return;
        if (!_selectedLoras.Any(l => l.Name.Equals(t.Key, StringComparison.OrdinalIgnoreCase)))
            _selectedLoras.Add(new SwarmLora { Name = t.Key, Weight = 0.8 });
        LoraList.SelectedItem = null;
    }

    private void RemoveLora_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SwarmLora l) _selectedLoras.Remove(l);
    }

    private Dictionary<string, string?> LoraTriggers() =>
        _loras.GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Trigger, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Historie übernehmen

    private bool _historyLoaded;
    private async void HistoryExpander_Expanded(object sender, RoutedEventArgs e) { if (!_historyLoaded) await LoadHistoryAsync(); }
    private async void RefreshHistory_Click(object sender, RoutedEventArgs e) => await LoadHistoryAsync();

    private async Task LoadHistoryAsync()
    {
        if (_client == null) return;
        try
        {
            var list = await _client.ListHistoryAsync(300);
            HistoryList.ItemsSource = list.Where(h => h.Params != null).Select(h => new ThumbItem
            {
                Key = h.Src, Label = h.Params?["model"]?.ToString() ?? "", Url = h.PreviewUrl, Tag = h, DecodeWidth = 160,
                ToolTip = (h.OriginalPrompt ?? h.Params?["prompt"]?.ToString() ?? "") + "\n\nLoRAs: " + string.Join(", ", h.Params?["loras"] as JsonArray ?? new JsonArray())
            }).ToList();
            _historyLoaded = true;
        }
        catch (Exception ex) { StatusText.Text = "Historie nicht lesbar: " + ex.Message; }
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ThumbItem { Tag: SwarmHistoryImage h } || h.Params == null) return;
        ReadUi();
        bool promptsOnly = PromptsOnlyCheck.IsChecked == true;
        if (promptsOnly) _s.ImportPromptsFrom(h.Params, h.OriginalPrompt);
        else _s.ImportFrom(h.Params, h.OriginalPrompt);
        ShowSettings();
        StatusText.Text = (promptsOnly ? "Prompts übernommen aus " : "Einstellungen übernommen aus ") + Path.GetFileName(h.Src);
        HistoryList.SelectedItem = null;
    }

    // ------------------------------------------------------------------ Profile

    private void RefreshProfiles()
    {
        _suppress = true;
        ProfileCombo.ItemsSource = new[] { "–" }.Concat(_s.Profiles.Keys.OrderBy(k => k)).ToList();
        ProfileCombo.SelectedIndex = 0;
        _suppress = false;
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ProfileCombo.SelectedIndex <= 0 || ProfileCombo.SelectedItem is not string name || !_s.Profiles.TryGetValue(name, out var p)) return;
        _s.ApplyProfile(p);
        ShowSettings();
        StatusText.Text = $"Profil „{name}“ geladen.";
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        ReadUi();
        string current = ProfileCombo.SelectedIndex > 0 ? ProfileCombo.SelectedItem as string ?? "" : "";
        var name = Ask("Profil speichern", "Name des Profils:", current == "" ? "Mein Profil" : current);
        if (string.IsNullOrWhiteSpace(name)) return;
        _s.Profiles[name.Trim()] = _s.CloneProfile();
        RefreshProfiles();
        _suppress = true; ProfileCombo.SelectedItem = name.Trim(); _suppress = false;
        StatusText.Text = $"Profil „{name.Trim()}“ gespeichert.";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedIndex <= 0 || ProfileCombo.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"Profil „{name}“ löschen?", "Profil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _s.Profiles.Remove(name);
        RefreshProfiles();
    }

    private string? Ask(string title, string label, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 6, 0, 8), MinWidth = 280 };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var w = new Window
        {
            Title = title, Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(12), Children = { new TextBlock { Text = label }, box, ok } }
        };
        ok.Click += (_, _) => w.DialogResult = true;
        box.SelectAll(); box.Focus();
        return w.ShowDialog() == true ? box.Text : null;
    }

    // ------------------------------------------------------------------ Generieren

    private void RandomSeed_Click(object sender, RoutedEventArgs e) => SeedBox.Text = "-1";

    private static string ToDataUrl(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        ReadUi();
        if (_client == null || !_client.BaseUrl.Equals(new SwarmClient(_s.Url).BaseUrl, StringComparison.OrdinalIgnoreCase)) await ConnectAsync();
        if (_client == null) return;
        JsonObject p; string what;
        try
        {
            if (!_s.UseInitImage && !_s.UseControlNet) StatusText.Text = "Hinweis: weder Init Image noch ControlNet – die Pose aus Pose Studio wird nicht verwendet.";
            (p, what) = BuildGenerateParams();
        }
        catch (Exception ex) { StatusText.Text = "Vorbereiten fehlgeschlagen: " + ex.Message; return; }
        await RunAsync(p, _s.Count, what);
    }

    /// <summary>Auftrag aus den aktuellen Einstellungen: Init Image aus der Szene, ControlNet aus der Szene oder aus einem Bild (mit Preprocessor).</summary>
    private (JsonObject P, string What) BuildGenerateParams()
    {
        string? cnPath = null;
        byte[]? cnBytes = null;
        if (_s.UseControlNet && _s.ControlFromImage)
        {
            cnPath = CnImagePath() ?? throw new InvalidOperationException("Kein Steuerbild: im Hauptfenster ein Referenzfoto laden oder im ControlNet-Bereich „Bild wählen…“.");
            cnBytes = File.ReadAllBytes(cnPath);
        }
        // Ohne Init Image bestimmt das Steuerbild das Seitenverhältnis (sonst würde es verzerrt)
        double aspect = _scene.PreviewAspect;
        if (cnBytes != null && !_s.UseInitImage)
        {
            using var ms = new MemoryStream(cnBytes);
            var fr = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (fr.PixelWidth > 0 && fr.PixelHeight > 0) aspect = (double)fr.PixelWidth / fr.PixelHeight;
        }
        var (w, h) = _s.SizeFor(aspect);
        string init = _s.UseInitImage ? ToDataUrl(_scene.RenderInit(w, h, _s.Look)) : "";
        string? cn = null;
        var kind = SwarmSettings.ImageControlKinds[Math.Clamp(_s.ImageControlKind, 0, SwarmSettings.ImageControlKinds.Length - 1)];
        if (_s.UseControlNet)
            cn = cnBytes != null ? BytesToDataUrl(cnBytes) : ToDataUrl(_scene.RenderControl(w, h, _s.ControlRegions));
        _s.ControlPreprocessor = cnBytes != null ? kind.Preprocessor : null;
        try
        {
            var p = _s.BuildParams(init, w, h, cn, LoraTriggers());
            string cnText = !_s.UseControlNet ? "" : cnBytes != null ? $", {kind.Name.Split(' ')[0]} aus {Path.GetFileName(cnPath)} {F(_s.ControlStrength)}" : $", ControlNet {F(_s.ControlStrength)}";
            return (p, $"{w}×{h}, " + (_s.UseInitImage ? $"Creativity {F(_s.Creativity)}" : "ohne Init Image") + cnText);
        }
        finally { _s.ControlPreprocessor = null; }
    }

    private static string BytesToDataUrl(byte[] data) =>
        (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 ? "data:image/jpeg;base64," : data.Length > 3 && data[0] == 'R' && data[1] == 'I' ? "data:image/webp;base64," : "data:image/png;base64,")
        + Convert.ToBase64String(data);

    // ------------------------------------------------------------------ ControlNet aus einem Bild

    private string? _cnImagePath;   // null = Referenzfoto der aktiven Figur

    private string? CnImagePath() => _cnImagePath != null && File.Exists(_cnImagePath) ? _cnImagePath : _scene.ReferenceImagePath;

    private void ShowCnImage()
    {
        bool fromImage = CnKindCombo.SelectedIndex >= 2;
        CnImagePanel.Visibility = fromImage ? Visibility.Visible : Visibility.Collapsed;
        if (!fromImage) return;
        string? path = CnImagePath();
        CnImageText.Text = path == null ? "Steuerbild: keins – im Hauptfenster ein Referenzfoto laden oder „Bild wählen…“."
            : _cnImagePath != null && File.Exists(_cnImagePath) ? $"Steuerbild: {Path.GetFileName(path)}"
            : $"Steuerbild: Referenzfoto der aktiven Figur ({Path.GetFileName(path)})";
    }

    private void CnKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        int k = CnKindCombo.SelectedIndex;
        var models = (CnModelCombo.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
        // passendes Modell vorschlagen: Bild-Arten → LLLite nach Art, Szene → anima_3dpose
        string? suggest = k >= 2 ? SwarmSettings.SuggestImageControlModel(k - 2, models)
            : k == 0 ? models.FirstOrDefault(m => m.Contains("3dpose", StringComparison.OrdinalIgnoreCase)) : null;
        if (suggest != null) CnModelCombo.Text = suggest;
        ShowCnImage();
    }

    private void CnPickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Alle Dateien|*.*", Title = "Steuerbild für ControlNet" };
        if (dlg.ShowDialog(this) == true) { _cnImagePath = dlg.FileName; ShowCnImage(); }
    }

    private void CnUseReference_Click(object sender, RoutedEventArgs e) { _cnImagePath = null; ShowCnImage(); }

    /// <summary>Testmodus: Parameter für ControlNet aus einem Bild (ohne Server).</summary>
    internal string TestControlImage(string imagePath)
    {
        _cnImagePath = imagePath;
        bool cn = _s.UseControlNet, fromImg = _s.ControlFromImage, init = _s.UseInitImage; int kind = _s.ImageControlKind; string model = _s.ControlModel;
        try
        {
            _s.UseControlNet = true; _s.ControlFromImage = true; _s.UseInitImage = false; _s.ImageControlKind = 0; _s.ControlModel = "anima-lllite-any-test-like-v2.safetensors";
            var (p, what) = BuildGenerateParams();
            return $"Preprocessor {p["controlnetpreprocessor"]}, Modell {p["controlnetmodel"]}, Steuerbild {p.ContainsKey("controlnetimageinput")}, initimage {p.ContainsKey("initimage")}, Größe {p["width"]}×{p["height"]} | {what}";
        }
        finally { _s.UseControlNet = cn; _s.ControlFromImage = fromImg; _s.UseInitImage = init; _s.ImageControlKind = kind; _s.ControlModel = model; _cnImagePath = null; }
    }

    private async void Refine_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not SwarmResultItem { Sent: { } sent } item) return;
        ReadUi();
        if (_client == null) await ConnectAsync();
        if (_client == null) return;
        await RunAsync(_s.BuildRefine(sent, item.Seed), 1, $"Nachgebessert (Seed {item.Seed}, ×{F(_s.RefinerUpscale)}, Refiner {F(_s.RefinerControl * 100)} %)");
    }

    private async Task RunAsync(JsonObject p, int count, string what)
    {
        // Zufallsseed selbst würfeln: so ist der Seed jedes Bildes bekannt (Nachbessern braucht ihn)
        if ((p["seed"]?.GetValue<long>() ?? -1) < 0) p["seed"] = (long)Random.Shared.Next(1, int.MaxValue);
        long baseSeed = p["seed"]!.GetValue<long>();
        _cts = new CancellationTokenSource();
        GenerateButton.IsEnabled = false; CancelButton.IsEnabled = true;
        var pending = new List<SwarmResultItem>();
        for (int i = 0; i < count; i++) { var it = new SwarmResultItem { Caption = "Warte auf SwarmUI …" }; pending.Add(it); _results.Insert(i, it); }
        Progress.Value = 0;
        StatusText.Text = "Generiere: " + what;
        var sw = Stopwatch.StartNew();
        var progress = new Progress<SwarmProgress>(pr =>
        {
            Progress.Value = pr.Overall;
            if (pr.Status != null) StatusText.Text = pr.Status;
            if (pr.PreviewDataUrl != null && pending.FirstOrDefault(x => !x.Done) is { } first)
            {
                try { first.Image = ThumbCache.Decode(Convert.FromBase64String(pr.PreviewDataUrl[(pr.PreviewDataUrl.IndexOf(',') + 1)..]), 0); first.Caption = $"läuft … {pr.Overall:P0}"; }
                catch { }
            }
        });
        try
        {
            var res = await _client!.GenerateAsync(p, progress, _cts.Token);
            Directory.CreateDirectory(OutputFolder);
            for (int i = 0; i < res.Count; i++)
            {
                var item = i < pending.Count ? pending[i] : new SwarmResultItem();
                if (i >= pending.Count) _results.Insert(i, item);
                var bytes = await _client.DownloadAsync(res[i].Path);
                string ext = ResultStore.ExtensionOf(res[i].Path);
                item.LocalPath = Path.Combine(OutputFolder, $"swarm_{DateTime.Now:yyyyMMdd_HHmmss}_{i}{ext}");
                await File.WriteAllBytesAsync(item.LocalPath, bytes);
                item.Image = ThumbCache.Decode(bytes, 600);
                long? known = ResultStore.SeedFrom(res[i].Metadata);
                if (known is null or < 0) known = await _client.LookupSeedAsync(res[i].Path);
                item.Seed = known is >= 0 ? known.Value : baseSeed + i;
                item.Sent = p;
                item.Caption = what.Contains("Seed") ? what : $"{what} · Seed {item.Seed}";
                item.ServerPath = res[i].Path;
                item.Done = true;
                SaveResult(item);
            }
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            SaveResultIndex();
            StatusText.Text = $"Fertig: {res.Count} Bild(er) in {sw.Elapsed.TotalSeconds:F0} s – auch in der SwarmUI-Historie.";
        }
        catch (OperationCanceledException)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            StatusText.Text = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            foreach (var left in pending.Where(x => !x.Done)) _results.Remove(left);
            StatusText.Text = "Fehler: " + ex.Message;
        }
        finally
        {
            GenerateButton.IsEnabled = true; CancelButton.IsEnabled = false; Progress.Value = 0;
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_client != null) await _client.InterruptAsync();
        _cts?.Cancel();
    }

    // ------------------------------------------------------------------ Ergebnisse dauerhaft (Format gemeinsam mit der Tablet-App)

    private static void SaveResult(SwarmResultItem item)
    {
        if (item.LocalPath != null) ResultStore.Save(item.LocalPath, item.Caption, item.Seed, item.Sent, item.ServerPath, item.Starred);
    }

    /// <summary>Reihenfolge der Liste speichern (nur fertige Bilder).</summary>
    private void SaveResultIndex() =>
        ResultStore.SaveIndex(OutputFolder, _results.Where(r => r.Done && r.LocalPath != null).Select(r => r.LocalPath!));

    private void LoadResults()
    {
        foreach (var r in ResultStore.Load(OutputFolder))
        {
            try
            {
                _results.Add(new SwarmResultItem
                {
                    LocalPath = r.Path, Caption = r.Caption, Seed = r.Seed, Sent = r.Sent, ServerPath = r.ServerPath, Starred = r.Starred, Done = true,
                    Image = ThumbCache.Decode(File.ReadAllBytes(r.Path), 600)
                });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Ergebnis-Aktionen

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not SwarmResultItem { ServerPath: { } sp } item) { StatusText.Text = "Für dieses Bild ist der Pfad in SwarmUI unbekannt (vor 0.22.11 erzeugt)."; return; }
        if (_client == null) await ConnectAsync();
        if (_client == null) return;
        try
        {
            item.Starred = await _client.ToggleStarAsync(sp);
            SaveResult(item);
            StatusText.Text = item.Starred ? "Stern gesetzt – in SwarmUI unter „Starred“." : "Stern entfernt.";
        }
        catch (Exception ex) { StatusText.Text = "Stern nicht gesetzt: " + ex.Message; }
    }

    /// <summary>Sterne aus SwarmUI übernehmen (auch dort gesetzte oder entfernte).</summary>
    private async Task SyncStarsAsync()
    {
        if (_client == null) return;
        try
        {
            var starred = await _client.ListStarredAsync();
            foreach (var r in _results.Where(x => x.Done && x.ServerPath != null))
            {
                bool s = starred.Contains(_client.RelativePath(r.ServerPath!));
                if (s == r.Starred) continue;
                r.Starred = s;
                SaveResult(r);
            }
        }
        catch { }
    }

    private void ResultImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SwarmResultItem { LocalPath: { } path }) return;
        var img = new Image { Source = ThumbCache.Decode(File.ReadAllBytes(path), 0), Stretch = Stretch.Uniform };
        var w = new Window { Title = Path.GetFileName(path), Owner = this, Width = 1300, Height = 900, Background = Brushes.Black, Content = img, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        img.MouseLeftButtonUp += (_, _) => w.Close();
        w.Show();
    }

    private void UseSeed_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SwarmResultItem item) SeedBox.Text = item.Seed.ToString(CultureInfo.InvariantCulture);
    }

    private void UseAsReference_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SwarmResultItem { LocalPath: { } path }) { _scene.UseAsReference(path); StatusText.Text = "Als Referenzbild der aktiven Figur gesetzt."; }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SwarmResultItem { LocalPath: { } path } && File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void ClearResults_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;
        if (MessageBox.Show(this, $"Alle {_results.Count} Ergebnisse aus der Liste entfernen?\n\nDie Bilder bleiben in der SwarmUI-Historie und als lokale Kopie in\n{OutputFolder}.",
                "Liste leeren", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _results.Clear();
        SaveResultIndex();
    }
}
