namespace SnapGit.Core.Models;

/// <summary>How the user wants to pick what to capture.</summary>
public enum CaptureMode
{
    /// <summary>Snagit "All-in-One": hover highlights a window/control, click captures it, drag captures a free region.</summary>
    Region,

    /// <summary>Hover highlights a window, click captures it. Dragging is disabled.</summary>
    Window,

    /// <summary>The whole monitor under the mouse cursor.</summary>
    FullScreen,

    /// <summary>Pick an area, then the app scrolls it and stitches the frames (see <see cref="Imaging.ScrollDirection"/>).</summary>
    Scrolling,

    /// <summary>Draw any closed shape; everything outside it becomes transparent.</summary>
    FreeHand,

    /// <summary>Select several rectangles, finish with Enter: they are combined into one image.</summary>
    MultiArea,

    /// <summary>Pick an area and run OCR on it (text goes to the clipboard).</summary>
    GrabText,
}
