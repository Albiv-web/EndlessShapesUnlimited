using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.ExtendedSerialization;

namespace DecoLimitLifter.SerializationHud
{
    internal enum SerializationWireFormat
    {
        Unknown,
        Legacy,
        Sentinel,
        OverLimit
    }

    internal enum SerializationOperationKind
    {
        Load,
        Save,
        Measure
    }

    internal sealed class DecorationManagerUsage
    {
        internal DecorationManagerUsage(DecorationManager manager, int count)
        {
            Manager = manager;
            Count = count;
        }

        internal DecorationManager Manager { get; }

        internal int Count { get; }
    }

    internal sealed class DecorationUsageSnapshot
    {
        internal DecorationUsageSnapshot(
            IReadOnlyList<DecorationManagerUsage> managers,
            long totalDecorations,
            int peakManagerDecorations)
        {
            Managers = managers;
            TotalDecorations = totalDecorations;
            PeakManagerDecorations = peakManagerDecorations;
        }

        internal IReadOnlyList<DecorationManagerUsage> Managers { get; }

        internal long TotalDecorations { get; }

        internal int PeakManagerDecorations { get; }

        internal static DecorationUsageSnapshot Capture(MainConstruct mainConstruct)
        {
            var constructs = new List<AllConstruct>();
            mainConstruct?.AllBasicsRestricted?.GetAllConstructsBelowUsAndIncludingUs(constructs);

            var managers = new List<DecorationManagerUsage>(constructs.Count);
            long total = 0L;
            int peak = 0;
            foreach (AllConstruct construct in constructs)
            {
                var decorations = construct?.Decorations as AllConstructDecorations;
                DecorationManager manager = decorations?.Packets;
                if (manager == null)
                    continue;

                int count = Math.Max(0, decorations.DecorationCount);
                managers.Add(new DecorationManagerUsage(manager, count));
                total = Math.Min(long.MaxValue, total + count);
                peak = Math.Max(peak, count);
            }

            return new DecorationUsageSnapshot(managers, total, peak);
        }
    }

    internal sealed class DecorationManagerCalibration
    {
        internal DecorationManagerCalibration(
            DecorationManager manager,
            int decorationCount,
            uint containerHeaderBytes,
            uint containerDataBytes,
            uint contributionHeaderBytes,
            uint contributionDataBytes,
            bool contributionMeasured)
        {
            Manager = manager;
            DecorationCount = decorationCount;
            ContainerHeaderBytes = containerHeaderBytes;
            ContainerDataBytes = containerDataBytes;
            ContributionHeaderBytes = contributionHeaderBytes;
            ContributionDataBytes = contributionDataBytes;
            ContributionMeasured = contributionMeasured;
        }

        internal DecorationManager Manager { get; }

        internal int DecorationCount { get; }

        internal uint ContainerHeaderBytes { get; }

        internal uint ContainerDataBytes { get; }

        internal uint ContributionHeaderBytes { get; }

        internal uint ContributionDataBytes { get; }

        internal bool ContributionMeasured { get; }
    }

    internal sealed class CraftSerializationSnapshot
    {
        internal CraftSerializationSnapshot(
            SerializationOperationKind kind,
            SerializationWireFormat format,
            uint peakHeaderBytes,
            uint peakDataBytes,
            uint nonDecorationPeakHeaderBytes,
            uint nonDecorationPeakDataBytes,
            ulong totalWireBytes,
            ulong totalHeaderBytes,
            ulong totalDataBytes,
            ulong peakContainerWireBytes,
            int containerCount,
            int sentinelContainerCount,
            ulong designChangeCounter,
            DecorationUsageSnapshot decorations,
            IReadOnlyList<DecorationManagerCalibration> calibrations,
            BlueprintSerializationUsage blueprintUsage)
        {
            Kind = kind;
            Format = format;
            PeakHeaderBytes = peakHeaderBytes;
            PeakDataBytes = peakDataBytes;
            NonDecorationPeakHeaderBytes = nonDecorationPeakHeaderBytes;
            NonDecorationPeakDataBytes = nonDecorationPeakDataBytes;
            TotalWireBytes = totalWireBytes;
            TotalHeaderBytes = totalHeaderBytes;
            TotalDataBytes = totalDataBytes;
            PeakContainerWireBytes = peakContainerWireBytes;
            ContainerCount = containerCount;
            SentinelContainerCount = sentinelContainerCount;
            DesignChangeCounter = designChangeCounter;
            Decorations = decorations;
            Calibrations = calibrations;
            BlueprintUsage = blueprintUsage ?? BlueprintSerializationUsage.Empty;
        }

