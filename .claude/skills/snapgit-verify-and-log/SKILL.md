---
name: snapgit-verify-and-log
description: Finish a SnapGit work session — build with zero warnings, run all tests, run the live e2e when behaviour on Windows changed, update ROADMAP/PROGRESS docs. Use before reporting a SnapGit task as done or before the user clears the context.
---

# Verify and log

1. **Build + tests** (stop any running `SnapGit.exe` first):
   ```
   dotnet build SnapGit.sln -nologo -v q            # 0 errors, 0 warnings required
   dotnet test  SnapGit.sln --no-build -nologo -v q # all green, ~15 s
   ```
   `dotnet test` doesn't rebuild the App exe for the e2e — always build the solution first.
2. **Regression tests**: for every bug fixed, a test that FAILS without the fix (temporarily disable the fix to prove it, then restore).
3. **Windows behaviour changed** (capture, overlay, scrolling, OCR, recording, library)? run `tools\smoke\e2e.ps1` (skill `snapgit-e2e`) and read the failures — don't just rerun.
   UI changed? `$env:SNAPGIT_SHOTS` + `UiSmokeTests` renders the forms to PNG so you can look at them offscreen.
4. **Clean up**: delete artifacts you created (`artifacts/`, temp folders, `%USERPROFILE%\Pictures\SnapGit` only if YOU made it), kill test processes. Never leave `SnapGit.exe` running.
5. **Docs**: tick `docs/ROADMAP.md` honestly ([x] only if verified; say what is missing for [~]); add a dated entry on top of `docs/PROGRESS.md` (state, what changed, bugs found & fixed, next steps, what was NOT verified). Update `CLAUDE.md`/`docs/ARCHITECTURE.md`/`docs/TESTING.md` if structure, commands or conventions changed.
6. **Report honestly**: what was verified (and how), what was not, what is left. Don't commit unless the user asks.
