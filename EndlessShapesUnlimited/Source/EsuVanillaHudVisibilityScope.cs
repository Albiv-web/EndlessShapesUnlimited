using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ui.Displayer;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuVanillaHudVisibilityScope
    {
        private const float LogCooldownSeconds = 6f;

        private static bool _captured;
        private static bool _previousDisplayGuis = true;
        private static float _nextLogTime;
        private static string _lastBeginContext = "(none)";
        private static string _lastTickContext = "(none)";
        private static string _lastEndContext = "(none)";
        private static string _lastForceContext = "(none)";
        private static string _lastRestoreContext = "(none)";

        internal static bool Captured => _captured;

        internal static bool PreviousDisplayGuis => _previousDisplayGuis;

        internal static bool CurrentDisplayGuis
        {
            get
            {
                try
                {
                    return GuiDisplayBase.displayGUIs;
                }
                catch
                {
                    return true;
                }
            }
        }

        internal static string Status =>
            "vanilla_gui_hidden_by_esu=" + (_captured ? "true" : "false") +
            "\nprevious_display_guis=" + (_previousDisplayGuis ? "true" : "false") +
            "\ncurrent_display_guis=" + (CurrentDisplayGuis ? "true" : "false") +
            "\nlast_hud_begin_context=" + _lastBeginContext +
            "\nlast_hud_tick_context=" + _lastTickContext +
            "\nlast_hud_end_context=" + _lastEndContext +
            "\nlast_hud_force_context=" + _lastForceContext +
            "\nlast_hud_restore_context=" + _lastRestoreContext;

        internal static void Begin(string context)
        {
            _lastBeginContext = SafeContext(context);
            if (!_captured)
            {
                _previousDisplayGuis = CurrentDisplayGuis;
                _captured = true;
                LogInfo(
                    "Vanilla HUD display flag captured for ESU editor.",
                    "context=" + (context ?? "unknown") +
                    "\nprevious_display_guis=" + (_previousDisplayGuis ? "true" : "false"));
            }

            ForceHidden(context);
        }

        internal static void Tick(string context)
        {
            _lastTickContext = SafeContext(context);
            if (EsuEditorScope.ShouldHideVanillaHud)
            {
                Begin(context);
                return;
            }

            Restore(context);
        }

        internal static void End(string context)
        {
            _lastEndContext = SafeContext(context);
            if (EsuEditorScope.ShouldHideVanillaHud)
            {
                ForceHidden(context);
                return;
            }

            Restore(context);
        }

        internal static void Restore(string context)
        {
            if (!_captured)
                return;

            _lastRestoreContext = SafeContext(context);
            bool restoreTo = _previousDisplayGuis;
            _captured = false;
            try
            {
                GuiDisplayBase.displayGUIs = restoreTo;
            }
            catch
            {
                // If the vanilla GUI type is not ready, the next tick can try again.
                _captured = true;
                return;
            }

            LogInfo(
                "Vanilla HUD display flag restored after ESU editor.",
                "context=" + (context ?? "unknown") +
                "\nrestored_display_guis=" + (restoreTo ? "true" : "false"));
        }

        private static void ForceHidden(string context)
        {
            _lastForceContext = SafeContext(context);
            try
            {
                if (GuiDisplayBase.displayGUIs)
                    GuiDisplayBase.displayGUIs = false;
            }
            catch
            {
                return;
            }

            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            float now = Time.unscaledTime;
            if (now < _nextLogTime)
                return;

            _nextLogTime = now + LogCooldownSeconds;
            EsuRuntimeLog.Info(
                "HUD diagnostics",
                "Vanilla HUD display flag is forced off while ESU editor is active.",
                "context=" + (context ?? "unknown") +
                "\neditor=" + EsuEditorScope.CurrentEditorName +
                "\n" + Status);
        }

        private static void LogInfo(string message, string detail)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            try
            {
                EsuRuntimeLog.Info("HUD diagnostics", message, detail);
                AdvLogger.LogInfo("[EndlessShapes Unlimited] " + message + " " + detail);
            }
            catch
            {
                // HUD diagnostics must never affect editor lifetime.
            }
        }

        private static string SafeContext(string context) =>
            string.IsNullOrWhiteSpace(context)
                ? "unknown"
                : context.Trim();
    }

    [HarmonyPatch(typeof(GuiDisplayer), nameof(GuiDisplayer.LateUpdate))]
    internal static class EsuVanillaHudVisibilityScope_GuiDisplayer_LateUpdate_Patch
    {
        private static void Postfix() =>
            EsuVanillaHudVisibilityScope.Tick("GuiDisplayer LateUpdate");
    }
}
