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

### Implemented smoke tests

- Open Automation Editor with `Ctrl+Shift+A` and confirm the ESU toolbar,
  left status panel, right controller palette, filters, and target list appear.
- Confirm the Automation status panel, controller palette, and Automation editor
  have Decoration/Smart-style resize grips, clamp to the screen, and leave a
  full-width bottom status strip visible.
- Confirm the bottom strip and focused page controls report the current stage,
  native data surface, next safe action, safety note, compatibility label, and
  ESU Blocks state while selecting, placing, linking, and editing.
- Confirm the focused Graph, Code, ESU Blocks, and System Block pages repeat the
  current `Next` action and compatibility label near the page controls so the
  safe action remains visible while editing.
- On the Code page, confirm the `Recipe lowering` guide names the one-expression
  and four-line if/else recipe shapes, native Math Evaluator / Logic Gate /
  Switch / Generic Setter lowering, and the no-arbitrary-runtime rule.
- In the Code page recipe catalog, search by recipe name, target category, and
  expression text. Confirm the catalog reports matching/total deterministic
  recipes, `load` only copies a built-in recipe into the editor, and the empty
  state appears when no recipe matches.
- In the Code page output-target picker, search linked writable outputs by
  label, category, role, runtime type, cell, and target key. Confirm the picker
  reports shown/matching/total outputs, warns when more than six matching
  outputs are hidden by the HUD cap, and warns when the selected output is
  hidden by the current filter.
- In the Code page linked-identifier strip, search generated evaluator-safe
  input names. Confirm the strip reports shown/matching/total input links, warns
  when more than five matching identifiers are hidden by the HUD cap, and
  clicking a visible identifier only inserts that deterministic name into the
  recipe text.
- Open the ESU Blocks editor from the toolbar, left panel, linked-target
  inspector, and `Controllers on craft` list. Confirm it opens to the `Blocks`
  workflow by default, fits the full focused viewport, and that `Fit` restores
  that layout after dragging or resizing.
- Place a Bread Board, AI Breadboard, ACB, ACB Controller, and Missile
  Breadboard Controller from the right palette where the current FtD install
  exposes those items. Confirm the palette button changes to `placing`, the
  bottom strip reports the armed block, green placement preview cells are empty,
  amber preview cells are blocked, panel clicks do not place blocks, and any
  rejected placement writes an Automation Editor runtime-log warning.
- Use the right-panel `Controllers on craft` list grouped by main
  construct/subconstruct to select and open existing automation controllers.
- In the `Controllers on craft` list, search by controller label, controller
  type, construct, cell, and target key. Confirm the list reports
  shown/matching/total controllers, warns when group or total display caps hide
  matching controllers, and warns when the selected controller is hidden by the
  current search.
- Select each controller and run the left-panel `Run checks` action. Capture the
  ESU runtime log detail for any warning or failure. Confirm the button tooltip
  and empty-state copy say checks are observational native capability checks
  that may use same-value native access probes but do not create native nodes or
  change craft behavior.
- In the `Runtime checks` panel, filter diagnostic lines by severity, native
  capability label, proxy text, Switch, save/reload, and target text. Confirm
  the count reports shown/matching/total lines, the empty state appears when no
  line matches, and filtering says it is display-only: it filters cached lines
  and does not run probes or create native data.
- After `Run checks` on a Breadboard, confirm the `Live evidence gates` rows
  separately report the native graph fingerprint, Generic proxy fingerprint, and
  Switch fail-expression readiness instead of relying only on the runtime log.
  Confirm the rows say they are read-only results from the last `Run checks`
  pass, with `OK` meaning evidence is present and `WAIT` meaning
  link/apply/prepare/run checks work is still needed.
- Use `Target search` with a block label, runtime class fragment, category name,
  controller GUID fragment, role text, and cell coordinate. Confirm filter
  counts and the world target list update together.
- Confirm `Target search` shows concrete examples and an active-search reminder
  after typing, so large target lists can be narrowed before the visible-row cap.
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
  cell, proxy hints, reflective warning text, `Open builder`, and `Remove link`
  work for live and missing targets.