        internal SerializationOperationKind Kind { get; }

        internal SerializationWireFormat Format { get; }

        internal uint PeakHeaderBytes { get; }

        internal uint PeakDataBytes { get; }

        internal uint NonDecorationPeakHeaderBytes { get; }

        internal uint NonDecorationPeakDataBytes { get; }

        internal ulong TotalWireBytes { get; }

        internal ulong TotalHeaderBytes { get; }

        internal ulong TotalDataBytes { get; }

        internal ulong PeakContainerWireBytes { get; }

        internal int ContainerCount { get; }

        internal int SentinelContainerCount { get; }

        internal ulong DesignChangeCounter { get; }

        internal DecorationUsageSnapshot Decorations { get; }

        internal IReadOnlyList<DecorationManagerCalibration> Calibrations { get; }

        internal BlueprintSerializationUsage BlueprintUsage { get; }
    }

    internal sealed class SerializationContainerSample
    {
        internal SerializationContainerSample(
            object source,
            uint headerBytes,
            uint dataBytes,
            SerializationWireFormat format,
            ulong wireBytes = 0UL)
        {
            Source = source;
            HeaderBytes = headerBytes;
            DataBytes = dataBytes;
            Format = format;
            WireBytes = wireBytes == 0UL
                ? (ulong)headerBytes + dataBytes
                : wireBytes;
        }

        internal object Source { get; }
        internal uint HeaderBytes { get; }
        internal uint DataBytes { get; }
        internal ulong WireBytes { get; }
        internal SerializationWireFormat Format { get; }
        internal List<DecorationManagerCalibration> Calibrations { get; } =
            new List<DecorationManagerCalibration>();
    }

    internal static class CraftSerializationSnapshotFactory
    {
        internal static CraftSerializationSnapshot Create(
            SerializationOperationKind kind,
            IReadOnlyCollection<SerializationContainerSample> containers,
            ulong designChangeCounter,
            DecorationUsageSnapshot decorations,
            BlueprintSerializationUsage blueprintUsage = null)
        {
            uint peakHeader = 0U;
            uint peakData = 0U;
            uint nonDecorationPeakHeader = 0U;
            uint nonDecorationPeakData = 0U;
            ulong totalWire = 0UL;
            ulong totalHeader = 0UL;
            ulong totalData = 0UL;
            ulong peakContainerWire = 0UL;
            int sentinelCount = 0;
            var calibrations = new List<DecorationManagerCalibration>();
            foreach (SerializationContainerSample container in containers)
            {
                peakHeader = Math.Max(peakHeader, container.HeaderBytes);
                peakData = Math.Max(peakData, container.DataBytes);
                totalWire = SaturatingAdd(totalWire, container.WireBytes);
                totalHeader = SaturatingAdd(totalHeader, container.HeaderBytes);
                totalData = SaturatingAdd(totalData, container.DataBytes);
                peakContainerWire = Math.Max(peakContainerWire, container.WireBytes);
                if (container.Format == SerializationWireFormat.Sentinel)
                    sentinelCount++;

                if (container.Calibrations.Count == 0)
                {
                    nonDecorationPeakHeader = Math.Max(
                        nonDecorationPeakHeader,
                        container.HeaderBytes);
                    nonDecorationPeakData = Math.Max(
                        nonDecorationPeakData,
                        container.DataBytes);
                }
                else
                {
                    calibrations.AddRange(container.Calibrations);
                }
            }

            return new CraftSerializationSnapshot(
                kind,
                sentinelCount > 0
                    ? SerializationWireFormat.Sentinel
                    : SerializationWireFormat.Legacy,
                peakHeader,
                peakData,
                nonDecorationPeakHeader,
                nonDecorationPeakData,
                totalWire,
                totalHeader,
                totalData,
                peakContainerWire,
                containers.Count,
                sentinelCount,
                designChangeCounter,
                decorations,
                calibrations,
                blueprintUsage);
        }

