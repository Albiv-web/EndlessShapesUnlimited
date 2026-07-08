# EndlessShapes Unlimited

EndlessShapes Unlimited is a From The Depths mod that combines the hardened
DecoLimitLifter serializer with the OBJ import/export and decoration-building
tools from EndlessShapes2. It also adds ESU editor modes for decoration editing,
surface generation, and smart block placement.

This root README is the technical GitHub documentation. The packaged/player
readme used for the Steam Workshop is
[`EndlessShapesUnlimited/README.md`](EndlessShapesUnlimited/README.md).

The combined mod is maintained by **Albiv-web**. EndlessShapes2 was created by
**Huwa / huwahuwa** and is included with the author's permission under the MIT
License.

## Requirements

- From The Depths 4.3.3.
- Harmony 2.3.5, packaged as `0Harmony.dll`.
- The standalone `DecoLimitLifter` and `EndlessShapes2` mods must not be loaded
  beside this mod. `plugin.json` declares both as conflicts.
- Multiplayer peers that load an ESU extended/sentinel craft need the same ESU
  version installed.

## Feature Summary

- Raises each decoration manager from the vanilla 5,000 decoration limit to
  100,000.
- Preserves vanilla save data for ordinary constructs and switches to ESU
  sentinel serialization only when a serialized container exceeds vanilla wire
  limits.
- Grows serializer buffers on demand while preserving already-written data.
- Adds an optional serialization HUD with last loaded/saved format, live
  forecast, decoration count, manager peak, header peak, and data peak.
- Imports OBJ vertices, UVs, faces, lines, object/group records, comments,
  whitespace variants, and negative indices.
- Converts OBJ groups into FTD decorations with bounded geometry and texture
  validation.
- Exports construct geometry as OBJ/MTL with only referenced textures.
- Adds Decoration Edit Mode, Surface Builder, Smart Block Builder, and
  Automation Builder as modal ESU editor surfaces.
- Adds shared X/Y/Z symmetry planes for decoration placement, generated
  surfaces, generator paths/circles, and Smart Builder commits.
- Adds a Deco character item for moving tether blocks with linked decorations
  as one transaction.

## Installation

1. Remove standalone `DecoLimitLifter` and `EndlessShapes2` from the FTD `Mods`
   directory.
2. Copy the complete `EndlessShapesUnlimited` runtime folder into FTD `Mods`.
3. Start FTD and confirm the Alerts panel reports
   `EndlessShapes Unlimited v1.0.7 Active!`.

The runtime package must contain only:

- `EndlessShapesUnlimited.dll`
- `0Harmony.dll`
- `plugin.json`, `header.header`, `header.jpg`, `releases`, `README.md`,
  license files
- `Assets`, `Character Items`, `Items`, and `Meshes`

## Keybinds

Default bindings are profile-persisted and can be changed through FTD controls
where exposed.

| Input | Scope | Behavior |
| --- | --- | --- |
| `Ctrl+D` | Build mode | Open Decoration Edit Mode. |
| `Ctrl+Shift+B` | Build mode | Open Smart Block Builder. |
| `Ctrl+Shift+A` | Build mode | Open Automation Builder. |
| `Tab` | ESU editor, clean state | Cycle Decoration Edit Mode -> Surface Builder -> Smart Block Builder -> Automation Builder -> Decoration Edit Mode. Dirty previews must be applied or canceled first. |
| `Escape` | ESU editor | Close/cancel the active ESU mode and guard the same keypress from vanilla. |
| `Ctrl+Z` / `Ctrl+Y` | ESU editor | Undo/redo un-applied editor actions. |
| `1` | ESU editor | Create/select cycle. Decoration: Select Single/Box. Surface: Draw/Path/Circle. Smart Builder: Block/Down slope and arm Add. |
| `2` | ESU editor | Transform cycle. Move -> Rotate -> Scale -> Move. If outside the transform family, first press selects Move. |
| `3` | ESU editor | Display/preview cycle. Decoration and Surface: Mixed -> Wireframe -> Decoration only -> Normal -> Mixed. Smart Builder: Wireframe/Material preview. |
| `F8` | Build mode | Toggle the serialization HUD. |
| `Shift+F8` | Build mode | Trigger exact serializer measurement when configured. |

