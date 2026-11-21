using Android.Content;
using Android.Graphics;
using Android.Provider;
using PoseStudio.Swarm;
using AUri = Android.Net.Uri;

namespace PoseStudio.Tablet;

/// <summary>Posen-Vorlage aus dem synchronisierten Ordner: Dateiname → Dokument-Adresse.</summary>
public sealed record TemplateInfo(string Name, string Display, Dictionary<string, string> Files, long Modified)
{
    public string? Preview => Files.GetValueOrDefault(TabletTemplate.PreviewFile(Files.Keys));
}

/// <summary>Android-Zugriffe: Vorlagen-Ordner (Storage Access Framework), Ergebnisordner, Galerie.</summary>
public static class AndroidStore
{
    private static Context Ctx => Android.App.Application.Context;
    private static ContentResolver CR => Ctx.ContentResolver!;

    /// <summary>Ordner der App für Ergebnisse (ohne Berechtigung; per USB unter Android/data/… sichtbar).</summary>
    public static string ResultsFolder
    {
        get
        {
            var dir = System.IO.Path.Combine(Ctx.GetExternalFilesDir(null)?.AbsolutePath ?? FileSystem.AppDataDirectory, "SwarmUI");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<string?> PickFolderAsync() => (await MainActivity.PickTreeAsync())?.ToString();

    /// <summary>Lesbarer Name des gewählten Ordners (z. B. „primary:Sync/PoseStudio“ → „Sync/PoseStudio“).</summary>
    public static string FolderLabel(string tree)
    {
        try
        {
            var id = DocumentsContract.GetTreeDocumentId(AUri.Parse(tree)) ?? tree;
            return id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
        }
        catch { return tree; }
    }

    internal record Doc(string Id, string Name, string Mime, long Modified);

    internal static List<Doc> Children(AUri tree, string docId)
    {
        var list = new List<Doc>();
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, docId)!;
        string[] cols = { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName,
                          DocumentsContract.Document.ColumnMimeType, DocumentsContract.Document.ColumnLastModified };
        using var c = CR.Query(uri, cols, null, null, null);
        while (c != null && c.MoveToNext())
            list.Add(new Doc(c.GetString(0) ?? "", c.GetString(1) ?? "", c.GetString(2) ?? "", c.IsNull(3) ? 0 : c.GetLong(3)));
        return list;
    }

    internal static bool IsDir(Doc d) => d.Mime == DocumentsContract.Document.MimeTypeDir;

    /// <summary>Vorlagen im gewählten Ordner. Gewählt werden darf der Export-Ordner selbst oder sein Unterordner „Tablet“.</summary>
    public static List<TemplateInfo> ListTemplates(string tree)
    {
        var t = AUri.Parse(tree)!;
        string rootId = DocumentsContract.GetTreeDocumentId(t)!;
        var top = Children(t, rootId);
        if (top.FirstOrDefault(d => IsDir(d) && d.Name.Equals(TabletTemplate.FolderName, StringComparison.OrdinalIgnoreCase)) is { } tab)
            top = Children(t, tab.Id);
        var result = new List<TemplateInfo>();
        foreach (var dir in top.Where(IsDir))
        {
            var files = Children(t, dir.Id).Where(d => !IsDir(d))
                .ToDictionary(d => d.Name.ToLowerInvariant(), d => DocumentsContract.BuildDocumentUriUsingTree(t, d.Id)!.ToString()!);
            if (!files.ContainsKey(TabletTemplate.PoseFile) && !TabletTemplate.InitFiles.Any(files.ContainsKey)) continue;
            result.Add(new TemplateInfo(dir.Name, TabletTemplate.DisplayName(dir.Name), files, dir.Modified));
        }
        return result.OrderByDescending(x => x.Name.Length > 15 ? x.Name[^15..] : "").ThenByDescending(x => x.Modified).ToList();
    }

    public static byte[] Read(string uri)
    {
        using var s = CR.OpenInputStream(AUri.Parse(uri)!) ?? throw new IOException("Datei nicht lesbar: " + uri);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static Stream Open(string uri) => CR.OpenInputStream(AUri.Parse(uri)!) ?? throw new IOException("Datei nicht lesbar: " + uri);

    public static (int W, int H) ImageSize(byte[] data)
    {
        var o = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, o);
        return (o.OutWidth, o.OutHeight);
    }

    /// <summary>Bild in die Galerie (Bilder/PoseStudio) kopieren.</summary>
    public static void SaveToGallery(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        string mime = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/png";
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var v = new ContentValues();
            v.Put(MediaStore.IMediaColumns.DisplayName, name);
            v.Put(MediaStore.IMediaColumns.MimeType, mime);
            v.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/PoseStudio");
            var uri = CR.Insert(MediaStore.Images.Media.ExternalContentUri!, v) ?? throw new IOException("Galerie lehnt das Bild ab.");
            using var o = CR.OpenOutputStream(uri) ?? throw new IOException("Galerie nicht beschreibbar.");
            using var i = File.OpenRead(path);
            i.CopyTo(o);
        }
        else
        {
#pragma warning disable CA1422, CS0618
            var dir = System.IO.Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryPictures)!.AbsolutePath, "PoseStudio");
#pragma warning restore CA1422, CS0618
            Directory.CreateDirectory(dir);
            File.Copy(path, System.IO.Path.Combine(dir, name), true);
        }
    }
}

/// <summary>Ergebnisse zusätzlich in einem frei gewählten (z. B. per Syncthing synchronisierten) Ordner ablegen.
/// Die App arbeitet weiter mit lokalen Kopien; der Ordner ist die dauerhafte Ablage im gleichen Format wie Pose Studio.</summary>
public static class ResultMirror
{
    /// <summary>Eigener Unterordner, damit Tablet und PC nie in dieselbe Ergebnisliste schreiben.</summary>
    public const string SubFolder = "SwarmUI-Tablet";
    public const string SettingsFile = "einstellungen.json";

