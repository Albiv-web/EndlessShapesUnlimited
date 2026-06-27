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
- Retained both MIT copyright notices and added third-party provenance.
- Expanded automated verification from 44 to 111 checks.

## Imported baseline

The serialization core comes from the unreleased DecoLimitLifter 1.1 hardening
work: byte-compatible legacy output, safe sentinel parsing, preserving buffer
growth, exact boundary calculations, and startup patch ownership checks.