ESU suppresses vanilla number-key build input only while an ESU editor is
active. Text fields do not switch ESU tools, but ESU still claims the number
keypress so vanilla hotbar/build shortcuts do not leak through.

Manual input smoke tests:

- In build mode outside ESU, press Ctrl alone and confirm the screen does not
  enter an ESU editor, steal camera input, or open a modal.
- In Surface Builder, confirm Surface Builder Ctrl-click behavior still works
  for selecting existing points/faces while the ESU input guard is active.
- In any ESU editor, press `1`, `2`, and `3` and confirm vanilla build tools do
  not receive those key-downs until ESU closes.

## Serialization Compatibility

ESU does not decide save format from a simple decoration count alone. The save
format is calculated from each serialized decoration container's actual wire
metadata and payload size.

Legacy wire format is used while both conditions remain true for a serialized
container:

- Header record count is at or below 9,362 records.
- Sorted decoration data is at or below 6,553,500 bytes.

ESU sentinel format is used when either vanilla boundary is crossed. Sentinel
format is still bounded:

- 4 MiB maximum header data.
- 64 MiB maximum sorted decoration data.
- 256 MiB maximum destination buffer.

Why this matters:

- Wire format, vanilla loading, and vanilla decoration editing are separate.
  A craft can be `LEGACY WIRE` and still be over vanilla's 5,000-decoration
  editor cap.
- Small and ordinary craft remain fully vanilla-friendly when they are legacy
  wire and every decoration manager stays at or below 5,000 decorations.
- Legacy-wire craft above 5,000 decorations per manager have loaded in vanilla
  testing, but vanilla decoration tools are capped/limited there. ESU is needed
  to keep editing above the cap.
- Craft that cross legacy wire limits are saved in sentinel format and require
  ESU to load.
- The calculation is per serialized container, not a single whole-craft sum.
  Main constructs and subconstruct containers are assessed by their own peak
  header/data usage.
- Forecast labels are estimates until the next save/load telemetry confirms the
  exact observed format.

Malformed sentinel payloads are rejected before loader state is committed:
oversized lengths, truncated object IDs, invalid header lengths, segment offsets
outside the data block, and impossible object IDs all fail early.

## Serialization HUD

The native right-side vehicle HUD can append an **ESU serialization** section.
It is off by default.

The HUD reports:

- Total decorations on the focused craft, including subconstructs.
- Busiest decoration manager against the 100,000 ESU manager limit.
- Vanilla load compatibility against ESU-only wire/buffer requirements.
- Vanilla decoration editor cap status against the 5,000-decoration vanilla
  manager limit.
- Peak predicted header bytes for any one serialized container.
- Peak predicted sorted data bytes for any one serialized container.
- Forecast wire-byte format: `LEGACY WIRE`, `SENTINEL`, or `OVER LIMIT`.
- Exact last loaded and saved formats when telemetry exists.

Labels with `~` or `EST` are forecasts after craft changes. Loaded/saved labels
are exact observations.

## OBJ Tools

### Import

Use the **Decoration Builder** item from the Decorations inventory tab.

Importer support:

- Records: `v`, `vt`, `f`, `l`, `o`, `g`
- Face forms: `v`, `v/vt`, `v/vt/vn`, `v//vn`
- Negative indices
- Comments and extra whitespace
- Invariant decimal points even under comma-decimal Windows cultures
- Pasted paths with spaces and matching quotes

Importer ceilings:

