# EndlessShapes Unlimited

EndlessShapes Unlimited is a From The Depths mod for builders who want bigger
decoration limits and better decoration/block building tools.

This is the simple Steam Workshop/player readme. The GitHub readme has the long
technical details.

## What It Adds

- Raises the decoration limit from 5,000 to 100,000 per decoration manager.
- Keeps normal/small craft vanilla-save compatible.
- Saves very large decoration craft with ESU's extended format when vanilla
  limits are too small.
- Adds Decoration Edit Mode for moving, rotating, scaling, painting, retethering,
  and placing decorations.
- Adds Surface Builder for making decoration surfaces, paths, and circles.
- Adds Smart Block Builder for quick block/down-slope preview placement.
- Adds OBJ import/export tools from EndlessShapes2.
- Adds an optional HUD that tells you whether the next save should stay vanilla
  compatible or require ESU.

## Important Compatibility Notes

- Remove the old standalone `DecoLimitLifter` and `EndlessShapes2` mods before
  using this combined mod.
- Multiplayer friends need the same ESU version if the craft uses ESU extended
  saves.
- Normal craft stay in vanilla format when they fit vanilla limits.
- If a craft gets too large for vanilla decoration data, ESU saves it in
  sentinel/extended format. That craft needs ESU installed to load.

## Basic Install

1. Subscribe or place the `EndlessShapesUnlimited` folder in your From The
   Depths `Mods` folder.
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

## Serialization HUD

The optional HUD tells you:

- Total decorations.
- Busiest decoration manager.
- Whether the next save should be vanilla-compatible.
- Whether the craft will need ESU extended/sentinel format.
- Last loaded/saved format when known.

Turn it on with `F8` or in the ESU options screen.

## Credits

- Albiv-web: EndlessShapes Unlimited integration and maintenance.
- Huwa / huwahuwa: EndlessShapes2, used with permission under MIT.
- Harmony: patching library, bundled under MIT.
