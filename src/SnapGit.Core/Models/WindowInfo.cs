namespace SnapGit.Core.Models;

/// <summary>A top-level window as seen on screen. Bounds are virtual-screen pixels (shadow excluded).</summary>
public sealed record WindowInfo(IntPtr Handle, string Title, Rectangle Bounds);
