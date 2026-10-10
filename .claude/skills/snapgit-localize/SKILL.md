---
name: snapgit-localize
description: How to add or change any user-visible text in SnapGit so it is translated (English/Vietnamese) and the localisation test stays green. Use whenever you write a label, tooltip, message, hint, menu item or dialog text.
---

# Localisation

The **English text is the key**: `Loc.T("Save project")`. A missing translation falls back to English; a test makes sure that never ships.

1. Write the text through `Loc.T("...")` (with arguments: `Loc.T("Saved {0}.", name)` → `string.Format`). Don't concatenate sentences — one complete string per message so it can be translated.
2. Strings in static tables/initialisers (tool names, hints, presets, built-in share destinations) use `Loc.N("...")` to mark them, and `Loc.T(value)` where they are displayed.
3. Add the Vietnamese entry to `src/SnapGit.Core/Localization/Vietnamese.cs` (`["English text"] = "Tiếng Việt",`). Keep the same `{0}`/`{1}` placeholders and C# escapes (`\n`, `\"`) exactly as in the source literal.
   Terms: capture = chụp · library = thư viện · project = dự án · annotation = chú thích · stamp = nhãn dán · callout = chú thích bong bóng · spotlight = làm nổi bật vùng · magnify = phóng to chi tiết.
4. Run `dotnet test --filter LocalizationTests`: it scans every `Loc.T/Loc.N("...")` literal under `src/` and fails on a missing entry or a placeholder mismatch. Names built at run time (e.g. `Loc.T(kind.ToString())` for stamps) are NOT scanned: add those entries by hand.
5. Don't localise: log messages, exception messages for developers, file extensions, hotkey names, the product name.
6. Language is chosen at start-up (`Settings → General`, "auto" follows Windows) — a change needs a restart. `UiSmokeTests` builds the forms in `vi` + dark to catch layout/exception problems; use `SNAPGIT_SHOTS` to look at the result.
