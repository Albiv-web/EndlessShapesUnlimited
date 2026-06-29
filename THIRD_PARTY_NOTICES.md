# Third-party notices

## EndlessShapes2

EndlessShapes Unlimited incorporates and modifies EndlessShapes2 0.2.2 by
**Huwa / huwahuwa**, with the author's permission. EndlessShapes2 is distributed
under the MIT License; the original notice is retained in
`LICENSES/EndlessShapes2-MIT.txt`.

The canonical import was supplied as `EndlessShapes2.zip` with SHA-256:

`F77FE118FECF3430CB4768C509C09F6A8945C338BCF0D8FA00450F4208E8E23C`

Only the 4.2.9 source and runtime assets were imported. Historical projects,
IDE caches, local paths, prebuilt EndlessShapes binaries, and the assembly
selector were intentionally excluded.

## FtD Beamification

EndlessShapes Unlimited packages an optional copy of
[`DeltaEpsilon7787/FtD_Beamification`](https://github.com/DeltaEpsilon7787/FtD_Beamification)
by **DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787** under
`Tools/Beamification`. Beamification is distributed under the MIT License; the
original notice is retained in `LICENSES/FtD_Beamification-MIT.txt`.

Imported upstream commit:

`a0aaa63010c460563909cc8eb73f2c0aac2bf5ea`

The bundled copy fixes the command-line `debeamify` flag inversion while
retaining the original licence notice. Beamification remains an external Python
blueprint converter and is not loaded by FTD as a runtime mod assembly.

## BuildingTools reference

EndlessShapes Unlimited used
[`wengh/BuildingTools`](https://github.com/wengh/BuildingTools) by
**Wengh / Weng Haoyu** as an external research and implementation reference for
FTD build-tool registration, options/key-binding patterns, and build-mode UI
behavior.

BuildingTools is distributed under the MIT License. ESU does not package any
BuildingTools source files, binaries, Unity assets, item definitions, or copied
license text. If BuildingTools code or assets are imported later, retain its MIT
notice before bundling.

## Harmony

The runtime package includes Harmony 2.3.5 by Andreas Pardeike. Harmony is
distributed under the MIT License; its notice is retained in
`LICENSES/Harmony-MIT.txt`.
