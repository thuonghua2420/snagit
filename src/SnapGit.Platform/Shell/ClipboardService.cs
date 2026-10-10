namespace SnapGit.Platform.Shell;

/// <summary>Clipboard access. Must be called on an STA (UI) thread.</summary>
public static class ClipboardService
{
    /// <summary>Puts a copy of the image on the clipboard (callers keep ownership of their bitmap).</summary>
    public static void SetImage(Bitmap image)
    {
        // SetDataObject retries internally: another process may briefly hold the clipboard open.
        Clipboard.SetDataObject(new Bitmap(image), copy: true, retryTimes: 5, retryDelay: 50);
    }
}
