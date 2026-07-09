using DecoLimitLifter.AutomationBuilderMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;

namespace DecoLimitLifter
{
    internal static class EsuEditorScope
    {
        internal static bool AnyEditorActive =>
            DecorationEditorInputScope.Active ||
            SmartBuildInputScope.Active ||
            AutomationBuilderInputScope.Active;

        internal static bool ShouldHideVanillaHud =>
            AnyEditorActive ||
            EsuModeSwitchHandoff.Active;

        internal static string CurrentEditorName
        {
            get
            {
                if (AutomationBuilderInputScope.Active)
                    return "Automation Builder";
                if (SmartBuildInputScope.Active)
                    return "Smart Builder";
                if (DecorationEditorInputScope.Active)
                    return "Decoration Edit";
                if (EsuModeSwitchHandoff.Active)
                    return "Mode Switch";
                return "none";
            }
        }
    }
}
