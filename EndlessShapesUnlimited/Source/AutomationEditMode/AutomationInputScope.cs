using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ui.Displayer;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal static class AutomationInputScope
    {
        private static bool _active;
        private static bool _mouseOverUi;
        private static int _buildInputClaimUntilFrame = -1;
        private static int _cameraInputClaimUntilFrame = -1;

        internal static bool Active => _active;

        internal static bool MouseOverUi => _active && _mouseOverUi;

        internal static bool OwnsBuildInputThisFrame =>
            _active && Time.frameCount <= _buildInputClaimUntilFrame;

        internal static bool OwnsCameraInputThisFrame =>
            _active && Time.frameCount <= _cameraInputClaimUntilFrame;

        internal static bool ScrollWheelOverUi =>
            MouseOverUi &&
            Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f;

        internal static bool ControlHeldWhileActive =>
            _active && DecoLimitLifter.EsuInputState.IsControlHeld();

        internal static void Begin()
        {
            _active = true;
            DecoLimitLifter.EsuInputFocusGuard.BeginEditor("Automation Editor");
            _mouseOverUi = false;
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
            _mouseOverUi = false;
            _buildInputClaimUntilFrame = -1;
            _cameraInputClaimUntilFrame = -1;
            if (wasActive)
                DecoLimitLifter.EsuInputFocusGuard.EndEditor("Automation Editor");
        }

        internal static void ForceResetIfActive(string reason)
        {
            if (!_active &&
                !_mouseOverUi &&
                _buildInputClaimUntilFrame < 0 &&
                _cameraInputClaimUntilFrame < 0)
            {
                return;
            }

            End();
            try
            {
                AdvLogger.LogWarning(
                    "[EndlessShapes Unlimited] Automation Editor input scope was force-reset: " +
                    reason,
                    LogOptions._AlertDevInGame);
            }
            catch
            {
                // Input recovery must not depend on logging.
            }
        }

        internal static void SetMouseOverUi(bool value) =>
            _mouseOverUi = _active && value;

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
                // Build/camera claims still protect the current frame.
            }
        }

        internal static bool SuppressBuildHud() => _active;

        internal static bool SuppressBuildInput() =>
            DecoLimitLifter.EsuEscapeCloseGuard.Active ||
            ControlHeldWhileActive ||
            OwnsBuildInputThisFrame ||
            (_active && DecoLimitLifter.EsuInputState.AnyEsuNumberShortcutDown()) ||
            ScrollWheelOverUi;

        internal static bool SuppressCameraInput() =>
            DecoLimitLifter.EsuEscapeCloseGuard.Active ||
            OwnsCameraInputThisFrame ||
            ScrollWheelOverUi;
    }
}
