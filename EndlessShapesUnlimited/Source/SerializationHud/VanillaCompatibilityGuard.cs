using System;
using System.Globalization;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter.SerializationHud
{
    internal sealed class VanillaCompatibilityException : InvalidOperationException
    {
        internal VanillaCompatibilityException(string message)
            : base(message)
        {
        }
    }

    internal enum VanillaLoadCompatibilityKind
    {
        VanillaCompatible,
        SentinelFormat,
        ModBuffer
    }

    internal enum VanillaEditorCompatibilityKind
    {
        WithinLimit,
        TooManyDecorations
    }

    internal static class VanillaCompatibilityGuard
    {
        internal const int VanillaDecorationLimit = 5_000;
        internal const string CreationBlockedMessage =
            "Vanilla compatibility mode blocked this: this construct would exceed 5,000 decorations.";
        internal const string SaveExtendedFormatBlockedMessage =
            "Vanilla compatibility mode blocked saving: this craft would save in ESU extended format. Reduce decorations or disable the compatibility mode.";

        [ThreadStatic]
        private static int _suppressionDepth;

        internal static bool Enabled
        {
            get
            {
                try { return SerializationHudProfile.Data?.EnforceVanillaCompatibility == true; }
                catch { return true; }
            }
        }

        internal static bool Suppressed => _suppressionDepth > 0;

        internal static IDisposable BeginSuppression(string reason) =>
            new SuppressionScope();

        internal static bool TryAllowDecorationCreation(
            AllConstructDecorations decorations,
            int additionalDecorations,
            out string message)
        {
            message = null;
            if (!Enabled || Suppressed || decorations == null)
                return true;

            long requested = (long)Math.Max(0, decorations.DecorationCount) +
                             Math.Max(0, additionalDecorations);
            if (requested <= VanillaDecorationLimit)
                return true;

            message = CreationBlockedMessage + " " +
                      FormatCount(Math.Max(0, decorations.DecorationCount)) +
                      "/" +
                      FormatCount(VanillaDecorationLimit) +
                      " currently present, " +
                      FormatCount(Math.Max(0, additionalDecorations)) +
                      " requested.";
            return false;
        }

        internal static void EnsureDecorationCreationAllowed(
            AllConstructDecorations decorations,
            int additionalDecorations)
        {
            if (!TryAllowDecorationCreation(decorations, additionalDecorations, out string message))
                throw new VanillaCompatibilityException(message);
        }

        internal static void EnsureBlueprintSaveAllowed(
            MainConstruct construct,
            global::Blueprint blueprint)
        {
            if (Suppressed)
                return;

            DecorationUsageSnapshot decorations = DecorationUsageSnapshot.Capture(construct);
            BlueprintSerializationUsage usage = BlueprintSerializationUsageAnalyzer.Analyze(blueprint);
            bool overEditorLimit = TryFindOverVanillaDecorationLimit(decorations, out _);
            VanillaLoadCompatibilityKind loadKind =
                ClassifyVanillaLoadCompatibility(decorations, usage);
            if (!overEditorLimit && loadKind == VanillaLoadCompatibilityKind.VanillaCompatible)
                return;

            if (Enabled)
                AbortSave(FormatSaveBlockedMessage(overEditorLimit, loadKind, decorations, usage));

            ReportWarning(FormatSaveWarning(overEditorLimit, loadKind, decorations, usage));
        }

        internal static void WarnLoadedBlueprintIfNeeded(
            MainConstruct construct,
            BlueprintSerializationUsage blueprintUsage)
        {
            DecorationUsageSnapshot decorations = DecorationUsageSnapshot.Capture(construct);
            bool overEditorLimit = TryFindOverVanillaDecorationLimit(decorations, out _);
            VanillaLoadCompatibilityKind loadKind =
                ClassifyVanillaLoadCompatibility(decorations, blueprintUsage);
            if (!overEditorLimit && loadKind == VanillaLoadCompatibilityKind.VanillaCompatible)
                return;

            ReportWarning(FormatLoadWarning(overEditorLimit, loadKind, decorations, blueprintUsage));
        }

        internal static bool TryFindOverVanillaDecorationLimit(
            DecorationUsageSnapshot decorations,
            out DecorationManagerUsage manager)
        {
            manager = null;
            if (decorations?.Managers == null)
                return false;

            foreach (DecorationManagerUsage current in decorations.Managers)
            {
                if (current != null && current.Count > VanillaDecorationLimit)
                {
                    manager = current;
                    return true;
                }
            }

            return false;
        }

        internal static bool RequiresExtendedFormat(BlueprintSerializationUsage usage) =>
            usage != null &&
            (usage.SentinelContainerCount > 0 || usage.RequiresModBuffer);

        internal static VanillaLoadCompatibilityKind ClassifyVanillaLoadCompatibility(
            DecorationUsageSnapshot decorations,
            BlueprintSerializationUsage usage) =>
            ClassifyVanillaLoadCompatibility(usage);

        internal static VanillaLoadCompatibilityKind ClassifyVanillaLoadCompatibility(
            BlueprintSerializationUsage usage)
        {
            if (usage?.SentinelContainerCount > 0)
                return VanillaLoadCompatibilityKind.SentinelFormat;
            if (usage?.RequiresModBuffer == true)
                return VanillaLoadCompatibilityKind.ModBuffer;
            return VanillaLoadCompatibilityKind.VanillaCompatible;
        }

        internal static VanillaLoadCompatibilityKind ClassifyVanillaLoadCompatibility(
            SerializationForecast forecast)
        {
            if (forecast == null)
                return VanillaLoadCompatibilityKind.VanillaCompatible;
            if (forecast.Format == SerializationWireFormat.Sentinel ||
                forecast.LoadedFormat == SerializationWireFormat.Sentinel ||
                forecast.SavedFormat == SerializationWireFormat.Sentinel ||
                forecast.BlueprintUsage?.SentinelContainerCount > 0)
            {
                return VanillaLoadCompatibilityKind.SentinelFormat;
            }
            if (forecast.BlueprintUsage?.RequiresModBuffer == true)
                return VanillaLoadCompatibilityKind.ModBuffer;
            return VanillaLoadCompatibilityKind.VanillaCompatible;
        }

        internal static VanillaEditorCompatibilityKind ClassifyVanillaEditorCompatibility(
            DecorationUsageSnapshot decorations) =>
            TryFindOverVanillaDecorationLimit(decorations, out _)
                ? VanillaEditorCompatibilityKind.TooManyDecorations
                : VanillaEditorCompatibilityKind.WithinLimit;

        internal static string FormatVanillaLoadStatus(SerializationForecast forecast)
        {
            switch (ClassifyVanillaLoadCompatibility(forecast))
            {
                case VanillaLoadCompatibilityKind.SentinelFormat:
                    return "Vanilla load: ESU required (sentinel)";
                case VanillaLoadCompatibilityKind.ModBuffer:
                    return "Vanilla load: ESU required (buffer)";
                default:
                    return "Vanilla load: OK";
            }
        }

        internal static Color VanillaLoadStatusColor(
            SerializationForecast forecast,
            Color warning,
            Color error)
        {
            switch (ClassifyVanillaLoadCompatibility(forecast))
            {
                case VanillaLoadCompatibilityKind.SentinelFormat:
                case VanillaLoadCompatibilityKind.ModBuffer:
                    return warning;
                default:
                    return Color.white;
            }
        }

        internal static string FormatVanillaEditorStatus(SerializationForecast forecast)
        {
            if (forecast != null &&
                TryFindOverVanillaDecorationLimit(
                    forecast.Decorations,
                    out DecorationManagerUsage manager))
            {
                return "Vanilla edit limit: over 5k (" +
                       FormatCount(manager.Count) +
                       "/" +
                       FormatCount(VanillaDecorationLimit) +
                       ")";
            }

            return "Vanilla edit limit: OK";
        }

        internal static Color VanillaEditorStatusColor(
            SerializationForecast forecast,
            Color warning,
            Color error)
        {
            if (forecast != null &&
                TryFindOverVanillaDecorationLimit(forecast.Decorations, out _))
            {
                return Enabled ? error : warning;
            }

            return Color.white;
        }

        private static string FormatSaveBlockedMessage(
            bool overEditorLimit,
            VanillaLoadCompatibilityKind loadKind,
            DecorationUsageSnapshot decorations,
            BlueprintSerializationUsage usage)
        {
            if (loadKind == VanillaLoadCompatibilityKind.SentinelFormat ||
                loadKind == VanillaLoadCompatibilityKind.ModBuffer)
            {
                return SaveExtendedFormatBlockedMessage;
            }

            if (overEditorLimit &&
                TryFindOverVanillaDecorationLimit(decorations, out DecorationManagerUsage manager))
            {
                return "Vanilla compatibility mode blocked saving: this craft is over the vanilla decoration editor limit (" +
                       FormatCount(manager.Count) +
                       "/" +
                       FormatCount(VanillaDecorationLimit) +
                       "). Vanilla can load legacy-wire craft above this cap, but vanilla decoration tools are limited. Reduce decorations or disable the compatibility mode.";
            }

            return "Vanilla compatibility mode blocked saving: this craft is not vanilla-compatible.";
        }

        private static string FormatSaveWarning(
            bool overEditorLimit,
            VanillaLoadCompatibilityKind loadKind,
            DecorationUsageSnapshot decorations,
            BlueprintSerializationUsage usage)
        {
            string prefix = "Vanilla Compatibility Mode is OFF; save allowed. ";
            if (loadKind == VanillaLoadCompatibilityKind.SentinelFormat)
                return prefix + "Saved as sentinel wire. ESU required to load the extended decoration data.";
            if (loadKind == VanillaLoadCompatibilityKind.ModBuffer)
                return prefix + "Saved with an ESU-sized blueprint stream. ESU required to load the full blueprint data.";

            if (overEditorLimit &&
                TryFindOverVanillaDecorationLimit(decorations, out DecorationManagerUsage manager))
            {
                return prefix +
                       "Saved as " +
                       FormatWireKind(usage) +
                       ". Vanilla can load this wire format, but one decoration manager is over the 5,000 decoration editor cap (" +
                       FormatCount(manager.Count) +
                       "/" +
                       FormatCount(VanillaDecorationLimit) +
                       "). Vanilla decoration tools may be limited; ESU is needed to keep editing above the cap.";
            }

            return prefix + "Saved craft is not fully vanilla-compatible.";
        }

        private static string FormatLoadWarning(
            bool overEditorLimit,
            VanillaLoadCompatibilityKind loadKind,
            DecorationUsageSnapshot decorations,
            BlueprintSerializationUsage usage)
        {
            if (loadKind == VanillaLoadCompatibilityKind.SentinelFormat)
                return "Loaded craft uses ESU sentinel decoration data. ESU required to load the extended decoration data." +
                       FormatExtendedLoadSuffix();
            if (loadKind == VanillaLoadCompatibilityKind.ModBuffer)
                return "Loaded craft uses an ESU-sized blueprint stream. ESU required to load the full blueprint data." +
                       FormatExtendedLoadSuffix();

            if (overEditorLimit &&
                TryFindOverVanillaDecorationLimit(decorations, out DecorationManagerUsage manager))
            {
                return "Loaded craft is " +
                       FormatWireKind(usage) +
                       ". Vanilla can load this wire format, but one decoration manager is over the 5,000 decoration editor cap (" +
                       FormatCount(manager.Count) +
                       "/" +
                       FormatCount(VanillaDecorationLimit) +
                       "). Vanilla decoration tools may be limited." +
                       FormatEditorLimitLoadSuffix();
            }

            return "Loaded craft is not fully vanilla-compatible." +
                   (Enabled
                       ? " Vanilla Compatibility Mode is ON, so saving is blocked until this is fixed or the mode is disabled."
                       : " Vanilla Compatibility Mode is OFF, so ESU can save it.");
        }

        internal static void ReportBlockedAction(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                message = CreationBlockedMessage;
            ReportWarning(message);
        }

        private static void AbortSave(string message)
        {
            ReportWarning(message);
            throw new VanillaCompatibilityException(message);
        }

        private static void ReportWarning(string message)
        {
            try { InfoStore.Add(message); }
            catch { }

            try { EsuRuntimeLog.Warning("Vanilla compatibility", message); }
            catch { }

            try
            {
                AdvLogger.LogWarning(
                    "[EndlessShapes Unlimited] " + message,
                    LogOptions._AlertDevInGame);
            }
            catch { }
        }

        private static string FormatCount(int count) =>
            count.ToString("N0", CultureInfo.InvariantCulture);

        private static string FormatWireKind(BlueprintSerializationUsage usage) =>
            usage?.SentinelContainerCount > 0 ? "sentinel wire" : "legacy wire";

        private static string FormatExtendedLoadSuffix() =>
            Enabled
                ? " Vanilla Compatibility Mode is ON, so saving is blocked until this is fixed or the mode is disabled."
                : " Vanilla Compatibility Mode is OFF, so ESU can save it.";

        private static string FormatEditorLimitLoadSuffix() =>
            Enabled
                ? " Vanilla Compatibility Mode is ON, so saving is blocked until this is reduced to 5,000 decorations or the mode is disabled."
                : " Vanilla Compatibility Mode is OFF, so ESU can keep saving and editing above the vanilla cap.";

        private sealed class SuppressionScope : IDisposable
        {
            private bool _disposed;

            internal SuppressionScope()
            {
                _suppressionDepth++;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _suppressionDepth = Math.Max(0, _suppressionDepth - 1);
            }
        }
    }

    [HarmonyPatch(
        typeof(AllConstructDecorations),
        nameof(AllConstructDecorations.NewDecoration),
        new[] { typeof(Vector3i), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class AllConstructDecorations_NewDecoration_VanillaCompatibility_Patch
    {
        internal static bool Prefix(
            AllConstructDecorations __instance,
            ref Decoration __result)
        {
            if (VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    __instance,
                    1,
                    out string message))
            {
                return true;
            }

            __result = null;
            VanillaCompatibilityGuard.ReportBlockedAction(message);
            return false;
        }
    }
}
