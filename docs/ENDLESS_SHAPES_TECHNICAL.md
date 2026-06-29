# EndlessShapes2 technical reference

This document describes the EndlessShapes2 subsystem as it exists in
EndlessShapes Unlimited 1.0.0. It is based on the imported 4.2.9 source and the
current hardened integration, not on the historical standalone binaries.

## Purpose and runtime surface

EndlessShapes2 converts Wavefront OBJ primitives into native From The Depths
decorations. It also adds a character tool for moving a tether block and an OBJ
export action to the construct General tab.

The subsystem remains under the `EndlessShapes2` namespace so old mod assets and
saved constructs can resolve their original class names. Its public FTD entry
types are:

| Type | FTD role |
| --- | --- |
| `EndlessShapes2.DecorationBuilder` | Block behavior attached to the Decoration Builder item |
| `EndlessShapes2.DecorationBuilderData` | Serialized configuration package for that block |
| `EndlessShapes2.DecorationTetherMove` | Two-handed character item used to move tether blocks |

EndlessShapes2 no longer has its own startup class or Steam polling. The combined
mod's single `GamePlugin_PostLoad` entry point installs its one Harmony UI patch.

## Component and asset identity

These bindings are compatibility-critical:

| Asset | GUID | Runtime binding |
| --- | --- | --- |
| Decoration Builder item | `232087b9-90de-4eb8-9680-ccb876edfe4f` | `DecorationBuilder` |
| Deco character item | `19cf3dd2-86fc-4b97-9e69-23e34cc3f198` | `DecorationTetherMove` |
| Stereoscopic Hologram mesh | `057bd9bb-6442-406a-aee5-c11bb313426c` | `StereoscopicHologram.obj` |

Changing a class namespace/name, component GUID, or data variable ID can make old
constructs fail to bind or silently load different settings.

## User interaction flow

The Decoration Builder is a zero-volume mod block in the Decorations inventory
tab. Its secondary interaction opens two FTD console windows:

1. **Basic Setting** configures thickness, structure material, default palette
   color, texture path, and OBJ path. **Load** parses the OBJ and adds one button
   per `o` or `g` mesh group.
2. **Advanced Setting** configures position, non-uniform scale, Euler rotation,
   automatic tether behavior, build animation/speed, and local-origin projection.
3. Selecting a mesh converts it into an in-memory decoration plan and opens the
   confirmation window.
4. The confirmation window reports face count, line count, and the actual number
   of decorations that will be created. **Build** starts generation. An animated
   run reports progress and exposes an explicit cancel-and-rollback action.

```mermaid
flowchart LR
    A["OBJ file"] --> B["ObjParser"]
    B --> C["OBJ_Mesh groups, vertices, UVs"]
    C --> D["Scale, rotate, translate"]
    D --> E["PolygonDataControl"]
    E --> F["PolygonData decoration plan"]
    F --> G["MADCD_PolygonInput"]
    G --> H["AllConstructDecorations.NewDecoration"]
    H --> I["Native FTD decorations"]
```

## Serialized Decoration Builder settings

`DecorationBuilderData` is a `DataPackage`. IDs are part of the save format and
must not be reordered or reused.

| ID | Property | Default | Meaning |
| ---: | --- | --- | --- |
| 0 | `FaceThickness` | `0.05` | Thickness used for face decorations |
| 1 | `LineThickness` | `0.05` | X/Y thickness of line poles |
| 2 | `SBType` | `Metal` | Structure-block mesh family |
| 3 | `DefaultColorIndex` | `0` | FTD palette index when no texture sample applies |
| 4 | `TexturePath` | empty | Optional PNG/JPG used for palette matching |
| 5 | `OBJ_FilePath` | empty | OBJ input path |
| 6 | `Positioning` | `(0,0,0)` | Model translation |
| 7 | `Scaling` | `(1,1,1)` | Component-wise model scale |
| 8 | `Orientation` | `(0,0,0)` | Unity Euler rotation in degrees |
| 9 | `TP_AutoTetherPoint` | `false` | Round each decoration position to a nearby tether point |
| 10 | `TP_NormalOffset` | `false` | Offset automatic tethers opposite the polygon normal |
| 11 | `TP_DistanceToShift` | `0.87` | Normal-offset distance |
| 12 | `TP_BlockPlacement` | `false` | Place a block at a missing tether point |
| 13 | `TP_BlockGUID` | `8bd20877-417f-4094-ab24-1ebae4d73f85` | Item definition to place as a tether |
| 14 | `BuildAnimation` | `false` | Generate over fixed updates instead of synchronously |
| 15 | `BA_Speed` | `40` | Animated decorations per second |
| 16 | `LocalOrigin` | `false` | Use the construct-local-origin projection path |

