# Progress log

Newest entry first. Each session: add an entry, tick `docs/ROADMAP.md`. (Skill: `snapgit-verify-and-log`.)

## 2026-10-10 — Compact classic look: 16 px colour icons, slim toolbars (final layout pass)
**State:** 0 warnings; **289 tests pass (~1m40)**; live e2e 18/18.

**Owner's request:** (two sample screenshots: a 25 px top bar and a 26 px tool strip) replace all icons with that small, tidy colour style; layouts waste space and look "countrified"; apply it exactly, analyse icons and sizes, finish it.

**Analysis of the samples:** icons 16 × 16 colour, buttons ≈ 24 × 24, 1 px grey separators, tiny dropdown arrows, disabled icons grey, ONE 26 px column tool strip with 24 px pitch and 1 px group separators, selection = 1 px blue outline on light fill;
icon style = classic Office-2007 colour icons (1 px darker outline, slight gloss, light-blue drawing tools with white handles, each kind its own hue).

**Done:** `IconSet` rewritten as typed `Piece`s on a 16 px grid (all `UiIcon`s, hand-tuned per hue); `Icons` renders tint/gloss/dashes/text and registers bitmaps so `ModernRenderer`/`ToolPalette` redraw at the exact slot size
(`Icons.TryDescribe`); `Toolbars` (16 px icons, padding 4, bar padding 3/1), `ToolPalette` one column ≈ 26 px with automatic two-column fallback on short windows, `SideNav` 30 px rows, `HistoryStrip` compact + collapsible
(title click), status bar/style bar/colour picker/timeline (64 px)/caption panel/Settings (760 × 560) tightened; menus use 16 px icon slots; toggle icons are no longer tinted (outline shows the state).
Tests: icon grid/ink/size/disabled/describe, palette layout rewritten for the slim column + fallback. `IconSheetTests` sheet now shows 16 px, 32 px and disabled versions.

**Open:** a look at 125–200 % DPI on the real screen (tests render at 100 %); the history strip's collapsed state is not persisted (by decision).

## 2026-10-05 — "The video is still blurry": it was the editor preview
**State:** 0 warnings; **282 tests pass (~1m50)**; live e2e 18/18 (scenario C needed its documented single retry).

**Owner's report:** with Full HD on, the video looked blurry in the editor compared with the real screen (sample screenshot).

**Measured instead of guessed** (diagnostic tests `RealScreenFidelityTests`, `SNAPGIT_REAL_SCREEN=1`, own window with dense text recorded through the real grabber):
- recording: luma 37–39 dB (text shapes intact) but chroma ≈ 21 dB at any bitrate, hardware or software encoder, either colour-conversion mode → H.264 4:2:0 limit (colour fringes of ClearType/coloured text); 2× pixel doubling only reaches ≈ 25 dB, not worth 4× pixels;
- export through the editor: loses < 0.5 dB;
- **editor preview**: it decoded frames at 1280 px and stretched them to the view (≈ 1445 px) → 61 % of the edge sharpness; Media Foundation's own scaler aliases as well (luma 22.6 dB). Native decode + one bicubic shrink keeps 97 % (luma 42 dB).

**Fix:** `VideoPlayback` decodes still frames (paused, scrubbing, after Pause/end) at the video's own resolution; while playing it also starts at native (cap 1920) and only lowers the width itself when decoding is slower than ~15 fps; `VideoView` shrinks with bicubic.
Tests: `StillFramesAreDecodedAtTheVideosOwnResolutionWhateverTheCapIs`, `VideoViewSharpnessTests` (shown picture keeps ≥ 90 % of the reference edge sharpness).
Extras: `VideoRecorder.BitrateOverride/HighQualityColorConversion` (diagnostics), playback test thresholds relaxed (timing under load).

