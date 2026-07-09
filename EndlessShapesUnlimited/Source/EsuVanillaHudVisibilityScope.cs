using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ui.Displayer;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuVanillaHudVisibilityScope
    {
        private const float LogCooldownSeconds = 6f;

        private static bool _captured;
        private static bool _previousDisplayGuis = true;
        private static float _nextLogTime;

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
            "\ncurrent_display_guis=" + (CurrentDisplayGuis ? "true" : "false");

        internal static void Begin(string context)
        {
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
            if (EsuEditorScope.ShouldHideVanillaHud)
            {
                Begin(context);
                return;
            }

            Restore(context);
        }

        internal static void End(string context)
        {
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
            try
            {
                if (GuiDisplayBase.displayGUIs)
                    GuiDisplayBase.displayGUIs = false;
            }
            catch
            {
                return;
            }

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
    }
}
