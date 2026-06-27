# Changelog

## 1.1.0 - 2026-06-27

- Fixed startup patch installation; required Harmony targets now install as one verified set.
- Set the game's static decoration limit to 100,000 after serializer verification succeeds.
- Removed the obsolete `BlueprintConverter` transpiler.
- Added exact shared sizing for legacy and sentinel containers.
- Preserved existing bytes when header, data, `ByteStore`, or multiplayer buffers grow.
- Reused enlarged header/data pools across later saver instances.
- Added strict length, structure, truncation, ceiling, and object-ID validation.
- Fixed `ConvertToReader` sizing for sentinel payloads and exact 65,535-byte multiples.
- Added fail-closed startup behavior to avoid leaving partial Harmony patches active.
- Added an installed-game verification executable with 44 regression checks.
- Made game assembly hint paths use `FTD_DIR` and automated Release DLL packaging.
- Disabled Release PDB generation and bumped the mod/assembly version to 1.1.0.

Compatibility remains unchanged: legacy payloads load in vanilla; sentinel payloads require version 1.1.0 on every reader or multiplayer peer.

## 1.0.0 - 2025-11-01

- Initial public release.
