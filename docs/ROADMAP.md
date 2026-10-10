# Roadmap — Snagit parity checklist

Legend: [x] done and verified · [~] partial (what is missing is stated) · [ ] not done (reason stated)
"Verified" = unit/integration test, UI smoke test or the live e2e script (`tools/smoke/e2e.ps1`).

## Capture
- [x] Region + window/control hover ("All-in-One"), wheel = parent/child control, window-only, full screen (monitor under cursor), multi-monitor + mixed DPI
- [x] Global hotkeys (configurable, conflict reporting), cursor option, frozen-snapshot overlay with size label
- [x] Magnifier loupe (exact pixel, coordinates, colour), arrow keys move the cursor 1/10 px
- [x] Capture delay (countdown HUD), fixed-size region, repeat last capture (incl. free-hand/multi-area shapes)
- [x] **Scrolling capture** vertical + horizontal: auto-scroll, adaptive step, sticky header/footer, pixel-exact on a 300-line test (e2e)
- [~] Panoramic: horizontal auto-scroll works; a true 2-D (both axes, user-driven) panorama is not implemented
- [x] Window capture via PrintWindow (covered/off-screen windows, invisible border trimmed) — setting
- [x] Menu/tooltip capture (via delay), free-hand region (transparent outside), multi-area (combined, transparent gaps)
- [x] Capture profiles (named presets: mode, delay, fixed size, cursor, clipboard, editor) — Settings → Profiles, run from the tray/library menu; (no per-profile hotkey, menu click not covered by the live e2e)
- [x] **Video capture** MP4 (H.264/AAC) with system audio + microphone, GIF capture; floating record bar (excluded from the recording)
- [x] Trim recordings: GIF by frames (live preview/play) → saved as a NEW library item
- [x] **Video editor** in the app: play/scrub, keep / cut out ranges, undo/redo, rotate/flip, crop, mute, timed text captions, grab frame → image editor, export as a new MP4 (opens after a recording)
- [ ] Video editor extras: speed, audio level/fade, trim handles on the picture, webcam overlay, export to GIF, cursor highlight / click effects
- [x] Settings reachable from the editor (menu + Hotkeys button), opening on the right tab
- [~] Recording: no webcam overlay; audio is verified only for "doesn't fail" (no listening test)
- [x] Import files, paste from clipboard, drag & drop into library/editor · [ ] capture from camera (not started)
- [ ] Windows.Graphics.Capture / DXGI backend (HDR, protected content): BitBlt measured at ~90 ms for a 5760×1200 desktop, so not needed yet; `IScreenGrabber` is the seam

## Editor
- [x] Annotation types: 15 (incl. group). Tools (19): select, arrow (curved, 5 head styles), line, rectangle (rounded), ellipse, pen, highlight, text, callout, step, stamp (12), blur/pixelate, spotlight, magnify, eyedropper, fill, crop, cut out, **smart move**
- [x] Multi-select (Ctrl/Shift/marquee), z-order, duplicate, nudge, paste/drop image as a layer, undo/redo for everything
- [x] Style: colours, fill, width, opacity, dash, corner radius, shadow, font family/size/bold/italic/alignment, quick-style presets
- [x] Image menu: rotate/flip, canvas size, resize, trim borders, border, drop shadow, torn edges, grayscale/invert/sepia, brightness/contrast, replace colour, simplify
- [x] `.snapgit` re-editable project (annotations stay editable), autosave + crash recovery, print, export PNG/JPG/BMP/GIF/PDF
- [x] Group/ungroup, align/distribute, cut/copy/paste of objects (works between editor windows, stays editable; also puts a picture on the clipboard), gradient fills, text outline
- [ ] Eraser, custom palettes, toolbar customisation
- [x] Editor layout v2: coloured tool icons, captioned action bar, 2-column tool box, **History strip** of recent captures under the canvas (click opens it)
- [x] **Design system**: compact classic 16 px colour icon set (SVG paths, gloss, exact-size rendering), light/dark palette (WCAG), flat renderer, command bar, one-column 26 px tool palette, collapsible history strip, Settings navigation, Segoe UI Variable
- [x] **Video quality**: Full HD default, HD / original, Standard–High–Best (bitrate per pixel), H.264 High, pooled buffers
- [x] **Single main window**: editor + library + video editor are pages of one window; captures/history/library items load into it in place; the window hides itself while capturing
- [ ] Tabbed multi-image editor (one picture at a time today; history tiles/library items replace the current picture after "Save changes?"), image properties (DPI/canvas colour)

## Text & data
- [x] **Grab Text (OCR)** — Windows.Media.Ocr, language choice, row re-joining, result window, clipboard; also from inside the editor

## Library & output
- [x] Thumbnail gallery for images/projects/GIF/video, date groups, search, type/tag/favourite filters, sort, rename, tags, favourites, bulk delete/copy
- [x] Share: Windows share sheet (email, Teams, nearby…), copy to folder (OneDrive etc.), run program, clipboard file
- [x] Export multi-page PDF, batch convert/resize, combine images (vertical/horizontal/grid), step-by-step document, naming patterns, default format
- [ ] Sub-folders/collections (tags cover most of it) · [ ] Slack/Google Drive/FTP uploads — deliberately skipped: they need stored credentials/OAuth, no secret store yet
- [ ] Share-sheet UI was not clicked through live (compiles, constructs; the destinations logic is tested only for folder/program/clipboard paths)

## App shell
- [x] Tray, single instance, start with Windows, crash log, settings window (5 tabs), About/diagnostics, real icon (.ico), version metadata
- [x] Dark theme + dark title bar, English/Vietnamese UI (test-enforced coverage), accessible names for icon-only buttons
- [x] Update check (JSON feed, notify-only) · [x] `tools/publish.ps1` (verified: 25 MB single file starts) · [~] installer: `installer/SnapGit.iss` written but **not compiled** (Inno Setup not installed here)
- [ ] High-contrast theme pass, screen-reader walkthrough with a real reader, MSIX packaging, auto-install of updates

## Beyond Snagit (the owner's own ideas — add here)
- [ ] _…_
