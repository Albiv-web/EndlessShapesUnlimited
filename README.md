# EndlessShapes Unlimited

EndlessShapes Unlimited is a From The Depths mod that combines the hardened
DecoLimitLifter serializer with the OBJ import/export and decoration-building
tools from EndlessShapes2.

The combined mod is maintained by **Albiv-web**. EndlessShapes2 was created by
**huwahuwa** and is included with the author's permission under the MIT License.

## Features

- Raises the per-construct decoration limit from 5,000 to 100,000.
- Grows FTD serializer buffers on demand while preserving already-written data.
- Keeps vanilla-compatible serialization for ordinary constructs.
- Imports OBJ vertices, UVs, faces, lines, groups, and negative indices.
- Converts selected OBJ mesh groups into FTD decorations.
- Supports optional textures, automatic tether placement, and animated builds.
- Adds a character tool for moving tether blocks with nearby decorations.
- Adds OBJ export beside FTD's existing STL export in the construct General tab.

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
4. Select a mesh group, configure scale/orientation/tether options, and confirm.

OBJ numbers are parsed with invariant culture, so decimal points work even when
Windows uses comma decimals. The importer accepts `v`, `vt`, `f`, `l`, `o`, and
`g` records, common face forms such as `v/vt/vn` and `v//vn`, comments, extra
whitespace, and negative indices.

Importer safety ceilings are 256 MiB, 2,000,000 vertices, and 100,000 face/line
records. Final generation is also checked against remaining construct capacity.

### Move tether blocks

Equip the **Deco** character item, point at a tether block, and use left/right
click to move it one camera-relative axis step. The destination is placed and
the source removed before nearby decorations are retethered.

### Export a construct

Open the construct information General tab and select **Create an OBJ file of
this vehicle**. The mod writes OBJ, MTL, and readable texture files beneath the
FTD profile directory using invariant decimal formatting.

## Serialization compatibility

The mod preserves FTD's original format until a construct exceeds a vanilla
metadata boundary:

- Up to 9,362 header records remain legacy-compatible.
- Up to 6,553,500 sorted data bytes remain legacy-compatible.
- Larger payloads use the mod's sentinel format and require this mod to load.

Configured ceilings are 4 MiB of header data, 64 MiB of sorted data, and a
256 MiB destination buffer. Malformed, truncated, inconsistent, or oversized
sentinel payloads are rejected before committing loader state.

## Building and verification

Set `FTD_DIR` to the From The Depths installation directory, then run:

```powershell
dotnet build .\EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj -c Release
dotnet run --project .\tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj -c Release -p:NoWarn=MSB3277
```

Or run `build.ps1` to build, execute verification, and create a clean runtime
ZIP under `artifacts`. Release builds do not emit PDB files.

The automated suite currently has 61 checks covering Harmony targets, legacy
byte compatibility, serialization boundaries and corruption handling, buffer
growth, OBJ parsing and limits, package identity, runtime assets, and legacy
EndlessShapes2 bindings.

Automated checks do not replace the required in-game acceptance pass for UI,
Unity rendering, construct import/export, multiplayer, and save/load behavior.

## Technical documentation

- [`docs/ENDLESS_SHAPES_TECHNICAL.md`](docs/ENDLESS_SHAPES_TECHNICAL.md) explains
  OBJ parsing, coordinate conversion, polygon classification, decoration
  generation, palette mapping, tether movement, and OBJ export.
- [`docs/DECO_LIMIT_LIFTER_TECHNICAL.md`](docs/DECO_LIMIT_LIFTER_TECHNICAL.md)
  explains FTD's serializer model, every Harmony patch, legacy and sentinel wire
  formats, buffer growth, corruption checks, limits, and multiplayer constraints.
- [`TECHNICAL_LOG.md`](TECHNICAL_LOG.md) records the combined integration and
  verification status.

## Source provenance and licenses

- Combined project: MIT, see `LICENSE`.
- EndlessShapes2: copyright 2022 huwahuwa, MIT.
- Harmony: copyright 2017 Andreas Pardeike, MIT.

See `THIRD_PARTY_NOTICES.md` and `LICENSES` for retained notices and import
provenance.
