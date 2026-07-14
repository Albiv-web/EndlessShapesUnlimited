# EndlessShapes Unlimited 1.0.7 in-game acceptance plan

Test target: From The Depths 4.3.3

Installed mod folder: `%USERPROFILE%\Documents\From The Depths\Mods\EndlessShapesUnlimited`

Expected assembly: `EndlessShapesUnlimited.dll`

Expected assembly SHA-256:
`DA8FEBD37208F37CB0FC35092438AF26FE12A4D0F4BE2805201BA95B78CAEFE6`

Do not merge the draft PR or publish a release until every required section
passes. Record the FTD build, test date, result, and evidence for each section.

## Preparation

- Back up any construct used for legacy or high-decoration testing.
- Confirm standalone `DecoLimitLifter` and `EndlessShapes2` are absent or disabled.
- Confirm `EndlessShapes Unlimited 1.0.7` is enabled. The separately installed
  `AdvancedMimicUi` mod is not a dependency; the required input helper is bundled.
- Close FTD before replacing mod files.
- Preserve the full FTD log for every failed test.

## 1. Startup and registration

1. Start FTD and reach the main menu.
2. Confirm the log contains the successful `EndlessShapes Unlimited v1.0.7`
   message exactly once.
3. Confirm the Alerts panel contains `EndlessShapes Unlimited v1.0.7 Active!`.
4. Confirm there is no patch-installation, missing-target, duplicate-GUID,
   duplicate-class, or `AutoSyncroniser.fullArray` error.
5. Open the Decorations inventory and confirm **Decoration Builder** appears.
6. Open the character-item inventory and confirm **Deco** appears.

Pass: the game reaches the menu, both assets register once, and no startup error
or standalone-mod conflict is present.

## 2. Legacy compatibility

1. Load an untouched construct saved with standalone EndlessShapes2.
2. Confirm its Decoration Builder block and all settings IDs 0 through 16 load.
3. Confirm existing generated decorations and tether data retain their positions.
4. Save under a new name, reload it, and compare the result with the original.

Pass: no missing component, reset setting, moved decoration, or load exception.

## 3. OBJ parsing, geometry, and numeric input

Run these against small known OBJ fixtures before heavy models:

- right, isosceles, and scalene triangles;
- rectangle, 16-point ellipse, line, and convex n-gon;
- faces with and without UVs in the same mesh;
- negative indexes and `v`, `v/vt`, `v//vn`, and `v/vt/vn` face forms.

Confirm each valid mesh produces the expected preview count and orientation.
Then confirm repeated points, zero-length edges, collinear faces, concave faces,
non-finite values, and a transform-collapsed axis reject the selected mesh with
an OBJ line diagnostic and create no construct content.

Under a German/comma-decimal locale:

1. Enter `0.05` and confirm it remains `0.05` rather than becoming `5`.
2. Enter `0,05` and confirm it becomes `0.05` internally.
3. Enter incomplete text such as `0,` or `-`, leave the field, and confirm the
   prior value is retained.
4. Confirm the palette slider cannot exceed 31.

## 4. Generation lifecycle and concurrency

For both synchronous and animated generation:

1. Generate an untextured model and verify position, scale, orientation, color,
   and decoration count.
2. Generate a textured model and verify independent per-face UV behavior.
3. Generate once with local-origin projection disabled and once enabled.
4. During animation, verify progress increases and **Cancel** removes every
   decoration and auto-placed block from that run.
5. During animation, delete the Decoration Builder block. Confirm cleanup occurs
   before FTD deletes the block and no callback exception repeats afterward.
6. Start a second builder on the same main construct. Confirm it is rejected.
7. Start builders on two different main constructs. Confirm both can proceed.
8. After success, cancellation, failure, and block deletion, confirm the main
   construct's connection-rule master/request switches match their prior values.

Pass: no partial build, leaked texture, stale callback, stuck connection state,
or cross-builder setting contamination.

## 5. Tether movement transaction

1. Point **Deco** at a non-tether block and confirm movement is rejected.
2. Move a valid tether with several linked decorations in all six directions.
3. Confirm every decoration stays at the same construct-space location while its
   tether and relative positioning update.
4. Include one decoration whose resulting positioning would exceed +/-10 and
   confirm the whole move aborts before either block changes.
5. Block the destination and confirm no source block or decoration changes.
6. Force a command/property failure if a reproducible in-game setup is available;
   confirm decorations restore first, followed by source and destination blocks.
7. Equip, unequip, disable, and re-equip the item repeatedly; confirm there is
   only one pointer update and no stale callback.

## 6. OBJ export

1. Export a main construct with at least one subconstruct.
2. Include a carried object whose mesh has multiple submeshes/materials.
3. Include two used materials sharing one texture and two textures sharing the
   same source filename.
4. Export under `de-DE` and inspect OBJ coordinates for decimal points, never
   decimal commas.
5. Confirm every `usemtl` has one matching MTL entry, unused materials are absent,
   shared textures are written once, and texture filenames have distinct GUIDs.
