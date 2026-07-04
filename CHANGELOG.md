# Changelog

This file tracks player-visible and release-process changes for EndlessShapes
Unlimited. Dates use `YYYY-MM-DD`.

The packaged Steam Workshop/player readme remains
`EndlessShapesUnlimited/README.md`. Keep this changelog technical enough for
GitHub history, but short enough that it can be copied into release notes.

## 1.0.7 - 2026-07-04

### Added

- Added a Smart Builder `Mat only` preview mode that shows material-family
  preview faces without the wireframe overlay.
- Added Smart Builder cursor tooltips for buttons and interactive panel
  controls, matching Decoration Edit Mode and Surface Builder behavior.
- Added mirrored Decoration Edit placement and anchor preview hints while
  symmetry planes are active, so the original and mirrored side can be checked
  before committing.

### Changed

- Surface Builder previews now draw the actual planned decoration meshes using
  the selected material and paint color before Apply, while keeping draft
  wireframes and points visible for editing.
- Smart Builder blocked-placement and occupied-cell warnings now use the shared
  ESU HUD notification/log path instead of clipped bottom-right status text.
- The packaged Steam/player readme was synchronized with the current Workshop
  description text.
- Main-menu Alerts entries for ESU now use the same cyan accent color as the
  in-game ESU HUD.
- Decoration Edit Mode scale fields now accept `0` on any axis and allow
  nonzero scale values down to `0.00001`; the scale snap minimum now matches
  that precision.

### Fixed

- Fixed Decoration Edit symmetry deletion so deleting a decoration while
  symmetry is active also deletes its mirrored counterpart decorations in the
  same undoable operation.
- Fixed mirrored anchor movement visibility and history handling so symmetry
  anchor edits show the counterpart anchor and keep undo/redo grouped.
- Fixed ESU undo/redo input so remapping away from `Ctrl+Z` / `Ctrl+Y` does not
  leave the hardcoded defaults active.
- Fixed the forwarded vanilla Freeze input so a remapped Freeze key no longer
  keeps Caps Lock active after the profile key map is loaded.
- Fixed the Workshop update notifier so stalled Steam detail requests time out
  and transient failed or empty callbacks retry instead of suppressing update
  checks until restart.
- Fixed ESU paint color swatches and preview tints to read the active craft's
  paint palette instead of a hardcoded color table.
- Fixed Surface Builder material previews on spin-block and subobject
  constructs so normal-flipped generated surfaces use the subobject's local
  position and rotation without inheriting transform scale or shear.
- Fixed Surface Builder context actions and same-anchor guide overlays
  implicitly entering material preview mode after Create Face, shared-anchor
  edits, or helper redraws.
- Fixed Surface Builder normal-flipped split surfaces double-applying the
  normal reversal during ES2 polygon conversion, which could corrupt placed
  decorations on angled spin-block or subobject constructs.

## 1.0.6 - 2026-07-04

### Added

- Added a Decoration Edit Mode viewport right-click context menu for the
  decoration under the cursor. It can select, switch to Move, Rotate, Scale, or
  Anchor, duplicate, hide/show the anchor mesh, or delete the decoration.
- Decoration context-menu deletion now participates in undo/redo and Cancel
  restores deleted existing decorations before leaving the edit session.

### Changed

- Smart Builder fixed-shape scene wires now use the real FtD item mesh hard
  edges instead of an aggregate cuboid outline. Shared internal faces are
  filtered so repeated/tiled preview shapes do not draw inner seams.
- Decoration Builder Mesh Palette 3D grid now uses the same adaptive card
  sizing behavior as Smart Builder's Shapes 3D grid.
- Smart Builder Shapes panel sizing now uses the available panel space more
  aggressively, and the 3D grid palette adapts its columns, card size, preview
  image size, and scroll bounds to the current panel width.
- Surface Builder Extra Tools now sizes its scroll area and material picker from
  the live panel viewport instead of a fixed list height.
- Surface Builder Extra Tools now opens the material override list by default.
- Surface Builder paint color is now shared across Draw and Extra Tools, tints
  previews before placement, and can be assigned per surface face with the face
  color shown in the draft list.
- Surface Builder bridge selection now reuses an already selected edge as the
  first bridge edge, so only the second edge needs Shift-clicking.
