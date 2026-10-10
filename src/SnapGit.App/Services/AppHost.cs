using SnapGit.App.Forms;
using SnapGit.App.Ui;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Core.Settings;
using SnapGit.Editor;
using SnapGit.Editor.Persistence;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Hotkeys;
using SnapGit.Platform.Ocr;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Services;

/// <summary>
/// Composition root: creates and wires every service once. Forms receive the host (or the services they
/// need) through their constructors — nothing else in the app uses statics/singletons.
/// </summary>
public sealed class AppHost : IDisposable
{
    public AppHost()
    {
        Settings = new JsonSettingsService();
        Settings.Load();
        ApplyLanguageAndTheme();

        Library = new FileLibraryService(Settings);
        Grabber = new ScreenGrabber();
        Hotkeys = new GlobalHotkeyService();
        Ocr = new WindowsOcrService();
        Sharing = new ShareService();
        Capture = new CaptureWorkflow(this);
        Recording = new RecordingService(this);

        Hotkeys.HotkeyPressed += (_, action) => Dispatch(action);
        Settings.Changed += (_, _) => ApplySettings();
    }

    public ISettingsService Settings { get; }
    public ILibraryService Library { get; }
    public IScreenGrabber Grabber { get; }
    public IHotkeyService Hotkeys { get; }
    public IOcrService Ocr { get; }
    public ShareService Sharing { get; }
    public CaptureWorkflow Capture { get; }
    public RecordingService Recording { get; }

    /// <summary>A message for the tray (optionally with something to run when it is clicked).</summary>
    public event EventHandler<(string Message, Action? OnClick)>? Notification;

    /// <summary>Raised when hotkeys could not be registered (so the UI can tell the user).</summary>
    public event EventHandler<IReadOnlyList<HotkeyAction>>? HotkeyConflicts;

    public void Start() => ApplySettings();

    public void Notify(string message, Action? onClick = null)
    {
        Log.Info($"notify: {message}"); // tray balloons vanish; the log keeps a trace for support/debugging
        Notification?.Invoke(this, (message, onClick));
    }

    private void ApplyLanguageAndTheme()
    {
        Loc.SetLanguage(Settings.Current.Language);
        Theme.Configure(Settings.Current.Theme);
    }

    /// <summary>Re-applies everything derived from settings (hotkeys, autostart).</summary>
    private void ApplySettings()
    {
        var failed = Hotkeys.Apply(Settings.Current.GetGestures());
        if (failed.Count > 0) HotkeyConflicts?.Invoke(this, failed);

        try { StartupRegistration.Set(Settings.Current.StartWithWindows, Application.ExecutablePath); }
        catch (Exception ex) { Log.Warn($"Could not update autostart: {ex.Message}"); }
    }

