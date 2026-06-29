# EndlessShapes Unlimited 1.0.0 in-game acceptance plan

Test target: From The Depths 4.3.3

Installed mod folder: `F:\FTD saves\From The Depths\Mods\EndlessShapesUnlimited`

Expected assembly: `EndlessShapesUnlimited.dll`

Expected assembly SHA-256:
`506F462A23C2B91CC15C37043A584E6C22131699CEA5FDBC0FB2AB546405EA37`

Do not merge the draft PR or publish a release until every required section
passes. Record the FTD build, test date, result, and evidence for each section.

## Preparation

- Back up any construct used for legacy or high-decoration testing.
- Confirm standalone `DecoLimitLifter` and `EndlessShapes2` are absent or disabled.
- Confirm `EndlessShapes Unlimited 1.0.0` is enabled. The separately installed
  `AdvancedMimicUi` mod is not a dependency; the required input helper is bundled.
- Close FTD before replacing mod files.
- Preserve the full FTD log for every failed test.

## 1. Startup and registration

1. Start FTD and reach the main menu.
2. Confirm the log contains the successful `EndlessShapes Unlimited v1.0.0`
   message exactly once.
3. Confirm the Alerts panel contains `EndlessShapes Unlimited v1.0.0 Active!`.
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

Pass: all content survives both reload paths without truncation or corruption.

## 8. Decoration Edit Mode native shell

1. Start FTD, enter build mode on a craft, and press `Ctrl+D`.
2. Confirm the mouse remains visible, build controls do not place/remove blocks
   while the editor is active, and `Esc` or `Ctrl+D` closes the editor.
3. Reopen from the Decoration Builder **Decoration Edit Mode** button.
4. Confirm the UI is the native-styled shell: compact top toolbar, independent
   **Mesh Palette**, **Inspector**, **Outliner**, and **Selected anchor** panels,
   compact icon headers with no oversized panel icons, compact bottom status
   strip, compact cyan rows, and readable text at the current UI scale. Confirm
   the Inspector panel title appears once.
5. On a laptop-sized or 1366x768/1600x900 window, confirm the top toolbar,
   mesh palette, right outliner, and bottom transform strip all fit without
   clipped buttons or off-screen editors. In FTD options, toggle automatic ESU
   editor scaling, adjust the manual editor scale multiplier, and use
   **Reset ESU editor layout** to return panels to their responsive defaults.
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
8. Resize the left and right panel stacks using their corner grips, switch
   Deco -> Surf -> Build -> Deco, and confirm the resized Deco panel stacks and
   independent visibility choices persist and remain clamped inside the screen.
9. Hover ESU panels and confirm WASD/mouse-look still works after leaving them.
   Scroll over ESU panels and confirm only the panel scrolls; the camera must not
   zoom. Drag the mesh palette, switch modes twice, and confirm its position
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
12. While Deco or Smart Builder is open, press Ctrl alone and confirm the vanilla
   **Press L Ctrl** control/drive HUD does not appear and the screen does not
   turn green. Confirm `Ctrl+D`, `Ctrl+Shift+B`, `Ctrl+Z`, `Ctrl+Y`,
   `Ctrl+Shift+Z`, and Surface Builder Ctrl-click behavior still work.
13. Press Caps Lock while ESU is open in build mode and confirm vanilla vehicle
   freeze toggles once. Press it again and confirm freeze toggles off.
14. Confirm FTD decoration wireframe is enabled while editing and restored to the
   prior profile value after closing. Cycle **View** through **Mixed**,
   **Wireframe**, **Deco only**, **Mass**, **Drag**, **Cost**, **Surface**,
   **Important**, and **Normal**. Verify modes change the intended visualisation
   without disabling block renderers.
15. In Smart Block Builder, select a supported 1x1x1 or known armour block,
     click empty space near the focused construct, and confirm a 1x1x1 translucent
     voxel preview appears on the selected draw plane without placing a real
     block and the active tool switches from **Draw** to **Scale**. Click an
     occupied cell and confirm no preview is created there. Click an existing
     block face and confirm the preview seeds beside that face.
