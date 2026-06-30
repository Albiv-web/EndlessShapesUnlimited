# EndlessShapes Unlimited

EndlessShapes Unlimited is a From The Depths mod that combines the hardened
DecoLimitLifter serializer with the OBJ import/export and decoration-building
tools from EndlessShapes2.

The combined mod is maintained by **Albiv-web**. EndlessShapes2 was created by
**Huwa / huwahuwa** and is included with the author's permission under the MIT
License.

## Features

- Raises the per-construct decoration limit from 5,000 to 100,000.
- Grows FTD serializer buffers on demand while preserving already-written data.
- Keeps vanilla-compatible serialization for ordinary constructs.
- Imports OBJ vertices, UVs, faces, lines, groups, and negative indices.
- Converts selected OBJ mesh groups into FTD decorations.
- Supports bounded textures, local-origin projection, automatic tether placement,
  animated progress, and explicit cancel-with-rollback.
- Adds an atomic character tool for moving tether blocks with linked decorations.
- Adds staged, multi-material OBJ export beside FTD's existing STL export.
- Adds an optional native vehicle-HUD section for whole-craft decoration counts,
  peak serializer usage, live wire-format forecasts, and exact last load/save
  formats.
- Adds a native-looking Decoration Edit Mode shell with FTD-styled toolbar,
  decoration outliner, inspector, mesh browser, focus view, and Apply/Cancel
  transactions.
- Registers an `EndlessShapes Unlimited v1.0.0 Active!` entry in FTD's Alerts
  panel after transactional startup succeeds.

## Requirements

- From The Depths 4.3.3.
- Every multiplayer peer loading an extended-format construct must use the same
  EndlessShapes Unlimited version.
- The standalone `DecoLimitLifter` and `EndlessShapes2` mods must be removed.
  The manifest declares both as conflicts to prevent duplicate patches, classes,
  and asset GUIDs.

## Installation

1. Remove standalone DecoLimitLifter and EndlessShapes2 folders from the FTD
   `Mods` directory.
2. Copy the complete `EndlessShapesUnlimited` folder into the FTD `Mods`
   directory.
3. Start FTD and confirm the log reports `EndlessShapes Unlimited v1.0.0`.

The runtime folder contains one mod assembly, `EndlessShapesUnlimited.dll`, and
the Harmony 2.3.5 runtime dependency.

## Using the OBJ tools

### Import and generate decorations

1. Place the **Decoration Builder** from the Decorations inventory tab.
2. Open its secondary interaction screen.
3. Enter an OBJ path and select **Load**.
4. Select a mesh group and configure transform, texture, tether, local-origin,
   and optional animation settings.
5. Confirm the plan. Animated builds show progress and a cancel action that
   deletes the run's decorations and undoes its block placements.

OBJ numbers are parsed with invariant culture, so decimal points work even when
Windows uses comma decimals. The importer accepts `v`, `vt`, `f`, `l`, `o`, and
`g` records, common face forms such as `v/vt/vn` and `v//vn`, comments, extra
whitespace, and negative indices. Pasted paths preserve spaces, accept matching
quotes, and report expected file/format mistakes without a full-screen stack trace.

Importer safety ceilings are 256 MiB, 2,000,000 vertices, and 100,000 face/line
records. PNG/JPEG textures are limited to 64 MiB encoded, 8,192 pixels per axis,
and 16,777,216 total pixels. Degenerate, concave, non-finite, collapsed, or
over-cap geometry rejects the selected mesh with its OBJ source line.

### Move tether blocks

Equip the **Deco** character item, point at the EndlessShapes tether block, and
use left/right click to move it one camera-relative axis step. The tool preflights
every linked decoration and aborts if any new offset would exceed +/-10. Block
commands and decoration properties are one transaction and roll back in reverse
order on failure.

### Export a construct

Open the construct information General tab and select **Create an OBJ file of
this vehicle**. The mod writes OBJ, MTL, and only referenced textures beneath the
FTD profile directory using invariant decimal formatting. Multi-material carried
objects retain their submesh/material mapping. Output is staged and becomes
visible only after every file is written successfully.

## Serialization compatibility

The mod preserves FTD's original format until a construct exceeds a vanilla
metadata boundary:

- Up to 9,362 header records remain legacy-compatible.
- Up to 6,553,500 sorted data bytes remain legacy-compatible.
- Larger payloads use the mod's sentinel format and require this mod to load.

Configured ceilings are 4 MiB of header data, 64 MiB of sorted data, and a
256 MiB destination buffer. Malformed, truncated, inconsistent, or oversized
sentinel payloads are rejected before committing loader state.

### Serialization HUD