    public void Dispatch(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.CaptureRegion: Capture.Run(CaptureMode.Region); break;
            case HotkeyAction.CaptureWindow: Capture.Run(CaptureMode.Window); break;
            case HotkeyAction.CaptureFullScreen: Capture.Run(CaptureMode.FullScreen); break;
            case HotkeyAction.CaptureScrolling: Capture.Run(CaptureMode.Scrolling); break;
            case HotkeyAction.CaptureFreehand: Capture.Run(CaptureMode.FreeHand); break;
            case HotkeyAction.CaptureMultiArea: Capture.Run(CaptureMode.MultiArea); break;
            case HotkeyAction.GrabText: Capture.Run(CaptureMode.GrabText); break;
            case HotkeyAction.RepeatLastCapture: _ = RepeatLastSafe(); break;
            case HotkeyAction.RecordGif: Recording.Toggle(RecordingKind.Gif); break;
            case HotkeyAction.RecordVideo: Recording.Toggle(RecordingKind.Video); break;
            case HotkeyAction.OpenLibrary: ShowLibrary(); break;
        }
    }

    private async Task RepeatLastSafe()
    {
        try { await Capture.RepeatLastAsync(); }
        catch (Exception ex) { Log.Error("Repeat capture crashed", ex); }
    }

    /// <summary>Runs a user-defined capture profile (its options apply to this capture only).</summary>
    public void RunProfile(CaptureProfile profile) => Capture.Run(profile.Mode, profile);

    // ================================================================== the main window

    private EditorForm? _main;
    private bool _hiddenForCapture;

    /// <summary>The one window of the app: image editor, library and video editor as pages (created on first use, kept for the whole session).</summary>
    public EditorForm MainWindow => _main is { IsDisposed: false } window ? window : _main = new EditorForm(this, EditorForm.BlankDocument(), null, isProject: false);

    /// <summary>Makes the main window visible and in front (without changing its page).</summary>
    public void ShowMain()
    {
        var window = MainWindow;
        if (!window.Visible) window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Activate();

        // Windows may refuse to give focus to a background app; the window must still come to the front (hotkey / tray / capture finished).
        if (!window.ContainsFocus)
        {
            window.TopMost = true;
            window.TopMost = false;
        }
    }

    /// <summary>The library page: every capture so far.</summary>
    public void ShowLibrary()
    {
        ShowMain();
        MainWindow.ShowLibraryPage();
    }

    /// <summary>Hides the main window before a capture or recording starts, so it is not in the picture.</summary>
    public async Task HideMainWindowAsync()
    {
        if (_main is not { IsDisposed: false, Visible: true } window) return;
        _hiddenForCapture = true;
        window.Hide();
        await Task.Delay(250); // let the window really disappear before the screen is grabbed
    }

    /// <summary>Brings the main window back if <see cref="HideMainWindowAsync"/> hid it.</summary>
    public void RestoreMainWindow()
    {
        if (!_hiddenForCapture) return;
        _hiddenForCapture = false;
        if (_main is { IsDisposed: false, Visible: false } window) window.Show();
    }
    public void ShowSettings(IWin32Window? owner = null, SettingsTab tab = SettingsTab.General)
    {
        using var form = new SettingsForm(Settings, Ocr.AvailableLanguages, tab);
        form.ShowDialog(owner);
    }

    public void ShowAbout(IWin32Window? owner = null)
    {
        using var form = new AboutForm(this);
        form.ShowDialog(owner);
    }

    /// <summary>Opens a library item: projects/images in the image editor, videos in the video editor, GIFs in the default app.</summary>
    public void OpenItem(string path)
    {
        switch (LibraryItem.KindOf(path))
        {
            case LibraryItemKind.Animation:
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                break;
            case LibraryItemKind.Video:
                OpenVideoEditor(path);
                break;
            default:
                OpenInEditor(path);
                break;
        }
    }

    /// <summary>Opens a video on the video page of the main window.</summary>
    public void OpenVideoEditor(string path) => _ = OpenVideoEditorAsync(path);

    public async Task OpenVideoEditorAsync(string path)
    {
        try
        {
            ShowMain();
            await MainWindow.OpenVideoAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open the video editor for {path}", ex);
            MessageBox.Show(Loc.T("Could not open \"{0}\":\n\n{1}", Path.GetFileName(path), ex.Message), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Loads a project or image file into the main window's editor. Returns false when it could not be opened or the user
    /// cancelled the "save the current picture?" question (the file is untouched either way).
    /// </summary>
    public bool OpenInEditor(string filePath)
    {
        try
        {
            ShowMain(); // visible first: a "Save changes?" question needs a window to belong to
            var isProject = SnapProjectFile.IsProject(filePath);
            var document = isProject ? ProjectStorage.Load(filePath) : new EditorDocument(ImageEncoder.LoadUnlocked(filePath));

            if (MainWindow.OpenDocument(document, filePath, isProject)) return true;
            document.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {filePath}", ex);
            MessageBox.Show(Loc.T("Could not open \"{0}\":\n\n{1}", Path.GetFileName(filePath), ex.Message), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Opens an in-memory image (paste, combine, a video frame) as a new, unsaved picture in the main window.</summary>
    public bool OpenNewInEditor(Bitmap image)
    {
        ShowMain();
        var document = new EditorDocument(image);
        if (MainWindow.OpenDocument(document, null, isProject: false)) return true;
        document.Dispose();
        return false;
    }

    public void Dispose()
    {
        (Hotkeys as IDisposable)?.Dispose();
        _main?.Dispose();
    }
}