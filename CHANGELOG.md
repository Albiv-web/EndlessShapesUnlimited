# Changelog

This file tracks player-visible and release-process changes for EndlessShapes
Unlimited. Dates use `YYYY-MM-DD`.

The packaged Steam Workshop/player readme remains
`EndlessShapesUnlimited/README.md`. Keep this changelog technical enough for
GitHub history, but short enough that it can be copied into release notes.

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
- Added an experimental Blueprint loading options section with opt-in `Off`,
  `V1`, `V2`, and `V3` fast-load tiers.
- Added V1 streamed blueprint JSON loading for large `.blueprint` files to avoid
  loading the whole JSON document into one string.
- Added V2 parallel `BlockData` container predecode with serial main-thread
  block-data application in original block order.
- Added passive fast-load diagnostics and a small-blueprint testing override,
  both default OFF.
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
  sidecar files, and keeps V3 conservative by falling back to V2 unless bulk
  loading can be proven safe.

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