- In the left linked-target list search, search by Input/Output, target label,
  category, role, runtime type, cell, controller, and target key. Confirm the
  count reports matching/total links for the selected controller, per-direction
  empty states appear when Inputs or Outputs are filtered out, and the selected
  linked-target hidden warning appears when the filter hides it.
- In the default `Blocks` page, link an APS/weapon target as an input and a
  spinblock as an output. Confirm the contextual starter creates
  `Read ammo -> Compare < 10 -> Constant 45 -> Set angle`, with else `0`.
- In the ESU Blocks palette, use `Search semantic ESU blocks` terms such as
  `read`, `threshold`, `port`, and `setter`. Confirm the semantic count updates,
  matching beginner blocks remain addable, the empty state appears when no
  semantic block matches, and cards show role, named ports, compatibility, and
  whether they are lowerable now.
- Confirm the ESU Blocks palette categories read as Inputs, Logic, Math,
  Outputs, Timing, Organization, and Advanced Native.
- On the ESU Blocks canvas, confirm block cards show named input/output nubs,
  the starter links are drawn nub-to-nub with direction labels, and incomplete
  or unsupported blocks are not presented as silently lowerable.
- In the `Linked signals` panel, confirm empty Inputs/Outputs explain the click
  order, and populated rows explain that Inputs create Read/GBG paths while
  Outputs create Set/GBS paths before using `Add read` or `Add set`.
- In the same `Linked signals` panel, use `Search linked Inputs/Outputs` by
  direction, label, category, role, cell, or target key. Confirm input/output
  counts update, Add read/set rows remain usable for matches, empty states
  appear for filtered-out directions, and the selected-signal hidden warning
  appears when the active search hides it.
- Confirm the left linked-target list and the `Linked signals` panel both warn
  that link identity uses live-session target keys today, and that linked
  Inputs/Outputs should be re-checked after save/reload or cross-craft reuse
  until stable target identity lands.
- In the ESU Blocks selected-block inspector, confirm the no-selection state
  explains Target, Property, Threshold/Value, and order edits. Select Read/Set
  blocks and confirm Auto property binding says it uses preferred terms at
  Apply, the explicit picker is native GBG/GBS, and `Use auto` clears explicit
  selections. Confirm selected blocks also report role, port summary,
  compatibility, and validation state.
- Open the Read/Set native property picker and confirm it says FtD options are
  discovered through a temporary native Generic Getter/Setter proxy, the probe
  is cleaned up, selecting a property stores ESU metadata, and Apply performs
  the real GBG/GBS binding. Confirm the options count shows the 80-option cap
  and warns to use Filter when the cap is reached.
- Select an Advanced native wrapper block and confirm the inspector says Apply
  creates the advertised native Breadboard component while Revert removes only
  ESU-generated component ids.
- Confirm `Check blocks` reports the native lowering plan without mutation,
  `Apply blocks` creates native Evaluator/Switch nodes plus GBG/GBS proxy
  binding, and `Revert blocks` removes only the generated component ids.
- Remove the Read/Compare/Set path or add unsupported Delay/System Block blocks
  and confirm `Check blocks` gives block-friendly validation such as Compare
  needing a left input, Set Target needing a target and value, Delay not being
  lowerable yet, and nested System Blocks needing exposed ports in Systems.
- In the ESU Blocks `Native lowering` panel, confirm the plan summary reports
  visible/total native lowering steps and warns when extra steps are hidden in
  the compact HUD while still saying Check is preview-only. Confirm it also
  reports graph completeness for the supported slice, read/write targets and
  properties, Advanced native wrapper count, and metadata-only blocks.
- Confirm Check actions do not create native nodes: ESU Blocks `Check blocks`,
  System Block `Check lowering`, System Block signature `Check`, and nested
  `Check internals` should only validate or preview until an Apply action is
  pressed.
- Confirm linked targets appear as readable/writable block options rather than
  raw Generic Getter/Setter nodes in the default workflow.
- Use `Collapse to System Block` on a selected ESU Block and confirm a reusable
  System Block template appears with named input/output ports derived from the
  Read/Set target usage.
- Open `Advanced`, then in the native Breadboard graph page create
  Getter/Setter/Both proxy nodes for linked
  world targets, create a Button getter for an ACB Controller output, and create
  Rule getter/setter proxies for a linked ACB.
