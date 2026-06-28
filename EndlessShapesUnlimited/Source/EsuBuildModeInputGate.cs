using BrilliantSkies.Core.Constants;
using BrilliantSkies.PlayerProfiles;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuBuildModeInputGate
    {
        private static int _switchModeConsumedFrame = -1;
        private static int _decorationEditToggleConsumedFrame = -1;
        private static int _smartBuildToggleConsumedFrame = -1;
        private static bool _switchModeRequiresRelease;
        private static bool _decorationEditToggleRequiresRelease;
        private static bool _smartBuildToggleRequiresRelease;

        internal static bool ConsumeSwitchModeDown()
        {
            bool down = ReadSwitchModeDown();
            if (!down && !IsSwitchModeDefaultHeld())
                _switchModeRequiresRelease = false;

            if (_switchModeRequiresRelease ||
                _switchModeConsumedFrame == Time.frameCount ||
                !down)
            {
                return false;
            }

            _switchModeConsumedFrame = Time.frameCount;
            _switchModeRequiresRelease = true;
            return true;
        }

        internal static bool ConsumeDecorationEditToggleDown()
        {
            bool down = ReadDecorationEditToggleDown();
            if (!down && !IsDecorationEditToggleDefaultHeld())
                _decorationEditToggleRequiresRelease = false;

            if (_decorationEditToggleRequiresRelease ||
                _decorationEditToggleConsumedFrame == Time.frameCount ||
                !down)
            {
                return false;
            }

            _decorationEditToggleConsumedFrame = Time.frameCount;
            _decorationEditToggleRequiresRelease = true;
            return true;
        }

        internal static bool ConsumeSmartBuildToggleDown()
        {
            bool down = ReadSmartBuildToggleDown();
            if (!down && !IsSmartBuildToggleDefaultHeld())
                _smartBuildToggleRequiresRelease = false;

            if (_smartBuildToggleRequiresRelease ||
                _smartBuildToggleConsumedFrame == Time.frameCount ||
                !down)
            {
                return false;
            }

            _smartBuildToggleConsumedFrame = Time.frameCount;
            _smartBuildToggleRequiresRelease = true;
            return true;
        }

        private static bool ReadSwitchModeDown()
        {
            bool switchDown = false;
            try
            {
                switchDown = SerializationHudKeyMap.Instance.Bool(
                    SerializationHudKeyInput.SwitchEsuBuildMode,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Keep the fallback below available if the profile key map is not ready.
            }

            return switchDown || Input.GetKeyDown(KeyCode.Tab);
        }

        private static bool ReadDecorationEditToggleDown()
        {
            bool toggleDown = false;
            try
            {
                toggleDown = SerializationHudKeyMap.Instance.Bool(
                    SerializationHudKeyInput.ToggleDecorationEditMode,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Keep the fallback below available if the profile key map is not ready.
            }

            return toggleDown || IsDecorationEditToggleDefaultDown();
        }

        private static bool ReadSmartBuildToggleDown()
        {
            bool toggleDown = false;
            try
            {
                toggleDown = SerializationHudKeyMap.Instance.Bool(
                    SerializationHudKeyInput.ToggleSmartBuildMode,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Keep the fallback below available if the profile key map is not ready.
            }

            return toggleDown || IsSmartBuildToggleDefaultDown();
        }

        private static bool IsSwitchModeDefaultHeld() =>
            Input.GetKey(KeyCode.Tab);

        private static bool IsDecorationEditToggleDefaultDown() =>
            IsControlHeld() &&
            Input.GetKeyDown(KeyCode.D);

        private static bool IsDecorationEditToggleDefaultHeld() =>
            IsControlHeld() &&
            Input.GetKey(KeyCode.D);

        private static bool IsSmartBuildToggleDefaultDown() =>
            IsControlHeld() &&
            IsShiftHeld() &&
            Input.GetKeyDown(KeyCode.B);

        private static bool IsSmartBuildToggleDefaultHeld() =>
            IsControlHeld() &&
            IsShiftHeld() &&
            Input.GetKey(KeyCode.B);

        private static bool IsControlHeld() =>
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        private static bool IsShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);
    }
}
