using SnapGit.Core.Diagnostics;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;

namespace SnapGit.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>User settings persisted as JSON. Keep every property settable with a sane default (forward/backward compatible).</summary>
public sealed class AppSettings
{
    // ---- hotkeys
    /// <summary>Gestures stored as text, e.g. "Ctrl+Shift+S", so the JSON stays human-editable.</summary>
    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = DefaultHotkeys();

    // ---- library & files
    public string LibraryFolder { get; set; } = AppPaths.DefaultLibraryFolder;

    /// <summary>true = captures are saved as editable ".snapgit" projects; false = plain image files.</summary>
    public bool SaveCapturesAsProject { get; set; } = true;

    /// <summary>Format used when captures are saved as plain images and for "export" defaults.</summary>
    public ImageFileFormat DefaultFormat { get; set; } = ImageFileFormat.Png;

    public int JpegQuality { get; set; } = 90;

    /// <summary>See <see cref="Library.NamePattern"/>.</summary>
    public string NamePattern { get; set; } = Library.NamePattern.Default;

    // ---- capture behaviour
    public bool CopyToClipboardAfterCapture { get; set; }
    public bool OpenEditorAfterCapture { get; set; } = true;
    public bool IncludeCursor { get; set; }

    /// <summary>Countdown before the screen is grabbed (lets you open a menu first). 0 = immediate.</summary>
    public int CaptureDelaySeconds { get; set; }

    /// <summary>"1280x720" style fixed-size region; empty = free selection.</summary>
    public string FixedRegionSize { get; set; } = string.Empty;

    /// <summary>Capture windows with PrintWindow (works for covered windows) instead of cropping the screen.</summary>
    public bool UsePrintWindowForWindows { get; set; }

    // ---- scrolling capture
    public ScrollDirection ScrollDirection { get; set; } = ScrollDirection.Vertical;
    public int ScrollDelayMs { get; set; } = 400;
    public int ScrollMaxFrames { get; set; } = 80;

    // ---- OCR
    /// <summary>BCP-47 tag such as "vi" or "en-US"; empty = the user's profile languages.</summary>
    public string OcrLanguage { get; set; } = string.Empty;

    // ---- recording
    public int GifFramesPerSecond { get; set; } = 10;
    public int VideoFramesPerSecond { get; set; } = 30;

    /// <summary>Full HD by default: sharp, standard 1920 × 1080 frames.</summary>
    public Recording.VideoResolution VideoResolution { get; set; } = Recording.VideoResolution.FullHd;

    public Recording.VideoQuality VideoQuality { get; set; } = Recording.VideoQuality.High;
    public bool RecordCursor { get; set; } = true;
    public bool RecordSystemAudio { get; set; }
    public bool RecordMicrophone { get; set; }
    public int GifMaxSeconds { get; set; } = 30;

    // ---- app
    public bool StartWithWindows { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>"auto", "en" or "vi".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>URL of a JSON update feed (see UpdateChecker). Empty = update checks disabled.</summary>
    public string UpdateFeedUrl { get; set; } = string.Empty;

    public List<ShareDestination> ShareDestinations { get; set; } = new();

    /// <summary>User-defined capture presets (tray and library "Profiles" menu).</summary>
    public List<CaptureProfile> Profiles { get; set; } = new();

    public static Dictionary<HotkeyAction, string> DefaultHotkeys() => new()
    {
        // Not PrintScreen on purpose: Windows 11's Snipping Tool (and tools like Greenshot) already claim it.
        // Users can rebind to PrintScreen in Settings once those are disabled.
        [HotkeyAction.CaptureRegion] = "Ctrl+Alt+1",
        [HotkeyAction.CaptureWindow] = "Ctrl+Alt+2",
        [HotkeyAction.CaptureFullScreen] = "Ctrl+Alt+3",
        [HotkeyAction.OpenLibrary] = "Ctrl+Alt+L",
        [HotkeyAction.CaptureScrolling] = "Ctrl+Alt+4",
        [HotkeyAction.GrabText] = "Ctrl+Alt+5",
        [HotkeyAction.CaptureFreehand] = "Ctrl+Alt+6",
        [HotkeyAction.CaptureMultiArea] = "Ctrl+Alt+7",
        [HotkeyAction.RecordGif] = "Ctrl+Alt+8",
        [HotkeyAction.RecordVideo] = "Ctrl+Alt+9",
        [HotkeyAction.RepeatLastCapture] = "Ctrl+Alt+R",
    };

    /// <summary>Parsed hotkeys; unparsable or missing entries are skipped (falling back to defaults for missing ones).</summary>
    public Dictionary<HotkeyAction, HotkeyGesture> GetGestures()
    {
        var result = new Dictionary<HotkeyAction, HotkeyGesture>();
        var defaults = DefaultHotkeys();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var text = Hotkeys.TryGetValue(action, out var configured) ? configured
                     : defaults.GetValueOrDefault(action);

            // An explicitly empty string means "unbound".
            if (HotkeyGesture.TryParse(text, out var gesture))
                result[action] = gesture;
        }
        return result;
    }

    /// <summary>Parses <see cref="FixedRegionSize"/>; null when empty or invalid.</summary>
    public Size? GetFixedRegionSize()
    {
        var parts = FixedRegionSize.Split('x', 'X', '×', ' ');
        parts = parts.Where(p => p.Length > 0).ToArray();
        return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0
            ? new Size(w, h)
            : null;
    }
}
