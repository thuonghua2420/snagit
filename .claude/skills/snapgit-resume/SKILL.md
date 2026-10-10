---
name: snapgit-resume
description: Reload SnapGit project context after /clear or at the start of a session — reads CLAUDE.md, architecture, roadmap and progress log, verifies build+tests, and proposes the next task. Use first whenever you start working on SnapGit.
---

# Resume work on SnapGit

1. Read, in order: `CLAUDE.md`, `docs/PROGRESS.md` (latest entry = current state + next steps), `docs/ROADMAP.md` (done / partial / not done, with reasons).
   Read `docs/ARCHITECTURE.md` when the task touches capture flow, scrolling, recording, the editor or the library; `docs/TESTING.md` before writing tests.
2. Verify the baseline (tells you whether the last session left things broken):
   ```
   (stop any running SnapGit.exe first — it locks the DLLs)
   dotnet build SnapGit.sln -nologo -v q
   dotnet test  SnapGit.sln --no-build -nologo -v q
   ```
   Expected: 0 errors, 0 warnings, all ~165 tests pass in ~15 s. If not, fix that before new work.
3. `git status --short` / `git log --oneline -5` (the repo may have no commits; never commit unless the user asks).
4. Tell the user in 3–5 lines: current state, the top "next steps" from PROGRESS.md, and ask which to take if they haven't said.
5. Pick the matching skill: `snapgit-add-annotation-tool`, `snapgit-add-capture-mode`, `snapgit-localize` (any user-visible text!), `snapgit-e2e` (live desktop test), and finish with `snapgit-verify-and-log`.

Rules to remember (details in CLAUDE.md): all P/Invoke in `Platform/Native/NativeMethods.cs`; every edit is an `IUndoableCommand`; every visible string through `Loc.T`;
never rewrite source with PowerShell Get-Content/Set-Content; never screenshot the whole desktop; a regression test must be seen failing without its fix.
