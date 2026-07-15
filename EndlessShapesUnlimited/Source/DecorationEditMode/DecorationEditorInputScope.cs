using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.HUD;
using BrilliantSkies.Ftd.Avatar.Input;
using BrilliantSkies.Ftd.Cameras;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Modding.Types;
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
        private static Func<bool> _mouseOverEditorUiProbe;
        private static int _buildInputClaimUntilFrame = -1;
        private static int _cameraInputClaimUntilFrame = -1;
        private static bool _nativeBlockPaletteMode;
        private static bool _nativeBlockPalettePlacementArmed;
        private static ItemDefinition _nativeBlockPaletteExpectedItem;
        private static bool _nativeBlockPaletteRequiresMouseRelease;
        private static bool _nativeBlockPaletteTextInputFocused;
        private static bool _nativeBlockPaletteModalInputOpen;
        private static bool _nativeBlockPaletteAuxiliaryInputWasSuppressed;
        private static Func<cBuild, bool> _nativeBlockPaletteCursorPreviewUpdater;
        private static bool _nativeBlockPaletteCursorPlacementReady;
        private static bool _nativeBlockPaletteCursorPreviewApplying;
        private static readonly FieldInfo NativeInputBuildField =
            AccessTools.Field(typeof(cInput), "_i_cInput_cBuild");
        private static readonly MethodInfo ResetCursorDirectionStatesMethod =
            AccessTools.Method(typeof(cBuild), "ResetCursorDirectionStates");

        internal static bool Active => _active;

        internal static bool MouseOverEditorUi =>
            _active &&
            (_mouseOverEditorUi || ProbeMouseOverEditorUi());

        internal static bool OwnsBuildInputThisFrame =>
            _active && Time.frameCount <= _buildInputClaimUntilFrame;

        internal static bool OwnsCameraInputThisFrame =>
            _active && Time.frameCount <= _cameraInputClaimUntilFrame;

        internal static bool ScrollWheelOverEditorUi =>
            MouseOverEditorUi &&
            Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f;

        internal static bool ControlHeldWhileActive =>
            _active && DecoLimitLifter.EsuInputState.IsControlHeld();

        internal static bool NativeBlockPaletteMode =>
            _active && _nativeBlockPaletteMode;

        internal static bool NativeBlockPaletteOwnsRightMouse =>
            NativeBlockPaletteMode &&
            (Input.GetMouseButtonDown(1) ||
             Input.GetMouseButton(1) ||
             Input.GetMouseButtonUp(1));

        internal static bool NativeBlockPaletteRequiresMouseRelease =>
            NativeBlockPaletteMode &&
            _nativeBlockPaletteRequiresMouseRelease;

        internal static void Begin()
        {
            _active = true;
            DecoLimitLifter.EsuVanillaHudVisibilityScope.Begin("Decoration Edit begin");
            DecoLimitLifter.EsuInputFocusGuard.BeginEditor("Decoration Edit Mode");
            _mouseOverEditorUi = false;
            _mouseOverEditorUiProbe = null;
            _buildInputClaimUntilFrame = -1;
            _cameraInputClaimUntilFrame = -1;
            _nativeBlockPaletteMode = false;
            _nativeBlockPalettePlacementArmed = false;
            _nativeBlockPaletteExpectedItem = null;
            _nativeBlockPaletteRequiresMouseRelease = false;
            _nativeBlockPaletteTextInputFocused = false;
            _nativeBlockPaletteModalInputOpen = false;
            _nativeBlockPaletteAuxiliaryInputWasSuppressed = false;
            _nativeBlockPaletteCursorPreviewUpdater = null;
            _nativeBlockPaletteCursorPlacementReady = false;
            _nativeBlockPaletteCursorPreviewApplying = false;
        }

        internal static void End()
        {
            bool wasActive = _active;
            bool nativePreviewWasActive =
                _nativeBlockPaletteMode ||
                _nativeBlockPaletteCursorPreviewUpdater != null ||
                _nativeBlockPaletteCursorPlacementReady;
            if (nativePreviewWasActive)
            {
                try
                {
                    HideNativeBlockPaletteCursorPreview(cBuild.GetSingleton());
                }
                catch
                {
                    // Scope cleanup below must remain unconditional.
                }
            }

            _active = false;
            _mouseOverEditorUi = false;
            _mouseOverEditorUiProbe = null;
            _buildInputClaimUntilFrame = -1;
            _cameraInputClaimUntilFrame = -1;
            _nativeBlockPaletteMode = false;
            _nativeBlockPalettePlacementArmed = false;
            _nativeBlockPaletteExpectedItem = null;
            _nativeBlockPaletteRequiresMouseRelease = false;
            _nativeBlockPaletteTextInputFocused = false;
            _nativeBlockPaletteModalInputOpen = false;
            _nativeBlockPaletteAuxiliaryInputWasSuppressed = false;
            _nativeBlockPaletteCursorPreviewUpdater = null;
            _nativeBlockPaletteCursorPlacementReady = false;
            _nativeBlockPaletteCursorPreviewApplying = false;
            if (wasActive)
                DecoLimitLifter.EsuInputFocusGuard.EndEditor("Decoration Edit Mode");
            if (wasActive)
                DecoLimitLifter.EsuVanillaHudVisibilityScope.End("Decoration Edit end");
        }

        internal static void ForceResetIfActive(string reason)
        {
            if (!_active &&
                !_mouseOverEditorUi &&
                _buildInputClaimUntilFrame < 0 &&
                _cameraInputClaimUntilFrame < 0 &&
                !_nativeBlockPaletteMode &&
                _nativeBlockPaletteCursorPreviewUpdater == null &&
                !_nativeBlockPaletteCursorPlacementReady)
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

        internal static void SetMouseOverEditorUi(bool value)
        {
            _mouseOverEditorUi = _active && value;
            if (_mouseOverEditorUi && NativeBlockPaletteMode)
                RefreshNativeBlockPaletteCursorPreview(cBuild.GetSingleton());
        }

        internal static void SetMouseOverEditorUiProbe(Func<bool> probe) =>
            _mouseOverEditorUiProbe = _active ? probe : null;

        internal static void SetNativeBlockPaletteMode(bool value)
        {
            _nativeBlockPaletteMode = _active && value;
            if (_nativeBlockPaletteMode)
                _nativeBlockPaletteRequiresMouseRelease = true;
            else
            {
                _nativeBlockPalettePlacementArmed = false;
                _nativeBlockPaletteExpectedItem = null;
                _nativeBlockPaletteRequiresMouseRelease = false;
                _nativeBlockPaletteTextInputFocused = false;
                _nativeBlockPaletteModalInputOpen = false;
                _nativeBlockPaletteAuxiliaryInputWasSuppressed = false;
                _nativeBlockPaletteCursorPreviewUpdater = null;
                _nativeBlockPaletteCursorPlacementReady = false;
                _nativeBlockPaletteCursorPreviewApplying = false;
            }
        }

        internal static void SetNativeBlockPaletteAuxiliaryInputOwnership(
            bool textInputFocused,
            bool modalInputOpen)
        {
            _nativeBlockPaletteTextInputFocused =
                NativeBlockPaletteMode && textInputFocused;
            _nativeBlockPaletteModalInputOpen =
                NativeBlockPaletteMode && modalInputOpen;
            if (_nativeBlockPaletteTextInputFocused ||
                _nativeBlockPaletteModalInputOpen)
            {
                RefreshNativeBlockPaletteCursorPreview(cBuild.GetSingleton());
            }
        }

        internal static void SetNativeBlockPaletteCursorPreviewUpdater(
            Func<cBuild, bool> updater)
        {
            _nativeBlockPaletteCursorPreviewUpdater = NativeBlockPaletteMode
                ? updater
                : null;
            _nativeBlockPaletteCursorPlacementReady = false;
            _nativeBlockPaletteCursorPreviewApplying = false;
            if (_nativeBlockPaletteCursorPreviewUpdater != null)
                RefreshNativeBlockPaletteCursorPreview(cBuild.GetSingleton());
        }

        internal static void SetNativeBlockPalettePlacementArmed(bool value) =>
            SetNativeBlockPaletteExpectedItem(value ? _nativeBlockPaletteExpectedItem : null);

        internal static void SetNativeBlockPaletteExpectedItem(
            ItemDefinition definition)
        {
            _nativeBlockPaletteExpectedItem = NativeBlockPaletteMode
                ? definition
                : null;
            _nativeBlockPalettePlacementArmed =
                _nativeBlockPaletteExpectedItem != null;
        }

        internal static void RequireNativeBlockPaletteMouseRelease()
        {
            if (NativeBlockPaletteMode)
                _nativeBlockPaletteRequiresMouseRelease = true;
        }

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
            DecoLimitLifter.EsuEditorScope.ShouldHideVanillaHud;

        internal static bool SuppressBuildInput(cBuild build = null)
        {
            return SuppressBuildInputCore(
                build,
                allowNativeReleaseReset: false);
        }

        internal static bool SuppressFixedBuildInput(cBuild build)
        {
            return SuppressBuildInputCore(
                build,
                allowNativeReleaseReset: true);
        }

        private static bool SuppressBuildInputCore(
            cBuild build,
            bool allowNativeReleaseReset)
        {
            bool nativeCursorPlacementReady = NativeBlockPaletteMode &&
                                              RefreshNativeBlockPaletteCursorPreview(build);
            bool editorUiMouseGesture =
                _active &&
                MouseOverEditorUi &&
                (Input.GetMouseButton(0) || Input.GetMouseButton(1));
            bool sharedBlocker =
                DecoLimitLifter.EsuEscapeCloseGuard.Active ||
                editorUiMouseGesture ||
                (!NativeBlockPaletteMode && ControlHeldWhileActive) ||
                OwnsBuildInputThisFrame ||
                (!NativeBlockPaletteMode &&
                 _active &&
                 DecoLimitLifter.EsuInputState.AnyEsuBuildShortcutDown()) ||
                ScrollWheelOverEditorUi ||
                SmartBuildInputScope.SuppressBuildInput();
            if (!NativeBlockPaletteMode)
                return sharedBlocker;

            bool primaryOrSecondaryHeld =
                Input.GetMouseButton(0) || Input.GetMouseButton(1);
            bool auxiliaryInputOwned =
                _nativeBlockPaletteTextInputFocused ||
                _nativeBlockPaletteModalInputOpen;
            bool editorOwnsPointer =
                MouseOverEditorUi ||
                NativeBlockPaletteOwnsRightMouse ||
                auxiliaryInputOwned;
            if (editorOwnsPointer)
            {
                try
                {
                    if (build != null)
                        build.H2P = enumH2P.idle;
                }
                catch
                {
                    // The synchronous input gate remains authoritative.
                }
            }

            if ((MouseOverEditorUi && primaryOrSecondaryHeld) ||
                NativeBlockPaletteOwnsRightMouse ||
                (auxiliaryInputOwned && primaryOrSecondaryHeld))
            {
                _nativeBlockPaletteRequiresMouseRelease = true;
            }

            if (_nativeBlockPaletteRequiresMouseRelease)
            {
                if (!allowNativeReleaseReset ||
                    primaryOrSecondaryHeld ||
                    sharedBlocker ||
                    MouseOverEditorUi ||
                    NativeBlockPaletteOwnsRightMouse ||
                    auxiliaryInputOwned)
                {
                    return true;
                }

                // Let the first button-free native fixed/update pass reset FtD's
                // private left/right hold counters before placement is enabled.
                _nativeBlockPaletteRequiresMouseRelease = false;
                return false;
            }

            if (sharedBlocker ||
                MouseOverEditorUi ||
                NativeBlockPaletteOwnsRightMouse ||
                auxiliaryInputOwned ||
                !ExactNativeBlockPaletteItemSelected(build))
            {
                return true;
            }

            bool primaryHeld = Input.GetMouseButton(0);
            return ShouldSuppressNativeBlockPalettePrimaryPlacement(
                primaryHeld,
                nativeCursorPlacementReady,
                UnsafeSimpleBuildReplacementGesture(build));
        }

        internal static bool ShouldSuppressNativeBlockPalettePrimaryPlacement(
            bool primaryHeld,
            bool cursorPlacementReady,
            bool unsafeSimpleReplacementGesture) =>
            primaryHeld &&
            (!cursorPlacementReady || unsafeSimpleReplacementGesture);

        private static bool RefreshNativeBlockPaletteCursorPreview(cBuild build)
        {
            if (!NativeBlockPaletteMode ||
                build == null ||
                !NativeBlockPaletteBuildContextValid(build) ||
                _nativeBlockPaletteCursorPreviewUpdater == null)
            {
                _nativeBlockPaletteCursorPlacementReady = false;
                if (NativeBlockPaletteMode)
                    HideNativeBlockPaletteCursorPreview(build);
                return false;
            }

            if (_nativeBlockPaletteCursorPreviewApplying)
                return _nativeBlockPaletteCursorPlacementReady;

            _nativeBlockPaletteCursorPreviewApplying = true;
            try
            {
                _nativeBlockPaletteCursorPlacementReady =
                    _nativeBlockPaletteCursorPreviewUpdater(build);
            }
            catch
            {
                _nativeBlockPaletteCursorPlacementReady = false;
                HideNativeBlockPaletteCursorPreview(build);
            }
            finally
            {
                _nativeBlockPaletteCursorPreviewApplying = false;
            }

            return _nativeBlockPaletteCursorPlacementReady;
        }

        internal static bool OverrideNativeBlockPaletteDoBuildMode(cBuild build)
        {
            if (!NativeBlockPaletteMode)
                return false;

            if (!NativeBlockPaletteBuildContextValid(build))
            {
                HideNativeBlockPaletteCursorPreview(build);
                return false;
            }

            RefreshNativeBlockPaletteCursorPreview(build);
            return true;
        }

        internal static bool CanOverrideNativeBlockPaletteDoBuildMode(
            bool nativeBlockPaletteMode,
            bool constructPresent,
            bool constructDestroyed,
            bool markerPresent,
            bool teamMatches) =>
            nativeBlockPaletteMode &&
            constructPresent &&
            !constructDestroyed &&
            markerPresent &&
            teamMatches;

        internal static bool NativeBlockPaletteBuildContextValid(cBuild build)
        {
            AllConstruct construct = null;
            bool teamMatches = false;
            try
            {
                construct = build?.GetC();
                teamMatches = construct != null &&
                              construct.GetTeam().Equals(build.team);
            }
            catch
            {
                construct = null;
            }

            return CanOverrideNativeBlockPaletteDoBuildMode(
                NativeBlockPaletteMode,
                constructPresent: construct != null,
                constructDestroyed: construct == null || construct.Destroyed,
                markerPresent: build?.buildMarker != null,
                teamMatches);
        }

        internal static bool SuppressNativeBlockPaletteLateUpdate(cBuild build)
        {
            if (!NativeBlockPaletteMode)
                return false;

            return !RefreshNativeBlockPaletteCursorPreview(build);
        }

        internal static bool SuppressNativeBlockPaletteIndicators() =>
            NativeBlockPaletteMode;

        private static bool UnsafeSimpleBuildReplacementGesture(cBuild build)
        {
            if (build == null || !build.shiftHeld)
                return false;

            try
            {
                return build.GetOptions()?.SimpleBuildMode == true;
            }
            catch
            {
                return true;
            }
        }

        private static void HideNativeBlockPaletteCursorPreview(cBuild build)
        {
            try
            {
                if (build == null)
                    return;

                DecorationEditSession.NeutralizeNativeBlockPalettePlacement(build);
                if (build.buildMarker == null)
                    return;
                build.buildMarker.SetBlockBeingBuiltOn(null);
                build.buildMarker.DisableTheRotationMarkers();
                build.buildMarker.SetVisibilityOfMainMarkerAndVehicleDirectionArrows(false);
            }
            catch
            {
                // Skipping native placement remains authoritative if FtD changes the marker.
            }
        }

        private static bool ExactNativeBlockPaletteItemSelected(cBuild build)
        {
            bool exactNativeItem = _nativeBlockPalettePlacementArmed;
            if (!exactNativeItem || build == null)
                return exactNativeItem;

            try
            {
                return build.BuildingWith?.Mode == BuildingWithMode.Item &&
                       ReferenceEquals(
                           build.BuildingWith.Item,
                           _nativeBlockPaletteExpectedItem) &&
                       build.BuildingWith.Item?.ItemType == enumItemType.block;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldSuppressNativeBlockPaletteAuxiliaryInput(
            bool nativeBlockPaletteMode,
            bool textInputFocused,
            bool modalInputOpen,
            bool ownsUiGesture) =>
            nativeBlockPaletteMode &&
            (textInputFocused || modalInputOpen || ownsUiGesture);

        internal static bool SuppressNativeBlockPaletteAuxiliaryInput(
            cInput input)
        {
            bool pointerGestureOwned =
                OwnsBuildInputThisFrame ||
                NativeBlockPaletteOwnsRightMouse ||
                NativeBlockPaletteRequiresMouseRelease ||
                DecoLimitLifter.EsuEscapeCloseGuard.Active ||
                (MouseOverEditorUi &&
                 (Input.GetMouseButton(0) || Input.GetMouseButton(1)));
            bool suppress = ShouldSuppressNativeBlockPaletteAuxiliaryInput(
                NativeBlockPaletteMode,
                _nativeBlockPaletteTextInputFocused,
                _nativeBlockPaletteModalInputOpen,
                pointerGestureOwned) ||
                DecoLimitLifter.EsuEscapeCloseGuard.Active;
            if (!suppress)
            {
                _nativeBlockPaletteAuxiliaryInputWasSuppressed = false;
                return false;
            }

            if (!_nativeBlockPaletteAuxiliaryInputWasSuppressed)
                ResetNativeBlockPaletteDirectionalInput(input);
            _nativeBlockPaletteAuxiliaryInputWasSuppressed = true;
            return true;
        }

        private static void ResetNativeBlockPaletteDirectionalInput(cInput input)
        {
            try
            {
                cBuild build =
                    NativeInputBuildField?.GetValue(input) as cBuild ??
                    cBuild.GetSingleton();
                if (build == null)
                    return;

                build.H2P = enumH2P.idle;
                build.TabHeld(false);
                build.ShiftHeld(false);
                build.useDoubleTapSpeed = false;
                build.OrientationKeyPressed(Vector3.zero);
                ResetCursorDirectionStatesMethod?.Invoke(build, null);
            }
            catch
            {
                // Skipping cInput remains authoritative if FtD changes internals.
            }
        }

        internal static bool SuppressCameraInput() =>
            DecoLimitLifter.EsuEscapeCloseGuard.Active ||
            (NativeBlockPaletteMode &&
             (MouseOverEditorUi ||
              _nativeBlockPaletteTextInputFocused ||
              _nativeBlockPaletteModalInputOpen ||
              NativeBlockPaletteOwnsRightMouse ||
              NativeBlockPaletteRequiresMouseRelease)) ||
            OwnsCameraInputThisFrame ||
            ScrollWheelOverEditorUi ||
            SmartBuildInputScope.SuppressCameraInput();

        internal static bool IsPaintHoverMessage(object[] arguments)
        {
            return false;
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

        private static bool ProbeMouseOverEditorUi()
        {
            if (_mouseOverEditorUiProbe == null)
                return false;

            try
            {
                return _mouseOverEditorUiProbe();
            }
            catch
            {
                return false;
            }
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
        private static bool Prefix(cBuild __instance) =>
            !DecorationEditorInputScope.SuppressBuildInput(__instance);
    }

    [HarmonyPatch(typeof(BuildCameraMode), nameof(BuildCameraMode.RunUpdate))]
    internal static class DecorationEditor_BuildCameraMode_RunUpdate_Patch
    {
        private static bool Prefix() => !DecorationEditorInputScope.SuppressCameraInput();
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.RunFixedUpdate))]
    internal static class DecorationEditor_cBuild_RunFixedUpdate_Patch
    {
        private static bool Prefix(cBuild __instance) =>
            !DecorationEditorInputScope.SuppressFixedBuildInput(__instance);
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.DoBuildMode))]
    internal static class DecorationEditor_cBuild_DoBuildMode_Patch
    {
        private static bool Prefix(cBuild __instance) =>
            !DecorationEditorInputScope.OverrideNativeBlockPaletteDoBuildMode(
                __instance);
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.RunLateUpdate))]
    internal static class DecorationEditor_cBuild_RunLateUpdate_Patch
    {
        private static bool Prefix(cBuild __instance) =>
            !DecorationEditorInputScope.SuppressNativeBlockPaletteLateUpdate(
                __instance);
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.DrawIndicators))]
    internal static class DecorationEditor_cBuild_DrawIndicators_Patch
    {
        private static bool Prefix() =>
            !DecorationEditorInputScope.SuppressNativeBlockPaletteIndicators();
    }

    [HarmonyPatch]
    internal static class DecorationEditor_cInput_RunUpdate_Patch
    {
        private static MethodBase TargetMethod() =>
            DecoLimitLifter.Plugin.ResolveDecorationEditorInputUpdateTarget();

        private static bool Prefix(cInput __instance) =>
            !DecorationEditorInputScope.SuppressNativeBlockPaletteAuxiliaryInput(
                __instance);
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
