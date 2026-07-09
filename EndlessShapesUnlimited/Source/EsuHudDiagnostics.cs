using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuHudDiagnostics
    {
        private const float InfoStoreSampleLogCooldownSeconds = 15f;
        private const float StaleScopeLogCooldownSeconds = 8f;
        private const float ZoomLeakLogCooldownSeconds = 2f;

        private static float _nextInfoStoreSampleLogTime;
        private static float _nextStaleScopeLogTime;
        private static float _nextZoomLeakLogTime;
        private static int _wheelOverUiGateCount;
        private static int _hybridZoomInputIgnoredCount;
        private static int _getZoomSeenCount;
        private static int _getZoomBlockedCount;
        private static int _hybridZoomBlockedCount;
        private static int _zoomLeakCandidateCount;
        private static int _lastWheelOverUiGateFrame = -1;
        private static int _lastHybridZoomInputIgnoredFrame = -1;
        private static int _lastHybridZoomBlockedFrame = -1;
        private static int _lastZoomTraceFrame = -1;
        private static string _lastZoomTrace = "none";

        internal static string WheelZoomGateStatus =>
            "wheel_over_esu_ui_gate_count=" + _wheelOverUiGateCount.ToString() +
            "\nhybrid_zoom_input_ignored_count=" + _hybridZoomInputIgnoredCount.ToString() +
            "\nget_zoom_seen=" + _getZoomSeenCount.ToString() +
            "\nget_zoom_blocked=" + _getZoomBlockedCount.ToString() +
            "\nhybrid_zoom_blocked=" + _hybridZoomBlockedCount.ToString() +
            "\nzoom_leak_candidate=" + _zoomLeakCandidateCount.ToString() +
            "\nlast_wheel_over_esu_ui_gate_frame=" + _lastWheelOverUiGateFrame.ToString() +
            "\nlast_hybrid_zoom_input_ignored_frame=" + _lastHybridZoomInputIgnoredFrame.ToString() +
            "\nlast_hybrid_zoom_blocked_frame=" + _lastHybridZoomBlockedFrame.ToString() +
            "\nlast_zoom_trace_frame=" + _lastZoomTraceFrame.ToString() +
            "\nlast_zoom_trace=" + _lastZoomTrace;

        internal static void LogGateStatus(string context)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            EsuRuntimeLog.Info(
                "HUD diagnostics",
                "Current scoped vanilla HUD visibility status.",
                "context=" + (context ?? "unknown") +
                "\n" + EsuEditorScope.Status +
                "\n" + EsuVanillaHudVisibilityScope.Status +
                "\n" + EsuVanillaHudRenderGate.Status +
                "\n" + WheelZoomGateStatus +
                "\n" + RuntimeAssemblyStatus());
        }

        internal static void RecordEsuWheelOverUiGate()
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            if (_lastWheelOverUiGateFrame == Time.frameCount)
                return;

            _lastWheelOverUiGateFrame = Time.frameCount;
            _wheelOverUiGateCount++;
        }

        internal static void RecordHybridZoomInputIgnored()
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            if (_lastHybridZoomInputIgnoredFrame == Time.frameCount)
                return;

            _lastHybridZoomInputIgnoredFrame = Time.frameCount;
            _hybridZoomInputIgnoredCount++;
        }

        internal static void RecordGetZoomSeen(
            float rawZoom,
            EsuPanelUiHit hit,
            bool mouseWheelInUseThisFrame)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            _getZoomSeenCount++;
            UpdateZoomTrace("get_zoom_seen", rawZoom, hit, mouseWheelInUseThisFrame);
        }

        internal static void RecordGetZoomBlocked(
            float rawZoom,
            EsuPanelUiHit hit,
            bool mouseWheelInUseThisFrame)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            _getZoomBlockedCount++;
            UpdateZoomTrace("get_zoom_blocked", rawZoom, hit, mouseWheelInUseThisFrame);
        }

        internal static void RecordHybridZoomBlocked(EsuPanelUiHit hit)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            if (_lastHybridZoomBlockedFrame == Time.frameCount)
                return;

            _lastHybridZoomBlockedFrame = Time.frameCount;
            _hybridZoomBlockedCount++;
            UpdateZoomTrace(
                "hybrid_zoom_blocked",
                0f,
                hit,
                MouseWheelInUseThisFrameFallback());
        }

        internal static void RecordHybridZoomAllowed(EsuPanelUiHit hit)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled ||
                !EsuEditorScope.AnyEditorActive)
            {
                return;
            }

            UpdateZoomTrace(
                "hybrid_zoom_allowed_hit_false",
                0f,
                hit,
                MouseWheelInUseThisFrameFallback());
        }

        internal static void RecordZoomLeakCandidate(
            float rawZoom,
            EsuPanelUiHit hit,
            bool mouseWheelInUseThisFrame)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled ||
                !EsuEditorScope.AnyEditorActive)
            {
                return;
            }

            _zoomLeakCandidateCount++;
            UpdateZoomTrace("zoom_leak_candidate", rawZoom, hit, mouseWheelInUseThisFrame);

            float now = Time.unscaledTime;
            if (now < _nextZoomLeakLogTime)
                return;

            _nextZoomLeakLogTime = now + ZoomLeakLogCooldownSeconds;
            EsuRuntimeLog.Warning(
                "HUD diagnostics",
                "FtD zoom input was nonzero while an ESU editor was active, but the live ESU UI hit-test missed.",
                _lastZoomTrace);
        }

        internal static void RecordInfoStoreCaptured(string message)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

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

        private static void UpdateZoomTrace(
            string source,
            float rawZoom,
            EsuPanelUiHit hit,
            bool mouseWheelInUseThisFrame)
        {
            _lastZoomTraceFrame = Time.frameCount;
            _lastZoomTrace =
                "source=" + (source ?? "unknown") +
                "\neditor_active=" + (EsuEditorScope.AnyEditorActive ? "true" : "false") +
                "\ncurrent_editor=" + EsuEditorScope.CurrentEditorName +
                "\nraw_zoom_delta=" + rawZoom.ToString("0.########", CultureInfo.InvariantCulture) +
                "\nmouse_wheel_in_use_this_frame=" + (mouseWheelInUseThisFrame ? "true" : "false") +
                "\n" + hit.ToDiagnosticString() +
                "\nassembly_location=" + AssemblyLocation();
        }

        private static bool MouseWheelInUseThisFrameFallback()
        {
            try
            {
                return BrilliantSkies.Ui.Displayer.GuiDisplayBase.MouseWheelInUseThisFrame;
            }
            catch
            {
                return false;
            }
        }

        private static string AssemblyLocation()
        {
            try
            {
                return typeof(EsuHudDiagnostics).Assembly.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static void WarnIfEditorScopeActive(
            string context,
            string editorName,
            Func<bool> isEditorScopeActive)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

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

        private static string RuntimeAssemblyStatus()
        {
            try
            {
                Assembly assembly = typeof(EsuHudDiagnostics).Assembly;
                string location = assembly.Location ?? string.Empty;
                string root = FindPackageRoot(location);
                string[] dlls = string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)
                    ? Array.Empty<string>()
                    : Directory.GetFiles(root, "EndlessShapesUnlimited.dll", SearchOption.AllDirectories);
                return
                    "assembly_location=" + location +
                    "\nassembly_version=" + assembly.GetName().Version +
                    "\nassembly_package_root=" + (root ?? "(unknown)") +
                    "\nduplicate_esu_dll_count=" + dlls.Length.ToString() +
                    "\nduplicate_esu_dll_warning=" + (dlls.Length > 1 ? "true" : "false") +
                    "\nduplicate_esu_dlls=" + FormatDuplicateDlls(root, dlls);
            }
            catch (Exception exception)
            {
                return "assembly_diagnostics_error=" + exception.GetType().Name;
            }
        }

        private static string FindPackageRoot(string assemblyLocation)
        {
            if (string.IsNullOrWhiteSpace(assemblyLocation))
                return null;

            DirectoryInfo current = new FileInfo(assemblyLocation).Directory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "plugin.json")))
                    return current.FullName;

                current = current.Parent;
            }

            return Path.GetDirectoryName(assemblyLocation);
        }

        private static string FormatDuplicateDlls(string root, string[] dlls)
        {
            if (dlls == null || dlls.Length == 0)
                return "(none)";

            string[] display = dlls
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(path => FormatRelativePath(root, path))
                .ToArray();
            string suffix = dlls.Length > display.Length
                ? ", +" + (dlls.Length - display.Length).ToString() + " more"
                : string.Empty;
            return string.Join(", ", display) + suffix;
        }

        private static string FormatRelativePath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                return path ?? string.Empty;

            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : path;
        }
    }
}
