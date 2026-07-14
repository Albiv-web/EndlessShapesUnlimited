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
   In the vanilla ESU options page, confirm **Fade HUD behind popups** defaults
   off and **Responsive paint palettes** defaults on.
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
   confirm additive/range selection behaves like a normal list. Right-click a
   selected row and confirm the group stays selected with that row promoted to
   primary; toggle Focus deco and confirm its button lights, then exercise
   Copy/Paste settings, Duplicate selection, and Delete selection. Right-click
   an unselected row and confirm it becomes the only target. Use the Inspector
   or keyboard shortcuts for whole-selection copy/paste.
10. Paint a multi-selected group from the swatches, typed color field, and paint
    tool; confirm the whole group changes in one undoable step. In Paint mode,
    drag a viewport selection over mixed eligible decorations/blocks and confirm
    every target inside it is colored without affecting targets outside it.
    Resize each visible paint-color panel and confirm its buttons fill the width
    in at least two rows. Disable **Responsive paint palettes**, confirm the
    legacy fixed grid returns, then re-enable the default.
11. In Block Palette, confirm **Build** is immediately left of **Hide** and off
    by default, with decoration-mesh behavior retained. Toggle it on; the title
    must remain **Block Palette**, Objects
    and non-block definitions must disappear, while locked or construct-incompatible
    choices are rejected without changing the active block or selecting Wood.
    Select usable entries with left and right click without placing through the
    panel. Confirm the full-size native marker follows the free mouse before
    placement and that its item, cell, rotation, footprint, and validity exactly
    match the committed block on all six faces, including occupied/invalid
    targets. Repeat all faces while focused on a rotated subobject; sample an
    existing craft block with right click, rotate/mirror, and
    undo/redo. The marker must hide over every ESU panel, popup, and text field,
    and a hidden/invalid marker must never place or remove. Type
    digits/WASD/orientation keys in the palette search and confirm
    the hotbar and marker stay unchanged. The first viewport click must only
    clear focus; the next may act, with no delayed action on focus loss.
    Include a multi-cell block and a subobject. Toggle it off and
    confirm normal decoration-mesh placement returns. Re-enable it, sample a
    different block/rotation and change the native hotbar item; the marker must
    refresh immediately. After moving over and then away from a panel, it must
    reappear at the current pointer target without a stale transform. Orbit while
    clicking to catch same-frame ray changes. Hold Tab, then enter UI/modal/empty
    space/exit and confirm all rotation markers hide. Attachment indicators are
    intentionally absent. Simple-mode Shift+LMB must not replace, and PermaBuild
    over an occupied/invalid cell must never enter hold-remove. If practical,
    destroy or lose team control of the focused craft and confirm native cleanup.
12. Move, rotate, axis-scale, and uniform-scale the selected group with each
    pivot mode: Bounds, Average, Selected, and Anchor.
13. Type `0.1, 0.1, 0.1` in the normal Scale XYZ fields with a group selected
    and confirm the entire group scales around the selected pivot.
14. Toggle symmetry and confirm mirrored decoration preview/anchors are visible,
    mirrored scale/rotation match, and deleting one side deletes its counterpart.
15. Open Surface Builder. Confirm Draw is absent beside the left Draft header,
    appears first in right-side Extra Tools, and clears every generator-button
    highlight when selected. Selecting a generator must clear Draw's highlight.
    Extra Tools must use the full available panel body without a large empty
    scroll region.
16. Build triangles on a normal block, a spin block, and an angled subobject.
17. Toggle normal flip and confirm preview and applied decorations match.
18. Use Create Face, Same anchor, and right-click context actions; confirm they
    do not unexpectedly enter material preview mode.
19. Select a paint color and material override; click Preview and confirm the
    preview and placed triangle use the same active craft-palette color and
    material after Apply.
20. Open Smart Builder.
    Arm a shape, then click every panel, blank panel area, scrollbar, splitter,
    and footer; none may place a preview through the HUD.
