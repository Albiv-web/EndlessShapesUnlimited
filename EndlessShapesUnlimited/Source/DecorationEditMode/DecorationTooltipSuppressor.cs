using System;
using BrilliantSkies.Core.Logger;
using HarmonyLib;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class DecorationTooltipSuppressor
    {
        private static bool _loggedDisabled;

        internal static void Install(Harmony harmony)
        {
            LogDisabledForSafety();
        }

        internal static void LogDisabledForSafety()
        {
            if (_loggedDisabled)
                return;

            _loggedDisabled = true;
            try
            {
                AdvLogger.LogInfo(
                    "[EndlessShapes Unlimited] Vanilla tooltip suppression is disabled for menu safety.");
            }
            catch
            {
                // Diagnostics must not block startup.
            }
        }

        internal static bool IsLegacyPaintHoverMessage(object[] arguments) => false;

        internal static void ClearActiveTooltipState(bool force = false)
        {
            // Deliberately no-op. Do not mutate GUI.tooltip, TipDisplayer, or any
            // private FtD tooltip state; doing so can break vanilla menu hovers.
        }
    }
}
