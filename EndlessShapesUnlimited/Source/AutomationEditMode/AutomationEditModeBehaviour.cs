using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationEditModeBehaviour : MonoBehaviour
    {
        private AutomationEditSession _session;

        internal bool Active => _session != null && _session.Active;

        internal void ToggleFromUi()
        {
            if (Active)
            {
                Close("toggle pressed");
                return;
            }

            if (!AutomationEditModeRegistration.CanOpenNow(out string reason))
            {
                EsuRuntimeLog.Warning("Automation Editor", reason);
                InfoStore.Add(reason);
                return;
            }

            Open();
        }

        internal bool OpenFromModeSwitch()
        {
            if (Active)
                return true;

            if (!AutomationEditModeRegistration.CanOpenFromModeSwitch(out string reason))
            {
                EsuRuntimeLog.Warning("Automation Editor", reason);
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
                EsuRuntimeLog.Warning("Automation Editor", reason);
                InfoStore.Add(reason);
                return true;
            }

            if (!DecorationEditModeRegistration.CanOpenFromModeSwitch(out reason))
            {
                EsuRuntimeLog.Warning("Automation Editor", reason);
                InfoStore.Add(reason);
                return true;
            }

            DecoLimitLifter.EsuModeSwitchHandoff.Begin();
            Close(reason: null, notifyClose: false, preserveSharedHud: true);
            if (DecorationEditModeRegistration.OpenFromModeSwitch())
                InfoStore.Add("ESU mode: Decoration Edit.");
            else
            {
                Open(modeSwitch: true);
                InfoStore.Add("Decoration Edit Mode failed to open; Automation Editor restored.");
            }

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
                    .ConsumeAutomationEditToggleDown();

                if (Active)
                    DecoLimitLifter.EsuVanillaInputBridge.Tick();

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

                if (Active && toggleDown)
                {
                    Close("toggle pressed");
                    return;
                }

                if (Active && Input.GetKeyDown(KeyCode.Escape))
                {
                    DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                    Close("Escape pressed");
                    return;
                }

                if (!Active && toggleDown)
                {
                    ToggleFromUi();
                    return;
                }

                if (!Active)
                {
                    if (DecoLimitLifter.EsuModeSwitchHandoff.ConsumeInactiveCleanupFrame())
                        return;

                    AutomationInputScope.ForceResetIfActive(
                        "no active automation editor session");
                    DecoLimitLifter.EsuInputFocusGuard.TickPostExitRepair(
                        "Automation Editor inactive");
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
                EsuRuntimeLog.Exception("Automation Editor", exception, "Automation Editor update failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Automation Editor update failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
            }
        }

        private static bool ReadSwitchModeKeyDown() =>
            DecoLimitLifter.EsuBuildModeInputGate.ConsumeSwitchModeDown();

        private void OnGUI()
        {
            try
            {
                _session?.OnGUI();
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception("Automation Editor", exception, "Automation Editor GUI failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Automation Editor GUI failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
            }
        }

        private void Open(bool modeSwitch = false)
        {
            cBuild build = cBuild.GetSingleton();
            _session = new AutomationEditSession(build);
            _session.Begin();
            EsuRuntimeLog.Info(
                "Automation Editor",
                modeSwitch
                    ? "Automation Editor opened from mode switch."
                    : "Automation Editor opened.");
            if (!modeSwitch)
                InfoStore.Add("Automation Editor opened. Place or select a Breadboard/ACB, then link target blocks.");
        }

        private void Close(
            string reason = null,
            bool notifyClose = true,
            bool preserveSharedHud = false)
        {
            AutomationEditSession session = _session;
            _session = null;
            session?.End(preserveSharedHud);
            if (notifyClose)
                DecoLimitLifter.EsuSymmetry.Clear();
            if (notifyClose)
            {
                EsuRuntimeLog.Info(
                    "Automation Editor",
                    string.IsNullOrWhiteSpace(reason)
                        ? "Automation Editor closed."
                        : "Automation Editor closed: " + reason + ".");
                InfoStore.Add(string.IsNullOrWhiteSpace(reason)
                    ? "Automation Editor closed."
                    : "Automation Editor closed: " + reason + ".");
            }
        }
    }
}
