namespace SnapGit.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Save"/> persisted new values.</summary>
    event EventHandler? Changed;

    void Load();
    void Save();
}
