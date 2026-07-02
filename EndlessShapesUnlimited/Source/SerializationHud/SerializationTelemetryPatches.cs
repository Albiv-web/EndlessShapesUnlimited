using System;
using System.Reflection;
using Assets.Scripts;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.DataManagement.Saving;
using BrilliantSkies.DataManagement.Saving.DeferredChanges;
using BrilliantSkies.DataManagement.Packages;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.Patches;
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
            TryRecordBlueprintUsage(__state, __result);
            VanillaCompatibilityGuard.EnsureBlueprintSaveAllowed(constructable, __result);
            TryComplete(__state, constructable);
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

        private static void TryRecordBlueprintUsage(
            SerializationTelemetryOperation operation,
            global::Blueprint blueprint)
        {
            try
            {
                operation?.RecordBlueprintUsage(blueprint);
            }
            catch (Exception exception)
            {
                LogTelemetryFailure("record blueprint save telemetry", exception);
            }
        }

        private static void TryComplete(
            SerializationTelemetryOperation operation,
            MainConstruct construct)
        {
            try { operation?.Complete(construct); }
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
            out BlueprintLoadTelemetryState __state)
        {
            FastBlueprintLoadConversionScope fastLoadTrace = null;
            try { fastLoadTrace = FastBlueprintLoadRouter.BeginBlueprintConversionTrace(blueprint); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "begin fast blueprint load trace",
                    exception);
            }

            IDisposable suppression = VanillaCompatibilityGuard.BeginSuppression("blueprint load");
            BlueprintSerializationUsage usage = BlueprintSerializationUsage.Empty;
            try { usage = BlueprintSerializationUsageAnalyzer.Analyze(blueprint); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "analyze blueprint load compatibility",
                    exception);
            }

            if (SerializationTelemetry.HasCurrentOperation)
            {
                __state = new BlueprintLoadTelemetryState(
                    null,
                    suppression,
                    usage,
                    fastLoadTrace);
                return;
            }

            try
            {
                SerializationTelemetryOperation operation =
                    SerializationTelemetry.Begin(SerializationOperationKind.Load);
                operation.RecordBlueprintUsage(blueprint);
                __state = new BlueprintLoadTelemetryState(
                    operation,
                    suppression,
                    usage,
                    fastLoadTrace);
            }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "begin blueprint load telemetry",
                    exception);
                __state = new BlueprintLoadTelemetryState(
                    null,
                    suppression,
                    usage,
                    fastLoadTrace);
            }
        }

        internal static void Postfix(
            MainConstruct __result,
            BlueprintLoadTelemetryState __state)
        {
            try { __state?.Operation?.Complete(__result); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "complete blueprint load telemetry",
                    exception);
            }

            try { __state?.CompleteFastLoadTrace(__result); }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "complete fast blueprint load trace",
                    exception);
            }

            VanillaCompatibilityGuard.WarnLoadedBlueprintIfNeeded(
                __result,
                __state?.BlueprintUsage ?? BlueprintSerializationUsage.Empty);
        }

        internal static Exception Finalizer(
            Exception __exception,
            BlueprintLoadTelemetryState __state)
        {
            if (__exception != null)
            {
                try { __state?.FailFastLoadTrace(__exception); }
                catch (Exception exception)
                {
                    BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                        "fail fast blueprint load trace",
                        exception);
                }
            }

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

    internal sealed class BlueprintLoadTelemetryState : IDisposable
    {
        private readonly IDisposable _suppression;
        private readonly FastBlueprintLoadConversionScope _fastLoadTrace;
        private bool _disposed;

        internal BlueprintLoadTelemetryState(
            SerializationTelemetryOperation operation,
            IDisposable suppression,
            BlueprintSerializationUsage blueprintUsage,
            FastBlueprintLoadConversionScope fastLoadTrace)
        {
            Operation = operation;
            _suppression = suppression;
            BlueprintUsage = blueprintUsage ?? BlueprintSerializationUsage.Empty;
            _fastLoadTrace = fastLoadTrace;
        }

        internal SerializationTelemetryOperation Operation { get; }

        internal BlueprintSerializationUsage BlueprintUsage { get; }

        internal void CompleteFastLoadTrace(MainConstruct construct)
        {
            FastBlueprintLoadRouter.CompleteBlueprintConversionTrace(
                _fastLoadTrace,
                construct);
        }

        internal void FailFastLoadTrace(Exception exception)
        {
            FastBlueprintLoadRouter.FailBlueprintConversionTrace(
                _fastLoadTrace,
                exception);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { Operation?.Dispose(); }
            finally
            {
                try { _fastLoadTrace?.Dispose(); }
                finally { _suppression?.Dispose(); }
            }
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
