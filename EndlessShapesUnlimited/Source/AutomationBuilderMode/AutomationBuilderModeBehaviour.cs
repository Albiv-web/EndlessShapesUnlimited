using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed class AutomationBuilderModeBehaviour : MonoBehaviour
    {
        private AutomationBuilderSession _session;
        private AutomationBuilderSession _handoffGuiSession;
        private int _handoffGuiFrame = -1;

        internal bool Active => _session != null && _session.Active;

        internal void ToggleFromUi()
        {
            if (Active)
            {
                Close("toggle pressed");
                return;
            }

            if (!AutomationBuilderModeRegistration.CanOpenNow(out string reason))
            {
                EsuRuntimeLog.Warning("Automation Builder", reason);
                InfoStore.Add(reason);
                return;
            }

            Open();
        }

        internal bool OpenFromModeSwitch()
        {
            if (Active)
                return true;

            if (!AutomationBuilderModeRegistration.CanOpenFromModeSwitch(out string reason))
            {
                EsuRuntimeLog.Warning("Automation Builder", reason);
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
                EsuRuntimeLog.Warning("Automation Builder", reason);
                InfoStore.Add(reason);
                return true;
            }

            if (!DecorationEditModeRegistration.CanOpenFromModeSwitch(out reason))
            {
                EsuRuntimeLog.Warning("Automation Builder", reason);
                InfoStore.Add(reason);
                return true;
            }

            DecoLimitLifter.EsuModeSwitchHandoff.Begin();
            Close(
                reason: null,
                notifyClose: false,
                preserveSharedHud: true,
                keepModeSwitchHandoffGui: true);
            if (!DecorationEditModeRegistration.OpenFromModeSwitch())
            {
                ClearModeSwitchHandoffGui();
                Open(modeSwitch: true);
                InfoStore.Add("Decoration Edit Mode failed to open; Automation Builder restored.");
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
                    .ConsumeAutomationBuilderToggleDown();

                if (Active)
                    DecoLimitLifter.EsuVanillaInputBridge.Tick();

                if (Active && toggleDown)
                {
                    _session?.RequestClose();
                    if (_session?.CloseRequested == true)
                        Close("toggle pressed");
                    return;
                }

                if (Active && Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_session != null && _session.DismissCanvas())
                        return;

                    DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                    _session?.RequestClose();
                    if (_session?.CloseRequested == true)
                        Close("Escape pressed");
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
                    if (DecoLimitLifter.EsuModeSwitchHandoff.ConsumeInactiveCleanupFrame())
                        return;

                    AutomationBuilderInputScope.ForceResetIfActive("no active automation builder session");
                    DecoLimitLifter.EsuInputFocusGuard.TickPostExitRepair(
                        "Automation Builder inactive");
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
                EsuRuntimeLog.Exception("Automation Builder", exception, "Automation Builder update failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Automation Builder update failed",
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
                EsuRuntimeLog.Exception("Automation Builder", exception, "Automation Builder GUI failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Automation Builder GUI failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                Close();
            }
        }

        private void Open(bool modeSwitch = false)
        {
            ClearModeSwitchHandoffGui();
            cBuild build = cBuild.GetSingleton();
            _session = new AutomationBuilderSession(build);
            _session.Begin();
            EsuRuntimeLog.Info("Automation Builder", modeSwitch ? "Automation Builder opened from mode switch." : "Automation Builder opened.");
            if (!modeSwitch)
                InfoStore.Add("Automation Builder opened. Pick a breadboard on the right, place it, then link blocks or open its graph.");
        }

        private void Close(
            string reason = null,
            bool notifyClose = true,
            bool preserveSharedHud = false,
            bool keepModeSwitchHandoffGui = false)
        {
            AutomationBuilderSession session = _session;
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

            AutomationBuilderInputScope.ForceResetIfActive("automation builder closed");
            DecoLimitLifter.EsuHudDiagnostics.WarnIfEditorScopeActive(
                "automation builder closed",
                "Automation Builder",
                () => AutomationBuilderInputScope.Active);

            if (notifyClose)
            {
                EsuRuntimeLog.Info(
                    "Automation Builder",
                    string.IsNullOrWhiteSpace(reason)
                        ? "Automation Builder closed."
                        : "Automation Builder closed: " + reason + ".");
                InfoStore.Add(string.IsNullOrWhiteSpace(reason)
                    ? "Automation Builder closed."
                    : "Automation Builder closed: " + reason + ".");
            }
        }

        private void ClearModeSwitchHandoffGui()
        {
            AutomationBuilderSession session = _handoffGuiSession;
            _handoffGuiSession = null;
            _handoffGuiFrame = -1;
            session?.End(preserveSharedHud: true);
            AutomationBuilderInputScope.ForceResetIfActive("automation builder handoff gui cleared");
            DecoLimitLifter.EsuHudDiagnostics.WarnIfEditorScopeActive(
                "automation builder handoff gui cleared",
                "Automation Builder",
                () => AutomationBuilderInputScope.Active);
        }

        private void OnDisable()
        {
            if (!Active)
                AutomationBuilderInputScope.ForceResetIfActive("automation builder behaviour disabled");
        }

        private void OnDestroy()
        {
            AutomationBuilderInputScope.ForceResetIfActive("automation builder behaviour destroyed");
        }
    }
}
