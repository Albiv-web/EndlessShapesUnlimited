using System;
using System.Collections.Generic;
using DecoLimitLifter.ExtendedSerialization;

namespace DecoLimitLifter.SerializationHud
{
    internal sealed class BlueprintSerializationUsage
    {
        internal static readonly BlueprintSerializationUsage Empty =
            new BlueprintSerializationUsage(
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0,
                0,
                0,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0);

        internal BlueprintSerializationUsage(
            ulong blockDataBytes,
            ulong vehicleDataBytes,
            ulong totalWireBytes,
            ulong totalHeaderBytes,
            ulong totalDataBytes,
            ulong peakHeaderBytes,
            ulong peakDataBytes,
            int containerCount,
            int sentinelContainerCount,
            int malformedContainerCount,
            ulong peakContainerWireBytes,
            ulong largestStreamBytes,
            ulong blockIdEntries,
            ulong structuralEntries,
            ulong totalBlockCount,
            ulong subConstructCount,
            int recursionLimitHits)
        {
            BlockDataBytes = blockDataBytes;
            VehicleDataBytes = vehicleDataBytes;
            TotalWireBytes = totalWireBytes;
            TotalHeaderBytes = totalHeaderBytes;
            TotalDataBytes = totalDataBytes;
            PeakHeaderBytes = peakHeaderBytes;
            PeakDataBytes = peakDataBytes;
            ContainerCount = containerCount;
            SentinelContainerCount = sentinelContainerCount;
            MalformedContainerCount = malformedContainerCount;
            PeakContainerWireBytes = peakContainerWireBytes;
            LargestStreamBytes = largestStreamBytes;
            BlockIdEntries = blockIdEntries;
            StructuralEntries = structuralEntries;
            TotalBlockCount = totalBlockCount;
            SubConstructCount = subConstructCount;
            RecursionLimitHits = recursionLimitHits;
        }

        internal ulong BlockDataBytes { get; }
        internal ulong VehicleDataBytes { get; }
        internal ulong PayloadBytes => SaturatingAdd(BlockDataBytes, VehicleDataBytes);
        internal ulong TotalWireBytes { get; }
        internal ulong TotalHeaderBytes { get; }
        internal ulong TotalDataBytes { get; }
        internal ulong PeakHeaderBytes { get; }
        internal ulong PeakDataBytes { get; }
        internal int ContainerCount { get; }
        internal int SentinelContainerCount { get; }
        internal int MalformedContainerCount { get; }
        internal ulong PeakContainerWireBytes { get; }
        internal ulong LargestStreamBytes { get; }
        internal ulong BlockIdEntries { get; }
        internal ulong StructuralEntries { get; }
        internal ulong TotalBlockCount { get; }
        internal ulong SubConstructCount { get; }
        internal int RecursionLimitHits { get; }

        internal bool RequiresModBuffer =>
            LargestStreamBytes > (ulong)DecoLimits.VanillaSaveBufferBytes;

        internal bool AggregatePayloadExceedsVanillaBuffer =>
            PayloadBytes > (ulong)DecoLimits.VanillaSaveBufferBytes;

        internal bool HasData =>
            PayloadBytes > 0UL ||
            ContainerCount > 0 ||
            BlockIdEntries > 0UL ||
            TotalBlockCount > 0UL;

        private static ulong SaturatingAdd(ulong left, ulong right) =>
            ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    }

    internal static class BlueprintSerializationUsageAnalyzer
    {
        private const int MaximumBlueprintDepth = 256;

        internal static BlueprintSerializationUsage Analyze(global::Blueprint blueprint)
        {
            if (blueprint == null)
                return BlueprintSerializationUsage.Empty;

            var builder = new Builder();
            builder.AddBlueprint(blueprint, 0);
            return builder.ToUsage();
        }