- ESU mode hotkeys now treat the profiled vanilla settings binding as the
  authoritative input once loaded, so remapping actions such as Toggle
  Decoration Edit Mode away from `Ctrl+D` works across sessions.
- Surface Builder Extra Tools generated placements now orient from the clicked
  block face normal rather than the camera direction.
- The packaged Steam/player readme was rewritten into a shorter
  Workshop-friendly BBCode description.
- Release-channel documentation now treats
  `[b]Mod latest version X.Y.Z[/b]` as the preferred Workshop update line while
  keeping support for the older plain-text form.

### Fixed

- Fixed the Decoration Edit Hide mesh button so it hides only the selected
  decoration's anchor/tether block mesh, matching the advanced mimic UI, instead
  of switching the whole craft to decoration-only view.
- Fixed the Decoration/Surface View submenu draw order so it renders in the
  foreground over editor panels and the ESU console.
- Fixed ESU Console and Log foreground priority in Smart Builder so it matches
  Decoration Edit and Surface Builder behavior.
- Fixed Smart Builder fixed-shape previews, such as wedges, corners,
  transitions, facing slopes, and offset slopes, drawing as cuboid wireframes.
- Fixed Smart Builder 3D grid palette cards staying fixed to a narrow two-column
  layout when the Shapes panel had more available width.
- Fixed packaging so the developer-only Smart Builder HUD notes are no longer
  included in the shippable mod folder.
- Fixed Workshop update checks failing to parse BBCode-wrapped
  `Mod latest version` lines.

## 1.0.5 - 2026-07-03

### Added

- Added explicit shared-anchor picking for Surface Builder same-anchor mode.
  Surface Draw drafts and Extra Tools generator drafts can now pick one craft
  block as the shared anchor, select it independently, and move it before
  applying.
- Added Surface Builder Extra Tools generators for partial circles, 2D cone
  sectors, cones, frustums, spheres, and partial spheres.
- Added Smart Builder shape palette list/grid previews with categorized filters,
  searchable rows, size buttons, and visible-only 3D thumbnail rendering.
- Added Smart Builder draggable split dividers for Palette, Selected, and Scene
  sections.
- Added a Hide Original Mesh view toggle to the ESU bottom controls.
- Added finer transform snap settings and live snap-setting application so
  values such as `0.625` can be typed without pressing Set every time.
- Added extended decoration scale bounds so ESU scale editing and generated
  placements can exceed the old 10x limit when needed.

### Changed

- Surface Builder preview/apply validation now uses Unity-compatible Euler and
  LookRotation behavior so non-axis-aligned manually-created and bridged faces
  keep strict normal validation without false rejections.
- Surface Builder bridge creation now reuses the normal face winding checks,
  validates conflicts between both new bridge faces, and tries alternate quad
  bridge diagonals when the preferred pairing duplicates or conflicts.
- Surface Builder Shift-click point workflows remain explicit: two selected
  points can become an edge seed, and three selected points create a face from
  the context action.
- Smart Builder world previews now draw hard/boundary mesh outlines and
  aggregate piece hulls instead of internal triangle diagonals and repeated
  placement seams.
- Smart Builder `1` shortcut now cycles only shapes with a 1m candidate and
  forces the selected size back to 1m. Size buttons remain the way to choose
  larger variants.
- Smart Builder material changes now clear preview caches before rebuilding
  palette thumbnails and scene previews.
- Smart Builder right-click now cancels active edits, cancels Add cursor ghosts,
  or deselects the active preview piece before opening piece context menus.
- Smart Builder panel sizing now follows the same resizable split-panel pattern
  as the other ESU editor modes.

### Fixed

- Fixed Surface Builder bridge previews rejected with transform-normal mismatch
  errors on screenshot-like slanted roof triangles.
- Fixed Smart Builder list mode hover previews not appearing.
- Fixed Smart Builder 3D previews disappearing after switching material.
- Fixed Smart Builder grid mode rendering off-screen thumbnails.
- Fixed shared-anchor handle clicks moving the anchor by one block even when the
  mouse had not crossed a block movement threshold.
- Fixed generator same-anchor overlay trying to build a generator preview when
  only an explicit shared anchor had been selected.

### Notes

