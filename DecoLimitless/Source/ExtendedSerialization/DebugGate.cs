using BrilliantSkies.Core.Logger;

namespace DecoLimitLifter
{
    /// <summary>
    /// Flip Enabled to true for ingame debug popups.
    /// Leave false for silent. Not const to avoid CS0162 warnings.
    /// </summary>
    internal static class DclDebug
    {
        public static bool Enabled { get; set; } = false;

        public static void Log(string msg)
        {
            if (!Enabled) return;
            AdvLogger.LogError("[DecoLimitLifter] " + msg, LogOptions._AlertDevInGame);
        }

        public static void LogDevOnly(string msg)
        {
            if (!Enabled) return;
            AdvLogger.LogError("[DecoLimitLifter] " + msg, LogOptions._AlertDevInGame);
        }
    }
}
