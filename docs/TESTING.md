# Testing

| Layer | Where | What it proves | Needs |
|---|---|---|---|
| Unit | `tests/SnapGit.Tests` (`Core*`, `Editor*`, `ImageOps*`, `ScrollStitcher*`, `Localization*`) | pure logic: hotkeys, settings, library, image ops, annotations, commands, tools, stitching, GIF/PDF, naming, OCR layout, i18n coverage | nothing |
| Windows integration | `PlatformTests`, `VideoRecorderTests`, `VideoEditorTests`, `RecorderTests`, `PerformanceTests` | real Windows OCR, real MP4 encode (with audio inputs, orientation), video editor renders (cut/crop/rotate/flip/mute/captions, 3-pass chain) checked on a red/green/blue/yellow clip, playback rate, BitBlt grab, GIF recording with a fake grabber, loose perf guard-rails | a desktop session (each test returns early and says why when it can't run) |
| Design system | `SvgPathTests`, `IconAndThemeTests` (every icon defined, on the 16 px grid, ink at 16 px, 16–32 px sizes, disabled = grey, `TryDescribe`, WCAG contrast), `ToolPaletteTests` (one slim column, two-column fallback, keyboard, accessibility, primary button colour) | the icon parser/artwork, palettes, custom controls | UI tests need an STA thread |
| Video quality | `VideoEncodingTests` (sizes, bitrates, defaults), `VideoQualityTests` (1080p30 frame rate, Full HD output, PSNR), `VideoRecorderTests` (orientation incl. scaled) | Full HD recording keeps up and stays sharp | a desktop session; part of the serial "Heavy" collection |
| Preview ↔ export | `VideoPreviewMatchesExportTests`, `VideoEditPlanTests` | the editor preview maths equals what the renderer produces; plan arithmetic (cuts, time mapping, crop/rotate bookkeeping) | nothing |
| UI smoke | `UiSmokeTests`, `MainWindowTests` (pages, in-place loading, "Save changes?", close = hide, hide-for-capture, key guard), `VideoEditorFormTests` (video page), `IconSheetTests` | every form builds and paints (light/dark × en/vi), every annotation type renders, every tool selectable, settings save through the real button, accessible names, history strip, settings entry points, the video editor edits/plays/exports a clip, every icon has ink | STA thread; **fails after 45–90 s** if a modal dialog blocks |
| Live e2e | `tools/smoke/e2e.ps1` | hotkey → overlay → capture → file for region, scrolling (pixel-exact 300 lines), OCR, GIF, MP4, free-hand, multi-area, repeat, PrintWindow (of the test window), main window steps aside during a capture | the real desktop; ~1.5 min; hands off the mouse |

## Running
```
dotnet build SnapGit.sln && dotnet test SnapGit.sln --no-build
powershell -NoProfile -File tools\smoke\e2e.ps1                       # all scenarios
powershell -NoProfile -File tools\smoke\e2e.ps1 -Only B,G -KeepFiles  # some scenarios, keep output + print the app log
$env:SNAPGIT_DEBUG_FRAMES = "C:\temp\frames"                          # scrolling capture dumps every frame
$env:SNAPGIT_SHOTS = "C:\temp\shots"                                  # UI tests save PNGs of the forms + icon sheets (visual review, no screen capture)
```

## Diagnostics (not in the normal run)
`SNAPGIT_REAL_SCREEN=1 dotnet test --no-build --filter RealScreenFidelity -v n --logger "console;verbosity=detailed"` records a real region of the screen (a top-most window of our own with dense small text), then prints
luma / chroma PSNR and edge sharpness of: the recording, an editor export (2nd generation) and the editor's preview scaling paths. It saves nothing and skips itself if the window is covered.
What it showed (2026-10-05): luma 37–39 dB (text shapes intact), chroma ≈ 21 dB at ANY bitrate or encoder (H.264 4:2:0 limit; 2× pixel doubling only reaches ≈ 25 dB), export loses < 0.5 dB,
**the editor preview was the blurry part**: decoding at 1280 px and stretching kept 61 % of the edge sharpness, native decode + one bicubic shrink keeps 97 %.

## Rules we learned the hard way
- A regression test must be seen failing without the fix (e.g. `DisposingAFinishedRecorderKeepsTheVideo`).
- Tests for video need a distinctive picture (four colour quadrants), never "a screen grab": orientation/crop/rotate bugs are invisible on uniform content. The shell thumbnail of an MP4 is not frame 0 — ask for an exact time.
- A UI test must fail on timeout; a hung modal dialog once made a test "pass" after 2 minutes.
- Live synthetic input is disturbed by anything else touching mouse/keyboard: e2e retries a failing scenario once and *reports* the retry.
- Judge stitching by pixels, not by height: a 3-line gap once hid inside a plausible 7097 px result.
- Never screenshot the whole desktop here (other monitors show private content).