- 256 MiB OBJ file size
- 2,000,000 vertices
- 2,000,000 UVs
- 100,000 face/line records
- 64 MiB encoded PNG/JPEG texture size
- 8,192 texture pixels per axis
- 16,777,216 total texture pixels

Rejected geometry includes repeated face points, transform-collapsed edges,
collinear faces, concave faces, non-finite values, overflowing derived math, and
plans that would exceed the final decoration cap. Failures report the relevant
OBJ source line where possible.

### Export

Open the construct information General tab and select **Create an OBJ file of
this vehicle**. ESU writes OBJ, MTL, and only referenced textures beneath the
FTD profile output location. Output is staged and published only after every
file writes successfully.

OBJ export uses invariant decimal formatting, sanitizes generated filenames, and
keeps carried-object submesh/material mappings.

## Decoration Edit Mode

Open with `Ctrl+D` in build mode or through the Decoration Builder button.

The editor shell includes:

- Top toolbar with mode, tools, symmetry, view, notifications, log, and apply
  controls.
- Mesh Palette with searchable list and virtualized 3D grid previews.
- Outliner grouped by construct/subconstruct, including normal Shift/Ctrl
  multi-select constrained to the same construct.
- Inspector for color, material override, mesh, owner details, and exact
  selected decoration fields.
- Selected Anchor panel for tether/anchor workflows.
- Bottom strip with status, save forecast, live transform fields, snap settings,
  and Apply/Cancel.

Important Decoration Edit behavior:

- Apply commits previewed edits. Cancel restores the current preview.
- Move uses snapped X/Y/Z handles plus center freeform movement.
- Rotate uses RGB rotation rings and snaps to 5 degrees by default.
- Scale uses X/Y/Z handles and snaps to 0.05 by default.
- Paint can paint decorations and pointed blocks.
- Material and paint color palettes use the actual selected color/material
  swatches where available.
- Anchor follow retethers while preserving visual world position.
- Mesh placement uses a pointer ray against real craft blocks, not the vanilla
  build cursor.

## Surface Builder

Surface Builder is the second ESU mode in the `Tab` cycle. It creates decoration
surfaces and generated Path/Circle decoration runs.

Tools:

- **Draw**: click craft surfaces to create draft points and triangle faces.
- **Path**: click points to generate segmented decoration paths.
- **Circle**: click a surface to place a circle center. Circle orientation uses
  the clicked surface normal instead of camera perspective.
- **Move/Rotate/Scale**: transform selected surface, path, or circle draft
  points.
- **View**: cycle editor view modes.

The left Surface Builder panel is the single draft/action home:

- Draft list includes surface points/faces, path points, and circle center.
- `Preview`, `Place`, `Clear`, and `Delete` dispatch to the selected draft row
  first, then to the active tool.
- `Bridge` remains Surface-only.
- Right-side Extra Tools is settings-only for mesh, shape, paint color, and
  material override.

Surface notes:

- `Nearest anchor` resolves each generated decoration to its nearest valid
  anchor.
- `Same anchor` previews the shared anchor block with wireframe hints and
  rejects placement if no common anchor is valid.
- Right-clicking Surface, Path, or Circle points opens a small context menu.
- Hovering generator mesh rows shows the shared mesh preview card.

## Smart Block Builder

Open with `Ctrl+Shift+B` or via `Tab` from Surface Builder while clean.

Smart Builder uses runtime preview scenes and vanilla block placement commands.
It currently supports basic structural materials and Block/Down slope shape
families. Future shapes can extend the `1` shortcut cycle without changing the
shortcut model.

Core workflow:

- Add/Build creates a `SmartBuildDraft`.
- Move, Rotate, and Scale edit the selected preview piece.
- Apply commits planned vanilla block placements.
- Cancel restores the current preview.
- Occupancy can fail on occupied cells or skip occupied cells.
- Down slopes use true slope placement lines and support cells.
- Support: Full/Step controls slope support behavior.
- Preview can switch between Wireframe and Material.

Input notes:

