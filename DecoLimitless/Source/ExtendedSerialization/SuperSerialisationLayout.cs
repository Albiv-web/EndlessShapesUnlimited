using System;

namespace DecoLimitLifter.ExtendedSerialization
{
    internal enum SuperContainerFormat
    {
        Legacy,
        Sentinel
    }

    internal readonly struct SuperSerialisationLayout
    {
        internal const ushort Sentinel = ushort.MaxValue;
        internal const uint ChunkSize = ushort.MaxValue;
        internal const int MaximumLegacyChunks = 100;
        internal const uint MaximumLegacyDataBytes = ChunkSize * MaximumLegacyChunks;

        internal SuperContainerFormat Format { get; }
        internal uint HeaderBytes { get; }
        internal uint DataBytes { get; }
        internal int LegacyLengthPieces { get; }
        internal int MetadataBytes { get; }
        internal int TotalBytes { get; }

        private SuperSerialisationLayout(
            SuperContainerFormat format,
            uint headerBytes,
            uint dataBytes,
            int legacyLengthPieces,
            int metadataBytes,
            int totalBytes)
        {
            Format = format;
            HeaderBytes = headerBytes;
            DataBytes = dataBytes;
            LegacyLengthPieces = legacyLengthPieces;
            MetadataBytes = metadataBytes;
            TotalBytes = totalBytes;
        }

        internal static SuperSerialisationLayout Create(uint headerCount, uint dataBytes, byte objectIdBytes)
        {
            if (objectIdBytes < 1 || objectIdBytes > 8)
                throw new ArgumentOutOfRangeException(nameof(objectIdBytes), "Object IDs must use between one and eight bytes.");

            ulong headerLength = (ulong)headerCount * 7UL;
            if (headerLength > DecoLimits.MaxHeaderBytes)
                throw new InvalidOperationException(
                    $"SuperSaver header requires {headerLength} bytes, above the {DecoLimits.MaxHeaderBytes}-byte limit.");
            if (dataBytes > DecoLimits.MaxDataSortedBytes)
                throw new InvalidOperationException(
                    $"SuperSaver data requires {dataBytes} bytes, above the {DecoLimits.MaxDataSortedBytes}-byte limit.");

            bool legacy = headerLength <= ushort.MaxValue && dataBytes <= MaximumLegacyDataBytes;
            int pieces = legacy ? GetLegacyLengthPieceCount(dataBytes) : 0;
            int metadataBytes = legacy ? 4 + pieces * 2 : 10;

            ulong total = (ulong)objectIdBytes + (ulong)metadataBytes + headerLength + dataBytes;
            if (total > int.MaxValue)
                throw new InvalidOperationException("Serialized SuperSaver payload exceeds the CLR array limit.");

            return new SuperSerialisationLayout(
                legacy ? SuperContainerFormat.Legacy : SuperContainerFormat.Sentinel,
                (uint)headerLength,
                dataBytes,
                pieces,
                metadataBytes,
                (int)total);
        }

        internal static void ValidateObjectId(ulong objectId, byte objectIdBytes)
        {
            if (objectIdBytes < 1 || objectIdBytes > 8)
                throw new ArgumentOutOfRangeException(nameof(objectIdBytes), "Object IDs must use between one and eight bytes.");

            if (objectIdBytes == 8)
                return;

            ulong maximum = (1UL << (objectIdBytes * 8)) - 1UL;
            if (objectId > maximum)
                throw new ArgumentException(
                    $"{objectIdBytes} bytes cannot represent object ID {objectId}.",
                    nameof(objectId));
        }

        private static int GetLegacyLengthPieceCount(uint dataBytes)
        {
            if (dataBytes == MaximumLegacyDataBytes)
                return MaximumLegacyChunks;

            // This also accounts for the zero terminator needed by zero and by
            // exact positive multiples of 65,535 below the 100-piece ceiling.
            return checked((int)(dataBytes / ChunkSize) + 1);
        }
    }
}
