# Smart Builder HUD

This note tracks the current Smart Block Builder workflow and the HUD responsibilities used by in-game smoke testing.

## Current Workflow

- Pick a primitive or generator from the right palette. For down slopes, choose
  size **1-4**.
- Shape selection arms **Add** mode and shows a snapped placement ghost at the cursor.
- Left click creates a primitive scene node. The new node becomes selected and
  Smart Builder switches to **Scale**.
- Use the virtualized Scene list, viewport click/marquee, or selection commands
  to edit one node or a group. Numeric craft-local transforms, X/Y/Z quarter
  turns, align/distribute, craft-face snap, bounds, spans, and gaps are available
  in the selection inspector.
- Convert selected primitives into editable Linear, Grid, Radial, or Polyline
  pattern nodes, or create compressed rectangle, wall, plane, brush, and flood
  region nodes. Patterns expose viewport handles and typed parameters; Enter
  confirms a gesture and Escape restores its starting parameters.
- Pattern actions can edit embedded sources, Dissolve back to the original
  source group, or atomically Bake instances into independent primitives.
- Right click a preview piece to open the context menu for Select, Duplicate, Delete, Yaw, and Flip.
- The context menu owns foreground input even when it overlaps panel fields or
  buttons; background controls remain noninteractive until the menu closes.
  Their normal opacity is retained by default and follows the optional modal
  HUD fading preference.
- **Apply** expands all nodes and commits the currently displayed valid plan
  atomically through native placement/removal commands and one native undo.
  **Cancel** clears the whole scene.
- Right click cancels the current Add mode or active drag only; it does not clear the scene.
- Panel clicks, drags, and scrolling are claimed by Smart Builder and never
  place an armed preview through the HUD into the world.

## HUD Areas

- **Top toolbar:** mode switch, Add/Move/Scale/Rotate, draw plane, occupancy,
  material, symmetry, notifications/log, Undo/Redo, Yaw/Flip,
  Apply/Cancel/Close. Dense layouts compact labels and rely on the pinned left
  footer for Apply/Cancel instead of overlapping the notification rail.
- **Right palette:** full-height Shapes/Generators browsing, including shape
  selection and down-slope length buttons (**1m**, **2m**, **3m**, **4m**).
- **Bottom strip:** current state, readable handle modes (**Gizmo**, **Face**,
  **Edge**, **Corner**), preview mode (**Wireframe**, **Material**, or **Mat
  only**), and down-slope support mode.
- **Left panel:** material/tool/plane/occupancy/symmetry readout, then the Scene
  list above scrollable selected-node metrics and actions. Scene rows are
  virtualized, while cached node previews avoid re-enumerating unchanged
  geometry. Node-specific actions include embedded-source editing,
  Dissolve/Bake, and explicit flood recomputation. Both splitters are
  draggable and retain their requested ratios when Smart Builder reopens;
  temporary resolution constraints do not overwrite them. Collapsed Scene or
  Selected shelves consume header height only.
- Every visible toolbar, left-panel, right-browser, footer, popup, and console
  rectangle is an input barrier for world placement. The placement ghost can
  remain armed while using the HUD, but placement requires a later unobstructed
  viewport click.

## Add, Scale, Move, Cancel

- **Add** creates new preview pieces and never converts the selected piece.
- **Scale** is entered automatically after a successful placement.
- **Move** and **Scale** use the selected handle mode.
- **Cancel** is the destructive scene clear. Right click is the lightweight escape for Add or drag.

## Preview And Support

- **Wireframe** draws clean outer hulls. **Material** adds a translucent
  material-tinted ghost under the wireframe. **Mat only** hides the wireframe.
- Per-cell colors distinguish valid, skipped occupied, preview overlap, craft
  collision, disconnected, removal, replacement, and unsupported output. The
  pinned legend and **Next Issue** select and pulse the responsible node/cell
  without moving the camera.
- Support defaults to **Full** for new down slopes.
- **Support: Full/Step** in the bottom strip controls the selected down slope and becomes the default for the next down slope.
- **Full** uses vertical support columns below raised slope segments. **Step** places one support block below each down-slope cell instead of filling every column.

## HUD Improvement Backlog

- Add stronger world-hover selection outlines for each individual slope hull and wide slope previews.
- Add wider shape cards once more block families are supported.
- Add a persistent preference for the default down-slope support fill.
- Add true FtD prefab mesh ghosts if a stable block mesh resolver becomes available.

## Smoke Checklist

- Select **Block**, hover a target, confirm a cube ghost appears, place it, and confirm **Scale** becomes active.
- Select **Down slope** size 1-4, hover a target, and confirm a sloped ghost appears.
- Right click while adding and confirm existing preview pieces remain.
- Press toolbar **Cancel** and confirm the preview scene clears.
- Toggle **Support: Full/Step**, apply a down slope, and confirm Step places one support block under each down-slope cell.
- Scale a wide down slope sideways and confirm the preview draws only outer hull edges.
- Place a full-width down slope from a wide block face and confirm committed lanes line up with the source width.
- Test **Gizmo**, **Face**, **Edge**, and **Corner** from the bottom strip.
- Test **Wireframe**, **Material**, and **Mat only** preview modes.
- Confirm Scene and Selected live only on the left, selecting a Scene row
  refreshes the actions below it, and the right browser uses the full height.
- At 1366x768 with the effective 1.44x scale and at 1920x1080 with 2x scale,
  confirm both panels and the left Apply/Cancel footer stay between the toolbar
  and status strip; every overview, Scene, and Selected body remains reachable.
- Right click a preview piece and confirm the context menu can Duplicate, Delete, Yaw, and Flip without clearing the scene.
- Arm Block and each generator, then click buttons, rows, empty panel space,
  split dividers, scrollbars, and the Apply/Cancel footer. Confirm no preview
  piece is added until the next unobstructed viewport click.
- Exercise valid and invalid numeric transforms, all pivot modes, X/Y/Z quarter
  turns, align/distribute, craft-face snap, bounds/span/gap measurement, and
  preview undo/redo. Invalid fields must leave the scene unchanged.
- Exercise `Ctrl+A`, `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, `Delete`, and Escape while no
  text field is active. Paste at a pointed craft cell and with the fallback
  one-cell-right offset.
- Create every editable pattern and region kind. Test viewport handles,
  Polyline Keep/Cardinal Tangent, Edit Source, Dissolve, Bake refusal above 512
  nodes, fixed flood snapshots, and explicit flood recomputation.
- Trigger every diagnostic color, use **Next Issue**, mirror down slopes over Y,
  and confirm an unsupported mirrored orientation rejects only its responsible
  node. Verify the 1,000-placement warning and pre-enumeration rejection above
  the 10,000-placement hard cap.
