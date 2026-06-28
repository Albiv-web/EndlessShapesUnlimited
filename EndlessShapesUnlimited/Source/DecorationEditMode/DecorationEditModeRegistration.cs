using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.SmartBuildMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class DecorationEditModeRegistration
    {
        private const string BehaviourName = "EndlessShapesUnlimited.DecorationEditMode";
        private static GameObject _host;
        private static DecorationEditModeBehaviour _behaviour;
        private static bool _registered;

        internal static bool Active => _behaviour != null && _behaviour.Active;

        internal static void Register()
        {
            if (_registered)
                return;

            _ = SerializationHudKeyMap.Instance;
            _host = new GameObject(BehaviourName);
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _behaviour = _host.AddComponent<DecorationEditModeBehaviour>();
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
                    InfoStore.Add("Decoration Edit Mode is not registered.");
                    return;
                }
                _behaviour.ToggleFromUi();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode toggle failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }
        }

        internal static bool TrySwitchToSmartBuild()
        {
            try
            {
                if (_behaviour == null || !_behaviour.Active)
                    return false;

                return _behaviour.TrySwitchToSmartBuild();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode switch failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                return true;
            }
        }

        internal static bool OpenFromModeSwitch()
        {
            try
            {
                if (_behaviour == null)
                {
                    InfoStore.Add("Decoration Edit Mode is not registered.");
                    return false;
                }

                return _behaviour.OpenFromModeSwitch();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode switch-open failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                return false;
            }
        }

        internal static bool CanOpenFromModeSwitch(out string reason) =>
            CanOpenNow(out reason, ignoreSmartBuildMode: true);

        internal static bool CanOpenNow(out string reason) =>
            CanOpenNow(out reason, ignoreSmartBuildMode: false);

        private static bool CanOpenNow(out string reason, bool ignoreSmartBuildMode)
        {
            reason = null;
            if (!ignoreSmartBuildMode &&
                SmartBuildModeRegistration.Active)
            {
                reason = "Close Smart Block Builder before opening Decoration Edit Mode.";
                return false;
            }

            if (!DecoLimitLifter.EsuInputState.CanUseHotkeys())
            {
                reason = "Close text input or blocking UI before opening Decoration Edit Mode.";
                return false;
            }

            cBuild build = cBuild.GetSingleton();
            if (build == null ||
                (build.buildMode != enumBuildMode.active &&
                 build.buildMode != enumBuildMode.activeInventory) ||
                build.GetC() == null ||
                build.GetCC() == null)
            {
                reason = "Enter build mode on a craft before opening Decoration Edit Mode.";
                return false;
            }

            return true;
        }
    }
}