The native right-side vehicle HUD can append a compact **ESU serialization**
section for the focused main construct and all of its subconstructs. It is off by
default. Enable it under the **Endless Shapes Unlimited** options screen or use
the configurable **Toggle serialization HUD** binding, which defaults to `F8`.
Both the visibility setting and key mapping are profile-persisted.

The HUD reports total decorations, the busiest individual decoration manager,
peak expected header/data bytes, the current `LEGACY WIRE`, `SENTINEL`, or
`OVER LIMIT` forecast, and the exact formats last loaded and serialized. Limits
apply per serialized container, so the displayed byte values are peaks rather
than misleading sums. `~` and `EST` identify a forecast after the craft changes;
loaded/saved labels remain exact observations. Sentinel is informational and
means this mod is required to load the extended container.

Serialization HUD fields:

- **Decorations** shows the total decoration count on the focused craft,
  including subconstructs.
- **Manager max** shows the busiest single decoration manager against the
  100,000 ESU manager limit.
- **Header/Data peaks** show the largest predicted serialized header and sorted
  data buffers for any one saved container.
- **Wire format** shows whether the next save is expected to remain
  vanilla-compatible, require ESU sentinel loading, or exceed ESU's configured
  limits.
- **Loaded/Saved** show exact observed formats from the last load/save; forecast
  rows are estimates until the craft is serialized again.

### Decoration Edit Mode

Press `Ctrl+D` in build mode, or use the Decoration Builder's
**Decoration Edit Mode** button, to open the modal editor. The native-styled
overlay provides a top toolbar, right-side decoration outliner, selected
decoration inspector, selected-anchor context, mesh palette, and a compact
bottom status strip. The **Pal**, **Insp**, **Out**, and **Anch** toolbar buttons
toggle those panels independently while preserving the same left/right stack
areas. Press `Tab` while the ESU editor is clean to cycle from Decoration Edit
Mode to Surface Builder to Smart Block Builder; apply or cancel live edits first.
The overlay auto-scales on smaller
game windows, has an options-screen manual scale multiplier plus layout reset,
and the left/right panel stacks can be resized in-game. The top toolbar keeps a
stable mode/notification slot across Decoration Edit, Surface Builder, and Smart
Builder; notification text has no icon, the slot stays fixed-height, and long
messages show a **Details** button without changing top-panel padding. The same
slot includes a fixed **Log** button that opens the in-memory ESU console across
Decoration Edit Mode, Surface Builder, and Smart Builder. A blocked mode switch
makes **Apply** and **Cancel** flash without toolbar movement. ESU-owned cursor
tooltips appear near the cursor after hovering interactive controls for two
seconds; clicks, scrolls, drags, panel resizing, and transform dragging reset the
tooltip timer without handing control back to vanilla hover popups.

Decoration Edit Mode HUD reference:

- **Deco** is the first toolbar slot and indicates Decoration Edit Mode. Press it
  or `Tab` while clean to switch into Surface Builder.
- **Select** selects decorations from the viewport or outliner.
- **Move** shows snapped X/Y/Z movement handles plus center freeform movement.
- **Rotate** shows projected rotation rings and snaps edits to 5 degrees.
- **Scale** shows X/Y/Z scale handles and snaps edits to 0.05.
- **Local/Global** changes transform handles between decoration-local axes and
  construct/global axes.
- **X**, **Y**, and **Z** toggle construct-local symmetry planes for new
  placement and matched existing-decoration edits.
- **Anchor** opens anchor retether tools. Retethering moves the tether block
  while preserving the decoration's visual world position.
- **Paint** enables viewport click painting and material/color replacement.
- **View** opens the ESU/native view menu for mixed, wireframe, decoration-only,
  mass, drag, cost, surface, important, and normal views.
- **Warning/Details/Log** is the fixed notification slot. Short messages appear
  inline, long messages open through **Details**, and **Log** opens the shared
  ESU console with filters, copy, clear, drag, and resize controls.
- **Pal**, **Out**, **Insp**, and **Anch** toggle the Mesh Palette, Outliner,
  Inspector, and Selected Anchor panels.
- **U/R** undo and redo un-applied editor actions.
- **Apply** commits previewed decoration edits, **Cancel** restores the current
  preview, and **Close** exits the mode.

Decoration Edit panels and bottom strip:

- **Mesh Palette** lists every available mesh. **List** is a cheap searchable
  text list; **3D grid** renders only visible preview cards. **All**, **Items**,
  **Objects**, and **Recent** filter the catalog. Clicking a mesh starts
  placement, or swaps the selected decoration's mesh/material context.
- **Outliner** groups decorations by construct and subconstruct. Row clicks
  select decorations, **Pins off/on** toggles tether pins, and **Refresh**
  rebuilds the visible list.
- **Inspector** edits the selected decoration's color, material override, mesh,
  owner details, and exact transform fields that are not handled by the bottom
  quick strip.
