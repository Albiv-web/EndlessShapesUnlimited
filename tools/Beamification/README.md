# FtD Beamification bundled with EndlessShapes Unlimited

This folder contains a bundled copy of
[`DeltaEpsilon7787/FtD_Beamification`](https://github.com/DeltaEpsilon7787/FtD_Beamification)
by **DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787**.

Source import:

- Upstream repository: `https://github.com/DeltaEpsilon7787/FtD_Beamification`
- Imported commit: `a0aaa63010c460563909cc8eb73f2c0aac2bf5ea`
- License: MIT, retained in `LICENSE` and in ESU's `LICENSES` folder.

## What this is

Beamification is an external Python blueprint converter. It is not loaded by
From The Depths as a Harmony mod and it is not part of `EndlessShapesUnlimited.dll`.
It reads a `.blueprint` JSON file, converts eligible armour blocks into longer
beam variants, and writes a new blueprint file.

## ESU local modification

The bundled copy fixes one CLI flag inversion:

- upstream CLI set `debeamify = args.procedure == "beamify"`;
- ESU sets `debeamify = args.procedure == "debeamify"`.

GUI mode in the upstream script already used the intended boolean semantics.

## Requirements

Use Python with the packages listed in `requirements.txt`:

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
```

## CLI examples

Beamify a craft for front/back threat priority:

```powershell
python __main__.py cli --ftd "H:\SteamLibrary\steamapps\common\From The Depths" --input "input.blueprint" --output "output.blueprint" beamify --grain xyz --bias random
```

Convert eligible armour back into 1 m blocks:

```powershell
python __main__.py cli --ftd "H:\SteamLibrary\steamapps\common\From The Depths" --input "input.blueprint" --output "output.blueprint" debeamify
```

The tool writes a new blueprint; it does not modify the source file in place.
Back up important constructs before converting them.