        private sealed class Builder
        {
            private ulong _blockDataBytes;
            private ulong _vehicleDataBytes;
            private ulong _totalWireBytes;
            private ulong _totalHeaderBytes;
            private ulong _totalDataBytes;
            private ulong _peakHeaderBytes;
            private ulong _peakDataBytes;
            private int _containerCount;
            private int _sentinelContainerCount;
            private int _malformedContainerCount;
            private ulong _peakContainerWireBytes;
            private ulong _largestStreamBytes;
            private ulong _blockIdEntries;
            private ulong _structuralEntries;
            private ulong _totalBlockCount;
            private ulong _subConstructCount;
            private int _recursionLimitHits;

            internal void AddBlueprint(global::Blueprint blueprint, int depth)
            {
                if (blueprint == null)
                    return;
                if (depth > MaximumBlueprintDepth)
                {
                    _recursionLimitHits++;
                    return;
                }

                _totalBlockCount = SaturatingAdd(
                    _totalBlockCount,
                    (ulong)Math.Max(0, blueprint.TotalBlockCount));
                _blockIdEntries = SaturatingAdd(
                    _blockIdEntries,
                    LengthOf(blueprint.BlockIds));
                _structuralEntries = SaturatingAdd(
                    _structuralEntries,
                    StructuralEntryCount(blueprint));

                AddStream(blueprint.BlockData, 3, blockData: true);
                AddStream(blueprint.VehicleData, 8, blockData: false);

                List<global::Blueprint> children = blueprint.SCs;
                if (children == null)
                    return;

                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i] == null)
                        continue;
                    _subConstructCount++;
                    AddBlueprint(children[i], depth + 1);
                }
            }

            internal BlueprintSerializationUsage ToUsage() =>
                new BlueprintSerializationUsage(
                    _blockDataBytes,
                    _vehicleDataBytes,
                    _totalWireBytes,
                    _totalHeaderBytes,
                    _totalDataBytes,
                    _peakHeaderBytes,
                    _peakDataBytes,
                    _containerCount,
                    _sentinelContainerCount,
                    _malformedContainerCount,
                    _peakContainerWireBytes,
                    _largestStreamBytes,
                    _blockIdEntries,
                    _structuralEntries,
                    _totalBlockCount,
                    _subConstructCount,
                    _recursionLimitHits);

            private void AddStream(byte[] bytes, byte objectIdBytes, bool blockData)
            {
                if (bytes == null || bytes.Length == 0)
                    return;

                ulong length = (ulong)bytes.LongLength;
                if (blockData)
                    _blockDataBytes = SaturatingAdd(_blockDataBytes, length);
                else
                    _vehicleDataBytes = SaturatingAdd(_vehicleDataBytes, length);
                _largestStreamBytes = Math.Max(_largestStreamBytes, length);

                ParseContainers(bytes, objectIdBytes);
            }

            private void ParseContainers(byte[] bytes, byte objectIdBytes)
            {
                int cursor = 0;
                while (cursor < bytes.Length)
                {
                    int start = cursor;
                    if (!Require(bytes, cursor, objectIdBytes + 2))
                    {
                        MarkMalformedRemainder(bytes, start);
                        return;
                    }

                    cursor += objectIdBytes;
                    uint marker = ReadUInt(bytes, cursor, 2);
                    cursor += 2;

                    bool sentinel = marker == SuperSerialisationLayout.Sentinel;
                    uint headerBytes;
                    ulong dataBytes;
                    int metadataBytes;
                    if (sentinel)
                    {
                        if (!Require(bytes, cursor, 8))
                        {
                            MarkMalformedRemainder(bytes, start);
                            return;
                        }

                        headerBytes = ReadUInt(bytes, cursor, 4);
                        cursor += 4;
                        dataBytes = ReadUInt(bytes, cursor, 4);
                        cursor += 4;
                        metadataBytes = 10;
                    }
                    else
                    {
                        headerBytes = marker;
                        if (!Require(bytes, cursor, 2))
                        {
                            MarkMalformedRemainder(bytes, start);
                            return;
                        }

                        cursor += 2;
                        dataBytes = 0UL;
                        int pieces = 0;
                        while (true)
                        {
                            if (!Require(bytes, cursor, 2))
                            {
                                MarkMalformedRemainder(bytes, start);
                                return;
                            }

                            uint piece = ReadUInt(bytes, cursor, 2);
                            cursor += 2;
                            pieces++;
                            dataBytes = SaturatingAdd(dataBytes, piece);
                            if (piece < SuperSerialisationLayout.ChunkSize)
                                break;
                            if (pieces > SuperSerialisationLayout.MaximumLegacyChunks)
                            {
                                MarkMalformedRemainder(bytes, start);
                                return;
                            }
                        }
                        metadataBytes = 4 + pieces * 2;
                    }

                    ulong payloadBytes = SaturatingAdd(headerBytes, dataBytes);
                    ulong payloadEnd = SaturatingAdd((ulong)cursor, payloadBytes);
                    if (payloadEnd > (ulong)bytes.Length || headerBytes % 7U != 0U)
                    {
                        MarkMalformedRemainder(bytes, start);
                        return;
                    }

                    ulong wireBytes = SaturatingAdd(
                        (ulong)objectIdBytes + (ulong)metadataBytes,
                        payloadBytes);
                    _containerCount++;
                    if (sentinel)
                        _sentinelContainerCount++;
                    _totalWireBytes = SaturatingAdd(_totalWireBytes, wireBytes);
                    _totalHeaderBytes = SaturatingAdd(_totalHeaderBytes, headerBytes);
                    _totalDataBytes = SaturatingAdd(_totalDataBytes, dataBytes);
                    _peakHeaderBytes = Math.Max(_peakHeaderBytes, headerBytes);
                    _peakDataBytes = Math.Max(_peakDataBytes, dataBytes);
                    _peakContainerWireBytes = Math.Max(_peakContainerWireBytes, wireBytes);

                    cursor = checked((int)payloadEnd);
                }
            }

            private void MarkMalformedRemainder(byte[] bytes, int start)
            {
                _malformedContainerCount++;
                ulong remainder = (ulong)Math.Max(0, bytes.Length - start);
                _totalWireBytes = SaturatingAdd(_totalWireBytes, remainder);
                _peakContainerWireBytes = Math.Max(_peakContainerWireBytes, remainder);
            }

            private static bool Require(byte[] bytes, int cursor, int count) =>
                cursor >= 0 &&
                count >= 0 &&
                cursor <= bytes.Length &&
                count <= bytes.Length - cursor;

            private static uint ReadUInt(byte[] bytes, int offset, byte count)
            {
                uint value = 0U;
                for (int i = 0; i < count; i++)
                    value |= (uint)bytes[offset + i] << (i * 8);
                return value;
            }

            private static ulong StructuralEntryCount(global::Blueprint blueprint) =>
                SaturatingAdd(
                    SaturatingAdd(
                        SaturatingAdd(
                            SaturatingAdd(
                                LengthOf(blueprint.BlockIds),
                                LengthOf(blueprint.BLP)),
                            SaturatingAdd(
                                LengthOf(blueprint.BLR),
                                LengthOf(blueprint.BCI))),
                        SaturatingAdd(
                            SaturatingAdd(
                                LengthOf(blueprint.BP1),
                                LengthOf(blueprint.BP2)),
                            LengthOf(blueprint.BEI))),
                    SaturatingAdd(
                        SaturatingAdd(
                            LengthOf(blueprint.BlockState),
                            LengthOf(blueprint.BlockStringData)),
                        LengthOf(blueprint.BlockStringDataIds)));

            private static ulong LengthOf<T>(T[] array) =>
                array == null ? 0UL : (ulong)array.LongLength;

            private static ulong SaturatingAdd(ulong left, ulong right) =>
                ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
        }
    }
}
