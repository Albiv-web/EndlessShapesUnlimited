using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.JsonPlus.Converters;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.DataOwnerInterfaces;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using DecoLimitLifter.ExtendedSerialization;
using DecoLimitLifter.SerializationHud;
using HarmonyLib;
using Newtonsoft.Json;

namespace DecoLimitLifter.Patches
{
    internal readonly struct FastBlueprintBlockDataRecord
    {
        internal FastBlueprintBlockDataRecord(
            int ordinal,
            int blockIndex,
            uint start,
            uint end)
        {
            Ordinal = ordinal;
            BlockIndex = blockIndex;
            Start = start;
            End = end;
        }

        internal int Ordinal { get; }
        internal int BlockIndex { get; }
        internal uint Start { get; }
        internal uint End { get; }
    }

    internal readonly struct FastBlueprintBlockApplyStats
    {
        internal FastBlueprintBlockApplyStats(
            int loaded,
            int skippedNull,
            int skippedOutOfRange)
        {
            Loaded = loaded;
            SkippedNull = skippedNull;
            SkippedOutOfRange = skippedOutOfRange;
        }

        internal int Loaded { get; }
        internal int SkippedNull { get; }
        internal int SkippedOutOfRange { get; }
    }

    internal readonly struct FastBlueprintV3CaptureDecision
    {
        internal FastBlueprintV3CaptureDecision(bool capture, string reason)
        {
            Capture = capture;
            Reason = reason ?? string.Empty;
        }

        internal bool Capture { get; }
        internal string Reason { get; }
    }

    internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceIdentityComparer Instance =
            new ReferenceIdentityComparer();

        private ReferenceIdentityComparer()
        {
        }

        public new bool Equals(object x, object y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }

    internal sealed class FastBlueprintV3BulkLoadContext : IDisposable
    {
        private readonly FastBlueprintLoadTrace _trace;
        private readonly FastBlueprintV3BulkLoadContext _previous;
        private readonly List<CapturedBlockState> _captured = new List<CapturedBlockState>();
        private readonly HashSet<Block> _capturedBlocks = new HashSet<Block>();
        private readonly Dictionary<object, List<CapturedBlockState>> _capturedByMainConstruct =
            new Dictionary<object, List<CapturedBlockState>>(ReferenceIdentityComparer.Instance);
        private readonly Dictionary<object, List<CapturedBlockState>> _capturedByOwnerConstruct =
            new Dictionary<object, List<CapturedBlockState>>(ReferenceIdentityComparer.Instance);
        private readonly Stack<AllConstruct> _constructScopes = new Stack<AllConstruct>();
        private bool _disabled;
        private bool _disposed;
        private bool _flushing;
        private int _flushCount;
        private int _availableFlushCount;
        private int _syncFlushCount;
        private int _syncSkippedCount;
        private int _fallbackReplayCount;
        private int _fireResetCount;
        private int _acbProcessingCount;
        private int _controlSurfaceCount;
        private int _mainCatalogueCount;
        private int _constructCatalogueCount;
        private int _statusCheckCount;
        private int _statusCheckSkippedCount;
        private int _statusCheckBulkCount;
        private int _statusCheckFallbackCount;
        private long _fireResetTicks;
        private long _acbProcessingTicks;
        private long _controlSurfaceTicks;
        private long _mainCatalogueTicks;
        private long _constructCatalogueTicks;
        private long _statusCheckTicks;
        private long _syncRegistrationTicks;

        private sealed class CapturedBlockState
        {
            internal CapturedBlockState(
                Block block,
                BlockStateChangeType type,
                bool available,
                bool registerSync)
            {
                Block = block;
                Type = type;
                Available = available;
                RegisterSync = registerSync;
            }

            internal Block Block { get; }
            internal BlockStateChangeType Type { get; }
            internal bool Available { get; }
            internal bool RegisterSync { get; }
            internal bool Flushed { get; set; }
            internal bool StatusFlushed { get; set; }
        }

        private sealed class StatusCollectionSnapshot
        {
            internal StatusCollectionSnapshot(
                string memberName,
                IList list,
                Type elementType)
            {
                MemberName = memberName ?? string.Empty;
                List = list;
                ElementType = elementType;
                Entries = CopyEntries(list);
            }

            internal string MemberName { get; }
            internal IList List { get; }
            internal Type ElementType { get; }
            internal object[] Entries { get; }

            internal bool ContainsReference(object value)
            {
                foreach (object entry in Entries)
                {
                    if (ReferenceEquals(entry, value))
                        return true;
                }
                return false;
            }

            private static object[] CopyEntries(IList list)
            {
                if (list == null)
                    return Array.Empty<object>();

                var entries = new object[list.Count];
                for (int i = 0; i < entries.Length; i++)
                    entries[i] = list[i];
                return entries;
            }
        }

        private sealed class StatusBulkOwnerPlan
        {
            private readonly StatusCollectionSnapshot[] _targets;

            internal StatusBulkOwnerPlan(StatusCollectionSnapshot[] targets)
            {
                _targets = targets ?? Array.Empty<StatusCollectionSnapshot>();
            }

            internal int TargetCount => _targets.Length;

            internal void Commit(IList<Block> statusBlocks)
            {
                foreach (StatusCollectionSnapshot target in _targets)
                {
                    target.List.Clear();
                    var added = new HashSet<object>(ReferenceIdentityComparer.Instance);
                    foreach (object entry in target.Entries)
                    {
                        if (entry != null && added.Add(entry))
                            target.List.Add(entry);
                    }

                    foreach (Block block in statusBlocks)
                    {
                        if (block == null)
                            continue;
                        if (!target.ElementType.IsAssignableFrom(block.GetType()))
                            throw new InvalidOperationException(
                                "Status target element type cannot accept " + block.GetType().FullName);
                        if (added.Add(block))
                            target.List.Add(block);
                    }
                }
            }
        }

        internal FastBlueprintV3BulkLoadContext(
            FastBlueprintLoadTrace trace,
            FastBlueprintV3BulkLoadContext previous)
        {
            _trace = trace;
            _previous = previous;
            _trace?.Event(
                "v3-preflight",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("supported", true),
                FastBlueprintLoadTrace.Pair("mode", "safe-base-registration-bulk"));
        }

        internal FastBlueprintV3BulkLoadContext Previous => _previous;

        internal IDisposable BeginInitialiseStage2(AllConstruct construct)
        {
            if (_disabled || construct == null)
                return null;

            _constructScopes.Push(construct);
            _trace?.Event(
                "v3-scope-start",
                "v3",
                advLogger: false,
                FastBlueprintLoadTrace.Pair("construct_type", construct.GetType().Name),
                FastBlueprintLoadTrace.Pair("alive_dead_blocks", construct.AllBasics?.AliveAndDead?.Count ?? 0),
                FastBlueprintLoadTrace.Pair("depth", _constructScopes.Count));
            return new Stage2Scope(this, construct);
        }

        internal bool TryCapture(Block block, IBlockStateChange change)
        {
            if (_disabled || _flushing || block == null || change == null || _constructScopes.Count == 0)
                return false;

            FastBlueprintV3CaptureDecision decision = ShouldCaptureForVerification(change.Type);
            if (!decision.Capture)
                return false;

            if (!_capturedBlocks.Add(block))
                return true;

            _captured.Add(new CapturedBlockState(
                block,
                change.Type,
                change.IsAvailableToConstruct,
                change.InitiatedOrInitiatedInUnrepairedState_OnlyCalledOnce));
            BucketCapturedBlock(block);

            _trace?.Heartbeat(
                "V3 base state capture",
                _captured.Count,
                Math.Max(1, _constructScopes.Peek().UninitialisedBlocks?.Count ?? _captured.Count),
                "records");
            return true;
        }

        internal void FlushBeforeBlockData(string reason)
        {
            if (_disposed || _disabled || _captured.Count == 0 || !HasUnflushedRecords())
                return;

            Stopwatch timer = Stopwatch.StartNew();
            _trace?.Event(
                "v3-flush-start",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("reason", reason ?? string.Empty),
                FastBlueprintLoadTrace.Pair("captured", _captured.Count));

            _flushing = true;
            try
            {
                int flushedNow = 0;
                PreSizeKnownRegistries();
                foreach (CapturedBlockState record in _captured)
                {
                    if (record.Flushed || record.Block == null)
                        continue;
                    FlushRecord(record);
                    record.Flushed = true;
                    flushedNow++;
                    _flushCount++;
                    _trace?.Heartbeat(
                        "V3 base state flush",
                        _flushCount,
                        _captured.Count,
                        "records",
                        FastBlueprintLoadTrace.Pair("available", _availableFlushCount),
                        FastBlueprintLoadTrace.Pair("sync", _syncFlushCount));
                }
                FlushDeferredStatusChecks(reason);

                timer.Stop();
                _trace?.Event(
                    "v3-flush-complete",
                    "v3",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("reason", reason ?? string.Empty),
                    FastBlueprintLoadTrace.Pair("flushed_now", flushedNow),
                    FastBlueprintLoadTrace.Pair("captured", _captured.Count),
                    FastBlueprintLoadTrace.Pair("available", _availableFlushCount),
                    FastBlueprintLoadTrace.Pair("sync", _syncFlushCount),
                    FastBlueprintLoadTrace.Pair("sync_skipped", _syncSkippedCount),
                    FastBlueprintLoadTrace.Pair("status_skipped", _statusCheckSkippedCount),
                    FastBlueprintLoadTrace.Pair("status_bulk", _statusCheckBulkCount),
                    FastBlueprintLoadTrace.Pair("status_fallback", _statusCheckFallbackCount),
                    FastBlueprintLoadTrace.Pair("unsafe_probe", FastBlueprintLoadRouter.ActiveUnsafeProbeName),
                    FastBlueprintLoadTrace.Pair("unsafe_probe_active", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                    FastBlueprintLoadTrace.Pair("correctness_valid", !FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                    FastBlueprintLoadTrace.Pair("do_not_save", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                    FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds));
                LogFlushPhaseSummary();
            }
            catch (Exception exception)
            {
                timer.Stop();
                DisableAndReplayFallback(exception, reason ?? "flush-failed");
            }
            finally
            {
                _flushing = false;
            }
        }

        internal void ScopeComplete(AllConstruct construct)
        {
            FlushBeforeBlockData("stage2-complete");
            PopConstructScope(construct);
        }

        internal void ScopeFailed(AllConstruct construct, Exception exception)
        {
            if (exception != null)
            {
                _trace?.Exception("v3-stage2", exception);
                DisableAndReplayFallback(exception, "stage2-exception");
            }
            else
            {
                FlushBeforeBlockData("stage2-finalizer");
            }

            PopConstructScope(construct);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            FlushBeforeBlockData("conversion-complete");
            _disposed = true;
            _constructScopes.Clear();
            _trace?.Event(
                "v3-capture-summary",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("captured", _captured.Count),
                FastBlueprintLoadTrace.Pair("flushed", _flushCount),
                FastBlueprintLoadTrace.Pair("available", _availableFlushCount),
                FastBlueprintLoadTrace.Pair("sync", _syncFlushCount),
                FastBlueprintLoadTrace.Pair("sync_skipped", _syncSkippedCount),
                FastBlueprintLoadTrace.Pair("fallback_replayed", _fallbackReplayCount),
                FastBlueprintLoadTrace.Pair("status_skipped", _statusCheckSkippedCount),
                FastBlueprintLoadTrace.Pair("status_bulk", _statusCheckBulkCount),
                FastBlueprintLoadTrace.Pair("status_fallback", _statusCheckFallbackCount),
                FastBlueprintLoadTrace.Pair("main_construct_buckets", _capturedByMainConstruct.Count),
                FastBlueprintLoadTrace.Pair("construct_owner_buckets", _capturedByOwnerConstruct.Count),
                FastBlueprintLoadTrace.Pair("unsafe_probe", FastBlueprintLoadRouter.ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("unsafe_probe_active", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_invalid", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_valid", !FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("do_not_save", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("disabled", _disabled));
        }

        internal static FastBlueprintV3CaptureDecision ShouldCaptureForVerification(
            BlockStateChangeType type)
        {
            switch (type)
            {
                case BlockStateChangeType.Initiated:
                case BlockStateChangeType.InitiatedNewPlacement:
                case BlockStateChangeType.InitiatedInUnrepairedState:
                    return new FastBlueprintV3CaptureDecision(true, "initial-positive-load-change");
                default:
                    return new FastBlueprintV3CaptureDecision(false, "not-initial-load-change");
            }
        }

        private void FlushRecord(CapturedBlockState record)
        {
            Block block = record.Block;
            if (record.Available)
            {
                MeasureFlushPart("fireReset", ref _fireResetTicks, ref _fireResetCount, () =>
                {
                    block.FireDamageFraction = 0f;
                });
                MeasureFlushPart("acbProcessing", ref _acbProcessingTicks, ref _acbProcessingCount, () =>
                {
                    block.MainConstruct.AcbProcessing.AddBlock(block, true);
                });
                if (block is IControlSurface controlSurface)
                {
                    MeasureFlushPart("controlSurfaces", ref _controlSurfaceTicks, ref _controlSurfaceCount, () =>
                    {
                        block.MainConstruct.iBlockTypeStorage.ControlSurfaceBlocks.Add(controlSurface);
                    });
                }

                MeasureFlushPart("mainCatalogue", ref _mainCatalogueTicks, ref _mainCatalogueCount, () =>
                {
                    block.MainConstruct.iBlockTypeStorage.EntireBlockCatalogue.AddBlock(block);
                });
                if (block.IsOnSubConstructable)
                {
                    MeasureFlushPart("constructCatalogue", ref _constructCatalogueTicks, ref _constructCatalogueCount, () =>
                    {
                        block.SubConstruct.iBlockTypeStorageSpecific.EntireBlockCatalogue.AddBlock(block);
                    });
                }
                else
                {
                    MeasureFlushPart("constructCatalogue", ref _constructCatalogueTicks, ref _constructCatalogueCount, () =>
                    {
                        block.MainConstruct.iBlockTypeStorageSpecific.EntireBlockCatalogue.AddBlock(block);
                    });
                }

                _availableFlushCount++;
            }

            if (record.RegisterSync)
            {
                if (FastBlueprintLoadRouter.SkipV3SyncRegistrationForDiagnostics)
                {
                    _syncSkippedCount++;
                    _trace?.Heartbeat(
                        "V3 flush syncRegistration",
                        _syncSkippedCount,
                        _captured.Count,
                        "records",
                        FastBlueprintLoadTrace.Pair("unsafe_probe_active", true),
                        FastBlueprintLoadTrace.Pair("skipped", _syncSkippedCount));
                }
                else
                {
                    MeasureFlushPart("syncRegistration", ref _syncRegistrationTicks, ref _syncFlushCount, () =>
                    {
                        ((ISyncableDataOwner)block).RegisterAllWithAChangeSync(
                            block.GetConstructableOrSubConstructable().AutoSyncroniserRestricted.GetSyncer(),
                            block.LocalPosition);
                    });
                }
            }
        }

        private void FlushDeferredStatusChecks(string reason)
        {
            var statusRecords = new List<CapturedBlockState>();
            foreach (CapturedBlockState record in _captured)
            {
                Block block = record.Block;
                if (!record.Flushed ||
                    record.StatusFlushed ||
                    !record.Available ||
                    block == null ||
                    !OverridesCheckStatus(block))
                {
                    continue;
                }
                statusRecords.Add(record);
            }

            if (statusRecords.Count == 0)
                return;

            if (FastBlueprintLoadRouter.SkipV3StatusRegistrationForDiagnostics)
            {
                _statusCheckSkippedCount += statusRecords.Count;
                foreach (CapturedBlockState record in statusRecords)
                    record.StatusFlushed = true;
                FastBlueprintLoadRouter.LogUnsafeProbeEvent(
                    _trace,
                    "v3-status-registration",
                    "skipped V3 status registration",
                    FastBlueprintLoadTrace.Pair("status_skipped", statusRecords.Count));
                return;
            }

            var byStatusOwner = new Dictionary<object, List<CapturedBlockState>>(
                ReferenceIdentityComparer.Instance);
            foreach (CapturedBlockState record in statusRecords)
            {
                object owner = record.Block?.MainConstruct?.PartStatusRestricted;
                if (owner == null)
                {
                    RegisterStatusBlocksRowByRow(
                        new[] { record },
                        "missing-status-owner");
                    continue;
                }
                if (!byStatusOwner.TryGetValue(owner, out List<CapturedBlockState> records))
                {
                    records = new List<CapturedBlockState>();
                    byStatusOwner.Add(owner, records);
                }
                records.Add(record);
            }

            foreach (KeyValuePair<object, List<CapturedBlockState>> pair in byStatusOwner)
                FlushStatusOwner(pair.Key, pair.Value, reason);
        }

        private void FlushStatusOwner(
            object owner,
            List<CapturedBlockState> records,
            string reason)
        {
            if (owner == null || records == null || records.Count == 0)
                return;

            var statusBlocks = new List<Block>(records.Count);
            foreach (CapturedBlockState record in records)
            {
                if (record.Block != null)
                    statusBlocks.Add(record.Block);
            }

            _trace?.Event(
                "v3-status-bulk-preflight",
                "v3.status",
                advLogger: false,
                FastBlueprintLoadTrace.Pair("reason", reason ?? string.Empty),
                FastBlueprintLoadTrace.Pair("owner_type", owner.GetType().FullName),
                FastBlueprintLoadTrace.Pair("status_count", statusBlocks.Count));

            if (!TryCreateStatusBulkOwnerPlan(
                    owner,
                    statusBlocks.Count > 0 ? statusBlocks[0] : null,
                    out StatusBulkOwnerPlan plan,
                    out string fallbackReason))
            {
                _trace?.Event(
                    "v3-status-bulk-fallback",
                    "v3.status",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("reason", fallbackReason),
                    FastBlueprintLoadTrace.Pair("owner_type", owner.GetType().FullName),
                    FastBlueprintLoadTrace.Pair("status_count", statusBlocks.Count));
                RegisterStatusBlocksRowByRow(records, fallbackReason);
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                plan.Commit(statusBlocks);
                timer.Stop();
                _statusCheckBulkCount += statusBlocks.Count;
                _statusCheckCount += statusBlocks.Count;
                _statusCheckTicks += timer.ElapsedTicks;
                foreach (CapturedBlockState record in records)
                    record.StatusFlushed = true;
                _trace?.Event(
                    "v3-status-bulk-complete",
                    "v3.status",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("owner_type", owner.GetType().FullName),
                    FastBlueprintLoadTrace.Pair("status_count", statusBlocks.Count),
                    FastBlueprintLoadTrace.Pair("target_count", plan.TargetCount),
                    FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds));
            }
            catch (Exception exception)
            {
                timer.Stop();
                _trace?.Exception("v3-status-bulk", exception);
                _trace?.Event(
                    "v3-status-bulk-fallback",
                    "v3.status",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("reason", "bulk-commit-failed"),
                    FastBlueprintLoadTrace.Pair("owner_type", owner.GetType().FullName),
                    FastBlueprintLoadTrace.Pair("status_count", statusBlocks.Count),
                    FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds));
                RegisterStatusBlocksRowByRow(records, "bulk-commit-failed");
            }
        }

        private void RegisterStatusBlocksRowByRow(
            IEnumerable<CapturedBlockState> records,
            string reason)
        {
            int fallbackCount = 0;
            foreach (CapturedBlockState record in records)
            {
                Block block = record.Block;
                if (block == null || record.StatusFlushed)
                    continue;
                try
                {
                    MeasureFlushPart("statusChecks", ref _statusCheckTicks, ref _statusCheckCount, () =>
                    {
                        block.MainConstruct.PartStatusRestricted.RegisterCheckableBlock(block);
                    });
                    record.StatusFlushed = true;
                    fallbackCount++;
                    _trace?.Heartbeat(
                        "V3 flush statusChecks",
                        _statusCheckCount,
                        _captured.Count,
                        "records",
                        FastBlueprintLoadTrace.Pair("fallback", true),
                        FastBlueprintLoadTrace.Pair("reason", reason ?? string.Empty));
                }
                catch (Exception exception)
                {
                    _trace?.Exception("v3-status-row-fallback", exception);
                    FastBlueprintLoadRouter.LogException("replay V3 status block registration", exception);
                }
            }
            _statusCheckFallbackCount += fallbackCount;
        }

        private bool TryCreateStatusBulkOwnerPlan(
            object owner,
            Block probeBlock,
            out StatusBulkOwnerPlan plan,
            out string reason)
        {
            plan = null;
            if (owner == null)
            {
                reason = "missing-status-owner";
                return false;
            }
            if (probeBlock == null)
            {
                reason = "missing-probe-block";
                return false;
            }

            MethodInfo register = FindStatusMethod(owner.GetType(), "RegisterCheckableBlock", probeBlock);
            MethodInfo unregister = FindStatusMethod(owner.GetType(), "UnregisterCheckableBlock", probeBlock);
            if (register == null || unregister == null)
            {
                reason = "status-methods-unresolved";
                return false;
            }

            StatusCollectionSnapshot[] before = SnapshotStatusCollections(owner);
            if (before.Length == 0)
            {
                reason = "no-mutable-block-collections";
                return false;
            }
            foreach (StatusCollectionSnapshot snapshot in before)
            {
                if (snapshot.ContainsReference(probeBlock))
                {
                    reason = "probe-already-registered";
                    return false;
                }
            }

            try
            {
                register.Invoke(owner, new object[] { probeBlock });
            }
            catch (Exception exception)
            {
                reason = "probe-register-failed:" + UnwrapReflectionException(exception).GetType().Name;
                return false;
            }

            StatusCollectionSnapshot[] afterRegister = SnapshotStatusCollections(owner);
            var changed = new List<StatusCollectionSnapshot>();
            foreach (StatusCollectionSnapshot beforeSnapshot in before)
            {
                StatusCollectionSnapshot afterSnapshot = FindSnapshotForList(
                    afterRegister,
                    beforeSnapshot.List);
                if (afterSnapshot != null &&
                    afterSnapshot.ContainsReference(probeBlock) &&
                    !beforeSnapshot.ContainsReference(probeBlock))
                {
                    changed.Add(beforeSnapshot);
                }
            }

            try
            {
                unregister.Invoke(owner, new object[] { probeBlock });
            }
            catch (Exception exception)
            {
                reason = "probe-unregister-failed:" + UnwrapReflectionException(exception).GetType().Name;
                return false;
            }

            if (changed.Count == 0)
            {
                reason = "probe-did-not-identify-collection";
                return false;
            }

            StatusCollectionSnapshot[] afterUnregister = SnapshotStatusCollections(owner);
            foreach (StatusCollectionSnapshot changedSnapshot in changed)
            {
                StatusCollectionSnapshot restored = FindSnapshotForList(
                    afterUnregister,
                    changedSnapshot.List);
                if (restored == null ||
                    restored.ContainsReference(probeBlock))
                {
                    reason = "probe-unregister-did-not-restore";
                    return false;
                }
            }

            plan = new StatusBulkOwnerPlan(changed.ToArray());
            reason = "direct-list-rebuild";
            return true;
        }

        private static MethodInfo FindStatusMethod(
            Type ownerType,
            string methodName,
            Block probeBlock)
        {
            foreach (MethodInfo method in CandidateStatusMethods(ownerType, methodName))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType.IsAssignableFrom(probeBlock.GetType()))
                {
                    return method;
                }
            }
            return null;
        }

        private static IEnumerable<MethodInfo> CandidateStatusMethods(
            Type ownerType,
            string methodName)
        {
            if (ownerType == null)
                yield break;

            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in ownerType.GetMethods(flags))
            {
                if (method.Name == methodName || method.Name.EndsWith("." + methodName, StringComparison.Ordinal))
                    yield return method;
            }

            foreach (Type iface in ownerType.GetInterfaces())
            {
                foreach (MethodInfo method in iface.GetMethods())
                {
                    if (method.Name == methodName)
                        yield return method;
                }
            }
        }

