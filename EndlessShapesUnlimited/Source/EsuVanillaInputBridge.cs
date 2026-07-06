using BrilliantSkies.Core.Constants;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.AutomationEditMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuVanillaInputBridge
    {
        private static int _pendingFreezeFrame = -1;
        private static int _freezeHandledFrame = -1;

        internal static void Tick()
        {
            if (!DecorationEditModeRegistration.Active &&
                !SmartBuildModeRegistration.Active &&
                !AutomationEditModeRegistration.Active)
            {
                _pendingFreezeFrame = -1;
                return;
            }

            if (ReadFreezeDown())
                _pendingFreezeFrame = Time.frameCount;

            if (_pendingFreezeFrame < 0 ||
                Time.frameCount <= _pendingFreezeFrame)
            {
                return;
            }

            if (_freezeHandledFrame >= _pendingFreezeFrame)
            {
                _pendingFreezeFrame = -1;
                return;
            }

            cBuild build = cBuild.GetSingleton();
            if (build != null && build.IsActive())
            {
                build.ToggleFreeze();
                InfoStore.Add("Vehicle freeze key forwarded to vanilla build mode.");
            }

            _pendingFreezeFrame = -1;
        }

        internal static void MarkFreezeHandledThisFrame() =>
            _freezeHandledFrame = Time.frameCount;

        private static bool ReadFreezeDown()
        {
            try
            {
                return FtdKeyMap.Instance.Bool(
                    KeyInputsFtd.Freeze,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Direct keyboard fallback is only for early boot/profile failures.
                return Input.GetKeyDown(KeyCode.CapsLock);
            }
        }
    }

    [HarmonyPatch(typeof(cBuild), nameof(cBuild.ToggleFreeze))]
    internal static class EsuVanillaInputBridge_cBuild_ToggleFreeze_Patch
    {
        private static void Postfix() =>
            EsuVanillaInputBridge.MarkFreezeHandledThisFrame();
    }
}
