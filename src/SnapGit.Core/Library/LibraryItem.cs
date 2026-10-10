namespace SnapGit.Core.Library;

public enum LibraryItemKind
{
    Image,
    Project,
    Animation,
    Video,
}

public sealed record LibraryItem(string FilePath, DateTime CreatedAt, long SizeBytes, LibraryItemKind Kind = LibraryItemKind.Image,
    IReadOnlyList<string>? TagList = null, bool IsFavorite = false)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public IReadOnlyList<string> Tags => TagList ?? [];

    public static LibraryItemKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        SnapProjectFile.Extension => LibraryItemKind.Project,
        ".gif" => LibraryItemKind.Animation,
        ".mp4" or ".avi" or ".wmv" or ".mkv" or ".webm" => LibraryItemKind.Video,
        _ => LibraryItemKind.Image,
    };

    public static bool IsSupported(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".mp4" or ".avi" or ".wmv" or ".mkv" or ".webm" or SnapProjectFile.Extension;
}

public enum LibrarySort
{
    Date,
    Name,
    Size,
    Kind,
}

/// <summary>Filter + ordering applied by <see cref="ILibraryService.GetItems"/>.</summary>
public sealed record LibraryQuery
{
    /// <summary>Case-insensitive match on the file name or any tag.</summary>
    public string? Text { get; init; }
    public LibraryItemKind? Kind { get; init; }
    public string? Tag { get; init; }
    public bool FavoritesOnly { get; init; }
    public LibrarySort Sort { get; init; } = LibrarySort.Date;
    public bool Descending { get; init; } = true;
}