21. Switch the right panel between `Shapes` and `Generators`.
22. Create Circle, Polygon, Triangle, Hex, Octagon, Decagon, and Sphere
    generators in Shell and Filled modes.
23. Resize each generator with face, edge, and corner handles; confirm round-lock
    circles/spheres stay centered and scale predictably.
24. Toggle Wireframe, Material, and Mat only preview modes.
25. Create a large sphere shell, around 28 x 28 x 28, and confirm the editor
    remains responsive while the shape-aware hull still reads as a sphere.
26. Trigger Smart Builder blocked/occupied warnings and confirm they use the
    shared ESU notification/log UI instead of clipped status text.
27. Apply/cancel generated pieces, then undo/redo through FtD.
28. Save, reload, and confirm committed blocks/decorations persist.

## Startup And Options

- Game reaches main menu with ESU installed and no required-patch popup.
- ESU options tab opens in the vanilla options menu.
- Defaults stay safe for normal players: vanilla compatibility remains enabled,
  fast-load and memory-safe part-status features remain opt-in, modal HUD fading
  is off, and responsive paint palettes are on.
- Confirm **Memory-safe part status checks** defaults off and persists per
  profile. With it off, the vanilla `StatusUpdate` capacity is unchanged. With
  it on, load a status-heavy test craft and confirm warnings/errors still update,
  cleared problems lose their flags, and no repeated part-status allocation
  exception appears.
- Toggle **Fade HUD behind popups**. With it off, a modal keeps the background
  at normal opacity while blocking all background input; with it on, the same
  background visibly fades and remains noninteractive.
- Toggle **Responsive paint palettes** and confirm the responsive two-or-more-row
  layout and the legacy fixed grid switch immediately and persist per profile.
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

The current Automation Builder is the Scratch-style native breadboard workflow
documented in `docs/AUTOMATION_BUILDER.md`. These checks cover the released
surface. Historical Code, System Block, nested-workspace, and arbitrary native
component editor concepts are not part of acceptance.

### Current Scratch-style workflow

- On a fresh profile, open Automation Builder and confirm the blocking
  work-in-progress warning says the editor is unfinished and potentially very
  buggy. No world, panel, graph, console, camera, or build input may pass behind
  it. Acknowledge it, reopen the editor, and confirm it is not shown again for
  that profile.
- Open Automation Builder with `Ctrl+Shift+A` and confirm the shared ESU
  toolbar, left selected-board/link panel, right placement panel, filters, target
  list, and bottom status strip appear.
- Switch into and out of Automation Builder with Tab while clean. Make a graph
  draft and confirm Tab and editor close require Apply or explicit discard.
- Confirm left/right panels and the block canvas can be moved/resized, clamp
  inside the screen, preserve the bottom strip, and retain their valid persisted
  split ratios.
- Repeat at 1366x768/effective 1.44x and 1920x1080/2x. The selected-board
  summary must scroll independently, Input Links and Output Links must each
  retain a complete row, and no toolbar action, footer, popup, or panel may
  overlap or leave the screen.
- Place the loaded AI and basic breadboard definitions through the normal FtD
  block command path. If no distinct basic item is loaded, confirm the Basic
  card explicitly reports its AI fallback. A blocked placement must not create
  a controller.
- Select a target and then a board to create an input/getter link. Select the
  board and then a target to create an output/setter link. Confirm animated
  world lines, the selected-board shelves, link direction filters, and `1`/`2`
  modes agree.
- Remove a staged link and an ESU-owned applied link. Confirm imported native
  Getter/Setter links remain read-only and direct the player to FtD's native
  breadboard editor.
- Save/reload a craft with applied Generic Getter/Setter proxies. Select the
  board and confirm world links rehydrate from native target/filter/property
  data without session metadata.
- Open the selected board with `E` or **Open blocks**. Confirm the palette
  categories are Inputs, Logic, Values, Math, Outputs, and Notes, and that
  every full palette row begins a drag.