- Steam Workshop descriptions now use a `[b]Mod latest version 1.0.5[/b]`
  line so installed copies can check whether a newer Workshop version exists.

## 1.0.4 - 2026-07-01

### Added

- Added Vanilla Compatibility Mode, default ON, to block decoration creation and
  saves that would exceed vanilla-compatible decoration data.
- Added an ESU Options toggle for Vanilla Compatibility Mode.
- Added save-time checks that reject actual converted blueprint output when it
  contains ESU sentinel containers or requires ESU-only serializer buffers.
- Added load warnings for existing sentinel craft and over-5,000-decoration
  craft while still allowing those craft to load.
- Added Serialization HUD compatibility status text.
- Added separate HUD wording for serializer wire format versus vanilla load
  compatibility.
- Added a separate Vanilla edit limit HUD status so legacy-wire
  over-5,000-decoration craft are shown as over the vanilla decoration-tool cap
  without claiming vanilla cannot load them.
- Added verifier coverage for compatibility defaults, options UI, save/load
  guard behavior, creation guards, load/cleanup suppression, and unchanged
  legacy/sentinel serializer selection.
- Added an opt-in Blueprint loading options section with `Off`, `V1`, `V2`,
  and `V3` fast-load tiers.
- Added V1 streamed blueprint JSON loading for large `.blueprint` files to avoid
  loading the whole JSON document into one string.
- Added V2 parallel `BlockData` container predecode with serial main-thread
  block-data application in original block order.
- Added passive fast-load diagnostics and a small-blueprint testing override,
  both default OFF.
- Added diagnostics-only V3 collider timing rows that report per-method
  `call_count`, total elapsed time, maximum single-call time, and parent/child
  unaccounted collider time for huge-craft load analysis.
- Added a fast blueprint loading developer guide documenting V1/V2/V3 routing,
  diagnostics, unsafe probes, and vanilla-schema compatibility.
- Added a global ESU notification overlay so Vanilla Compatibility Mode
  blocked-save errors are visible in game instead of only appearing in logs.
- Added Smart Block Builder structural shape descriptors and a categorized
  shape palette for blocks, poles, slopes, wedges, corners, and transitions.
- Added fixed-geometry Smart Builder planning that uses FtD `SizeInfo`
  footprints for preview, collision, symmetry, and commit coverage.
- Added a coarse ESU change test checklist for future build, verifier,
  packaging, HUD, save/load, Surface Builder, and Smart Builder smoke tests.
- Added a Steam Workshop update notifier that can show a non-error main-menu
  mod status row when the Workshop description advertises a newer version.
- Added this changelog.

### Changed

- ESU decoration creation paths now preflight the vanilla 5,000-decoration
  manager cap when Vanilla Compatibility Mode is enabled.
- Compatibility warnings now explain that over-5,000-decoration craft can still
  be legacy wire and may load in vanilla, while vanilla decoration tools remain
  capped/limited above 5,000 per manager.
- Surface Builder, Path/Circle generation, mesh placement, OBJ Decoration
  Builder generation, symmetry placement batches, and redo recreation now reject
  incompatible creation before partial batches are committed.
- Blueprint loading remains fully vanilla by default. Fast-load routing only
  activates for large blueprints, or for small blueprints when the explicit
  testing override is enabled.
- V3 wording now presents it as the recommended opt-in mode for huge
  block-count craft while keeping the default load path vanilla.
- Fast-load route diagnostics now avoid the ambiguous conversion-local
  `file_bytes=-1` field; conversion-only traces log
  `conversion_file_bytes_known` and `conversion_path_file_bytes` instead.
- Surface Builder mirrored triangle placement now preserves intended committed
  normals for right, isosceles, and scalene faces across symmetry variants.
- Surface Builder right-click context menus now cover point, edge, and face
  targets for conservative preview, bridge, select, and delete actions.
- Smart Builder fixed structural shapes now repeat whole items on voxel
  strides instead of distorting block geometry, and handed shape mirrors swap
  to their left/right counterpart when odd-axis symmetry requires it.

### Notes

- Existing sentinel craft are not automatically converted back to vanilla data.
  They must be reduced/repaired and saved again while compatible.
- The serializer still chooses legacy versus sentinel from the actual serialized
  header/data byte sizes. The compatibility mode is a guard around creation and
  saving, not a new save format.