6. Import the OBJ into an independent viewer and inspect orientation, winding,
   UVs, subconstruct placement, and material assignment.
7. Confirm no `.partial-<GUID>` directory remains after success. If a safe write
   failure can be induced, confirm no final export directory is published and the
   partial directory is removed.

## 7. Heavy save/load

1. Generate a construct above the old 5,000-decoration limit.
2. Save it, exit to the main menu, reload, and compare decoration count/placement.
3. Restart FTD completely and reload again.
4. Repeat with texture-derived colors, automatic tethers, and subconstructs.
5. Review the log for serializer ceiling, corruption, cursor, or allocation errors.
6. Confirm **Memory-safe part status checks** is off on a fresh profile. Enable
   it, load a status-heavy construct, and confirm existing warnings/errors still
   appear and clear normally while the log contains no repeated
   `StatusUpdate`/`MainConstructPartStatusChecker` allocation exception. Disable
   it again and confirm the setting immediately returns to the vanilla path.

Pass: all content survives both reload paths without truncation or corruption.

## 8. Decoration Edit Mode native shell

1. Start FTD, enter build mode on a craft, and press `Ctrl+D`.
2. Confirm the mouse remains visible, build controls do not place/remove blocks
   while the editor is active, and `Esc` or `Ctrl+D` closes the editor.
   Make a dirty preview, press `Ctrl+D`, and click the toolbar **Close** button.
   Confirm both paths show the foreground
   **Unapplied decorations** prompt with **Apply and close**, **Discard**, and
   **Keep editing** buttons. The background should remain solid when modal HUD
   fading is off and fade when it is on; both states must block background
   input, with no prompt-less state where WASD/view input is captured.
3. Reopen from the Decoration Builder **Decoration Edit Mode** button.
4. Confirm the UI is the native-styled shell: compact top toolbar, independent
   **Block Palette**, **Inspector**, **Outliner**, and **Selected anchor** panels,
   compact icon headers with no oversized panel icons, compact bottom status
   strip, and readable text at the current UI scale. Confirm cyan/accent color
   marks actionable hover, selected, active, or focused states rather than
   decorating passive panel borders, and the Inspector panel title appears once.
5. On a laptop-sized or 1366x768/1600x900 window, confirm the top toolbar,
   Block Palette, right outliner, and bottom transform strip all fit without
   clipped buttons or off-screen editors. In FTD options, toggle automatic ESU
   editor scaling, adjust the manual editor scale multiplier, and use
   **Reset ESU editor layout** to return panels to their responsive defaults.
   Confirm **Fade HUD behind popups** defaults off and **Responsive paint
   palettes** defaults on. Resize each paint grid and verify responsive buttons
   fill the available width in at least two rows; turn the option off to verify
   the legacy fixed grid, then restore the default.
   Repeat specifically at 1366x768 with effective 1.44x scale and 1920x1080 at
   2x. In Decoration, Surface, Smart, and Automation, confirm dense toolbar
   labels compact without overlapping, every action still has its tooltip, and
   the notification/**Log** rail remains visible. All panel rectangles must be
   ordered, nonnegative, on-screen, and above the status strip.
6. Trigger an ESU toolbar warning, such as selecting empty space with no
   decoration center near the cursor. Confirm the toolbar slot stays fixed-height
   and text-only with no icon. If the text does not fit, confirm it shows a
   compact severity label plus **Details**/**Hide** controls that expand the full
   message below the toolbar without changing top-panel padding. Hover painted
   blocks in Deco, Surf, and Smart Builder and confirm there is no `Colored with paint #N` tooltip,
   while ESU toolbar messages still appear.
   Close ESU and confirm vanilla paint hover text returns.
7. Toggle **Pal**, **Insp**, **Out**, and **Anch** independently. Confirm hiding
   **Pal** leaves **Insp** visible, hiding **Out** leaves **Anch** visible, each
   single visible panel expands into its side stack, and paired visible panels
   split that stack.
   Select a decoration, enable **Focus deco** in the bottom selection strip, and
   confirm viewport clicks, Box select, Outliner rows, and Selected anchor rows
   cannot switch away from it until the toggle is turned off.
8. Resize the left and right panel stacks using their corner grips, switch
   Deco -> Surf -> Build -> Deco, and confirm the resized Deco panel stacks and
   independent visibility choices persist and remain clamped inside the screen.
9. Hover ESU panels and confirm WASD/mouse-look still works after leaving them.
   Scroll over ESU panels and confirm only the panel scrolls; the camera must not
   zoom. Drag the Block Palette, switch modes twice, and confirm its position
   persists.
10. Press `Ctrl+Shift+B` from vanilla build mode and confirm Smart Block Builder
   opens, remains open after the first frame, and closes only after a separate
   toolbar close or `Ctrl+Shift+B` action.