- At 0.55x, default, and 1.6x zoom, drag blocks to free space, a stack edge, a
  named value socket, and a control body. The ghost, snap outline, and final
  placement must match; occupied sockets must reject a second value.
- Drag an attached root and confirm its socketed values, body descendants, and
  following stack move as one group. Release outside the workspace and simulate
  a lost mouse-up; no block or latched drag may remain.
- Pan with left or middle mouse over empty workspace, zoom around the pointer,
  and exercise Fit, Center, and Arrange. Imported vanilla component positions
  and sizes must not be rewritten by display normalization or Arrange.
- Use Starter Flow with exactly one linked input and output. Confirm it stages
  Read -> Below Threshold -> If True -> Set with threshold `10`, then value
  `45`, and else value `0`. It must remain an ordinary editable block graph.
- On Input Getter and Output Setter, choose a linked target and an actual native
  readable/writable property. Auto/default choices may resolve only from the
  properties FtD advertises for the target type; unresolved properties must
  remain visible readiness errors.
- Create and Check every palette mapping:
  - Input Getter -> `GenericBlockGetter`;
  - Output Setter -> `GenericBlockSetter`;
  - If True and Switch > Threshold -> native `Switch`;
  - Not/And/Or/Xor/Nand/Nor/Xnor -> matching `LogicGate`;
  - Above/Below Threshold -> matching `FuzzyThreshold`;
  - Add/Subtract/Multiply -> supported `Evaluator` expression;
  - Max/Min -> matching `MaxMin`;
  - Clamp -> `Clamp`;
  - Delay -> native `Delay`;
  - Constant -> `ConstantInput`;
  - Random -> `RandomInput`; and
  - Comment and Forever -> native Comments, with Forever carrying the ESU
    round-trip marker.
- For every block with a primary stack/value input, connect exactly one source.
  Check must reject no source and simultaneous stack-plus-socket sources.
- Check logic B and Max/Min B requirements, finite constant/scalar values,
  threshold input, evaluator/Clamp/Delay input, Getter/Setter target/property
  bindings, and Setter value requirements. The readiness popover and Generated
  Native Plan must identify the offending block and slot.
- Build Read -> Switch > Threshold -> Setter. Feed the Switcher from the stack,
  socket its required then/pass value, set a constant else/fail value, and put
  the Setter in the body. Confirm Check describes the Switch's selected numeric
  result feeding the Setter, not imperative conditional execution.
- Repeat with If True and confirm its native threshold is 0.5. Repeat with a
  constant threshold on Switch > Threshold and confirm it becomes the native
  Switch threshold setting.
- Wrap a valid chain in Forever. Confirm Check explains that the vanilla board
  evaluates continuously, Apply creates only the marked Comment for layout
  identity, and the child native chain does not depend on an ESU runtime loop or
  a wire from that Comment.
