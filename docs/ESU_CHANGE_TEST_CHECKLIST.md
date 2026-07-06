# ESU Change Test Checklist

This is the coarse living checklist for ESU changes. Use it as the default smoke plan before handing a build to another tester, especially when touching HUD modes, serialization, save/load, or Smart Block Builder.

## Required Commands

Run these from the repository root:

```powershell
$env:FTD_DIR='C:\Program Files (x86)\Steam\steamapps\common\From The Depths'
dotnet build .\EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj -c Release
dotnet run --project .\tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj -c Release -p:NoWarn=MSB3277
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## v1.0.7 Focused Smoke Course

Use this short course for the current v1.0.7 development build before a longer
release pass.

1. Start FtD with only ESU and normal test mods enabled.
2. Confirm the main-menu Alerts row for ESU is readable and uses the cyan ESU
   HUD accent for update notices.
3. Enter build mode on a small craft with decorations, slopes, spin blocks, and
   at least one subobject.
4. Open Decoration Edit Mode.
5. Verify toolbar icons are ESU-owned/fitting icons, not unrelated sprites from
   another mod.
6. Box-select several decorations on the same construct.
7. Confirm the `Pivot` button is always visible left of `Single`, disabled for
   single/no selection, and enabled for multi-selection.
8. Click an already selected decoration and confirm the multi-selection outline
   remains while the clicked decoration becomes primary.
9. Shift-click and Ctrl-click rows in the Outliner and Selected anchor panel;
   confirm additive/range selection behaves like a normal list.
10. Paint a multi-selected group from the swatches, typed color field, and paint
    tool; confirm the whole group changes in one undoable step.
11. Move, rotate, axis-scale, and uniform-scale the selected group with each
    pivot mode: Bounds, Average, Selected, and Anchor.
12. Type `0.1, 0.1, 0.1` in the normal Scale XYZ fields with a group selected
    and confirm the entire group scales around the selected pivot.
13. Toggle symmetry and confirm mirrored decoration preview/anchors are visible,
    mirrored scale/rotation match, and deleting one side deletes its counterpart.
14. Open Surface Builder.
15. Build triangles on a normal block, a spin block, and an angled subobject.
16. Toggle normal flip and confirm preview and applied decorations match.
17. Use Create Face, Same anchor, and right-click context actions; confirm they
    do not unexpectedly enter material preview mode.
18. Select a paint color and material override; click Preview and confirm the
    preview resembles the placed decoration material/color before Apply.
19. Open Smart Builder.
20. Switch the right panel between `Shapes` and `Generators`.
21. Create Circle, Polygon, Triangle, Hex, Octagon, Decagon, and Sphere
    generators in Shell and Filled modes.
22. Resize each generator with face, edge, and corner handles; confirm round-lock
    circles/spheres stay centered and scale predictably.
23. Toggle Wireframe, Material, and Mat only preview modes.
24. Create a large sphere shell, around 28 x 28 x 28, and confirm the editor
    remains responsive while the shape-aware hull still reads as a sphere.
25. Trigger Smart Builder blocked/occupied warnings and confirm they use the
    shared ESU notification/log UI instead of clipped status text.
26. Apply/cancel generated pieces, then undo/redo through FtD.
27. Save, reload, and confirm committed blocks/decorations persist.

## Startup And Options

- Game reaches main menu with ESU installed and no required-patch popup.
- ESU options tab opens in the vanilla options menu.
- Defaults stay safe for normal players: vanilla compatibility and fast-load features remain opt-in.
- Changing an ESU option updates the visible option state without needing to reopen the menu.
- Runtime logs and diagnostics are created only when the matching setting is enabled.

## Mode Switching

- Toggle Decoration Edit Mode.
- Toggle Surface Builder.
- Toggle Smart Block Builder.
- Toggle Automation Editor.
- Switch between ESU modes with Tab while clean.
- Confirm mode switching is blocked with a visible warning while Apply/Cancel work is pending.
- Confirm Close/Cancel restores normal FtD input and camera behavior.

## Automation Editor

- Open Automation Editor with `Ctrl+Shift+A` and confirm the ESU toolbar,
  left status panel, right controller palette, filters, and target list appear.
- Confirm the Automation status panel, controller palette, and graph/code editor
  have Decoration/Smart-style resize grips, clamp to the screen, and leave a
  full-width bottom status strip visible.
- Confirm the bottom strip and left `Workspace guide` report the current stage,
  native data surface, next safe action, safety note, and planned System Block
  vocabulary while selecting, placing, linking, and editing.
- Open the graph/code editor from the toolbar, left panel, linked-target
  inspector, and `Controllers on craft` list. Confirm it fits the central
  viewport between the side panels and the bottom strip, and that `Fit` restores
  that layout after dragging or resizing.
- Place a Bread Board, AI Breadboard, ACB, ACB Controller, and Missile
  Breadboard Controller from the right palette where the current FtD install
  exposes those items. Confirm the palette button changes to `placing`, the
  bottom strip reports the armed block, green placement preview cells are empty,
  amber preview cells are blocked, panel clicks do not place blocks, and any
  rejected placement writes an Automation Editor runtime-log warning.
- Use the right-panel `Controllers on craft` list grouped by main
  construct/subconstruct to select and open existing automation controllers.
- Select each controller and run the left-panel `Run checks` action. Capture the
  ESU runtime log detail for any warning or failure.
- After `Run checks` on a Breadboard, confirm the `Live evidence gates` rows
  separately report the native graph fingerprint, Generic proxy fingerprint, and
  Switch fail-expression readiness instead of relying only on the runtime log.
- Use `Target search` with a block label, runtime class fragment, category name,
  controller GUID fragment, role text, and cell coordinate. Confirm filter
  counts and the world target list update together.
- Cycle the `ACB Actions`, `Breadboard Read`, `Breadboard Write`, `Movement`,
  `Subobjects`, `Utility`, and `Media` filters and confirm target rows show
  matching role summaries.
- Select a Breadboard through overlapping blocks and confirm its cyan wireframe
  stays visible while affected blocks light up under the active filter.
- Link spin block, propulsion, weapon/turret, light, shield/defence, AI,
  missile, and ordinary ACB targets where the test craft has them. Confirm world
  link lines and the linked-target list update.
- Modify or undo a linked target while Automation Editor is open and wait for
  the target refresh. Confirm links either rebind to the live target or remain
  visible as `(missing)` until removed.
- Save/reload a craft with existing Generic Getter/Setter proxy nodes, select
  the Breadboard, and confirm the linked-target list rehydrates from persisted
  native proxies without manual relinking.
- Inspect a linked target in the left panel and confirm category/roles/runtime/
  cell, proxy hints, reflective warning text, `Open graph`, and `Remove link`
  work for live and missing targets.
- In the Breadboard graph page, create Getter/Setter/Both proxy nodes for linked
  world targets, create a Button getter for an ACB Controller output, and create
  Rule getter/setter proxies for a linked ACB.
- Confirm linked target cards also expose in-card proxy buttons and visible
  auto-pick terms before opening the full native proxy property picker.
- Confirm common target proxies auto-select a likely FtD property where exposed
  for spinblocks, propulsion, weapons/turrets, ACB rules, lights, doors/docking,
  detection, and resource/power blocks; use the picker to correct mismatches.
- In the Breadboard graph page, edit a linked ACB node's enabled state,
  priority, search pattern, condition, action, and numeric values. Edit a linked
  ACB Controller node's first buttons, including name, keyword, Breadboard
  output flag, shape, and color.
- Confirm the graph/code editor bottom strip reports page, link count, grid
  state, status text, immediate native edit behavior, and generated-node revert
  availability.
- Use the proxy property picker on created Generic Getter/Setter nodes and
  confirm FtD exposes a sensible property list for the selected target type.
- Run `Run checks` again after creating proxy nodes and confirm the runtime
  diagnostics report extended component palette coverage and Generic
  Getter/Setter property-picker enumeration.
- Confirm `Prepare validation graph` is unavailable or warns until the selected
  Breadboard has at least one writable linked target.
- Use `Prepare validation graph` on a Breadboard with a writable linked target.
  Confirm it loads the `Validation proof` recipe, creates
  expression-else Switch proof nodes, binds output through a target-specific
  Generic Setter proxy, and immediately refreshes the live evidence rows.
- Confirm `Capture baseline` and `Compare baseline` stay unavailable until all
  live evidence gates are OK.
- Run `Run checks`, use `Capture baseline`, save/reload, run checks again, and
  use `Compare baseline`. Confirm it reports an Automation validation baseline
  match for the `Native persistence fingerprint`, `Generic proxy fingerprint`,
  and Switch fail-expression readiness lines.
- Quick-add Comment, Constant, Evaluator, Switch, Logic, Getter, Setter, PID,
  Threshold, Clamp, Delay, Sum, Multiply, Variable Reader, and Variable Writer
  nodes where the board advertises them. Move, delete, connect, and clear native
  ports from the ESU canvas.
- Use the `Shared variable bridge` Pair action and confirm it creates native
  Variable Reader/Writer bridge nodes together where the board advertises them.
- Use the Code page recipes, compile an expression, a numeric-else if/else
  recipe, and an expression-else if/else recipe. Confirm linked identifier
  buttons insert evaluator-safe names for the selected proxy targets. Verify
  native Evaluator/Switch/Logic Gate nodes appear where FtD exposes the needed Switch
  inputs, select a Code output target, and confirm compiled output binds to that
  target's Generic Setter input. When no setter exists, confirm compile creates
  the target setter proxy and `revert compile` removes the auto-created proxy
  with the generated code nodes.
- Edit ACB enabled state, priority, delay, range, minimum interval, search
  pattern, condition/action type, condition inversion, and condition/action
  values. Trigger a test only on a disposable craft.
- Edit an ACB Controller button name, keyword, Breadboard output flag, shape,
  and color, then create a Breadboard getter that reads the keyword output.
- On Missile Breadboard Controller, quick-add missile-specific output/fuse
  components that the live board advertises.
- Save, reload, and confirm placed controllers, ACB edits, ACB Controller button
  edits, Breadboard graph nodes, wires, and compiled code nodes persist.

## Decoration Edit

- Select existing decorations in viewport and outliner.
- Move, rotate, scale, anchor, paint, and view-filter decorations.
- Apply and undo changes.
- Save and reload the construct.
- With Vanilla Compatibility Mode on, verify blocked saves show an ESU popup and log entry.

## Surface Builder

- Create triangle faces from three craft-surface points.
- Preview, place, delete, and bridge surfaces.
- Test right-click menus for point, edge, and face targets.
- Test X/Y/Z symmetry and multi-axis symmetry.
- Place right, isosceles, and scalene triangles with normal flip on and off.
- Confirm mirrored placed decorations face the same intended side as the preview.
- Save, reload, and visually inspect surface normals.

## Smart Block Builder Baseline

- Open Smart Builder from build mode and from ESU mode switching.
- Select material, draw plane, occupancy mode, symmetry, handles, and preview mode.
- Place an independent shape on empty construct space.
- Snap a new shape to an existing construct block.
- Snap a new shape to an existing preview piece.
- Move, scale, yaw, flip, duplicate, delete, undo, and redo.
- Test Skip occupied and Block occupied behavior.
- Apply, undo through FtD, save, reload, and confirm committed blocks match preview.

## Smart Builder Shape Checklist

Run this for every newly enabled shape family or variant:

- Catalog discovery finds the correct FtD `ItemDefinition` from `DragSettings.Geometry` and `SizeInfo`.
- Palette label and category are clear.
- Unsupported material/geometry variants are hidden or marked unsupported, not guessed.
- Preview footprint matches `SizeInfo.GetPosition` coverage.
- Rotate and flip keep the item footprint aligned to the voxel grid.
- Scaling repeats whole items on voxel strides; it must not distort fixed geometry.
- X/Y/Z symmetry mirrors position and rotation.
- Odd-axis symmetry swaps handed left/right variants when required.
- Missing mirror replacement invalidates the whole atomic plan.
- `Skip` omits only conflicting placements.
- `Block` prevents Apply on any occupied footprint.
- Apply uses vanilla `PlaceBlockCommand`.
- Undo removes the committed blocks.
- Save/reload preserves the final FtD blocks.

## Fast Blueprint Loading

- Off path loads as vanilla.
- V1 uses streamed JSON only when routed by settings and size.
- V2 runs only for meaningful `BlockData` size/record count or Force V2.
- V3 remains explicit and opt-in.
- Unsafe probes are timing-only, correctness-invalid, and must never be saved from.
- Diagnostics logs include route decision, V2 skip/active state, V3 timing, total load time, block count, and error rows.

## Packaging And Deployment

- `build.ps1` produces the expected mod package.
- Local mod folder has the latest DLL, metadata, docs, and bundled files.
- Steam Workshop zip/package contains no development-only artifacts unless intended.
- A fresh game launch with the packaged mod matches the repository build.

## Evidence To Capture

When reporting a test result, capture:

- ESU version and FtD version.
- Settings used.
- Blueprint/craft name.
- Log file name.
- Total load/save/apply time if relevant.
- Whether Apply/Save/Undo/Reload succeeded.
- Any ESU popup text.
- Any FtD game-log errors.
- Screenshots for visual or normal/shape issues.
