using System;
using System.Linq;
using System.Reflection;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.DataManagement.Serialisation.VariableTypes;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch(typeof(SuperSaver), nameof(SuperSaver.WriteHeader))]
    internal static class SuperSaver_WriteHeader_Guard
    {
        [HarmonyPrefix]
        private static void Prefix(SuperSaver __instance)
        {
            ulong usedBytes = (ulong)__instance.HeaderCount * 7UL;
            ulong requiredBytes = usedBytes + 7UL;
            if (requiredBytes > int.MaxValue)
                throw new InvalidOperationException("SuperSaver header size exceeds the CLR array limit.");

            byte[] previous = __instance.Header;
            byte[] grown = BufferGrowth.GrowPreserving(
                previous,
                (int)requiredBytes,
                DecoLimits.MaxHeaderBytes,
                (int)usedBytes,
                "SuperSaver.Header");

            if (!ReferenceEquals(previous, grown))
            {
                __instance.Header = grown;
                if (ReferenceEquals(previous, SuperSaverReusableByteArray.Header))
                    SuperSaverReusableByteArray.Header = grown;
            }
        }
    }

    [HarmonyPatch]
    internal static class SuperSaver_ByIdHelpWrite_Guard
    {
        private static MethodBase TargetMethod()
        {
            var saverType = typeof(SuperSaver);
            var map = saverType.GetInterfaceMap(typeof(IVariableWriteHelp));

            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                var interfaceMethod = map.InterfaceMethods[i];
                var parameters = interfaceMethod.GetParameters();
                if (interfaceMethod.Name == nameof(IVariableWriteHelp.ByIdHelpWrite) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(uint) &&
                    parameters[1].ParameterType == typeof(uint))
                    return map.TargetMethods[i];
            }

            var fallback = AccessTools.GetDeclaredMethods(saverType).FirstOrDefault(method =>
            {
                if (!method.Name.Contains(nameof(IVariableWriteHelp.ByIdHelpWrite)))
                    return false;
                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(uint) &&
                       parameters[1].ParameterType == typeof(uint);
            });

            return fallback ?? throw new MissingMethodException(
                "Could not locate SuperSaver's IVariableWriteHelp.ByIdHelpWrite implementation.");
        }

        [HarmonyPrefix]
        private static void Prefix(SuperSaver __instance, uint dataSize)
        {
            uint payloadBytes = Math.Min(255U, dataSize);
            ulong usedBytes = __instance._datasWrittenSorted;
            ulong requiredBytes = usedBytes + 3UL + payloadBytes;
            if (requiredBytes > int.MaxValue)
                throw new InvalidOperationException("SuperSaver data size exceeds the CLR array limit.");

            byte[] previous = __instance.DataSorted;
            byte[] grown = BufferGrowth.GrowPreserving(
                previous,
                (int)requiredBytes,
                DecoLimits.MaxDataSortedBytes,
                (int)usedBytes,
                "SuperSaver.DataSorted");

            if (!ReferenceEquals(previous, grown))
            {
                __instance.DataSorted = grown;
                if (ReferenceEquals(previous, SuperSaverReusableByteArray.DataSorted))
                    SuperSaverReusableByteArray.DataSorted = grown;
            }
        }
    }
}
