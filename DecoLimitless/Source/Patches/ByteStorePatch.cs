using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch]
    internal static class ByteStorePatch
    {
        internal static void EnsureMegaBytes()
        {
            int minimum = DecoLimits.SaveBufferBytes;
            if (minimum < DecoLimits.InitialSaveBufferBytes)
                minimum = DecoLimits.InitialSaveBufferBytes;

            var current = ByteStore.MegaBytes;
            int preserve = current?.Length ?? 0;
            ByteStore.MegaBytes = BufferGrowth.GrowPreserving(
                current,
                minimum,
                DecoLimits.MaxSaveBufferBytes,
                preserve,
                "ByteStore.MegaBytes",
                exact: true);
        }

        // Covers any saver constructed before or outside the normal plugin load path.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperSaver), MethodType.Constructor)]
        private static void AfterSuperSaverConstructor() => EnsureMegaBytes();
    }
}
