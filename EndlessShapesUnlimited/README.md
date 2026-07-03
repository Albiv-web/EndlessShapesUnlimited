EndlessShapes Unlimited is a From The Depths mod for builders who want higher decoration limits and better decoration/block building tools.

It combines DecoLimitLifter-style extended decoration saving with the OBJ import/export and building tools from EndlessShapes2.

Mod latest version 1.0.5

DISCORD SERVER FOR BUG REPORTS, IDEAS, FEEDBACK OR HELP:
https://discord.gg/6Us9UamUPx

[b]IMPORTANT SAVE COMPATIBILITY[/b]

ESU shows two different things:

Wire bytes / save format:
This says whether the saved decoration bytes are still legacy wire or need ESU sentinel wire.

ESU sentinel / extended format:
Used when the saved decoration byte data gets too large for the vanilla legacy wire format. The file still looks like a normal blueprint or vehicle save, but ESU is required to read the extended decoration data correctly.

Vanilla load:
This says whether vanilla From The Depths can read the saved decoration data.

Vanilla decoration tools:
Vanilla has a 5,000 decoration cap per construct/manager for decoration editing and creation.

Important rule:
- LEGACY WIRE is about save bytes only.
- More than 5,000 decorations does not automatically mean ESU is required to load the craft.
- In testing, vanilla could load legacy-wire craft above 5,000 decorations, but vanilla decoration tools were capped / limited once the craft was already over the vanilla limit.
- A craft over 5,000 decorations can still say LEGACY WIRE if the byte data is small enough.
- This applies separately to the main vehicle and every subobject / subconstruct.
- Staying at 5,000 decorations or less per construct, and staying out of sentinel/buffer limits, keeps the craft both vanilla-load compatible and vanilla-editor friendly.
- Using subobjects to spread decorations still works, because the vanilla 5k limit is per construct.
- The Serialization HUD shows Wire bytes, Vanilla load, and Vanilla edit limit. Press F8 in-game to bring it up.

Examples:
- Main vehicle 4,900 decorations + turret 4,900 decorations = can still be vanilla-load compatible.
- Main vehicle 5,001 decorations + LEGACY WIRE = vanilla may load it, but vanilla decoration tools are over cap.
- Subobject 5,001 decorations = that subobject is over the vanilla decoration editor cap, even if the main vehicle is under 5,000.

[b]OPTIONAL LARGE BLUEPRINT SAVE SETTING[/b]

ESU Options -> Blueprint saving -> Stream large blueprint JSON saves

This is off by default.

When enabled:
- Blueprints below 64 MiB use the normal vanilla save path.
- Blueprints at 64 MiB or larger are written through a safer temp-file path.
- The old file is left alone if the streamed write or replace step fails.

This does not create a new blueprint format. It only helps very large blueprints save without building one huge JSON string in memory.

[b]OPTIONAL HUGE-CRAFT BLUEPRINT LOADING[/b]

ESU Options -> Blueprint loading

These are off by default. Enable them only when you are deliberately loading very large craft or helping test/debug huge-craft loading.

- V1 streams blueprint JSON loading and is mainly a memory/OOM safety path.
- V2 only helps craft with large amounts of saved block data.
- V3 is the recommended opt-in mode for extremely large block-count craft.
- "Apply V3 to block-count-heavy blueprints" lets V3 handle huge block-count craft even when the `.blueprint` file is below 64 MiB.
- Unsafe probe modes are timing-only, correctness-invalid, and should never be used for normal play or saved from.

Tester result: a 3.8 million block craft that took about 2.5 hours in vanilla loaded in about 21 minutes with V3 enabled.

The `.blueprint` schema is unchanged. These settings do not create sidecar files.

[b]WHAT ESU ADDS[/b]

- Raises the decoration limit from 5,000 to 100,000 per decoration manager.
- Keeps normal/small craft vanilla-save compatible.
- Adds Decoration Edit Mode.
- Adds Surface Builder.
- Adds Smart Block Builder.
- Adds OBJ import/export tools.
- Adds an optional serialization HUD.
- Adds optional streamed saving for very large blueprints.
- Adds optional V3 loading for huge block-count blueprints.

[b]NEW IN 1.0.5[/b]

