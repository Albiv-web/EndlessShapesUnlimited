# Changelog

## 1.0.0 - 2026-06-27

First EndlessShapes Unlimited release candidate.

- Combined the hardened DecoLimitLifter serializer with EndlessShapes2 0.2.2.
- Ported the current EndlessShapes2 4.2.9 source to FTD 4.3.3.
- Preserved the original `EndlessShapes2` public types, data IDs, and asset GUIDs.
- Removed the historical multi-version selector, Steam polling, prebuilt binaries,
  IDE caches, and private-path build configuration.
- Replaced private decoration/material reflection with current public FTD APIs.
- Added validated, invariant-culture OBJ parsing with negative-index and common
  face-format support.
- Added per-main-construct generation leases, immutable run snapshots, restored
  animation/local-origin controls, progress, cancellation, and best-effort
  rollback through `Block.PrepForDelete()`.
- Replaced quadratic face/build queues, removed shared UV state, added source-line
  geometry diagnostics and final plan caps, and preflighted PNG/JPEG dimensions.
- Made UI float input locale-safe without thousands parsing or invalid-text
  writes; palette input is constrained to indexes 0 through 31.
- Made tether movement validate the source GUID, preflight every linked
  decoration, and atomically undo properties and both block commands on failure.
- Made export filter unused materials, preserve carried-object submesh materials,
  deduplicate textures, suffix filenames with GUIDs, and publish only a complete
  staged directory.
- Unified startup under one Harmony owner with exact constructor-hook and
  `AutoSyncroniser.fullArray` verification plus decoration-limit rollback.
- Made reusable serializer pools idempotent and zero-length loader buffers safe.
- Made packaging Release-only, manifest-versioned, deterministic, allowlisted,
  stale-DLL checked, and privacy/secret scanned.
- Added the FTD Alerts-panel active status used by AdvancedMimicUi, without its
  Steam polling, and normalized quoted file paths with non-disruptive input errors.
- Reworked Decoration Edit Mode from the first plain IMGUI window into an
  FTD-styled editor shell with compact toolbar, outliner, palette-hosted
  inspector/anchor details, draggable left mesh palette, focus-view dimming, runtime FTD icon
  references, and ESU fallback glyphs.
- Hardened Decoration Edit Mode with modal input capture, foreground
  RenderTexture mesh previews, KSP-style pointed-block mesh placement, automatic
  move handles after selection, and session-scoped undo/redo on `Ctrl+Z`,
  `Ctrl+Y`, and `Ctrl+Shift+Z`.
- Added ESU editor view modes: Mixed, Wireframe, Deco only, Mass, Drag, Cost,
  Surface, Important, and Normal. The modes reuse FTD decoration wireframe and
  special-view state where safe, and restore the player's prior view setting on
  exit.
- Added Tab-based ESU mode switching between Decoration Edit Mode and Smart
  Block Builder, with dirty edit sessions requiring explicit Apply or Cancel.
- Tightened the Decoration Edit mesh palette so mesh hover previews are clipped
  to mesh rows, Inspector/Anchor details use vertical-only readable controls,
  ESU transform overlays draw with unlit stable colors, and Move/Rotate/Scale
  share a persistent Global/Local orientation toggle.
- Made the mesh palette 3D grid thumbnail renderer lazy: it only renders visible
  grid cells, uses cached thumbnails first, and budgets new thumbnail generation
  across frames.
- Added a visible mesh count strip to the palette for both list and 3D grid
  modes, showing total meshes and the current filtered/search result count.
- Rebalanced the Decoration Edit layout so the left mesh palette is taller with
  Inspector-only details, color/material appear first, editable transforms move
  to the compact bottom panel, and Anchor context returns to the right outliner
  panel.
- Added an explicit Inspector **Clear** button for removing an active material
  override from the selected decoration.
- Added a compact Anchor toolbar dropdown with an optional anchor-follow mode
  and configurable follow distance. The dropdown now stays pinned open until
  toggled closed, so moved decorations can retether to nearby valid blocks while
  preserving their visible position.
- Tightened Smart Builder Draw mode so a click places an empty origin, switches
  directly to Scale, rejects occupied origins, and applies connected placements
  before disconnected ones.
- Bundled Delta Epsilon's MIT-licensed FtD Beamification Python blueprint
  converter under `Tools/Beamification`, retained its license notice, documented
  its mixed-integer armour-beam packing pipeline, and fixed the command-line
  `debeamify` flag inversion in the ESU copy.
- Retained all bundled MIT copyright notices and added third-party provenance.
- Expanded automated verification beyond the original 44 serializer checks.

## Imported baseline

The serialization core comes from the unreleased DecoLimitLifter 1.1 hardening
work: byte-compatible legacy output, safe sentinel parsing, preserving buffer
growth, exact boundary calculations, and startup patch ownership checks.