    private static ContentResolver CR => Android.App.Application.Context.ContentResolver!;

    /// <summary>Besteht die (dauerhafte) Schreibberechtigung für den Ordner noch? Nach einer Neuinstallation nicht.</summary>
    public static bool HasAccess(string tree)
    {
        try { return CR.PersistedUriPermissions.Any(p => p.Uri?.ToString() == tree && p.IsWritePermission); }
        catch { return false; }
    }

    private sealed record Dir(AUri Tree, string Id, Dictionary<string, string> Files);

    private static Dir Open(string tree)
    {
        var t = AUri.Parse(tree)!;
        string root = DocumentsContract.GetTreeDocumentId(t)!;
        string id;
        if (AndroidStore.FolderLabel(tree).Split('/').Last().Equals(SubFolder, StringComparison.OrdinalIgnoreCase)) id = root;
        else if (AndroidStore.Children(t, root).FirstOrDefault(d => AndroidStore.IsDir(d) && d.Name.Equals(SubFolder, StringComparison.OrdinalIgnoreCase)) is { } sub) id = sub.Id;
        else
        {
            var created = DocumentsContract.CreateDocument(CR, DocumentsContract.BuildDocumentUriUsingTree(t, root)!, DocumentsContract.Document.MimeTypeDir, SubFolder)
                ?? throw new IOException($"Unterordner „{SubFolder}“ lässt sich nicht anlegen.");
            id = DocumentsContract.GetDocumentId(created)!;
        }
        var files = AndroidStore.Children(t, id).Where(d => !AndroidStore.IsDir(d)).GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First().Id);
        return new Dir(t, id, files);
    }

    private static string Mime(string name) =>
        name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg"
        : name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "application/octet-stream"; // octet-stream: Name bleibt unverändert

    private static void Write(Dir d, string name, byte[] data)
    {
        AUri uri;
        if (d.Files.TryGetValue(name, out var id)) uri = DocumentsContract.BuildDocumentUriUsingTree(d.Tree, id)!;
        else
        {
            uri = DocumentsContract.CreateDocument(CR, DocumentsContract.BuildDocumentUriUsingTree(d.Tree, d.Id)!, Mime(name), name)
                ?? throw new IOException($"„{name}“ lässt sich im Ergebnis-Ordner nicht anlegen.");
            d.Files[name] = DocumentsContract.GetDocumentId(uri)!;
        }
        Stream? o;
        try { o = CR.OpenOutputStream(uri, "wt"); } catch (Java.Lang.Exception) { o = null; }
        o ??= CR.OpenOutputStream(uri, "w") ?? throw new IOException($"„{name}“ ist nicht beschreibbar.");
        using (o) o.Write(data);
    }

    private static byte[]? Read(Dir d, string name) =>
        d.Files.TryGetValue(name, out var id) ? AndroidStore.Read(DocumentsContract.BuildDocumentUriUsingTree(d.Tree, id)!.ToString()!) : null;

    private static List<string> Names(byte[]? index)
    {
        try
        {
            if (index != null && System.Text.Json.Nodes.JsonNode.Parse(index) is System.Text.Json.Nodes.JsonArray arr)
                return arr.Select(n => n?.ToString() ?? "").Where(n => n != "").ToList();
        }
        catch { }
        return new();
    }

    /// <summary>Lokale Liste und Ordner abgleichen. merge = true (Start, Ordnerwahl): Vereinigung beider Listen, fehlende Dateien
    /// in beide Richtungen kopieren. merge = false (nach Generieren/Entfernen): die lokale Liste gilt, neue Bilder hochladen.
    /// Dateien im Ordner werden nie gelöscht. Liefert die Dateinamen der Liste (neueste zuerst).</summary>
    public static List<string> Sync(string tree, string localFolder, IReadOnlyList<string> localNames, bool merge)
    {
        var d = Open(tree);
        var names = localNames.ToList();
        if (merge)
            foreach (var n in Names(Read(d, ResultStore.IndexName)))
                if (!names.Contains(n) && d.Files.ContainsKey(n)) names.Add(n);
        foreach (var n in names)
        {
            string local = System.IO.Path.Combine(localFolder, n);
            if (File.Exists(local))
            {
                if (!d.Files.ContainsKey(n)) Write(d, n, File.ReadAllBytes(local));
                if (!d.Files.ContainsKey(n + ".json") && File.Exists(local + ".json")) Write(d, n + ".json", File.ReadAllBytes(local + ".json"));
            }
            else if (d.Files.ContainsKey(n))
            {
                File.WriteAllBytes(local, Read(d, n)!);
                if (Read(d, n + ".json") is { } side) File.WriteAllBytes(local + ".json", side);
            }
        }
        names.RemoveAll(n => !File.Exists(System.IO.Path.Combine(localFolder, n)));
        var arr = new System.Text.Json.Nodes.JsonArray(names.Select(n => (System.Text.Json.Nodes.JsonNode?)n).ToArray());
        Write(d, ResultStore.IndexName, System.Text.Encoding.UTF8.GetBytes(arr.ToJsonString()));
        return names;
    }

    public static void WriteSettings(string tree, string json) => Write(Open(tree), SettingsFile, System.Text.Encoding.UTF8.GetBytes(json));

    public static string? ReadSettings(string tree) => Read(Open(tree), SettingsFile) is { } b ? System.Text.Encoding.UTF8.GetString(b) : null;
}
