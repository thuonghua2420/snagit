using System.Drawing.Imaging;
using SnapGit.App.Forms;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Imaging;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Core.Settings;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Services;

/// <summary>
/// Orchestrates one capture end to end: (delay) → grab → pick area → crop/compose → clipboard / library / editor.
/// To add a capture mode: extend <see cref="CaptureMode"/>, handle it in <see cref="CaptureAsync"/>
/// (see .claude/skills/snapgit-add-capture-mode).
/// </summary>
public sealed class CaptureWorkflow
{
    private readonly AppHost _host;
    private bool _busy;
    private (CaptureMode Mode, Rectangle Area, IntPtr Window)? _last;
    private OverlayResult? _lastComposite; // free-hand path / multi-area regions, for "repeat last"
    private CaptureProfile? _lastProfile;   // repeat-last runs with the same profile options
    private CaptureOptions _options = new(0, null, false, false, true, false); // effective options of the capture in progress

    public CaptureWorkflow(AppHost host) => _host = host;

    public bool IsBusy => _busy;

    /// <summary>Starts a capture and reports (never throws) problems through <see cref="AppHost.Notify"/>.</summary>
    public async void Run(CaptureMode mode, CaptureProfile? profile = null)
    {
        try { await RunAsync(mode, profile); }
        catch (Exception ex) { Log.Error($"Capture ({mode}) crashed", ex); }
    }

    /// <param name="profile">Optional preset: its delay/cursor/clipboard/editor/fixed-size options replace the global ones for this capture.</param>
    public async Task RunAsync(CaptureMode mode, CaptureProfile? profile = null)
    {
        if (_busy) return; // ignore key-repeat / double triggers while a capture is in progress
        _busy = true;
        _lastProfile = profile;
        _options = CaptureOptions.Create(_host.Settings.Current, profile);

        try
        {
            await _host.HideMainWindowAsync(); // the app's own window must not be in the picture
            if (!await WaitForDelayAsync()) return;
            await CaptureAsync(mode, repeat: false);
        }
        catch (Exception ex)
        {
            Log.Error($"Capture ({mode}) failed", ex);
            _host.Notify(Loc.T("Capture failed: {0}", ex.Message));
        }
        finally
        {
            _busy = false;
            _host.RestoreMainWindow();
        }
    }

    /// <summary>Captures the same screen area as last time, without asking.</summary>
    public async Task RepeatLastAsync()
    {
        if (_busy) return;
        if (_last is null) { _host.Notify(Loc.T("Nothing to repeat yet: take a capture first.")); return; }

        _busy = true;
        _options = CaptureOptions.Create(_host.Settings.Current, _lastProfile);
        try
        {
            await _host.HideMainWindowAsync();
            if (!await WaitForDelayAsync()) return;
            await CaptureAsync(_last.Value.Mode, repeat: true);
        }
        catch (Exception ex)
        {
            Log.Error("Repeat capture failed", ex);
            _host.Notify(Loc.T("Capture failed: {0}", ex.Message));
        }
        finally
        {
            _busy = false;
            _host.RestoreMainWindow();
        }
    }

    private async Task<bool> WaitForDelayAsync()
    {
        var seconds = _options.DelaySeconds;
        return seconds <= 0 || await CountdownForm.RunAsync(seconds, new Rectangle(Cursor.Position, new Size(1, 1)));
    }

    // ------------------------------------------------------------------ the modes

