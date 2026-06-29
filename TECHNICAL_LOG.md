# EndlessShapes Unlimited technical log

Audit date: 2026-06-27

Target: From The Depths 4.3.3, .NET Standard 2.1, Harmony 2.3.5.

Detailed subsystem references:

- [`docs/ENDLESS_SHAPES_TECHNICAL.md`](docs/ENDLESS_SHAPES_TECHNICAL.md)
- [`docs/DECO_LIMIT_LIFTER_TECHNICAL.md`](docs/DECO_LIMIT_LIFTER_TECHNICAL.md)
- [`docs/SERIALIZATION_HUD_TECHNICAL.md`](docs/SERIALIZATION_HUD_TECHNICAL.md)
- [`docs/DECORATION_EDITOR_NATIVE_UI.md`](docs/DECORATION_EDITOR_NATIVE_UI.md)
- [`docs/BEAMIFICATION_TECHNICAL.md`](docs/BEAMIFICATION_TECHNICAL.md)

## Runtime architecture

`EndlessShapesUnlimited.dll` contains two deliberately preserved code domains:

- `DecoLimitLifter.*` implements serializer capacity, compatibility, and safety.
- `EndlessShapes2.*` preserves the public class names used by existing mod assets
  and constructs while hosting the OBJ and decoration tools.

The release package also contains `Tools/Beamification`, an optional Python
blueprint converter imported from DeltaEpsilon / Delta Epsilon /
DeltaEpsilon7787's FtD Beamification project. It is not compiled into
`EndlessShapesUnlimited.dll` and is not loaded by FTD.

One `GamePlugin_PostLoad` entry point installs every Harmony patch with owner
`alb.endlessshapesunlimited`. Startup verifies the serializer hooks and the
private `GeneralTab.Mesh` UI target. Startup also verifies the exact constructor
prefix/postfix methods and `AutoSyncroniser.fullArray`. Any missing dependency
removes all owned patches and restores the preceding decoration limit. A failure
to write the success log cannot invalidate a completed startup.

After commit, the plugin adds a non-error `ModProblems` entry and refreshes active
GUIs, matching FTD's Alerts-panel convention. This status is non-critical and
does not reintroduce AdvancedMimicUi's historical Steam polling.

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

EndlessShapes2 is credited to Huwa / huwahuwa. Source provenance is
`EndlessShapes2.zip` SHA-256
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

Generation rejects malformed or collapsed geometry with OBJ source lines and
uses bounded largest-face-first queues. A per-main-construct lease prevents two
builders from mutating the same connection rules while allowing separate main
constructs to run concurrently. Each run snapshots its settings, palette, and
texture. Synchronous failures, explicit cancellation, and `Block.PrepForDelete`
delete generated decorations, undo auto-placed tether blocks, restore connection
switches, unregister callbacks, and release texture state independently.

## Tether and export tools

The tether tool uses `TryGetDecorationsList`, verifies the pointed block GUID,
and preflights every linked decoration before issuing commands. Destination
placement, source removal, and property updates execute as one journaled
transaction. Failure restores attempted decorations and undoes source removal
and destination placement in reverse order. `TetherPoint` callbacks keep FTD's
position index synchronized.

OBJ export uses the public material collection and caches runtime-material
lookups. Carried-object submeshes bind to matching `sharedMaterials` entries.
Only emitted materials and referenced textures are written; shared textures are
encoded once and GUID suffixes prevent filename collisions. Output is written to
a temporary directory and renamed only after every OBJ, MTL, and texture write
succeeds. Owned Unity resources are released in `finally` blocks.

## Decoration Edit Mode native shell

Decoration Edit Mode is a modal build-mode overlay opened by `Ctrl+D` or the
Decoration Builder button. The first native pass keeps one active decoration but
stores selection as a set for future box/multi-select. It replaces the initial
plain IMGUI window with an FTD-styled toolbar, right-side outliner with selected
anchor context, inspector built into the draggable left mesh palette, plus a
compact bottom status strip.

The editor uses FTD runtime UI element names/GUIDs where textures are available
and generates ESU-owned fallback glyphs in memory for editor-only concepts. It
does not package copied FTD textures. View modes preserve FTD decoration
wireframe state, use safe screen-space dimming and capped decoration hints, and
do not disable block renderers.

View mode offers Mixed, Wireframe, Deco only, Mass, Drag, Cost, Surface,
Important, and Normal. Mixed is the default FTD-wireframe editing view; the
special-view modes bridge into FTD's native mass/drag/cost/surface/important
visualisations where available; Normal restores the player's pre-editor view
state while keeping editor handles/selectors available.

The hardened edit pass draws the shell through a full-screen Unity modal window
so vanilla build HUD controls behind ESU do not receive clicks. Mesh hover
previews render to ESU-owned `RenderTexture` thumbnails from a hidden preview
camera, avoiding the previous world-space preview that appeared behind the HUD.

### Scoped paint-hover tooltip suppression

FTD assembles the vanilla `Colored with paint #N` hover line inside
`Block.GetToolTip(IInteractionSettings)` from the `Base_Block/Tip_Colored`
localization entry. Earlier ESU experiments patched broad tooltip setters or
manipulated FTD cursor ownership through `UserInput.SetGameControlOptions`,
`StaticOptionsManager.SetMouseCursorActive`, and `_playerWantsMouseLookInUi`.
In-game testing showed those fixes could leave vanilla build HUD mouse-function
panels active after ESU closed, break Esc-menu cursor release, and corrupt
vanilla menu hover rendering.

