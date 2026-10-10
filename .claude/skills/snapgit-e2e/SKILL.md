---
name: snapgit-e2e
description: Run and extend SnapGit's live end-to-end acceptance test (real desktop, synthetic hotkeys/mouse, checks produced files). Use after changing capture, scrolling, OCR, recording, overlay or library behaviour, and when a unit test cannot prove the Windows behaviour.
---

# Live e2e (`tools/smoke/e2e.ps1`)

What it does: starts SnapGit with `SNAPGIT_DATA` pointing at a temp folder (your real settings/library are never touched), opens a scrollable test window
(`testwin.ps1`: 300 numbered lines, 24 px each, at 120,120 on the PRIMARY monitor), then drives SnapGit with `keybd_event`/`mouse_event`:
A region · B scrolling (must be 7100–7400 px tall) · C OCR (clipboard text, rows kept together) · D GIF · E MP4 (kept after dispose) · F free-hand (transparent notch) · G multi-area · H repeat-last · I PrintWindow.

```
dotnet build SnapGit.sln                       # first! the script runs the Debug exe
powershell -NoProfile -File tools\smoke\e2e.ps1                          # all, ~1.5 min — keep your hands off mouse and keyboard
powershell -NoProfile -File tools\smoke\e2e.ps1 -Only B,G -KeepFiles     # chosen scenarios; keeps the temp folder and prints the app log
```
Exit code = number of failed checks. A failing scenario is retried once and the `[RETRY]` line is printed — synthetic input is disturbed by anything else using the mouse.

Debugging aids: the app logs `capture …`, `overlay …`, `scroll: frame n, notches, outcome, shift`, `notify: …` at INFO (`<SNAPGIT_DATA>\logs`). `SNAPGIT_DEBUG_FRAMES=<dir>` dumps every scrolling frame —
diff two frames pixel-by-pixel to see which columns/rows change (that is how the border-column and caret problems were found).

Extending: add a `Test-X-Name` function to `e2e.ps1` using helpers from `lib.ps1` (`Send-Hotkey`, `Drag-Mouse`, `Click-At`, `Scroll-Wheel`, `Get-Projects`, `Read-ProjectImage`, `Wait-Until`, `Assert-Check`) and register it in `$scenarios`.
Judge results by pixels/content, not just sizes. Never screenshot the whole desktop; if you need to look, capture one window (`SnapWin.VisibleWindows` gives rectangles per process).