    private async Task CaptureAsync(CaptureMode mode, bool repeat)
    {
        var grabber = _host.Grabber;
        var virtualScreen = grabber.VirtualScreen;
        Log.Info($"capture {mode} (repeat={repeat}, profile={_lastProfile?.Name ?? "-"}) virtualScreen={virtualScreen}");

        // One frozen snapshot of everything: the overlay never ends up in the picture and the user captures
        // exactly what they saw when they pressed the hotkey.
        using var snapshot = grabber.Grab(virtualScreen, _options.IncludeCursor);

        CaptureResult? result;
        switch (mode)
        {
            case CaptureMode.FullScreen:
                var monitor = Rectangle.Intersect(grabber.MonitorUnderCursor, virtualScreen);
                result = Crop(snapshot, virtualScreen, monitor, CaptureMode.FullScreen, null);
                _last = (mode, monitor, IntPtr.Zero);
                break;

            case CaptureMode.FreeHand:
            case CaptureMode.MultiArea:
                result = PickComposite(mode, snapshot, virtualScreen, repeat);
                break;

            case CaptureMode.Scrolling:
                result = await CaptureScrollingAsync(snapshot, virtualScreen, repeat);
                break;

            case CaptureMode.GrabText:
                await GrabTextAsync(snapshot, virtualScreen, repeat);
                return;

            default:
                result = PickRectangle(mode, snapshot, virtualScreen, repeat);
                break;
        }

        if (result is null) return;
        using (result) Deliver(result);
    }

