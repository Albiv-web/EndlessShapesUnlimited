using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;

namespace DecoLimitLifter.Patches
{
    // This is deliberately not a Harmony patch. The game limit is one static
    // field initialized when the type loads, so setting it once after PatchAll
    // is sufficient and avoids patching several hot decoration methods.
    internal static class DecoLimitsPatch
    {
        internal static int ApplyLimit()
        {
            int previous = AllConstructDecorations._limitPerPacketManager;
            if (AllConstructDecorations._limitPerPacketManager < DecoLimits.MaxDecorations)
                AllConstructDecorations._limitPerPacketManager = DecoLimits.MaxDecorations;
            return previous;
        }

        internal static void RestoreLimit(int previous)
        {
            AllConstructDecorations._limitPerPacketManager = previous;
        }
    }
}
