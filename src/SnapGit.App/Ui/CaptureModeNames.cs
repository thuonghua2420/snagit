using SnapGit.Core.Localization;
using SnapGit.Core.Models;

namespace SnapGit.App.Ui;

/// <summary>User-facing, translated names of the capture modes (used by profile lists and dialogs).</summary>
public static class CaptureModeNames
{
    public static IReadOnlyList<CaptureMode> All { get; } = Enum.GetValues<CaptureMode>();

    public static string Display(CaptureMode mode) => mode switch
    {
        CaptureMode.Region => Loc.T("Region / window / control"),
        CaptureMode.Window => Loc.T("Window"),
        CaptureMode.FullScreen => Loc.T("Full screen"),
        CaptureMode.Scrolling => Loc.T("Scrolling area"),
        CaptureMode.FreeHand => Loc.T("Free-hand"),
        CaptureMode.MultiArea => Loc.T("Multiple areas"),
        CaptureMode.GrabText => Loc.T("Grab text (OCR)"),
        _ => mode.ToString(),
    };
}