    private OverlayResult? ShowOverlay(OverlayMode overlayMode, Bitmap snapshot, Rectangle virtualScreen)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var windows = _host.Grabber.GetWindows();
        using var overlay = new CaptureOverlayForm(snapshot, virtualScreen, windows, overlayMode,
            _options.FixedRegion, _host.Grabber.GetWindowChain);
        overlay.Shown += (_, _) => Log.Info($"overlay ready {timer.ElapsedMilliseconds} ms after the snapshot (windows: {windows.Count})");
        var accepted = overlay.ShowDialog() == DialogResult.OK;
        Log.Info($"overlay {overlayMode}: {(accepted ? $"{overlay.Result.Outcome} bounds={overlay.Result.Bounds} window='{overlay.Result.Window?.Title}'" : "cancelled")}");
        return accepted ? overlay.Result : null;
    }

    /// <summary>Region / Window: rectangle or window picked on the overlay (or the remembered one when repeating).</summary>
    private CaptureResult? PickRectangle(CaptureMode mode, Bitmap snapshot, Rectangle virtualScreen, bool repeat)
    {
        Rectangle area;
        IntPtr windowHandle = IntPtr.Zero;
        string? title = null;

        if (repeat && _last is { } last)
        {
            area = last.Area;
            windowHandle = last.Window;
        }
        else
        {
            var picked = ShowOverlay(mode == CaptureMode.Window ? OverlayMode.Window : OverlayMode.Region, snapshot, virtualScreen);
            if (picked is null) return null;
            area = picked.Bounds;
            windowHandle = picked.Window?.Handle ?? IntPtr.Zero;
            title = picked.Window?.Title;
        }

        if (area.Width < 1 || area.Height < 1) return null;
        _last = (mode, area, windowHandle);

        // Covered / off-screen windows: ask Windows to render the window itself.
        if (windowHandle != IntPtr.Zero && _options.UsePrintWindow)
        {
            var rendered = _host.Grabber.GrabWindow(windowHandle);
            if (rendered is not null) return new CaptureResult(rendered, mode, area, title);
        }

        return Crop(snapshot, virtualScreen, area, mode, title);
    }

    private CaptureResult? PickComposite(CaptureMode mode, Bitmap snapshot, Rectangle virtualScreen, bool repeat)
    {
        // Repeating replays the remembered shape/regions instead of asking again.
        var picked = repeat && _lastComposite is { } remembered && _last?.Mode == mode
            ? remembered
            : ShowOverlay(mode == CaptureMode.FreeHand ? OverlayMode.FreeHand : OverlayMode.MultiArea, snapshot, virtualScreen);
        if (picked is null) return null;

        _lastComposite = picked;
        _last = (mode, picked.Bounds, IntPtr.Zero);

        Bitmap image;
        Rectangle bounds;
        if (mode == CaptureMode.FreeHand)
        {
            var path = picked.Path!.Select(p => new Point(p.X - virtualScreen.X, p.Y - virtualScreen.Y)).ToList();
            image = CaptureComposer.FreeHand(snapshot, path, out bounds);
        }
        else
        {
            var regions = picked.Regions!.Select(r => new Rectangle(r.X - virtualScreen.X, r.Y - virtualScreen.Y, r.Width, r.Height)).ToList();
            image = CaptureComposer.MultiArea(snapshot, regions, out bounds);
        }

        var screenBounds = new Rectangle(bounds.X + virtualScreen.X, bounds.Y + virtualScreen.Y, bounds.Width, bounds.Height);
        return new CaptureResult(image, mode, screenBounds);
    }

    private async Task<CaptureResult?> CaptureScrollingAsync(Bitmap snapshot, Rectangle virtualScreen, bool repeat)
    {
        Rectangle area;
        string? title = null;
        if (repeat && _last is { } last) area = last.Area;
        else
        {
            var picked = ShowOverlay(OverlayMode.Region, snapshot, virtualScreen);
            if (picked is null) return null;
            area = picked.Bounds;
            title = picked.Window?.Title;
        }
        if (area.Width < 20 || area.Height < 20) return null;
        _last = (CaptureMode.Scrolling, area, IntPtr.Zero);

        using var progressForm = new ScrollProgressForm(area);
        progressForm.Show();
        var progress = new Progress<int>(progressForm.UpdateFrames);

        var stitched = await ScrollingCapture.RunAsync(_host.Grabber, _host.Settings.Current, area, progress);
        if (stitched is null) return null;
        return new CaptureResult(stitched, CaptureMode.Scrolling, area, title);
    }

    private async Task GrabTextAsync(Bitmap snapshot, Rectangle virtualScreen, bool repeat)
    {
        Rectangle area;
        if (repeat && _last is { } last) area = last.Area;
        else
        {
            var picked = ShowOverlay(OverlayMode.Region, snapshot, virtualScreen);
            if (picked is null) return;
            area = picked.Bounds;
        }
        if (area.Width < 4 || area.Height < 4) return;
        _last = (CaptureMode.GrabText, area, IntPtr.Zero);

        var crop = snapshot.Clone(new Rectangle(area.X - virtualScreen.X, area.Y - virtualScreen.Y, area.Width, area.Height), PixelFormat.Format32bppArgb);
        try
        {
            var text = await _host.Ocr.RecognizeAsync(crop, _host.Settings.Current.OcrLanguage);
            if (!string.IsNullOrWhiteSpace(text.Text))
            {
                Clipboard.SetText(text.Text);
                _host.Notify(Loc.T("Text copied to the clipboard ({0} line(s)).", text.Lines.Count));
            }
            else
            {
                _host.Notify(Loc.T("No text found in the selected area."));
            }

            var form = new GrabTextForm(_host, crop, text);
            form.Show();
        }
        catch (Exception ex)
        {
            crop.Dispose();
            _host.Notify(Loc.T("Text recognition failed: {0}", ex.Message));
        }
    }

    private static CaptureResult Crop(Bitmap snapshot, Rectangle virtualScreen, Rectangle area, CaptureMode mode, string? title)
    {
        var local = new Rectangle(area.X - virtualScreen.X, area.Y - virtualScreen.Y, area.Width, area.Height);
        return new CaptureResult(snapshot.Clone(local, PixelFormat.Format32bppArgb), mode, area, title);
    }

    // ------------------------------------------------------------------ delivery

    private void Deliver(CaptureResult result)
    {
        if (_options.CopyToClipboard)
            ClipboardService.SetImage(result.Image);

        var item = _host.Library.Add(result.Image, result.SourceTitle);

        // Into the main window's editor — unless that is switched off, or the user chose to keep their unsaved picture.
        if (!(_options.OpenEditor && _host.OpenInEditor(item.FilePath)))
            _host.Notify(Loc.T("Saved {0}", item.Name), () => _host.OpenInEditor(item.FilePath));
    }
}