- Surface Builder now handles slanted manually-created and bridged faces more reliably.
- Same-anchor mode in Surface Builder and Extra Tools can now use a picked shared anchor that you can select and move before applying.
- Extra Tools now include partial circles, 2D cone sectors, cones, frustums, spheres and partial spheres.
- Smart Builder has a fixed shape palette with list/grid previews, visible-only 3D thumbnails, material-safe preview refreshes and resizable Palette / Selected / Scene panels.
- Smart Builder wire previews now show outer/hard outlines instead of internal triangle diagonals and repeated placement seams.
- The Smart Builder `1` shortcut now cycles 1m-capable shapes only; use the size buttons for 2m/3m/4m variants.
- Decoration transform snap values can be smaller and more precise, update live while typing, and decoration scaling is no longer capped at 10x.

[b]MAIN CONTROLS[/b]

Ctrl+D - Open Decoration Edit Mode.
Ctrl+Shift+B - Open Smart Block Builder.
Tab - Switch ESU modes when the current mode is clean.
Escape - Close/cancel the active ESU editor.
Ctrl+Z / Ctrl+Y - Undo/redo un-applied ESU edits.
F8 - Toggle the ESU serialization HUD.

1 - Cycle create/select/tool mode:
- Decoration Edit: Select Single / Box.
- Surface Builder: Draw / Path / Shape.
- Smart Builder: cycle 1m-capable shapes.

2 - Cycle transform mode:
- Move -> Rotate -> Scale.

3 - Cycle view/preview mode:
- Decoration/Surface: Mixed / Wire / Decoration only / Normal.
- Smart Builder: Wireframe / Material.

Number keys only affect ESU while an ESU editor is open. Outside ESU, vanilla build controls keep working normally.

[b]DECORATION EDIT MODE[/b]

Open with Ctrl+D.

Use it to:
- Select decorations from the viewport or outliner.
- Move, rotate and scale decorations.
- Paint decorations and blocks.
- Change mesh, color and material override.
- Place decorations from the mesh palette.
- Use X/Y/Z symmetry for placement and edits.
- Retether decorations with the Anchor tools.
- Apply changes when happy, or Cancel to restore the preview.

Outliner selection:
- Click selects one decoration.
- Shift selects a range.
- Ctrl adds/removes individual rows.

[b]SURFACE BUILDER[/b]

Press Tab from Decoration Edit Mode while clean.

Tools:
- Draw: click points on the craft to make triangle surfaces.
- Path: click points to make a decoration path.
- Shape: click a surface to place circles, arcs, cones, frustums and spheres.
- Move / Rotate / Scale: edit selected draft points.

Left panel:
- Draft list shows surface points/faces, path points and circle centers.
- Preview shows what will be created.
- Place creates it.
- Clear removes the draft.
- Delete removes the selected draft row.
- Bridge is only for surface edges.

Anchor modes:
- Nearest: each decoration uses its nearest valid anchor.
- Same: generated decorations share one anchor when possible.
- Same anchor can also use a picked shared anchor. Select that anchor with Move
  and drag it before applying if you want every generated decoration anchored to
  a specific craft block.

[b]SMART BLOCK BUILDER[/b]

Open with Ctrl+Shift+B, or press Tab from Surface Builder while clean.

Use it to preview block placement before committing:
- Pick material.
- Pick a shape from the List or 3D grid palette.
- Use the size buttons for larger variants when available.
- Add a preview piece.
- Move, rotate or scale it.
- Switch preview between Wireframe and Material.
- Apply to place the blocks.
- Cancel to discard the preview.

[b]OBJ TOOLS[/b]

Use the Decoration Builder item.

You can:
- Import OBJ files and convert mesh groups into decorations.
- Use textures within ESU safety limits.
- Export craft geometry as OBJ / MTL.

If an OBJ is broken, too large, concave or has invalid geometry, ESU rejects it instead of half-building bad decorations.

[b]BEFORE YOU USE IT[/b]

- Remove standalone DecoLimitLifter and EndlessShapes2 before using this combined mod.
- Multiplayer friends need the same ESU version if the craft uses ESU extended saves.
- Workshop craft using ESU sentinel/buffer data should say ESU is required to load.
- Workshop craft over 5,000 decorations but still LEGACY WIRE should say vanilla may load it, but ESU is recommended/needed for decoration editing above the vanilla cap.
- Back up important craft before heavy decoration edits or huge blueprint saves.

[b]CREDITS[/b]

Albiv-web - EndlessShapes Unlimited integration and maintenance.
Huwa / huwahuwa - EndlessShapes2, used with permission under MIT.
Harmony - Patching library, bundled under MIT.
Le honorable mentions: Wengh & DeltaEpsilon
