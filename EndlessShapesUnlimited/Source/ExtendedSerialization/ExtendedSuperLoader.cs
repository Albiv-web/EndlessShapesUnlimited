using System;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.Serialisation;
using DecoLimitLifter.Patches;
using DecoLimitLifter.SerializationHud;

namespace DecoLimitLifter.ExtendedSerialization
{
    public static class ExtendedSuperLoader
    {
        public static ulong Deserialise(
            SuperLoader self,
            byte[] packet,
            ref uint startFrom,
            byte objectIdBytes)
        {
            if (self == null)
                throw new ArgumentNullException(nameof(self));
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));
            if (objectIdBytes < 1 || objectIdBytes > 8)
                throw new ArgumentOutOfRangeException(nameof(objectIdBytes), "Object IDs must use between one and eight bytes.");

            uint containerStart = startFrom;
            uint cursor = startFrom;
            Require(packet, cursor, objectIdBytes, "object ID");
            ulong objectId = ByteConversion.ConvertOutAnUnsignedInt(packet, cursor, objectIdBytes);
            cursor += objectIdBytes;

            Require(packet, cursor, 2U, "container marker/header length");
            uint marker = ReadUInt(packet, ref cursor, 2);

            uint headerBytes;
            uint dataBytes;
            bool sentinel = marker == SuperSerialisationLayout.Sentinel;
            if (sentinel)
            {
                Require(packet, cursor, 8U, "sentinel lengths");
                headerBytes = ReadUInt(packet, ref cursor, 4);
                dataBytes = ReadUInt(packet, ref cursor, 4);
            }
            else
            {
                headerBytes = marker;
                Require(packet, cursor, 2U, "legacy reserved field");
                ReadUInt(packet, ref cursor, 2);
                dataBytes = ReadLegacyDataLength(packet, ref cursor);
            }

            ValidateLengths(headerBytes, dataBytes);
            ulong payloadBytes = (ulong)headerBytes + dataBytes;
            Require(packet, cursor, payloadBytes, "header and data payload");
            ValidateHeaderTable(packet, cursor, headerBytes, dataBytes);

            byte[] previousHeader = self.Header;
            byte[] previousData = self.DataSorted;
            byte[] header = EnsureBuffer(previousHeader, headerBytes, DecoLimits.MaxHeaderBytes, "SuperLoader.Header");
            byte[] data = EnsureBuffer(previousData, dataBytes, DecoLimits.MaxDataSortedBytes, "SuperLoader.DataSorted");

            if (!ReferenceEquals(previousHeader, header) &&
                ReferenceEquals(previousHeader, SuperSaverReusableByteArray.Header))
                SuperSaverReusableByteArray.Header = header;
            if (!ReferenceEquals(previousData, data) &&
                ReferenceEquals(previousData, SuperSaverReusableByteArray.DataSorted))
                SuperSaverReusableByteArray.DataSorted = data;

            if (headerBytes > 0U)
                Buffer.BlockCopy(packet, checked((int)cursor), header, 0, checked((int)headerBytes));
            cursor += headerBytes;

            if (dataBytes > 0U)
                Buffer.BlockCopy(packet, checked((int)cursor), data, 0, checked((int)dataBytes));
            cursor += dataBytes;

            self.Id = objectId;
            self.Header = header;
            self.DataSorted = data;
            Priv.SetHeaderCount(self, headerBytes / 7U);
            Priv.SetTotalDataLength(self, dataBytes);
            Priv.SetDataCursor(self, 0U);
            Priv.SetReaderLength(self, headerBytes == 0U
                ? dataBytes
                : ByteConversion.ConvertOutLegacyElements(header, 3U));

            startFrom = cursor;

            if (DclDebug.Enabled)
            {
                DclDebug.Log(
                    $"Loader path={(sentinel ? "SENTINEL" : "LEGACY")} id={objectId} " +
                    $"headers={headerBytes / 7U} data={dataBytes} next={startFrom}");
            }

            SerializationTelemetry.RecordLoadedContainer(
                self,
                sentinel,
                headerBytes,
                dataBytes,
                cursor - containerStart);

            return objectId;
        }

        private static uint ReadLegacyDataLength(byte[] packet, ref uint cursor)
        {
            ulong total = 0UL;
            for (int i = 0; i < SuperSerialisationLayout.MaximumLegacyChunks; i++)
            {
                Require(packet, cursor, 2U, "legacy data-length piece");
                uint piece = ReadUInt(packet, ref cursor, 2);
                total += piece;
                if (total > SuperSerialisationLayout.MaximumLegacyDataBytes)
                    throw new FormatException("Legacy data length exceeds the 100-piece format limit.");
                if (piece < SuperSerialisationLayout.ChunkSize)
                    return (uint)total;
            }

            if (total != SuperSerialisationLayout.MaximumLegacyDataBytes)
                throw new FormatException("Legacy data length did not terminate within 100 pieces.");
            return (uint)total;
        }

        private static void ValidateLengths(uint headerBytes, uint dataBytes)
        {
            if (headerBytes % 7U != 0U)
                throw new FormatException($"Header byte length {headerBytes} is not divisible by seven.");
            if (headerBytes > DecoLimits.MaxHeaderBytes)
                throw new FormatException(
                    $"Header byte length {headerBytes} exceeds the {DecoLimits.MaxHeaderBytes}-byte limit.");
            if (dataBytes > DecoLimits.MaxDataSortedBytes)
                throw new FormatException(
                    $"Data byte length {dataBytes} exceeds the {DecoLimits.MaxDataSortedBytes}-byte limit.");
        }

        private static void ValidateHeaderTable(
            byte[] packet,
            uint headerStart,
            uint headerBytes,
            uint dataBytes)
        {
            uint previousStart = 0U;
            uint headerCount = headerBytes / 7U;
            for (uint i = 0U; i < headerCount; i++)
            {
                uint offset = headerStart + i * 7U + 3U;
                uint sortedStart = ByteConversion.ConvertOutLegacyElements(packet, offset);
                if (sortedStart > dataBytes)
                    throw new FormatException(
                        $"Header {i} starts at data offset {sortedStart}, beyond data length {dataBytes}.");
                if (i > 0U && sortedStart < previousStart)
                    throw new FormatException(
                        $"Header {i} starts before the preceding header segment.");
                previousStart = sortedStart;
            }
        }

        private static byte[] EnsureBuffer(byte[] current, uint required, int maximum, string name)
        {
            if (required == 0U)
                return current ?? Array.Empty<byte>();
            if (required > maximum)
                throw new FormatException($"{name} requires {required} bytes, above the configured maximum.");
            if (current != null && (uint)current.Length >= required)
                return current;

            int capacity = BufferGrowth.CapacityFor(checked((int)required), maximum);
            return new byte[capacity];
        }

        private static uint ReadUInt(byte[] packet, ref uint cursor, byte bytes)
        {
            uint value = ByteConversion.ConvertOut(packet, cursor, bytes);
            cursor += bytes;
            return value;
        }

        private static void Require(byte[] packet, uint cursor, ulong bytes, string section)
        {
            ulong end = (ulong)cursor + bytes;
            if (end > (ulong)packet.Length)
                throw new FormatException(
                    $"Truncated SuperLoader packet while reading {section}: need through byte {end}, have {packet.Length}.");
        }
    }
}