16. Resize the Smart Builder left panel, close and reopen Smart Builder, and
    confirm its panel size persists, fits the current screen, and responds to
    the same ESU editor scale options.
17. Use **Move** and **Scale** handles on the Smart Builder preview. Confirm
    movement/resizing snap to whole cells, WASD/mouse-look still work while idle,
    camera input is suppressed only while dragging a handle, mouse wheel still
    zooms in the scene, and middle mouse shows the FTD cursor without closing or
    canceling the mode.
18. Toggle **Skip** and **Block** occupancy modes. Confirm **Skip** fills only
     empty cells and reports skipped occupied cells, while **Block** rejects an
     overlapping preview. Press **Apply** on a preview touching the construct and
     confirm real blocks are spawned only at that point. Move/scale a preview so
     it no longer touches the construct and confirm Apply is blocked with a clear
     connection message.
19. Click near an existing decoration center and confirm the selected decoration
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
25. Use the left mesh palette in list and 3D preview-grid modes. Confirm the
    mesh count strip shows total and currently shown meshes, updates while
    changing kind filters/search text, the 3D grid fills visible thumbnails
    progressively instead of freezing to load the full mesh catalog, hover
    previews draw in front of the editor, and moving the mouse over Inspector
    details does not keep or change a mesh preview.
26. Click a mesh row to enter placement mode. Confirm the translucent mesh-shaped
   ghost follows the pointer, snaps to the pointed craft block, shows
   valid/invalid feedback, and left click creates one decoration anchored to that
   block. Confirm right click and Esc cancel placement without creating a
   decoration.
27. Click the Decoration Edit toolbar **X** symmetry button, then click a real
   craft block to place the red X symmetry plane. Place a mesh on one side of
   the plane and confirm ESU creates the original plus one mirrored decoration.
   Enable **Y** as well, place another mesh, and confirm four total decorations
   are created with no duplicates when a target lies on a plane. Remove or choose
   a missing mirrored tether block and confirm the whole placement is rejected,
   including the original. Switch Deco -> Surf -> Build -> Deco and confirm placed
   planes persist; close ESU and confirm they clear on reopen.
28. With an X plane active, select one decoration from a mirrored pair and move
   it with the Move gizmo. Confirm the matching mirrored decoration follows on
   the other side and undo/redo treats both moves as one action. Repeat with the
   position inspector fields and with an anchor move. Delete or duplicate the
   mirrored counterpart and confirm ESU skips live follow instead of moving
   unrelated decorations on the same anchor. Remove the mirrored target block and
   confirm the linked movement is rejected/restored.
29. In Smart Block Builder, place the same X/Y planes, create a runtime preview,
   and confirm mirrored translucent preview cells and outlines appear before
   Apply. Confirm **Skip** and **Block** occupancy modes apply to the combined
   mirrored preview, every mirrored component must touch the existing construct,
   and Apply places all mirrored blocks atomically or none.
30. Undo and redo the created decoration. Confirm undo removes it and redo
   recreates it with the same mesh/tether/fields.
31. Press **Cancel** and verify tether, position, scale, orientation, mesh, color,
   and material return to their original values. Repeat and press **Apply**,
   then save/reload and verify the edit persists.
32. Search for a known FTD item/object mesh, switch All/Items/Objects/Recent
    filters, hover it, and confirm the preview card plus rotating wire preview
    appear near the cursor. Select it and confirm the selected decoration changes
    mesh as a dirty preview.
33. Load a decoration-heavy construct and confirm the outliner remains responsive
    and viewport hints are capped rather than drawing every decoration.
34. Repeat with AdvancedMimicUi installed and confirm its decoration UI still
    opens and works after ESU edit mode is closed.

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

1. Install the exact same 1.0.0 DLL on host and every client; compare SHA-256.
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
