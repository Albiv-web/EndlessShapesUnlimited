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
- Packages DeltaEpsilon's Beamification Python blueprint converter as an
  optional external tool under `Tools/Beamification`.
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

### Beamify armour blueprints

The release package includes **FtD Beamification** by
**DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787** under
`Tools/Beamification`. This is an external Python blueprint converter, not an
FTD-loaded Harmony mod. It reads a
`.blueprint`, replaces eligible armour cells with 1 m to 4 m beam variants using
a mixed-integer solver, and writes a new blueprint. Install its Python
dependencies from `Tools/Beamification/requirements.txt` and keep backups of
source blueprints before conversion.

The bundled copy is imported from commit
`a0aaa63010c460563909cc8eb73f2c0aac2bf5ea` and retains Delta Epsilon's MIT
license notice. ESU locally fixes the command-line `debeamify` flag inversion.

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
messages show a **Details** button without changing top-panel padding. A blocked
mode switch makes **Apply** and **Cancel** flash without toolbar movement.

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
opens through **Details** without changing top-panel padding.

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

The automated suite currently has 234 checks covering exact Harmony methods,
legacy byte compatibility, serialization boundaries and corruption handling,
shared-buffer growth, locale parsing, image limits, geometry and 100,000-entry
processing, atomic tether rollback, exporter transactions, scoped serialization
telemetry, forecasts, HUD profiles, decoration edit-mode math/wiring, native
editor shell, runtime icon catalog, outliner/inspector checks,
Beamification bundle provenance, package identity, runtime assets, and legacy
EndlessShapes2 bindings.

Automated checks do not replace the required in-game acceptance pass for UI,
Unity rendering, construct import/export, multiplayer, and save/load behavior.

## Source provenance and licenses

- Combined project: MIT, see `LICENSE`.
- EndlessShapes2: Huwa / huwahuwa, copyright 2022 huwahuwa, MIT.
- FtD Beamification: DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787,
  copyright 2025 Delta Epsilon, MIT.
- BuildingTools reference: Wengh / Weng Haoyu, MIT. ESU used this only as an
  external implementation reference; no BuildingTools source, assets, or
  binaries are bundled.
- Harmony: copyright 2017 Andreas Pardeike, MIT.

See `THIRD_PARTY_NOTICES.md` and `LICENSES` for retained notices and import
provenance.