- On the Advanced Graph page, confirm the `Native edit mode` guide says
  quick-adds, moves, wires, setting changes, and deletes apply immediately
  through FtD board commands, while `Revert compile` only removes ESU-generated
  component ids from the last Blocks/Code/System apply.
- In the Advanced Graph quick-add rows, confirm the normal Breadboard,
  shared-variable bridge, and Missile Breadboard shortcut guidance repeats that
  these buttons add advertised/native vanilla nodes immediately, separate from
  Blocks/Code/System Apply-owned nodes tracked by `Revert compile`.
- Hover the Advanced Graph quick-add buttons and confirm the tooltips name the
  native component or search terms, say the node is added immediately, and
  explain that disabled buttons mean the selected board does not advertise a
  matching vanilla component.
- On a large native Breadboard, confirm the Breadboard Inspector warns when the
  reflected native graph reaches the 64-component scan cap and says full
  compatibility still needs scalable native enumeration/virtualization.
- In the Advanced Graph stored-component list, search by native component label,
  type, id, proxy target/filter, or description and confirm the count reports
  shown/matching/total reflected native components before the safe 18-row cap.
  Confirm the cap warning says full compatibility still needs
  virtualization/pagination.
- Select a native Breadboard component with more than the visible port limits
  and confirm the selected-node inspector says the canvas shows the first eight
  input/output nubs, the wire controls show the first four ports, and full
  compatibility still needs port virtualization/pagination.
- In the selected native node wire-control port search, search by input/output,
  port index, label, connected/open state, and connected source. Confirm the
  count reports visible/capped ports, the empty state appears when no visible
  port matches, and the HUD warns if the selected wire-source output is hidden
  by the filter.
- In the ESU Blocks `Advanced` native wrapper palette, confirm each visible
  native wrapper card shows its `Native type` line and that hovering it reveals
  the full advertised FtD component type before Apply creates anything.
- Confirm linked target cards also expose in-card proxy buttons and visible
  auto-pick terms before opening the full native proxy property picker.
- On an individual linked target card, confirm the proxy shortcut row says
  Getter/Setter/Both buttons create native GBG/GBS Proxy Nodes immediately, that
  only Blocks/Code/System Apply-owned nodes are tracked by `Revert compile`, and
  that auto-picked properties can be corrected with the native property picker.
- In the Breadboard `Linked target proxies` search, search by Input/Output,
  GBG/GBS, target label, category, role, runtime type, controller, cell, and
  target key. Confirm the count reports shown/matching/total linked targets, the
  empty state appears when no proxy action matches, and the HUD warns if the
  selected link is hidden by the filter.
- Confirm common target proxies auto-select a likely FtD property where exposed
  for spinblocks, propulsion, weapons/turrets, ACB rules, lights, doors/docking,
  detection, and resource/power blocks; use the picker to correct mismatches.
- In the Breadboard graph page, edit a linked ACB node's enabled state,
  priority, search pattern, condition, action, and numeric values. Edit a linked
  ACB Controller node's first buttons, including name, keyword, Breadboard
  output flag, shape, and color.
- Confirm the ACB `Native ACB edit mode` guide says fields write directly to
  native `ControlBlockData` through FtD's `Var.Us` path, and linked ACB rows are
  native rule data rather than ESU recipe nodes.
- Confirm the ACB Controller `Native ACB Controller edit mode` guide says name,
  keyword, Breadboard output, shape, and color are immediate native button data
  edits, while ESU Revert only owns generated proxy/component ids.
- In the ACB Controller button search, search by button number, name, keyword,
  Breadboard output, shape, color, and reflected type. Confirm the count reports
  shown/matching/total native button rows, the empty state appears when no
  button matches, and clearing the search restores the capped editable rows.
- Confirm the graph/code editor bottom strip reports page, link count, grid
  state, status text, immediate native edit behavior, and generated-node revert
  availability.
- Open the `Advanced` -> `Systems` tab, confirm the breadcrumb reads from `Root` through the
  selected controller into the draft System Block, use `Suggest ports`, `Check`,
  `Apply template`, and `Revert draft`, and confirm the tab stores only ESU
  metadata for named ports/templates with no native controller mutation.
