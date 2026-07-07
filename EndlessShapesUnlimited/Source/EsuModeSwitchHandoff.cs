using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuModeSwitchHandoff
    {
        private const int HandoffFrames = 3;
        private static int s_framesRemaining;
        private static int s_lastConsumedFrame = -1;
        private static int s_beginFrame = -1;
        private static bool s_targetEditorClaimed;

        internal static bool Active => s_framesRemaining > 0;

        internal static int PassiveGuiFrames => HandoffFrames;

        internal static bool ShouldDrawPassiveGui() =>
            Active &&
            !s_targetEditorClaimed &&
            s_beginFrame != Time.frameCount &&
            !DecorationEditMode.DecorationEditModeRegistration.Active &&
            !SmartBuildMode.SmartBuildModeRegistration.Active &&
            !AutomationEditMode.AutomationEditModeRegistration.Active;

        internal static void Begin()
        {
            s_framesRemaining = HandoffFrames;
            s_lastConsumedFrame = -1;
            s_beginFrame = Time.frameCount;
            s_targetEditorClaimed = false;
        }

        internal static void ClaimTargetEditorOpened()
        {
            if (s_framesRemaining <= 0)
                return;

            s_targetEditorClaimed = true;
            s_framesRemaining = 0;
        }

        internal static bool ConsumeInactiveCleanupFrame()
        {
            if (s_framesRemaining <= 0)
                return false;

            if (s_lastConsumedFrame != Time.frameCount)
            {
                s_framesRemaining--;
                s_lastConsumedFrame = Time.frameCount;
            }

            return true;
        }
    }
}
