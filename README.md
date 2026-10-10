# SnapGit

Screenshot, screen-recording and annotation tool for Windows (a Snagit-style app). C# / .NET 8 / WinForms.

## Use
Run `SnapGit.exe` — it lives in the system tray (double-click the icon for the library). Default hotkeys (change them in *Settings → Hotkeys*):

| Hotkey | Action |
|---|---|
| Ctrl+Alt+1 | Capture region / window / control (hover = highlight, wheel = parent/child, click or drag) |
| Ctrl+Alt+2 | Capture a window |
| Ctrl+Alt+3 | Capture the full screen (monitor under the cursor) |
| Ctrl+Alt+4 | Scrolling capture (auto-scrolls and stitches) |
| Ctrl+Alt+5 | Grab Text (OCR → clipboard) |
| Ctrl+Alt+6 / 7 | Free-hand / multi-area capture |
| Ctrl+Alt+8 / 9 | Start/stop GIF / video recording (same key stops) |
| Ctrl+Alt+R | Repeat the last capture |
| Ctrl+Alt+L | Open the library |

Captures are saved as editable `.snapgit` projects in `Pictures\SnapGit` (annotations stay editable); export PNG/JPG/BMP/GIF/PDF from the editor.
UI languages: English, Vietnamese. Themes: light, dark, follow Windows.

## Build, test, publish
```
dotnet build SnapGit.sln
dotnet test  SnapGit.sln --no-build
powershell -File tools\smoke\e2e.ps1          # live end-to-end test on the real desktop (~1.5 min, hands off the mouse)
powershell -File tools\publish.ps1            # artifacts\publish\SnapGit.exe (+ installer if Inno Setup 6 is installed)
```
Requires Windows 10 2004+ and the .NET 8 SDK (runtime for the framework-dependent build). See `CLAUDE.md` and `docs/` for architecture, roadmap, testing and the project skills.
# snagit
