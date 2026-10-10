namespace SnapGit.Core.Library;

public interface ILibraryService
{
    string Folder { get; }

    /// <summary>Raised after items were added/removed/changed through this service.</summary>
    event EventHandler? Changed;

    /// <summary>Items matching <paramref name="query"/> (default: everything, newest first).</summary>
    IReadOnlyList<LibraryItem> GetItems(LibraryQuery? query = null);

    /// <summary>
    /// Saves a new capture into the library — as an editable ".snapgit" project or a plain image, depending on settings —
    /// named from the naming pattern. <paramref name="title"/> feeds the {title} token (e.g. the window title).
    /// </summary>
    LibraryItem Add(Bitmap image, string? title = null);

    /// <summary>Copies an existing file (image/GIF/video/project) into the library.</summary>
    LibraryItem Import(string sourcePath);

    /// <summary>Reserves a unique file path inside the library for something written by a recorder/exporter.</summary>
    string ReservePath(string extension, string? title = null);

    /// <summary>Notifies listeners that a library file was written/modified outside the service.</summary>
    void NotifyModified(string filePath);

    void Delete(LibraryItem item);

    LibraryItem Rename(LibraryItem item, string newName);

    void SetTags(LibraryItem item, IEnumerable<string> tags);

    void SetFavorite(LibraryItem item, bool favorite);

    /// <summary>Every tag in use, alphabetically.</summary>
    IReadOnlyList<string> AllTags();
}
