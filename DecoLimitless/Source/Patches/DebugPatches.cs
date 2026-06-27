using System.Reflection;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Core.Serialisation.Bytes;
using HarmonyLib;
#if DCL_DEBUG
namespace DecoLimitLifter.Patches
{
    // Logs game selects a header/segment.
    [HarmonyPatch(typeof(SuperLoader), nameof(SuperLoader.FindHeader))]
    internal static class SuperLoader_FindHeader_Logger
    {
        static void Postfix(SuperLoader __instance, uint i, ref uint length, bool __result)
        {
            uint hc = __instance.HeaderCount;
            uint tot = (uint)typeof(SuperLoader).GetField("_totalDataLengthSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint dw = (uint)typeof(SuperLoader).GetField("_datasWrittenSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint rl = (uint)typeof(SuperLoader).GetField("_readerLengthOfSortedSegment", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);

            AdvLogger.LogError(
                $"[DecoLimitLifter] FindHeader(i={i}) => {__result} len={length} | HC={hc} total={tot} written={dw} readerLen={rl}",
                LogOptions._AlertDevInGame);
        }
    }

    // Logs success/failure of direct id/length lookups.
    [HarmonyPatch(typeof(SuperLoader), nameof(SuperLoader.ByIdHelpRead))]
    internal static class SuperLoader_ByIdHelpRead_Logger
    {
        static void Prefix(SuperLoader __instance, uint id, uint lengthLookingFor)
        {
            AdvLogger.LogError($"[DecoLimitLifter] ByIdHelpRead(id={id}, len={lengthLookingFor}) — scanning…",
                LogOptions._AlertDevInGame);
        }

        static void Postfix(SuperLoader __instance, bool __result)
        {
            uint dw = (uint)typeof(SuperLoader).GetField("_datasWrittenSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint rl = (uint)typeof(SuperLoader).GetField("_readerLengthOfSortedSegment", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);
            AdvLogger.LogError($"[DecoLimitLifter] ByIdHelpRead => {(__result ? "FOUND" : "MISS")} | start={dw} len={rl}",
                LogOptions._AlertDevInGame);
        }
    }
}
#endif