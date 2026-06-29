using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.HUD;
using BrilliantSkies.Ftd.Cameras;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ui.Displayer;
using DecoLimitLifter.SmartBuildMode;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    /// <summary>
    /// Central ownership flag for the modal decoration editor.
    /// Harmony patches deliberately keep this small: while the ESU editor is
    /// active FTD stays in build mode, but the vanilla build HUD and build
    /// input are not allowed to consume clicks/scroll intended for ESU.
    /// </summary>
    internal static class DecorationEditorInputScope
    {
        private static bool _active;
        private static bool _mouseOverEditorUi;
        private static int _buildInputClaimUntilFrame = -1;
        private static int _cameraInputClaimUntilFrame = -1;

        internal static bool Active => _active;

        internal static bool MouseOverEditorUi => _active && _mouseOverEditorUi;

        internal static bool OwnsBuildInputThisFrame =>
            _active && Time.frameCount <= _buildInputClaimUntilFrame;

        internal static bool OwnsCameraInputThisFrame =>
            _active && Time.frameCount <= _cameraInputClaimUntilFrame;

        internal static bool ScrollWheelOverEditorUi =>
            MouseOverEditorUi &&
            Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f;

        internal static bool ControlHeldWhileActive =>
            _active && DecoLimitLifter.EsuInputState.IsControlHeld();

        internal static void Begin()
        {
            _active = true;
            DecoLimitLifter.EsuInputFocusGuard.BeginEditor("Decoration Edit Mode");
            _mouseOverEditorUi = false;
            _buildInputClaimUntilFrame = -1;
            _cameraInputClaimUntilFrame = -1;
            DecorationTooltipSuppressor.ClearActiveTooltipState(force: true);
        }

        internal static void End()
        {
            bool wasActive = _active;
            if (_active)
                DecorationTooltipSuppressor.ClearActiveTooltipState(force: true);
            _active = false;
            _mouseOverEditorUi = false;
            _buildInputClaimUntilFrame = -1;
            _cameraInputClaimUntilFrame = -1;
            if (wasActive)
                DecoLimitLifter.EsuInputFocusGuard.EndEditor("Decoration Edit Mode");
        }

        internal static void ForceResetIfActive(string reason)
        {
            if (!_active &&
                !_mouseOverEditorUi &&
                _buildInputClaimUntilFrame < 0 &&
                _cameraInputClaimUntilFrame < 0)
            {
                return;
            }

            End();
            try
            {
                AdvLogger.LogWarning(
                    "[EndlessShapes Unlimited] Decoration Edit Mode input scope was force-reset: " +
                    reason,
                    LogOptions._AlertDevInGame);
            }
            catch
            {
                // Input recovery must not depend on logging.
            }
        }

        internal static void SetMouseOverEditorUi(bool value) =>
            _mouseOverEditorUi = _active && value;

        internal static void ClaimBuildInputForFrames(int frames = 2)
        {
            if (!_active)
                return;

            int until = Time.frameCount + Mathf.Max(1, frames);
            if (until > _buildInputClaimUntilFrame)
                _buildInputClaimUntilFrame = until;
        }

        internal static void ClaimCameraInputForFrames(int frames = 2)
        {
            if (!_active)
                return;

            int until = Time.frameCount + Mathf.Max(1, frames);
            if (until > _cameraInputClaimUntilFrame)
                _cameraInputClaimUntilFrame = until;
        }

        internal static void ClaimMouseWheelInputForFrames(int frames = 2)
        {
            if (!_active)
                return;

            ClaimBuildInputForFrames(frames);
            ClaimCameraInputForFrames(frames);
            try
            {
                GuiDisplayBase.MouseWheelInUse.Now();
            }
            catch
            {
                // Camera suppression still works through the build/camera claims.
            }
        }

        internal static bool SuppressBuildHud() =>
            _active || SmartBuildInputScope.SuppressBuildHud();

        internal static bool SuppressBuildInput() =>
            ControlHeldWhileActive ||
            OwnsBuildInputThisFrame ||
            ScrollWheelOverEditorUi ||
            SmartBuildInputScope.SuppressBuildInput();

        internal static bool SuppressCameraInput() =>
            OwnsCameraInputThisFrame ||
            ScrollWheelOverEditorUi ||
            SmartBuildInputScope.SuppressCameraInput();

        internal static bool IsPaintHoverMessage(object[] arguments)
        {
            return DecorationTooltipSuppressor.IsLegacyPaintHoverMessage(arguments);
        }

        internal static bool IsMouseOver(params Rect[] rects)
        {
            if (!_active || rects == null || rects.Length == 0)
                return false;

            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            for (int index = 0; index < rects.Length; index++)
            {
                if (rects[index].Contains(mouse))
                    return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(HudBuildCommands), nameof(HudBuildCommands.DrawBuildModeCommands))]
    internal static class DecorationEditor_HudBuildCommands_DrawBuildModeCommands_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(HudBuildCommands), "TopRightOptions")]
    internal static class DecorationEditor_HudBuildCommands_TopRightOptions_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(HudBuildCommands), "TopRightOptionsList")]
    internal static class DecorationEditor_HudBuildCommands_TopRightOptionsList_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(DrawButtons), nameof(DrawButtons.DrawInsideBuildMode))]
    internal static class DecorationEditor_DrawButtons_DrawInsideBuildMode_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "DrawBuildModeCommands")]
    internal static class DecorationEditor_cHud_DrawBuildModeCommands_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "ShowMouseFunctions")]
    internal static class DecorationEditor_cHud_ShowMouseFunctions_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "DrawRhs")]
    internal static class DecorationEditor_cHud_DrawRhs_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "DrawWeaponInfo")]
    internal static class DecorationEditor_cHud_DrawWeaponInfo_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "DrawInteractionIcon")]
    internal static class DecorationEditor_cHud_DrawInteractionIcon_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cHud), "DisplayCorrectToolBar")]
    internal static class DecorationEditor_cHud_DisplayCorrectToolBar_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildHud();
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.RunUpdate))]
    internal static class DecorationEditor_cBuild_RunUpdate_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressBuildInput();
    }

    [HarmonyPatch(typeof(BuildCameraMode), nameof(BuildCameraMode.RunUpdate))]
    internal static class DecorationEditor_BuildCameraMode_RunUpdate_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressCameraInput();
    }

    [HarmonyPatch]
    internal static class DecorationEditor_InfoStore_Add_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            TypeInfo type = AccessTools.TypeByName("BrilliantSkies.Ui.Special.InfoStore.InfoStore")
                ?.GetTypeInfo();
            if (type == null)
                yield break;

            foreach (MethodInfo method in type
                         .DeclaredMethods
                         .Where(method => method.Name == "Add"))
            {
                yield return method;
            }
        }

        private static bool Prefix(object[] __args)
        {
            if (DecorationEditorInputScope.IsPaintHoverMessage(__args))
                return false;

            return !EsuHudNotifications.TryCaptureInfoStore(__args);
        }
    }
}
