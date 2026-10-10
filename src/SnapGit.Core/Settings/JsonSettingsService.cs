using System.Text.Json;
using System.Text.Json.Serialization;
using SnapGit.Core.Diagnostics;

namespace SnapGit.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonSettingsService(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings, using defaults.", ex);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Write-then-replace so a crash never leaves a half-written file.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
        File.Move(tmp, _path, overwrite: true);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
