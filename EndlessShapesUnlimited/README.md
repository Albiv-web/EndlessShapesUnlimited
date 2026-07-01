# EndlessShapes Unlimited

EndlessShapes Unlimited is a From The Depths mod for builders who want higher
decoration limits and better decoration/block building tools.

This is the Steam Workshop/player readme. Please read the save-compatibility
notes before subscribing, sharing craft, or loading someone else's ESU craft.

## Read This First: Save Compatibility

ESU has two save formats for decoration data:

- **Vanilla format**: works without ESU, as long as the craft stays inside
  vanilla limits.
- **ESU sentinel/extended format**: keeps the same normal craft/blueprint file,
  but the decoration data inside it uses ESU's extended marker. Vanilla From The
  Depths will not load that extended decoration data correctly. ESU is required.

The important player rule is:

- If **any one construct** has more than **5,000 decorations**, the craft will
  save in ESU sentinel/extended format and will need ESU to load.
- This applies to the **main vehicle and every subobject/subconstruct
  separately**.
- Staying at **5,000 decorations or less per construct** keeps the craft
  vanilla-save compatible.
- Using subobjects to spread decorations out still works, because the vanilla
  5k decoration limit is per construct, not one total number for the whole
  vehicle.

Examples:

- Main vehicle has 4,900 decorations and a turret has 4,900 decorations: this
  can still save in vanilla format.
- Main vehicle has 5,001 decorations: ESU sentinel/extended format is used.
- A subobject has 5,001 decorations even if the main vehicle is under 5,000: ESU
  sentinel/extended format is used.

The file may still look like a normal `.blueprint` or vehicle save, but if it
uses ESU sentinel/extended decoration data, vanilla players need this mod
installed to load it correctly.

## Optional Large Blueprint Save Setting

ESU also includes an optional setting under:

**ESU Options -> Blueprint saving -> Stream large blueprint JSON saves**

This setting is **off by default**.

When enabled, ESU checks blueprint saves before writing them. If the blueprint
JSON is below **64 MiB**, saving goes through the normal vanilla path. If it is
**64 MiB or larger**, ESU writes the same JSON blueprint directly to a
same-folder temp file, then replaces the target file when the write succeeds.

This does **not** create a new blueprint format. It is only a safer save path
for extremely large blueprint files, meant to avoid freezes/crashes from
building one huge JSON string in memory.

When replacing an existing blueprint, ESU uses the normal backup behavior by
creating a `_backup` file. If the streamed write or replace step fails, the old
file is left alone.

## What ESU Adds

- Raises the decoration limit from 5,000 to 100,000 per decoration manager.
- Keeps normal/small craft vanilla-save compatible.
- Saves over-limit decoration craft with ESU sentinel/extended format when
  vanilla limits are too small.
- Adds Decoration Edit Mode for moving, rotating, scaling, painting,
  retethering, and placing decorations.
- Adds Surface Builder for making decoration surfaces, paths, and circles.
- Adds Smart Block Builder for quick block/down-slope preview placement.
- Adds OBJ import/export tools from EndlessShapes2.
- Adds an optional HUD that tells you whether the next save should stay
  vanilla-compatible or require ESU.
- Adds the optional 64 MiB streamed blueprint JSON save path for very large
  blueprints.

## Before You Use It

- Remove the old standalone `DecoLimitLifter` and `EndlessShapes2` mods before
  using this combined mod.
- Multiplayer friends need the same ESU version if the craft uses ESU
  sentinel/extended saves.
- Workshop craft that use ESU sentinel/extended format should say that ESU is
  required.
- If you want a craft to stay vanilla-compatible, keep every construct and
  subconstruct at or below 5,000 decorations.
- Back up important craft before doing heavy decoration edits or huge blueprint
  saves.

## Basic Install

1. Subscribe on Steam Workshop, or place the `EndlessShapesUnlimited` folder in
   your From The Depths `Mods` folder.
2. Make sure standalone DecoLimitLifter and EndlessShapes2 are not also loaded.
3. Start the game and check that ESU appears in the Alerts panel.

## Main Keybinds

| Key | What it does |
| --- | --- |
| `Ctrl+D` | Open Decoration Edit Mode. |
| `Ctrl+Shift+B` | Open Smart Block Builder. |
| `Tab` | Switch ESU modes when the current mode is clean. |
| `Escape` | Close/cancel the active ESU editor. |
| `Ctrl+Z` / `Ctrl+Y` | Undo/redo un-applied ESU edits. |
| `1` | Create/select cycle. Deco: Select Single/Box. Surface: Draw/Path/Circle. Smart: Block/Down slope. |
| `2` | Transform cycle: Move -> Rotate -> Scale. |
| `3` | View/preview cycle. Deco/Surface: Mixed/Wire/Decoration only/Normal. Smart: Wireframe/Material. |
| `F8` | Toggle the ESU serialization HUD. |

Number keys only affect ESU while an ESU editor is open. Outside ESU, vanilla
build controls keep working normally.

## Serialization HUD

The optional HUD helps you check compatibility before saving. It can show:

- Total decorations on the focused craft.
- The busiest individual decoration manager.
- Whether the next save is expected to stay vanilla-compatible.
- Whether the craft is expected to need ESU sentinel/extended format.
- The last loaded/saved format when known.

Turn it on with `F8` or in the ESU options screen.

## Decoration Edit Mode

Open it with `Ctrl+D`.

Use it to:

- Select decorations from the screen or outliner.
- Move, rotate, and scale decorations.
- Paint decorations and blocks.
- Change decoration mesh, color, and material override.
- Place decorations from the mesh palette.
- Use X/Y/Z symmetry for placement and edits.
- Apply changes when happy, or Cancel to restore the preview.

The outliner supports normal list selection:

- Click selects one decoration.
- `Shift` selects a range.
- `Ctrl` adds/removes individual rows.

## Surface Builder

Press `Tab` from Decoration Edit Mode while clean.

Surface Builder tools:

- **Draw**: click points on the craft to make triangle surfaces.
- **Path**: click points to make a decoration path.
- **Circle**: click a surface to place a circle on that surface.
- **Move/Rotate/Scale**: edit selected draft points.

The left panel is the main place to work:

- Draft list shows surface points/faces, path points, and circle centers.
- Preview shows what will be created.
- Place creates it.
- Clear removes the draft.
- Delete removes the selected draft row.
- Bridge is only for surface edges.

Anchor modes:

- **Nearest**: each decoration uses its nearest valid anchor.
- **Same**: generated decorations share one anchor when possible and preview the
  anchor block.

## Smart Block Builder

Open it with `Ctrl+Shift+B`, or press `Tab` from Surface Builder while clean.

Use it to preview block placement before committing:

- Pick material.
- Pick Block or Down slope.
- Add a preview piece.
- Move, rotate, or scale it.
- Apply to place the blocks.
- Cancel to discard the preview.

Preview mode can be Wireframe or Material.

## OBJ Tools

Use the Decoration Builder item.

You can:

- Import OBJ files and convert mesh groups into decorations.
- Use textures within ESU safety limits.
- Export craft geometry as OBJ/MTL.

If an OBJ is broken, too large, concave, or has invalid geometry, ESU rejects it
instead of half-building bad decorations.

## Credits

- Albiv-web: EndlessShapes Unlimited integration and maintenance.
- Huwa / huwahuwa: EndlessShapes2, used with permission under MIT.
- Harmony: patching library, bundled under MIT.
