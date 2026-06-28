using System.Reflection;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;
#if DCL_DEBUG
namespace DecoLimitLifter.Patches
{
    // Unique class name to avoid collisions 
    [HarmonyPatch(typeof(SuperLoader), nameof(SuperLoader.FindHeader))]
    internal static class SuperLoader_FindHeader_Logger2
    {
        static void Postfix(SuperLoader __instance, uint i, ref uint length, bool __result)
        {
            var t = typeof(SuperLoader);
            uint tot = (uint)t.GetField("_totalDataLengthSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint dw = (uint)t.GetField("_datasWrittenSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint rl = (uint)t.GetField("_readerLengthOfSortedSegment", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);
            uint hc = __instance.HeaderCount;

            // Basic header/segment info
            AdvLogger.LogError(
                $"[DecoLimitLifter] FindHeader(i={i}) => {__result}  segLen={length} | HC={hc} total={tot} start={dw} len={rl}",
                LogOptions._AlertDevAndCustomerInGame
            );

            if (!__result) return;

            // --- Dump first few (id,len) pairs from the selected segment ---
            // Segment layout: [UInt16 id][Byte len][<len> bytes payload] repeated
            var data = (byte[])t.GetField("DataSorted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(__instance);
            uint pos = dw;
            uint end = dw + rl;

            int printed = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
            sb.Append("[DecoLimitLifter] seg dump: ");

            while (pos + 3 <= end && printed < 10) // up to 10 entries to keep spam low
            {
                uint id = ByteConversion.ConvertOut(data, pos, 2); pos += 2;
                uint len = ByteConversion.ConvertOut(data, pos, 1); pos += 1;

                sb.Append(id);
                sb.Append('(').Append(len).Append(')');
                sb.Append(' ');

                if (pos + len > end) { sb.Append("…truncated"); break; }
                pos += len;
                printed++;
            }

            AdvLogger.LogError(sb.ToString(), LogOptions._AlertDevAndCustomerInGame);
        }
    }
}
#endif