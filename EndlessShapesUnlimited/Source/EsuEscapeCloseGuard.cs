using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuEscapeCloseGuard
    {
        private static int s_suppressInputUntilFrame = -1;

        internal static bool Active =>
            s_suppressInputUntilFrame >= Time.frameCount;

        internal static void Arm(int frames = 2)
        {
            int until = Time.frameCount + Mathf.Max(1, frames);
            if (until > s_suppressInputUntilFrame)
                s_suppressInputUntilFrame = until;
        }
    }
}
