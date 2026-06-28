using System;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.Serialisation;
using DecoLimitLifter.SerializationHud;

namespace DecoLimitLifter.ExtendedSerialization
{
    public static class ExtendedSuperSaver
    {
        public static void Serialise(
            SuperSaver self,
            byte[] destination,
            ref uint startByte,
            ulong objectId,
            byte objectIdBytes)
        {
            if (self == null)
                throw new ArgumentNullException(nameof(self));

            SuperSerialisationLayout.ValidateObjectId(objectId, objectIdBytes);
            var layout = SuperSerialisationLayout.Create(
                self.HeaderCount,
                self._datasWrittenSorted,
                objectIdBytes);

            ValidateSourceBuffers(self, layout);
            destination = DestinationBuffer.Ensure(destination, startByte, layout.TotalBytes);
            uint expectedEnd = checked(startByte + (uint)layout.TotalBytes);

            ByteConversion.ConvertInAnUnsignedInt(destination, startByte, objectIdBytes, objectId);
            startByte += objectIdBytes;

            if (layout.Format == SuperContainerFormat.Legacy)
            {
                ByteConversion.ConvertIn(destination, startByte, 2, layout.HeaderBytes);
                startByte += 2U;
                ByteConversion.ConvertIn(destination, startByte, 2, 0U);
                startByte += 2U;
                WriteLegacyDataLength(destination, ref startByte, layout);
            }
            else
            {
                ByteConversion.ConvertIn(destination, startByte, 2, SuperSerialisationLayout.Sentinel);
                startByte += 2U;
                ByteConversion.ConvertIn(destination, startByte, 4, layout.HeaderBytes);
                startByte += 4U;
                ByteConversion.ConvertIn(destination, startByte, 4, layout.DataBytes);
                startByte += 4U;
            }

            Copy(self.Header, layout.HeaderBytes, destination, ref startByte);
            Copy(self.DataSorted, layout.DataBytes, destination, ref startByte);

            if (startByte != expectedEnd)
                throw new InvalidOperationException(
                    $"Internal serializer size mismatch: expected cursor {expectedEnd}, reached {startByte}.");

            SerializationTelemetry.RecordSavedContainer(self, layout);
        }

        private static void ValidateSourceBuffers(SuperSaver self, SuperSerialisationLayout layout)
        {
            int headerCapacity = self.Header?.Length ?? 0;
            if ((uint)headerCapacity < layout.HeaderBytes)
                throw new InvalidOperationException(
                    $"SuperSaver header state is incomplete: {layout.HeaderBytes} bytes declared, {headerCapacity} available.");

            int dataCapacity = self.DataSorted?.Length ?? 0;
            if ((uint)dataCapacity < layout.DataBytes)
                throw new InvalidOperationException(
                    $"SuperSaver data state is incomplete: {layout.DataBytes} bytes declared, {dataCapacity} available.");
        }

        private static void WriteLegacyDataLength(
            byte[] destination,
            ref uint cursor,
            SuperSerialisationLayout layout)
        {
            uint remaining = layout.DataBytes;
            for (int i = 0; i < layout.LegacyLengthPieces; i++)
            {
                uint piece = Math.Min(SuperSerialisationLayout.ChunkSize, remaining);
                ByteConversion.ConvertIn(destination, cursor, 2, piece);
                cursor += 2U;
                remaining -= piece;
            }

            if (remaining != 0U)
                throw new InvalidOperationException("Legacy length encoding did not consume the complete data length.");
        }

        private static void Copy(byte[] source, uint length, byte[] destination, ref uint cursor)
        {
            if (length == 0U)
                return;

            Buffer.BlockCopy(source, 0, destination, checked((int)cursor), checked((int)length));
            cursor += length;
        }
    }
}
