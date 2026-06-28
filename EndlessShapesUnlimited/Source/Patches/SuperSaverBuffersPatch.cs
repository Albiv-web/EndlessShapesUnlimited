using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch]
    internal static class SuperSaverBuffersPatch
    {
        private static readonly object InitLock = new object();

        internal static void OnBootEnsurePools() => EnsurePools();

        private static void EnsurePools()
        {
            lock (InitLock)
            {
                var data = SuperSaverReusableByteArray.DataSorted;
                SuperSaverReusableByteArray.DataSorted = BufferGrowth.GrowPreserving(
                    data,
                    DecoLimits.VanillaDataSortedBytes,
                    DecoLimits.MaxDataSortedBytes,
                    data?.Length ?? 0,
                    "SuperSaver reusable data pool",
                    exact: true);

                var header = SuperSaverReusableByteArray.Header;
                SuperSaverReusableByteArray.Header = BufferGrowth.GrowPreserving(
                    header,
                    DecoLimits.VanillaHeaderBytes,
                    DecoLimits.MaxHeaderBytes,
                    header?.Length ?? 0,
                    "SuperSaver reusable header pool",
                    exact: true);

            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperSaver), MethodType.Constructor)]
        private static void ConstructorPrefix() => EnsurePools();
    }
}