        private static StatusCollectionSnapshot[] SnapshotStatusCollections(object owner)
        {
            if (owner == null)
                return Array.Empty<StatusCollectionSnapshot>();

            var snapshots = new List<StatusCollectionSnapshot>();
            var seen = new HashSet<object>(ReferenceIdentityComparer.Instance);
            AddStatusCollectionSnapshotsForObject(snapshots, seen, owner, prefix: null);
            AddNestedStatusHandlerSnapshots(snapshots, seen, owner);
            return snapshots.ToArray();
        }

        private static void AddNestedStatusHandlerSnapshots(
            List<StatusCollectionSnapshot> snapshots,
            HashSet<object> seen,
            object owner)
        {
            if (owner == null)
                return;

            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            while (type != null)
            {
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (field.IsStatic)
                        continue;
                    if (!LooksLikeStatusHandlerType(field.FieldType))
                        continue;
                    object handler;
                    try { handler = field.GetValue(owner); }
                    catch { continue; }
                    AddStatusCollectionSnapshotsForObject(
                        snapshots,
                        seen,
                        handler,
                        field.Name);
                }
                type = type.BaseType;
            }
        }

        private static void AddStatusCollectionSnapshotsForObject(
            List<StatusCollectionSnapshot> snapshots,
            HashSet<object> seen,
            object owner,
            string prefix)
        {
            if (owner == null)
                return;

            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            while (type != null)
            {
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (field.IsStatic)
                        continue;
                    TryAddStatusCollectionSnapshot(
                        snapshots,
                        seen,
                        CombineMemberPath(prefix, field.Name),
                        field.FieldType,
                        () => field.GetValue(owner));
                }

                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;
                    MethodInfo getter = property.GetGetMethod(nonPublic: true);
                    if (getter == null || getter.IsStatic)
                        continue;
                    TryAddStatusCollectionSnapshot(
                        snapshots,
                        seen,
                        CombineMemberPath(prefix, property.Name),
                        property.PropertyType,
                        () => getter.Invoke(owner, null));
                }
                type = type.BaseType;
            }
        }

        private static bool LooksLikeStatusHandlerType(Type type)
        {
            if (type == null)
                return false;

            string fullName = type.FullName ?? string.Empty;
            return fullName == "BrilliantSkies.Common.StatusChecking.StatusHandler" ||
                   fullName.EndsWith(".StatusHandler", StringComparison.Ordinal);
        }

        private static string CombineMemberPath(string prefix, string memberName)
        {
            if (string.IsNullOrEmpty(prefix))
                return memberName ?? string.Empty;
            if (string.IsNullOrEmpty(memberName))
                return prefix;
            return prefix + "." + memberName;
        }

        private static void TryAddStatusCollectionSnapshot(
            List<StatusCollectionSnapshot> snapshots,
            HashSet<object> seen,
            string memberName,
            Type memberType,
            Func<object> valueFactory)
        {
            object value;
            try { value = valueFactory(); }
            catch { return; }

            if (!(value is IList list) ||
                list.IsReadOnly ||
                !seen.Add(list))
            {
                return;
            }

            Type elementType = CollectionElementType(memberType) ??
                               CollectionElementType(value.GetType());
            if (elementType == null ||
                !elementType.IsAssignableFrom(typeof(Block)))
            {
                return;
            }

            snapshots.Add(new StatusCollectionSnapshot(
                memberName,
                list,
                elementType));
        }

        private static Type CollectionElementType(Type type)
        {
            if (type == null)
                return null;
            if (type.IsArray)
                return type.GetElementType();
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) ||
                    definition == typeof(IList<>) ||
                    definition == typeof(ICollection<>) ||
                    definition == typeof(IEnumerable<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            foreach (Type iface in type.GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return iface.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static StatusCollectionSnapshot FindSnapshotForList(
            IEnumerable<StatusCollectionSnapshot> snapshots,
            IList list)
        {
            foreach (StatusCollectionSnapshot snapshot in snapshots)
            {
                if (ReferenceEquals(snapshot.List, list))
                    return snapshot;
            }
            return null;
        }

        private static Exception UnwrapReflectionException(Exception exception)
        {
            return exception is TargetInvocationException target &&
                   target.InnerException != null
                ? target.InnerException
                : exception;
        }

        private void PreSizeKnownRegistries()
        {
            AllConstruct construct = _constructScopes.Count > 0 ? _constructScopes.Peek() : null;
            int sourceBlocks = construct?.AllBasics?.AliveAndDead?.Count ?? 0;
            int available = 0;
            int registerSync = 0;
            int controlSurfaces = 0;
            int statusChecks = 0;
            foreach (CapturedBlockState record in _captured)
            {
                Block block = record.Block;
                if (block == null)
                    continue;
                if (record.Available)
                {
                    available++;
                    if (block is IControlSurface)
                        controlSurfaces++;
                    if (OverridesCheckStatus(block))
                        statusChecks++;
                }
                if (record.RegisterSync)
                    registerSync++;
            }

            int presized = 0;
            IMainConstructBlock main = FirstCapturedMainConstruct();
            if (main != null && TryEnsureCapacity(main.iBlockTypeStorage?.ControlSurfaceBlocks, controlSurfaces))
                presized++;

            _trace?.Event(
                "v3-presize",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("source_alive_dead_blocks", sourceBlocks),
                FastBlueprintLoadTrace.Pair("captured", _captured.Count),
                FastBlueprintLoadTrace.Pair("available", available),
                FastBlueprintLoadTrace.Pair("sync", registerSync),
                FastBlueprintLoadTrace.Pair("control_surfaces", controlSurfaces),
                FastBlueprintLoadTrace.Pair("status_checks", statusChecks),
                FastBlueprintLoadTrace.Pair("main_construct_buckets", _capturedByMainConstruct.Count),
                FastBlueprintLoadTrace.Pair("construct_owner_buckets", _capturedByOwnerConstruct.Count),
                FastBlueprintLoadTrace.Pair("presized_targets", presized),
                FastBlueprintLoadTrace.Pair("unsafe_probe", FastBlueprintLoadRouter.ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("unsafe_probe_active", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics));
        }

        private IMainConstructBlock FirstCapturedMainConstruct()
        {
            foreach (CapturedBlockState record in _captured)
            {
                if (record.Block?.MainConstruct != null)
                    return record.Block.MainConstruct;
            }

            return null;
        }

        private void BucketCapturedBlock(Block block)
        {
            if (block == null)
                return;

            AddBucket(
                _capturedByMainConstruct,
                block.MainConstruct,
                _captured[_captured.Count - 1]);
            object owner = block.IsOnSubConstructable
                ? (object)block.SubConstruct
                : block.MainConstruct;
            AddBucket(
                _capturedByOwnerConstruct,
                owner,
                _captured[_captured.Count - 1]);
        }

        private static void AddBucket(
            Dictionary<object, List<CapturedBlockState>> buckets,
            object key,
            CapturedBlockState record)
        {
            if (buckets == null || key == null || record == null)
                return;
            if (!buckets.TryGetValue(key, out List<CapturedBlockState> records))
            {
                records = new List<CapturedBlockState>();
                buckets.Add(key, records);
            }
            records.Add(record);
        }

        private void MeasureFlushPart(
            string phase,
            ref long ticks,
            ref int count,
            Action action)
        {
            if (_trace == null)
            {
                action();
                count++;
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            ticks += timer.ElapsedTicks;
            count++;
            _trace.Heartbeat(
                "V3 flush " + phase,
                count,
                _captured.Count,
                "records");
        }

        private void LogFlushPhaseSummary()
        {
            LogFlushPhase("fireReset", _fireResetCount, _fireResetTicks);
            LogFlushPhase("acbProcessing", _acbProcessingCount, _acbProcessingTicks);
            LogFlushPhase("controlSurfaces", _controlSurfaceCount, _controlSurfaceTicks);
            LogFlushPhase("mainCatalogue", _mainCatalogueCount, _mainCatalogueTicks);
            LogFlushPhase("constructCatalogue", _constructCatalogueCount, _constructCatalogueTicks);
            LogFlushPhase("statusChecks", _statusCheckCount, _statusCheckTicks);
            LogFlushPhase("syncRegistration", _syncFlushCount, _syncRegistrationTicks,
                FastBlueprintLoadTrace.Pair("sync_skipped", _syncSkippedCount),
                FastBlueprintLoadTrace.Pair("unsafe_probe", FastBlueprintLoadRouter.ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("unsafe_probe_active", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_invalid", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_valid", !FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("do_not_save", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics));
            LogFlushPhase("statusSkipped", _statusCheckSkippedCount, 0L,
                FastBlueprintLoadTrace.Pair("unsafe_probe", FastBlueprintLoadRouter.ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("unsafe_probe_active", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_invalid", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("correctness_valid", !FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics),
                FastBlueprintLoadTrace.Pair("do_not_save", FastBlueprintLoadRouter.AnyUnsafeProbeActiveForDiagnostics));
        }

        private void LogFlushPhase(
            string name,
            int count,
            long ticks,
            params KeyValuePair<string, object>[] fields)
        {
            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>())
            {
                FastBlueprintLoadTrace.Pair("registry", name),
                FastBlueprintLoadTrace.Pair("count", count),
                FastBlueprintLoadTrace.Pair("elapsed_ms", ticks * 1000.0 / Stopwatch.Frequency)
            };
            _trace?.Event(
                "v3-flush-registry",
                "v3.flush." + name,
                advLogger: true,
                merged.ToArray());
        }

        private static bool TryEnsureCapacity(object target, int capacity)
        {
            if (target == null || capacity <= 0)
                return false;
            try
            {
                MethodInfo ensure = target.GetType().GetMethod("EnsureCapacity", new[] { typeof(int) });
                if (ensure != null)
                {
                    ensure.Invoke(target, new object[] { capacity });
                    return true;
                }

                PropertyInfo capacityProperty = target.GetType().GetProperty("Capacity");
                if (capacityProperty != null && capacityProperty.CanWrite)
                {
                    object current = capacityProperty.CanRead ? capacityProperty.GetValue(target, null) : null;
                    if (!(current is int currentCapacity) || currentCapacity < capacity)
                        capacityProperty.SetValue(target, capacity, null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void DisableAndReplayFallback(Exception exception, string reason)
        {
            _disabled = true;
            _trace?.Event(
                "v3-flush-failed",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("reason", reason ?? string.Empty),
                FastBlueprintLoadTrace.Pair("exception", exception?.GetType().Name ?? string.Empty),
                FastBlueprintLoadTrace.Pair("message", exception?.Message ?? string.Empty));
            _trace?.Event(
                "v3-fallback",
                "v3",
                advLogger: true,
                FastBlueprintLoadTrace.Pair("reason", "safe bulk disabled; replaying uncaptured vanilla base state changes"));
            if (exception != null)
                FastBlueprintLoadRouter.LogException("flush V3 bulk blueprint block state", exception);

            foreach (CapturedBlockState record in _captured)
            {
                if (record.Flushed || record.Block == null)
                    continue;
                try
                {
                    FastBlueprintLoadRouter.InvokeVanillaBlockStateChanged(
                        record.Block,
                        new BlockStateChange(record.Type));
                    record.Flushed = true;
                    _fallbackReplayCount++;
                }
                catch (Exception replayException)
                {
                    _trace?.Exception("v3-fallback-replay", replayException);
                    FastBlueprintLoadRouter.LogException("replay V3 fallback block state", replayException);
                }
            }
        }

        private bool HasUnflushedRecords()
        {
            foreach (CapturedBlockState record in _captured)
            {
                if (!record.Flushed && record.Block != null)
                    return true;
            }

            return false;
        }

        private void PopConstructScope(AllConstruct construct)
        {
            if (_constructScopes.Count == 0)
                return;
            if (ReferenceEquals(_constructScopes.Peek(), construct))
            {
                _constructScopes.Pop();
                return;
            }

            var remaining = new Stack<AllConstruct>();
            while (_constructScopes.Count > 0)
            {
                AllConstruct current = _constructScopes.Pop();
                if (ReferenceEquals(current, construct))
                    break;
                remaining.Push(current);
            }

            while (remaining.Count > 0)
                _constructScopes.Push(remaining.Pop());
        }

        private static bool OverridesCheckStatus(Block block)
        {
            MethodInfo method = block?.GetType().GetMethod("CheckStatus");
            return method != null && method.DeclaringType != typeof(Block);
        }

        internal sealed class Stage2Scope : IDisposable
        {
            private readonly FastBlueprintV3BulkLoadContext _context;
            private readonly AllConstruct _construct;
            private bool _completed;
            private bool _disposed;

            internal Stage2Scope(
                FastBlueprintV3BulkLoadContext context,
                AllConstruct construct)
            {
                _context = context;
                _construct = construct;
            }

            internal void Complete()
            {
                if (_completed)
                    return;
                _completed = true;
                _context.ScopeComplete(_construct);
            }

            internal void Fail(Exception exception)
            {
                if (_completed)
                    return;
                _completed = true;
                _context.ScopeFailed(_construct, exception);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (!_completed)
                    _context.ScopeFailed(_construct, null);
            }
        }
    }

    internal sealed class FastBlueprintLoadConversionScope : IDisposable
    {
        private readonly FastBlueprintLoadTraceScope _traceScope;
        private readonly FastBlueprintV3BulkLoadContext _v3Context;
        private readonly Action _onDispose;
        private bool _completed;
        private bool _disposed;

        internal FastBlueprintLoadConversionScope(
            FastBlueprintLoadTraceScope traceScope,
            FastBlueprintV3BulkLoadContext v3Context,
            Action onDispose)
        {
            _traceScope = traceScope;
            _v3Context = v3Context;
            _onDispose = onDispose;
        }

        internal FastBlueprintLoadTrace Trace => _traceScope?.Trace;

        internal KeyValuePair<string, object>[] Fields =>
            _traceScope?.Fields ?? Array.Empty<KeyValuePair<string, object>>();

        internal void Complete(params KeyValuePair<string, object>[] fields)
        {
            if (_completed)
                return;
            _completed = true;
            _v3Context?.Dispose();
            _traceScope?.Complete(fields);
            Dispose();
        }

        internal void Fail(Exception exception, params KeyValuePair<string, object>[] fields)
        {
            if (_completed)
                return;
            _completed = true;
            _v3Context?.Dispose();
            _traceScope?.Complete(fields);
            Trace?.CraftLoadFailed(exception, fields);
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _v3Context?.Dispose(); }
            finally
            {
                try { _traceScope?.Dispose(); }
                finally { _onDispose?.Invoke(); }
            }
        }
    }

    internal sealed class FastBlueprintInitialiseStage2State
    {
        private readonly Stopwatch _timer;
        private readonly IDisposable _v3Scope;
        private bool _completed;
        private bool _disposed;

        internal FastBlueprintInitialiseStage2State(
            Stopwatch timer,
            IDisposable v3Scope)
        {
            _timer = timer;
            _v3Scope = v3Scope;
        }

        internal void Complete()
        {
            if (_completed)
                return;
            _completed = true;
            if (_v3Scope is FastBlueprintV3BulkLoadContext.Stage2Scope stage2)
                stage2.Complete();
            Dispose();
        }

        internal void Fail(Exception exception)
        {
            if (_completed)
                return;
            _completed = true;
            if (_v3Scope is FastBlueprintV3BulkLoadContext.Stage2Scope stage2)
                stage2.Fail(exception);
            Dispose();
        }

        internal void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _v3Scope?.Dispose(); }
            finally { FastBlueprintLoadRouter.EndDiagnosticPhase("block initialization", _timer); }
        }
    }

    internal sealed class FastBlueprintDiagnosticPhaseState
    {
        private readonly string _phase;
        private readonly Stopwatch _timer;
        private bool _completed;

        internal FastBlueprintDiagnosticPhaseState(string phase)
        {
            _phase = phase;
            _timer = FastBlueprintLoadRouter.BeginDiagnosticPhase(phase);
        }

        internal void Complete()
        {
            if (_completed)
                return;
            _completed = true;
            FastBlueprintLoadRouter.EndDiagnosticPhase(_phase, _timer);
        }
    }

    internal static class FastBlueprintLoadRouter
    {
        private enum V3DModuleTarget
        {
            None,
            Collider,
            Shell,
            Skin
        }

        internal const long LargeBlueprintLoadThresholdBytes = 64L * 1024L * 1024L;
        internal const long V2BlockDataPayloadThresholdBytes = 1L * 1024L * 1024L;
        internal const int V2BlockDataRecordThreshold = 10_000;
        internal const int V3BlockCountThreshold = 100_000;
        internal const long V3MetadataProbeThresholdBytes = 1L * 1024L * 1024L;

        private const byte BlockDataObjectIdBytes = 3;
        private const int JsonBufferBytes = 64 * 1024;
        private const int BlockBlockStateChangedMetadataToken = 0x06000D86;
        private const string ConstructableCollidersTypeName =
            "BrilliantSkies.Ftd.Constructs.Modules.All.Colliders.ConstructableColliders";
        private const string AllConstructShellTypeName =
            "BrilliantSkies.Ftd.Constructs.Modules.All.Shell.AllConstructShell";
        private const string MainConstructSkinCalcTypeName =
            "BrilliantSkies.Ftd.Constructs.Modules.Main.SkinCalcs.MainConstructSkinCalc";

        private static Type ConstructExtraInfoType =>
            ResolveConstructExtraInfoType();
        private static FieldInfo ConstructExtraInfoDataField =>
            AccessTools.Field(ConstructExtraInfoType, "_data");
        private static FieldInfo ConstructExtraInfoVersionField =>
            AccessTools.Field(ConstructExtraInfoType, "_versionSavedAt");
        private static MethodInfo ConstructExtraInfoConstructGetter =>
            ResolveConstructExtraInfoConstructGetter();
        private static readonly FieldInfo BaseFileSourceField =
            AccessTools.Field(typeof(BaseFile), "_fileSource");
        private static readonly MethodInfo BlueprintFileUpdateConstructMethod =
            AccessTools.Method(
                typeof(BlueprintFile),
                "UpdateConstructAndChildren",
                new[] { typeof(Blueprint), typeof(Func<int, int>) });
        private static readonly MethodInfo BlueprintFilePerformVersionUpdatesMethod =
            AccessTools.Method(
                typeof(BlueprintFile),
                "PerformVersionUpdatesOnBlueprint",
                new[] { typeof(Blueprint) });
        private static readonly ConditionalWeakTable<Blueprint, FastBlueprintLoadTrace> BlueprintTraces =
            new ConditionalWeakTable<Blueprint, FastBlueprintLoadTrace>();
        private static readonly ConditionalWeakTable<Blueprint, object> V3RoutedBlueprints =
            new ConditionalWeakTable<Blueprint, object>();
        private static MethodInfo _v3BlockStateChangedTarget;
        private static bool _v3BlockStatePatchInstalled;
        private static MethodBase _stage2ModuleCallsiteTarget;
        private static bool _stage2ModuleCallsitePatchInstalled;
        private static MethodBase _stage2ModuleExternalLinkupTarget;
        private static bool _stage2ModuleExternalLinkupPatchInstalled;
        private static int _stage2ModuleExternalLinkupPatchCount;
        private static readonly object Stage2ModuleLinkupMethodSync = new object();
        private static readonly Dictionary<Type, MethodInfo> Stage2ModuleLinkupMethods =
            new Dictionary<Type, MethodInfo>();

        [ThreadStatic]
        private static Stack<FastBlueprintLoadTrace> _activeTraces;
        [ThreadStatic]
        private static FastBlueprintV3BulkLoadContext _activeV3BulkContext;
        [ThreadStatic]
        private static bool _insideStage2ModuleCallsiteWrapper;

        internal static MethodBase ResolveBlueprintFileModelLoadDataTarget()
            => AccessTools.Method(
                typeof(BlueprintFile),
                nameof(BlueprintFile.Load),
                new[] { typeof(bool) });

        internal static MethodBase ResolveConstructExtraInfoDataArrayTarget() =>
            ConstructExtraInfoType == null
                ? null
                : AccessTools.Method(ConstructExtraInfoType, "DataArray");

        internal static MethodBase ResolveConstructExtraInfoProvideInfoToBlocksTarget() =>
            ConstructExtraInfoType == null
                ? null
                : AccessTools.Method(ConstructExtraInfoType, "ProvideInfoToBlocks");

        internal static MethodBase ResolveConstructExtraInfoDoubleArrayTarget() =>
            ConstructExtraInfoType == null
                ? null
                : AccessTools.Method(ConstructExtraInfoType, "DoubleArray");

        internal static MethodBase ResolveConstructExtraInfoUpgradeConstructTarget() =>
            ConstructExtraInfoType == null
                ? null
                : AccessTools.Method(
                    ConstructExtraInfoType,
                    "UpgradeConstruct",
                    new[] { typeof(AllConstruct), typeof(Version) });

        internal static MethodBase ResolveBlockBlockStateChangedTarget() =>
            _v3BlockStateChangedTarget ?? TryResolveBlockBlockStateChangedTarget();

        internal static void InstallOptionalV3BlockStatePatch(Harmony harmony)
        {
            if (harmony == null || _v3BlockStatePatchInstalled)
                return;

            MethodInfo target = TryResolveBlockBlockStateChangedTarget();
            if (target == null)
            {
                LogInfo("V3 fast blueprint bulk load unavailable: Block.BlockStateChanged could not be resolved.");
                return;
            }

            try
            {
                MethodInfo prefix = AccessTools.Method(
                    typeof(Block_BlockStateChanged_V3BulkLoad_Patch),
                    nameof(Block_BlockStateChanged_V3BulkLoad_Patch.Prefix));
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _v3BlockStateChangedTarget = target;
                _v3BlockStatePatchInstalled = true;
            }
            catch (Exception exception)
            {
                _v3BlockStateChangedTarget = null;
                _v3BlockStatePatchInstalled = false;
                LogException("install optional V3 block state patch", exception);
            }
        }

        internal static void InstallOptionalStage2ModuleCallsitePatch(Harmony harmony)
        {
            if (harmony == null || _stage2ModuleCallsitePatchInstalled)
                return;

            MethodBase target = TryResolveStage2ModuleExternalLinkupTarget();
            if (target == null)
            {
                LogInfo("Stage2 module callsite diagnostics unavailable: module dictionary linkup target could not be resolved.");
                return;
            }

            try
            {
                Type patchType = typeof(AllConstruct_Stage2ModuleExternalLinkup_FastLoadDiagnostics_Patch);
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(patchType, "Prefix")),
                    postfix: new HarmonyMethod(AccessTools.Method(patchType, "Postfix")),
                    transpiler: new HarmonyMethod(AccessTools.Method(patchType, "Transpiler")),
                    finalizer: new HarmonyMethod(AccessTools.Method(patchType, "Finalizer")));
                _stage2ModuleCallsiteTarget = target;
                _stage2ModuleCallsitePatchInstalled = true;
                LogInfo("Stage2 module callsite diagnostics installed on module dictionary linkup loop.");
            }
            catch (Exception exception)
            {
                _stage2ModuleCallsiteTarget = null;
                _stage2ModuleCallsitePatchInstalled = false;
                LogInfo(
                    "Stage2 module callsite diagnostics unavailable: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static void InstallOptionalStage2ModuleExternalLinkupPatch(Harmony harmony)
        {
            if (harmony == null || _stage2ModuleExternalLinkupPatchInstalled)
                return;

            try
            {
                List<MethodInfo> targets = ResolveConcreteStage2ModuleExternalLinkupTargets();
                if (targets.Count == 0)
                {
                    LogInfo("Stage2 module external-linkup diagnostics unavailable: no concrete module methods could be resolved.");
                    return;
                }

                Type patchType = typeof(AllConstruct_Stage2ConcreteModuleExternalLinkup_FastLoadDiagnostics_Patch);
                var prefix = new HarmonyMethod(AccessTools.Method(patchType, "Prefix"));
                var postfix = new HarmonyMethod(AccessTools.Method(patchType, "Postfix"));
                var finalizer = new HarmonyMethod(AccessTools.Method(patchType, "Finalizer"));
                int installed = 0;
                int failed = 0;
                MethodInfo firstInstalled = null;
                foreach (MethodInfo target in targets)
                {
                    try
                    {
                        harmony.Patch(
                            target,
                            prefix: prefix,
                            postfix: postfix,
                            finalizer: finalizer);
                        installed++;
                        if (firstInstalled == null)
                            firstInstalled = target;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                if (installed == 0)
                {
                    LogInfo("Stage2 module external-linkup diagnostics unavailable: resolved methods could not be patched.");
                    return;
                }

                _stage2ModuleExternalLinkupTarget = firstInstalled;
                _stage2ModuleExternalLinkupPatchInstalled = true;
                _stage2ModuleExternalLinkupPatchCount = installed;
                LogInfo(
                    "Stage2 module external-linkup diagnostics installed on " +
                    installed.ToString(CultureInfo.InvariantCulture) +
                    " concrete/inherited module methods" +
                    (failed > 0
                        ? " (" + failed.ToString(CultureInfo.InvariantCulture) + " unavailable)."
                        : "."));
            }
            catch (Exception exception)
            {
                _stage2ModuleExternalLinkupTarget = null;
                _stage2ModuleExternalLinkupPatchInstalled = false;
                _stage2ModuleExternalLinkupPatchCount = 0;
                LogInfo(
                    "Stage2 module external-linkup per-module diagnostics unavailable: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static MethodBase ResolveAllConstructInitialiseStage2Target() =>
            AccessTools.Method(typeof(AllConstruct), nameof(AllConstruct.InitialiseStage2));

        internal static MethodBase ResolveStage2ModuleExternalLinkupTarget()
        {
            try
            {
                return _stage2ModuleExternalLinkupTarget ??
                       TryResolveStage2ModuleExternalLinkupTarget();
            }
            catch
            {
                return null;
            }
        }

        private static MethodBase TryResolveStage2ModuleExternalLinkupTarget()
        {
            try
            {
                Type genericType = FindLoadedTypeByFullName(
                    "BrilliantSkies.Common.Modules.AllConstructExtraTypes`3");
                if (genericType == null)
                    return null;

                return genericType.GetMethod(
                    "LinkUpExternallyAfterBlocksInitialising");
            }
            catch
            {
                return null;
            }
        }

        private static List<MethodInfo> ResolveConcreteStage2ModuleExternalLinkupTargets()
        {
            var targets = new List<MethodInfo>();
            var seen = new HashSet<MethodInfo>();
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { return targets; }

            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (!IsConcreteStage2ModuleType(type))
                        continue;

                    AddStage2ModuleExternalLinkupTarget(
                        type,
                        declaredOnly: true,
                        seen,
                        targets);
                    AddStage2ModuleExternalLinkupTarget(
                        type,
                        declaredOnly: false,
                        seen,
                        targets);
                }
            }

            targets.Sort(
                (left, right) => string.Compare(
                    left.DeclaringType?.FullName,
                    right.DeclaringType?.FullName,
                    StringComparison.Ordinal));
            return targets;
        }

        private static void AddStage2ModuleExternalLinkupTarget(
            Type type,
            bool declaredOnly,
            HashSet<MethodInfo> seen,
            List<MethodInfo> targets)
        {
            MethodInfo method;
            try
            {
                BindingFlags flags =
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;
                if (declaredOnly)
                    flags |= BindingFlags.DeclaredOnly;

                method = type.GetMethod(
                    "LinkUpExternallyAfterBlocksInitialising",
                    flags,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
            }
            catch
            {
                return;
            }

            if (!IsPatchableStage2ModuleExternalLinkupTarget(method))
                return;

            if (seen.Add(method))
                targets.Add(method);
        }

        private static bool IsPatchableStage2ModuleExternalLinkupTarget(MethodInfo method) =>
            method != null &&
            !method.IsAbstract &&
            !method.IsStatic &&
            !method.ContainsGenericParameters &&
            method.DeclaringType != null &&
            method.ReturnType == typeof(void);

        private static bool IsConcreteStage2ModuleType(Type type)
        {
            if (type == null ||
                type.IsAbstract ||
                type.IsInterface ||
                type.ContainsGenericParameters)
            {
                return false;
            }

            try
            {
                foreach (Type iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType)
                    {
                        string name = iface.GetGenericTypeDefinition().FullName;
                        if (name == "BrilliantSkies.Common.Modules.IAllConstructModule`3" ||
                            name == "BrilliantSkies.Common.Modules.IMainConstructModule`3")
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            Type current = type;
            while (current != null)
            {
                string name = current.FullName ?? current.Name;
                if (name == "AllConstructModule" ||
                    name == "MainConstructModule" ||
                    name.StartsWith("BrilliantSkies.Common.Modules.AbstractAllConstructModule", StringComparison.Ordinal) ||
                    name.StartsWith("BrilliantSkies.Common.Modules.AbstractMainConstructModule", StringComparison.Ordinal))
                {
                    return true;
                }
                current = current.BaseType;
            }

            return false;
        }

        private static Type FindLoadedTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                assemblies = Array.Empty<Assembly>();
            }

            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null)
                    continue;

                try
                {
                    Type type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Some FtD/Unity assemblies do not tolerate eager reflection on all runtimes.
                }
            }

            string[] assemblyNames =
            {
                "Common",
                "FtD",
                "Assembly-CSharp",
                "Core"
            };
            foreach (string assemblyName in assemblyNames)
            {
                try
                {
                    Type type = Type.GetType(
                        fullName + ", " + assemblyName,
                        throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            try
            {
                return AccessTools.TypeByName(fullName);
            }
            catch
            {
                return null;
            }
        }

        internal static MethodBase ResolvePartStatusRegisterCheckableBlockTarget()
            => ResolvePartStatusHandlerMethod("RegisterCheckableBlock");

        internal static MethodBase ResolvePartStatusUnregisterCheckableBlockTarget()
            => ResolvePartStatusHandlerMethod("UnregisterCheckableBlock");

        internal static bool TryLoadBlueprint(
            BlueprintFile file,
            bool setNameFromFile,
            out Blueprint blueprint)
        {
            blueprint = null;
            string path = BlueprintFilePath(file);
            long fileBytes = BlueprintFileLength(path);
            bool routeByPayload = !string.IsNullOrWhiteSpace(path) &&
                                  ShouldRoutePayload(FastBlueprintLoadTier.V1, fileBytes);
            bool metadataProbe = !routeByPayload &&
                                 !string.IsNullOrWhiteSpace(path) &&
                                 ShouldProbeV3Metadata(fileBytes);
            bool route = routeByPayload || metadataProbe;
            FastBlueprintLoadTrace trace = null;
            if (DiagnosticsEnabled &&
                !string.IsNullOrWhiteSpace(path) &&
                (route || fileBytes >= LargeBlueprintLoadThresholdBytes || ProfileData?.FastBlueprintLoadSmallBlueprintTesting == true))
            {
                trace = FastBlueprintLoadTrace.TryStart(path, fileBytes, CurrentTier);
                trace.RouteDecision(
                    diagnostics: true,
                    smallBlueprintTesting: ProfileData?.FastBlueprintLoadSmallBlueprintTesting == true,
                    thresholdBytes: LargeBlueprintLoadThresholdBytes,
                    routed: route,
                    reason: routeByPayload
                        ? FastBlueprintLoadRouteReason()
                        : metadataProbe
                            ? "v3-metadata-probe"
                            : RouteSkipReason(FastBlueprintLoadTier.V1, fileBytes));
            }

            if (!route)
            {
                trace?.Complete("delegated-to-vanilla");
                return false;
            }

            Stopwatch timer = Stopwatch.StartNew();
            trace?.PhaseStart("V1 streamed JSON blueprint model load");
            try
            {
                BlueprintFileModel model = LoadBlueprintFileModelFromPath(
                    path,
                    PreserveReferencesHandling.None,
                    JsonConverters.Converters,
                    trace,
                    fileBytes);
                trace?.BlueprintMetadata(model);
                bool routeLoadedModel = ShouldRouteLoadedBlueprintModel(
                    model,
                    fileBytes,
                    routeByPayload,
                    out string loadedModelRouteReason);
                trace?.Event(
                    "v3-metadata-route",
                    "route",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("routed", routeLoadedModel),
                    FastBlueprintLoadTrace.Pair("reason", loadedModelRouteReason),
                    FastBlueprintLoadTrace.Pair("file_bytes", fileBytes),
                    FastBlueprintLoadTrace.Pair("saved_total_blocks", ModelSavedTotalBlockCount(model)),
                    FastBlueprintLoadTrace.Pair("block_ids_count", ModelBlockIdsCount(model)),
                    FastBlueprintLoadTrace.Pair("block_count_threshold", V3BlockCountThreshold));
                if (!routeLoadedModel)
                {
                    trace?.Complete("delegated-to-vanilla-after-v3-metadata-probe");
                    return false;
                }

                blueprint = PrepareBlueprintFromModel(file, model, setNameFromFile);
                if (blueprint == null)
                {
                    trace?.Event(
                        "fallback",
                        "v1-json",
                        advLogger: true,
                        FastBlueprintLoadTrace.Pair("reason", "null-blueprint"));
                    trace?.Complete("delegated-to-vanilla");
                    return false;
                }

                RegisterTrace(blueprint, trace);
                if (ShouldStartV3BulkLoadForModel(model, fileBytes, routeByPayload))
                    RegisterV3RoutedBlueprint(blueprint);
                trace?.PhaseEnd("V1 streamed JSON blueprint model load", timer);
                return true;
            }
            catch (Exception exception)
            {
                trace?.PhaseEnd(
                    "V1 streamed JSON blueprint model load failed",
                    timer,
                    FastBlueprintLoadTrace.Pair("completed", false));
                trace?.Exception("v1-json", exception);
                trace?.Complete("delegated-to-vanilla-after-exception");
                LogException("stream blueprint JSON load from " + path, exception);
                return false;
            }
        }

        internal static bool TryHandleConstructExtraInfoDataArray(object instance)
        {
            if (!ShouldUseTier(FastBlueprintLoadTier.V2) ||
                instance == null ||
                ConstructExtraInfoDataField == null ||
                ConstructExtraInfoVersionField == null ||
                ConstructExtraInfoConstructGetter == null)
            {
                return false;
            }

            var data = ConstructExtraInfoDataField.GetValue(instance) as byte[];
            if (data == null || data.Length == 0)
                return true;

            FastBlueprintLoadTrace trace = CurrentTrace;
            bool standaloneTrace = false;
            if (trace == null && DiagnosticsEnabled)
            {
                trace = FastBlueprintLoadTrace.TryStartStandalone(
                    "construct-extra-info",
                    data.LongLength,
                    CurrentTier);
                standaloneTrace = trace != null;
                trace?.RouteDecision(
                    diagnostics: true,
                    smallBlueprintTesting: ProfileData?.FastBlueprintLoadSmallBlueprintTesting == true,
                    thresholdBytes: LargeBlueprintLoadThresholdBytes,
                    routed: true,
                    reason: "standalone-v2");
            }

            if (standaloneTrace)
                PushTrace(trace);
            try
            {
                Stopwatch total = BeginDiagnosticPhase("V2 block-data fast load total");
                try
                {
                    var version = ConstructExtraInfoVersionField.GetValue(instance) as Version;
                    var construct = ConstructExtraInfoConstructGetter.Invoke(instance, null) as AllConstruct;
                    if (construct == null)
                    {
                        if (standaloneTrace)
                            trace?.Complete("standalone-v2-no-construct");
                        return false;
                    }

                    Stopwatch scan = BeginDiagnosticPhase("V2 block-data scan");
                    FastBlueprintBlockDataRecord[] records = ScanBlockData(data, trace);
                    EndDiagnosticPhase("V2 block-data scan", scan);

                    if (!ShouldRouteV2BlockData(data.LongLength, records.Length, out string routeReason))
                    {
                        trace?.Event(
                            "v2-skip",
                            "v2-block-data",
                            advLogger: true,
                            FastBlueprintLoadTrace.Pair("block_data_bytes", data.LongLength),
                            FastBlueprintLoadTrace.Pair("record_count", records.Length),
                            FastBlueprintLoadTrace.Pair("reason", routeReason));
                        EndDiagnosticPhase("V2 block-data fast load total", total);
                        if (standaloneTrace)
                            trace?.Complete("standalone-v2-skipped");
                        return false;
                    }

                    Stopwatch predecode = BeginDiagnosticPhase("V2 block-data parallel predecode");
                    SuperLoader[] loaders = PredecodeBlockData(data, records, trace);
                    EndDiagnosticPhase("V2 block-data parallel predecode", predecode);

                    Stopwatch apply = BeginDiagnosticPhase("V2 block-data serial apply");
                    FastBlueprintBlockApplyStats applyStats =
                        ApplyDecodedBlockData(construct, version, records, loaders, trace);
                    EndDiagnosticPhase("V2 block-data serial apply", apply);
                    trace?.Event(
                        "v2-summary",
                        "v2-block-data",
                        advLogger: true,
                        FastBlueprintLoadTrace.Pair("payload_bytes", data.LongLength),
                        FastBlueprintLoadTrace.Pair("version", version?.ToString() ?? string.Empty),
                        FastBlueprintLoadTrace.Pair("alive_dead_blocks", construct.AllBasics.AliveAndDead.Count),
                        FastBlueprintLoadTrace.Pair("record_count", records.Length),
                        FastBlueprintLoadTrace.Pair("loaded", applyStats.Loaded),
                        FastBlueprintLoadTrace.Pair("skipped_null", applyStats.SkippedNull),
                        FastBlueprintLoadTrace.Pair("skipped_out_of_range", applyStats.SkippedOutOfRange));
                    EndDiagnosticPhase("V2 block-data fast load total", total);
                    if (standaloneTrace)
                        trace?.Complete("standalone-v2-complete");
                    return true;
                }
                catch (Exception exception)
                {
                    EndDiagnosticPhase("V2 block-data fast load failed", total);
                    trace?.Exception("v2-block-data", exception);
                    if (standaloneTrace)
                        trace?.Complete("standalone-v2-exception");
                    LogException("fast-load blueprint block data", exception);
                    return false;
                }
            }
            finally
            {
                if (standaloneTrace)
                    PopTrace();
            }
        }

        internal static bool ShouldRouteForVerification(
            FastBlueprintLoadTier selected,
            FastBlueprintLoadTier minimum,
            long byteLength,
            bool smallBlueprintTesting)
        {
            return selected >= minimum &&
                   selected != FastBlueprintLoadTier.Off &&
                   (smallBlueprintTesting || byteLength >= LargeBlueprintLoadThresholdBytes);
        }

        internal static bool ShouldRouteV3ForVerification(
            FastBlueprintLoadTier selected,
            long byteLength,
            int savedTotalBlocks,
            int blockIdsCount,
            bool smallBlueprintTesting,
            bool blockCountRouting,
            out string reason) =>
            ShouldRouteV3ForMetadata(
                selected,
                byteLength,
                savedTotalBlocks,
                blockIdsCount,
                smallBlueprintTesting,
                blockCountRouting,
                out reason);

        internal static bool ShouldProbeV3MetadataForVerification(
            FastBlueprintLoadTier selected,
            long byteLength,
            bool smallBlueprintTesting,
            bool blockCountRouting) =>
            selected == FastBlueprintLoadTier.V3 &&
            !smallBlueprintTesting &&
            blockCountRouting &&
            byteLength >= V3MetadataProbeThresholdBytes &&
            byteLength < LargeBlueprintLoadThresholdBytes;

        internal static BlueprintFileModel LoadBlueprintFileModelFromJsonForVerification(
            string json,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            using (var reader = new StringReader(json ?? string.Empty))
            using (var jsonReader = new JsonTextReader(reader))
            {
                return CreateSerializer(referencesHandling, converters)
                    .Deserialize<BlueprintFileModel>(jsonReader);
            }
        }

        internal static FastBlueprintBlockDataRecord[] ScanBlockDataForVerification(
            byte[] data) =>
            ScanBlockData(data);

        internal static int[] PredecodeBlockIdsForVerification(byte[] data)
        {
            FastBlueprintBlockDataRecord[] records = ScanBlockData(data);
            SuperLoader[] loaders = PredecodeBlockData(data, records);
            return loaders.Select(loader => checked((int)loader.Id)).ToArray();
        }

        internal static SuperLoader[] PredecodeBlockLoadersForVerification(byte[] data)
        {
            FastBlueprintBlockDataRecord[] records = ScanBlockData(data);
            return PredecodeBlockData(data, records);
        }

        internal static string BuildTracePathForVerification(
            string root,
            string blueprintName,
            DateTime utcTimestamp,
            string sessionId) =>
            FastBlueprintLoadTrace.BuildLogPathForVerification(
                root,
                blueprintName,
                utcTimestamp,
                sessionId);

        internal static string SanitizeTraceFileNameForVerification(string name) =>
            FastBlueprintLoadTrace.SanitizeFileNameForVerification(name);

        internal static string FormatRouteDecisionForVerification(
            FastBlueprintLoadTier tier,
            bool diagnostics,
            bool smallBlueprintTesting,
            long fileBytes,
            bool routed,
            string reason) =>
            FastBlueprintLoadTrace.FormatRouteDecisionForVerification(
                tier,
                diagnostics,
                smallBlueprintTesting,
                fileBytes,
                LargeBlueprintLoadThresholdBytes,
                routed,
                reason);

        internal static bool ShouldRouteV2BlockDataForVerification(
            long blockDataBytes,
            int recordCount,
            bool forceV2,
            bool smallBlueprintTesting,
            bool diagnostics,
            out string reason) =>
            ShouldRouteV2BlockData(
                blockDataBytes,
                recordCount,
                forceV2,
                out reason);

        internal static bool UnsafeProbeActiveForVerification(
            FastBlueprintLoadUnsafeProbeMode mode,
            bool diagnostics,
            FastBlueprintLoadTier tier) =>
            UnsafeProbeActive(mode, diagnostics, tier);

        internal static FastBlueprintV3CaptureDecision V3CaptureDecisionForVerification(
            BlockStateChangeType type) =>
            FastBlueprintV3BulkLoadContext.ShouldCaptureForVerification(type);

        internal static bool V3PreflightTargetsAvailableForVerification() =>
            BlockBlockStateChangedMetadataToken == 0x06000D86;

        internal static bool TryCaptureV3BlockStateChange(
            Block block,
            IBlockStateChange change) =>
            _activeV3BulkContext?.TryCapture(block, change) == true;

        internal static IDisposable BeginV3InitialiseStage2(AllConstruct construct) =>
            _activeV3BulkContext?.BeginInitialiseStage2(construct);

        internal static void FlushV3BeforeBlockData() =>
            _activeV3BulkContext?.FlushBeforeBlockData("before-block-data");

        internal static FastBlueprintLoadConversionScope BeginBlueprintConversionTrace(
            Blueprint blueprint)
        {
            FastBlueprintLoadTrace trace = TraceForBlueprint(blueprint);
            bool startV3 = ShouldStartV3BulkLoad(blueprint);
            bool standaloneTrace = false;
            if (trace == null && DiagnosticsEnabled)
            {
                trace = FastBlueprintLoadTrace.TryStartStandalone(
                    BlueprintTraceName(blueprint),
                    BlueprintPayloadBytes(blueprint),
                    CurrentTier);
                standaloneTrace = trace != null;
            }

            if (trace == null && !startV3)
                return null;

            KeyValuePair<string, object>[] fields = BlueprintConversionFields(
                blueprint,
                standaloneTrace);
            FastBlueprintLoadTraceScope traceScope = null;
            bool tracePushed = false;
            if (trace != null)
            {
                trace.CraftLoadStart(fields);
                PushTrace(trace);
                tracePushed = true;
                traceScope = new FastBlueprintLoadTraceScope(
                    trace,
                    "blueprint conversion",
                    null,
                    fields);
            }

            FastBlueprintV3BulkLoadContext v3Context = startV3
                ? BeginV3BulkLoadContext(trace)
                : null;
            return new FastBlueprintLoadConversionScope(
                traceScope,
                v3Context,
                () =>
                {
                    if (tracePushed)
                        PopTrace();
                    if (ReferenceEquals(_activeV3BulkContext, v3Context))
                        _activeV3BulkContext = v3Context?.Previous;
                });
        }

        internal static void CompleteBlueprintConversionTrace(
            FastBlueprintLoadConversionScope scope,
            MainConstruct construct)
        {
            if (scope == null)
                return;
            FastBlueprintLoadTrace trace = scope.Trace;
            KeyValuePair<string, object>[] fields = AppendFields(
                scope.Fields,
                FastBlueprintLoadTrace.Pair("construct_loaded", construct != null),
                FastBlueprintLoadTrace.Pair(
                    "alive_dead_blocks",
                    construct?.AllBasics?.AliveAndDead?.Count ?? 0));
            scope.Complete(fields);
            trace?.CraftLoadComplete(fields);
            trace?.Complete("blueprint-conversion-complete");
        }

        internal static void FailBlueprintConversionTrace(
            FastBlueprintLoadConversionScope scope,
            Exception exception)
        {
            if (scope == null)
                return;
            FastBlueprintLoadTrace trace = scope.Trace;
            KeyValuePair<string, object>[] fields = AppendFields(
                scope.Fields,
                FastBlueprintLoadTrace.Pair("completed", false));
            scope.Fail(exception, fields);
            trace?.Complete("blueprint-conversion-failed");
        }

        internal static FastBlueprintDiagnosticPhaseState BeginDiagnosticPhaseState(string phase) =>
            new FastBlueprintDiagnosticPhaseState(phase);

        internal static Stopwatch BeginDiagnosticPhase(string phase)
        {
            if (!DiagnosticsEnabled)
                return null;
            FastBlueprintLoadTrace trace = CurrentTrace;
            trace?.PhaseStart(phase);
            return Stopwatch.StartNew();
        }

        internal static void EndDiagnosticPhase(string phase, Stopwatch timer)
        {
            if (timer == null)
                return;
            timer.Stop();
            FastBlueprintLoadTrace trace = CurrentTrace;
            if (trace != null)
            {
                trace.PhaseEnd(phase, timer);
                return;
            }

            LogDiagnostic(
                phase +
                " took " +
                timer.Elapsed.TotalMilliseconds.ToString("0.0 ms", CultureInfo.InvariantCulture));
        }

        internal static bool BeginStage2ConcreteModuleExternalLinkup(
            object module,
            MethodBase originalMethod,
            out Stopwatch timer)
        {
            timer = null;
            if (module == null)
                return true;

            V3DModuleTarget target = V3DTargetForModuleType(module.GetType());
            if (!_insideStage2ModuleCallsiteWrapper &&
                ShouldSkipV3DTargetForDiagnostics(target))
            {
                LogV3DUnsafeSkip(
                    target,
                    module.GetType().FullName ?? module.GetType().Name,
                    originalMethod,
                    -1,
                    -1);
                return false;
            }

            if (SkipStage2ModuleExternalLinkupForDiagnostics)
            {
                LogUnsafeProbeEvent(
                    CurrentTrace,
                    "stage2-module-external-linkup",
                    "skipped concrete Stage2 module external linkup",
                    FastBlueprintLoadTrace.Pair("module_type", module.GetType().FullName ?? module.GetType().Name),
                    FastBlueprintLoadTrace.Pair("method_declaring_type", originalMethod?.DeclaringType?.FullName ?? string.Empty));
                return false;
            }

            if (!DiagnosticsEnabled)
                return true;

            timer = Stopwatch.StartNew();
            return true;
        }

        internal static void EndStage2ConcreteModuleExternalLinkup(
            object module,
            MethodBase originalMethod,
            Stopwatch timer,
            bool threw)
        {
            if (timer == null)
                return;

            timer.Stop();
            FastBlueprintLoadTrace trace = CurrentTrace;
            if (trace == null)
                return;

            Type moduleType = module?.GetType();
            trace.Event(
                "stage2-module-linkup",
                "stage2.module",
                advLogger: false,
                FastBlueprintLoadTrace.Pair("module_type", moduleType?.FullName ?? moduleType?.Name ?? string.Empty),
                FastBlueprintLoadTrace.Pair("method_declaring_type", originalMethod?.DeclaringType?.FullName ?? string.Empty),
                FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds),
                FastBlueprintLoadTrace.Pair("threw", threw));

            if (!_insideStage2ModuleCallsiteWrapper)
            {
                V3DModuleTarget target = V3DTargetForModuleType(moduleType);
                if (target != V3DModuleTarget.None)
                {
                    LogV3DTargetLinkup(
                        trace,
                        target,
                        moduleType?.FullName ?? moduleType?.Name ?? string.Empty,
                        originalMethod,
                        -1,
                        -1,
                        timer.Elapsed.TotalMilliseconds,
                        skipped: false,
                        threw: threw);
                    LogV3DSubphaseUnsupported(
                        trace,
                        target,
                        moduleType?.FullName ?? moduleType?.Name ?? string.Empty,
                        originalMethod);
                }
            }
        }

        private static V3DModuleTarget V3DTargetForModuleType(Type type)
        {
            string name = type?.FullName ?? type?.Name ?? string.Empty;
            if (name == ConstructableCollidersTypeName)
                return V3DModuleTarget.Collider;
            if (name == AllConstructShellTypeName)
                return V3DModuleTarget.Shell;
            if (name == MainConstructSkinCalcTypeName)
                return V3DModuleTarget.Skin;
            return V3DModuleTarget.None;
        }

        private static bool ShouldSkipV3DTargetForDiagnostics(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return SkipV3ColliderLinkupForDiagnostics;
                case V3DModuleTarget.Shell:
                    return SkipV3ShellLinkupForDiagnostics;
                case V3DModuleTarget.Skin:
                    return SkipV3SkinCalcForDiagnostics;
                default:
                    return false;
            }
        }

        private static void LogV3DUnsafeSkip(
            V3DModuleTarget target,
            string moduleType,
            MethodBase method,
            int moduleIndex,
            int moduleCount)
        {
            if (target == V3DModuleTarget.None)
                return;

            string phase = V3DTargetPhaseName(target);
            KeyValuePair<string, object>[] fields = V3DTargetFields(
                target,
                moduleType,
                method,
                moduleIndex,
                moduleCount,
                elapsedMs: 0d,
                skipped: true,
                threw: false);
            LogUnsafeProbeEvent(
                CurrentTrace,
                phase,
                "skipped " + V3DTargetName(target) + " linkup",
                fields);
            CurrentTrace?.Event(
                "v3d-unsafe-skip",
                phase,
                advLogger: true,
                fields);
            CurrentTrace?.Event(
                V3DTargetEventName(target),
                phase,
                advLogger: true,
                fields);
        }

        private static void LogV3DTargetLinkup(
            FastBlueprintLoadTrace trace,
            V3DModuleTarget target,
            string moduleType,
            MethodBase method,
            int moduleIndex,
            int moduleCount,
            double elapsedMs,
            bool skipped,
            bool threw)
        {
            if (trace == null || target == V3DModuleTarget.None)
                return;

            trace.Event(
                V3DTargetEventName(target),
                V3DTargetPhaseName(target),
                advLogger: skipped,
                V3DTargetFields(
                    target,
                    moduleType,
                    method,
                    moduleIndex,
                    moduleCount,
                    elapsedMs,
                    skipped,
                    threw));
        }

        private static void LogV3DSubphaseUnsupported(
            FastBlueprintLoadTrace trace,
            V3DModuleTarget target,
            string moduleType,
            MethodBase method)
        {
            if (trace == null || target == V3DModuleTarget.None)
                return;

            var fields = new List<KeyValuePair<string, object>>(
                V3DTargetFields(
                    target,
                    moduleType,
                    method,
                    -1,
                    -1,
                    elapsedMs: 0d,
                    skipped: false,
                    threw: false))
            {
                FastBlueprintLoadTrace.Pair("supported", false),
                FastBlueprintLoadTrace.Pair("expected_type", V3DTargetExpectedType(target)),
                FastBlueprintLoadTrace.Pair("expected_method", V3DTargetExpectedMethod(target)),
                FastBlueprintLoadTrace.Pair("reason", "subphase target discovery pending"),
                FastBlueprintLoadTrace.Pair("fallback", "whole-module timing")
            };
            trace.Event(
                V3DTargetSubphaseEventName(target),
                V3DTargetSubphasePhaseName(target),
                advLogger: false,
                fields.ToArray());
            trace.Event(
                "v3d-target-unsupported",
                V3DTargetSubphasePhaseName(target),
                advLogger: false,
                fields.ToArray());
        }

        private static KeyValuePair<string, object>[] V3DTargetFields(
            V3DModuleTarget target,
            string moduleType,
            MethodBase method,
            int moduleIndex,
            int moduleCount,
            double elapsedMs,
            bool skipped,
            bool threw)
        {
            bool unsafeActive = AnyUnsafeProbeActiveForDiagnostics;
            return new[]
            {
                FastBlueprintLoadTrace.Pair("target", V3DTargetName(target)),
                FastBlueprintLoadTrace.Pair("module_type", moduleType ?? string.Empty),
                FastBlueprintLoadTrace.Pair("method_name", method?.Name ?? "LinkUpExternallyAfterBlocksInitialising"),
                FastBlueprintLoadTrace.Pair("method_declaring_type", method?.DeclaringType?.FullName ?? string.Empty),
                FastBlueprintLoadTrace.Pair("module_index", moduleIndex),
                FastBlueprintLoadTrace.Pair("module_count", moduleCount),
                FastBlueprintLoadTrace.Pair("elapsed_ms", elapsedMs),
                FastBlueprintLoadTrace.Pair("skipped", skipped),
                FastBlueprintLoadTrace.Pair("unsafe_probe", ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("correctness_valid", !unsafeActive),
                FastBlueprintLoadTrace.Pair("correctness_invalid", unsafeActive),
                FastBlueprintLoadTrace.Pair("do_not_save", unsafeActive),
                FastBlueprintLoadTrace.Pair("threw", threw)
            };
        }

        private static string V3DTargetName(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return "collider";
                case V3DModuleTarget.Shell:
                    return "shell";
                case V3DModuleTarget.Skin:
                    return "skin";
                default:
                    return "none";
            }
        }

        private static string V3DTargetEventName(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return "v3d-collider-linkup";
                case V3DModuleTarget.Shell:
                    return "v3d-shell-linkup";
                case V3DModuleTarget.Skin:
                    return "v3d-skin-calc-linkup";
                default:
                    return "v3d-target-linkup";
            }
        }

        private static string V3DTargetSubphaseEventName(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return "v3d-collider-subphase";
                case V3DModuleTarget.Shell:
                    return "v3d-shell-subphase";
                case V3DModuleTarget.Skin:
                    return "v3d-skin-calc-subphase";
                default:
                    return "v3d-target-subphase";
            }
        }

        private static string V3DTargetPhaseName(V3DModuleTarget target) =>
            "v3d." + V3DTargetName(target) + ".linkup";

        private static string V3DTargetSubphasePhaseName(V3DModuleTarget target) =>
            "v3d." + V3DTargetName(target) + ".subphase";

        private static string V3DTargetExpectedType(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return "BrilliantSkies.Common.Colliders.ConstructableColliderCommon`3";
                case V3DModuleTarget.Shell:
                    return AllConstructShellTypeName;
                case V3DModuleTarget.Skin:
                    return "BrilliantSkies.Common.Drag.SkinCalc`3";
                default:
                    return string.Empty;
            }
        }

        private static string V3DTargetExpectedMethod(V3DModuleTarget target)
        {
            switch (target)
            {
                case V3DModuleTarget.Collider:
                    return "ConstructableColliderCommon<TConstruct,TBlock,TPart> internal collider build calls";
                case V3DModuleTarget.Shell:
                    return "AllConstructShell.FindFirstX/FindFirstY/FindFirstZ";
                case V3DModuleTarget.Skin:
                    return "SkinCalc<MainConstruct,Block,BlockPart>.CalculateAllCalledOnce";
                default:
                    return string.Empty;
            }
        }

        internal static IEnumerable<CodeInstruction> AddInitialiseStage2SubphaseTimers(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            MethodInfo begin = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(BeginDiagnosticPhase));
            MethodInfo end = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(EndDiagnosticPhase));
            MethodInfo shouldUseModuleDiagnostics = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(ShouldUseStage2ModuleCallsiteDiagnostics));
            MethodInfo timedModuleDictionaryCall = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(CallStage2ModuleDictionaryLinkupWithDiagnostics));

            foreach (CodeInstruction instruction in instructions)
            {
                MethodBase method = instruction.operand as MethodBase;
                string phase = InitialiseStage2SubphaseForCall(method);
                if (phase == null)
                {
                    yield return instruction;
                    continue;
                }

                LocalBuilder timer = generator.DeclareLocal(typeof(Stopwatch));
                yield return new CodeInstruction(OpCodes.Ldstr, phase);
                yield return new CodeInstruction(OpCodes.Call, begin);
                yield return new CodeInstruction(OpCodes.Stloc, timer);

                if (IsStage2ModuleDictionaryLinkupCall(method))
                {
                    object vanillaCallLabel = generator.DefineLabel();
                    object endLabel = generator.DefineLabel();

                    yield return new CodeInstruction(
                        OpCodes.Call,
                        shouldUseModuleDiagnostics);
                    yield return new CodeInstruction(
                        OpCodes.Brfalse_S,
                        vanillaCallLabel);
                    yield return new CodeInstruction(OpCodes.Call, timedModuleDictionaryCall);
                    yield return new CodeInstruction(
                        OpCodes.Br_S,
                        endLabel);

                    var vanilla = new CodeInstruction(instruction);
                    AddLabelUntyped(vanilla, vanillaCallLabel);
                    yield return vanilla;

                    var done = new CodeInstruction(OpCodes.Nop);
                    AddLabelUntyped(done, endLabel);
                    yield return done;
                }
                else
                {
                    yield return instruction;
                }

                yield return new CodeInstruction(OpCodes.Ldstr, phase);
                yield return new CodeInstruction(OpCodes.Ldloc, timer);
                yield return new CodeInstruction(OpCodes.Call, end);
            }
        }

        internal static IEnumerable<CodeInstruction> AddStage2ModuleExternalLinkupCallsiteTimers(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            MethodInfo timedCall = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(CallStage2ModuleExternalLinkupWithDiagnostics));
            MethodInfo shouldUseDiagnostics = AccessTools.Method(
                typeof(FastBlueprintLoadRouter),
                nameof(ShouldUseStage2ModuleCallsiteDiagnostics));

            foreach (CodeInstruction instruction in instructions)
            {
                MethodBase method = instruction.operand as MethodBase;
                if (method == null ||
                    instruction.opcode != OpCodes.Callvirt ||
                    !IsStage2ModuleExternalLinkupDispatch(method))
                {
                    yield return instruction;
                    continue;
                }

                object vanillaCallLabel = generator.DefineLabel();
                object endLabel = generator.DefineLabel();

                var condition = new CodeInstruction(
                    OpCodes.Call,
                    shouldUseDiagnostics);
                MoveMetadataUntyped(instruction, condition);

                yield return condition;
                yield return new CodeInstruction(
                    OpCodes.Brfalse_S,
                    vanillaCallLabel);
                yield return new CodeInstruction(OpCodes.Call, timedCall);
                yield return new CodeInstruction(
                    OpCodes.Br_S,
                    endLabel);

                var vanilla = new CodeInstruction(instruction);
                AddLabelUntyped(vanilla, vanillaCallLabel);
                yield return vanilla;

                var done = new CodeInstruction(OpCodes.Nop);
                AddLabelUntyped(done, endLabel);
                yield return done;
            }
        }

        internal static bool ShouldUseStage2ModuleCallsiteDiagnostics() =>
            DiagnosticsEnabled ||
            SkipStage2ModuleExternalLinkupForDiagnostics ||
            SkipV3ColliderLinkupForDiagnostics ||
            SkipV3ShellLinkupForDiagnostics;

        private static bool IsStage2ModuleExternalLinkupDispatch(MethodBase method) =>
            method != null &&
            method.Name == "LinkUpExternallyAfterBlocksInitialising" &&
            method.DeclaringType != null &&
            method.DeclaringType.Name.StartsWith(
                "IAllConstructModule",
                StringComparison.Ordinal);

        private static bool IsStage2ModuleDictionaryLinkupCall(MethodBase method) =>
            method != null &&
            method.Name == "LinkUpExternallyAfterBlocksInitialising" &&
            method.DeclaringType != null &&
            method.DeclaringType.Name.StartsWith(
                "AllConstructExtraTypes",
                StringComparison.Ordinal);

        private static void AddLabelUntyped(CodeInstruction instruction, object label)
        {
            if (instruction == null || label == null)
                return;
            GetCodeInstructionList(instruction, "labels")?.Add(label);
        }

        private static void MoveMetadataUntyped(
            CodeInstruction source,
            CodeInstruction target)
        {
            if (source == null || target == null)
                return;
            IList sourceLabels = GetCodeInstructionList(source, "labels");
            IList targetLabels = GetCodeInstructionList(target, "labels");
            if (sourceLabels != null && targetLabels != null)
            {
                foreach (object label in sourceLabels)
                    targetLabels.Add(label);
                sourceLabels.Clear();
            }

            IList sourceBlocks = GetCodeInstructionList(source, "blocks");
            IList targetBlocks = GetCodeInstructionList(target, "blocks");
            if (sourceBlocks != null && targetBlocks != null)
            {
                foreach (object block in sourceBlocks)
                    targetBlocks.Add(block);
                sourceBlocks.Clear();
            }
        }

        private static IList GetCodeInstructionList(
            CodeInstruction instruction,
            string fieldName)
        {
            if (instruction == null)
                return null;
            try
            {
                return AccessTools.Field(
                    typeof(CodeInstruction),
                    fieldName)?.GetValue(instruction) as IList;
            }
            catch
            {
                return null;
            }
        }

        internal static void CallStage2ModuleDictionaryLinkupWithDiagnostics(
            object moduleDictionary)
        {
            if (moduleDictionary == null)
                throw new NullReferenceException(
                    "Stage2 module dictionary linkup target was null.");

            if (!ShouldUseStage2ModuleCallsiteDiagnostics())
            {
                InvokeStage2ModuleDictionaryLinkup(moduleDictionary);
                return;
            }

            if (SkipStage2ModuleExternalLinkupForDiagnostics)
            {
                LogUnsafeProbeEvent(
                    CurrentTrace,
                    "stage2-module-callsite-loop",
                    "skipped Stage2 module dictionary linkup",
                    FastBlueprintLoadTrace.Pair("module_dictionary_type", moduleDictionary.GetType().FullName ?? moduleDictionary.GetType().Name));
                return;
            }

            if (!TryCollectStage2ModuleDictionaryValues(
                    moduleDictionary,
                    out List<object> modules,
                    out string source))
            {
                CurrentTrace?.Event(
                    "stage2-module-callsite-loop-fallback",
                    "stage2.module.callsite",
                    advLogger: false,
                    FastBlueprintLoadTrace.Pair("module_dictionary_type", moduleDictionary.GetType().FullName ?? moduleDictionary.GetType().Name),
                    FastBlueprintLoadTrace.Pair("reason", source ?? "module-dictionary-unavailable"));
                InvokeStage2ModuleDictionaryLinkup(moduleDictionary);
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            CurrentTrace?.Event(
                "stage2-module-callsite-loop-start",
                "stage2.module.callsite",
                advLogger: false,
                FastBlueprintLoadTrace.Pair("module_dictionary_type", moduleDictionary.GetType().FullName ?? moduleDictionary.GetType().Name),
                FastBlueprintLoadTrace.Pair("module_count", modules.Count),
                FastBlueprintLoadTrace.Pair("source", source));

            try
            {
                for (int i = 0; i < modules.Count; i++)
                    CallStage2ModuleExternalLinkupWithDiagnostics(
                        modules[i],
                        i,
                        modules.Count);
            }
            finally
            {
                timer.Stop();
                CurrentTrace?.Event(
                    "stage2-module-callsite-loop-complete",
                    "stage2.module.callsite",
                    advLogger: false,
                    FastBlueprintLoadTrace.Pair("module_dictionary_type", moduleDictionary.GetType().FullName ?? moduleDictionary.GetType().Name),
                    FastBlueprintLoadTrace.Pair("module_count", modules.Count),
                    FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds));
            }
        }

        internal static void CallStage2ModuleExternalLinkupWithDiagnostics(object module)
        {
            CallStage2ModuleExternalLinkupWithDiagnostics(module, -1, -1);
        }

        private static void CallStage2ModuleExternalLinkupWithDiagnostics(
            object module,
            int moduleIndex,
            int moduleCount)
        {
            if (module == null)
                throw new NullReferenceException(
                    "Stage2 module external linkup module was null.");

            if (!ShouldUseStage2ModuleCallsiteDiagnostics())
            {
                InvokeStage2ModuleExternalLinkup(module);
                return;
            }

            if (SkipStage2ModuleExternalLinkupForDiagnostics)
            {
                LogUnsafeProbeEvent(
                    CurrentTrace,
                    "stage2-module-callsite-linkup",
                    "skipped Stage2 module callsite linkup",
                    FastBlueprintLoadTrace.Pair("module_type", module.GetType().FullName ?? module.GetType().Name),
                    FastBlueprintLoadTrace.Pair("module_index", moduleIndex),
                    FastBlueprintLoadTrace.Pair("module_count", moduleCount));
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            Type runtimeType = module.GetType();
            string moduleType = runtimeType.FullName ?? runtimeType.Name;
            MethodInfo method = ResolveStage2ModuleExternalLinkupMethod(runtimeType);
            V3DModuleTarget target = V3DTargetForModuleType(runtimeType);
            if (ShouldSkipV3DTargetForDiagnostics(target))
            {
                timer.Stop();
                LogV3DUnsafeSkip(
                    target,
                    moduleType,
                    method,
                    moduleIndex,
                    moduleCount);
                return;
            }

            bool previousCallsite = _insideStage2ModuleCallsiteWrapper;
            try
            {
                _insideStage2ModuleCallsiteWrapper = true;
                InvokeStage2ModuleExternalLinkup(module, method);
            }
            finally
            {
                _insideStage2ModuleCallsiteWrapper = previousCallsite;
                timer.Stop();
                CurrentTrace?.Event(
                    "stage2-module-callsite-linkup",
                    "stage2.module.callsite",
                    advLogger: false,
                    FastBlueprintLoadTrace.Pair("module_type", moduleType),
                    FastBlueprintLoadTrace.Pair("method_declaring_type", method?.DeclaringType?.FullName ?? string.Empty),
                    FastBlueprintLoadTrace.Pair("module_index", moduleIndex),
                    FastBlueprintLoadTrace.Pair("module_count", moduleCount),
                    FastBlueprintLoadTrace.Pair("elapsed_ms", timer.Elapsed.TotalMilliseconds));
                if (target != V3DModuleTarget.None)
                {
                    LogV3DTargetLinkup(
                        CurrentTrace,
                        target,
                        moduleType,
                        method,
                        moduleIndex,
                        moduleCount,
                        timer.Elapsed.TotalMilliseconds,
                        skipped: false,
                        threw: false);
                    LogV3DSubphaseUnsupported(
                        CurrentTrace,
                        target,
                        moduleType,
                        method);
                }
            }
        }

        private static bool TryCollectStage2ModuleDictionaryValues(
            object moduleDictionary,
            out List<object> modules,
            out string source)
        {
            modules = new List<object>();
            source = "unknown";

            FieldInfo field = FindFieldInHierarchy(
                moduleDictionary.GetType(),
                "_dictionary");
            if (field == null)
            {
                source = "missing-_dictionary-field";
                return false;
            }

            object value;
            try { value = field.GetValue(moduleDictionary); }
            catch (Exception exception)
            {
                source = "field-read-failed-" + exception.GetType().Name;
                return false;
            }

            if (!(value is IEnumerable enumerable))
            {
                source = "dictionary-not-enumerable";
                return false;
            }

            try
            {
                foreach (object entry in enumerable)
                {
                    object module = ExtractDictionaryEntryValue(entry);
                    if (module != null)
                        modules.Add(module);
                }
            }
            catch (Exception exception)
            {
                source = "dictionary-enumeration-failed-" + exception.GetType().Name;
                modules.Clear();
                return false;
            }

            source = field.DeclaringType?.FullName + "._dictionary";
            return true;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                try
                {
                    FieldInfo field = current.GetField(
                        name,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    if (field != null)
                        return field;
                }
                catch
                {
                }
            }
            return null;
        }

        private static object ExtractDictionaryEntryValue(object entry)
        {
            if (entry == null)
                return null;
            if (entry is DictionaryEntry dictionaryEntry)
                return dictionaryEntry.Value;

            try
            {
                PropertyInfo valueProperty = entry.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                return valueProperty?.GetValue(entry, null);
            }
            catch
            {
                return null;
            }
        }

        private static void InvokeStage2ModuleDictionaryLinkup(object moduleDictionary)
        {
            MethodInfo method = ResolveStage2ModuleDictionaryLinkupMethod(
                moduleDictionary.GetType());
            if (method == null)
            {
                throw new MissingMethodException(
                    moduleDictionary.GetType().FullName,
                    "LinkUpExternallyAfterBlocksInitialising");
            }

            try
            {
                method.Invoke(moduleDictionary, null);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException;
                if (inner != null)
                    ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
        }

        private static MethodInfo ResolveStage2ModuleDictionaryLinkupMethod(
            Type moduleDictionaryType)
        {
            if (moduleDictionaryType == null)
                return null;

            return AccessTools.Method(
                moduleDictionaryType,
                "LinkUpExternallyAfterBlocksInitialising");
        }

        private static void InvokeStage2ModuleExternalLinkup(object module)
        {
            MethodInfo method = ResolveStage2ModuleExternalLinkupMethod(module.GetType());
            InvokeStage2ModuleExternalLinkup(module, method);
        }

        private static void InvokeStage2ModuleExternalLinkup(
            object module,
            MethodInfo method)
        {
            if (method == null)
            {
                throw new MissingMethodException(
                    module.GetType().FullName,
                    "LinkUpExternallyAfterBlocksInitialising");
            }

            try
            {
                method.Invoke(module, null);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException;
                if (inner != null)
                    ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
        }

        private static MethodInfo ResolveStage2ModuleExternalLinkupMethod(Type moduleType)
        {
            if (moduleType == null)
                return null;

            lock (Stage2ModuleLinkupMethodSync)
            {
                if (Stage2ModuleLinkupMethods.TryGetValue(moduleType, out MethodInfo cached))
                    return cached;

                MethodInfo method = AccessTools.Method(
                    moduleType,
                    "LinkUpExternallyAfterBlocksInitialising");
                Stage2ModuleLinkupMethods[moduleType] = method;
                return method;
            }
        }

        internal static bool ShouldSkipStage2ModuleExternalLinkup(
            out Stopwatch timer)
        {
            timer = BeginDiagnosticPhase("Stage2 module external linkup body");
            if (!SkipStage2ModuleExternalLinkupForDiagnostics)
                return false;

            LogUnsafeProbeEvent(
                CurrentTrace,
                "stage2-module-external-linkup",
                "skipped Stage2 module external linkup");
            EndDiagnosticPhase("Stage2 module external linkup body", timer);
            timer = null;
            return true;
        }

        internal static string InitialiseStage2SubphaseForVerification(
            string methodName,
            string declaringTypeName) =>
            InitialiseStage2SubphaseForCall(methodName, declaringTypeName);

        private static string InitialiseStage2SubphaseForCall(MethodBase method)
        {
            if (method == null)
                return null;
            return InitialiseStage2SubphaseForCall(
                method.Name,
                method.DeclaringType?.Name);
        }

        private static string InitialiseStage2SubphaseForCall(
            string methodName,
            string declaringTypeName)
        {
            switch (methodName)
            {
                case "InitialiseBlocksInConstructable":
                    return "Stage2 InitialiseBlocksInConstructable";
                case "DestroyList":
                    return "Stage2 destroy uninitialised blocks";
                case "LinkUpExternallyAfterBlocksInitialising":
                    return "Stage2 module external linkup";
                case "HookUpPackages":
                    return "Stage2 package hookup";
                case "ApplyTextAndIdentifiers":
                    return "Stage2 text identifiers";
                case "set_InitialisationState":
                    if (declaringTypeName == nameof(AllConstruct))
                        return "Stage2 init-state commit";
                    break;
            }

            return null;
        }

        private static BlueprintFileModel LoadBlueprintFileModelFromPath(
            string path,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters,
            FastBlueprintLoadTrace trace,
            long fileBytes)
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                JsonBufferBytes,
                FileOptions.SequentialScan);
            if (trace != null)
            {
                stream = new FastBlueprintLoadProgressStream(
                    stream,
                    trace,
                    "V1 JSON read",
                    fileBytes);
            }

            using (stream)
            using (var reader = new StreamReader(
                       stream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true,
                       bufferSize: JsonBufferBytes))
            using (var json = new JsonTextReader(reader))
            {
                return CreateSerializer(referencesHandling, converters)
                    .Deserialize<BlueprintFileModel>(json);
            }
        }

        private static JsonSerializer CreateSerializer(
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                PreserveReferencesHandling = referencesHandling,
                MaxDepth = null
            };

            foreach (JsonConverter converter in converters ?? Array.Empty<JsonConverter>())
                serializer.Converters.Add(converter);
            return serializer;
        }

        private static FastBlueprintBlockDataRecord[] ScanBlockData(byte[] data)
            => ScanBlockData(data, null);

        private static FastBlueprintBlockDataRecord[] ScanBlockData(
            byte[] data,
            FastBlueprintLoadTrace trace)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<FastBlueprintBlockDataRecord>();

            var records = new List<FastBlueprintBlockDataRecord>();
            uint cursor = 0U;
            while (cursor < data.Length)
            {
                uint start = cursor;
                Require(data, cursor, BlockDataObjectIdBytes + 2U, "block-data object id and marker");
                uint objectId = ByteConversion.ConvertOut(
                    data,
                    cursor,
                    BlockDataObjectIdBytes);
                cursor += BlockDataObjectIdBytes;

                uint marker = ByteConversion.ConvertOut(data, cursor, 2);
                cursor += 2U;

                uint headerBytes;
                ulong dataBytes;
                if (marker == SuperSerialisationLayout.Sentinel)
                {
                    Require(data, cursor, 8U, "sentinel block-data lengths");
                    headerBytes = ByteConversion.ConvertOut(data, cursor, 4);
                    cursor += 4U;
                    dataBytes = ByteConversion.ConvertOut(data, cursor, 4);
                    cursor += 4U;
                }
                else
                {
                    headerBytes = marker;
                    Require(data, cursor, 2U, "legacy block-data reserved field");
                    cursor += 2U;
                    dataBytes = ReadLegacyDataLength(data, ref cursor);
                }

                if (headerBytes % 7U != 0U)
                    throw new FormatException("Block-data header byte length is not divisible by seven.");

                ulong payloadBytes = (ulong)headerBytes + dataBytes;
                ulong end = (ulong)cursor + payloadBytes;
                if (end > (ulong)data.Length || end > int.MaxValue)
                    throw new FormatException("Block-data container payload extends beyond the byte array.");

                records.Add(new FastBlueprintBlockDataRecord(
                    records.Count,
                    checked((int)objectId),
                    start,
                    checked((uint)end)));
                cursor = checked((uint)end);
                trace?.Heartbeat(
                    "V2 block-data scan",
                    cursor,
                    data.Length,
                    "bytes",
                    FastBlueprintLoadTrace.Pair("records", records.Count));
            }

            return records.ToArray();
        }

        private static SuperLoader[] PredecodeBlockData(
            byte[] data,
            FastBlueprintBlockDataRecord[] records) =>
            PredecodeBlockData(data, records, null);

        private static SuperLoader[] PredecodeBlockData(
            byte[] data,
            FastBlueprintBlockDataRecord[] records,
            FastBlueprintLoadTrace trace)
        {
            var loaders = new SuperLoader[records.Length];
            int completed = 0;
            Parallel.For(
                0,
                records.Length,
                index =>
                {
                    FastBlueprintBlockDataRecord record = records[index];
                    uint cursor = record.Start;
                    // Default SuperLoader instances use shared static buffers, which are unsafe for retained predecode results.
                    var loader = new SuperLoader(useStaticArrays: false);
                    ExtendedSuperLoader.Deserialise(
                        loader,
                        data,
                        ref cursor,
                        BlockDataObjectIdBytes);
                    if (cursor != record.End)
                        throw new FormatException("Decoded block-data container ended at an unexpected byte offset.");
                    loaders[index] = loader;
                    int done = Interlocked.Increment(ref completed);
                    trace?.Heartbeat(
                        "V2 block-data parallel predecode",
                        done,
                        records.Length,
                        "records");
                });
            return loaders;
        }

        private static FastBlueprintBlockApplyStats ApplyDecodedBlockData(
            AllConstruct construct,
            Version version,
            FastBlueprintBlockDataRecord[] records,
            SuperLoader[] loaders,
            FastBlueprintLoadTrace trace)
        {
            var aliveAndDead = construct.AllBasics.AliveAndDead;
            int blockCount = aliveAndDead.Count;
            int loaded = 0;
            int skippedNull = 0;
            int skippedOutOfRange = 0;
            for (int i = 0; i < records.Length; i++)
            {
                FastBlueprintBlockDataRecord record = records[i];
                if (record.BlockIndex < 0 || record.BlockIndex >= blockCount)
                {
                    skippedOutOfRange++;
                    LogError(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Cannot give extra info for block| {0} because only contains {1} blocks. Probably because some blocks were not loaded correctly (duplicate positions) so things were thrown out of order.",
                            record.BlockIndex,
                            blockCount));
                    continue;
                }

                Block block = aliveAndDead[record.BlockIndex];
                if (block == null)
                {
                    skippedNull++;
                    continue;
                }

                ((ISaveableDataOwner)block).Load(loaders[i], version, null);
                loaded++;
                trace?.Heartbeat(
                    "V2 block-data serial apply",
                    i + 1,
                    records.Length,
                    "records",
                    FastBlueprintLoadTrace.Pair("loaded", loaded),
                    FastBlueprintLoadTrace.Pair("skipped_null", skippedNull),
                    FastBlueprintLoadTrace.Pair("skipped_out_of_range", skippedOutOfRange));
            }

            return new FastBlueprintBlockApplyStats(
                loaded,
                skippedNull,
                skippedOutOfRange);
        }

        private static ulong ReadLegacyDataLength(byte[] data, ref uint cursor)
        {
            ulong total = 0UL;
            for (int i = 0; i < SuperSerialisationLayout.MaximumLegacyChunks; i++)
            {
                Require(data, cursor, 2U, "legacy data-length piece");
                uint piece = ByteConversion.ConvertOut(data, cursor, 2);
                cursor += 2U;
                total += piece;
                if (piece < SuperSerialisationLayout.ChunkSize)
                    return total;
            }

            if (total == SuperSerialisationLayout.MaximumLegacyDataBytes)
                return total;
            throw new FormatException("Legacy block-data length did not terminate within 100 pieces.");
        }

        private static bool ShouldRoutePath(
            FastBlueprintLoadTier minimum,
            string path)
        {
            if (!ShouldUseTier(minimum) || string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                return ShouldRoutePayload(minimum, new FileInfo(path).Length);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldRoutePayload(
            FastBlueprintLoadTier minimum,
            long byteLength)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return data != null &&
                   ShouldRouteForVerification(
                       data.FastBlueprintLoadTier,
                       minimum,
                       byteLength,
                       data.FastBlueprintLoadSmallBlueprintTesting);
        }

        private static bool ShouldProbeV3Metadata(long byteLength)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return data != null &&
                   ShouldProbeV3MetadataForVerification(
                       data.FastBlueprintLoadTier,
                       byteLength,
                       data.FastBlueprintLoadSmallBlueprintTesting,
                       data.FastBlueprintLoadBlockCountRouting);
        }

        private static bool ShouldRouteLoadedBlueprintModel(
            BlueprintFileModel model,
            long byteLength,
            bool routeByPayload,
            out string reason)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            if (data == null)
            {
                reason = "profile-unavailable";
                return false;
            }
            if (data.FastBlueprintLoadTier != FastBlueprintLoadTier.V3)
            {
                reason = routeByPayload
                    ? FastBlueprintLoadRouteReason()
                    : "not-v3";
                return routeByPayload;
            }

            return ShouldRouteV3ForMetadata(
                data.FastBlueprintLoadTier,
                byteLength,
                ModelSavedTotalBlockCount(model),
                ModelBlockIdsCount(model),
                data.FastBlueprintLoadSmallBlueprintTesting,
                data.FastBlueprintLoadBlockCountRouting,
                out reason);
        }

        private static bool ShouldStartV3BulkLoadForModel(
            BlueprintFileModel model,
            long byteLength,
            bool routeByPayload)
        {
            if (CurrentTier != FastBlueprintLoadTier.V3)
                return false;
            return ShouldRouteLoadedBlueprintModel(
                model,
                byteLength,
                routeByPayload,
                out _);
        }

        private static bool ShouldRouteV3ForMetadata(
            FastBlueprintLoadTier selected,
            long byteLength,
            int savedTotalBlocks,
            int blockIdsCount,
            bool smallBlueprintTesting,
            bool blockCountRouting,
            out string reason)
        {
            if (selected != FastBlueprintLoadTier.V3)
            {
                reason = selected == FastBlueprintLoadTier.Off
                    ? "tier-off"
                    : "tier-not-v3";
                return false;
            }
            if (smallBlueprintTesting)
            {
                reason = "small-blueprint-testing";
                return true;
            }
            if (byteLength >= LargeBlueprintLoadThresholdBytes)
            {
                reason = "large-blueprint-file";
                return true;
            }
            if (!blockCountRouting)
            {
                reason = "block-count-routing-disabled";
                return false;
            }
            int blockCount = Math.Max(savedTotalBlocks, blockIdsCount);
            if (blockCount >= V3BlockCountThreshold)
            {
                reason = "large-block-count";
                return true;
            }

            reason = "below-v3-block-count-threshold";
            return false;
        }

        private static int ModelSavedTotalBlockCount(BlueprintFileModel model)
        {
            try { return model?.SavedTotalBlockCount ?? 0; }
            catch { return 0; }
        }

        private static int ModelBlockIdsCount(BlueprintFileModel model)
        {
            try { return model?.Blueprint?.BlockIds?.Length ?? 0; }
            catch { return 0; }
        }

        private static bool ShouldRouteV2BlockData(
            long blockDataBytes,
            int recordCount,
            out string reason)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return ShouldRouteV2BlockData(
                blockDataBytes,
                recordCount,
                data?.FastBlueprintLoadForceV2BlockData == true,
                out reason);
        }

        private static bool ShouldRouteV2BlockData(
            long blockDataBytes,
            int recordCount,
            bool forceV2,
            out string reason)
        {
            if (forceV2)
            {
                reason = "forced-v2-block-data";
                return true;
            }

            if (blockDataBytes >= V2BlockDataPayloadThresholdBytes)
            {
                reason = "large-block-data-payload";
                return true;
            }

            if (recordCount >= V2BlockDataRecordThreshold)
            {
                reason = "many-block-data-records";
                return true;
            }

            reason = "tiny-block-data";
            return false;
        }

        private static bool ShouldUseTier(FastBlueprintLoadTier minimum)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return data != null &&
                   data.FastBlueprintLoadTier >= minimum &&
                   data.FastBlueprintLoadTier != FastBlueprintLoadTier.Off;
        }

        private static FastBlueprintLoadTier CurrentTier
        {
            get
            {
                try { return SerializationHudProfile.Data.FastBlueprintLoadTier; }
                catch { return FastBlueprintLoadTier.Off; }
            }
        }

        private static bool DiagnosticsEnabled
        {
            get
            {
                try { return SerializationHudProfile.Data.FastBlueprintLoadDiagnostics; }
                catch { return false; }
            }
        }

        internal static bool SkipV3SyncRegistrationForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration);
            }
        }

        internal static bool SkipV3StatusRegistrationForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipV3StatusRegistration);
            }
        }

        internal static bool SkipStage2ModuleExternalLinkupForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipStage2ModuleExternalLinkup);
            }
        }

        internal static bool SkipV3ColliderLinkupForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipV3ColliderLinkup);
            }
        }

        internal static bool SkipV3ShellLinkupForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipV3ShellLinkup);
            }
        }

        internal static bool SkipV3SkinCalcForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return UnsafeProbeActive(
                    data,
                    FastBlueprintLoadUnsafeProbeMode.SkipV3SkinCalc);
            }
        }

        internal static bool AnyUnsafeProbeActiveForDiagnostics
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return data != null &&
                       data.FastBlueprintLoadUnsafeProbeMode != FastBlueprintLoadUnsafeProbeMode.Off &&
                       UnsafeProbeActive(
                           data.FastBlueprintLoadUnsafeProbeMode,
                           data.FastBlueprintLoadDiagnostics,
                           data.FastBlueprintLoadTier);
            }
        }

        internal static string ActiveUnsafeProbeName
        {
            get
            {
                SerializationHudProfile.ProfileData data = ProfileData;
                return data == null
                    ? FastBlueprintLoadUnsafeProbeMode.Off.ToString()
                    : data.FastBlueprintLoadUnsafeProbeMode.ToString();
            }
        }

        private static bool UnsafeProbeActive(
            SerializationHudProfile.ProfileData data,
            FastBlueprintLoadUnsafeProbeMode mode) =>
            data != null &&
            data.FastBlueprintLoadUnsafeProbeMode == mode &&
            UnsafeProbeActive(
                data.FastBlueprintLoadUnsafeProbeMode,
                data.FastBlueprintLoadDiagnostics,
                data.FastBlueprintLoadTier);

        private static bool UnsafeProbeActive(
            FastBlueprintLoadUnsafeProbeMode mode,
            bool diagnostics,
            FastBlueprintLoadTier tier) =>
            diagnostics &&
            tier == FastBlueprintLoadTier.V3 &&
            mode != FastBlueprintLoadUnsafeProbeMode.Off;

        internal static void LogUnsafeProbeEvent(
            FastBlueprintLoadTrace trace,
            string phase,
            string detail,
            params KeyValuePair<string, object>[] fields)
        {
            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>())
            {
                FastBlueprintLoadTrace.Pair("unsafe_probe", ActiveUnsafeProbeName),
                FastBlueprintLoadTrace.Pair("unsafe_probe_active", true),
                FastBlueprintLoadTrace.Pair("correctness_valid", false),
                FastBlueprintLoadTrace.Pair("correctness_invalid", true),
                FastBlueprintLoadTrace.Pair("do_not_save", true),
                FastBlueprintLoadTrace.Pair("detail", detail ?? string.Empty)
            };
            trace?.Event(
                "unsafe-probe",
                phase ?? "unsafe-probe",
                advLogger: true,
                merged.ToArray());
        }

        private static SerializationHudProfile.ProfileData ProfileData
        {
            get
            {
                try { return SerializationHudProfile.Data; }
                catch { return null; }
            }
        }

        private static FastBlueprintLoadTrace CurrentTrace
        {
            get
            {
                Stack<FastBlueprintLoadTrace> stack = _activeTraces;
                return stack != null && stack.Count > 0
                    ? stack.Peek()
                    : null;
            }
        }

        private static void PushTrace(FastBlueprintLoadTrace trace)
        {
            if (trace == null)
                return;
            if (_activeTraces == null)
                _activeTraces = new Stack<FastBlueprintLoadTrace>();
            _activeTraces.Push(trace);
        }

        private static void PopTrace()
        {
            Stack<FastBlueprintLoadTrace> stack = _activeTraces;
            if (stack == null || stack.Count == 0)
                return;
            stack.Pop();
        }

        private static void RegisterTrace(
            Blueprint blueprint,
            FastBlueprintLoadTrace trace)
        {
            if (blueprint == null || trace == null)
                return;
            try { BlueprintTraces.Remove(blueprint); }
            catch { }
            try { BlueprintTraces.Add(blueprint, trace); }
            catch { }
        }

        private static void RegisterV3RoutedBlueprint(Blueprint blueprint)
        {
            if (blueprint == null)
                return;
            try { V3RoutedBlueprints.Remove(blueprint); }
            catch { }
            try { V3RoutedBlueprints.Add(blueprint, new object()); }
            catch { }
        }

        private static bool ShouldStartV3BulkLoad(Blueprint blueprint)
        {
            if (blueprint == null || CurrentTier != FastBlueprintLoadTier.V3)
                return false;
            try { return V3RoutedBlueprints.TryGetValue(blueprint, out _); }
            catch { return false; }
        }

        private static FastBlueprintV3BulkLoadContext BeginV3BulkLoadContext(
            FastBlueprintLoadTrace trace)
        {
            string missingTargets = V3PreflightMissingTargets();
            if (!string.IsNullOrEmpty(missingTargets))
            {
                trace?.Event(
                    "v3-unsupported",
                    "v3",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("supported", false),
                    FastBlueprintLoadTrace.Pair("reason", "required patch targets unavailable"),
                    FastBlueprintLoadTrace.Pair("missing_targets", missingTargets));
                trace?.Event(
                    "v3-fallback",
                    "v3",
                    advLogger: true,
                    FastBlueprintLoadTrace.Pair("reason", "V3 unsupported; continuing with V2 block-data path"));
                return null;
            }

            var context = new FastBlueprintV3BulkLoadContext(trace, _activeV3BulkContext);
            _activeV3BulkContext = context;
            return context;
        }

        private static bool V3PreflightTargetsAvailable()
            => string.IsNullOrEmpty(V3PreflightMissingTargets());

        private static string V3PreflightMissingTargets()
        {
            var missing = new List<string>();
            if (!_v3BlockStatePatchInstalled)
                missing.Add("Block.BlockStateChanged patch");
            if (_v3BlockStateChangedTarget == null)
                missing.Add("Block.BlockStateChanged target");
            if (ResolveConstructExtraInfoProvideInfoToBlocksTarget() == null)
                missing.Add("ConstructExtraInfo.ProvideInfoToBlocks");
            if (ResolveAllConstructInitialiseStage2Target() == null)
                missing.Add("AllConstruct.InitialiseStage2");
            if (SkipStage2ModuleExternalLinkupForDiagnostics &&
                !_stage2ModuleExternalLinkupPatchInstalled)
            {
                missing.Add("Stage2 module external-linkup diagnostics patch");
            }
            if (ResolvePartStatusRegisterCheckableBlockTarget() == null)
                missing.Add("IStatusHandler.RegisterCheckableBlock(ICheckState)");
            if (ResolvePartStatusUnregisterCheckableBlockTarget() == null)
                missing.Add("IStatusHandler.UnregisterCheckableBlock(ICheckState)");
            return string.Join(", ", missing.ToArray());
        }

        private static MethodInfo TryResolveBlockBlockStateChangedTarget()
        {
            try
            {
                MethodInfo method =
                    typeof(Block).Module.ResolveMethod(BlockBlockStateChangedMetadataToken) as MethodInfo;
                if (method == null || method.Name != "BlockStateChanged")
                    return null;
                return method;
            }
            catch
            {
                return null;
            }
        }

        internal static void InvokeVanillaBlockStateChanged(
            Block block,
            IBlockStateChange change)
        {
            if (block == null || change == null)
                return;
            MethodInfo method = _v3BlockStateChangedTarget ?? TryResolveBlockBlockStateChangedTarget();
            if (method == null)
                throw new MissingMethodException(typeof(Block).FullName, "BlockStateChanged");
            method.Invoke(block, new object[] { change });
        }

        private static FastBlueprintLoadTrace TraceForBlueprint(Blueprint blueprint)
        {
            if (blueprint == null)
                return null;
            try
            {
                return BlueprintTraces.TryGetValue(blueprint, out FastBlueprintLoadTrace trace)
                    ? trace
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string BlueprintTraceName(Blueprint blueprint)
        {
            if (!string.IsNullOrWhiteSpace(blueprint?.blueprintName))
                return blueprint.blueprintName;
            return "blueprint-conversion";
        }

        private static long BlueprintPayloadBytes(Blueprint blueprint)
        {
            if (blueprint == null)
                return 0L;
            long blockData = blueprint.BlockData?.LongLength ?? 0L;
            long vehicleData = blueprint.VehicleData?.LongLength ?? 0L;
            return blockData + vehicleData;
        }

        private static KeyValuePair<string, object>[] BlueprintConversionFields(
            Blueprint blueprint,
            bool standaloneTrace) =>
            new[]
            {
                FastBlueprintLoadTrace.Pair("standalone_trace", standaloneTrace),
                FastBlueprintLoadTrace.Pair("blueprint_name", blueprint?.blueprintName),
                FastBlueprintLoadTrace.Pair("block_ids_count", blueprint?.BlockIds?.Length ?? 0),
                FastBlueprintLoadTrace.Pair("block_data_bytes", blueprint?.BlockData?.LongLength ?? 0L),
                FastBlueprintLoadTrace.Pair("vehicle_data_bytes", blueprint?.VehicleData?.LongLength ?? 0L),
                FastBlueprintLoadTrace.Pair("subconstruct_count", blueprint?.SCs?.Count ?? 0)
            };

        private static KeyValuePair<string, object>[] AppendFields(
            IEnumerable<KeyValuePair<string, object>> fields,
            params KeyValuePair<string, object>[] additions)
        {
            var merged = new List<KeyValuePair<string, object>>();
            if (fields != null)
                merged.AddRange(fields);
            if (additions != null)
                merged.AddRange(additions);
            return merged.ToArray();
        }

        private static long BlueprintFileLength(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0L;
            try { return new FileInfo(path).Length; }
            catch { return 0L; }
        }

        private static string RouteSkipReason(
            FastBlueprintLoadTier minimum,
            long byteLength)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            if (data == null)
                return "profile-unavailable";
            if (data.FastBlueprintLoadTier == FastBlueprintLoadTier.Off)
                return "tier-off";
            if (data.FastBlueprintLoadTier < minimum)
                return "tier-below-" + minimum;
            if (!data.FastBlueprintLoadSmallBlueprintTesting &&
                byteLength < LargeBlueprintLoadThresholdBytes)
            {
                return "below-large-blueprint-threshold";
            }
            return "not-routed";
        }

        private static string FastBlueprintLoadRouteReason()
        {
            switch (CurrentTier)
            {
                case FastBlueprintLoadTier.V2:
                    return "routed-v1-json-then-v2-block-data";
                case FastBlueprintLoadTier.V3:
                    return "routed-v1-json-then-v3-safe-bulk";
                default:
                    return "routed-v1-json";
            }
        }

        private static Blueprint PrepareBlueprintFromModel(
            BlueprintFile file,
            BlueprintFileModel model,
            bool setNameFromFile)
        {
            Blueprint blueprint = model?.Blueprint;
            if (blueprint == null)
                return null;

            if (Configured.i != null &&
                model.ItemDictionary != null &&
                model.ItemDictionary.Count > 0 &&
                BlueprintFileUpdateConstructMethod != null)
            {
                ModificationComponentContainerItem itemTypes =
                    Configured.i.Get<ModificationComponentContainerItem>();
                int fallback = Math.Max(
                    0,
                    itemTypes.FindTheRuntimeIdOrMinus1(
                        new Guid("9a0ae372-beb4-4009-b14e-36ed0715af73")));
                Func<int, int> translator = new Translator().GetTranslator(
                    model.ItemDictionary,
                    fallback,
                    guid => itemTypes.FindTheRuntimeIdOrMinus1(guid));
                BlueprintFileUpdateConstructMethod.Invoke(
                    file,
                    new object[] { blueprint, translator });
            }

            if (setNameFromFile)
                blueprint.blueprintName = file.Name;

            if (Configured.i != null && BlueprintFilePerformVersionUpdatesMethod != null)
            {
                BlueprintFilePerformVersionUpdatesMethod.Invoke(
                    file,
                    new object[] { blueprint });
            }

            return blueprint;
        }

        private static string BlueprintFilePath(BlueprintFile file)
        {
            var source = BaseFileSourceField?.GetValue(file) as IFileSource;
            return source?.FilePath;
        }

        private static MethodInfo ResolveConstructExtraInfoConstructGetter()
        {
            Type current = ConstructExtraInfoType;
            while (current != null)
            {
                MethodInfo method = AccessTools.Method(current, "get__construct");
                if (method != null)
                    return method;
                current = current.BaseType;
            }
            return null;
        }

        private static Type ResolveConstructExtraInfoType()
        {
            Type type = AccessTools.TypeByName("ConstructExtraInfo");
            if (type != null)
                return type;

            try { return typeof(Blueprint).Assembly.GetType("ConstructExtraInfo"); }
            catch { return null; }
        }

        private static Type ResolvePartStatusRestrictedType()
        {
            try
            {
                PropertyInfo property = AccessTools.Property(
                    typeof(MainConstruct),
                    "PartStatusRestricted");
                if (property != null)
                    return property.PropertyType;
            }
            catch
            {
            }

            return AccessTools.TypeByName("IPartStatusRestricted");
        }

        private static MethodInfo ResolvePartStatusHandlerMethod(string methodName)
        {
            Type handlerType = ResolvePartStatusHandlerType();
            if (handlerType == null)
                return null;

            Type checkStateType = ResolveCheckStateType();
            if (checkStateType != null)
            {
                try
                {
                    MethodInfo typed = AccessTools.Method(
                        handlerType,
                        methodName,
                        new[] { checkStateType });
                    if (typed != null)
                        return typed;
                }
                catch
                {
                }
            }

            try
            {
                foreach (MethodInfo method in handlerType.GetMethods())
                {
                    if (method.Name == methodName &&
                        method.GetParameters().Length == 1)
                    {
                        return method;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static Type ResolvePartStatusHandlerType()
        {
            Type explicitHandler = FindLoadedTypeByFullName(
                "BrilliantSkies.Common.StatusChecking.IStatusHandler");
            if (explicitHandler != null)
                return explicitHandler;

            Type statusType = ResolvePartStatusRestrictedType();
            return FindStatusHandlerInterface(statusType);
        }

        private static Type ResolveCheckStateType()
        {
            Type checkState = FindLoadedTypeByFullName(
                "BrilliantSkies.Common.StatusChecking.ICheckState");
            if (checkState != null)
                return checkState;

            try
            {
                return AccessTools.TypeByName("ICheckState");
            }
            catch
            {
                return null;
            }
        }

        private static Type FindStatusHandlerInterface(Type type)
        {
            if (type == null)
                return null;

            if (HasStatusHandlerMethods(type))
                return type;

            try
            {
                foreach (Type iface in type.GetInterfaces())
                {
                    if (HasStatusHandlerMethods(iface))
                        return iface;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool HasStatusHandlerMethods(Type type)
        {
            if (type == null)
                return false;

            try
            {
                return type.GetMethods().Any(method =>
                           method.Name == "RegisterCheckableBlock" &&
                           method.GetParameters().Length == 1) &&
                       type.GetMethods().Any(method =>
                           method.Name == "UnregisterCheckableBlock" &&
                           method.GetParameters().Length == 1);
            }
            catch
            {
                return false;
            }
        }

        private static void Require(byte[] data, uint cursor, ulong count, string section)
        {
            ulong end = (ulong)cursor + count;
            if (end > (ulong)(data?.Length ?? 0))
                throw new FormatException("Truncated " + section + ".");
        }

        private static void LogDiagnostic(string message)
        {
            if (!DiagnosticsEnabled)
                return;
            LogInfo("[fast blueprint load] " + message);
        }

        private static void LogInfo(string message)
        {
            try { AdvLogger.LogInfo("[EndlessShapes Unlimited] " + message); }
            catch { }
        }

        private static void LogError(string message)
        {
            try { AdvLogger.LogError("[EndlessShapes Unlimited] " + message, LogOptions._AlertDevInGame); }
            catch { }
        }

        internal static void LogException(string action, Exception exception)
        {
            try
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not " + action,
                    exception,
                    LogOptions._AlertDevInGame);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch]
    internal static class BlueprintFile_Load_FastLoad_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveBlueprintFileModelLoadDataTarget();

        private static bool Prefix(
            BlueprintFile __instance,
            [HarmonyArgument(0)] bool setNameFromFile,
            ref Blueprint __result)
        {
            if (!FastBlueprintLoadRouter.TryLoadBlueprint(
                    __instance,
                    setNameFromFile,
                    out Blueprint blueprint))
            {
                return true;
            }

            __result = blueprint;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ConstructExtraInfo_DataArray_FastLoad_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveConstructExtraInfoDataArrayTarget();

        private static bool Prefix(
            object __instance,
            out FastBlueprintDiagnosticPhaseState __state)
        {
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhaseState(
                "ConstructExtraInfo.DataArray");
            if (!FastBlueprintLoadRouter.TryHandleConstructExtraInfoDataArray(__instance))
                return true;

            __state?.Complete();
            __state = null;
            return false;
        }

        private static void Postfix(FastBlueprintDiagnosticPhaseState __state) =>
            __state?.Complete();

        private static Exception Finalizer(
            Exception __exception,
            FastBlueprintDiagnosticPhaseState __state)
        {
            __state?.Complete();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ConstructExtraInfo_ProvideInfoToBlocks_V3BulkLoad_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveConstructExtraInfoProvideInfoToBlocksTarget();

        private static void Prefix(out FastBlueprintDiagnosticPhaseState __state)
        {
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhaseState(
                "ConstructExtraInfo.ProvideInfoToBlocks total");
            FastBlueprintLoadRouter.FlushV3BeforeBlockData();
        }

        private static void Postfix(FastBlueprintDiagnosticPhaseState __state) =>
            __state?.Complete();

        private static Exception Finalizer(
            Exception __exception,
            FastBlueprintDiagnosticPhaseState __state)
        {
            __state?.Complete();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ConstructExtraInfo_DoubleArray_FastLoadDiagnostics_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveConstructExtraInfoDoubleArrayTarget();

        private static void Prefix(out FastBlueprintDiagnosticPhaseState __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhaseState(
                "ConstructExtraInfo.DoubleArray");

        private static void Postfix(FastBlueprintDiagnosticPhaseState __state) =>
            __state?.Complete();

        private static Exception Finalizer(
            Exception __exception,
            FastBlueprintDiagnosticPhaseState __state)
        {
            __state?.Complete();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ConstructExtraInfo_UpgradeConstruct_FastLoadDiagnostics_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveConstructExtraInfoUpgradeConstructTarget();

        private static void Prefix(out FastBlueprintDiagnosticPhaseState __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhaseState(
                "ConstructExtraInfo.UpgradeConstruct");

        private static void Postfix(FastBlueprintDiagnosticPhaseState __state) =>
            __state?.Complete();

        private static Exception Finalizer(
            Exception __exception,
            FastBlueprintDiagnosticPhaseState __state)
        {
            __state?.Complete();
            return __exception;
        }
    }

    internal static class Block_BlockStateChanged_V3BulkLoad_Patch
    {
        internal static bool Prefix(
            Block __instance,
            [HarmonyArgument(0)] IBlockStateChange change) =>
            !FastBlueprintLoadRouter.TryCaptureV3BlockStateChange(__instance, change);
    }

    internal static class AllConstruct_Stage2ModuleExternalLinkup_FastLoadDiagnostics_Patch
    {
        private static bool Prefix(out Stopwatch __state)
        {
            if (FastBlueprintLoadRouter.ShouldSkipStage2ModuleExternalLinkup(out __state))
                return false;
            return true;
        }

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase(
                "Stage2 module external linkup body",
                __state);

        private static Exception Finalizer(
            Exception __exception,
            Stopwatch __state)
        {
            if (__exception != null)
            {
                FastBlueprintLoadRouter.EndDiagnosticPhase(
                    "Stage2 module external linkup body",
                    __state);
            }
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator) =>
            FastBlueprintLoadRouter.AddStage2ModuleExternalLinkupCallsiteTimers(
                instructions,
                generator);
    }

    internal static class AllConstruct_Stage2ConcreteModuleExternalLinkup_FastLoadDiagnostics_Patch
    {
        private static bool Prefix(
            object __instance,
            MethodBase __originalMethod,
            out Stopwatch __state) =>
            FastBlueprintLoadRouter.BeginStage2ConcreteModuleExternalLinkup(
                __instance,
                __originalMethod,
                out __state);

        private static void Postfix(
            object __instance,
            MethodBase __originalMethod,
            Stopwatch __state) =>
            FastBlueprintLoadRouter.EndStage2ConcreteModuleExternalLinkup(
                __instance,
                __originalMethod,
                __state,
                threw: false);

        private static Exception Finalizer(
            Exception __exception,
            object __instance,
            MethodBase __originalMethod,
            Stopwatch __state)
        {
            if (__exception != null)
            {
                FastBlueprintLoadRouter.EndStage2ConcreteModuleExternalLinkup(
                    __instance,
                    __originalMethod,
                    __state,
                    threw: true);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AllConstruct), nameof(AllConstruct.InitialiseStage2))]
    internal static class AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch
    {
        private static void Prefix(
            AllConstruct __instance,
            out FastBlueprintInitialiseStage2State __state)
        {
            __state = new FastBlueprintInitialiseStage2State(
                FastBlueprintLoadRouter.BeginDiagnosticPhase("block initialization"),
                FastBlueprintLoadRouter.BeginV3InitialiseStage2(__instance));
        }

        private static void Postfix(FastBlueprintInitialiseStage2State __state) =>
            __state?.Complete();

        private static Exception Finalizer(
            Exception __exception,
            FastBlueprintInitialiseStage2State __state)
        {
            if (__exception != null)
                __state?.Fail(__exception);
            else
                __state?.Dispose();
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator) =>
            FastBlueprintLoadRouter.AddInitialiseStage2SubphaseTimers(
                instructions,
                generator);
    }

    [HarmonyPatch(typeof(AllConstruct), nameof(AllConstruct.Load), new[] { typeof(byte[]), typeof(Version) })]
    internal static class AllConstruct_Load_FastLoadDiagnostics_Patch
    {
        private static void Prefix(out Stopwatch __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhase("vehicle-data load");

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase("vehicle-data load", __state);
    }

    [HarmonyPatch(typeof(MainConstruct), nameof(MainConstruct.EverythingLinkedUp))]
    internal static class MainConstruct_EverythingLinkedUp_FastLoadDiagnostics_Patch
    {
        private static void Prefix(out Stopwatch __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhase("linkup");

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase("linkup", __state);
    }
}
