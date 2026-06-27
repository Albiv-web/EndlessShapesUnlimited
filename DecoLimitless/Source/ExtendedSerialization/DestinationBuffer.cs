using System;
using System.Reflection;
using BrilliantSkies.DataManagement.Serialisation;
using HarmonyLib;
using DecoLimitLifter.Patches;

namespace DecoLimitLifter.ExtendedSerialization
{
    internal static class DestinationBuffer
    {
        private static readonly FieldInfo AutoSyncBufferField = ResolveAutoSyncBufferField();

        internal static byte[] Ensure(byte[] buffer, uint prefixBytes, int bytesToAppend)
        {
            ulong requiredLong = (ulong)prefixBytes + (ulong)bytesToAppend;
            if (requiredLong > DecoLimits.MaxSaveBufferBytes)
                throw new InvalidOperationException(
                    $"Serialized output requires {requiredLong} bytes, above the {DecoLimits.MaxSaveBufferBytes}-byte limit.");

            int required = (int)requiredLong;
            if (buffer != null && buffer.Length >= required)
                return buffer;

            bool isByteStore = ReferenceEquals(buffer, ByteStore.MegaBytes);
            bool isAutoSync = IsCurrentAutoSyncBuffer(buffer);
            if (!isByteStore && !isAutoSync)
            {
                int available = buffer?.Length ?? 0;
                throw new IndexOutOfRangeException(
                    $"Destination buffer is too small. Need {required} bytes, have {available}. " +
                    "Only FtD's known shared buffers can be replaced safely by the mod.");
            }

            byte[] grown = BufferGrowth.GrowPreserving(
                buffer,
                required,
                DecoLimits.MaxSaveBufferBytes,
                checked((int)prefixBytes),
                isByteStore ? "ByteStore.MegaBytes" : "AutoSyncroniser.fullArray");

            if (isByteStore)
                ByteStore.MegaBytes = grown;
            if (isAutoSync)
                AutoSyncBufferField.SetValue(null, grown);
            return grown;
        }

        private static bool IsCurrentAutoSyncBuffer(byte[] buffer)
        {
            if (AutoSyncBufferField == null)
                return false;
            try
            {
                return ReferenceEquals(buffer, AutoSyncBufferField.GetValue(null));
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo ResolveAutoSyncBufferField()
        {
            try
            {
                var type = AccessTools.TypeByName("BrilliantSkies.DataManagement.Synching.AutoSyncroniser");
                return type == null ? null : AccessTools.Field(type, "fullArray");
            }
            catch
            {
                return null;
            }
        }
    }
}
