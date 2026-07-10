# Smart Builder HUD

This note tracks the current Smart Block Builder workflow and the HUD responsibilities used by in-game smoke testing.

## Current Workflow

- Pick **Block** or **Down slope** from the right shape palette. For down slopes, choose size **1-4**.
- Shape selection arms **Add** mode and shows a snapped placement ghost at the cursor.
- Left click places a preview piece. The new piece becomes selected and Smart Builder switches to **Scale**.
- Use **Move** or **Scale** with the selected handle mode to edit the selected preview piece.
- Use **Rotate** or **Yaw** to turn the selected preview piece in 90-degree steps.
- Use the scene list or click a preview piece in the world to select between pieces.
- Right click a preview piece to open the context menu for Select, Duplicate, Delete, Yaw, and Flip.
- The context menu owns foreground input even when it overlaps panel fields or
  buttons; background controls remain disabled until the menu closes.
- **Apply** commits all preview pieces atomically. **Cancel** clears the whole scene.
- Right click cancels the current Add mode or active drag only; it does not clear the scene.

## HUD Areas

- **Top toolbar:** mode switch, Add/Move/Scale/Rotate, draw plane, occupancy, material, symmetry, notifications/log, Undo/Redo, Yaw/Flip, Apply/Cancel/Close.
- **Right palette:** full-height Shapes/Generators browsing, including shape
  selection and down-slope length buttons (**1m**, **2m**, **3m**, **4m**).
- **Bottom strip:** current state, readable handle modes (**Gizmo**, **Face**, **Edge**, **Corner**), preview mode (**Wireframe** or **Material**), and down-slope support mode.
- **Left panel:** material/tool/plane/occupancy/symmetry readout, then the Scene
  list above scrollable Selected-piece metrics and actions. Both splitters are
  draggable and retain their ratios when Smart Builder reopens.

## Add, Scale, Move, Cancel

- **Add** creates new preview pieces and never converts the selected piece.
- **Scale** is entered automatically after a successful placement.
- **Move** and **Scale** use the selected handle mode.
- **Cancel** is the destructive scene clear. Right click is the lightweight escape for Add or drag.

## Preview And Support

- **Wireframe** draws clean outer hulls. **Material** adds a translucent material-tinted ghost under the wireframe.
- Support defaults to **Full** for new down slopes.
- **Support: Full/Step** in the bottom strip controls the selected down slope and becomes the default for the next down slope.
- **Full** uses vertical support columns below raised slope segments. **Step** places one support block below each down-slope cell instead of filling every column.

## HUD Improvement Backlog

- Add stronger world-hover selection outlines for each individual slope hull and wide slope previews.
- Add explicit conversion commands for changing a selected piece from block to slope or slope to block.
- Add wider shape cards once more block families are supported.
- Add per-piece material override once the current scene-level material flow is stable.
- Add persistent user preferences for default support fill, panel layout, and preferred handle mode.
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
- Test **Wireframe** and **Material** preview modes.
- Confirm Scene and Selected live only on the left, selecting a Scene row
  refreshes the actions below it, and the right browser uses the full height.
- At 1366x768 with the effective 1.44x scale and at 1920x1080 with 2x scale,
  confirm both panels and the left Apply/Cancel footer stay between the toolbar
  and status strip; every overview, Scene, and Selected body remains reachable.
- Right click a preview piece and confirm the context menu can Duplicate, Delete, Yaw, and Flip without clearing the scene.
