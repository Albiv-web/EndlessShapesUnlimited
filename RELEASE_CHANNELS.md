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
- Root `CHANGELOG.md` for version-to-version history.
- Steam/player package readme at `EndlessShapesUnlimited/README.md`.
- `RELEASE_CHANNELS.md`.
- Runtime assets required to build the package, including `header.jpg`.
- Licenses and third-party notices.

Exclude:

- `artifacts/`.
- `bin/`, `obj/`, `.vs/`, `.user`, `.suo`, `.pdb`.
- Local-only planning files such as `*.local.md` and `TECHNICAL_LOG.md` unless
  intentionally promoted.
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
- `<From-The-Depths-user-mods-folder>/EndlessShapesUnlimited`

The Steam Workshop upload folder should match the GitHub release zip runtime
contents exactly, with one extra practical requirement from FTD:

- `header.jpg` must exist at the top level and must be smaller than 1 MB.

The packaged `README.md` is the Steam Workshop/player-facing text. Do not upload
the root GitHub `README.md` as the Steam description.

Exclude:

- Root GitHub documentation.
- `CHANGELOG.md`.
- `RELEASE_CHANNELS.md`.
- Source, tests, tools, and build scripts.
- Local screenshots or work-in-progress notes.
- Duplicate readmes such as `README Steam version.md`.
- Real user-profile paths, machine names, local install paths, or screenshots
  that show personal folders.

## Current Readme Split

- `README.md`: technical GitHub readme.
- `EndlessShapesUnlimited/README.md`: simple Steam Workshop/player readme,
  packaged into the runtime mod folder.

## Release Checklist

1. Update version metadata in `plugin.json`, `Plugin.cs`, and
   `Properties/AssemblyInfo.cs`.
2. Update `EndlessShapesUnlimited/releases`.
3. Update `CHANGELOG.md` with player-visible changes, packaging changes, and
   compatibility notes for the new version.
4. Update the packaged Steam/player readme in
   `EndlessShapesUnlimited/README.md`. This is also the source text for the
   Steam Workshop description artifact.
5. Keep this exact line in the Steam Workshop description and archived Steam
   description artifact, with the version matching `plugin.json`:

   ```text
   Mod latest version X.Y.Z
   ```

   For the current release:

   ```text
   Mod latest version 1.0.5
   ```

6. Confirm `EndlessShapesUnlimited/header.jpg` exists and is below 1 MB.
7. Run:

   ```powershell
   $env:FTD_DIR = '<path-to-From-The-Depths-install>'
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   ```

8. Confirm the verifier passes and record the zip SHA256 printed by
   `build.ps1`.
9. Copy `artifacts/staging/EndlessShapesUnlimited` to the local FTD Mods folder
   before Steam Workshop upload.
10. Copy `EndlessShapesUnlimited/README.md` to
   `artifacts/SteamWorkshopDescription-vX.Y.Z.md` so the exact Steam text is
   archived locally.
11. Scan the repository, generated zip, staged package, installed Workshop
   folder, Steam description text, GitHub release body, GitHub release asset,
   and GitHub source archive for:

   - real user-profile paths;
   - machine names;
   - local install paths;
   - tokens, passwords, private keys, or API keys;
   - copied source/build/debug files in the runtime package.

12. Commit and push the release changes to `main`.
13. Create or move the `vX.Y.Z` tag to the final release commit and push it.
14. Create or update the GitHub release:

   - release name: `EndlessShapes Unlimited vX.Y.Z`;
   - attach only `EndlessShapesUnlimited-X.Y.Z.zip`;
   - include the exact SHA256 in the release body;
   - mention Steam Workshop when the release is also a Workshop release.
15. Download the public GitHub release asset and source archive, then scan them
   again before announcing the release.

Prefer a fresh version/tag for final public releases. Replacing an asset under
the same GitHub release filename can be affected by download caching, so a fresh
tag and fresh asset name is safer when publishing something users will install.
