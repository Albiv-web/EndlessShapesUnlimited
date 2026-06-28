using System.Reflection;
using Assets.Scripts;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Ftd.Avatar.Build;
using HarmonyLib;

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
