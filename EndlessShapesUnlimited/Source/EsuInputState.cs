using System.Reflection;
using Assets.Scripts;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Ftd.Avatar.Build;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuInputState
    {
        private static readonly FieldInfo ChatGuiInstanceField =
            AccessTools.Field(typeof(ChatGUI), "_instance");

        internal static bool AllGameControlsEnabled
        {
            get
            {
                try
                {
                    return Get.UserInput.AllGameControlsEnabled;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static bool CanUseHotkeys() =>
            AllGameControlsEnabled && !IsTextInputActive();

        internal static bool CanSwitchEsuModes() =>
            !IsTextInputActive();

        internal static bool IsControlHeld() =>
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        internal static bool IsEsuNumberShortcutDown(int shortcut)
        {
            switch (shortcut)
            {
                case 1:
                    return Input.GetKeyDown(KeyCode.Alpha1) ||
                           Input.GetKeyDown(KeyCode.Keypad1);
                case 2:
                    return Input.GetKeyDown(KeyCode.Alpha2) ||
                           Input.GetKeyDown(KeyCode.Keypad2);
                case 3:
                    return Input.GetKeyDown(KeyCode.Alpha3) ||
                           Input.GetKeyDown(KeyCode.Keypad3);
                default:
                    return false;
            }
        }

        internal static bool IsFacingSymmetryShortcutDown() =>
            Input.GetKeyDown(KeyCode.N);

        internal static bool AnyEsuNumberShortcutDown() =>
            IsEsuNumberShortcutDown(1) ||
            IsEsuNumberShortcutDown(2) ||
            IsEsuNumberShortcutDown(3);

        internal static bool AnyEsuBuildShortcutDown() =>
            AnyEsuNumberShortcutDown() ||
            IsFacingSymmetryShortcutDown();

        internal static bool IsTextInputActive()
        {
            try
            {
                if (ChatGuiInstanceField?.GetValue(null) is ChatGUI chat)
                    return chat.IsTyping();
            }
            catch
            {
                // Hotkey guards must not construct or depend on chat UI state.
            }

            return false;
        }
    }
}
