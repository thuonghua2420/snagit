# Architecture

## Capture flow
```
Hotkey (GlobalHotkeyService, WM_HOTKEY on a message-only window)  ──or──  tray / the main window's Capture menu (Ui/CaptureMenu)
  └─ AppHost.Dispatch(action)
       └─ CaptureWorkflow.Run(mode)                              [App/Services]   (busy-guarded, async)
            0. AppHost.HideMainWindowAsync (+250 ms: the app's own window must not be in the picture; RestoreMainWindow in `finally`)
               CountdownForm if CaptureDelaySeconds > 0         (HUD: no-activate, excluded from capture)
            1. IScreenGrabber.Grab(VirtualScreen)               frozen snapshot of ALL monitors (BitBlt + CAPTUREBLT, ~90 ms for 5760×1200)
            2. IScreenGrabber.GetWindows()                      visible top-level windows, front-most first (DWM frame bounds)
            3. CaptureOverlayForm (OverlayMode)                 Region | Window | FreeHand | MultiArea  → OverlayResult
                 hover → GetWindowChain (RealChildWindowFromPoint): wheel/PageUp-Down = parent/child control
                 magnifier loupe, arrow keys move the cursor pixel-wise, fixed-size region, Esc cancels
            4. build the image:  crop snapshot │ PrintWindow (GrabWindow) │ CaptureComposer.FreeHand/MultiArea │ ScrollingCapture │ OCR
            5. Deliver: clipboard? → ILibraryService.Add (.snapgit project by default) → AppHost.OpenInEditor = loaded INTO THE MAIN WINDOW
               (tray balloon instead when "open the editor" is off, or the user chose to keep their unsaved picture)
```
- `Grab Text`: crop → `IOcrService` (Windows.Media.Ocr) → `OcrLayout.MergeRows` (re-joins the engine's word fragments into rows) → clipboard + `GrabTextForm`.
- `Repeat last`: `CaptureWorkflow` remembers `(mode, area, window)` and the free-hand/multi-area `OverlayResult`.
- The overlay is placed with `SetWindowPos` in exact pixel bounds and swallows `WM_DPICHANGED` (it spans monitors of different DPI).

## Scrolling capture
`ScrollingCapture.RunAsync` (App) drives, `ScrollStitcher` (Core, pure) decides.
1. Grab the area, inject mouse-wheel at its centre (`ScrollInjector`, SendInput), wait `ScrollDelayMs`, grab again.
2. `ScrollStitcher.TryAdd` → `Appended | NoChange | NoMatch`. Rows are hashed in 24 horizontal segments; a row "matches" with ≤3 bad segments, so window borders,
   scrollbars and a blinking caret don't break alignment. `AlignmentScore` counts only **informative** segments (not plain background) so blank margins can't
   make a wrong offset look right. Sticky headers/footers are detected on the first pair and kept once.
3. The step size adapts (aim ≈55 % of the view); `NoMatch` → re-grab once (animations), then scroll back and halve the step; 2× `NoChange` = end of page.
4. Horizontal mode rotates frames 90° and reuses the vertical algorithm. `SNAPGIT_DEBUG_FRAMES=<dir>` dumps every grabbed frame.

## Recording
- `IRecorder` (Core). `GifRecorder` (Core, testable with a fake grabber): capture thread at N fps → `GifEncoder` (median-cut palette, frame differencing, LZW).
- Video quality: `VideoResolution` (Full HD default / HD / Original) and `VideoQuality` (Standard / High default / Best) → `VideoEncoding.OutputSize/Bitrate` (pure, tested). Frames enter at the size of the recorded area and
  the transcoder scales to the output size (GPU video processor; scaling per frame with GDI+ on the capture thread dropped 1080p30 to ~24 fps). H.264 High profile, `MrfCrf444` colour conversion, ~11 Mbit/s for 1080p30,
  frame buffers are pooled (`MediaStreamSample.Processed`) to avoid 8 MB allocations per frame. `VideoRecorder.FramesCaptured/FramesDropped/OutputSize` are exposed for diagnostics and tests.
- `VideoRecorder` (Platform): capture thread → bounded queue → `MediaStreamSource` → `MediaTranscoder` (H.264/AAC MP4). Audio: `AudioMixer` (NAudio WASAPI loopback + mic,
  48 kHz stereo, wall-clock paced so silence never desyncs). Even width/height enforced.
- `RecordingService` (App): overlay → countdown → start → `RecordingBar` (HUD) → same hotkey stops → file lands in the library.

## Design system (compact classic toolbars, light + dark)
Reference: the owner's Snagit-style samples — a 25 px top bar and a 26 px wide tool strip, 16 px colour icons, 24 px buttons, 1 px separators, selection = 1 px blue outline on a light fill.
- **Icons**: `IconSet` (artwork: a list of `Piece(Path, Fill, Stroke, Width, Gloss, Dashed, FillAlpha, Text)` per `UiIcon`; paths are SVG path data on a **16 × 16 grid**) → `SvgPath` (parser: M L H V C S Q T A Z,
  absolute/relative, arc → Béziers) → `Icons.Get(icon, tint?, size)` (cached bitmaps). Colour icons in the classic Office-2007 spirit: 1 px darker outline, a slight top-to-bottom gloss, drawing tools in light-blue body +
  blue outline + white corner handles, each kind with its own hue (pencil yellow, step badge red, crop brown, scissors/printer grey, …). `Ink` (neutral parts) follows the theme. Derived icons (Redo = mirrored Undo,
  Align Top = Align Left turned…) cost no artwork. `tint` renders a one-colour silhouette (white icon on the accent button); disabled icons are `Icons.Disabled` (grey).
  **Exact-size rendering:** items hold the 32 px reference bitmap, but `ModernRenderer.OnRenderItemImage` / `ToolPalette` look it up with `Icons.TryDescribe` and redraw it at the real slot size
  (16 px at 100 % DPI, 20/24/32 on denser screens) so the 1 px outline is one crisp pixel row. Menus/dropdowns use 16 px slots too. `IconSheetTests` writes `icons-light.png/-dark.png` (16 px, 32 px, disabled).
  Shapes are our own artwork; there are no third-party assets in the repo.
- **Theme**: `Theme.Colors` (`Palette`: Window, Surface, Canvas, Border, Text, Muted, Accent, OnAccent, Hover, Pressed, Selected, Input) for light and dark, `Theme.FontName` (Segoe UI Variable Text, else Segoe UI).
  `Theme.Apply(form)` / `ApplyTo(control)` set fonts + colours + accessible names; `Recolor` skips controls that paint themselves (canvas, palette, history, timeline, video view).
  Contrast is pinned by `ThemesMeetWcagContrast` (text AAA, secondary/accent AA).
- **Toolbars**: `ModernRenderer` is the global `ToolStripManager.Renderer` (menus, toolbars, status bar, context menus, tray menu): flat, rounded hover/checked states, thin separators, chevrons, accent-filled
  "primary" button (`Tag = ModernRenderer.PrimaryTag`); radius 3, checked = tint + 1 px accent outline, hover = soft outline. `Toolbars.Button/Toggle/Menu/Primary/PrimaryMenu/Item` build items with one proportion
  everywhere (**16 px icon in a ≈ 24 px button**, padding 4, tooltip = accessible name); bar padding (3,1,3,1), separators 1 px.
- **Custom controls** (all keyboard- and screen-reader-accessible): `ToolPalette` (**one column** of 24 px buttons ≈ 26 px wide, hairline group dividers; switches to two columns by itself when the window is too short),
  `SideNav` (Settings sections, 30 px rows), `HistoryStrip` (compact tiles 92 × 58; clicking the title collapses it to its header line), `TimelineBar` (64 px), `VideoView`.
- **Layout rules**: one command bar (icons; words only on the primary action, the page switch and where an icon is ambiguous), context-sensitive style bar, tool palette left, zoom controls in the status bar (right),
  secondary actions grouped in menus (Arrange ▾, Picture ▾) instead of a row of 20 buttons. Settings = left navigation + content + accent "Save" (760 × 560). Minimum main-window size 860 × 540.

## Main window (one window for everything)
```
AppHost.MainWindow = EditorForm     the only top-level window of the app; the app lives in the tray, X just hides it
   pages (EditorForm.MainPage):  Editor ◄──► Library ◄──► Video       each keeps its state while another page is in front
   ├─ Editor   canvas + tool box + action/style bars + HistoryStrip      EditorForm.cs / .Ui.cs / .ImageOps.cs
   ├─ Library  LibraryView : UserControl — gallery, filters, import, tags, share, PDF…   ("Open library ›", tray, Ctrl+Alt+L)
   └─ Video    VideoEditorPage : UserControl                             (see "Video editor")
```
- `EditorForm.Pages.cs`: `ShowPage` toggles the chrome (the editor's menus/bars/canvas/history hide on other pages), `OpenDocument` swaps the editor's document IN PLACE
  (asks "Save changes?" when the current picture is modified — Cancel keeps everything and the capture only gets a tray balloon; per-tool styles and the stamp carry over via
  `EditorContext.CopyPreferencesFrom`), `OpenVideoAsync` replaces the video page, `GoBack` returns from the library to the last editing page, `HideInsteadOfClose` = X.
- Keys: while another page is in front, the editor's menu shortcuts (`_editorShortcuts`) and tool letters are NOT processed (Ctrl+C belongs to the library list, etc.).
- Closing (X / Ctrl+W): asks about unsaved work, hides the window, resets to a blank canvas + library page. App exit (tray "Exit", shutdown): no questions, `Autosave()`, unsaved
  work stays in the recovery folder. Recovered pictures at the next start go into the library (one window = one picture), the last is opened.
- Capture/recording hide the window first (`HideMainWindowAsync`) and bring it back afterwards (`RestoreMainWindow`; a recording keeps it hidden until it ends).
- `AppHost.ShowMain` also forces the window to the front (TopMost flicker) because Windows may refuse focus to a background app.
- Test seams: `EditorForm.Page/Library/Video/Document/CurrentPath/SaveChangesPrompt`, `AppHost.MainWindow`.

## Editor
```
EditorForm (App; partial: .cs state/save, .Ui.cs construction, .ImageOps.cs dialogs, .Pages.cs pages/lifetime)
   └─ EditorCanvas (Editor/Controls)  zoom/pan, paint, in-place TextBox, mouse → tool
         └─ EditorContext   active tool, per-tool style, multi-selection, stamps, arrange/duplicate/nudge/paste-layer
               ├─ IEditorTool (Tools/): Select(marquee), DragShape, Pen, Text/Callout, Step, Stamp, Eyedropper, Fill, Crop, CutOut, SmartMove
               └─ EditorDocument   base Bitmap + List<Annotation> + CommandHistory + ApplyImageOperation
                     └─ IUndoableCommand: Add, Remove, Modify, Composite, Reorder, ReplaceImage, Crop
```
- Layout: menu → action bar (icon + caption, grouped file │ history │ organise │ image │ view) → context-sensitive style bar; left = tool box (ToolStrip in Table layout, 2 columns of 40 px buttons,
  name + key in the tooltip); bottom = `HistoryStrip` (Controls/) under the canvas only (added before the tool box so it docks right of it), then the status bar.
- `HistoryStrip`: the 30 newest `ILibraryService` items as tiles (`ThumbnailLoader`, static shared cache), follows `Library.Changed`, current file outlined, click/Enter → `AppHost.OpenItem`
  (loads into the same window), "Open library ›" link (switches to the library page), wheel/keys scroll. Test seams `EditorForm.History`, `ItemCount`, `ThumbnailsLoaded`.
- Icons: see "Design system" below.
- Annotations: Rectangle(rounded), Ellipse, Highlight, Line, Arrow(curved, head styles), Pen, Step, Text, Callout, Stamp, Blur, Spotlight, Magnify, ImageLayer.
- `ApplyImageOperation(flatten:true)` hands the op the rendered picture and consumes the annotations (rotate, effects, cut-out); `flatten:false` keeps them and shifts them (canvas resize, crop).
- Organising: `GroupAnnotation` (children restored in place on undo — other history entries point at those instances), `EditorContext.Align/Distribute/GroupSelection`,
  clipboard objects = `AnnotationJson` under the format `SnapGit.Annotations.v1` (+ a bitmap of the selection for other apps).
- Style extras: `GradientTo/GradientAngle` (`Geometry.FillBrush`), text outline (`GraphicsPath.AddString`).
- Smart Move = `DetectObjectBounds` (region growing) → `ImageLayerAnnotation` + `FillRegionSmart` patch of the hole, one undo step.
- Project file: `.snapgit` = ZIP { image.png, document.json (polymorphic via `AnnotationJson`), preview.png }. Rasters are never overwritten by Save.
- Autosave every 30 s to `%APPDATA%\SnapGit\recovery\*.snapgit`; offered back on the next start (`RecoveryService`).

## Video editor
```
VideoEditorPage (App, a UserControl on the main window; .cs = state/plan history/playback/export/keys, .Ui.cs = bars, caption panel)
   ├─ VideoView (Controls/)      current frame through VideoFrameTransform; crop mode = draggable rectangle on the whole rotated picture
   ├─ TimelineBar (Controls/)    ruler scrubs, track drags a selection, hatched = cut parts, amber = captions, red = playhead
   ├─ VideoPlayback (Platform)   MediaPlayer is the CLOCK (+ sound); pictures come from MediaComposition.GetThumbnailAsync at its position
   ├─ VideoEditPlan (Core)       immutable data: Keep ranges, Rotation, FlipX/Y, Crop, Mute, Captions  (undo stack = list of plans)
   └─ VideoEditor.RenderAsync (Platform)   plan → MP4, a chain of only the passes needed:
        1. cut + mute   MediaComposition (one MediaClip per kept range)
        2. rotate → flip → crop   MediaTranscoder + VideoTransformEffectDefinition   (MediaClip REJECTS this effect: 0xC00DA7FC)
        3. captions     MediaComposition overlay of a frame-sized transparent PNG per caption (Core CaptionRenderer), last so text isn't rotated
```
- Picture order is rotate → flip → crop and the crop is in pixels of the rotated + flipped picture (what the user sees); this is exactly how the system effect behaves
  (found by probing), and `VideoFrameTransform` (preview, snapshot) uses the same maths — `VideoPreviewMatchesExportTests` pins both against a four-colour clip.
- Opening: `AppHost.OpenItem` sends videos to `OpenVideoEditor` → `EditorForm.OpenVideoAsync` (the same file again = same page; another video asks `TryClose()` about unexported edits first);
  a finished recording opens it when "open the editor after each capture" is on; library "Edit video…" too. The page pauses itself when another page comes to the front.
- Export is always a NEW library item ("(edited)"); close asks only when the plan differs from the last export.
- **MF quirk:** uncompressed RGB32 samples are read BOTTOM-UP — `VideoRecorder` writes rows in reverse (regression test `RecordedVideoIsNotUpsideDown`). Older recordings can be fixed with Flip vertically.
- Settings: `AppHost.ShowSettings(owner, SettingsTab)`; editor menu "Settings" + the "Hotkeys" button open the right tab.

## Profiles & trimming
- `CaptureProfile` (settings) → `CaptureOptions.Create(settings, profile)`: the effective delay/fixed size/cursor/clipboard/editor of ONE capture; `CaptureWorkflow` keeps it in `_options`
  (and remembers the profile for repeat-last). Menus are built on `DropDownOpening` from `Settings.Profiles`.
- `TrimForm`: GIF → `GifTrimmer` (GDI+ returns composited frames + delay table → `GifEncoder`), video → `VideoTrimmer` (`MediaClip` trim + `MediaComposition.RenderToFileAsync`).
  Result is always a new library item.

## Library & sharing
- Folder of files (project/image/GIF/video) + `library-index.json` (tags, favourites) keyed by file name. `NamePattern` tokens: `{title} {counter} {user} {machine}` + date formats.
- `ThumbnailLoader` per kind (project preview, GIF frame, video poster via WinRT thumbnails). Query = text/kind/tag/favourites/sort.
- `ShareService`: Windows share sheet (IDataTransferManagerInterop), copy to folder, run program, clipboard file. **No credentials are stored** (no FTP/cloud passwords).
- Export: PDF (own writer, validated by Windows' PDF renderer in tests), batch convert/resize, combine images, step-by-step document.

## App shell
`AppHost` (composition root) · `TrayApplicationContext` (tray, balloons, recovery prompt, update check) · `Theme` (light/dark, title bar, accessible names) ·
`Loc` (English text is the key; `Vietnamese.Table`) · `UpdateChecker` (JSON feed URL in settings; notify-only).

## Persistence
Settings `%APPDATA%\SnapGit\settings.json` (atomic write; hotkeys as text, empty = unbound) · library folder (default `Pictures\SnapGit`) · logs `…\logs` ·
override all of `%APPDATA%\SnapGit` with the `SNAPGIT_DATA` environment variable (e2e/tests do).

## Extension points
| To add… | Touch |
|---|---|
| annotation/tool | skill `snapgit-add-annotation-tool` |
| capture mode / hotkey | skill `snapgit-add-capture-mode` |
| any user-visible text | skill `snapgit-localize` |
| different capture backend (DXGI / Windows.Graphics.Capture) | implement `IScreenGrabber` in Platform, swap in `AppHost` |
| setting | `AppSettings` + `SettingsForm` (+ test in `CoreTests`/`CoreFeatureTests`) |
