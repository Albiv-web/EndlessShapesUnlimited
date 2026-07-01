# Release Channels

This document defines what belongs in each public version of EndlessShapes
Unlimited. Keep it updated whenever the package layout changes.

## GitHub Repository

Purpose: source control, technical history, development, review, and release
automation.

Include:

- Source code under `EndlessShapesUnlimited/Source`.
- Runtime package source under `EndlessShapesUnlimited`.
- Build and verification tooling, including `build.ps1` and
  `tools/EndlessShapesUnlimited.Verification`.
- Technical root `README.md`.
- Steam/player package readme at `EndlessShapesUnlimited/README.md`.
- `RELEASE_CHANNELS.md`.
- Runtime assets required to build the package, including `header.jpg`.
- Licenses and third-party notices.

Exclude:

- `artifacts/`.
- `bin/`, `obj/`, `.vs/`, `.user`, `.suo`, `.pdb`.
- Local-only planning files such as `*.local.md`, `CHANGELOG.md`, and
  `TECHNICAL_LOG.md` unless intentionally promoted.
- Local investigation folders or copied game/decompiled assets.
- Personal paths, tokens, private keys, screenshots, and machine-specific
  configuration.

## GitHub Release Zip

Purpose: downloadable runtime package for users and archival releases.

The zip is produced by `build.ps1` from `artifacts/staging/EndlessShapesUnlimited`.
It should contain one top-level `EndlessShapesUnlimited/` folder.

Include only:

- `0Harmony.dll`
- `EndlessShapesUnlimited.dll`
- `header.header`
- `header.jpg`
- `plugin.json`
- `releases`
- `README.md`
- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- `Assets/`
- `Character Items/`
- `Items/`
- `Meshes/`
- `LICENSES/`

Exclude:

- Source code.
- Build scripts and verifier tooling.
- Git metadata.
- `artifacts/`.
- Debug symbols and IDE files.
- Any file not listed above.

## Steam Workshop Upload Folder

Purpose: exact folder uploaded through From The Depths Workshop tools.

Use either:

- `artifacts/staging/EndlessShapesUnlimited`
- `C:/Users/knuta/Documents/From The Depths/Mods/EndlessShapesUnlimited`

The Steam Workshop upload folder should match the GitHub release zip runtime
contents exactly, with one extra practical requirement from FTD:

- `header.jpg` must exist at the top level and must be smaller than 1 MB.

The packaged `README.md` is the Steam Workshop/player-facing text. Do not upload
the root GitHub `README.md` as the Steam description.

Exclude:

- Root GitHub documentation.
- `RELEASE_CHANNELS.md`.
- Source, tests, tools, and build scripts.
- Local screenshots or work-in-progress notes.
- Duplicate readmes such as `README Steam version.md`.

## Current Readme Split

- `README.md`: technical GitHub readme.
- `EndlessShapesUnlimited/README.md`: simple Steam Workshop/player readme,
  packaged into the runtime mod folder.

## Release Checklist

1. Update version metadata in `plugin.json`, `Plugin.cs`, and
   `Properties/AssemblyInfo.cs`.
2. Update `EndlessShapesUnlimited/releases`.
3. Confirm `EndlessShapesUnlimited/header.jpg` exists and is below 1 MB.
4. Run:

   ```powershell
   $env:FTD_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\From The Depths'
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   ```

5. Confirm the zip SHA256 and release asset.
6. Copy `artifacts/staging/EndlessShapesUnlimited` to the local FTD Mods folder
   before Steam Workshop upload.
