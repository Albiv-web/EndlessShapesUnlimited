namespace DecoLimitLifter
{
    internal static class EsuModeSwitchHandoff
    {
        private const int HandoffFrames = 2;
        private static int s_framesRemaining;

        internal static bool Active => s_framesRemaining > 0;

        internal static void Begin()
        {
            s_framesRemaining = HandoffFrames;
        }

        internal static bool ConsumeInactiveCleanupFrame()
        {
            if (s_framesRemaining <= 0)
                return false;

            s_framesRemaining--;
            return true;
        }
    }
}
