using SnapGit.Core.Imaging;
using SnapGit.Core.Settings;

namespace SnapGit.Core.Library;

/// <summary>Library backed by a plain folder. The folder always follows the current settings.</summary>
public sealed class FileLibraryService : ILibraryService
{
    private readonly ISettingsService _settings;
    private readonly LibraryIndex _index;

    public FileLibraryService(ISettingsService settings, LibraryIndex? index = null)
    {
        _settings = settings;
        _index = index ?? new LibraryIndex();
    }

    public string Folder => _settings.Current.LibraryFolder;

    public event EventHandler? Changed;

    public IReadOnlyList<LibraryItem> GetItems(LibraryQuery? query = null)
    {
        if (!Directory.Exists(Folder)) return [];
        query ??= new LibraryQuery();

        IEnumerable<LibraryItem> items = new DirectoryInfo(Folder)
            .EnumerateFiles()
            .Where(f => LibraryItem.IsSupported(f.Name))
            .Select(f => new LibraryItem(f.FullName, f.CreationTime, f.Length, LibraryItem.KindOf(f.Name), _index.TagsOf(f.Name), _index.IsFavorite(f.Name)));

        if (query.Kind is { } kind) items = items.Where(i => i.Kind == kind);
        if (query.FavoritesOnly) items = items.Where(i => i.IsFavorite);
        if (!string.IsNullOrWhiteSpace(query.Tag)) items = items.Where(i => i.Tags.Contains(query.Tag, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            items = items.Where(i => i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                     || i.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)));
        }

        Func<LibraryItem, object> key = query.Sort switch
        {
            LibrarySort.Name => i => i.Name.ToLowerInvariant(),
            LibrarySort.Size => i => i.SizeBytes,
            LibrarySort.Kind => i => i.Kind,
            _ => i => i.CreatedAt,
        };
        var ordered = query.Descending ? items.OrderByDescending(key) : items.OrderBy(key);
        return ordered.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public LibraryItem Add(Bitmap image, string? title = null)
    {
        var settings = _settings.Current;
        Directory.CreateDirectory(Folder);

        string path;
        if (settings.SaveCapturesAsProject)
        {
            path = ReservePath(SnapProjectFile.Extension, title);
            SnapProjectFile.Write(path, image, SnapProjectFile.EmptyDocumentJson);
        }
        else
        {
            path = ReservePath(ImageEncoder.GetExtension(settings.DefaultFormat), title);
            ImageEncoder.Save(image, path, settings.DefaultFormat, settings.JpegQuality);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return ToItem(path);
    }

    public LibraryItem Import(string sourcePath)
    {
        Directory.CreateDirectory(Folder);
        var extension = Path.GetExtension(sourcePath);
        var path = ReservePath(extension, Path.GetFileNameWithoutExtension(sourcePath));
        File.Copy(sourcePath, path, overwrite: true);

        Changed?.Invoke(this, EventArgs.Empty);
        return ToItem(path);
    }

    public string ReservePath(string extension, string? title = null)
    {
        Directory.CreateDirectory(Folder);
        var existing = Directory.EnumerateFiles(Folder).Count();
        var name = NamePattern.Format(_settings.Current.NamePattern, DateTime.Now, title, existing + 1);

        var path = Path.Combine(Folder, name + extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(Folder, $"{name} ({i}){extension}");
        return path;
    }

    public void NotifyModified(string filePath) => Changed?.Invoke(this, EventArgs.Empty);

    public void Delete(LibraryItem item)
    {
        if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
        _index.Remove(Path.GetFileName(item.FilePath));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public LibraryItem Rename(LibraryItem item, string newName)
    {
        var cleaned = NamePattern.Sanitize(newName);
        var target = Path.Combine(Path.GetDirectoryName(item.FilePath)!, cleaned + Path.GetExtension(item.FilePath));
        if (!string.Equals(target, item.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(target)) throw new IOException($"A file named \"{Path.GetFileName(target)}\" already exists.");
            File.Move(item.FilePath, target);
            _index.Rename(Path.GetFileName(item.FilePath), Path.GetFileName(target));
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return ToItem(target);
    }

    public void SetTags(LibraryItem item, IEnumerable<string> tags)
    {
        _index.SetTags(Path.GetFileName(item.FilePath), tags);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFavorite(LibraryItem item, bool favorite)
    {
        _index.SetFavorite(Path.GetFileName(item.FilePath), favorite);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> AllTags() => _index.AllTags();

    private LibraryItem ToItem(string path)
    {
        var info = new FileInfo(path);
        var name = info.Name;
        return new LibraryItem(info.FullName, info.CreationTime, info.Length, LibraryItem.KindOf(name), _index.TagsOf(name), _index.IsFavorite(name));
    }
}