The current fix is deliberately narrow: ESU manually transpiles only
`Block.GetToolTip(IInteractionSettings)` and routes the two `Block.color`
paint-tooltip checks through `ShouldIncludeVanillaPaintTooltipLine`. While
Decoration Edit Mode, Surface Builder, or Smart Builder owns the editor view,
the paint line behaves as if the block color were zero for tooltip generation
only. Other block status tooltip text still follows vanilla behavior, and ESU
does not patch `TipDisplayer`, `ForceTip`, `TooltipGUI`, `ProTip.Add`, generic
tooltip state, or vanilla cursor ownership APIs.
Clicking a mesh enters pointed-block placement: a ghost follows the build cursor,
validates the hovered craft block, and creates a decoration anchored to that
block.

ESU editor HUD layout is now shared through `EsuHudLayout`. Decoration Edit Mode
and Smart Builder use the same automatic scale calculation against a 1920x1080
reference, a profiled manual multiplier, and profile-options reset control.
Decoration Edit Mode keeps resizable mesh palette and right outliner/anchor
panels, Smart Builder keeps a resizable left panel, and all panels are clamped
back inside the current game window when resolution or scale changes.

Smart Builder/build-mode handoff now uses `EsuBuildModeInputGate` to treat
`Tab` and `Ctrl+Shift+B` as one-press transitions. The gate requires key release
before the newly opened ESU mode can consume the same binding again, preventing
the previous instant close/reopen bounce when cycling Decoration Edit Mode,
Surface Builder, Smart Builder, and vanilla build mode.

Smart Builder is now an editable runtime draft tool rather than a block-face
drag command. It stores a lightweight `SmartBuildDraft` with focused construct,
origin, size, draw plane, active tool, occupancy mode, and current plan. Clicking
an existing block seeds a 1x1x1 preview beside that face; clicking empty space
raycasts against the focused construct's snapped draw plane. Draw rejects
occupied origin cells and switches to **Scale** after a valid origin is placed.
**Move** and **Scale** handles adjust the draft before Apply. Middle mouse may
show the FTD cursor without closing Smart Builder, and camera/WASD input is not
suppressed unless ESU is actively dragging a handle or scrolling an ESU panel.
Apply still commits normal FTD `PlaceBlockCommand` instances through the
existing planner/committer, but the committer now orders planned placements from
existing/just-placed neighboring cells so FtD receives a connected placement
sequence.

Editor history is session-scoped. Snapshot commands cover transform, mesh, color,
material, and anchor-retether edits; creation commands undo/redo decorations
created through pointed-block palette placement. Drag movement is coalesced into
one command on mouse release. Tether restores use FTD's decoration-manager shift
path so the manager's position index stays synchronized.

Decoration Edit Mode and Smart Builder share ESU session-local X/Y/Z symmetry
planes. A toolbar axis click arms plane placement, the next construct-grid click
stores that axis coordinate, and mode switching preserves active planes until ESU
is actually closed. V1 symmetry mirrors only new mesh placements and Smart
Builder draft target cells; it does not mirror existing edit transforms or mesh
handedness. Mirrored decoration placement is validated before creation and
requires every mirrored tether block plus +/-10 positioning to be legal. Smart
Builder mirrors the draft into a composite target-cell set and requires each
mirrored component to satisfy the same occupied-cell and connectivity checks
before Apply.

## Beamification tool

Beamification source provenance is
`DeltaEpsilon7787/FtD_Beamification` commit
`a0aaa63010c460563909cc8eb73f2c0aac2bf5ea`, MIT. The bundled copy remains a
Python blueprint converter with `attrs`, NumPy, SciPy, and tqdm dependencies.
It scans FTD item and duplicate definitions, expands blueprint blocks into a
voxel field, filters eligible armour families/colours, solves a mixed-integer
beam-packing problem, and writes a new blueprint with 1 m to 4 m armour beams.

The bundled tool is credited to DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787.

ESU fixes the upstream command-line `debeamify` flag inversion in its bundled
copy and retains the original MIT notice in `LICENSES/FtD_Beamification-MIT.txt`.
The tool does not modify ESU serialization formats and does not affect FTD
startup.

## BuildingTools reference

Wengh / Weng Haoyu's MIT-licensed `wengh/BuildingTools` project was reviewed as
an external reference for FTD build-tool registration, profile options, key
bindings, and build-mode UI behavior. ESU does not bundle BuildingTools source,
binaries, item definitions, Unity assets, or copied license text.

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

Automated result: 164 passing checks. Coverage includes the original serializer
compatibility suite plus exact startup hooks, rollback, shared-buffer growth,
zero-length loaders, locale parsing, image ceilings, every polygon class,
degenerate/cap rejection, a 100,000-entry run, atomic tether rollback, exporter
material/texture/staging behavior, HUD telemetry, decoration edit mode, native
editor shell, runtime icon catalog, outliner/inspector checks, Beamification
bundle provenance, package identity, and runtime assets.

Remaining manual acceptance work must be performed inside FTD: plugin startup,
legacy construct loading, textured import, animated generation/cancellation,
tether moves, OBJ export under comma-decimal locale, extended save/load, and a
multiplayer session where every peer uses version 1.0.0.

## Privacy and release controls

The new repository uses a GitHub noreply commit identity and clean history.
Tracked source/package content excludes `.vs`, `bin`, `obj`, `.user`, PDB,
selector DLLs, local paths, and original compiled EndlessShapes binaries. Release
packaging stages an explicit runtime allowlist, rejects stale assemblies, scans
tracked and staged content, and creates a deterministic manifest-versioned ZIP.

The upstream Harmony 2.3.5 binary retains its publisher's CodeView build path.
This is third-party provenance, not either mod author's local data.
`EndlessShapesUnlimited.dll` contains no absolute path.
