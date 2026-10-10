namespace SnapGit.Core.Diagnostics;

/// <summary>Well-known on-disk locations. Override <see cref="DataRoot"/> in tests.</summary>
public static class AppPaths
{
    /// <summary>Folder for settings, logs, library index and recovery files. Override with the SNAPGIT_DATA environment variable (used by the e2e scripts so they never touch real user data).</summary>
    public static string DataRoot { get; set; } =
        Environment.GetEnvironmentVariable("SNAPGIT_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapGit");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string LogFolder => Path.Combine(DataRoot, "logs");

    public static string DefaultLibraryFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapGit");
}