Float fields commit on Enter or focus loss. Parsing allows current-locale decimal
commas and invariant decimal points, disables thousands separators, and requires
finite complete text. Invalid or incomplete input is reverted without writing a
zero into the data package. The palette slider is limited to indexes 0 through 31.

## OBJ parser

`ObjParser` is independent of Unity, which allows it to run in the verification
host. It streams text through a `TextReader` and uses invariant-culture numeric
parsing.

The builder normalizes pasted file paths by trimming whitespace, removing one
matching quote pair, and expanding environment variables. Expected missing-file,
access, and OBJ-format failures stay in `InfoStore` and the normal log instead of
opening FTD's full-screen developer stack-trace alert.

Supported records:

- `v x y z`: three coordinates are read; additional values are ignored.
- `vt u [v]`: one or two texture coordinates are read.
- `f`: three or more points in `v`, `v/vt`, `v//vn`, or `v/vt/vn` form.
- `l`: two or more vertex points; optional slash suffixes are ignored.
- `o` and `g`: both start a new selectable `OBJ_Mesh`.
- blank lines, comments, inline comments, spaces, and tabs.

Positive OBJ indexes become zero-based indexes. Negative indexes are resolved
relative to the vertices or UVs already parsed. Index zero, out-of-range indexes,
repeated face points, and adjacent duplicate polyline points are rejected with an
OBJ line number. Forward references are therefore not supported.

Normals are accepted syntactically in face points but are not read or validated.
Material libraries, `usemtl`, smoothing groups, vertex colors, and other OBJ
records are ignored. If geometry appears before an `o` or `g`, it is assigned to
a synthetic group named `Default`.

The parser reverses the point order of each face. `DecorationBuilder.Load` also
mirrors every vertex on X (`x = -x`). Reversing the winding compensates for that
handedness change so polygon normals remain usable.

### Parser safety ceilings

| Limit | Value |
| --- | ---: |
| File size | 256 MiB |
| Line length | 1,048,576 characters |
| Vertices | 2,000,000 |
| Texture coordinates | 2,000,000 |
| Face plus line records | 100,000 |

All floats must be finite. NaN and infinity are rejected. A file must contain at
least one vertex and one face or line.

The primitive ceiling is not the final decoration count. An n-gon is split into
triangles and an arbitrary triangle can require two decorations. The exact
expanded count is calculated before generation and checked against remaining FTD
capacity.

## Coordinate conversion

For every parsed vertex, mesh selection performs this base transform:

```text
v0 = (-OBJ.x, OBJ.y, OBJ.z)
v1 = componentScale(v0, Data.Scaling)
v2 = Euler(Data.Orientation) * v1
```

With `LocalOrigin == false`, the final vertex is:

```text
v = v2 + Data.Positioning + DecorationBuilder.LocalPosition
```

With `LocalOrigin == true`, the builder position is not added. On the main
construct the result is `v2 + Data.Positioning`. On a subconstruct, the code then
applies the exact conversion:

```text
mainInverse = inverse(main rotation)
subOffset = mainInverse * (sub position - main position)
subRotation = inverse(mainInverse * sub rotation)
v = subRotation * (v - subOffset)
```

This transformed vertex list is the only geometry used by classification and
generation; the original OBJ coordinates are not revisited.

## Polygon classification

`PolygonDataControl.PolygonClassify` converts faces and polylines into a list of
`PolygonData`. Each entry represents exactly one future decoration.

Faces are processed from the largest vertex count to the smallest:

- A triangle is classified as right, isosceles, or arbitrary.
- A four-point face becomes one rectangle only if all four corners pass the
  right-angle tolerance; otherwise it is fan-triangulated.
- A 16-point face becomes one ellipse only if its points match the built-in
  16-sample ellipse template within `0.001` units; otherwise it is triangulated.
- Every other n-gon is fan-triangulated as `(0, 1, 2)`, `(0, 2, 3)`, and so on.
- Every OBJ polyline is split into adjacent two-point line segments.

The right-angle tests scale angular error by the longest side and compare it to
`AllowableError_length`, whose default is `0.001`. A right or isosceles triangle
maps to one decoration. A general scalene triangle maps to two complementary
slope decorations, `OtherTriangle_F` and `OtherTriangle_B`.

`PolygonData` stores ordered sides, an average UV sample, its OBJ source line,
and a winding-aware Newell normal computed across every edge:

```text
normalize(NewellSum(all ordered vertices))
```