- **Selected anchor** lists decorations sharing the selected tether block and is
  used with anchor follow/retether workflows.
- **Bottom status strip** shows mode, current view/tool, selection, dirty state,
  undo/redo counts, save-format forecast, decoration count, manager peak, and
  the live Position/Rotation/Scale X/Y/Z editors. **Anchor follow: on/off**
  mirrors the anchor menu follow toggle.

The current pass edits one active decoration at a time. Select a decoration in
the viewport or outliner, use **Move** for snapped XYZ handles or center
freeform movement, use **Rotate**/**Scale** for transform gizmos, use
**Anchor** for whole-block retethering that keeps the mesh visually in place,
and use **Paint** plus the mesh palette for mesh, color, and material changes.
The lower transform strip keeps the Position, Rotation, and Scale X/Y/Z labels
visible at laptop scale, and its **Anchor follow: on/off** control toggles the
same retether behavior as the anchor options menu. Preview edits
are live for rendering; **Ctrl+Z**/**Ctrl+Y** or the toolbar buttons undo and
redo un-applied editor actions; **Apply** commits and **Cancel** or closing
restores the original decoration fields.

Mesh palette row clicks enter placement mode: the selected mesh follows the
pointer as a valid/invalid ghost, then clicking a real craft block creates a
decoration anchored to that block. The toolbar X/Y/Z symmetry buttons can place
construct-local mirror planes; active planes mirror new mesh placements
atomically and live-follow matched existing decoration moves, rejecting the
linked edit if any mirrored tether is invalid. The palette can switch between a
low-cost searchable list and a lazy 3D preview grid that renders only visible
cards instead of previewing the full mesh catalog at once.

The toolbar **View** control offers **Mixed**, **Wireframe**, **Deco only**,
**Mass**, **Drag**, **Cost**, **Surface**, **Important**, and **Normal**. ESU uses
FTD's decoration wireframe/special-view settings where safe and restores the
player's previous view state when the editor closes.

The editor references FTD's runtime UI element icons by GUID/name from
`StreamingAssets/Mods/UI/Ui Elements`, including `editButton`, and uses
generated ESU-owned fallback icons if a texture is unavailable. It does not
package copied FTD texture files.

In-game smoke checks should include: no `Colored with paint #N` tooltip while an
ESU editor is active; press Ctrl alone and confirm the screen does not show the
vanilla vehicle-control overlay; `Ctrl+Shift+B` remains open after the first
frame; Surface Builder Ctrl-click behavior still works; and a long ESU warning
opens through **Details** without changing top-panel padding. Hover toolbar,
panel, notification, console, row, card, field, and resize-grip controls for two
seconds and confirm the ESU tooltip appears, clamps to the screen, and disappears
when the mouse moves, clicks, scrolls, or starts a drag.

### Surface Builder

Press `Tab` from a clean Decoration Edit Mode session to open Surface Builder.
This mode creates freeform triangular decoration surfaces from points clicked on
craft block faces, then converts the draft into EndlessShapes polygon
decorations when placed.

Surface Builder HUD reference:

- **Surf** is the first toolbar slot and indicates Surface Builder. Press `Tab`
  while clean to continue to Smart Block Builder.
- **X**, **Y**, and **Z** use the shared construct-local symmetry planes. Pending
  plane clicks are handled before draft point placement, and active planes draw
  vehicle-sized wire overlays on the relevant construct axes.
- **Preview** rebuilds the generated decoration preview from the current draft.
- **Place** commits the previewed surface decorations to the craft.
- **Clear** removes the current surface draft.
- **Delete** removes the selected draft point, edge, or face.
- **Material** arrows cycle the generated decoration material.
- **Thickness** sets surface thickness; the `0.025`, `0.05`, and `0.1` buttons
  are quick presets.
- **Color** sets the generated paint index; `-` and `+` step the value.
- **Normal flip** reverses the decoration thickness direction.
- **Nearest anchor** chooses whether generated decorations attach to nearby
  existing craft blocks instead of only the exact clicked surface.
- **Draft** lists the current point/edge/face draft state. Click three block
  surface points to seed a triangle, select an edge and click another point to
  extend it, or Ctrl-click existing points to create a face from any three.
- The shared **Out**, **Anch**, **Apply**, **Cancel**, **Close**, notification
  slot, and bottom status strip behave the same as Decoration Edit Mode.

When symmetry is active, Surface Builder mirrors the draft preview visually and
plans the generated surface decorations for every active X/Y/Z variant. Placement
is atomic: if any mirrored surface cannot resolve a valid nearest anchor or
would exceed FTD's positioning limits/capacity, none of the surfaces are placed.
Draft geometry that lies on a symmetry plane is deduped instead of double-placed.

### Smart Block Builder

