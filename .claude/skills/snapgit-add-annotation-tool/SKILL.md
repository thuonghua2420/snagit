---
name: snapgit-add-annotation-tool
description: Step-by-step checklist for adding a new annotation type and editor tool to SnapGit (e.g. a new shape, badge, effect or pixel tool). Use when adding anything the user can draw or place on an image in the editor.
---

# Add an annotation + tool

Reference implementations: `Annotations/ShapeAnnotations.cs`, `CalloutAnnotation.cs`, `EffectAnnotations.cs` and `Tools/DragShapeTool.cs`, `Tools/PixelTools.cs`.
Files are in `src/SnapGit.Editor` unless noted.

1. **Annotation class** (new file in `Annotations/`):
   - Base: `BoxAnnotation` (rect + 8 handles, drag-created), `LineAnnotation` (2 points), or `Annotation`.
   - `DrawCore(Graphics g, RenderContext ctx, AnnotationStyle style)` — draw with the `style` parameter (NOT `this.Style`: the drop-shadow pass re-uses it) and `colour.WithOpacity(style.Opacity)`. Effects that read pixels use `ctx.BaseImage`.
   - `HitTest`, `Translate`, `CopyStateFrom` (copy EVERY mutable field — undo depends on it), `OnCloned` if it owns reference-type state, `SupportsShadow => false` for effects.
   - Convention: `Stroke` = outline/text colour, `Fill` = interior colour. A new style property goes into `AnnotationStyle` (immutable record) and is ignored by types that don't use it.
2. **Register for project files**: add `[JsonDerivedType(typeof(YourAnnotation), "yourname")]` on `Annotation` (Annotation.cs). Containers of annotations (like `GroupAnnotation`) must restore children IN PLACE in `CopyStateFrom` and renew ids on paste (`EditorContext.RenewIds`). Anything non-serialisable (Bitmap...) needs `[JsonIgnore]` + a string/base64 property (see `ImageLayerAnnotation`). Extend `EveryAnnotationTypeRoundTripsThroughJson`.
3. **Tool**: drag-created → `DragShapeTool` (annotation implements `IDragCreatable`). Otherwise derive from `EditorTool`; commit with `context.Document.History.Execute(...)` on mouse-up — ONE command per gesture. Pixel operations: `doc.ApplyImageOperation(name, op, flatten, shift)`.
4. **Register the tool**: `Tools/ToolKind.cs` (value), `Tools/ToolFactory.cs`, `Tools/ToolDefaults.cs` (initial style).
5. **UI** (`src/SnapGit.App`): same name in `Ui/UiIcon.cs` (tool block mirrors ToolKind names) + an entry in `Ui/IconSet.cs` (16 px colour icon = list of `Piece`s with SVG path data on a 16 px grid: its own hue, 1 px darker outline (`Box`/`Line` helpers align to .5), gloss on bodies — copy the style of a neighbour such as Rectangle/Pen; `IconAndThemeTests` fails for a missing/off-grid/near-empty icon; look at the result with `SNAPGIT_SHOTS=<dir>` → `icons-light.png`/`icons-dark.png` from `IconSheetTests`, which shows 16 px, 32 px and disabled) and a `PaletteTool` in `BuildToolBox` (`EditorForm.Ui.cs`, pick the group it belongs to); entry in `ToolDefinitions` and `Hints` of `Forms/EditorForm.cs` (use `Loc.N("...")`, pick a free shortcut letter — taken: V A L R E P H T Q N M B S G I F C X W); style-bar relevance in `CurrentRelevance()` and `ToolKindOf()`.
6. **Strings**: every new visible text via `Loc.T`/`Loc.N` + Vietnamese entry (skill `snapgit-localize`).
7. **Tests** (`tests/SnapGit.Tests`): add→undo→redo, hit-test, a render pixel check (`doc.Render().GetPixel`), a tool gesture test (see `ToolsCreateUndoableAnnotations`), JSON round trip. `UiSmokeTests.EditorFormBuildsAndPaintsEveryAnnotationType` should also draw it (and it iterates `Enum.GetValues<ToolKind>()` automatically).
8. Finish with `snapgit-verify-and-log`; tick `docs/ROADMAP.md`.

Pitfalls: never mutate the document outside a command (except live preview during a drag); `Annotation.Clone()` keeps the same `Id` (duplicate sets a new one); text measures and draws with `StringFormat.GenericTypographic`; export uses default compositing (don't set `CompositingQuality.HighQuality`).