Lines have a zero normal. UV readability is evaluated independently per face: a
face gets an average UV only if all points have valid texture-coordinate indexes;
otherwise its UV is `(0,0)`.

Before classification, geometry must be finite, planar, convex, non-collapsed,
and free of repeated vertices, zero-length edges, and collinear corners. Invalid
geometry rejects the selected mesh with its OBJ source line. Largest-face-first
processing uses append-only buckets and indexes, and triangulation checks the
final expanded decoration cap before the plan can exceed it.

## Polygon-to-decoration mapping

`MADCD_PolygonInput` turns geometric measurements into the standard decoration
fields `MeshGuid`, `Positioning`, `Scaling`, `Orientation`, `ColorIndex`, and
`MaterialReplacement`. Position, scale, and Euler angles are rounded to four
decimal places before assignment.

| Polygon type | FTD mesh primitive | Main calculation |
| --- | --- | --- |
| Right triangle | Slope | Position at the reference-side midpoint; scale from face thickness and the other side lengths |
| Isosceles triangle | Wedge | Position halfway between base midpoint and apex; scale from base, thickness, and height |
| Arbitrary triangle front/back | Two slopes | Complementary slope placements derived from projected side lengths |
| Rectangle | Block | Position at opposite-corner center; scale from thickness and adjacent side lengths |
| 16-point ellipse | Pole | Position at 16-point center; scale from two diameters and face thickness |
| Line segment | Pole | Position at segment midpoint; scale `(lineThickness, lineThickness, length)` |

Face decorations are offset by half their thickness along the negative polygon
normal. `NormalReversal` exists in the adapter but the builder currently sets it
to `false`.

The selected structure material chooses one set of vanilla FTD block, slope,
wedge, and pole mesh GUIDs:

| Material | Block | Slope | Wedge | Pole |
| --- | --- | --- | --- | --- |
| Glass | `2d519ca8-1f12-4a8e-9340-aa6648b5e799` | `174b5b41-b70e-485d-b00a-a61cc9826b2c` | `573f6de1-1379-49f8-8342-588bd81a50b7` | `f8cd4d8b-c7ef-434e-a404-04423fb5fcae` |
| Alloy | `3cc75979-18ac-46c4-9a5b-25b327d99410` | `911fe222-f9b2-4892-9cd6-8b154d55b2aa` | `f1a3f1bd-b5f7-43bf-9b72-c7a060c24f73` | `04019f87-c371-4574-acfe-b7086557eaba` |
| Wood | `9a0ae372-beb4-4009-b14e-36ed0715af73` | `bdafa446-f615-49cb-94f3-d7652dde6cec` | `0b73f42f-ff32-4654-8857-aa13413bff33` | `0de95539-4751-4355-88bd-156f17b5f64a` |
| Metal | `ab699540-efc8-4592-bc97-204f6a874b3a` | `5548037e-8428-43f8-bcb6-d730dbcd0a79` | `ea2f8200-a920-40fc-9715-d0f66ae5f492` | `ad00935b-e95c-4345-8ea7-646846bc16db` |
| Stone | `710ee212-563b-42f8-acd1-57515479524d` | `11fcac17-e3b9-47d5-aeb8-2224d86b2f1d` | `0630b5e3-d51b-4441-8533-c054e018ee64` | `e62d5f04-5c7a-4524-b97d-d60504babb2f` |
| Lead | `e71e6f97-fbe8-4bf5-9645-d15179ba0c17` | `df61d4c4-a514-4f23-baab-4da8fce066a3` | `6a20f299-6c3e-4406-a859-157075aab08d` | `995d25a2-7237-4cd2-b763-7eb3b3f7e1e7` |
| Heavy armour | `0c03433e-8947-4e7d-9dec-793526fe06d1` | `78b81c0a-44df-4c24-b2a5-5d273737da60` | `a0945b5c-2f1e-45ce-95fb-721e5657afa7` | `60b279e2-9c1e-409f-8248-568039537baa` |
| Rubber | `6c0bab88-aa88-4825-9cf5-55df36aa12b8` | `552d8144-11c0-46e6-8607-927f825b18be` | `f9b1d3e4-c4c8-47eb-9547-b4bf3f3ba730` | `4da4057d-1f5a-4d82-97dd-10502ae2bb80` |

## Texture-to-palette conversion

FTD decorations use construct palette indexes rather than arbitrary per-item
colors. At generation start the builder snapshots all 32 colors from
`Main.ColorsRestricted`.

