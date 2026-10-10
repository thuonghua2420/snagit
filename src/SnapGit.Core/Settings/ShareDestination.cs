namespace SnapGit.Core.Settings;

public enum ShareKind
{
    /// <summary>Copy the file into a folder (OneDrive/Dropbox/network share folders work too).</summary>
    Folder,

    /// <summary>Run a program with the file path as argument ({file} placeholder in <see cref="ShareDestination.Arguments"/>).</summary>
    Program,

    /// <summary>The standard Windows share sheet (Mail, Teams, Nearby sharing, ...).</summary>
    WindowsShare,

    /// <summary>Put the file itself on the clipboard (paste into Explorer, Outlook, Slack...).</summary>
    ClipboardFile,
}

/// <summary>A user-defined "Share to..." target. Credentials are deliberately NOT stored (no FTP/cloud passwords in plain JSON).</summary>
public sealed class ShareDestination
{
    public string Name { get; set; } = string.Empty;
    public ShareKind Kind { get; set; }

    /// <summary>Folder path or program path, depending on <see cref="Kind"/>.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Program arguments; "{file}" is replaced by the quoted file path.</summary>
    public string Arguments { get; set; } = "{file}";
}
