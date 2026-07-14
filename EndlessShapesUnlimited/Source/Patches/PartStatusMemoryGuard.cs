using System;
using System.Runtime.CompilerServices;
using BrilliantSkies.Common.StatusChecking;
using DecoLimitLifter.SerializationHud;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    internal static class PartStatusMemoryGuard
    {
        internal const int MaximumInitialCapacity = 4096;

        private static readonly ConditionalWeakTable<StatusUpdate, EnabledMarker>
            OptimizedUpdates = new ConditionalWeakTable<StatusUpdate, EnabledMarker>();

        private sealed class EnabledMarker
        {
        }

        internal static bool PrepareUpdate(ref int count)
        {
            bool enabled = Enabled;
            if (enabled && count > MaximumInitialCapacity)
                count = MaximumInitialCapacity;
            return enabled;
        }

        internal static void MarkUpdate(StatusUpdate update, bool enabled)
        {
            if (!enabled || update == null)
                return;
            OptimizedUpdates.Add(update, new EnabledMarker());
        }

        internal static bool ShouldElideAlreadyClearOkay(
            StatusUpdate update,
            IFlagState focusedPart,
            ShaderFlagCollection focusedFlag)
        {
            return update != null &&
                   OptimizedUpdates.TryGetValue(update, out _) &&
                   focusedPart != null &&
                   focusedFlag == 0 &&
                   focusedPart.GetFlag() == 0;
        }

        internal static int InitialCapacityForVerification(int count, bool enabled) =>
            enabled && count > MaximumInitialCapacity
                ? MaximumInitialCapacity
                : count;

        internal static bool ShouldElideForVerification(
            bool enabled,
            bool hasFocusedPart,
            ShaderFlagCollection focusedFlag,
            ShaderFlagCollection currentFlag) =>
            enabled && hasFocusedPart && focusedFlag == 0 && currentFlag == 0;

        private static bool Enabled
        {
            get
            {
                try
                {
                    return SerializationHudProfile.Data.MemorySafePartStatusChecks;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StatusUpdate), MethodType.Constructor, typeof(int))]
    internal static class StatusUpdate_Constructor_PartStatusMemoryGuard_Patch
    {
        internal static void Prefix(
            [HarmonyArgument(0)] ref int count,
            out bool __state) =>
            __state = PartStatusMemoryGuard.PrepareUpdate(ref count);

        internal static void Postfix(StatusUpdate __instance, bool __state) =>
            PartStatusMemoryGuard.MarkUpdate(__instance, __state);
    }

    [HarmonyPatch(typeof(StatusUpdate), "FlushChange")]
    internal static class StatusUpdate_FlushChange_PartStatusMemoryGuard_Patch
    {
        internal static bool Prefix(
            StatusUpdate __instance,
            ref IFlagState ____focusedPart,
            ref ShaderFlagCollection ____focusedFlag,
            ref string ____focusedDescription)
        {
            if (!PartStatusMemoryGuard.ShouldElideAlreadyClearOkay(
                    __instance,
                    ____focusedPart,
                    ____focusedFlag))
            {
                return true;
            }

            ____focusedPart = null;
            ____focusedFlag = 0;
            ____focusedDescription = string.Empty;
            return false;
        }
    }
}
