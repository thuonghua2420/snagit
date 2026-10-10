# SnapGit — Snagit-style screenshot, recording & annotation tool for Windows

C# / .NET 8 / WinForms (PerMonitorV2 DPI), Windows 10 2004+ APIs (WinRT OCR, Media Foundation, share sheet).
Goal: Snagit feature parity, then the owner's own extras. After a `/clear`, run the **`snapgit-resume`** skill first.

## Commands
```
dotnet build SnapGit.sln                          # must be 0 errors / 0 warnings
dotnet test  SnapGit.sln --no-build               # ~280 tests, ~1m45 (unit + real Windows OCR/MP4/video editor/Full HD + UI smoke)
powershell -NoProfile -File tools\smoke\e2e.ps1   # LIVE acceptance on the real desktop (~1.5 min, don't touch mouse/keyboard)
dotnet run --project src/SnapGit.App              # tray app (add -- --background to start hidden)
powershell -File tools\publish.ps1 [-SelfContained]   # release build (+ installer if Inno Setup is installed)
```
`dotnet test` does NOT rebuild SnapGit.App for the exe — build the solution first. Kill a running `SnapGit.exe` before building (it locks the DLLs).

Default hotkeys (Settings → Hotkeys): Ctrl+Alt+1 region/window/control · 2 window · 3 full screen · 4 scrolling · 5 Grab Text (OCR) ·
6 free-hand · 7 multi-area · 8 GIF start/stop · 9 video start/stop · R repeat last · L library.
(PrintScreen is NOT default: Windows 11 Snipping Tool / Greenshot claim it.)

## Layout (dependency direction: App → Editor/Platform → Core; nothing points back)
| Project | Responsibility | Rules |
|---|---|---|
| `src/SnapGit.Core` | Models, settings, hotkey gestures, library (+index, naming, `.snapgit` container), image ops, scroll stitcher, GIF/PDF writers, GIF recorder, OCR layout, localisation, update checker | No forms, no Win32. Interfaces for platform things (`IScreenGrabber`, `IHotkeyService`, `IOcrService`, `IRecorder`). Pure + unit-tested. |
| `src/SnapGit.Platform` | Win32/WinRT: hotkeys, BitBlt/PrintWindow capture, window chain, SendInput scrolling, Windows OCR, MP4+audio recorder | **All P/Invoke in `Native/NativeMethods.cs`**. |
| `src/SnapGit.Editor` | Document, annotations (14 types), commands (undo/redo), tools (19), `EditorCanvas`, project JSON | No App/Platform dependency; headless-testable. |
| `src/SnapGit.App` | WinForms shell: `AppHost` (composition root), tray, `CaptureWorkflow`, `RecordingService`, forms, theme, icons. **One main window** (`EditorForm` = `AppHost.MainWindow`) with pages Editor / Library (`LibraryView`) / Video (`VideoEditorPage`); dialogs (Settings, About, Trim GIF, Grab Text) stay separate | Services via constructors; no static singletons (except `Loc`/`Theme` config). |
| `tests/SnapGit.Tests` | xunit | Never touch real `%APPDATA%` (module initializer redirects `AppPaths.DataRoot`). |
| `tools/` | `make-icon.ps1`, `publish.ps1`, `smoke/` (live e2e) | |
| `installer/` | Inno Setup script | |

Docs: `docs/ARCHITECTURE.md` (flows), `docs/ROADMAP.md` (parity checklist), `docs/PROGRESS.md` (session log), `docs/TESTING.md` (test layers).

## Conventions
- File-scoped namespaces, nullable on, `_camelCase` fields, small related types may share a file.
- Annotations live in IMAGE pixels; the canvas converts. Style is an immutable record (`with`). **Stroke = outline/text colour, Fill = interior colour.**
- Every user edit = one `IUndoableCommand` via `doc.History.Execute`. Live drags mutate, then commit ONE command on mouse-up. Image-wide ops: `doc.ApplyImageOperation(name, op, flatten, shift)`.
- Every user-visible string: `Loc.T("English text")` (static tables: `Loc.N("...")`). Add the Vietnamese entry in `Core/Localization/Vietnamese.cs` — `LocalizationTests` fails otherwise.
- New annotation types must be registered with `[JsonDerivedType]` on `Annotation` (project files).
- Never rewrite source with PowerShell `Get-Content | Set-Content` (UTF-8 corruption): use Edit/Write, or `[IO.File]::ReadAllText/WriteAllText` with explicit UTF-8.
- Windows facts already learned (don't rediscover): `Graphics.CopyFromScreen` rejects CAPTUREBLT → we BitBlt; `new Bitmap(path)` locks files → `ImageEncoder.LoadUnlocked`; `Enum.TryParse("1")` = numeric value; `Dispose()` of a recorder must not delete the finished file;
  `Application.DoEvents` tests must never leave a modal dialog open (UI tests fail on a 45 s timeout); exports use default compositing (HighQuality gamma-blends and differs from the canvas);
  Media Foundation reads uncompressed RGB32 bottom-up (flip rows before encoding); `VideoTransformEffectDefinition` works with `MediaTranscoder`, NOT on `MediaClip` (rotate → flip → crop order, see ARCHITECTURE "Video editor").
- Privacy: this dev machine has other monitors with private content. Never screenshot the whole desktop; capture only the window under test (`tools/smoke` and the UI tests render forms offscreen).

## UI conventions (details: docs/ARCHITECTURE.md "Design system")
- Icons = `Ui/IconSet.cs` (compact classic **16 px colour icons**: `Piece` list of SVG paths on a 16 px grid, own hue + 1 px darker outline + gloss) — never bitmaps or emoji; colours = `Theme.Colors` (never hard-coded greys); toolbars via `Ui/Toolbars` (icon-only + tooltip, words only on the primary action). Compact metrics: 16 px icon in a ≈ 24 px button, one-column 26 px `ToolPalette`; renderers redraw icons at the exact slot size (`Icons.TryDescribe`).
- Anything drawn by hand must follow the palette and be keyboard + screen-reader accessible (see `ToolPalette`, `SideNav`). New toolbar/menu items need a tooltip (it becomes the accessible name).

## Skills (in `.claude/skills/`)
`snapgit-resume` · `snapgit-add-annotation-tool` · `snapgit-add-capture-mode` · `snapgit-localize` · `snapgit-e2e` · `snapgit-verify-and-log`
