using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal static class SmartBuildModeRegistration
    {
        private const string BehaviourName = "EndlessShapesUnlimited.SmartBuildMode";
        private static GameObject _host;
        private static SmartBuildModeBehaviour _behaviour;
        private static bool _registered;

        internal static bool Active => _behaviour != null && _behaviour.Active;

        internal static void Register()
        {
            if (_registered)
                return;

            _ = SerializationHudKeyMap.Instance;
            _host = new GameObject(BehaviourName);
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _behaviour = _host.AddComponent<SmartBuildModeBehaviour>();
            _registered = true;
        }

        internal static void Unregister()
        {
            Exception failure = null;
            try
            {
                _behaviour?.ForceClose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                if (_host != null)
                    UnityEngine.Object.Destroy(_host);
            }
            catch (Exception exception)
            {
                failure = failure == null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            _behaviour = null;
            _host = null;
            _registered = false;
            if (failure != null)
                throw failure;
        }

        internal static void ToggleFromUi()
        {
            try
            {
                if (_behaviour == null)
                {
                    InfoStore.Add("Smart Block Builder is not registered.");
                    return;
                }

                _behaviour.ToggleFromUi();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder toggle failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }
        }

        internal static bool OpenFromModeSwitch()
        {
            try
            {
                if (_behaviour == null)
                {
                    InfoStore.Add("Smart Block Builder is not registered.");
                    return false;
                }

                return _behaviour.OpenFromModeSwitch();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder switch-open failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                return false;
            }
        }

        internal static bool CanOpenFromModeSwitch(out string reason) =>
            CanOpenNow(
                out reason,
                ignoreDecorationEditMode: true,
                modeSwitch: true);

        internal static bool CanOpenNow(out string reason) =>
            CanOpenNow(
                out reason,
                ignoreDecorationEditMode: false,
                modeSwitch: false);

        private static bool CanOpenNow(
            out string reason,
            bool ignoreDecorationEditMode,
            bool modeSwitch)
        {
            reason = null;
            if (!ignoreDecorationEditMode &&
                DecorationEditModeRegistration.Active)
            {
                reason = "Close Decoration Edit Mode before opening Smart Block Builder.";
                return false;
            }

            bool inputAvailable = modeSwitch
                ? DecoLimitLifter.EsuInputState.CanSwitchEsuModes()
                : DecoLimitLifter.EsuInputState.CanUseHotkeys();
            if (!inputAvailable)
            {
                reason = modeSwitch
                    ? "Close text input before switching ESU modes."
                    : "Close text input or blocking UI before opening Smart Block Builder.";
                return false;
            }

            cBuild build = cBuild.GetSingleton();
            if (build == null ||
                (build.buildMode != enumBuildMode.active &&
                 build.buildMode != enumBuildMode.activeInventory) ||
                build.GetC() == null ||
                build.GetCC() == null)
            {
                reason = "Enter build mode on a craft before opening Smart Block Builder.";
                return false;
            }

            return true;
        }
    }
}