Press `Ctrl+Shift+B` in build mode, or press `Tab` from a clean Surface Builder
session, to open Smart Block Builder. The builder uses the same native ESU
toolbar/panel/status styling as Decoration Edit Mode and creates a runtime-only
wireframe preview on the focused construct grid. A click on an existing block
seeds the preview beside that face; a click in empty space seeds a snapped
preview on the selected draw plane. Its left panel shares the ESU auto-scale
settings, can be resized independently from Decoration Edit Mode panels, and has
an internal material picker for Wood, Stone, Metal, Alloy, Glass, Lead, Heavy
armour, and Rubber.

Smart Block Builder HUD reference:

- **Build** is the first toolbar slot and indicates Smart Builder. Press it or
  `Tab` when clean to return to Decoration Edit Mode.
- **Draw** seeds a new runtime-only 1x1x1 draft from a pointed block face or the
  active free-space draw plane.
- **Move** moves the draft by whole focused-grid cells.
- **Scale** resizes the draft. Drag X/Y/Z handles or any of the six highlighted
  preview faces; the opposite face stays anchored and the size never drops below
  one cell.
- **Plane Cam/X/Y/Z** selects the draw plane for free-space seeding.
- **Skip/Block** chooses occupancy behavior. **Skip** omits occupied cells from
  the placement plan; **Block** makes any occupied cell invalidate Apply.
- **Material** cycles Wood, Stone, Metal, Alloy, Glass, Lead, Heavy armour, and
  Rubber. The left panel arrows expose the same picker.
- **X**, **Y**, and **Z** mirror the preview and final placement plan across
  construct-local symmetry planes.
- **Apply** places the planned blocks atomically, **Cancel** removes the runtime
  preview, and **Close** exits Smart Builder.

Smart Builder panel and preview behavior:

- The left panel shows material, active tool, draw plane, occupancy mode,
  symmetry state, preview origin, size, cell count, and planned placements.
- Large previews render as one lightweight outer corner/edge wireframe, not as a
  translucent cube for every cell. Hovered or dragged faces get a temporary
  highlight.
- The bottom status strip reports draft size, placement count, invalid material
  or occupancy reasons, and whether the builder is ready to apply.

The preview is not saved and does not place blocks until **Apply**. Use **Move**
and **Scale** handles, or drag any preview face in **Scale**, to adjust the
rectangular volume. Cycle **Plane** for free-space drawing, cycle **Material** to
choose the placed block type, and use **Skip** or **Block** occupancy mode to
decide whether existing cells are skipped or make the plan invalid. By default,
occupied cells are skipped and reported in the status strip. Active X/Y/Z
symmetry planes mirror the draft preview and Apply commit as one atomic plan.
Middle mouse can be used to show the FTD cursor without closing Smart Builder,
and camera/WASD input remains live unless an ESU handle drag or ESU panel scroll
owns that frame.
Implementation notes: `EsuBuildModeInputGate` owns the one-press mode-switch
guard, `SmartBuildDraft` stays runtime-only until Apply, and Middle mouse may
show the FTD cursor without closing Smart Builder; click empty space to seed on
the selected draw plane and confirm middle mouse shows the FTD cursor without
closing. Large-preview smoke checks should confirm the preview stays to a single
outer wireframe, all six faces resize by whole cells, and each material preset
places the expected basic block.

## Building and verification

Set `FTD_DIR` to the From The Depths installation directory, then run:

```powershell
dotnet build .\EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj -c Release
dotnet run --project .\tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj -c Release -p:NoWarn=MSB3277
```

Or run `build.ps1` to build, verify formatting, execute tests, scan for stale
binaries, PDBs, local paths, and secrets, and create a deterministic runtime ZIP
under `artifacts`. The script accepts Release configuration only and derives the
archive version from `plugin.json`.

The automated suite currently has 247 checks covering exact Harmony methods,
legacy byte compatibility, serialization boundaries and corruption handling,
shared-buffer growth, locale parsing, image limits, geometry and 100,000-entry
processing, atomic tether rollback, exporter transactions, scoped serialization
telemetry, forecasts, HUD profiles, decoration edit-mode math/wiring, native
editor shell, runtime icon catalog, outliner/inspector checks, ESU runtime log
and tooltip behavior, surface/smart symmetry planning, package identity, runtime
assets, and legacy EndlessShapes2 bindings.

Automated checks do not replace the required in-game acceptance pass for UI,
Unity rendering, construct import/export, multiplayer, and save/load behavior.

## Source provenance and licenses

- Combined project: MIT, see `LICENSE`.
- EndlessShapes2: Huwa / huwahuwa, copyright 2022 huwahuwa, MIT.
- Harmony: copyright 2017 Andreas Pardeike, MIT.

See `THIRD_PARTY_NOTICES.md` and `LICENSES` for retained notices and import
provenance.