**Privacy slip (fixed):** the first diagnostic run recorded a region that was not covered by its own window, so a crop of the owner's real desktop was written to the scratchpad and viewed; it was deleted at once and the test now verifies it sees its own window, saves no images and only runs on request.

**Open:** colour (chroma) fidelity of fine coloured text is limited by H.264 4:2:0 — a "sharp colours" mode (record at 2× size) was measured and rejected for now; the preview of a 4K source decodes up to 3840 px stills (fine) and lowers the playing width automatically.

## 2026-10-04 (night) — Full HD video quality + a modern design system
**State:** 0 warnings; **277 tests pass (~1m40s)**; live e2e 18/18.

**Owner's request:** recordings look blurry → Full HD quality; icons look "2000s" → modern icons, international-standard UI, friendly UX.

**Video:** new settings Video resolution (Full HD default / HD / Original) and Video quality (Standard / High default / Best); bitrate by bits-per-pixel (1080p30 ≈ 11 Mbit/s instead of ≈ 4–10), H.264 High profile,
high-quality colour conversion, scaling done by the transcoder, pooled frame buffers; tests: output size/aspect, 1080p30 keeps 30 fps with 0 dropped frames (also 720p→1080p), PSNR of screen-like content ≥ 38 dB, orientation after scaling.
Honest note: on this machine the old settings were not visibly blurry on synthetic content; the gain is mainly bitrate/profile/resolution headroom — it could not be compared on the owner's GPU.

**UI:** complete icon set redrawn (SVG-path line icons, one weight, 24-px grid; `IconSet`/`SvgPath`/`Icons`), light/dark palettes with WCAG contrast tests, Segoe UI Variable, a flat Fluent-style renderer for all toolbars/menus,
new command bar (icon-only + accent Capture button, Arrange/Picture menus), grouped `ToolPalette`, zoom in the status bar, restyled history strip / colour picker / library / video page, Settings with a left navigation (`SideNav`) and accent Save.

**Found while doing it:** GDI+ scaling of every captured frame is too slow for 1080p30 (use the transcoder); the old `Theme.Recolor` only ran in dark mode — now both, so colour-chip buttons and note labels need tags (`SwatchTag`, `NoteTag`);
heavy encode/perf tests are flaky when run in parallel → `[Collection("Heavy")]` runs them alone; a custom control needs its own `AccessibleObject` children to be usable with a screen reader.

**Open:** other secondary dialogs (About, Trim GIF, Grab Text, Parameter dialogs) only get the palette/fonts/primary button, not a redesign; native combo boxes/text boxes keep the system look; a human look at the real app at 125–200 % DPI is still pending (tests render at 100 %).

## 2026-10-04 (later) — ONE window: library, editor, history and video editor merged
**State:** 0 warnings; **239 tests pass (~60 s)**; live e2e **18/18** (new scenario J: the main window is visible, steps aside during a capture, returns after it).

**Owner's request:** two windows (capture/library and editor) → one; "Open library" shows the whole history; everything else inside the editor.