11. Press `Tab` with no dirty edits and confirm ESU switches from Decoration
   Edit Mode to Surface Builder, then to Smart Block Builder, then back to
   Decoration Edit Mode. Confirm the top toolbar keeps the same height, padding,
   notification slot width, and right-control positions across all three modes.
   Make a dirty Decoration Edit change and confirm a
   blocked mode switch makes **Apply** and **Cancel** flash without toolbar movement.
   Create a Smart Builder draft and confirm `Tab` is blocked until
   **Apply** or **Cancel**, with the same Apply/Cancel flash even when Apply is
   disabled by an invalid draft. Confirm the first toolbar button is always the
   mode toggle and shows **Deco**, **Surf**, or **Build** for the current ESU
   mode. With the FTD cursor visible, confirm `Tab` still switches between ESU
   modes; with an active text field, confirm switching remains blocked.
   Click **Log** from the notification slot in Deco, Surface, and Smart Builder;
   confirm the same console opens, can be dragged/resized, filters entries,
   copies text, clears entries, and captures hover/scroll input without moving
   the camera or placing blocks. Verify its visible/total badge, alternating
   entry rows, centered empty state, and disabled Clear/Copy controls when their
   actions have no target.
   Continue the cycle through Automation Editor and back to Decoration Edit
   Mode. Confirm the HUD never disappears between Automation Editor and
   Decoration Edit Mode, the shared notification slot stays visible through the
   handoff, and the previous ESU panel frame bridges the transition without
   accepting input.
   On a fresh player profile, the first Automation Builder open must show the
   blocking work-in-progress warning and explicitly say the feature is unfinished
   and potentially very buggy. Verify no world, panel, console, graph, camera,
   or build input passes behind it. Acknowledge it, reopen Automation Builder,
   and confirm the warning stays dismissed for that profile.
   In Automation Builder, place one loaded basic breadboard and one AI
   breadboard. If the basic item is unavailable, confirm the card explicitly
   reports its AI fallback. Select a target and then a board to create an input
   link; select the board and then a target to create an output link. Confirm the
   left shelves, direction filters, animated world lines, and `1`/`2` shortcuts
   agree, and that Delete removes only the selected link.
   Open a board with `E`. Confirm every palette card has one readable caption and
   that its full row starts a drag. At minimum, default, and maximum graph zoom,
   drag blocks to free space, a value socket, a control body, and a stack edge;
   confirm the ghost, highlighted snap outline, and final placement agree. Repeat
   after changing the ESU HUD scale, and confirm an occupied value socket rejects
   the drop without replacing its current block.
   Build and Check each supported family: Input Getter and Output Setter;
   Forever; If True and Switch > Threshold; all seven Logic Gate operations;
   Above/Below Threshold; Add/Subtract/Multiply; Max/Min; Clamp; Delay;
   Constant; Random; and Comment. Confirm the native plan names the corresponding
   vanilla component and does not mutate the breadboard before Apply.
   Build `Read -> Switch > Threshold -> Set`. Socket a Constant into then/pass,
   use a constant else value, and place the Setter in the Switch body. Check that
   the incoming stack drives `Switcher`, the Switch's selected numeric result
   feeds the Setter, and the UI never describes this as imperative branch
   execution. Put the same chain in Forever and confirm the plan explains that
   vanilla evaluates continuously and Forever is a marked Comment/layout
   container, not an ESU runtime loop.
   Exercise `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, Delete/Backspace, arrow nudging,
   Shift+Arrow nudging, `Ctrl+Z`, `Ctrl+Y`, and `Ctrl+Shift+Z`. Moving, copying,
   and duplicating a root must carry its attached value blocks, body descendants,
   and following stack. Undo/redo must restore bindings, connections, layout,
   staged links, and pending ESU-owned removals without changing already-applied
   native data. Paste the same stack repeatedly and confirm every copy cascades
   without exact overlap and its value/body/stack connections remain inside the
   matching copy. Copy a bound block between boards on the same construct and
   confirm its breadboard endpoint rebinds; paste it into a graph on another
   construct and confirm the foreign target binding is cleared.
   On an otherwise empty vanilla breadboard, Apply one ESU block and confirm the
   first native component (including native ID `0`) reopens as ESU-owned and
   Revert removes both it and its invisible marker. Create or import a
   vanilla-source wire into an ESU-owned target and confirm ESU presents it as
   read-only and blocks replacement instead of deleting or adopting that wire.
   Use Starter Flow with exactly one input and one output. Confirm it stages
   `Read -> Below Threshold -> If True -> Set` with threshold `10`, then `45`,
   and else `0`. Resolve the Getter/Setter properties, Check, Apply, reopen, and
   save/reload the craft. Compact values must remain socketed, bodies must remain
   nested, world links must rehydrate from native proxies, and a second clean
   Apply must not append duplicate native packages.
   Add a vanilla component outside the supported mapping in FtD's native editor.
   Reopen Automation Builder and confirm it appears as an opaque Advanced Native
   block; recognized imported nodes and all imported wires must also be
   read-only. Move/Arrange visual cards and Apply an ESU-owned destination, then
   verify no imported component settings, native bounds, or unrelated wires were
   rewritten. Revert must remove only ESU-owned generated components, ownership
   markers, and wires sourced from them.
   Make a dirty draft and request close. Confirm **Keep editing** and Escape
   preserve it, **Apply and close** closes only after successful validation, and
   **Close anyway** discards staged blocks/links/owned edits while leaving the
   native board unchanged. Open a graph popup first and confirm it is dismissed
   before the close prompt; the editor and console must be noninteractive behind
   that prompt, and clicks/Escape may affect only the topmost foreground.
   At 1366x768/effective 1.44x and 1920x1080/2x, slot/property/readiness popups
   must remain inside the visible graph workspace with all rows reachable by
   scrolling. Back in the world HUD, the selected-board summary must scroll while
   Input Links and Output Links each retain a complete visible row. Drag outside
   the workspace and back before release, release outside, and simulate a lost
   mouse-up by switching mode; no stray block may be created and no drag state
   may remain latched.
12. While Deco or Smart Builder is open, press Ctrl alone and confirm the vanilla
   **Press L Ctrl** control/drive HUD does not appear and the screen does not
   turn green. Confirm `Ctrl+D`, `Ctrl+Shift+B`, `Ctrl+Z`, `Ctrl+Y`,
   `Ctrl+Shift+Z`, and Surface Builder Shift-click point selection still works.
13. Press Caps Lock while ESU is open in build mode and confirm vanilla vehicle
   freeze toggles once. Press it again and confirm freeze toggles off.
14. Confirm FTD decoration wireframe is enabled while editing and restored to the
   prior profile value after closing. Cycle **View** through **Mixed**,
   **Wireframe**, **Deco only**, **Mass**, **Drag**, **Cost**, **Surface**,
   **Important**, and **Normal**. Verify modes change the intended visualisation
   without disabling block renderers.
15. In Smart Block Builder, select **Block** in the right palette, hover a target,
     and confirm a snapped wire cube ghost appears instead of only a cursor cross.
     Click empty space near the focused construct and confirm a 1x1x1 runtime
     preview appears without placing a real block and the active tool switches
     from **Add** to **Scale**. Click an occupied cell and confirm no preview is
     created there. Click an existing block face and confirm the preview seeds
     beside that face.
     Arm Block again, then click toolbar controls, panel rows, blank left/right
     panel space, scrollbars, split dividers, and the pinned footer. None of
     those clicks may place or seed a preview through the HUD; the next clear
     viewport click must still work.
16. Resize the Smart Builder left panel, close and reopen Smart Builder, and
    confirm its panel size persists, fits the current screen, and responds to
    the same ESU editor scale options. Confirm the left panel shows the Scene
    list above scrollable Selected-piece actions, both split dividers resize
    independently and persist after reopening, and selecting a Scene row updates
    the actions below it. Confirm the right Shapes/Generators browser uses its
    full height and contains no duplicate Scene or Selected shelves. Repeat at
    1366x768 with effective 1.44x scaling and 1920x1080 at 2x; both panels and
    the Apply/Cancel footer must stay between the toolbar and status strip, all
    bodies must remain reachable, and split rectangles must not overlap.
17. Use **Move** and **Scale** handles on the Smart Builder preview. Confirm
    movement/resizing snap to whole cells, the bottom strip exposes readable
    **Gizmo**, **Face**, **Edge**, and **Corner** buttons, Edge mode highlights a
    hovered edge instead of drawing plus markers everywhere, WASD/mouse-look still
    work while idle, camera input is suppressed only while dragging a handle,
    mouse wheel still zooms in the scene, and middle mouse shows the FTD cursor
    without closing or canceling the mode. Open the gear beside **Smart Block
    Builder** and confirm it exposes the same saved gizmo settings as Deco and
    Surface modes.
18. In Smart Builder, choose **Down slope** size 1-4 from the right palette, hover
    a target, and confirm a sloped placement ghost appears. Place a wide slope
    from a wide block face and confirm its lanes line up with the source face.
    click empty space on the selected draw plane and confirm it seeds a snapped
    placement ghost there.
    Toggle **Support: Step**, Apply, and confirm one support block is placed
    directly under each down-slope cell. Repeat with **Support: Full** and confirm
    vertical support fill appears under raised slope sections. Right click while adding and confirm existing preview pieces
    remain; press toolbar **Cancel** and confirm the full scene clears.
19. Toggle **Skip** and **Block** occupancy modes. Confirm **Skip** fills only
     empty cells and reports skipped occupied cells, while **Block** rejects an
     overlapping preview. Press **Apply** on a preview touching the construct and
     confirm real blocks are spawned only at that point. Move/scale a preview so
     it no longer touches the construct and confirm Apply is blocked with a clear
     connection message.
20. Click near an existing decoration center and confirm the selected decoration
   gets a yellow center marker, magenta anchor line, and red/green/blue XYZ
   move handles in **Move** mode. Orbit around the craft and confirm those colors
   do not shift with lighting or camera direction.
20. Select the same decoration from the right outliner and confirm viewport
   selection, outliner highlight, Inspector values, and status strip
   agree.
21. Drag each axis handle and confirm movement snaps in 0.05 m steps, cannot
    exceed +/-10 positioning, and becomes a dirty preview until **Apply**.
    Toggle **Global**/**Local** and confirm Move, Rotate, and Scale handles
    change orientation; Anchor remains block-axis based. Confirm the bottom
    Position, Rotation, and Scale fields update live while dragging.
22. Press `Ctrl+Z`, toolbar **Undo**, `Ctrl+Y`, and `Ctrl+Shift+Z` after move,
   anchor, mesh, color, and material edits. Confirm undo/redo restores the same
   decoration fields and one drag creates one undo step.
23. Edit color and material in the Inspector panel, use
    **Clear** to remove an active material override, then edit
    position, rotation, and scale from the compact bottom status panel.
    Confirm locale comma/point decimal input works, NaN/infinity are rejected,
    color clamps to 0-31, invalid material GUID text is rejected, and no
    horizontal scrollbar appears. Confirm owner, mesh, GUID, tether, and
    material metadata remain in the Inspector, while editable transform fields
    only appear in the bottom status panel.
    Switch to Paint, drag a viewport selection across several eligible
    decorations and blocks, and confirm every target inside is colored while
    targets outside remain unchanged. Starting, ending, or continuing the drag
    over an ESU panel must not paint or place through that panel. Undo and Redo
    must restore the decorations, blocks, marquee selection, and primary as one
    action; Cancel must restore every pre-session block color, while Apply must
    retain the complete mixed paint gesture after reopen. Repeat once as host
    and client to confirm block restoration follows the multiplayer paint path.
24. Use the **Selected anchor** panel and each anchor button (`+/-X`, `+/-Y`,
    `+/-Z`) and confirm the tether changes by one block while the visible
    decoration remains in place. In Anchor tool mode, confirm ESU draws lines
    from the selected tether to all decorations sharing that tether, and clicking
    another decoration selects it without leaving Anchor mode. Confirm invalid
    shifts are rejected. Open the top-toolbar **Anchor** dropdown, enable anchor
    follow, set 1 m/2 m/3 m/5 m ranges, then select another decoration and
    confirm the dropdown stays open until toggled closed. Move a decoration far
    enough from its tether over nearby valid blocks and confirm the tether
    follows while the visible decoration stays in place.
25. Use the left Block Palette with **Build** off in list and 3D preview-grid modes. Confirm the
    mesh count strip shows total and currently shown meshes, updates while
    changing kind filters/search text, the 3D grid fills visible thumbnails
    progressively instead of freezing to load the full mesh catalog, hover
    previews draw in front of the editor, and moving the mouse over Inspector
    details does not keep or change a mesh preview.
26. Confirm **Build** appears immediately left of **Hide** and is off when the
    editor opens while the header remains **Block Palette** in either state.
    Enable it and confirm Objects and non-block entries are not offered, and
    locked or construct-incompatible entries are rejected without changing the
    active block or falling back to Wood. Select several usable entries with both
    left and right palette clicks. Confirm neither click places through the HUD. In the
    viewport, the full-size selected block marker must follow the free mouse
    cursor before placement and match the exact block, rotation, cell, and
    footprint that left click commits. Test all six faces on the main craft and
    again while focused on a rotated subobject, plus rotation controls,
    mirror controls, attachment/collision rejection, occupied and invalid
    targets, resources, native undo/redo, and one block whose native footprint
    spans multiple cells. The marker must hide over every ESU panel, popup, and
    text field, and an invalid or hidden marker must never place or remove a
    block. Orbit the camera while clicking and confirm placement uses the final
    current mouse ray, never the preceding frame. Hold Tab until the rotation
    markers appear, then cross onto a panel, open a modal, point at empty space,
    and exit Build; every rotation marker must disappear. Native attachment
    indicators are intentionally absent at both close and far orbit distances.
    With Simple Build Mode enabled, Shift+LMB must not replace or remove a block;
    cursor-follow mode deliberately disables that unsafe gesture. Arm PermaBuild
    over an occupied/invalid outside cell and confirm it cannot enter hold-remove
    or delete anything. Focus the search field, type digits plus movement and
    orientation keys, and confirm neither the hotbar selection nor build marker
    changes. Click the viewport once and confirm that click only leaves the field;
    the next click may build or sample. Native controls must resume without a
    delayed step, rotation, placement, or removal. Right-click blocks already on
    the craft and on a
    subobject; confirm type and rotation are sampled without deleting the block
    or opening a decoration context menu, and that the marker changes to the
    sampled item/rotation immediately without showing its old mesh. Change the
    active item through FtD's native hotbar/inventory and confirm the marker
    refreshes immediately as well. Move from an ESU panel back into the viewport
    and confirm the marker reappears at the current mouse target rather than its
    last pre-panel transform. Escape, Paint, Surface, editor close,
    and mode handoff must leave Block Palette mode cleanly. Save/reload, then
    repeat once as multiplayer host/client and confirm normal FtD replication.
    If practical, destroy or lose control/team ownership of the focused craft
    while the marker is active and confirm FtD exits/cleans build state without a
    stale ghost or command.
27. Disable **Build** and confirm the title remains **Block Palette**. Click a
    row to enter decoration placement mode. Confirm the translucent mesh-shaped
    ghost follows the pointer, snaps to the pointed craft block, shows
    valid/invalid feedback, and left click creates one decoration anchored to that
    block. Confirm right click and Esc cancel placement without creating a
    decoration. Place a decoration center behind a main-craft block and another
    behind a subobject block. With **X-ray** off, hover, left/Shift-click,
    right-click context, Paint, short Box fallback, and full Box drag must not
    acquire either hidden center; a slightly farther visible center must still
    win. Turn X-ray on and confirm those same viewport paths can acquire the
    hidden centers. Turn it off again and confirm Outliner and Selected anchor
    rows can still select and edit them. The target decoration's own tether block
    must not falsely hide an exposed center on a wedge/partial-block surface,
    while moving that center to the far side of the same tether block must make
    X-ray-off reject it. Drag an X-ray-off box over exactly 512 and then more than
    512 projected decoration centers: the first may resolve visibility, while the
    second must reject before any selection or mixed paint change; the live label
    must say it is a projected count. Close and reopen the editor and confirm
    X-ray has returned to its safe off default.
28. Click the Decoration Edit toolbar **X** symmetry button, then click a real
   craft block to place the red X symmetry plane. Place a mesh on one side of
   the plane and confirm ESU creates the original plus one mirrored decoration.
   Enable **Y** as well, place another mesh, and confirm four total decorations
   are created with no duplicates when a target lies on a plane. Remove or choose
   a missing mirrored tether block and confirm the whole placement is rejected,
   including the original. Switch Deco -> Surf -> Build -> Deco and confirm placed
   planes persist; close ESU and confirm they clear on reopen.
29. With an X plane active, select one decoration from a mirrored pair and move
   it with the Move gizmo. Confirm the matching mirrored decoration follows on
   the other side and undo/redo treats both moves as one action. Repeat with the
   position inspector fields and with an anchor move. Delete or duplicate the
   mirrored counterpart and confirm ESU skips live follow instead of moving
   unrelated decorations on the same anchor. Remove the mirrored target block and
   confirm the linked movement is rejected/restored.
30. In Smart Block Builder, place the same X/Y planes, create a runtime preview,
   and confirm mirrored translucent preview cells and outlines appear before
   Apply. Confirm **Skip** and **Block** occupancy modes apply to the combined
   mirrored preview, every mirrored component must touch the existing construct,
   and Apply places all mirrored blocks atomically or none.
31. In Surface Builder, place an X symmetry plane from the Surface toolbar and
   first confirm **Draw** is absent beside the left Draft header and is the first
   right-panel Extra Tools button. Switch Draw -> Path -> Circle -> Draw and
   confirm only the active creation button lights each time. Then confirm the
   plane wire spans the vehicle's Y/Z size instead of a tiny local
   square. Create a triangular surface draft on one side and confirm the mirrored
   preview appears. Press **Place** and confirm original plus mirrored surface
   decorations are created as one atomic batch. Enable X+Y and confirm four
   variants; put a draft on a plane and confirm no duplicate placement; remove
   mirrored-side anchor blocks and confirm the whole placement is rejected.
32. Undo and redo the created decoration. Confirm undo removes it and redo
   recreates it with the same mesh/tether/fields.
33. Press **Cancel** and verify tether, position, scale, orientation, mesh, color,
   and material return to their original values. Repeat and press **Apply**,
   then save/reload and verify the edit persists.
34. Search for a known FTD item/object mesh, switch All/Items/Objects/Recent
    filters, hover it, and confirm the preview card plus rotating wire preview
    appear near the cursor. Select it and confirm the selected decoration changes
    mesh as a dirty preview.
35. Load a decoration-heavy construct and confirm the outliner remains responsive
    and viewport hints are capped rather than drawing every decoration.
36. Repeat with AdvancedMimicUi installed and confirm its decoration UI still
    opens and works after ESU edit mode is closed.
37. Select one decoration and use Inspector **Settings Copy**. Paste in the
    vanilla/Advanced Mimic UI and compare mesh, position, rotation, scale,
    color, material, and hide-mesh state. Copy there and paste onto a multi-
    selection in ESU; confirm all explicit targets change atomically while each
    tether and runtime identity stays unchanged. Include a valid native offset
    between 10 m and 20 m, and force one invalid/stale target if reproducible;
    the full batch must restore on failure.
38. Select several decorations in a deliberately shuffled order and use
    **Decorations Copy selection** / **Paste in place**, then repeat with
    `Ctrl+C/V` and explicit `Ctrl+Shift+C/V`. Confirm exact in-place clones are ordered primary first,
    selected as one Move group, do not expand active symmetry, and can be pasted
    repeatedly. Confirm one undo removes the entire group and redo recreates and
    reselects it. Apply, Cancel, close/reopen ESU, and verify the memory clipboard
    remains available; focus another subconstruct/main construct and confirm the
    same clipboard is rejected without mutation. Delete a copied tether block
    and repeat the rejection test. Finally, select one decoration, press
    `Ctrl+C/V`, and confirm the configured native shortcut returns to settings
    copy/paste instead of creating another decoration.
39. Select several decorations, then right-click one selected row in both the
    **Outliner** and **Selected anchor** lists. Confirm the cursor menu opens at
    the row, retains the group, promotes that row to primary, and offers
    **Select only this**, Move/Rotate/Scale, active-state **Focus deco**, native
    settings Copy/Paste, **Duplicate selection**, and **Delete selection**.
    Confirm Focus deco appears above Copy settings, lights while enabled, and
    Copy selection is absent from this menu but still works from Inspector and
    its shortcuts. Move the menu over the bottom numeric fields and click
    **Delete** plus the other overlapping menu rows; only the menu action may
    run, with no field focus/value change or camera/build input. Repeat the
    foreground-priority check for Surface, Smart Builder, Automation Builder,
    and an Automation graph-node context menu. Exercise each applicable action.
    Duplicate must create exact primary-first in-place clones
    without replacing either clipboard; delete must include deduplicated active
    symmetry counterparts, and each batch must undo/redo as one action.
    Right-click an unselected row and confirm it becomes the only target; verify
    construct headers do not open the menu and Focus deco blocks another row.
    For one decoration, confirm the action remains **Duplicate in place**.
    Finally, leave Box mode active after a completed viewport box selection and
    right-click a selected decoration center, the group gizmo between centers,
    and inside the cyan group bounds: the same multi-selection menu must open
    before Box-off fallback. Verify an unfinished Box drag still cancels,
    while empty-space right-click can disable Box without clearing the group.
40. In Deco, Surface, and Smart Builder modes open the bottom-left Gizmo settings
    gear. Confirm preferences are shared across all three editors, then exercise
    Move/Rotate/Scale size and Thickness at 0.5x and 3x, Click area at 8px and
    40px, Set, Reset defaults, Close, and Escape. Reopen ESU and restart FtD to
    confirm profile persistence. While open, verify background buttons, fields,
    scroll views, build actions, camera/WASD, and mouse wheel do not respond.
    Repeat with **Fade HUD behind popups** off and on. Off must keep the editor
    visually solid; on must fade it. Input ownership, outside-click dismissal,
    Escape priority, and console blocking must remain identical.
    Repeat at 1366x768, normal desktop resolution, automatic HUD scale, and 200%
    manual HUD scale; no controls may overlap or leave the screen.
41. At near/far camera distances and strong foreshortening, hover and click the
    base, middle, and tip of Move, Scale, Smart Builder Move/Scale, Surface/generator point, decoration
    anchor, and shared-anchor shafts. Confirm collapsed axes are ignored without
    disabling visible axes, the highlight predicts the click, the center free-
    move core is only a small target, and the whole shaft remains selectable.
    Repeat identical pixel drags at every size and confirm translation, scale,
    anchor stepping, and rotation sensitivity do not change.
42. In Surface Builder use the left-panel **Coordinates** shelf with a point,
    edge, face, generator path point, and generator
    shape center. Confirm the header remains visible, vector/range rows scroll,
    and the bottom strip only points to this editor while retaining its existing
    snap controls and height. Type X/Y/Z with dot and comma decimals; verify
    nothing changes before Enter, Revert reloads current values, successful
    values normalize to 0.001 m, shared indexes propagate, and each Enter text
    commit is one undo step. Drag every existing-point slider and confirm live 0.001 m
    updates, one undo command per complete drag, atomic rejection of invalid
    intermediate faces, and full camera/build/text/wheel capture. Revert must
    restore the selection-time coordinates without rolling back unrelated settings.
43. Confirm Coordinates defaults hidden, the header is only **Coordinates**,
    and it has neither New triangle nor Apply text actions. Verify its compact
    **Coordinates | Show** header remains
    pinned above Surface settings and the Draft list fills the released area,
    then use Show. Drag the Draft/Coordinates divider to both extremes and
    reopen the editor; the custom split must persist, Draft controls/list/hints
    must not overlap Coordinates, and resizing Coordinates should not resize the
    Draft list until its true minimum workspace is reached. The automatic split
    must not leave a second list-sized empty gap below the final draft hint.
    Leave Coordinates open, add/remove points and triangles, resize the panel,
    and change HUD scale. Draft must immediately occupy all remaining height,
    keep its rows reachable, and never overlap Coordinates or leave unused space.
44. Set independent X/Y/Z ranges, restart FtD, and verify profile persistence.
    Reject non-finite, reversed, equal, and sub-0.001 m ranges, then use **Reset
    -10/+10**. Select/stage values below -10 and above +10 across A/B/C; the
    effective limits must expand for the current target without clamping values
    or altering the saved limits. Invalid/incomplete coordinate text disables
    only its slider and remains unchanged. Exercise the `-step` / `+step`
    buttons and right-click chooser on every axis; every button must display its
    configured amount and right-click must not apply a normal step. Include custom dot/comma
    input and 1/8, 1/4, and 1/2 presets; verify independent profile persistence,
    one undo step per click, and safe rejection outside 0.001-1000 m. Finish at
    1366x768, normal resolution, and 200% HUD scale with Preview/Place, symmetry,
    undo/redo, save/reload, and no overlap with pinned material/action shelves.
45. With point, edge, face, generator path point, and generator center targets,
    hover every coordinate vector header and slider row. Confirm the row lights
    up and a bright marker identifies exactly that bound point on the construct;
    leaving Coordinates or collapsing it must clear the marker.
46. Confirm Extra Tools is a full-width three-by-four grid containing Draw,
    Path, Circle, Arc, 2D cone, Sphere, Part sph, Quad, Cone, Frustum, Polygon,
    and Tube. For every Extra Tool, click **Preview** and confirm the selected
    decoration mesh replaces the line-only result at the exact planned
    transforms while cyan guides and edit handles remain visible. Repeat with
    X/Y/Z mesh axes, limited and rotated arcs, nearest/same anchoring, paint,
    material override, and symmetry; **Place** must match Preview. Changing the
    draft or symmetry must remove stale meshes until Preview is clicked again.
    Exercise Quad width/height and aspect-preserving Scale; Polygon
    radius and 3/12-side boundaries; and straight/bent Tube paths with diameter
    and 3/8/64 sides. For all three, verify move/rotate where applicable, mesh and
    strut diameter, paint/material, nearest/same anchor, symmetry, Preview/Place,
    Clear/Delete, undo/redo, invalid-input rejection, capacity rollback, and no
    Tube frame flips at bends. Exact and near-180-degree backtracks, including a
    direction dot product of `-0.999`, must identify and reject the offending
    path point. Confirm 781 points x 64 sides produces 99,904 segments, while
    782 x 64 and aggregate two/eight-variant symmetry output above 100,000 reject
    before placement allocation.
    On a plan above the exact-preview budgets, confirm sampled meshes, guide
    segments, and Same-anchor hints span the entire shape without a frame-time
    collapse, while Place still uses the complete uncapped plan.
    Resize Extra Tools vertically and horizontally and confirm its controls and
    scroll workspace use the full available panel rather than retaining a large
    empty body. For several active craft-palette colors, Preview and Place a
    triangle and confirm both resolve exactly the same color entry.

Pass: modal input, FTD-styled shell, Smart Builder runtime previews,
outliner/inspector synchronization, move preview, anchor retether, mesh
assignment, auto scaling, panel resizing, XYZ symmetry placement,
live symmetry follow, add-decoration flow, Apply/Cancel, focus restore, and save/load all
behave without stuck cursor, lost keyboard, unintended block placement,
performance stutter, or serializer HUD regressions.

## 9. Beamification bundled tool

1. In the packaged mod folder, confirm `Tools/Beamification` exists and contains
   `README.md`, `LICENSE`, `requirements.txt`, `__main__.py`, and `src`.
2. Create a Python virtual environment outside the mod folder and install:
   `python -m pip install -r Tools/Beamification/requirements.txt`.
3. Copy a disposable armour-heavy `.blueprint` and run the bundled CLI with a
   new output path, for example `beamify --grain xyz --bias random`.
4. Load the output blueprint in FTD and inspect armour orientation, material
   family, colour preservation, and absence of obvious missing blocks.
5. Run the bundled `debeamify` command on a copy and confirm it converts eligible
   armour back toward 1 m variants. This specifically checks ESU's local CLI flag
   fix.
6. Save the converted craft under a new name, reload it, and confirm the ESU
   serializer HUD/load-save status remains normal.

Pass: Beamification remains an external tool, does not affect FTD startup, and
produces loadable converted blueprints from disposable test inputs.

## 10. Multiplayer

1. Install the exact same 1.0.7 DLL on host and every client; compare SHA-256.
2. Join with a decoration-heavy construct and verify initial synchronization.
3. Generate, cancel, move a tether, save, and reload while connected.
4. Disconnect and reconnect each client and compare decoration state.
5. Review every peer log for serialization, synchronization, or Harmony errors.

Pass: all peers remain synchronized and stable with the same mod version.

## Acceptance record

| Section | Required | Result | Evidence/notes |
| --- | --- | --- | --- |
| Startup and registration | Yes | Not run | |
| Legacy compatibility | Yes | Not run | |
| OBJ/geometry/locale | Yes | Not run | |
| Generation lifecycle/concurrency | Yes | Not run | |
| Tether transaction | Yes | Not run | |
| OBJ export | Yes | Not run | |
| Heavy save/load | Yes | Not run | |
| Decoration Edit Mode native shell | Yes | Not run | |
| Beamification bundled tool | Yes | Not run | |
| Multiplayer | Yes | Not run | |

For a failure, retain the input OBJ/texture, affected construct, complete log,
exact reproduction steps, and whether reopening the save changes the result.
