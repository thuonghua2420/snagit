namespace SnapGit.App.Ui;

/// <summary>
/// Every toolbar/menu glyph. The first block deliberately mirrors <c>ToolKind</c> names so a tool's icon
/// is found with <c>Enum.Parse&lt;UiIcon&gt;(kind.ToString())</c>.
/// </summary>
public enum UiIcon
{
    // Editor tools (names match SnapGit.Editor.Tools.ToolKind)
    Select, Arrow, Line, Rectangle, Ellipse, Pen, Highlight, Text, Callout, Step, Stamp, Blur, Spotlight, Magnify,
    Eyedropper, Fill, Crop, CutOut, SmartMove,

    // Editing actions
    Undo, Redo, Copy, Save, SaveAs, Delete, ZoomIn, ZoomOut, Fit, ActualSize, Back, Library, Play, Pause, Volume, Muted, Camera, Shadow, Bold, Italic,
    Duplicate, BringFront, SendBack, RotateLeft, RotateRight, FlipHorizontal, FlipVertical, Paste, Print, Image,
    Cut, Group, Ungroup, AlignLeft, AlignCenter, AlignRight, AlignTop, AlignMiddle, AlignBottom, DistributeH, DistributeV, Trim, Profile,
    Export, Arrange, Transform, Keyboard, ArrowBoth,

    // Capture / library
    Capture, Window, FullScreen, Scroll, FreeHand, MultiArea, GrabText, Repeat, Record, Gif, Video,
    Folder, Settings, Refresh, Edit, Share, Pdf, Combine, Star, StarFilled, Tag, Search, Import, Info,
}