- On the System Block signature page, confirm the `System Block scope` guide
  says Apply template saves ESU-only metadata, Check validates without native
  controller mutation, Suggest ports reads current linked Inputs/Outputs, and
  ports should be re-checked after save/reload or cross-craft reuse until stable
  target identity and portable rebinding land.
- On the System Block signature page, edit input/output port text and confirm
  the `Port preview` shows normalized port/nub counts before Check/Apply. Enter
  a repeated name such as `ammo, ammo` and confirm a `Duplicate System Block port`
  warning appears and Check/Apply reject it instead of silently dropping the
  duplicate.
- On the System Block reusable template list, confirm the library count shows
  current templates against the 64-template persistence cap, says duplicate
  normalized name/ports keep the latest template, and warns at the cap that
  library paging/virtualization is still missing.
- In the same reusable template list, search by template name, controller,
  input/output port, comment, and internal graph note. Confirm the count reports
  matching/total saved templates and the empty state appears when no template
  matches.
- Close/reopen Automation Editor after applying a System Block template and
  confirm the reusable template library reloads the template without requiring
  the original controller/craft identity.
- Return to the Graph page and confirm the saved System Block appears as a
  visible `System Block nodes` graph node with compact input/output port
  summaries and `Enter`, `Ports`, and `Code` actions.
- In the `System Block nodes` graph-node search, search by node name,
  controller, input/output port, comment, and internal graph note. Confirm the
  count reports shown/total nodes for the selected controller, the empty state
  appears when no graph node matches, and the HUD warns if the currently open System Block is hidden by the filter.
- On the System Block graph node, use `Check lowering` and confirm it reports
  the native Generic Getter/Setter proxies that can be created without mutation.
  Use `Apply proxies`, confirm matching linked ports create native proxy nodes,
  then use `Revert` to remove only the generated System Block proxy nodes.
- After applying a System Block template, use `Enter` to open the nested
  workspace. Confirm the breadcrumb reads from `Root` through the controller and
  System Block into `Internal Graph`, edit the internal graph draft, use
  `Check internals`, `Apply internal graph`, and `Revert internal`, and confirm
  the `Up` control returns to the host workspace after clean internal metadata.
- Use the proxy property picker on created Generic Getter/Setter nodes and
  confirm FtD exposes a sensible property list for the selected target type.
- Run `Run checks` again after creating proxy nodes and confirm the runtime
  diagnostics report extended component palette coverage and Generic
  Getter/Setter property-picker enumeration.

### Future full compatibility tests

The following checks describe the full vanilla Breadboard compatibility target.
They should become required as the missing work in
`AUTOMATION_EDITOR_RESEARCH_AND_DESIGN.md` lands; until then they are roadmap
coverage, not proof that the current editor is complete.

- Confirm `Prepare validation graph` is unavailable or warns until the selected
  Breadboard has at least one writable linked target.
- Use `Prepare validation graph` on a Breadboard with a writable linked target.
  Confirm the panel/tooltip says it compiles the deterministic validation
  Recipe into native Evaluator/Switch proof nodes plus a target-specific
  Generic Setter Proxy Node, that those generated nodes are tracked by
  `Revert compile`, and that it immediately refreshes the live evidence rows.
- Confirm `Capture baseline` and `Compare baseline` stay unavailable until all
  live evidence gates are OK, and that their tooltips/panel text say they are
  diagnostic-only fingerprint reads that do not write native Breadboard data.
- Run `Run checks`, use `Capture baseline`, save/reload, run checks again, and
  use `Compare baseline`. Confirm it reports an Automation validation baseline
  match for the `Native persistence fingerprint`, `Generic proxy fingerprint`,
  and Switch fail-expression readiness lines.
- Quick-add every native component family advertised by the selected board,
  including Comment, Constant, Evaluator, Switch, Logic, Getter, Setter, PID,
  Threshold, Clamp, Delay, Sum, Multiply, Variable Reader, and Variable Writer
  where available. Move, delete, connect, clear native ports, and edit exposed
  settings from the ESU canvas.
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