**Done:** `EditorForm` is now the single main window (`AppHost.MainWindow`) with three pages — Editor, Library (`LibraryView`, was `LibraryForm`), Video (`VideoEditorPage`, was `VideoEditorForm`).
Captures, history tiles, library items, video recordings and video frames all load INTO it (the editor's document is swapped in place; "Save changes?" first, Cancel keeps the picture).
Capture menu everywhere (menu bar, editor toolbar, library page via `Ui/CaptureMenu`), Library/Back buttons, window hides itself during capture/recording, X hides to the tray and resets, recovered pictures go to the library.

**Found while doing it:** menu shortcuts of a hidden page still fire unless `ProcessCmdKey` blocks them (`_editorShortcuts`); a fabricated `Message()` can't trigger menu shortcuts in tests (use `Message.Create` with the form handle);
Windows keeps a background app's window behind others on `Activate()` — `ShowMain` forces it to the front; the e2e "PrintWindow" scenario can no longer target the app's own window (it hides during captures) → it captures the test window.

**Open:** the old per-window model is gone, so there are no side-by-side pictures (a tabbed editor would be the next step); `TrimForm` (GIFs) and Settings/About/GrabText are still separate dialogs/windows; live click-through of the merged window by a human not done (the tests drive it programmatically).

## 2026-10-04 — Video fixes + in-app video editor + settings entry points
**State:** 0 warnings; **230 tests pass (~48 s)**; live e2e 17/17.

**Owner's reports:** (1) recorded video is upside down, (2) wants to edit video inside the app, (3) could not find the hotkey settings.

**Done:** (1) root cause = Media Foundation reads RGB32 bottom-up; `VideoRecorder` now writes rows reversed (regression test records a red-over-blue clip and reads the poster frame back; it failed first).
(2) `VideoEditorForm`: play/pause/scrub, timeline selection → keep / cut out, undo/redo, rotate/flip, crop (draw on the picture), mute, text captions (timed, position, size, colour), grab frame → image editor, export as new MP4 with progress + cancel.
Engine: `VideoEditPlan` (Core), `VideoEditor` + `VideoPlayback` (Platform). (3) editor menu "Settings" (Capture hotkeys / Recording options / Profiles / All settings), "Hotkeys" button in the action bar, `ShowSettings(owner, SettingsTab)`.

**Found while doing it:** `VideoTransformEffectDefinition` fails on `MediaClip` (0xC00DA7FC) but works on `MediaTranscoder.AddVideoEffect`; it applies rotate → mirror → crop in output space (so the plan stores crop/flip in "view" coordinates);
the shell thumbnail of an MP4 is NOT frame 0 (use `VideoEditor.GetFrameAsync` in tests); `StatusStrip` needs `ToolStripStatusLabel` for `Spring`.

**Open:** no speed change / audio level / trim handles on the picture itself; preview decodes frames one by one (fine up to ~1080p, heavy 4K clips will drop frames); the old two-slider `TrimForm` now serves GIFs only (its video branch is unused); older recordings stay flipped until fixed with Flip vertically; live click-through of the video editor window and audible check not done.

## 2026-10-03 (evening) — Editor layout, icons, history strip
**State:** 0 warnings; **187 tests pass (~24 s)**. Not re-run: live e2e (capture code untouched; only the editor window changed).

**Done (owner's request: ugly/unclear icons, redo layout, show captured-image history inside the editor):**
tool icons redrawn as coloured pictograms (`ToolIcons`: pencil, arrow, bucket, pipette, mosaic, spotlight…), action bar regrouped with a caption under every icon, tool box as a 2-column grid of big buttons,
new `HistoryStrip` under the canvas (recent library items, click opens, live-updates), new `ActualSize` icon, Vietnamese strings, `IconSheetTests` (renders every icon to a contact sheet with `SNAPGIT_SHOTS`), `EditorShowsRecentCapturesInItsHistoryStrip`.

**Found while doing it:** `GenericTypographic`'s `LineLimit` bit again — big glyph icons (Text/Bold/Italic) drew nothing (flag cleared in `Icons.DrawGlyph`); a leftover Debug `SnapGit.exe` locks the build output (stop it before `dotnet build`).

**Open:** clicking a history tile opens a new editor window (switching the current window's document would need an "unsaved changes" flow); the style bar is an empty band while the Select tool is active (could be hidden).

## 2026-10-03 (later) — Milestone 3: organising, profiles, trimming
**State:** 0 warnings; **184 tests pass in ~16–19 s**; live e2e 17/17 (clean run, no retry). One UI smoke test failed once while the machine was heavily loaded and could not be reproduced in 12 further runs.

**Added:** group/ungroup (`GroupAnnotation`, children restored *in place* on undo), align (6) + distribute (2), cut/copy/paste of objects through the clipboard (own format + a picture; Ctrl+C copies the selection or, with nothing selected, the whole image; Ctrl+Shift+C always the image),
gradient fills (`GradientTo/GradientAngle`), text outline, capture profiles (`CaptureProfile` → `CaptureOptions` per capture), GIF trim (`GifTrimmer`, live preview/play) and video trim (`VideoTrimmer`, Windows media composition), 13 new icons, Vietnamese strings, UI smoke coverage for all of it.

**Found while doing it:** GDI+ `StringFormat.GenericTypographic` carries `LineLimit` — text draws NOTHING when its box is shorter than one line (flag removed); `Color` records compare *names*, so style round-trip tests must compare the serialised form/ARGB; group z-position = top-most member; the e2e run is disturbed by anything else using the mouse (it retries a failing scenario once and reports it).

**Next:** webcam overlay, eraser/custom palettes/toolbar customisation, tabbed editor, compile-check the installer, the owner's own features.

## 2026-10-03 — Milestone 2: Snagit parity push
**State:** solution builds 0 errors / 0 warnings; **163 tests pass in ~15 s**; live e2e `tools/smoke/e2e.ps1` **17/17** (one scenario needed a reported retry);
published single-file exe (25 MB) starts and registers all hotkeys.

**Added** (details in ROADMAP): scrolling/panorama capture, Grab Text (OCR), GIF + MP4 recording with audio, free-hand + multi-area capture, PrintWindow, control-level
selection, magnifier, delay, fixed size, repeat last · editor with 19 tools, multi-select, z-order, 14 annotation types, image menu (canvas/resize/effects/cut out/smart move),
`.snapgit` projects + autosave/recovery · library with tags/favourites/search/filters/GIF+video thumbnails, share, PDF, batch, combine, step document · dark theme,
Vietnamese UI, accessibility names, update check, icon, publish script + Inno script · `SNAPGIT_DATA` override, e2e + UI smoke tests.

**Bugs found by running the real thing (all fixed, all have regression tests)**
- `VideoRecorder.Dispose()` → `Cancel()` deleted the finished MP4 (unit test checked the file before disposing).
- Free-hand overlay: `GraphicsPath.AddPolygon` throws with 2 points (first moments of a drag).
- Repeat-last ignored free-hand/multi-area (re-opened the overlay).
- Exports used `CompositingQuality.HighQuality` (gamma blend): exported image differed from the canvas.
- OCR: Windows returns word fragments; greedy merging depended on input order → connected components (`OcrLayout`).
- Scroll stitcher: (1) a 24-px blinking caret or a window border column that changes shade defeated whole-row hashes; (2) the first tolerant fix then mis-aligned sparse
  text and **dropped 3 lines** → rows are hashed in 24 segments and scored only on informative (non-background) segments. Result is now pixel-exact (7217 px / 300 lines).
- Library file names took a control's *text* as title (edit box content) → top-level window title, line breaks flattened.
- A UI test "passed" after hanging 2 minutes on a modal "Save changes?" dialog → 45 s timeout now fails the test.

**Known gaps / next steps (priority order)**
1. Capture profiles/presets; webcam overlay + recording trim.
2. Compile & test the Inno installer on a machine with Inno Setup; MSIX if wanted.
3. Group/align/distribute, copy-paste of annotations, gradient fills, text outline, tabbed editor.
4. Click-through of the Windows share sheet and a listening test of recorded audio (not automatable here).
5. Sub-folder collections; cloud destinations once a credential store exists.
6. Anything from "Beyond Snagit" in the roadmap — the owner's own features.

## 2026-10-02 — Milestone 1: foundation + capture + editor + library
Solution layout, docs, skills; region/window/full-screen capture, 11 editor tools, library, settings, tray; 45 tests. Bugs then: `Enum.TryParse("1")` hotkey,
`CopyFromScreen` + CAPTUREBLT, PowerShell UTF-8 corruption, PrintScreen claimed by Snipping Tool/Greenshot.
