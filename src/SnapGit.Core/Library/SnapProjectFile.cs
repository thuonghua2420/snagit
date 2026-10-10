using System.IO.Compression;
using SnapGit.Core.Imaging;

namespace SnapGit.Core.Library;

public sealed record SnapProjectContents(Bitmap Image, string DocumentJson);

/// <summary>
/// The ".snapgit" container: a ZIP holding <c>image.png</c> (untouched base image), <c>document.json</c>
/// (annotations, written by the editor — opaque to Core) and <c>preview.png</c> (flattened thumbnail for the library).
/// </summary>
public static class SnapProjectFile
{
    public const string Extension = ".snapgit";
    public const string EmptyDocumentJson = "{\"version\":1,\"annotations\":[]}";

    private const string ImageEntry = "image.png";
    private const string DocumentEntry = "document.json";
    private const string PreviewEntry = "preview.png";
    private const int PreviewMaxSize = 640;

    public static bool IsProject(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <param name="preview">Flattened result (base + annotations); stored down-scaled. Pass null to reuse <paramref name="image"/>.</param>
    public static void Write(string path, Bitmap image, string documentJson, Bitmap? preview = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Write to a temp file first so a crash never corrupts an existing project.
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            WriteBitmap(zip, ImageEntry, image, CompressionLevel.Fastest);

            var entry = zip.CreateEntry(DocumentEntry, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
                writer.Write(documentJson);

            using var thumb = ImageOps.ScaleToFit(preview ?? image, PreviewMaxSize, PreviewMaxSize);
            WriteBitmap(zip, PreviewEntry, thumb, CompressionLevel.Fastest);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static SnapProjectContents Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var imageEntry = zip.GetEntry(ImageEntry) ?? throw new InvalidDataException("Not a SnapGit project: image.png is missing.");
        var image = ReadBitmap(imageEntry);

        var documentEntry = zip.GetEntry(DocumentEntry);
        var json = EmptyDocumentJson;
        if (documentEntry is not null)
        {
            using var reader = new StreamReader(documentEntry.Open());
            json = reader.ReadToEnd();
        }
        return new SnapProjectContents(image, json);
    }

    /// <summary>The flattened thumbnail, or the base image when the project has no preview.</summary>
    public static Bitmap ReadPreview(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(PreviewEntry) ?? zip.GetEntry(ImageEntry) ?? throw new InvalidDataException("Empty project.");
        return ReadBitmap(entry);
    }

    private static void WriteBitmap(ZipArchive zip, string name, Bitmap bitmap, CompressionLevel level)
    {
        var entry = zip.CreateEntry(name, level);
        using var stream = entry.Open();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static Bitmap ReadBitmap(ZipArchiveEntry entry)
    {
        using var buffer = new MemoryStream();
        using (var stream = entry.Open()) stream.CopyTo(buffer);
        buffer.Position = 0;
        using var loaded = new Bitmap(buffer);
        return new Bitmap(loaded); // detach from the stream
    }
}
