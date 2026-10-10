using System.Text.Json;
using SnapGit.Core.Diagnostics;

namespace SnapGit.Core.Library;

/// <summary>Per-file metadata that can't live in the image file (tags, favourite flag). Stored as JSON next to the settings.</summary>
public sealed class LibraryIndex
{
    private sealed class Entry
    {
        public List<string> Tags { get; set; } = new();
        public bool Favorite { get; set; }
    }

    private readonly string _path;
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public LibraryIndex(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "library-index.json");
        Load();
    }

    public IReadOnlyList<string> TagsOf(string fileName) =>
        _entries.TryGetValue(fileName, out var entry) ? entry.Tags : [];

    public bool IsFavorite(string fileName) => _entries.TryGetValue(fileName, out var entry) && entry.Favorite;

    public IReadOnlyList<string> AllTags() =>
        _entries.Values.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

    public void SetTags(string fileName, IEnumerable<string> tags)
    {
        var cleaned = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Update(fileName, e => e.Tags = cleaned);
    }

    public void SetFavorite(string fileName, bool favorite) => Update(fileName, e => e.Favorite = favorite);

    public void Rename(string oldName, string newName)
    {
        if (!_entries.Remove(oldName, out var entry)) return;
        _entries[newName] = entry;
        Save();
    }

    public void Remove(string fileName)
    {
        if (_entries.Remove(fileName)) Save();
    }

    private void Update(string fileName, Action<Entry> change)
    {
        if (!_entries.TryGetValue(fileName, out var entry)) _entries[fileName] = entry = new Entry();
        change(entry);
        if (entry.Tags.Count == 0 && !entry.Favorite) _entries.Remove(fileName); // keep the index small
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path));
            if (loaded is not null) _entries = new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read library index: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write library index: {ex.Message}");
        }
    }
}
