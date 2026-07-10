using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.AutomationBuilderMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed class SmartBuildModeBehaviour : MonoBehaviour
    {
        private SmartBuildSession _session;
        private SmartBuildSession _handoffGuiSession;
        private int _handoffGuiFrame = -1;

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
                EsuRuntimeLog.Warning("Smart Builder", reason);
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
                EsuRuntimeLog.Warning("Smart Builder", reason);
                InfoStore.Add(reason);
                return false;
            }

            Open(modeSwitch: true);
            return true;
        }

        internal bool TrySwitchToAutomationBuilder()
        {
            if (!Active)
                return false;

            if (_session != null &&
                !_session.CanSwitchToDecorationEdit(out string reason))
            {
                EsuRuntimeLog.Warning("Smart Builder", reason);
                InfoStore.Add(reason);
                return true;
            }

            if (!AutomationBuilderModeRegistration.CanOpenFromModeSwitch(out reason))
            {
                EsuRuntimeLog.Warning("Smart Builder", reason);
                InfoStore.Add(reason);
                return true;
            }

            DecoLimitLifter.EsuModeSwitchHandoff.Begin();
            Close(
                reason: null,
                notifyClose: false,
                preserveSharedHud: true,
                keepModeSwitchHandoffGui: true);
            if (!AutomationBuilderModeRegistration.OpenFromModeSwitch())
            {
                ClearModeSwitchHandoffGui();
                Open(modeSwitch: true);
                InfoStore.Add("Automation Builder failed to open; Smart Block Builder restored.");
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
                    .ConsumeSmartBuildToggleDown();

                if (Active)
                    DecoLimitLifter.EsuVanillaInputBridge.Tick();

                if (Active && toggleDown)
                {
                    if (_session != null && _session.DismissOpenPopup())
                        return;

                    Close("toggle pressed");
                    return;
                }

                if (Active && Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_session != null && _session.DismissOpenPopup())
                        return;

                    DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                    Close("Escape pressed");
                    return;
                }

                if (Active &&
                    _session != null &&
                    _session.SwitchToDecorationEditRequested)
                {
                    _session.ClearSwitchToDecorationEditRequest();
                    TrySwitchToAutomationBuilder();
                    return;
                }

                if (Active && ReadSwitchModeKeyDown())
                {
                    TrySwitchToAutomationBuilder();
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

                    SmartBuildInputScope.ForceResetIfActive("no active smart build session");
                    DecoLimitLifter.EsuInputFocusGuard.TickPostExitRepair(
                        "Smart Block Builder inactive");
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
                EsuRuntimeLog.Exception("Smart Builder", exception, "Smart Block Builder update failed");
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
                if (_session != null)
                {
                    ClearModeSwitchHandoffGui();
                    _session.OnGUI();
                    DecoLimitLifter.EsuModeSwitchHandoff.ClaimTargetEditorOpened();
                    return;
                }

                if (_handoffGuiSession != null &&
                    Time.frameCount <= _handoffGuiFrame &&
                    DecoLimitLifter.EsuModeSwitchHandoff.ShouldDrawPassiveGui())
                {
                    _handoffGuiSession.DrawModeSwitchHandoffGui();
                    return;
                }

                ClearModeSwitchHandoffGui();
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception("Smart Builder", exception, "Smart Block Builder GUI failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder GUI failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
            }
        }

        private void Open(bool modeSwitch = false)
        {
            ClearModeSwitchHandoffGui();
            cBuild build = cBuild.GetSingleton();
            _session = new SmartBuildSession(build);
            _session.Begin();
            EsuRuntimeLog.Info("Smart Builder", modeSwitch ? "Smart Block Builder opened from mode switch." : "Smart Block Builder opened.");
            if (!modeSwitch)
                InfoStore.Add("Smart Block Builder opened. Click the focused construct grid to create a runtime preview, then Apply to place blocks.");
        }

        private void Close(
            string reason = null,
            bool notifyClose = true,
            bool preserveSharedHud = false,
            bool keepModeSwitchHandoffGui = false)
        {
            SmartBuildSession session = _session;
            _session = null;
            if (keepModeSwitchHandoffGui && session != null)
            {
                ClearModeSwitchHandoffGui();
                session.SuspendForModeSwitchHandoff();
                _handoffGuiSession = session;
                _handoffGuiFrame = Time.frameCount + DecoLimitLifter.EsuModeSwitchHandoff.PassiveGuiFrames;
            }
            else
            {
                session?.End(preserveSharedHud);
            }

            SmartBuildInputScope.ForceResetIfActive("smart builder closed");
            DecoLimitLifter.EsuHudDiagnostics.WarnIfEditorScopeActive(
                "smart builder closed",
                "Smart Builder",
                () => SmartBuildInputScope.Active);
            if (notifyClose)
                DecoLimitLifter.EsuSymmetry.Clear();
            if (notifyClose)
            {
                EsuRuntimeLog.Info(
                    "Smart Builder",
                    string.IsNullOrWhiteSpace(reason)
                        ? "Smart Block Builder closed."
                        : "Smart Block Builder closed: " + reason + ".");
                InfoStore.Add(string.IsNullOrWhiteSpace(reason)
                    ? "Smart Block Builder closed."
                    : "Smart Block Builder closed: " + reason + ".");
            }
        }

        private void ClearModeSwitchHandoffGui()
        {
            SmartBuildSession session = _handoffGuiSession;
            _handoffGuiSession = null;
            _handoffGuiFrame = -1;
            session?.End(preserveSharedHud: true);
            SmartBuildInputScope.ForceResetIfActive("smart builder handoff gui cleared");
            DecoLimitLifter.EsuHudDiagnostics.WarnIfEditorScopeActive(
                "smart builder handoff gui cleared",
                "Smart Builder",
                () => SmartBuildInputScope.Active);
        }
    }
}
