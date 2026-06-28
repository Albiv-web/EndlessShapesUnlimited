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
    internal sealed class SmartBuildModeBehaviour : MonoBehaviour
    {
        private SmartBuildSession _session;

        internal bool Active => _session != null && _session.Active;

        internal void ToggleFromUi()
        {
            if (Active)
            {
                Close();
                return;
            }

            if (!SmartBuildModeRegistration.CanOpenNow(out string reason))
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

            if (!SmartBuildModeRegistration.CanOpenFromModeSwitch(out string reason))
            {
                InfoStore.Add(reason);
                return false;
            }

            Open(modeSwitch: true);
            return true;
        }

        internal bool TrySwitchToDecorationEdit()
        {
            if (!Active)
                return false;

            if (_session != null &&
                !_session.CanSwitchToDecorationEdit(out string reason))
            {
                InfoStore.Add(reason);
                return true;
            }

            if (!DecorationEditModeRegistration.CanOpenFromModeSwitch(out reason))
            {
                InfoStore.Add(reason);
                return true;
            }

            Close(reason: null, notifyClose: false);
            if (DecorationEditModeRegistration.OpenFromModeSwitch())
                InfoStore.Add("ESU mode: Decoration Edit.");
            else
                Open(modeSwitch: true);
            return true;
        }

        internal void ForceClose()
        {
            if (Active)
                Close();
        }

        private void Update()
        {
            try
            {
                bool toggleDown = DecoLimitLifter.EsuBuildModeInputGate
                    .ConsumeSmartBuildToggleDown();

                if (Active)
                    DecoLimitLifter.EsuVanillaInputBridge.Tick();

                if (Active && toggleDown)
                {
                    Close("toggle pressed");
                    return;
                }

                if (Active &&
                    _session != null &&
                    _session.SwitchToDecorationEditRequested)
                {
                    _session.ClearSwitchToDecorationEditRequest();
                    TrySwitchToDecorationEdit();
                    return;
                }

                if (Active && ReadSwitchModeKeyDown())
                {
                    TrySwitchToDecorationEdit();
                    return;
                }

                if (!Active && toggleDown)
                {
                    ToggleFromUi();
                    return;
                }

                if (!Active)
                {
                    SmartBuildInputScope.ForceResetIfActive("no active smart build session");
                    return;
                }

                cBuild build = cBuild.GetSingleton();
                if (build == null ||
                    build.buildMode == enumBuildMode.inactive ||
                    build.GetC() == null ||
                    build.GetCC() == null)
                {
                    Close("build mode became unavailable");
                    return;
                }

                _session.Update();
                if (_session.CloseRequested)
                    Close();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder update failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
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
                    "[EndlessShapes Unlimited] Smart Block Builder GUI failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
            }
        }

        private void Open(bool modeSwitch = false)
        {
            cBuild build = cBuild.GetSingleton();
            _session = new SmartBuildSession(build);
            _session.Begin();
            if (!modeSwitch)
                InfoStore.Add("Smart Block Builder opened. Click the focused construct grid to create a runtime preview, then Apply to place blocks.");
        }

        private void Close(string reason = null, bool notifyClose = true)
        {
            SmartBuildSession session = _session;
            _session = null;
            session?.End();
            if (notifyClose)
            {
                InfoStore.Add(string.IsNullOrWhiteSpace(reason)
                    ? "Smart Block Builder closed."
                    : "Smart Block Builder closed: " + reason + ".");
            }
        }
    }
}
