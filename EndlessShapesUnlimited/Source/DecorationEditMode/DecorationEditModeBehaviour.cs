using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditModeBehaviour : MonoBehaviour
    {
        private DecorationEditSession _session;

        internal bool Active => _session != null && _session.Active;

        internal void ToggleFromUi()
        {
            if (Active)
            {
                Close(apply: false);
                return;
            }

            if (!DecorationEditModeRegistration.CanOpenNow(out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            Open();
        }

        internal bool OpenFromModeSwitch()
        {
            if (Active)
                return true;

            if (!DecorationEditModeRegistration.CanOpenFromModeSwitch(out string reason))
            {
                InfoStore.Add(reason);
                return false;
            }

            Open(modeSwitch: true);
            return true;
        }

        internal bool TrySwitchToSmartBuild()
        {
            if (!Active)
                return false;

            string reason = null;
            if (_session == null ||
                !_session.CanSwitchToSmartBuild(out reason))
            {
                InfoStore.Add(reason ?? "Apply or Cancel Decoration Edit changes before switching modes.");
                return true;
            }

            if (!SmartBuildModeRegistration.CanOpenFromModeSwitch(out reason))
            {
                InfoStore.Add(reason);
                return true;
            }

            Close(apply: false, notifySession: false, notifyClose: false);
            if (SmartBuildModeRegistration.OpenFromModeSwitch())
                InfoStore.Add("ESU mode: Smart Builder.");
            else
            {
                Open(modeSwitch: true);
                InfoStore.Add("Smart Builder failed to open; Decoration Edit Mode restored.");
            }
            return true;
        }

        internal void ForceClose()
        {
            if (Active)
                Close(apply: false);
        }

        private void Update()
        {
            try
            {
                bool toggleDown = DecoLimitLifter.EsuBuildModeInputGate
                    .ConsumeDecorationEditToggleDown();

                if (Active)
                    DecoLimitLifter.EsuVanillaInputBridge.Tick();

                if (Active &&
                    _session != null &&
                    _session.SwitchToSmartBuildRequested)
                {
                    _session.ClearSwitchToSmartBuildRequest();
                    TrySwitchToSmartBuild();
                    return;
                }

                if (Active &&
                    ReadSwitchModeKeyDown())
                {
                    TrySwitchToSmartBuild();
                    return;
                }

                if (Active &&
                    toggleDown)
                {
                    Close(apply: false);
                    return;
                }

                if (Active && Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_session.HandleEscape())
                        return;
                    Close(apply: false);
                    return;
                }

                if (!Active && toggleDown)
                {
                    ToggleFromUi();
                    return;
                }

                if (!Active)
                {
                    DecorationEditorInputScope.ForceResetIfActive(
                        "no active editor session");
                    return;
                }

                cBuild build = cBuild.GetSingleton();
                if (build == null ||
                    build.buildMode == enumBuildMode.inactive ||
                    build.GetC() == null ||
                    build.GetCC() == null)
                {
                    Close(apply: false);
                    return;
                }

                _session.Update();
                if (_session.CloseRequested)
                    Close(_session.CloseApplies);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode update failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close(apply: false);
            }
        }

        private static bool ReadSwitchModeKeyDown()
        {
            return DecoLimitLifter.EsuBuildModeInputGate.ConsumeSwitchModeDown();
        }

        private void OnGUI()
        {
            try
            {
                _session?.OnGUI();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode GUI failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close(apply: false);
            }
        }

        private void Open(bool modeSwitch = false)
        {
            cBuild build = cBuild.GetSingleton();
            _session = new DecorationEditSession(build);
            _session.Begin();
            if (!modeSwitch)
                InfoStore.Add("Decoration Edit Mode opened. Select one decoration; Apply commits, Cancel restores.");
        }

        private void Close(bool apply, bool notifySession = true, bool notifyClose = true)
        {
            DecorationEditSession session = _session;
            _session = null;
            session?.End(apply, notifySession);
            if (notifyClose)
                InfoStore.Add("Decoration Edit Mode closed.");
        }
    }
}
