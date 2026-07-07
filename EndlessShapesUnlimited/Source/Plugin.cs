using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Scripts;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.DataManagement.Saving;
using BrilliantSkies.DataManagement.Saving.DeferredChanges;
using BrilliantSkies.DataManagement.Packages;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.DataManagement.Serialisation.VariableTypes;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.HUD;
using BrilliantSkies.Ftd.Cameras;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ftd.Constructs.UI;
using BrilliantSkies.Modding;
using DecoLimitLifter.AutomationEditMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using DecoLimitLifter.SmartBuildMode;
using EndlessShapes2;
using HarmonyLib;

namespace DecoLimitLifter
{
    public sealed class Plugin : GamePlugin_PostLoad
    {
        private const string HarmonyId = "alb.endlessshapesunlimited";

        public string name => "EndlessShapes Unlimited";
        public Version version => new Version(1, 0, 7, 0);

        public void OnLoad()
        {
            Harmony harmony = null;
            var startup = new StartupTransaction(
                () => harmony?.UnpatchAll(HarmonyId));
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Patches.FastBlueprintLoadRouter.InstallOptionalV3BlockStatePatch(harmony);
                Patches.FastBlueprintLoadRouter.InstallOptionalV3DColliderInternalTimingPatch(harmony);
                DecorationTooltipSuppressor.Install(harmony);

                Patches.ByteStorePatch.EnsureMegaBytes();
                Patches.SuperSaverBuffersPatch.OnBootEnsurePools();
                VerifyRequiredPatches();
                ExtendedSerialization.DestinationBuffer.VerifyAutoSyncTarget();
                int previousDecorationLimit = Patches.DecoLimitsPatch.ApplyLimit();
                startup.TrackDecorationLimit(
                    previousDecorationLimit,
                    Patches.DecoLimitsPatch.RestoreLimit);
                startup.TrackRollback(SerializationHudRegistration.Unregister);
                SerializationHudRegistration.Register();
                startup.TrackRollback(DecorationEditModeRegistration.Unregister);
                DecorationEditModeRegistration.Register();
                startup.TrackRollback(SmartBuildModeRegistration.Unregister);
                SmartBuildModeRegistration.Register();
                startup.TrackRollback(AutomationEditModeRegistration.Unregister);
                AutomationEditModeRegistration.Register();
                startup.TrackRollback(EsuHudNotificationOverlayRegistration.Unregister);
                EsuHudNotificationOverlayRegistration.Register();
                startup.Commit();
            }
            catch (Exception exception)
            {
                // Rollback actions are independent and cannot hide the startup failure.
                IReadOnlyList<Exception> rollbackErrors = startup.Rollback();
                Exception logged = rollbackErrors.Count == 0
                    ? exception
                    : new AggregateException(
                        "Startup failed and rollback encountered additional errors.",
                        new[] { exception }.Concat(rollbackErrors));
                try
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Failed to install required patches",
                        logged,
                        LogOptions._AlertDevAndCustomerInGame);
                }
                catch
                {
                    // Logging must never leave a failed startup partially installed.
                }
                return;
            }

            try
            {
                RegisterActiveStatus();
                WorkshopUpdateNotifier.Start(name, version);
            }
            catch (Exception exception)
            {
                try
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Loaded, but the active-mod status could not be registered",
                        exception,
                        LogOptions._AlertDevInGame);
                }
                catch
                {
                    // Startup is already committed; logging cannot change that state.
                }
            }

            try
            {
                AdvLogger.LogInfo(
                    $"[EndlessShapes Unlimited] v{version.ToString(3)} loaded. " +
                    $"Decoration limit={DecoLimits.MaxDecorations}; OBJ tools, serialization HUD, decoration edit mode, and Smart Block Builder active.");
            }
            catch
            {
                // Startup and active status are already committed.
            }
        }

        private void RegisterActiveStatus()
        {
            string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(modPath))
                throw new InvalidOperationException("The installed mod folder could not be resolved.");

            ModProblems.AllModProblems.Remove(modPath);
            ModProblems.AddModProblem(
                EsuAlertText.HudColorize($"{name}  v{version.ToString(3)}  Active!"),
                modPath,
                string.Empty,
                false);
        }

        private static void VerifyRequiredPatches()
        {
            var required = new List<MethodBase>
            {
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.Serialise)),
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.ConvertToReader)),
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.WriteHeader)),
                AccessTools.Constructor(typeof(SuperSaver), Type.EmptyTypes),
                EndlessShapes2Patch.ResolveTarget(),
                ResolveBlueprintSaveTarget(),
                ResolveBlueprintLoadTarget(),
                ResolveBlueprintFileJsonSaveTarget(),
                ResolveBlueprintFileManagerFactoryTarget(),
                ResolveFastBlueprintFileModelLoadTarget(),
                ResolveConstructExtraInfoDataArrayTarget(),
                ResolveConstructExtraInfoProvideInfoToBlocksTarget(),
                ResolveConstructExtraInfoDoubleArrayTarget(),
                ResolveConstructExtraInfoUpgradeConstructTarget(),
                ResolveAllConstructInitialiseStage2Target(),
                ResolveVanillaDecorationCreationTarget(),
                ResolveDecorationSaveTarget(),
                ResolveDecorationLoadTarget(),
                ResolveSerializationHudTarget(),
                ResolveDecorationEditorHudTarget("DrawBuildModeCommands"),
                ResolveDecorationEditorHudTarget("ShowMouseFunctions"),
                ResolveDecorationEditorHudTarget("DrawRhs"),
                ResolveDecorationEditorHudTarget("DrawWeaponInfo"),
                ResolveDecorationEditorHudTarget("DrawInteractionIcon"),
                ResolveDecorationEditorHudTarget("DisplayCorrectToolBar"),
                ResolveDecorationEditorBuildUpdateTarget(),
                ResolveDecorationEditorCameraUpdateTarget(),
                ResolveBuildFreezeTarget(),
                DecorationTooltipSuppressor.ResolveBlockGetToolTipTarget()
            };

            required.AddRange(typeof(SuperLoader)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => method.Name == nameof(SuperLoader.Deserialise))
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType == typeof(byte[]) &&
                           parameters[1].ParameterType == typeof(uint).MakeByRefType() &&
                           parameters[2].ParameterType == typeof(byte);
                }));

            var writeMap = typeof(SuperSaver).GetInterfaceMap(typeof(IVariableWriteHelp));
            for (int i = 0; i < writeMap.InterfaceMethods.Length; i++)
            {
                var method = writeMap.InterfaceMethods[i];
                var parameters = method.GetParameters();
                if (method.Name == nameof(IVariableWriteHelp.ByIdHelpWrite) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(uint) &&
                    parameters[1].ParameterType == typeof(uint))
                {
                    required.Add(writeMap.TargetMethods[i]);
                    break;
                }
            }

            var missing = required
                .Where(method => method == null ||
                    Harmony.GetPatchInfo(method)?.Owners?.Contains(HarmonyId) != true)
                .Select(method => method == null
                    ? "<unresolved target>"
                    : $"{method.DeclaringType?.FullName}.{method.Name}")
                .ToArray();

            if (missing.Length > 0)
                throw new InvalidOperationException(
                    "Required Harmony patches are missing: " + string.Join(", ", missing));

            MethodBase constructor = AccessTools.Constructor(typeof(SuperSaver), Type.EmptyTypes);
            VerifyExactPatch(
                constructor,
                AccessTools.Method(
                    typeof(Patches.SuperSaverBuffersPatch),
                    "ConstructorPrefix"),
                prefix: true);
            VerifyExactPatch(
                constructor,
                AccessTools.Method(
                    typeof(Patches.ByteStorePatch),
                    "AfterSuperSaverConstructor"),
                prefix: false);

            MethodBase blueprintSave = ResolveBlueprintSaveTarget();
            VerifyExactPatch(
                blueprintSave,
                AccessTools.Method(
                    typeof(BlueprintConverter_SaveTelemetry_Patch),
                    nameof(BlueprintConverter_SaveTelemetry_Patch.Prefix)),
                prefix: true);
            VerifyExactPatch(
                blueprintSave,
                AccessTools.Method(
                    typeof(BlueprintConverter_SaveTelemetry_Patch),
                    nameof(BlueprintConverter_SaveTelemetry_Patch.Postfix)),
                prefix: false);
            VerifyExactFinalizer(
                blueprintSave,
                AccessTools.Method(
                    typeof(BlueprintConverter_SaveTelemetry_Patch),
                    nameof(BlueprintConverter_SaveTelemetry_Patch.Finalizer)));

            MethodBase blueprintLoad = ResolveBlueprintLoadTarget();
            VerifyExactPatch(
                blueprintLoad,
                AccessTools.Method(
                    typeof(BlueprintConverter_LoadTelemetry_Patch),
                    nameof(BlueprintConverter_LoadTelemetry_Patch.Prefix)),
                prefix: true);
            VerifyExactPatch(
                blueprintLoad,
                AccessTools.Method(
                    typeof(BlueprintConverter_LoadTelemetry_Patch),
                    nameof(BlueprintConverter_LoadTelemetry_Patch.Postfix)),
                prefix: false);
            VerifyExactFinalizer(
                blueprintLoad,
                AccessTools.Method(
                    typeof(BlueprintConverter_LoadTelemetry_Patch),
                    nameof(BlueprintConverter_LoadTelemetry_Patch.Finalizer)));

            VerifyExactPatch(
                ResolveBlueprintFileJsonSaveTarget(),
                AccessTools.Method(
                    typeof(Patches.BlueprintFile_Save_BlueprintJsonStreaming_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveBlueprintFileManagerFactoryTarget(),
                AccessTools.Method(
                    typeof(Patches.FileManagerMaker_CreateBlueprintFileModelSaver_BlueprintJsonStreaming_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactPatch(
                ResolveFastBlueprintFileModelLoadTarget(),
                AccessTools.Method(
                    typeof(Patches.BlueprintFile_Load_FastLoad_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveConstructExtraInfoDataArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DataArray_FastLoad_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveConstructExtraInfoDataArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DataArray_FastLoad_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactFinalizer(
                ResolveConstructExtraInfoDataArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DataArray_FastLoad_Patch),
                    "Finalizer"));
            VerifyExactPatch(
                ResolveConstructExtraInfoProvideInfoToBlocksTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_ProvideInfoToBlocks_V3BulkLoad_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveConstructExtraInfoProvideInfoToBlocksTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_ProvideInfoToBlocks_V3BulkLoad_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactFinalizer(
                ResolveConstructExtraInfoProvideInfoToBlocksTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_ProvideInfoToBlocks_V3BulkLoad_Patch),
                    "Finalizer"));
            VerifyExactPatch(
                ResolveConstructExtraInfoDoubleArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DoubleArray_FastLoadDiagnostics_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveConstructExtraInfoDoubleArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DoubleArray_FastLoadDiagnostics_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactFinalizer(
                ResolveConstructExtraInfoDoubleArrayTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_DoubleArray_FastLoadDiagnostics_Patch),
                    "Finalizer"));
            VerifyExactPatch(
                ResolveConstructExtraInfoUpgradeConstructTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_UpgradeConstruct_FastLoadDiagnostics_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveConstructExtraInfoUpgradeConstructTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_UpgradeConstruct_FastLoadDiagnostics_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactFinalizer(
                ResolveConstructExtraInfoUpgradeConstructTarget(),
                AccessTools.Method(
                    typeof(Patches.ConstructExtraInfo_UpgradeConstruct_FastLoadDiagnostics_Patch),
                    "Finalizer"));
            MethodBase v3BlockStateChanged = ResolveBlockBlockStateChangedTarget();
            if (v3BlockStateChanged != null)
            {
                VerifyExactPatch(
                    v3BlockStateChanged,
                    AccessTools.Method(
                        typeof(Patches.Block_BlockStateChanged_V3BulkLoad_Patch),
                        "Prefix"),
                    prefix: true);
            }
            VerifyExactPatch(
                ResolveAllConstructInitialiseStage2Target(),
                AccessTools.Method(
                    typeof(Patches.AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveAllConstructInitialiseStage2Target(),
                AccessTools.Method(
                    typeof(Patches.AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactFinalizer(
                ResolveAllConstructInitialiseStage2Target(),
                AccessTools.Method(
                    typeof(Patches.AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch),
                    "Finalizer"));
            VerifyExactTranspiler(
                ResolveAllConstructInitialiseStage2Target(),
                AccessTools.Method(
                    typeof(Patches.AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch),
                    "Transpiler"));

            VerifyExactPatch(
                ResolveVanillaDecorationCreationTarget(),
                AccessTools.Method(
                    typeof(AllConstructDecorations_NewDecoration_VanillaCompatibility_Patch),
                    "Prefix"),
                prefix: true);

            VerifyExactPatch(
                ResolveDecorationSaveTarget(),
                AccessTools.Method(
                    typeof(Decoration_SaveTelemetry_Patch),
                    nameof(Decoration_SaveTelemetry_Patch.Prefix)),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationSaveTarget(),
                AccessTools.Method(
                    typeof(Decoration_SaveTelemetry_Patch),
                    nameof(Decoration_SaveTelemetry_Patch.Postfix)),
                prefix: false);
            VerifyExactPatch(
                ResolveDecorationLoadTarget(),
                AccessTools.Method(
                    typeof(DecorationManager_LoadTelemetry_Patch),
                    nameof(DecorationManager_LoadTelemetry_Patch.Prefix)),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationLoadTarget(),
                AccessTools.Method(
                    typeof(DecorationManager_LoadTelemetry_Patch),
                    nameof(DecorationManager_LoadTelemetry_Patch.Postfix)),
                prefix: false);
            VerifyExactPatch(
                ResolveSerializationHudTarget(),
                AccessTools.Method(
                    typeof(SerializationHudRenderer),
                    nameof(SerializationHudRenderer.Postfix)),
                prefix: false);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("DrawBuildModeCommands"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_DrawBuildModeCommands_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("ShowMouseFunctions"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_ShowMouseFunctions_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("DrawRhs"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_DrawRhs_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("DrawWeaponInfo"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_DrawWeaponInfo_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("DrawInteractionIcon"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_DrawInteractionIcon_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorHudTarget("DisplayCorrectToolBar"),
                AccessTools.Method(
                    typeof(DecorationEditor_cHud_DisplayCorrectToolBar_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorBuildUpdateTarget(),
                AccessTools.Method(
                    typeof(DecorationEditor_cBuild_RunUpdate_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveDecorationEditorCameraUpdateTarget(),
                AccessTools.Method(
                    typeof(DecorationEditor_BuildCameraMode_RunUpdate_Patch),
                    "Prefix"),
                prefix: true);
            VerifyExactPatch(
                ResolveBuildFreezeTarget(),
                AccessTools.Method(
                    typeof(EsuVanillaInputBridge_cBuild_ToggleFreeze_Patch),
                    "Postfix"),
                prefix: false);
            VerifyExactTranspiler(
                DecorationTooltipSuppressor.ResolveBlockGetToolTipTarget(),
                AccessTools.Method(
                    typeof(DecorationTooltipSuppressor),
                    "Transpiler"));
            if (DecorationTooltipSuppressor.PatchedBlockTooltipColorCheckCount != 2)
                throw new InvalidOperationException(
                    "Required Block.GetToolTip paint-tooltip transpiler patched " +
                    DecorationTooltipSuppressor.PatchedBlockTooltipColorCheckCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " color checks instead of 2.");
        }

        internal static MethodBase ResolveBlueprintSaveTarget() =>
            AccessTools.Method(
                typeof(BlueprintConverter),
                nameof(BlueprintConverter.Convert),
                new[] { typeof(MainConstruct), typeof(bool) });

        internal static MethodBase ResolveBlueprintLoadTarget() =>
            AccessTools.Method(
                typeof(BlueprintConverter),
                nameof(BlueprintConverter.Convert),
                new[] { typeof(Force), typeof(Blueprint), typeof(SpawnInstructions) });

        internal static MethodBase ResolveBlueprintFileJsonSaveTarget() =>
            AccessTools.Method(
                typeof(BlueprintFile),
                nameof(BlueprintFile.Save),
                new[] { typeof(Blueprint) });

        internal static MethodBase ResolveBlueprintFileManagerFactoryTarget() =>
            AccessTools.Method(
                typeof(FileManagerMaker),
                nameof(FileManagerMaker.CreateBlueprintFileModelSaver),
                new[] { typeof(string) });

        internal static MethodBase ResolveFastBlueprintFileModelLoadTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveBlueprintFileModelLoadDataTarget();

        internal static MethodBase ResolveConstructExtraInfoDataArrayTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveConstructExtraInfoDataArrayTarget();

        internal static MethodBase ResolveConstructExtraInfoProvideInfoToBlocksTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveConstructExtraInfoProvideInfoToBlocksTarget();

        internal static MethodBase ResolveConstructExtraInfoDoubleArrayTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveConstructExtraInfoDoubleArrayTarget();

        internal static MethodBase ResolveConstructExtraInfoUpgradeConstructTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveConstructExtraInfoUpgradeConstructTarget();

        internal static MethodBase ResolveBlockBlockStateChangedTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveBlockBlockStateChangedTarget();

        internal static MethodBase ResolveAllConstructInitialiseStage2Target() =>
            Patches.FastBlueprintLoadRouter.ResolveAllConstructInitialiseStage2Target();

        internal static MethodBase ResolveStage2ModuleExternalLinkupTarget() =>
            Patches.FastBlueprintLoadRouter.ResolveStage2ModuleExternalLinkupTarget();

        internal static MethodBase ResolvePartStatusRegisterCheckableBlockTarget() =>
            Patches.FastBlueprintLoadRouter.ResolvePartStatusRegisterCheckableBlockTarget();

        internal static MethodBase ResolvePartStatusUnregisterCheckableBlockTarget() =>
            Patches.FastBlueprintLoadRouter.ResolvePartStatusUnregisterCheckableBlockTarget();

        internal static MethodBase ResolveVanillaDecorationCreationTarget() =>
            AccessTools.Method(
                typeof(AllConstructDecorations),
                nameof(AllConstructDecorations.NewDecoration),
                new[] { typeof(Vector3i), typeof(bool), typeof(bool), typeof(bool) });

        internal static MethodBase ResolveDecorationSaveTarget() =>
            AccessTools.Method(
                typeof(DataPackage),
                nameof(DataPackage.Save),
                new[] { typeof(ISuperSaver), typeof(SaveCriteria) });

        internal static MethodBase ResolveDecorationLoadTarget() =>
            AccessTools.Method(
                typeof(DecorationManager),
                nameof(DecorationManager.Load),
                new[]
                {
                    typeof(ISuperLoader),
                    typeof(SaveCriteria),
                    typeof(Version),
                    typeof(IDeferredChangeSyncManager)
                });

        internal static MethodBase ResolveSerializationHudTarget() =>
            AccessTools.Method(
                typeof(DrawExtraVehicleInfo),
                nameof(DrawExtraVehicleInfo.DrawRHSTextDisplay),
                new[] { typeof(MainConstruct), typeof(Rectum) });

        internal static MethodBase ResolveDecorationEditorHudTarget(string methodName) =>
            AccessTools.Method(typeof(cHud), methodName);

        internal static MethodBase ResolveDecorationEditorBuildUpdateTarget() =>
            AccessTools.Method(typeof(cBuild), nameof(cBuild.RunUpdate));

        internal static MethodBase ResolveDecorationEditorCameraUpdateTarget() =>
            AccessTools.Method(typeof(BuildCameraMode), nameof(BuildCameraMode.RunUpdate));

        internal static MethodBase ResolveBuildFreezeTarget() =>
            AccessTools.Method(typeof(cBuild), nameof(cBuild.ToggleFreeze));

        private static void VerifyExactPatch(MethodBase target, MethodInfo patchMethod, bool prefix)
        {
            HarmonyLib.Patches patchInfo = target == null ? null : Harmony.GetPatchInfo(target);
            IEnumerable<Patch> patches = prefix ? patchInfo?.Prefixes : patchInfo?.Postfixes;
            if (target == null || patchMethod == null ||
                patches?.Any(patch => patch.owner == HarmonyId && patch.PatchMethod == patchMethod) != true)
            {
                throw new InvalidOperationException(
                    $"Required Harmony {(prefix ? "prefix" : "postfix")} is missing: " +
                    $"{patchMethod?.DeclaringType?.FullName}.{patchMethod?.Name}");
            }
        }

        private static void VerifyExactFinalizer(MethodBase target, MethodInfo patchMethod)
        {
            HarmonyLib.Patches patchInfo = target == null ? null : Harmony.GetPatchInfo(target);
            if (target == null || patchMethod == null ||
                patchInfo?.Finalizers?.Any(
                    patch => patch.owner == HarmonyId && patch.PatchMethod == patchMethod) != true)
            {
                throw new InvalidOperationException(
                    "Required Harmony finalizer is missing: " +
                    $"{patchMethod?.DeclaringType?.FullName}.{patchMethod?.Name}");
            }
        }

        private static void VerifyExactTranspiler(MethodBase target, MethodInfo patchMethod)
        {
            HarmonyLib.Patches patchInfo = target == null ? null : Harmony.GetPatchInfo(target);
            if (target == null || patchMethod == null ||
                patchInfo?.Transpilers?.Any(
                    patch => patch.owner == HarmonyId && patch.PatchMethod == patchMethod) != true)
            {
                throw new InvalidOperationException(
                    "Required Harmony transpiler is missing: " +
                    $"{patchMethod?.DeclaringType?.FullName}.{patchMethod?.Name}");
            }
        }

        public bool AfterAllPluginsLoaded() => true;
        public void OnSave() { }
    }
}
