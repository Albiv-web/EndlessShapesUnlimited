using System;
using System.Reflection;
using Assets.Scripts;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.DataManagement.Saving;
using BrilliantSkies.DataManagement.Saving.DeferredChanges;
using BrilliantSkies.DataManagement.Packages;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using HarmonyLib;

namespace DecoLimitLifter.SerializationHud
{
    [HarmonyPatch(
        typeof(BlueprintConverter),
        nameof(BlueprintConverter.Convert),
        new[] { typeof(MainConstruct), typeof(bool) })]
    internal static class BlueprintConverter_SaveTelemetry_Patch
    {
        internal static void Prefix(
            MainConstruct constructable,
            out SerializationTelemetryOperation __state)
        {
            __state = TryBegin(SerializationOperationKind.Save, constructable);
        }

        internal static void Postfix(
            MainConstruct constructable,
            global::Blueprint __result,
            SerializationTelemetryOperation __state)
        {
            TryComplete(__state, constructable, __result);
        }

        internal static Exception Finalizer(
            Exception __exception,
            SerializationTelemetryOperation __state)
        {
            TryDispose(__state);
            return __exception;
        }

        private static SerializationTelemetryOperation TryBegin(
            SerializationOperationKind kind,
            MainConstruct construct)
        {
            if (SerializationTelemetry.HasCurrentOperation)
                return null;

            try { return SerializationTelemetry.Begin(kind, construct); }
            catch (Exception exception)
            {
                LogTelemetryFailure("begin blueprint save telemetry", exception);
                return null;
            }
        }

        private static void TryComplete(
            SerializationTelemetryOperation operation,
            MainConstruct construct,
            global::Blueprint blueprint)
        {
            try
            {
                operation?.RecordBlueprintUsage(blueprint);
                operation?.Complete(construct);
            }
            catch (Exception exception)
            {
                LogTelemetryFailure("complete blueprint save telemetry", exception);
            }
        }

        private static void TryDispose(SerializationTelemetryOperation operation)
        {
            try { operation?.Dispose(); }
            catch (Exception exception)
            {
                LogTelemetryFailure("dispose blueprint save telemetry", exception);
            }
        }

        internal static void LogTelemetryFailure(string action, Exception exception)
        {
            try
            {
                AdvLogger.LogException(
                    $"[EndlessShapes Unlimited] Could not {action}",
                    exception,
                    LogOptions._AlertDevInGame);
            }
            catch
            {
                // Telemetry must never affect the game serializer.
            }
        }
    }

    [HarmonyPatch(
        typeof(BlueprintConverter),
        nameof(BlueprintConverter.Convert),
        new[] { typeof(Force), typeof(Blueprint), typeof(SpawnInstructions) })]
    internal static class BlueprintConverter_LoadTelemetry_Patch
    {
        internal static void Prefix(
            [HarmonyArgument(1)] global::Blueprint blueprint,
            out SerializationTelemetryOperation __state)
        {
            if (SerializationTelemetry.HasCurrentOperation)
            {
                __state = null;
                return;
            }

            try
            {
                __state = SerializationTelemetry.Begin(SerializationOperationKind.Load);
                __state.RecordBlueprintUsage(blueprint);
            }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "begin blueprint load telemetry",
                    exception);
                __state = null;
            }
        }

        internal static void Postfix(
            MainConstruct __result,
            SerializationTelemetryOperation __state)
        {
            try { __state?.Complete(__result); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "complete blueprint load telemetry",
                    exception);
            }
        }

        internal static Exception Finalizer(
            Exception __exception,
            SerializationTelemetryOperation __state)
        {
            try { __state?.Dispose(); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "dispose blueprint load telemetry",
                    exception);
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class Decoration_SaveTelemetry_Patch
    {
        internal static MethodBase TargetMethod() => Plugin.ResolveDecorationSaveTarget();

        internal static void Prefix(
            DataPackage __instance,
            ISuperSaver s,
            out DecorationContributionState __state)
        {
            try
            {
                __state = __instance is Decoration decoration
                    ? SerializationTelemetry.BeginDecorationSave(decoration, s)
                    : null;
            }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "begin decoration save measurement",
                    exception);
                __state = null;
            }
        }

        internal static void Postfix(DecorationContributionState __state)
        {
            try { SerializationTelemetry.CompleteDecorationSave(__state); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "complete decoration save measurement",
                    exception);
            }
        }
    }

    [HarmonyPatch(
        typeof(DecorationManager),
        nameof(DecorationManager.Load),
        new[]
        {
            typeof(ISuperLoader),
            typeof(SaveCriteria),
            typeof(Version),
            typeof(IDeferredChangeSyncManager)
        })]
    internal static class DecorationManager_LoadTelemetry_Patch
    {
        internal static void Prefix(
            DecorationManager __instance,
            ISuperLoader s,
            out DecorationLoadState __state)
        {
            try { __state = SerializationTelemetry.BeginDecorationLoad(__instance, s); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "begin decoration load measurement",
                    exception);
                __state = null;
            }
        }

        internal static void Postfix(DecorationLoadState __state)
        {
            try { SerializationTelemetry.CompleteDecorationLoad(__state); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "complete decoration load measurement",
                    exception);
            }
        }
    }
}
