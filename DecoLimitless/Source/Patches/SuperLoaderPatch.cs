using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch(typeof(SuperLoader))]
    internal static class SuperLoader_Deserialise_All_Patch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(SuperLoader)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => method.Name == nameof(SuperLoader.Deserialise))
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType == typeof(byte[]) &&
                           parameters[1].ParameterType == typeof(uint).MakeByRefType() &&
                           parameters[2].ParameterType == typeof(byte) &&
                           method.ReturnType == typeof(ulong);
                });
        }

        [HarmonyPrefix]
        private static bool Prefix(
            SuperLoader __instance,
            [HarmonyArgument(0)] byte[] fullPacket,
            [HarmonyArgument(1)] ref uint startFrom,
            [HarmonyArgument(2)] byte bytesInTheObjectId,
            ref ulong __result)
        {
            __result = ExtendedSerialization.ExtendedSuperLoader.Deserialise(
                __instance,
                fullPacket,
                ref startFrom,
                bytesInTheObjectId);
            return false;
        }
    }
}