- Middle mouse may show the FTD cursor without closing Smart Builder.
- Middle mouse may show the FTD cursor without closing Smart Builder, and camera
  movement remains live unless a Smart Builder handle is being dragged.
- Right click cancels context/menu selection instead of clearing the whole scene.

## Automation Builder

Open with `Ctrl+Shift+A`, from the Decoration Builder item, or via `Tab` from
Smart Block Builder while clean.

Automation Builder is a breadboard-focused workflow editor:

- The right panel can arm breadboard placement. The pending breadboard follows
  the mouse over craft surfaces and places through the normal block command
  path.
- Selecting a breadboard and then a target block creates an output/setter link
  from the breadboard to that block.
- Selecting a target block and then a breadboard creates an input/getter link
  from that block into the breadboard.
- Linked blocks draw animated colored world lines so signal direction is easy
  to read.
- Opening the selected breadboard's graph shows a large canvas with draggable
  top-to-bottom component blocks and a right-side breadboard component palette.

## Symmetry

ESU symmetry planes are construct-local X/Y/Z grid planes. They can combine into
2, 4, or 8 variants and dedupe cells or decoration placements that lie on active
planes. Surface drafts flip face winding for odd-axis mirrored variants so
triangle normals stay coherent.

Placement batches validate every mirrored placement before commit. If any
mirrored anchor, surface, or block placement is invalid, the linked edit is
rejected rather than partially committed.

## Tether Movement Tool

Equip the **Deco** character item, point at an EndlessShapes tether block, and
use left/right click to move it one camera-relative axis step. The tool
preflights every linked decoration and aborts if any new offset would exceed
`+/-10`. Block commands and decoration fields are committed as one transaction
and rolled back in reverse order on failure.

## Development

Set `FTD_DIR` to your local From The Depths install before building. Keep
machine-specific paths out of committed docs and release notes:

```powershell
$env:FTD_DIR = '<path-to-From-The-Depths-install>'
dotnet build EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj -c Release --nologo
```

Run the verification harness:

```powershell
$env:FTD_DIR = '<path-to-From-The-Depths-install>'
dotnet run --project tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj -c Release -p:NoWarn=MSB3277
```

Package a release:

```powershell
$env:FTD_DIR = '<path-to-From-The-Depths-install>'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` performs:

- Release build.
- `dotnet format --verify-no-changes` for the mod and verifier projects.
- Verification harness run.
- Packaged DLL hash check against the Release build output.
- Assembly version check against `plugin.json`.
- Runtime package allowlist check.
- Sensitive-content scan over tracked files and staged runtime files.
- Deterministic zip creation under `artifacts`.

## Repository Layout

- `EndlessShapesUnlimited/Source`: mod source.
- `EndlessShapesUnlimited/Source/DecorationEditMode`: Decoration Edit Mode and
  Surface Builder UI/planning/session code.
- `EndlessShapesUnlimited/Source/SmartBuildMode`: Smart Block Builder runtime
  scene, planning, and commit code.
- `EndlessShapesUnlimited/Source/AutomationBuilderMode`: Automation Builder
  breadboard placement, linking, HUD, and graph canvas code.
- `EndlessShapesUnlimited/Source/Patches`: Harmony patches and serializer
  integration.
- `tools/EndlessShapesUnlimited.Verification`: non-Unity verifier and regression
  harness.
- `EndlessShapesUnlimited/README.md`: simplified Steam Workshop/player readme.
- `CHANGELOG.md`: version-to-version release history.
- `RELEASE_CHANNELS.md`: GitHub vs Steam Workshop packaging rules.
- `LICENSES`: bundled third-party notices for runtime packaging.

## Credits

- **Albiv-web**: EndlessShapes Unlimited integration and maintenance.
- **Huwa / huwahuwa**: EndlessShapes2, included with permission under MIT.
- **Harmony**: runtime patching library, bundled under MIT.