        private static ulong SaturatingAdd(ulong left, ulong right) =>
            ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    }

    internal sealed class CraftSerializationHistory
    {
        internal readonly object Sync = new object();
        internal CraftSerializationSnapshot Loaded;
        internal CraftSerializationSnapshot Saved;
        internal CraftSerializationSnapshot Measured;
    }

    internal sealed class DecorationContributionState
    {
        internal DecorationContributionState(
            DecorationManager manager,
            SuperSaver saver,
            uint headerCount,
            uint dataBytes,
            int decorationCount)
        {
            Manager = manager;
            Saver = saver;
            HeaderCount = headerCount;
            DataBytes = dataBytes;
            DecorationCount = decorationCount;
        }

        internal DecorationManager Manager { get; }
        internal SuperSaver Saver { get; }
        internal uint HeaderCount { get; }
        internal uint DataBytes { get; }
        internal int DecorationCount { get; }
    }

    internal sealed class DecorationLoadState
    {
        internal DecorationLoadState(
            DecorationManager manager,
            SuperLoader loader,
            uint dataCursor)
        {
            Manager = manager;
            Loader = loader;
            DataCursor = dataCursor;
        }

        internal DecorationManager Manager { get; }
        internal SuperLoader Loader { get; }
        internal uint DataCursor { get; }
    }

    internal sealed class SerializationTelemetryOperation : IDisposable
    {
        private sealed class PendingSaveContribution
        {
            internal DecorationManager Manager;
            internal int DecorationCount;
            internal uint HeaderBytes;
            internal uint DataBytes;
        }

        private readonly List<SerializationContainerSample> _containers =
            new List<SerializationContainerSample>();
        private readonly Dictionary<SuperSaver, Dictionary<DecorationManager, PendingSaveContribution>>
            _pendingSave =
            new Dictionary<SuperSaver, Dictionary<DecorationManager, PendingSaveContribution>>(
                ReferenceEqualityComparer<SuperSaver>.Instance);
        private BlueprintSerializationUsage _blueprintUsage;
        private bool _completed;
        private bool _disposed;

        internal SerializationTelemetryOperation(
            SerializationOperationKind kind,
            MainConstruct sourceConstruct)
        {
            Kind = kind;
            SourceConstruct = sourceConstruct;
        }

        internal SerializationOperationKind Kind { get; }

        internal MainConstruct SourceConstruct { get; }

        internal void RecordContainer(
            object source,
            SuperContainerFormat format,
            uint headerBytes,
            uint dataBytes,
            ulong wireBytes)
        {
            var measurement = new SerializationContainerSample(
                source,
                headerBytes,
                dataBytes,
                format == SuperContainerFormat.Sentinel
                    ? SerializationWireFormat.Sentinel
                    : SerializationWireFormat.Legacy,
                wireBytes);

            if (source is SuperSaver saver && _pendingSave.TryGetValue(saver, out var pending))
            {
                measurement.Calibrations.AddRange(pending.Values.Select(calibration =>
                    new DecorationManagerCalibration(
                        calibration.Manager,
                        calibration.DecorationCount,
                        headerBytes,
                        dataBytes,
                        calibration.HeaderBytes,
                        calibration.DataBytes,
                        contributionMeasured: true)));
                _pendingSave.Remove(saver);
            }
            _containers.Add(measurement);
        }

        internal void RecordBlueprintUsage(global::Blueprint blueprint)
        {
            _blueprintUsage = BlueprintSerializationUsageAnalyzer.Analyze(blueprint);
        }