An optional PNG/JPEG is limited to 64 MiB encoded, 8,192 pixels per dimension,
and 16,777,216 total pixels. Dimensions are parsed before Unity decoding and
checked again after decoding. For each face, `GetPixelBilinear` samples the
average UV. The builder selects the palette entry with the smallest:

```text
RGB Euclidean distance + absolute alpha difference
```

Lines and faces without a usable texture use `DefaultColorIndex`, clamped to
0 through 31. The temporary texture is destroyed after success, failure,
cancellation, reload, or block destruction.

## Generation transaction

`BeginGeneration` refuses to start without a selected non-empty mesh, a construct
decoration manager, connection rules, valid finite settings, a usable optional
tether GUID/texture, or sufficient exact remaining capacity. Although
`NewDecoration` is called with `forceEvenIfMaxReached: true`, FTD 4.3.3 still
checks the 100,000 manager limit first; the flag only bypasses the separate
200-decorations-per-voxel limit.

Before writing content, the builder:

- clears its per-run decoration and block-command journals;
- resolves the optional tether block item definition;
- loads and snapshots the palette and texture;
- captures every setting in an immutable run context; and
- acquires an exclusive lease for the main construct, then saves and disables
  its connection-rule master and request switches.

A second build on the same main construct is rejected. Builds on different main
constructs have independent leases and can proceed concurrently. Animated code
never rereads live `DecorationBuilderData` or shared polygon-adapter state.

Synchronous mode iterates the full plan immediately. Animated mode registers a
fixed-update callback and accumulates fractional build credit:

```text
credit += deltaTime * configuredSpeed
count = floor(credit)
credit -= count
```

An index advances through the immutable queue, so generation is linear instead
of repeatedly shifting a list.

On success, generated content remains while transient state is restored. On any
exception, command rejection, explicit cancel, reload, or `Block.PrepForDelete`,
generated decorations are deleted in reverse order and successful auto-placement
commands are undone. Callback removal, content rollback, connection restoration,
and texture disposal are independently best-effort so one cleanup exception does
not prevent the remaining cleanup or the base FTD deletion path.

## Tether selection during generation

The default tether is the Decoration Builder block's `LocalPosition`. With
automatic tethering enabled, the decoration position is rounded to a grid point.
Optional normal offset moves that grid point opposite the polygon normal and
clamps an axis to zero when the shift would cross the local origin on that axis.

The decoration's stored `Positioning` is then made relative to the selected
tether:

```text
decoration.Positioning -= tetherPosition
```

If automatic block placement is enabled, the builder places the configured item
only when the target grid position is empty. A rejected `PlaceBlockCommand`
aborts and rolls back the build. An invalid or unresolved block GUID fails
preflight before any construct state changes.

The actual decoration is created through the public
`AllConstructDecorations.NewDecoration` API. The calculated common data is copied
through normal decoration properties so FTD receives the ordinary variable
change notifications.

## Tether movement character item

`DecorationTetherMove` owns its pointed block and callback registration per item
instance. Enable/equip registration and disable/finish cleanup are idempotent.
Left click moves in the camera-forward direction; right click uses camera-backward.

The camera direction is transformed into construct-local space. The largest
absolute component selects a deterministic one-cell X, Y, or Z shift, with X then
Y winning exact ties.

Movement is ordered as follows:

1. Require the pointed block's component GUID to match the configured tether.
2. Read a public decoration-list snapshot with `TryGetDecorationsList` and
   journal every original tether and positioning value.
3. Preflight every result; if any position is non-finite or outside +/-10, abort
   the complete move before block commands.
4. Place the destination and remove the source, requiring both command success.
5. Apply every journaled tether/position pair.

Changing both values preserves the decoration's construct-space location while
moving its grid tether. Assigning `TetherPoint.Us` goes through FTD's normal
change path, keeping the decoration position index synchronized.

Any command or property failure restores attempted decorations in reverse order,
then undoes source removal and destination placement. Each rollback action is
best-effort and cannot prevent the following actions.

## OBJ export Harmony patch

`EndlessShapes2Patch` resolves the current private `GeneralTab.Mesh` method and
adds a postfix. The postfix appends an **OBJ file creator** segment and button to
the construct General tab. Startup treats failure to resolve or patch this method
as fatal and removes every Harmony patch owned by the combined mod.

Export creates a unique directory under the FTD profile root:

```text
<sanitized-blueprint-name>-yyyy-MM-dd_HH-mm-ss[-N]/
    Materials.mtl
    MainConstruct.obj
    SubConstruct_<persistent-index>.obj
    Textures/*.jpg
```

File names remove platform-invalid and control characters, trim trailing dots,
use fallbacks for empty names, and are limited to 100 characters. Texture names
include GUID suffixes to prevent collisions. OBJ numbers use the invariant
round-trip float format, so exports are not corrupted by a comma-decimal locale.

