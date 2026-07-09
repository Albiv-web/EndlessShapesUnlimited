using System;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuHudDiagnostics
    {
        private const float InfoStoreSampleLogCooldownSeconds = 15f;
        private const float StaleScopeLogCooldownSeconds = 8f;

        private static float _nextInfoStoreSampleLogTime;
        private static float _nextStaleScopeLogTime;

        internal static void LogGateStatus(string context)
        {
            EsuRuntimeLog.Info(
                "HUD diagnostics",
                "Current scoped vanilla HUD visibility status.",
                "context=" + (context ?? "unknown") +
                "\neditor=" + EsuEditorScope.CurrentEditorName +
                "\n" + EsuVanillaHudVisibilityScope.Status);
        }

        internal static void RecordInfoStoreCaptured(string message)
        {
            if (!EsuEditorScope.AnyEditorActive)
                return;

            float now = Time.unscaledTime;
            if (now < _nextInfoStoreSampleLogTime)
                return;

            _nextInfoStoreSampleLogTime = now + InfoStoreSampleLogCooldownSeconds;
            EsuRuntimeLog.Info(
                "HUD diagnostics",
                "InfoStore message captured while ESU editor is active.",
                "editor=" + EsuEditorScope.CurrentEditorName +
                "\nmessage=" + (message ?? string.Empty).Trim());
        }

        internal static void WarnIfEditorScopeActive(
            string context,
            string editorName,
            Func<bool> isEditorScopeActive)
        {
            if (isEditorScopeActive == null || !isEditorScopeActive())
                return;

            float now = Time.unscaledTime;
            if (now < _nextStaleScopeLogTime)
                return;

            _nextStaleScopeLogTime = now + StaleScopeLogCooldownSeconds;
            EsuRuntimeLog.Warning(
                "HUD diagnostics",
                "An ESU input scope is still active after an editor close path.",
                "context=" + (context ?? "unknown") +
                "\nclosed_editor=" + (editorName ?? "unknown") +
                "\nactive_editor=" + EsuEditorScope.CurrentEditorName);
        }
    }
}
