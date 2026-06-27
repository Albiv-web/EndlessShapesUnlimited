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
- Added transactional decoration generation, connection-rule restoration,
  animation cleanup, texture cleanup, and command success checks.
- Hardened tether moves and OBJ export path, resource, mesh, and number handling.
- Unified startup under one Harmony owner with all-or-nothing patch validation.
- Retained both MIT copyright notices and added third-party provenance.
- Expanded automated verification from 44 to 61 checks.

## Imported baseline

The serialization core comes from the unreleased DecoLimitLifter 1.1 hardening
work: byte-compatible legacy output, safe sentinel parsing, preserving buffer
growth, exact boundary calculations, and startup patch ownership checks.
