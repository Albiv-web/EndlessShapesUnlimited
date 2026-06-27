# EndlessShapes Unlimited technical log

Audit date: 2026-06-27

Target: From The Depths 4.3.3, .NET Standard 2.1, Harmony 2.3.5.

## Runtime architecture

`EndlessShapesUnlimited.dll` contains two deliberately preserved code domains:

- `DecoLimitLifter.*` implements serializer capacity, compatibility, and safety.
- `EndlessShapes2.*` preserves the public class names used by existing mod assets
  and constructs while hosting the OBJ and decoration tools.

One `GamePlugin_PostLoad` entry point installs every Harmony patch with owner
`alb.endlessshapesunlimited`. Startup verifies the serializer hooks and the
private `GeneralTab.Mesh` UI target. Any missing target causes all patches owned
by the mod to be removed before an in-game error is reported. The decoration
limit is raised only after verification succeeds.

## Serializer behavior

FTD's legacy layout remains unchanged below both legacy boundaries:

- 9,362 seven-byte header records;
- 6,553,500 sorted data bytes (100 full 65,535-byte pieces).

The first value above either boundary uses a sentinel followed by explicit
32-bit header/data lengths. Exact-size calculation is shared by the saver and
copy converter. Header, sorted-data, `ByteStore`, and multiplayer destination
buffers grow by copying the written prefix and promote safe buffers for reuse.

The loader validates object ID width, sentinel lengths, header divisibility,
payload presence, monotonic/in-range offsets, and configured ceilings before
allocating or changing loader state. Current ceilings are 4 MiB header data,
64 MiB sorted data, and a 256 MiB destination buffer.

## EndlessShapes import and generation

Source provenance is `EndlessShapes2.zip` SHA-256
`F77FE118FECF3430CB4768C509C09F6A8945C338BCF0D8FA00450F4208E8E23C`.
Only `Source/EndlessShapes2_v4p2p9` and runtime assets were imported. The `.vs`
cache contained the original author's local username/path and was excluded,
along with historical project variants, selector/prebuilt DLLs, and build output.

The new parser is independent of Unity and uses invariant culture. It supports
OBJ comments, spaces/tabs, object/group names, vertices, UVs, faces, lines,
positive and negative indices, and `v`, `v/vt`, `v//vn`, and `v/vt/vn` points.
It rejects zero/out-of-range indices, non-finite numbers, malformed primitives,
files above 256 MiB, more than 2,000,000 vertices, or more than 100,000 face/line
records.

Generation transforms the selected mesh on the main thread, preflights exact
decoration capacity, captures connection-rule state, and uses
`AllConstructDecorations.NewDecoration`. Synchronous failures and interrupted
animated builds delete generated decorations, undo auto-placed tether blocks,
restore connection switches, unregister scheduler callbacks, and release the
texture. Successful builds keep content and still restore transient state.

## Tether and export tools

The tether tool uses `TryGetDecorationsList` instead of reading the private
three-dimensional dictionary. It resolves a deterministic dominant camera axis,
requires destination placement and source removal to succeed, then retethers a
snapshot of nearby decorations. `TetherPoint` change callbacks keep FTD's
position index synchronized.

OBJ export uses the public material collection rather than the private
`DictionaryOfComponents`. Output folder/file names are sanitized, numbers use
invariant round-trip formatting, OBJ is streamed rather than accumulated in one
large string, and temporary render textures, texture copies, carried-object mesh
clones, and merged meshes are released in `finally` blocks.

## Compatibility contract

The following remain unchanged for existing EndlessShapes2 content:

- namespace and class names: `DecorationBuilder`, `DecorationBuilderData`, and
  `DecorationTetherMove` under `EndlessShapes2`;
- Decoration Builder data variable IDs 0 through 16;
- Decoration Builder, character item, mesh, and header-independent asset GUIDs.

The combined manifest conflicts with standalone DecoLimitLifter and
EndlessShapes2 because loading either beside the combined mod would duplicate
Harmony patches, class registrations, or component GUIDs.

## Verification status

The .NET Framework verification host patches only serializer classes because
initializing the current FTD UI under that non-Unity runtime calls APIs it does
not implement. It still resolves the exact `GeneralTab.Mesh` metadata target.

Automated result: 61 passing checks. Coverage includes all original serializer
checks plus OBJ parsing, input ceilings, UI target resolution, manifest identity,
legacy class/asset bindings, and runtime asset presence.

Remaining manual acceptance work must be performed inside FTD: plugin startup,
legacy construct loading, textured import, animated generation/cancellation,
tether moves, OBJ export under comma-decimal locale, extended save/load, and a
multiplayer session where every peer uses version 1.0.0.

## Privacy and release controls

The new repository uses a GitHub noreply commit identity and clean history.
Tracked source/package content excludes `.vs`, `bin`, `obj`, `.user`, PDB,
selector DLLs, local paths, and original compiled EndlessShapes binaries. Release
packaging stages an explicit runtime allowlist and scans the resulting DLL/ZIP
before publication.

The upstream Harmony 2.3.5 binary retains its publisher's CodeView build path.
This is third-party provenance, not either mod author's local data.
`EndlessShapesUnlimited.dll` contains no absolute path.