        internal void RecordSaveContribution(
            DecorationManager manager,
            SuperSaver saver,
            int decorationCount,
            uint headerBytes,
            uint dataBytes)
        {
            if (manager == null)
                return;
            if (!_pendingSave.TryGetValue(saver, out var pending))
            {
                pending = new Dictionary<DecorationManager, PendingSaveContribution>(
                    ReferenceEqualityComparer<DecorationManager>.Instance);
                _pendingSave.Add(saver, pending);
            }
            if (!pending.TryGetValue(manager, out var contribution))
            {
                contribution = new PendingSaveContribution { Manager = manager };
                pending.Add(manager, contribution);
            }
            contribution.DecorationCount = checked(
                contribution.DecorationCount + decorationCount);
            contribution.HeaderBytes = checked(contribution.HeaderBytes + headerBytes);
            contribution.DataBytes = checked(contribution.DataBytes + dataBytes);
        }

        internal void RecordLoadContribution(
            DecorationManager manager,
            SuperLoader loader,
            int decorationCount,
            uint dataBytes)
        {
            for (int i = _containers.Count - 1; i >= 0; i--)
            {
                SerializationContainerSample container = _containers[i];
                if (!ReferenceEquals(container.Source, loader))
                    continue;
                container.Calibrations.Add(new DecorationManagerCalibration(
                    manager,
                    decorationCount,
                    container.HeaderBytes,
                    container.DataBytes,
                    checked((uint)Math.Min(
                        uint.MaxValue,
                        (ulong)Math.Max(0, decorationCount) * 7UL)),
                    Math.Min(dataBytes, container.DataBytes),
                    contributionMeasured: dataBytes > 0U || decorationCount == 0));
                return;
            }
        }

        internal void Complete(MainConstruct mainConstruct)
        {
            if (_completed || mainConstruct == null)
                return;
            _completed = true;

            DecorationUsageSnapshot decorations = DecorationUsageSnapshot.Capture(mainConstruct);
            CraftSerializationSnapshot snapshot = CraftSerializationSnapshotFactory.Create(
                Kind,
                _containers,
                mainConstruct.DesignChangeCounter,
                decorations,
                _blueprintUsage);
            SerializationTelemetry.Publish(mainConstruct, snapshot);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            SerializationTelemetry.End(this);
        }
    }

    internal static class SerializationTelemetry
    {
        [ThreadStatic]
        private static List<SerializationTelemetryOperation> _operationStack;

        private static ConditionalWeakTable<MainConstruct, CraftSerializationHistory> _histories =
            new ConditionalWeakTable<MainConstruct, CraftSerializationHistory>();

        internal static SerializationTelemetryOperation Begin(
            SerializationOperationKind kind,
            MainConstruct sourceConstruct = null)
        {
            var operation = new SerializationTelemetryOperation(kind, sourceConstruct);
            if (_operationStack == null)
                _operationStack = new List<SerializationTelemetryOperation>();
            _operationStack.Add(operation);
            return operation;
        }

        internal static DecorationContributionState BeginDecorationSave(
            Decoration decoration,
            ISuperSaver saver)
        {
            if (Current == null || !(saver is SuperSaver concrete))
                return null;
            DecorationManager manager = decoration?.OurManager?.Packets;
            if (manager == null)
                return null;
            return new DecorationContributionState(
                manager,
                concrete,
                concrete.HeaderCount,
                concrete._datasWrittenSorted,
                1);
        }

        internal static void CompleteDecorationSave(DecorationContributionState state)
        {
            SerializationTelemetryOperation operation = Current;
            if (operation == null || state == null)
                return;
            uint currentHeaders = state.Saver.HeaderCount;
            uint currentData = state.Saver._datasWrittenSorted;
            if (currentHeaders < state.HeaderCount || currentData < state.DataBytes)
                return;
            operation.RecordSaveContribution(
                state.Manager,
                state.Saver,
                state.DecorationCount,
                checked((currentHeaders - state.HeaderCount) * 7U),
                checked(currentData - state.DataBytes + 26U));
        }

