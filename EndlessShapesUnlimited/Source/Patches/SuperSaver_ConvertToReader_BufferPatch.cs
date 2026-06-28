using System;
using BrilliantSkies.DataManagement.Serialisation;
using DecoLimitLifter.ExtendedSerialization;
using HarmonyLib;

namespace DecoLimitLifter.Patches
{
    [HarmonyPatch(typeof(SuperSaver), nameof(SuperSaver.ConvertToReader))]
    internal static class SuperSaver_ConvertToReader_BufferPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SuperSaver __instance, ref SuperLoader __result)
        {
            var layout = SuperSerialisationLayout.Create(
                __instance.HeaderCount,
                __instance._datasWrittenSorted,
                objectIdBytes: 1);

            const int minimumBufferBytes = 64 * 1024;
            int size = Math.Max(layout.TotalBytes, minimumBufferBytes);
            var buffer = new byte[size];
            uint cursor = 0U;

            __instance.Serialise(buffer, ref cursor, 0UL, 1);
            if (cursor != (uint)layout.TotalBytes)
                throw new InvalidOperationException(
                    $"ConvertToReader expected {layout.TotalBytes} bytes, serializer wrote {cursor}.");

            cursor = 0U;
            var loader = new SuperLoader();
            loader.Deserialise(buffer, ref cursor, 1);
            if (loader.Id != 0UL)
                throw new InvalidOperationException($"ConvertToReader expected object ID 0, got {loader.Id}.");

            __result = loader;
            return false;
        }
    }
}