For every main construct and subconstruct, the exporter collects:

- runtime chunk meshes from `ConstructableMeshMerger.D`, resolved through the
  public material runtime-ID collection; and
- carried-object submeshes whose matching `sharedMaterials` entry resolves to a
  configured material.

Carried-object submeshes are extracted and transformed from carried-object local
space through world space into construct-local space. Subconstruct vertices are
then transformed into main-construct coordinates. Meshes are grouped and merged
by `MaterialDefinition`, using 32-bit Unity mesh indexes.

The merged geometry is mirrored on X and triangle winding is swapped. Bounds and
normals are recalculated. OBJ output contains `v`, optional `vt`, `f`, `g`, and
`usemtl`; it does not emit `vn` records. If any source in a material group lacks a
complete UV set, that merged group is written without UV references.

`Materials.mtl` declares only materials referenced by emitted geometry.
File/resources textures are copied through a temporary render target, encoded as
JPEG, and deduplicated by texture GUID. Material names include an eight-character
material GUID suffix. Export occurs in a `.partial-<GUID>` directory; it is
renamed only after all OBJ, MTL, and texture writes succeed and is removed on
failure. Temporary render textures, readable copies, extracted submeshes, and
merged output meshes are released in `finally` blocks.

## Resource ownership and failure reporting

User-facing failures are added to `InfoStore`; full exceptions are written with
`AdvLogger`. The importer disposes its `StreamReader`. The exporter disposes all
writers and destroys Unity objects that it owns. It does not destroy chunk meshes
owned by FTD.

The builder keeps explicit journals only for content created during the current
run. It never rolls back decorations or blocks that existed before generation.

## Known constraints and maintenance risks

- Import ignores OBJ normals and materials. Color comes only from the optional
  single texture path and UV averages.
- Invalid, concave, self-intersecting, or transform-collapsed geometry rejects the
  selected mesh. The importer deliberately does not repair or skip primitives.
- The 16-point ellipse recognizer is deliberately narrow; most circular meshes
  are triangulated instead of becoming one pole decoration.
- JPEG export is lossy and does not preserve alpha.
- The General-tab patch is coupled to the private method name `GeneralTab.Mesh`.
  Startup validation prevents a silent partial install after an FTD UI refactor,
  but a game update can still require a new target.

## Verification boundary

Automated checks cover OBJ grammar, locale input, source-line geometry rejection,
all polygon classes, final plan limits, a 100,000-entry linear run, tether
transactions, exporter material/texture/staging rules, public class/asset
bindings, package assets, and resolution of `GeneralTab.Mesh`. The non-Unity host
cannot initialize the complete FTD UI, so the UI postfix is resolved but not
applied there.

The following remain in-game acceptance tests: UI navigation, Unity texture
decoding, geometry appearance, animated cancellation, automatic tether block
placement, tether moves, subconstruct export, locale export, save/load, and
multiplayer replication.

## Source map

Paths are relative to the runtime package directory `EndlessShapesUnlimited/`.

| Area | Source |
| --- | --- |
| Builder lifecycle | `Source/EndlessShapes2/DecorationBuilder.cs` |
| Generation lease | `Source/EndlessShapes2/GenerationCoordinator.cs` |
| Path, float, and texture preflight | `Source/EndlessShapes2/FilePathInput.cs`, `FlexibleFloatParser.cs`, `ImagePreflight.cs` |
| Serialized settings | `Source/EndlessShapes2/DecorationBuilderData.cs` |
| OBJ parser | `Source/EndlessShapes2/ObjParser.cs` |
| Geometry classification | `Source/EndlessShapes2/ES2_Polygon/PolygonDataControl.cs` |
| Primitive mapping | `Source/EndlessShapes2/ES2_Polygon/MADCD_PolygonInput.cs` |
| Decoration data adapter | `Source/EndlessShapes2/ES2_Polygon/MimicAndDecorationCommonData.cs` |
| Material mesh GUIDs | `Source/EndlessShapes2/ES2_Polygon/StructureBlockGUID.cs` |
| Builder UI | `Source/EndlessShapes2/ES2_UI` |
| Tether tool | `Source/EndlessShapes2/DecorationTetherMove.cs` |
| Tether transaction | `Source/EndlessShapes2/TetherMoveTransaction.cs` |
| OBJ export | `Source/EndlessShapes2/OBJ_FileCreation.cs` |
| General-tab patch | `Source/EndlessShapes2/EndlessShapes2Patch.cs` |
