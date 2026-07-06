using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuModeSwitchHandoff
    {
        private const int HandoffFrames = 3;
        private static int s_framesRemaining;
        private static int s_lastConsumedFrame = -1;

        internal static bool Active => s_framesRemaining > 0;

        internal static int PassiveGuiFrames => HandoffFrames;

        internal static void Begin()
        {
            s_framesRemaining = HandoffFrames;
            s_lastConsumedFrame = -1;
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
