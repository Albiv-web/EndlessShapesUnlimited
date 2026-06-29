# First commit preparation

This note is the scope check for the first public GitHub commit of EndlessShapes
Unlimited.

## Behavioral boundary

The mod should only change persisted craft/blueprint data when FtD's vanilla
serializer would exceed the legacy container shape. Small saves stay on the
vanilla-compatible legacy wire format.

Persistent save/load changes:

- `SuperSaver.Serialise` is replaced with ESU serialization.
  - Legacy output is retained while header and data lengths fit the vanilla
    encoding.
  - The ESU sentinel format is used only when the legacy header/data length
    limits are crossed.
  - Sentinel layout uses the `0xffff` marker followed by 32-bit header and data
    byte lengths.
- `SuperLoader.Deserialise` is replaced so both legacy and ESU sentinel
  containers load.
  - Loader validation rejects malformed header lengths, invalid header offsets,
    truncated packets, and configured hard-limit overflows.
- `SuperSaver.ConvertToReader` is replaced so in-memory saver-to-loader
  conversion uses the same extended wire rules.
- Known FtD serializer buffers are grown when needed:
  - `SuperSaver` reusable header/data pools.
  - `ByteStore.MegaBytes`.
  - `AutoSyncroniser.fullArray`.
  - `SuperSaver.WriteHeader` and `IVariableWriteHelp.ByIdHelpWrite` have
    capacity guards.

Runtime/global behavior that is not a save-format change:

- The decoration manager cap is raised in memory to `100,000` decorations per
  packet manager.
- Serialization telemetry observes save/load/measurement activity and feeds the
  HUD. It should not change serialized bytes.
- The Serialization HUD adds runtime display rows to the native vehicle HUD.
- Decoration Edit Mode is an ESU-owned runtime editor for selecting, moving,
  retethering, creating, assigning meshes/materials, and inspecting decorations.
- Smart Block Builder is an ESU-owned runtime preview tool:
  - It stores lightweight draft state only while open.
  - It creates/moves/scales snapped rectangular voxel previews without requiring
    existing blocks under the pointer.
  - It commits normal FtD block commands only on Apply.
- EndlessShapes2 OBJ import/export tooling remains runtime tooling:
  - Decoration Builder creates decorations/optional tether blocks from OBJ
    meshes.
  - OBJ export adds a vehicle export button to the native mesh tab.
  - The tether-move item retethers existing ES decorations in game.
- Modal input/HUD patches suppress vanilla build HUD/input only while ESU tools
  actively own those interactions.
- The Caps Lock freeze bridge forwards the vanilla freeze action while ESU modal
  HUD suppression is active.

## Patch inventory

Core serialization patches:

- `SuperSaver_Serialise_Patch`
- `SuperLoader_Deserialise_All_Patch`
- `SuperSaver_ConvertToReader_BufferPatch`

Serialization support patches:

- `SuperSaver_WriteHeader_Guard`
- `SuperSaver_ByIdHelpWrite_Guard`
- `SuperSaverBuffersPatch`
- `ByteStorePatch`

Telemetry/HUD patches:

- `BlueprintConverter_SaveTelemetry_Patch`
- `BlueprintConverter_LoadTelemetry_Patch`
- `Decoration_SaveTelemetry_Patch`
- `DecorationManager_LoadTelemetry_Patch`
- `SerializationHudRenderer`

Runtime editor/tool patches:

- `EndlessShapes2Patch`
- `DecorationEditorInputScope` HUD/input patches
- `EsuVanillaInputBridge_cBuild_ToggleFreeze_Patch`

## Reference and license notes

- EndlessShapes2 is credited to Huwa / huwahuwa. The imported source remains
  under the MIT License and the retained notice is
  `LICENSES/EndlessShapes2-MIT.txt`.
- FtD Beamification is credited to DeltaEpsilon / Delta Epsilon /
  DeltaEpsilon7787. ESU bundles the optional external Python tool under
  `Tools/Beamification` and retains its MIT notice in
  `LICENSES/FtD_Beamification-MIT.txt`.
- BuildingTools is credited to Wengh / Weng Haoyu. ESU used
  `wengh/BuildingTools` as an external implementation reference for FTD
  build-tool behavior only; no BuildingTools source, binaries, item definitions,
  Unity assets, or copied license text are bundled in this commit scope.
- Harmony is credited to Andreas Pardeike and its MIT notice is retained in
  `LICENSES/Harmony-MIT.txt`.

## Verification before commit

Current local verification run:

- `dotnet build EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj -c Release --nologo`
- `dotnet run --project tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj -c Release -p:NoWarn=MSB3277`
- `dotnet format EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj --verify-no-changes --no-restore --verbosity minimal`

Before pushing, also run an in-game smoke test:

- Save and reload a small craft and confirm it remains legacy-compatible.
- Save and reload a large decoration-heavy craft that crosses the sentinel
  boundary.
- Open/close Decoration Edit Mode, Surface Builder, and Smart Block Builder,
  including Tab handoff and empty-space Smart Builder preview creation.
- Verify the Decoration Builder OBJ path, generation cancel/rollback, and OBJ
  export button.

## Commit notes

Suggested first commit message:

`Initial EndlessShapes Unlimited serializer and runtime tools`

Suggested PR/commit summary:

- Adds ESU sentinel serialization for oversized FtD decoration containers while
  preserving legacy output for ordinary saves.
- Adds guarded buffer growth for known FtD save/sync buffers.
- Adds serialization HUD telemetry, Decoration Edit Mode, editable Smart Block
  Builder previews, OBJ import/export tooling, and bundled Beamification helper
  files.
- Adds verification coverage for serializer compatibility, sentinel boundaries,
  HUD telemetry, editor/tool integration, OBJ parsing, and package contents.

Known pre-push cleanup checks:

- Review all dirty and untracked files before staging.
- Make sure generated DLL/package artifacts match the Release build.
- Decide whether first commit should include packaged binaries and bundled
  optional tools.