- Use right-click Copy stack, Duplicate stack, and Delete. Repeat with
  `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, Delete/Backspace, arrows, and Shift+arrows.
  Imported vanilla nodes must refuse mutation.
- Exercise at least 64 graph edits, then use toolbar Undo/Redo, `Ctrl+Z`,
  `Ctrl+Y`, and `Ctrl+Shift+Z`. Verify bindings, values, layout, connections,
  staged links, and pending ESU-owned removals restore consistently; the oldest
  state may fall off after the 64-snapshot cap.
- Run **Check** and compare the plan to the visible blocks. Confirm it performs
  no board mutation and reports creates, ESU-owned updates/removals, target
  bindings, native wires, warnings, and blocking errors.
- Run **Apply** on a valid mixed graph. Confirm staged blocks become the mapped
  vanilla component types, owner-marker Comments identify only generated
  components, native properties and wires match the plan, and the dirty state
  clears only after verification succeeds.
- Force or simulate a native connection/lowering failure on a disposable craft.
  Confirm Apply reports the failure and rolls back created components, owned
  edits, block-name changes, and native input connections.
- Apply the same clean graph again. Confirm the operation is idempotent and does
  not append duplicate component packages or ownership markers.
- Close/reopen the canvas and save/reload the craft. Compact value blocks must
  remain socketed, body children must remain nested in expanded hosts, native
  positions/settings must round-trip, and world links must rebuild from the
  persisted proxies.
- Add recognized native components and one unsupported component through FtD's
  native breadboard editor. Reopen ESU: recognized imports must have semantic
  shapes, the unsupported component must appear as opaque **Advanced Native**,
  and all imported nodes/wires must remain read-only and intact.
- Connect an imported native source to an ESU-owned destination where compatible.
  Apply may create the destination input connection but must not take ownership
  of, edit, move, or delete the imported source.
- Run **Revert**. Confirm it removes only ESU-owned generated components, their
  ownership marker Comments, and wires sourced from those components. Unrelated
  vanilla components and wires must remain byte-for-byte/native-state
  equivalent.
- Make a dirty draft and close the editor. **Keep editing** and Escape must
  preserve it; **Apply and close** must close only after successful validation;
  **Close anyway** must discard staged blocks, staged links, pending owned edits,
  and pending owned removals while leaving the applied native board unchanged.
- Open each property picker, slot dropdown, context menu, and readiness popover
  over nodes, workspace, palette, inspector, dividers, coordinate fields, and
  the ESU Console. Only the popup may receive input. Outside click must only
  dismiss it, and Escape must close the topmost popup before the canvas/editor.
- Open a graph popup, then request dirty close. Confirm the popup is dismissed,
  the whole editor and console are noninteractive behind the close prompt, and
  Escape/clicks affect only that prompt.

### Explicitly not current release features

The following names appeared in earlier research and prototype checklists. They
do not identify hidden pages and must not be reported as missing controls in the
current release:

- a **Code** page, recipe catalog, expression compiler, linked-identifier strip,
  or arbitrary scripting runtime;
- **System Blocks**, reusable templates, signature/port pages, nested internal
  graphs, breadcrumbs, or Collapse to System Block;
- an **Advanced Graph** that immediately edits arbitrary native component
  families, ACB rules, ACB Controller buttons, Missile Breadboard components, or
  shared-variable bridges;
- Prepare validation graph, Capture baseline, Compare baseline, live-evidence
  gates, or diagnostic fingerprints; and
- dedicated ACB, ACB Controller, or Missile Breadboard Controller adapters.

The current Advanced Native card means “preserved opaque vanilla component,” not
an editor for that component. If any former concept is implemented later, add
implementation-specific tests and native ownership rules before promoting it to
release acceptance.

## Decoration Edit

- Exercise the default-off Block Palette **Build** toggle in list and 3D-grid
  views. The title remains **Block Palette**, Objects disappear, unavailable or
  non-block definitions cannot silently fall back to Wood, and both mouse
  buttons select the exact native item. HUD clicks must never place through.
  Viewport left click uses normal FtD placement; viewport right click samples
  an existing main-craft or subobject block and its rotation before any
  decoration context/remove action. Verify native one-metre grid targeting,
  valid multi-cell footprints, rotation/mirroring, collision/resource checks,
  undo/redo, save/reload, and multiplayer replication. While a search or numeric
  field owns keyboard input, digits/WASD/orientation controls must not alter the
  native hotbar or marker. The first outside click only blurs the field; no input
  may replay after focus leaves. Escape and every editor
  handoff must restore normal input ownership.
- Put decoration centers behind blocks on both the main craft and a subobject.
  With X-ray off, confirm viewport hover, LMB/Shift+LMB, right-click context,
  direct Paint, short Box fallback, and full Box drag all skip hidden centers
  and can choose a slightly farther visible center. With X-ray on, confirm the
  same paths acquire hidden centers. Direct Outliner/Selected anchor selection
  must work in both states, and an exposed decoration must not be rejected just
  because its own wedge/partial tether cell lies on the coarse fallback ray; a
  center moved behind that same block must be rejected. Confirm 512 projected
  X-ray-off candidates may resolve, 513 reject before selection/paint, and the
  live marquee explicitly labels its count projected. Close/reopen and confirm
  X-ray defaults off again.
- Select existing decorations in viewport and outliner.
- Right-click selected decoration rows in both Outliner and Selected anchor.
  Confirm the cursor menu opens at the row, preserves the whole explicit group,
  promotes the clicked row to primary, and offers Select only this, Move,
  Rotate, Scale, active-state Focus deco, Copy/Paste settings, Duplicate
  selection, and Delete selection. Copy selection must remain absent from this
  compact menu but available in Inspector and through shortcuts. Right-clicking
  an unselected row must isolate it; construct headers and stale/removed rows
  must never open actionable menus.
- Leave Box mode active after selecting several viewport decorations, then
  right-click a selected decoration center. Confirm the group menu opens and
  every group action retains the explicit selection where appropriate. Repeat
  from the group gizmo between centers and inside the cyan group bounds. An
  active unfinished Box drag must still cancel safely; right-clicking empty
  space may disable Box mode without changing the completed selection.
- Open every Decoration/Surface, Smart Builder, and Automation right-click menu
  directly over a text or number field. Click each overlapping menu row,
  especially Delete, and confirm only the foreground action runs: the field
  behind it must not focus, edit, submit, scroll, or move the camera. Repeat for
  Automation graph-node context actions over editable node values.
- Duplicate a shuffled multi-selection from the row menu and verify stable
  primary-first manager ordering, exact in-place clones, selection of the new
  group, no clipboard replacement, and one-step undo/redo. Delete a group with
  symmetry off and on; all explicit/mirrored targets must be preflighted,
  deduplicated, deleted as one history action, and restored together by undo or
  by rollback after a forced failure.
- In Inspector, verify `Settings: Copy | Paste` interoperates in both directions
  with FtD's native decoration clipboard and preserves every target tether and
  runtime identity. Test a valid native decoration offset beyond +/-10 m.
- Box-select decorations in a deliberately non-sorted order. Use
  `Ctrl+C`, then `Ctrl+V`; confirm exact in-place clones are created
  primary-first, selected as one Move group, and repeated paste remains usable.
  Repeat with explicit `Ctrl+Shift+C/V`. Copy one decoration with `Ctrl+C` and
  confirm the next `Ctrl+V` returns to native settings paste instead of cloning.
- Confirm whole-selection paste rejects a different main/subconstruct, a
  missing tether block, Focus deco, placement, active drags, box selection,
  prompts, and active text fields without creating anything.
- Confirm active symmetry does not expand paste-in-place or Duplicate in place.
- Undo/redo a pasted group as one action, then exercise Apply, Cancel, editor
  reopen, and save/reload. The memory clipboard must survive Apply/Cancel/reopen
  but intentionally not a game restart or cross-construct paste.
- Click the base, middle, and tip of every Move/Scale/Anchor shaft at near/far
  camera distances and heavy foreshortening. Confirm the hovered axis always
  matches the clicked axis and the center free-move area remains compact.
- Open the bottom-left Gizmo settings gear in Deco, Surface, and Smart Builder
  modes. Confirm the same saved values appear in every editor, then test
  0.5x/3x size and thickness plus 8px/40px click areas, Set, Reset defaults,
  Escape/Close, profile persistence, low resolution, and 200% HUD scale.
- Compare the same pixel drag at every gizmo size in all three editors;
  movement, scale, and rotation sensitivity must not change. Confirm the popup blocks background UI, mouse
  wheel, keyboard, build, and camera input and cannot change settings mid-drag.
- Repeat those modal checks with **Fade HUD behind popups** off and on. The
  option changes only background appearance; input ownership and Escape/outside
  dismissal behavior must be identical.
- Toggle Focus deco on a selected decoration, then verify viewport click-select,
  Box select, outliner rows, and selected-anchor rows cannot move selection
  until Focus deco is turned off.
- Move, rotate, scale, anchor, paint, and view-filter decorations.
- In Paint mode, drag a selection rectangle across several eligible decorations
  and blocks. Confirm all targets inside are painted, outside targets are not,
  and beginning/ending the gesture over HUD panels cannot paint or place through
  them.
- Resize every paint-color group in Decoration, Surface, and any other ESU
  surface that exposes swatches. With the responsive option on, controls must
  fill the available width and prefer at least two rows; with it off, confirm
  the legacy fixed grid remains usable and scrollable.
- Apply and undo changes.
- With a dirty preview, press Ctrl+D and click toolbar Close. Confirm the
  foreground Unapplied decorations prompt appears above the editor (solid or
  faded according to the profile option) and
  offers Apply and close, Discard, and Keep editing without trapping input in a
  dim-only state.
- Save and reload the construct.
- With Vanilla Compatibility Mode on, verify blocked saves show an ESU popup and log entry.

## Surface Builder

- Confirm Draw is the first Extra Tools button and no longer appears beside the
  left Draft header. Switching between Draw and every generator must leave only
  the selected creation button highlighted; transform tools must not leave a
  stale Draw/generator highlight. Confirm all three four-column rows fill the
  panel width and include Quad, Polygon, and Tube.
- Place and rotate a Quad on several craft faces, edit width and height
  independently, then use uniform Scale and verify its aspect ratio remains
  stable. Preview/place with nearest and same-anchor modes, symmetry, undo, and
  redo; reject zero, negative, non-finite, and overflowed dimensions atomically.
- Place Polygons with 3 and 12 sides and several intermediate values. Confirm
  radius, rotation, center movement, mesh/strut diameter, paint, material,
  symmetry, preview/place, undo, and redo all use the normal generator paths;
  values outside 3-12 must normalize or reject without partial geometry.
- Draw straight and bent Tube paths with 3, 8, and 64 sides. Confirm tube
  diameter changes the ring radius, each path point has one ring, adjacent rings
  have matching rails without sudden frame flips, moved/typed path points rebuild
  safely, and Preview/Place remain transactional. Reject exact and near
  backtracks at the offending point, including a direction dot product exactly
  `-0.999`. Verify 781 points x 64 sides succeeds at 99,904 segments and 782 x
  64 rejects, then confirm the 100,000-segment pre-allocation guard counts
  aggregate unique output across two- and eight-variant symmetry.
- Click **Preview** for Path, Circle, Arc, 2D cone, Sphere, Part sphere, Quad,
  Cone, Frustum, Polygon, and Tube. Confirm the chosen decoration mesh appears
  at the final anchor/position/rotation/scale with matching paint, material
  override, and symmetry, while cyan guides and handles remain on top. Include
  X/Y/Z mesh axes plus limited and rotated arcs; Place must match Preview.
- Change a draft setting, coordinate, handle transform, shared anchor, or active
  symmetry plane after Preview and confirm stale real meshes disappear until
  Preview is clicked again. Same-anchor hints alone must not enable mesh
  preview. Exercise a generator above 768 placements and confirm deterministic
  samples span the full shape without uncapped mesh, wire, or anchor-hint draws.

- Create triangle faces from three craft-surface points.
- Select a point, edge, and face and use the **Coordinates** shelf in the left
  Surface Builder panel. Confirm it defaults collapsed, its action header stays
  visible, its coordinate body scrolls, and no Apply text button remains. Exact
  text stages until Enter, comma/point decimals work, values normalize to 0.001
  m, shared point indexes update connected faces, and one Enter commit produces
  one undo step. Confirm the
  bottom strip keeps its existing height and snap controls and only points to
  the left-panel editor.
- Edit and persist independent X/Y/Z slider ranges, then restart FtD and verify
  they reload. Reject non-finite, reversed, equal, and sub-0.001 m ranges. Test
  Reset -10/+10, values below -10 and above +10, and all A/B/C values outside
  multiple axes; effective ranges must temporarily expand without changing the
  saved profile or clamping staged coordinates. Invalid/incomplete coordinate
  text must disable only that row's slider and must remain untouched.
- Drag each X/Y/Z slider at low/normal resolution and 200% HUD scale. Existing
  points must update live at 0.001 m while one complete drag creates one undo
  command; rejected zero-edge/zero-area positions must leave the last valid
  geometry unchanged. Mouse/text/scroll input must never move the camera or
  trigger a Surface build action. Revert must restore the target's selection-time
  coordinates without reverting unrelated Surface settings.
- Confirm the header reads only **Coordinates**, New triangle is absent, Hide
  collapses it to a persistent bottom **Coordinates | Show** header, and the
  Draft list expands through all released space. Show must restore the editor.
  Drag the divider in both directions, reopen the editor, and verify the
  customized split persists while Draft controls never overlap Coordinates and
  the draft list stays stable until the real minimum workspace is reached.
  With the automatic split, the Coordinates header should follow the final
  draft hint without the previous empty list-sized gap.
- Keep Coordinates open while adding/removing points and triangles, resizing the
  left panel, and changing HUD scale. Draft must immediately consume the exact
  remaining height, keep all rows reachable, and never overlap or leave unused
  space above Coordinates.
- Click the `-step` and `+step` buttons around every axis slider and verify each
  button visibly includes the current configured step and each click updates an
  existing point live as one undo action. Right-click either
  button and confirm it opens the chooser without changing the coordinate;
  test custom comma/point values plus 0.001, 0.01, 0.05, 0.1, 1/8, 1/4,
  1/2, and 1 presets, then restart FtD to confirm independent X/Y/Z persistence.
  Reject zero, sub-0.001, non-finite, overflow, and greater-than-1000 m steps
  without changing the saved preference or craft history.
- Select every generator path point and shape center and repeat typed coordinate
  edits; confirm the shape basis is preserved and previews invalidate only on a
  real successful change.
- Hover each A/B/C vector header and each X/Y/Z slider row. Confirm that row
  highlights and only its bound Surface/generator point gains the bright world
  marker, including faces with three different points.
- Preview, place, delete, and bridge surfaces.
- Select several craft-palette colors, Preview each triangle, then Place it.
  Compare against the same active palette entry and confirm preview and placed
  decoration color match exactly rather than using separate tint conversion.
- Test right-click menus for point, edge, and face targets.
- Test X/Y/Z symmetry and multi-axis symmetry.
- Place right, isosceles, and scalene triangles with normal flip on and off.
- Confirm mirrored placed decorations face the same intended side as the preview.
- Save, reload, and visually inspect surface normals.

## Smart Block Builder Baseline

- Open Smart Builder from build mode and from ESU mode switching.
- Confirm Scene and Selected appear only in the left panel, Scene is above
  Selected, both bodies scroll independently, and their actions still target
  the active preview piece.
- Drag both left-panel dividers, close and reopen Smart Builder, and confirm the
  split ratios persist. Confirm the right Shapes/Generators browser uses the
  full available height without duplicate Scene or Selected shelves.
- Repeat at 1366x768 with effective 1.44x scaling and 1920x1080 at 2x. Both
  panels, the Apply/Cancel footer, and every scrollable body must remain on
  screen; split rectangles must stay ordered, nonnegative, and nonoverlapping.
- Select material, draw plane, occupancy mode, symmetry, handles, and preview mode.
- Arm each shape/generator and click toolbar controls, Scene/Selected rows,
  blank left/right-panel space, scrollbars, dividers, and the pinned footer.
  Confirm none of those clicks place or seed a preview behind the HUD.
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
