using SnapGit.App.Forms;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Core.Recording;
using SnapGit.Platform.Recording;

namespace SnapGit.App.Services;

public enum RecordingKind
{
    Gif,
    Video,
}

/// <summary>Pick an area → (countdown) → record → save into the library. The same hotkey toggles start/stop.</summary>
public sealed class RecordingService
{
    private readonly AppHost _host;
    private IRecorder? _recorder;
    private RecordingBar? _bar;
    private string? _path;
    private bool _starting;

    public RecordingService(AppHost host) => _host = host;

    public bool IsRecording => _recorder is not null;

    public async void Toggle(RecordingKind kind)
    {
        try
        {
            if (IsRecording) await StopAsync();
            else await StartAsync(kind);
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed", ex);
            _host.Notify(Loc.T("Recording failed: {0}", ex.Message));
            Cleanup();
        }
    }

    public async Task StartAsync(RecordingKind kind)
    {
        if (_starting || IsRecording || _host.Capture.IsBusy) return;
        _starting = true;
        var started = false;

        try
        {
            await _host.HideMainWindowAsync(); // stays hidden for the whole recording, so it is not in the picture
            var settings = _host.Settings.Current;
            var grabber = _host.Grabber;
            var virtualScreen = grabber.VirtualScreen;

            Rectangle area;
            using (var snapshot = grabber.Grab(virtualScreen, includeCursor: false))
            {
                using var overlay = new CaptureOverlayForm(snapshot, virtualScreen, grabber.GetWindows(), OverlayMode.Region,
                    settings.GetFixedRegionSize(), grabber.GetWindowChain);
                if (overlay.ShowDialog() != DialogResult.OK) return;
                area = Rectangle.Intersect(overlay.Result.Bounds, virtualScreen);
            }
            if (area.Width < 16 || area.Height < 16) { _host.Notify(Loc.T("The selected area is too small to record.")); return; }

            // A short countdown even when no delay is configured: the selection overlay just closed.
            var seconds = Math.Max(settings.CaptureDelaySeconds, 3);
            if (!await CountdownForm.RunAsync(seconds, area)) return;

            _path = _host.Library.ReservePath(kind == RecordingKind.Gif ? ".gif" : ".mp4", Loc.T("Recording"));

            if (kind == RecordingKind.Gif)
            {
                _recorder = new GifRecorder(grabber);
                _recorder.Start(new RecordingOptions(area, settings.GifFramesPerSecond, settings.RecordCursor, settings.GifMaxSeconds), _path);
            }
            else
            {
                _recorder = new VideoRecorder(grabber);
                _recorder.Start(new RecordingOptions(area, settings.VideoFramesPerSecond, settings.RecordCursor)
                {
                    CaptureSystemAudio = settings.RecordSystemAudio,
                    CaptureMicrophone = settings.RecordMicrophone,
                    Resolution = settings.VideoResolution,
                    Quality = settings.VideoQuality,
                }, _path);
            }

            _recorder.StoppedByItself += OnStoppedByItself;

            _bar = new RecordingBar(kind == RecordingKind.Gif ? "GIF" : Loc.T("video"), () => _recorder?.Elapsed ?? TimeSpan.Zero,
                onStop: () => Toggle(kind), onCancel: Cancel, area);
            _bar.Show();
            started = true;
        }
        finally
        {
            _starting = false;
            if (!started) _host.RestoreMainWindow(); // cancelled / too small / failed before the recording began
        }
    }

    public async Task StopAsync()
    {
        if (_recorder is not { } recorder) return;

        var bar = _bar;
        _bar = null;
        bar?.Close();
        _host.Notify(Loc.T("Saving the recording…"));

        string saved;
        try
        {
            saved = await recorder.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not finish recording", ex);
            _host.Notify(Loc.T("Recording failed: {0}", ex.Message));
            return;
        }
        finally
        {
            recorder.StoppedByItself -= OnStoppedByItself;
            recorder.Dispose();
            _recorder = null;
            _host.RestoreMainWindow();
        }

        _host.Library.NotifyModified(saved);
        _host.Notify(Loc.T("Recording saved: {0}", Path.GetFileName(saved)), () => _host.OpenItem(saved));

        // Like screenshots: a finished video opens in its editor when "open the editor after each capture" is on.
        if (_host.Settings.Current.OpenEditorAfterCapture && SnapGit.Core.Library.LibraryItem.KindOf(saved) == SnapGit.Core.Library.LibraryItemKind.Video)
            _host.OpenVideoEditor(saved);
    }

    public void Cancel()
    {
        if (_recorder is null) return;
        _recorder.StoppedByItself -= OnStoppedByItself;
        _recorder.Cancel();
        Cleanup();
        _host.Notify(Loc.T("Recording cancelled."));
    }

    private void OnStoppedByItself(object? sender, string? error)
    {
        // Raised on a worker thread: hop to the UI thread.
        var bar = _bar;
        if (bar is { IsHandleCreated: true, IsDisposed: false })
        {
            bar.BeginInvoke(async () =>
            {
                if (error is not null)
                {
                    Cancel();
                    _host.Notify(Loc.T("Recording stopped: {0}", error));
                }
                else
                {
                    await StopAsync();
                }
            });
        }
    }

    private void Cleanup()
    {
        _bar?.Close();
        _bar = null;
        _recorder?.Dispose();
        _recorder = null;
        _host.RestoreMainWindow();
    }
}
