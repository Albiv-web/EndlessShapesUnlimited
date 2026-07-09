using System;
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

        private static float _nextInfoStoreSampleLogTime;
        private static float _nextStaleScopeLogTime;

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
                "\n" + RuntimeAssemblyStatus());
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
