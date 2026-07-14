[b]Mod latest version 1.0.8[/b]

[h1]EndlessShapes Unlimited[/h1]

EndlessShapes Unlimited is a builder-focused mod for From The Depths.

It adds Blender-like editing tools for decorations, surfaces, block previews, and breadboard automation workflows, while also raising the decoration limit for players who want to build far beyond vanilla.

The goal is simple:
more control, better editing tools, bigger decoration builds, and fewer painful manual steps.

DISCORD SERVER FOR BUG REPORTS, IDEAS, FEEDBACK OR HELP:
https://discord.gg/6Us9UamUPx


[h2]MAIN FEATURES[/h2]

[b]Decoration Edit Mode[/b]
Move, rotate, scale, paint, retether, select, and edit decorations directly on your craft. The paint tool can also drag a screen selection across decorations and blocks.
The Block Palette (formerly Mesh Palette) has a default-off Build toggle. With Build off, choices create decorations as before. Turn it on to select real native inventory block definitions with left or right click; FtD's full-size native marker follows the mouse and validates the exact one-metre-grid target before left-click placement. Right-click a block on the craft to sample its type and rotation. Locked or construct-incompatible items and invalid world targets are rejected safely, and native multi-cell block footprints are preserved. Simple Build Mode's Shift+LMB replacement gesture and distance-scaled attachment indicators are disabled in this cursor-following mode so neither can disagree with the full-size preview.

Decoration X-ray defaults off, so viewport selection cannot grab decoration centers hidden behind craft or subobject blocks. Turn X-ray on to select through blocks; list selection remains available either way. To keep very dense craft responsive, an X-ray-off box with more than 512 projected decoration centers is rejected before visibility checks or paint changes.

[b]Surface Builder[/b]
Create decoration surfaces, paths, circles, quads, polygons, tubes, cones, spheres, partial shapes, and other generated decoration layouts. Draft lists, Coordinates, Extra Tools, and paint palettes resize with their panels.
Extra Tools Preview draws the selected decoration mesh with the same transform, paint, material override, anchoring, and symmetry that Place will use; cyan guides and edit handles remain visible on top.

[b]Smart Block Builder[/b]
Preview block shapes before placing them. Move, rotate, scale, change materials, and apply when ready.

[b]Automation Builder[/b]
Place breadboards, link blocks as inputs or outputs, and build readable top-to-bottom breadboard graphs. This editor is work in progress and may be very buggy, so its first use shows a required warning.

[b]OBJ Tools[/b]
Import OBJ models and convert mesh groups into FtD decorations.
Export craft geometry as OBJ / MTL.

[b]Higher Decoration Limits[/b]
ESU raises the decoration manager limit from vanilla's 5,000 to up to 100,000.

[b]Serialization HUD[/b]
Press F8 to see decoration counts, save format status, vanilla compatibility, and ESU format warnings.

[b]Fast Blueprint Loading[/b]
Optional V1/V2/V3 loading modes for special cases.
V3 is mainly useful for extremely high block-count craft. I recommend not tweaking these settings for 99% of use cases. 


[h2]VANILLA COMPATIBILITY[/h2]

ESU tries to keep normal craft vanilla-friendly when possible.

By default, Vanilla Compatibility Mode is ON. This helps stop you from accidentally creating or saving decoration data that requires ESU.

Simple rule:

- 5,000 decorations or less per construct = vanilla-friendly.
- More than 5,000 decorations on a construct = may require ESU for editing or loading.
- Main vehicle and subobjects are counted separately.
- The Serialization HUD can show whether the craft is vanilla-compatible or ESU-only.

If a craft uses ESU sentinel / extended decoration data, vanilla From The Depths will not load that decoration data correctly. Players will need ESU installed.


[h2]MAIN CONTROLS[/h2]

Ctrl+D - Open Decoration Edit Mode
Ctrl+Shift+B - Open Smart Block Builder
Ctrl+Shift+A - Open Automation Builder
Tab - Cycle ESU modes when clean
Escape - Close / cancel active ESU mode
Ctrl+Z / Ctrl+Y - Undo / redo ESU edits
F8 - Toggle Serialization HUD

1 - Cycle create/select/tool mode
2 - Cycle Move / Rotate / Scale
3 - Cycle view / preview mode

Number keys only affect ESU while an ESU editor is open. Outside ESU, vanilla controls work normally.

[b]There are more controls and shortcuts; the full list is available in Discord.[/b]


[h2]BEFORE USING[/h2]

- Remove standalone DecoLimitLifter and EndlessShapes2 before using this combined mod.
- Multiplayer friends need the same ESU version if the craft uses ESU extended saves.
- Back up important craft before heavy decoration editing or huge blueprint work.
- Verify Automation Builder output in the vanilla breadboard editor before relying on it.
- Unsafe diagnostic fast-load probes are for testing only. Do not save from unsafe probe runs.
- **Memory-safe part status checks** is available in ESU options for
  multi-million-block craft. It is off by default and does not change blueprint files.


[h2]CREDITS[/h2]

Albiv-web - EndlessShapes Unlimited integration and maintenance.
Huwa / huwahuwa - EndlessShapes2, used with permission under MIT.
Harmony - Patching library, bundled under MIT.
Honorable mentions: Wengh & DeltaEpsilon
