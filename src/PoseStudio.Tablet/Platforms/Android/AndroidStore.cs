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

    private record Doc(string Id, string Name, string Mime, long Modified);

    private static List<Doc> Children(AUri tree, string docId)
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

    private static bool IsDir(Doc d) => d.Mime == DocumentsContract.Document.MimeTypeDir;

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
