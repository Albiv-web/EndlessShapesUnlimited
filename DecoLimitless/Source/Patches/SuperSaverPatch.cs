using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch(typeof(SuperSaver), nameof(SuperSaver.Serialise))]
    internal static class SuperSaver_Serialise_Patch
    {
        static bool Prefix(SuperSaver __instance, byte[] list, ref uint startByte, ulong objectId, byte bytesToWrite)
        {
            ExtendedSerialization.ExtendedSuperSaver.Serialise(__instance, list, ref startByte, objectId, bytesToWrite);
            return false; // skip original
        }
    }
}
