using System;
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
            if (!ReadSwitchModeHeld())
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
            if (!ReadDecorationEditToggleHeld())
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
            if (!ReadSmartBuildToggleHeld())
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

        private static bool ReadSwitchModeDown() =>
            ReadProfileKey(
                SerializationHudKeyInput.SwitchEsuBuildMode,
                KeyInputEventType.Down,
                () => Input.GetKeyDown(KeyCode.Tab));

        private static bool ReadSwitchModeHeld() =>
            ReadProfileKey(
                SerializationHudKeyInput.SwitchEsuBuildMode,
                KeyInputEventType.Held,
                () => Input.GetKey(KeyCode.Tab));

        private static bool ReadDecorationEditToggleDown() =>
            ReadProfileKey(
                SerializationHudKeyInput.ToggleDecorationEditMode,
                KeyInputEventType.Down,
                IsDecorationEditToggleDefaultDown);

        private static bool ReadDecorationEditToggleHeld() =>
            ReadProfileKey(
                SerializationHudKeyInput.ToggleDecorationEditMode,
                KeyInputEventType.Held,
                IsDecorationEditToggleDefaultHeld);

        private static bool ReadSmartBuildToggleDown() =>
            ReadProfileKey(
                SerializationHudKeyInput.ToggleSmartBuildMode,
                KeyInputEventType.Down,
                IsSmartBuildToggleDefaultDown);

        private static bool ReadSmartBuildToggleHeld() =>
            ReadProfileKey(
                SerializationHudKeyInput.ToggleSmartBuildMode,
                KeyInputEventType.Held,
                IsSmartBuildToggleDefaultHeld);

        private static bool ReadProfileKey(
            SerializationHudKeyInput input,
            KeyInputEventType eventType,
            Func<bool> fallback)
        {
            try
            {
                return SerializationHudKeyMap.Instance.Bool(input, eventType);
            }
            catch
            {
                // The direct keyboard fallback is only for early boot/profile failures.
                return fallback != null && fallback();
            }
        }

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
