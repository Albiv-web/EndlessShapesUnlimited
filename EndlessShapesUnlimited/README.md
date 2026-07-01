EndlessShapes Unlimited is a From The Depths mod for builders who want higher decoration limits and better decoration/block building tools.

It combines DecoLimitLifter-style extended decoration saving with the OBJ import/export and building tools from EndlessShapes2.

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

[b]WHAT ESU ADDS[/b]

- Raises the decoration limit from 5,000 to 100,000 per decoration manager.
- Keeps normal/small craft vanilla-save compatible.
- Adds Decoration Edit Mode.
- Adds Surface Builder.
- Adds Smart Block Builder.
- Adds OBJ import/export tools.
- Adds an optional serialization HUD.
- Adds optional streamed saving for very large blueprints.

[b]MAIN CONTROLS[/b]

Ctrl+D - Open Decoration Edit Mode.
Ctrl+Shift+B - Open Smart Block Builder.
Tab - Switch ESU modes when the current mode is clean.
Escape - Close/cancel the active ESU editor.
Ctrl+Z / Ctrl+Y - Undo/redo un-applied ESU edits.
F8 - Toggle the ESU serialization HUD.

1 - Cycle create/select/tool mode:
- Decoration Edit: Select Single / Box.
- Surface Builder: Draw / Path / Circle.
- Smart Builder: Block / Down slope.

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
- Circle: click a surface to place a circle.
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

[b]SMART BLOCK BUILDER[/b]

Open with Ctrl+Shift+B, or press Tab from Surface Builder while clean.

Use it to preview block placement before committing:
- Pick material.
- Pick Block or Down slope.
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
