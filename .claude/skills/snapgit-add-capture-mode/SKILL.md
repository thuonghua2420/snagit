---
name: snapgit-add-capture-mode
description: Checklist for adding a new capture mode, hotkey action or recording kind to SnapGit (e.g. a new way to pick or build the captured image). Use when changing how/what gets captured or adding a global hotkey.
---

# Add a capture mode / hotkey action

Reference: `CaptureWorkflow` (App/Services), `CaptureOverlayForm` + `OverlayMode`, `CaptureComposer` (Core), `ScrollingCapture`, `RecordingService`.

1. `Core/Models/CaptureMode.cs` — add the value (append only). `Core/Hotkeys/HotkeyAction.cs` — add an action (append only; persisted by name).
2. `Core/Settings/AppSettings.cs` — default gesture in `DefaultHotkeys()`. **Probe that it can be registered on this machine first** (PrintScreen, Alt/Ctrl/Shift+PrintScreen, Ctrl+Alt+S/W are taken by other apps here; 1–9, L, R work). `EveryHotkeyActionHasAUniqueDefault` enforces uniqueness. Mode options → new settings property (+ `SettingsForm` control + `Loc`).
3. Picking the area: reuse `CaptureOverlayForm`; a new interaction = new `OverlayMode` value + paint/mouse handling there, returning an `OverlayResult`.
4. Building the image: pure geometry/compositing belongs in `Core/Imaging` with unit tests (like `CaptureComposer`, `ScrollStitcher`). New screen/Win32 access goes behind `IScreenGrabber` (Core) / `ScreenGrabber` (Platform); new P/Invoke ONLY in `Platform/Native/NativeMethods.cs`.
5. `App/Services/CaptureWorkflow.cs` — handle the mode in `CaptureAsync` (keep the `_busy` guard, set `_last` for repeat-last, log with `Log.Info`). Recording-like features follow `RecordingService` + an `IRecorder`.
6. `App/Services/AppHost.cs` `Dispatch` → hotkey action; entry points: tray menu (`TrayApplicationContext`), the shared Capture menu (`Ui/CaptureMenu.cs` — used by the main window's menu bar, editor toolbar and library page), hotkey row in `SettingsForm.HotkeyRows`.
7. Strings via `Loc.T` + Vietnamese (skill `snapgit-localize`).
8. Verify: unit tests for the pure part; add a scenario to `tools/smoke/e2e.ps1` (skill `snapgit-e2e`) and run it; check a non-primary monitor / mixed DPI by hand if the overlay changed.
9. `snapgit-verify-and-log`; tick `docs/ROADMAP.md`.

Invariants: the overlay must never appear in the image (snapshot first); HUD windows (countdown, record bar, progress) derive from `HudForm` (no focus steal, excluded from capture); exceptions are logged and surfaced through `AppHost.Notify`, never crash the tray process; nothing deletes a finished file.
