using SnapGit.Core.Diagnostics;
using SnapGit.Core.Library;

namespace SnapGit.App.Services;

/// <summary>
/// Crash protection for the editor: unsaved work is autosaved as hidden-from-library project copies and
/// offered back the next time the app starts. A clean save/close deletes the copy.
/// </summary>
public static class RecoveryService
{
    public static string Folder => Path.Combine(AppPaths.DataRoot, "recovery");

    public static string NewPath() => Path.Combine(Folder, Guid.NewGuid().ToString("N") + SnapProjectFile.Extension);

    /// <summary>Recovery files left behind by a crash (older than a few seconds, so a live autosave isn't picked up).</summary>
    public static IReadOnlyList<string> Pending()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, "*" + SnapProjectFile.Extension)
            .Where(f => DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromSeconds(5))
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public static void Discard(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* locked: will be offered again, harmless */ }
    }
}
