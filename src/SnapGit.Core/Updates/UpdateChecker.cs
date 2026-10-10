using System.Text.Json;

namespace SnapGit.Core.Updates;

/// <summary>What the update feed says: <c>{ "version": "1.2.0", "url": "https://...", "notes": "..." }</c>.</summary>
public sealed record UpdateInfo(Version Version, string Url, string Notes);

/// <summary>
/// Checks a JSON feed (URL from settings; empty = disabled) for a newer version. No auto-install: the user
/// is told and opens the download page. Parsing/comparison are pure and unit-tested; only <see cref="CheckAsync"/> does I/O.
/// </summary>
public static class UpdateChecker
{
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var versionElement)) return null;
            if (!Version.TryParse(versionElement.GetString()?.TrimStart('v', 'V'), out var version)) return null;

            var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            return new UpdateInfo(version, url, notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The feed entry when it is newer than <paramref name="current"/>, otherwise null.</summary>
    public static UpdateInfo? NewerThan(Version current, string feedJson)
    {
        var info = Parse(feedJson);
        return info is not null && info.Version > current ? info : null;
    }

    public static async Task<UpdateInfo?> CheckAsync(string feedUrl, Version current, HttpClient? client = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;

        var owned = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var json = await client.GetStringAsync(uri, cancellation);
            return NewerThan(current, json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline or feed down: silently no update
        }
        finally
        {
            if (owned) client.Dispose();
        }
    }
}
