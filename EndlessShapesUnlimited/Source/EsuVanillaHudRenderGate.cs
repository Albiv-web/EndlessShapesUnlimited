using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Logger;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuVanillaHudRenderGate
    {
        private static readonly object GateLock = new object();
        private static readonly string[] CoreTargetNames =
        {
            "BrilliantSkies.Ui.Displayer.GuiDisplayBase.OnGUI",
            "BrilliantSkies.Ftd.Avatar.HUD.cHud.OnGUI",
            "BrilliantSkies.Ftd.Avatar.HUD.cHud.OnGuiInternal",
            "BrilliantSkies.Ftd.Avatar.HUD.cHud.DrawFleets",
            "BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands.DrawBuildModeCommands",
            "BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands.DisplayMessage",
            "BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands.TopRightOptions",
            "BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands.TopRightOptionsList",
            "BrilliantSkies.Ftd.Avatar.HUD.DrawButtons.DrawInsideBuildMode",
            "BrilliantSkies.Ftd.Avatar.HUD.DrawPlayerData.Draw",
            "BrilliantSkies.Ftd.Avatar.HUD.HudVehicleInfo.DrawVehicleInfo",
            "BrilliantSkies.Ftd.Avatar.HUD.DrawExtraVehicleInfo.DrawRHSTextDisplay",
            "BrilliantSkies.Ftd.Avatar.HUD.DrawTeamData.DrawResource",
            "BrilliantSkies.Ftd.Avatar.HUD.HudCameraControl.Draw"
        };
        private static readonly List<string> InstalledTargets = new List<string>();
        private static readonly List<string> MissingTargets = new List<string>();
        private static readonly Dictionary<string, int> SuppressionCounts = new Dictionary<string, int>();
        private static readonly HashSet<string> InstalledTargetKeys = new HashSet<string>(StringComparer.Ordinal);

        private static bool _installAttempted;
        private static int _totalSuppressions;

        internal static string Status
        {
            get
            {
                lock (GateLock)
                {
                    return
                        "render_gate_install_attempted=" + (_installAttempted ? "true" : "false") +
                        "\nrender_gate_targets_installed=" + InstalledTargets.Count.ToString() +
                        "\nrender_gate_targets_missing=" + MissingTargets.Count.ToString() +
                        "\nrender_gate_core_targets_installed=" + InstalledCoreTargetCount().ToString() + "/" + CoreTargetNames.Length.ToString() +
                        "\nrender_gate_core_targets_missing=" + FormatMissingCoreTargets() +
                        "\nrender_gate_targets_missing_list=" + FormatMissingTargets() +
                        "\nrender_gate_suppressions=" + _totalSuppressions.ToString() +
                        "\nrender_gate_recent=" + FormatRecentSuppressionCounts();
                }
            }
        }

        internal static MethodBase[] ResolveCoreTargets()
        {
            return new[]
            {
                ResolveSingleTarget("BrilliantSkies.Ui.Displayer.GuiDisplayBase", "OnGUI"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.cHud", "OnGUI"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.cHud", "OnGuiInternal"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.cHud", "DrawFleets"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands", "DrawBuildModeCommands"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands", "DisplayMessage"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands", "TopRightOptions"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands", "TopRightOptionsList"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.DrawButtons", "DrawInsideBuildMode"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.DrawPlayerData", "Draw"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudVehicleInfo", "DrawVehicleInfo"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.DrawExtraVehicleInfo", "DrawRHSTextDisplay"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.DrawTeamData", "DrawResource"),
                ResolveSingleTarget("BrilliantSkies.Ftd.Avatar.HUD.HudCameraControl", "Draw")
            };
        }

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
                return;

            lock (GateLock)
            {
                if (_installAttempted)
                    return;

                _installAttempted = true;
            }

            var seen = new HashSet<MethodBase>();
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ui.Displayer.GuiDisplayBase",
                "OnGUI",
                "CheckForToolTip");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.cHud",
                "OnGUI",
                "OnGuiInternal",
                "DrawFleets");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.HudBuildCommands",
                "DrawBuildModeCommands",
                "DisplayMessage",
                "TopRightOptions",
                "TopRightOptionsList");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.DrawButtons",
                "DrawInsideBuildMode");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.DrawPlayerData",
                "Draw");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.HudVehicleInfo",
                "DrawVehicleInfo");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.DrawExtraVehicleInfo",
                "DrawRHSTextDisplay");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.DrawTeamData",
                "DrawResource");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.HudCameraControl",
                "Draw");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ui.Tips.TipDisplayer",
                "TooltipGUI",
                "RenderPhase",
                "DoMyWindow");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Common.Controls.Hud.HudDisplay",
                "DrawTips");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.ToolBars",
                "DisplayBlockToolbar",
                "DisplayToolToolBar",
                "DisplayCharacterItemToolBar",
                "DisplayToolModeShortcut");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ftd.Avatar.HUD.HudHints.HudHintDrawer",
                "DrawHintsIfNeeded");
            PatchNamedMethods(
                harmony,
                seen,
                "BrilliantSkies.Ui.Special.InfoStore.InfoStore",
                "DisplayInfo");

            LogInstallSummary();
        }

        private static bool Prefix(MethodBase __originalMethod)
        {
            if (!EsuEditorScope.ShouldHideVanillaHud)
                return true;

            RecordSuppression(__originalMethod);
            return false;
        }

        private static void PatchNamedMethods(
            Harmony harmony,
            ISet<MethodBase> seen,
            string typeName,
            params string[] methodNames)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                RecordMissing(typeName + ".<type>");
                return;
            }

            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;
            MethodInfo prefix = AccessTools.Method(typeof(EsuVanillaHudRenderGate), nameof(Prefix));
            foreach (string methodName in methodNames)
            {
                MethodInfo[] methods = type.GetMethods(flags)
                    .Where(method => method.Name == methodName && !method.ContainsGenericParameters)
                    .ToArray();
                if (methods.Length == 0)
                {
                    RecordMissing(type.FullName + "." + methodName);
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (!seen.Add(method))
                        continue;

                    try
                    {
                        if (Harmony.GetPatchInfo(method)?.Prefixes?.Any(patch =>
                                patch.owner == harmony.Id &&
                                patch.PatchMethod == prefix) != true)
                        {
                            harmony.Patch(
                                method,
                                prefix: new HarmonyMethod(prefix)
                                {
                                    priority = Priority.First
                                });
                        }

                        RecordInstalled(FormatTargetKey(type.FullName, method.Name), FormatMethod(method));
                    }
                    catch (Exception exception)
                    {
                        RecordMissing(FormatMethod(method) + " (" + exception.GetType().Name + ")");
                    }
                }
            }
        }

        private static void RecordSuppression(MethodBase method)
        {
            lock (GateLock)
            {
                _totalSuppressions++;
                string key = method == null
                    ? "unknown"
                    : method.DeclaringType?.Name + "." + method.Name;
                SuppressionCounts.TryGetValue(key, out int count);
                SuppressionCounts[key] = count + 1;
            }
        }

        private static MethodBase ResolveSingleTarget(string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            return type == null
                ? null
                : AccessTools.Method(type, methodName);
        }

        private static void RecordInstalled(string targetKey, string target)
        {
            lock (GateLock)
            {
                InstalledTargetKeys.Add(targetKey);
                if (!InstalledTargets.Contains(target))
                    InstalledTargets.Add(target);
            }
        }

        private static void RecordMissing(string target)
        {
            lock (GateLock)
            {
                MissingTargets.Add(target);
            }
        }

        private static int InstalledCoreTargetCount()
        {
            int count = 0;
            for (int index = 0; index < CoreTargetNames.Length; index++)
            {
                if (InstalledTargetKeys.Contains(CoreTargetNames[index]))
                    count++;
            }

            return count;
        }

        private static string FormatMissingCoreTargets()
        {
            string[] missing = CoreTargetNames
                .Where(target => !InstalledTargetKeys.Contains(target))
                .ToArray();
            return missing.Length == 0
                ? "(none)"
                : string.Join(", ", missing);
        }

        private static string FormatMissingTargets()
        {
            if (MissingTargets.Count == 0)
                return "(none)";

            string[] missing = MissingTargets.Take(8).ToArray();
            string suffix = MissingTargets.Count > missing.Length
                ? ", +" + (MissingTargets.Count - missing.Length).ToString() + " more"
                : string.Empty;
            return string.Join(", ", missing) + suffix;
        }

        private static string FormatRecentSuppressionCounts()
        {
            if (SuppressionCounts.Count == 0)
                return "(none)";

            return string.Join(
                ", ",
                SuppressionCounts
                    .OrderByDescending(pair => pair.Value)
                    .Take(5)
                    .Select(pair => pair.Key + "=" + pair.Value.ToString())
                    .ToArray());
        }

        private static string FormatTargetKey(string typeName, string methodName) =>
            (typeName ?? "unknown") + "." + (methodName ?? "unknown");

        private static string FormatMethod(MethodInfo method)
        {
            string parameters = string.Join(
                ", ",
                method.GetParameters()
                    .Select(parameter => parameter.ParameterType.Name)
                    .ToArray());
            return method.DeclaringType?.FullName + "." + method.Name + "(" + parameters + ")";
        }

        private static void LogInstallSummary()
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            string detail;
            lock (GateLock)
            {
                detail =
                    "installed:\n- " + string.Join("\n- ", InstalledTargets.ToArray()) +
                    "\nmissing:\n- " + (MissingTargets.Count == 0
                        ? "(none)"
                        : string.Join("\n- ", MissingTargets.ToArray()));
            }

            try
            {
                EsuRuntimeLog.Info(
                    "HUD diagnostics",
                    "Scoped vanilla HUD render gates installed.",
                    detail);
            }
            catch
            {
                // Runtime log is optional during plugin startup.
            }

            try
            {
                AdvLogger.LogInfo(
                    "[EndlessShapes Unlimited] Scoped vanilla HUD render gates installed. " +
                    detail);
            }
            catch
            {
                // Optional diagnostics must never affect startup.
            }
        }
    }
}
