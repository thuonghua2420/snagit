using SnapGit.Core.Models;

namespace SnapGit.Core.Settings;

/// <summary>
/// A named bundle of "how to capture": mode plus the options that normally live in the global settings.
/// Running a profile overrides those options for that one capture only.
/// </summary>
public sealed class CaptureProfile
{
    public string Name { get; set; } = string.Empty;
    public CaptureMode Mode { get; set; } = CaptureMode.Region;
    public int DelaySeconds { get; set; }

    /// <summary>"1280x720"; empty = free selection.</summary>
    public string FixedRegionSize { get; set; } = string.Empty;

    public bool IncludeCursor { get; set; }
    public bool CopyToClipboard { get; set; }
    public bool OpenEditor { get; set; } = true;
}

/// <summary>The effective options of one capture: the global settings, overridden by a profile when one is running.</summary>
public sealed record CaptureOptions(
    int DelaySeconds,
    Size? FixedRegion,
    bool IncludeCursor,
    bool CopyToClipboard,
    bool OpenEditor,
    bool UsePrintWindow)
{
    public static CaptureOptions Create(AppSettings settings, CaptureProfile? profile = null) =>
        profile is null
            ? new CaptureOptions(settings.CaptureDelaySeconds, settings.GetFixedRegionSize(), settings.IncludeCursor,
                settings.CopyToClipboardAfterCapture, settings.OpenEditorAfterCapture, settings.UsePrintWindowForWindows)
            : new CaptureOptions(Math.Max(0, profile.DelaySeconds),
                new AppSettings { FixedRegionSize = profile.FixedRegionSize }.GetFixedRegionSize(),
                profile.IncludeCursor, profile.CopyToClipboard, profile.OpenEditor, settings.UsePrintWindowForWindows);
}
