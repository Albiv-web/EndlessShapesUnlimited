using DecoLimitLifter.AutomationBuilderMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuEditorScope
    {
        private static int _guiLeaseUntilFrame = -1;
        private static string _guiLeaseOwner = "(none)";

        internal static bool AnyInputScopeActive =>
            DecorationEditorInputScope.Active ||
            SmartBuildInputScope.Active ||
            AutomationBuilderInputScope.Active;

        internal static bool AnyRegisteredEditorActive =>
            DecorationEditModeRegistration.Active ||
            SmartBuildModeRegistration.Active ||
            AutomationBuilderModeRegistration.Active;

        internal static bool GuiLeaseActive =>
            Time.frameCount <= _guiLeaseUntilFrame;

        internal static bool AnyEditorActive =>
            AnyInputScopeActive ||
            AnyRegisteredEditorActive ||
            GuiLeaseActive;

        internal static bool ShouldHideVanillaHud =>
            AnyEditorActive ||
            EsuModeSwitchHandoff.Active;

        internal static string Status =>
            "editor=" + CurrentEditorName +
            "\ndecoration_editor_active=" + ((DecorationEditorInputScope.Active || DecorationEditModeRegistration.Active) ? "true" : "false") +
            "\nsmart_builder_active=" + ((SmartBuildInputScope.Active || SmartBuildModeRegistration.Active) ? "true" : "false") +
            "\nautomation_builder_active=" + ((AutomationBuilderInputScope.Active || AutomationBuilderModeRegistration.Active) ? "true" : "false") +
            "\ndecoration_input_scope_active=" + (DecorationEditorInputScope.Active ? "true" : "false") +
            "\nsmart_input_scope_active=" + (SmartBuildInputScope.Active ? "true" : "false") +
            "\nautomation_input_scope_active=" + (AutomationBuilderInputScope.Active ? "true" : "false") +
            "\ndecoration_registration_active=" + (DecorationEditModeRegistration.Active ? "true" : "false") +
            "\nsmart_registration_active=" + (SmartBuildModeRegistration.Active ? "true" : "false") +
            "\nautomation_registration_active=" + (AutomationBuilderModeRegistration.Active ? "true" : "false") +
            "\ngui_lease_active=" + (GuiLeaseActive ? "true" : "false") +
            "\ngui_lease_owner=" + _guiLeaseOwner +
            "\ngui_lease_until_frame=" + _guiLeaseUntilFrame.ToString() +
            "\ncurrent_frame=" + Time.frameCount.ToString() +
            "\nmode_switch_handoff_active=" + (EsuModeSwitchHandoff.Active ? "true" : "false") +
            "\nshould_hide_vanilla_hud=" + (ShouldHideVanillaHud ? "true" : "false");

        internal static void ClaimGuiOwnership(string owner, int frames = 2)
        {
            int until = Time.frameCount + Mathf.Max(1, frames);
            if (until >= _guiLeaseUntilFrame)
            {
                _guiLeaseUntilFrame = until;
                _guiLeaseOwner = SafeOwner(owner);
            }
        }

        internal static string CurrentEditorName
        {
            get
            {
                if (AutomationBuilderInputScope.Active || AutomationBuilderModeRegistration.Active)
                    return "Automation Builder";
                if (SmartBuildInputScope.Active || SmartBuildModeRegistration.Active)
                    return "Smart Builder";
                if (DecorationEditorInputScope.Active || DecorationEditModeRegistration.Active)
                    return "Decoration Edit";
                if (GuiLeaseActive)
                    return _guiLeaseOwner;
                if (EsuModeSwitchHandoff.Active)
                    return "Mode Switch";
                return "none";
            }
        }

        private static string SafeOwner(string owner) =>
            string.IsNullOrWhiteSpace(owner)
                ? "unknown"
                : owner.Trim();
    }
}
