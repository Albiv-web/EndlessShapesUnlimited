EndlessShapes Unlimited is a From The Depths mod for builders who want higher decoration limits and better decoration/block building tools.

It combines DecoLimitLifter-style extended decoration saving with the OBJ import/export and building tools from EndlessShapes2.

DISCORD SERVER FOR BUG REPORTS, IDEAS, FEEDBACK OR HELP:
https://discord.gg/6Us9UamUPx

[b]IMPORTANT SAVE COMPATIBILITY[/b]

ESU has two decoration save formats:

Vanilla format:
Works without ESU, as long as every construct stays inside vanilla limits.

ESU sentinel / extended format:
Used when a construct exceeds vanilla decoration limits. The file still looks like a normal blueprint or vehicle save, but ESU is required to load the extended decoration data correctly.

Important rule:
- If any one construct has more than 5,000 decorations, the craft saves in ESU extended format.
- This applies separately to the main vehicle and every subobject / subconstruct.
- Staying at 5,000 decorations or less per construct keeps the craft vanilla-save compatible.
- Using subobjects to spread decorations still works, because the vanilla 5k limit is per construct.
- The mod will say if a vehicle was saved in vanilla or new format via the Serialization Hud, press F8 in-game to bring it up.

Examples:
- Main vehicle 4,900 decorations + turret 4,900 decorations = can still save vanilla.
- Main vehicle 5,001 decorations = ESU required.
- Subobject 5,001 decorations = ESU required, even if the main vehicle is under 5,000.

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
- Workshop craft using ESU extended format should say that ESU is required.
- Back up important craft before heavy decoration edits or huge blueprint saves.

[b]CREDITS[/b]

Albiv-web - EndlessShapes Unlimited integration and maintenance.
Huwa / huwahuwa - EndlessShapes2, used with permission under MIT.
Harmony - Patching library, bundled under MIT.
Le honorable mentions: Wengh & DeltaEpsilon
