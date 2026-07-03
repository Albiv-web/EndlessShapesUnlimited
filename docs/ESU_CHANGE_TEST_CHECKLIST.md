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
- Switch between ESU modes with Tab while clean.
- Confirm mode switching is blocked with a visible warning while Apply/Cancel work is pending.
- Confirm Close/Cancel restores normal FtD input and camera behavior.

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