- The Steam/player readme now distinguishes `Wire bytes`, `Vanilla load`, and
  `Vanilla edit limit` compatibility.
- Steam Workshop descriptions now need a `Mod latest version 1.0.4` line so
  installed copies can check whether a newer Workshop version exists.
- Fast blueprint loading does not change the `.blueprint` schema, does not write
  sidecar files, and keeps V3 conservative by falling back unless bulk loading
  can be proven safe.
- Tester data from a 3.8 million block craft showed vanilla loading at about
  `2.5 hours` and V3 loading at about `21 minutes`; active fast-load
  optimization is paused in favor of documentation and polish.

## 1.0.3 - 2026-07-01

### Added

- Added Steam Workshop preview image packaging through `header.jpg`.
- Added GitHub/Steam release-channel documentation.
- Added explicit release workflow documentation for packaging, scanning,
  publishing, and Workshop upload preparation.
- Added Steam Workshop/player readme text synchronized with the live Workshop
  description.

### Changed

- Prepared the first Steam Workshop release.
- Refreshed the GitHub release package and release body to mention the first
  Steam Workshop release.
- Scrubbed public release artifacts and bundled binaries for local-path/privacy
  leaks.
- Clarified the difference between the GitHub technical readme and the packaged
  Steam/player readme.

### Packaging

- GitHub release tag: `v1.0.3`.
- Runtime package should contain only the files listed in
  `RELEASE_CHANNELS.md`.

## 1.0.2 - 2026-07-01

### Added

- Added the expanded ESU editor documentation pass.
- Added Steam Workshop player-readme compatibility notes.
- Added number shortcut cycles for ESU editors:
  - `1`: create/select family.
  - `2`: transform family.
  - `3`: display/preview family.
- Added Surface Builder unification work so Surface, Path, and Circle share the
  left draft/action panel.
- Added Smart Builder and Decoration Edit Mode UI polish checks in the verifier.

### Changed

- Surface Builder draft/settings layout was tightened.
- Extra Tools was changed toward settings-only behavior for Surface Builder.
- Smart Builder panel height and controls were polished.
- Outliner selection behavior was expanded toward normal Shift/Ctrl
  multi-select.
- Paint/color/material UI behavior was improved across ESU panels.

### Fixed

- Fixed several Surface Builder preview/apply and bridge-placement bugs.
- Fixed circle placement orientation to use clicked surface normals instead of
  camera perspective.
- Fixed down-slope preview/transform issues in Smart Builder.
- Fixed ESU number-key shortcuts so they do not leak into vanilla build input
  while an ESU editor is active.

### Packaging

- `v1.0.2` was an intermediate GitHub packaging/release-prep version. The final
  public release line moved to `v1.0.3`.

## 1.0.1 - 2026-06-27

### Added

- Added large Decoration Edit Mode, Surface Builder, and Smart Builder workflow
  polish.
- Added ESU runtime console/log support.
- Added cursor tooltips, toolbar notifications, and panel layout scaling.
- Added multi-piece Smart Builder scenes and material/wireframe preview work.
- Added Decoration Generator Path/Circle planning and shared symmetry support.
- Added broader verifier coverage for ESU editor workflows.

### Changed

- Refined Smart Builder preview controls and connected preview apply flow.
- Improved pointer probing, mesh placement, surface planning, and editor input
  ownership.
- Improved editor UI layout and native-style controls.

## 1.0.0 - 2026-06-27

### Added

- First combined EndlessShapes Unlimited public repository release candidate.
- Combined DecoLimitLifter-style extended decoration serialization with
  EndlessShapes2 OBJ import/export and decoration-building tools.
- Added the technical GitHub readme and simplified packaged player readme.
- Added build and verification tooling around the runtime package.

### Changed

- Slimmed the public repository contents to remove local investigation files,
  copied/decompiled game assets, and private development notes.
- Defined the runtime package layout used by GitHub releases and Steam Workshop
  upload preparation.

## Imported Legacy Notes

These entries are retained from the legacy runtime `releases` file for context.
They may predate the current GitHub tag history or refer to upstream/pre-combine
work.

- 2025-11-01: First public release.
- 2026-06-27: Serializer and Harmony reliability fixes were recorded as
  `1.1.0` in legacy notes.