        internal static DecorationLoadState BeginDecorationLoad(
            DecorationManager manager,
            ISuperLoader loader)
        {
            if (Current == null || !(loader is SuperLoader concrete))
                return null;
            return new DecorationLoadState(manager, concrete, Priv.GetDataCursor(concrete));
        }

        internal static void CompleteDecorationLoad(DecorationLoadState state)
        {
            SerializationTelemetryOperation operation = Current;
            if (operation == null || state == null)
                return;
            uint current = Priv.GetDataCursor(state.Loader);
            uint consumed = current >= state.DataCursor ? current - state.DataCursor : 0U;
            operation.RecordLoadContribution(
                state.Manager,
                state.Loader,
                Math.Max(0, state.Manager?.PackageCount ?? 0),
                consumed);
        }

        internal static void RecordSavedContainer(
            SuperSaver saver,
            SuperSerialisationLayout layout)
        {
            try
            {
                Current?.RecordContainer(
                    saver,
                    layout.Format,
                    layout.HeaderBytes,
                    layout.DataBytes,
                    (ulong)layout.TotalBytes);
            }
            catch
            {
                // Telemetry must never affect serialization.
            }
        }

        internal static void RecordLoadedContainer(
            SuperLoader loader,
            bool sentinel,
            uint headerBytes,
            uint dataBytes,
            ulong wireBytes)
        {
            try
            {
                Current?.RecordContainer(
                    loader,
                    sentinel ? SuperContainerFormat.Sentinel : SuperContainerFormat.Legacy,
                    headerBytes,
                    dataBytes,
                    wireBytes);
            }
            catch
            {
                // Telemetry must never affect deserialization.
            }
        }

        internal static bool TryGetHistory(
            MainConstruct mainConstruct,
            out CraftSerializationSnapshot loaded,
            out CraftSerializationSnapshot saved,
            out CraftSerializationSnapshot measured)
        {
            loaded = null;
            saved = null;
            measured = null;
            if (mainConstruct == null || !_histories.TryGetValue(mainConstruct, out var history))
                return false;
            lock (history.Sync)
            {
                loaded = history.Loaded;
                saved = history.Saved;
                measured = history.Measured;
            }
            return loaded != null || saved != null || measured != null;
        }

        internal static bool TryGetHistory(
            MainConstruct mainConstruct,
            out CraftSerializationSnapshot loaded,
            out CraftSerializationSnapshot saved) =>
            TryGetHistory(mainConstruct, out loaded, out saved, out _);

        internal static void Publish(
            MainConstruct mainConstruct,
            CraftSerializationSnapshot snapshot)
        {
            CraftSerializationHistory history = _histories.GetValue(
                mainConstruct,
                _ => new CraftSerializationHistory());
            lock (history.Sync)
            {
                if (snapshot.Kind == SerializationOperationKind.Load)
                    history.Loaded = snapshot;
                else if (snapshot.Kind == SerializationOperationKind.Save)
                    history.Saved = snapshot;
                else
                    history.Measured = snapshot;
            }
        }

        internal static bool HasCurrentOperation => Current != null;

        internal static void End(SerializationTelemetryOperation operation)
        {
            if (_operationStack == null || _operationStack.Count == 0)
                return;
            int last = _operationStack.Count - 1;
            if (ReferenceEquals(_operationStack[last], operation))
            {
                _operationStack.RemoveAt(last);
                return;
            }

            // A mismatched scope must not leak telemetry into unrelated serialisation.
            _operationStack.Clear();
        }

        internal static void ResetForTests()
        {
            _operationStack?.Clear();
            _histories = new ConditionalWeakTable<MainConstruct, CraftSerializationHistory>();
        }

        internal static int CurrentDepthForTests => _operationStack?.Count ?? 0;

        private static SerializationTelemetryOperation Current =>
            _operationStack != null && _operationStack.Count > 0
                ? _operationStack[_operationStack.Count - 1]
                : null;
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly ReferenceEqualityComparer<T> Instance =
            new ReferenceEqualityComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
