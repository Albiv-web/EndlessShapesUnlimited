using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BrilliantSkies.Blocks;
using BrilliantSkies.Blocks.BreadBoards;
using BrilliantSkies.Blocks.BreadBoards.GenericGetter;
using BrilliantSkies.Common.Circuits;
using BrilliantSkies.Common.Circuits.ComponentTypes;
using BrilliantSkies.Common.Circuits.ComponentTypes.Inputs;
using BrilliantSkies.Common.Circuits.Ui.UndoRedo;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;
using CircuitComponent = BrilliantSkies.Common.Circuits.Component;
using NativeAiBreadBoardBlock = BrilliantSkies.Blocks.BreadBoards.AiBreadboard;
using NativeBasicBreadBoardBlock = BrilliantSkies.Blocks.BreadBoard;
using NativeBreadBoard = BrilliantSkies.Blocks.FtdBoard;
using NativeComment = BrilliantSkies.Common.Circuits.ComponentTypes.Inputs.Comment;
using NativeSwitch = BrilliantSkies.Common.Circuits.ComponentTypes.Switch;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed partial class AutomationBuilderSession
    {
        private const uint NativeUnselectedCode = 999999u;
        private const string AutoNamePrefix = "Esu";
        private const string RetiredAutoNamePrefix = "EsuAutomation";
        private const string LegacyAutoNamePrefix = "ESU_AB_";
        private const string NativeOwnerMarkerPrefix = "ESU_AB_OWNER|";
        private const string NativeForeverMarkerPrefix = "ESU_AB_FOREVER|v1|";
        private const int AutoNameMaxLength = 30;
        private const int AutoNameTokenLength = 8;

        private static readonly Dictionary<string, Type> s_typeByNameCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> s_blockTypeByNameCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, List<NativeReadableCandidate>> s_readableCandidatesByType =
            new Dictionary<Type, List<NativeReadableCandidate>>();
        private static readonly Dictionary<string, List<NativeVariableCandidate>> s_nativeVariablesByType =
            new Dictionary<string, List<NativeVariableCandidate>>(StringComparer.Ordinal);

        private NativeBreadboardSnapshot _nativeSnapshot;
        private string _nativeAutomationCacheKey;
        private readonly List<NativeBlockNameChange> _nativeApplyNameChanges =
            new List<NativeBlockNameChange>();

        private void RefreshNativeAutomationCache(bool force = false)
        {
            string selectedKey = _selectedBreadboard?.StableKey;
            if (!force &&
                string.Equals(_nativeAutomationCacheKey, selectedKey, StringComparison.Ordinal) &&
                !_nativeAutomationCacheDirty &&
                Time.unscaledTime < _nextNativeAutomationRefreshTime)
            {
                _nativeRefreshSkippedCount++;
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            List<AutomationLink> previousLinks = _links.ToList();
            AutomationLink previousSelectedLink = _selectedLink;
            AutomationBlockRef previousSelectedBlock = _selectedBlock;
            NativeBreadboardSnapshot previousSnapshot = _nativeSnapshot;
            string previousCacheKey = _nativeAutomationCacheKey;
            int previousCacheVersion = _nativeAutomationCacheVersion;

            try
            {
                RefreshNativeAutomationCacheCore(force, timer);
            }
            catch (Exception exception)
            {
                _links.Clear();
                _links.AddRange(previousLinks);
                _selectedLink = previousSelectedLink;
                _selectedBlock = previousSelectedBlock;
                _nativeSnapshot = previousSnapshot;
                _nativeAutomationCacheKey = previousCacheKey;
                _nativeAutomationCacheVersion = previousCacheVersion;
                _nativeAutomationCacheDirty = false;
                _nextNativeAutomationRefreshTime = Time.unscaledTime + NativeRefreshIntervalSeconds;
                RecordNativeAutomationException("refresh", false, timer, force, exception);
            }
        }

        private void RefreshNativeAutomationCacheCore(bool force, Stopwatch timer)
        {
            string selectedKey = _selectedBreadboard?.StableKey;
            bool selectedKeyChanged = !string.Equals(_nativeAutomationCacheKey, selectedKey, StringComparison.Ordinal);
            if (selectedKeyChanged)
                force = true;

            float now = Time.unscaledTime;
            if (!force &&
                !_nativeAutomationCacheDirty &&
                now < _nextNativeAutomationRefreshTime)
            {
                _nativeRefreshSkippedCount++;
                return;
            }

            _nativeRefreshCount++;
            _nextNativeAutomationRefreshTime = now + NativeRefreshIntervalSeconds;
            _nativeAutomationCacheDirty = false;
            List<AutomationLink> stagedLinks = _links
                .Where(link => link?.NativeComponent == null)
                .ToList();
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                bool shouldClearNativeState = selectedKeyChanged ||
                                              _selectedBreadboard == null ||
                                              !_selectedBreadboard.IsStillValidBreadboard;
                if (shouldClearNativeState)
                {
                    bool hadNative = _nativeSnapshot != null || _links.Any(link => link?.NativeComponent != null);
                    _nativeAutomationCacheKey = selectedKey;
                    _nativeSnapshot = null;
                    _links.Clear();
                    _links.AddRange(stagedLinks);
                    _selectedLink = null;
                    if (hadNative)
                    {
                        _nativeAutomationCacheVersion++;
                        InvalidateAutomationDisplayCache();
                    }
                }
                else
                {
                    _nativeAutomationCacheDirty = true;
                }

                RecordNativeRefreshDiagnostics(force, timer, 0, 0, _links.Count);
                return;
            }

            if (selectedKeyChanged)
            {
                _nativeAutomationCacheKey = selectedKey;
                _nativeSnapshot = null;
            }

            NativeBreadboardSnapshot snapshot = NativeBreadboardSnapshot.Create(breadboard);
            bool nativeChanged = _nativeSnapshot == null ||
                                 !ReferenceEquals(_nativeSnapshot.Breadboard, breadboard) ||
                                 _nativeSnapshot.Signature != snapshot.Signature;
            _nativeSnapshot = snapshot;
            object selectedNative = _selectedLink?.NativeComponent;
            List<AutomationLink> rebuilt = BuildNativeLinks(snapshot);
            List<AutomationLink> mergedLinks = MergeNativeAndStagedLinks(rebuilt, stagedLinks);
            bool linksChanged = NativeLinksChanged(mergedLinks);
            _links.Clear();
            _links.AddRange(mergedLinks);

            _nextLinkId = Math.Max(_nextLinkId, _links.Count + 1);
            if (_selectedLink?.NativeComponent == null &&
                _selectedLink != null)
            {
                _selectedLink = _links.FirstOrDefault(link => ReferenceEquals(link, _selectedLink)) ??
                                _links.FirstOrDefault(link => LinksMatch(link, _selectedLink));
            }
            else if (selectedNative != null)
            {
                _selectedLink = _links.FirstOrDefault(link => ReferenceEquals(link.NativeComponent, selectedNative));
            }

            if (_selectedLink == null && selectedNative != null)
                _selectedBlock = _selectedBreadboard;

            if (nativeChanged || linksChanged)
            {
                _nativeAutomationCacheVersion++;
                InvalidateAutomationDisplayCache();
                SyncSelectedGraphFromNativeIfLoaded(force: true);
            }
            else
            {
                SyncSelectedGraphFromNativeIfLoaded(force: force);
            }

            PruneStaleLinkBoundGraphNodesForSelectedBreadboard();

            RecordNativeRefreshDiagnostics(
                force,
                timer,
                snapshot.Components.Count,
                SelectedGraphNodeCount(),
                _links.Count);
        }

        private bool NativeLinksChanged(IReadOnlyList<AutomationLink> nextLinks)
        {
            if (_links.Count != (nextLinks?.Count ?? 0))
                return true;

            foreach (AutomationLink link in nextLinks ?? Array.Empty<AutomationLink>())
            {
                if (!_links.Any(existing =>
                        LinksSameForRefresh(existing, link)))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<AutomationLink> MergeNativeAndStagedLinks(
            IReadOnlyList<AutomationLink> nativeLinks,
            IReadOnlyList<AutomationLink> stagedLinks)
        {
            var merged = new List<AutomationLink>();
            foreach (AutomationLink nativeLink in nativeLinks ?? Array.Empty<AutomationLink>())
            {
                if (nativeLink != null &&
                    !merged.Any(existing => LinksRepresentSameUserLink(existing, nativeLink)))
                {
                    merged.Add(nativeLink);
                }
            }

            foreach (AutomationLink stagedLink in stagedLinks ?? Array.Empty<AutomationLink>())
            {
                if (stagedLink != null &&
                    !merged.Any(existing => LinksRepresentSameUserLink(existing, stagedLink)))
                {
                    merged.Add(stagedLink);
                }
            }

            return merged;
        }

        private static bool LinksSameForRefresh(
            AutomationLink left,
            AutomationLink right)
        {
            if (left == null || right == null)
                return false;

            return ReferenceEquals(left.NativeComponent, right.NativeComponent) &&
                   LinksRepresentSameUserLink(left, right) &&
                   string.Equals(left.NativeStatus, right.NativeStatus, StringComparison.Ordinal);
        }

        private static bool LinksRepresentSameUserLink(
            AutomationLink left,
            AutomationLink right)
        {
            if (LinksMatch(left, right))
                return true;

            return left != null &&
                   right != null &&
                   left.Kind == right.Kind &&
                   LinkBlockRefsMatch(left.Source, right.Source) &&
                   LinkBlockRefsMatch(left.Target, right.Target) &&
                   string.Equals(
                       CanonicalLinkProperty(left.Property),
                       CanonicalLinkProperty(right.Property),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalLinkProperty(string property)
        {
            string text = (property ?? string.Empty).Trim();
            if (text.EndsWith("(float)", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - "(float)".Length).Trim();
            if (text.EndsWith("[number]", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - "[number]".Length).Trim();
            const string angleControlPrefix = "AngleControl:";
            if (text.StartsWith(angleControlPrefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(angleControlPrefix.Length).Trim();
            return text;
        }

        private void SyncSelectedGraphFromNativeIfLoaded(bool force)
        {
            if (_selectedBreadboard == null)
                return;

            if (!_graphs.TryGetValue(_selectedBreadboard.StableKey, out AutomationGraph graph))
                return;

            if (force || graph.NativeSyncVersion != _nativeAutomationCacheVersion)
                SyncGraphFromNativeBreadboard(_selectedBreadboard, graph);
        }

        private bool RemoveNativeLink(AutomationLink link)
        {
            if (link?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out _))
            {
                if (IsEsuOwnedNativeComponent(component))
                {
                    _pendingNativeNodeRemovals.Add(component.UniqueId);
                    return true;
                }

                NotifyNativeApply(
                    "Imported native links are read-only in Automation Builder.",
                    EsuHudNotificationKind.Warning,
                    "Remove imported native links in the native breadboard editor if needed.");
                return false;
            }

            return false;
        }

        private void SyncGraphFromNativeBreadboard(
            AutomationBlockRef breadboardRef,
            AutomationGraph graph)
        {
            if (graph == null)
                return;

            Stopwatch timer = Stopwatch.StartNew();
            List<AutomationGraphNode> previousNodes = graph.Nodes.ToList();
            List<AutomationGraphConnection> previousConnections = graph.Connections.ToList();
            List<AutomationGraphConnection> previousImportedConnections = graph.ImportedNativeConnections.ToList();
            int previousSelectedNodeId = graph.SelectedNodeId;
            int previousNativeSyncVersion = graph.NativeSyncVersion;

            try
            {
                SyncGraphFromNativeBreadboardCore(breadboardRef, graph, timer);
            }
            catch (Exception exception)
            {
                graph.Nodes.Clear();
                graph.Nodes.AddRange(previousNodes);
                graph.Connections.Clear();
                graph.Connections.AddRange(previousConnections);
                graph.ImportedNativeConnections.Clear();
                graph.ImportedNativeConnections.AddRange(previousImportedConnections);
                graph.SelectedNodeId = previousSelectedNodeId;
                graph.NativeSyncVersion = previousNativeSyncVersion;
                RecordNativeAutomationException("graph sync", true, timer, false, exception);
            }
        }

        private void SyncGraphFromNativeBreadboardCore(
            AutomationBlockRef breadboardRef,
            AutomationGraph graph,
            Stopwatch timer)
        {
            if (graph == null)
                return;

            if (!TryResolveNativeBreadboard(
                    breadboardRef,
                    out NativeBreadBoard breadboard,
                    out _))
            {
                graph.RebuildNativeNodes(Array.Empty<AutomationGraphNode>());
                if (!graph.ConnectionsInitialized)
                    graph.RebuildConnections(Array.Empty<AutomationGraphConnection>());
                graph.NativeSyncVersion = _nativeAutomationCacheVersion;
                RecordNativeGraphSyncDiagnostics(timer, 0, graph.Nodes.Count, _links.Count);
                return;
            }

            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            List<AutomationGraphNode> nodes = snapshot.Components
                .Where(component => component != null && !IsNativeOwnerMarker(component))
                .Where(component => !IsPendingNativeRemoval(component))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .Select(component => NativeComponentToNode(breadboardRef, snapshot, component))
                .ToList();
            ApplyPendingNativeNodeDrafts(nodes, breadboard);
            graph.RebuildNativeNodes(nodes);
            if (!graph.ConnectionsInitialized)
                graph.RebuildConnections(Array.Empty<AutomationGraphConnection>());
            graph.NativeSyncVersion = _nativeAutomationCacheVersion;
            RefreshNativeGraphConnections(graph);
            InvalidateAutomationDisplayCache();
            RecordNativeGraphSyncDiagnostics(timer, snapshot.Components.Count, nodes.Count, _links.Count);
        }

        private void SyncGraphFromNativeBreadboardIfNeeded(
            AutomationBlockRef breadboardRef,
            AutomationGraph graph,
            bool force)
        {
            if (graph == null)
                return;

            RefreshNativeAutomationCache(force);
            if (!force && graph.NativeSyncVersion == _nativeAutomationCacheVersion)
                return;

            SyncGraphFromNativeBreadboard(breadboardRef, graph);
        }

        private int SelectedGraphNodeCount()
        {
            return _selectedBreadboard != null &&
                   _graphs.TryGetValue(_selectedBreadboard.StableKey, out AutomationGraph graph)
                ? graph.Nodes.Count
                : 0;
        }

        private void RecordNativeRefreshDiagnostics(
            bool force,
            Stopwatch timer,
            int componentCount,
            int nodeCount,
            int linkCount)
        {
            _lastNativeRefreshElapsedMs = StopAndGetElapsedMs(timer);
            _lastNativeRefreshComponentCount = componentCount;
            _lastNativeRefreshNodeCount = nodeCount;
            _lastNativeRefreshLinkCount = linkCount;
            _lastNativeAutomationError = null;
            MaybeLogSlowNativeAutomation("native refresh", force, _lastNativeRefreshElapsedMs);
        }

        private void RecordNativeGraphSyncDiagnostics(
            Stopwatch timer,
            int componentCount,
            int nodeCount,
            int linkCount)
        {
            _nativeGraphSyncCount++;
            _lastNativeGraphSyncElapsedMs = StopAndGetElapsedMs(timer);
            _lastNativeRefreshComponentCount = componentCount;
            _lastNativeRefreshNodeCount = nodeCount;
            _lastNativeRefreshLinkCount = linkCount;
            _lastNativeAutomationError = null;
            MaybeLogSlowNativeAutomation("graph sync", false, _lastNativeGraphSyncElapsedMs);
        }

        private void RecordNativeAutomationException(
            string phase,
            bool sync,
            Stopwatch timer,
            bool force,
            Exception exception)
        {
            double elapsedMs = StopAndGetElapsedMs(timer);
            if (sync)
                _lastNativeGraphSyncElapsedMs = elapsedMs;
            else
                _lastNativeRefreshElapsedMs = elapsedMs;

            _lastNativeAutomationError = exception == null
                ? "unknown"
                : exception.GetType().Name + ": " + exception.Message;

            float now = Time.unscaledTime;
            if (now < _nextNativeDiagnosticsLogTime)
                return;

            _nextNativeDiagnosticsLogTime = now + NativeDiagnosticsLogCooldownSeconds;
            EsuRuntimeLog.Error(
                "Automation Builder",
                "Automation Builder " + phase + " failed; keeping the last cached HUD state.",
                NativeDiagnosticsDetail(phase, force) +
                "\nexception=" +
                (exception?.ToString() ?? "unknown"));
        }

        private void MaybeLogSlowNativeAutomation(
            string phase,
            bool force,
            double elapsedMs)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            if (elapsedMs < NativeDiagnosticsSlowThresholdMs)
                return;

            float now = Time.unscaledTime;
            if (now < _nextNativeDiagnosticsLogTime)
                return;

            _nextNativeDiagnosticsLogTime = now + NativeDiagnosticsLogCooldownSeconds;
            EsuRuntimeLog.Warning(
                "Automation Builder",
                "Automation Builder " + phase + " took " + elapsedMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms.",
                NativeDiagnosticsDetail(phase, force));
        }

        private string NativeDiagnosticsDetail(
            string phase,
            bool force)
        {
            return "phase=" + phase +
                   "\nforced=" + (force ? "true" : "false") +
                   "\nrefreshes=" + _nativeRefreshCount.ToString(CultureInfo.InvariantCulture) +
                   "\nskipped_refreshes=" + _nativeRefreshSkippedCount.ToString(CultureInfo.InvariantCulture) +
                   "\ngraph_syncs=" + _nativeGraphSyncCount.ToString(CultureInfo.InvariantCulture) +
                   "\nrefresh_ms=" + _lastNativeRefreshElapsedMs.ToString("0.0", CultureInfo.InvariantCulture) +
                   "\nsync_ms=" + _lastNativeGraphSyncElapsedMs.ToString("0.0", CultureInfo.InvariantCulture) +
                   "\ncomponents=" + _lastNativeRefreshComponentCount.ToString(CultureInfo.InvariantCulture) +
                   "\nnodes=" + _lastNativeRefreshNodeCount.ToString(CultureInfo.InvariantCulture) +
                   "\nlinks=" + _lastNativeRefreshLinkCount.ToString(CultureInfo.InvariantCulture) +
                   (string.IsNullOrWhiteSpace(_lastNativeAutomationError)
                       ? string.Empty
                       : "\nlast_error=" + _lastNativeAutomationError);
        }

        private static double StopAndGetElapsedMs(Stopwatch timer)
        {
            if (timer == null)
                return 0d;

            timer.Stop();
            return timer.Elapsed.TotalMilliseconds;
        }

        private void SyncNativeNodeRect(AutomationGraphNode node)
        {
            if (!(node?.NativeComponent is CircuitComponent component))
                return;

            if (IsEsuOwnedNativeComponent(component))
            {
                Rect nativeRect = NativeComponentRect(component, node.Kind);
                if (GraphRectsEquivalent(node.Rect, nativeRect))
                    _pendingNativeNodeRects.Remove(component.UniqueId);
                else
                    _pendingNativeNodeRects[component.UniqueId] = node.Rect;
                return;
            }

            // Imported vanilla components are opaque/read-only in the Scratch
            // editor. Never write expanded visual card bounds back to native data.
        }

        private void ApplyPendingNativeNodeDrafts(
            IEnumerable<AutomationGraphNode> nodes,
            NativeBreadBoard breadboard)
        {
            if (nodes == null ||
                _pendingNativeNodeDrafts.Count == 0 &&
                _pendingNativeNodeRects.Count == 0)
                return;

            var ownedIds = new HashSet<uint>(
                OwnerRecordsFor(breadboard)
                .Where(record => record?.Target != null)
                .Select(record => record.ComponentId));
            foreach (AutomationGraphNode node in nodes)
            {
                if (!TryGetNativeComponentId(node, out uint componentId))
                    continue;

                if (!ownedIds.Contains(componentId))
                {
                    _pendingNativeNodeDrafts.Remove(componentId);
                    _pendingNativeNodeRects.Remove(componentId);
                    continue;
                }

                if (_pendingNativeNodeDrafts.TryGetValue(componentId, out AutomationGraphNodeDraft draft))
                    draft.ApplyTo(node);
                if (_pendingNativeNodeRects.TryGetValue(componentId, out Rect rect))
                    node.Rect = rect;
            }
        }

        private static bool GraphRectsEquivalent(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) <= 0.001f &&
                   Mathf.Abs(left.y - right.y) <= 0.001f &&
                   Mathf.Abs(left.width - right.width) <= 0.001f &&
                   Mathf.Abs(left.height - right.height) <= 0.001f;
        }

        private bool IsEsuOwnedNativeComponent(CircuitComponent component)
        {
            return component != null &&
                   TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard) &&
                   OwnerRecordsFor(breadboard).Any(record =>
                       record?.Target != null &&
                       record.ComponentId == component.UniqueId);
        }

        private bool IsEsuOwnedNativeNode(AutomationGraphNode node)
        {
            return node?.NativeComponent is CircuitComponent component &&
                   IsEsuOwnedNativeComponent(component);
        }

        private static int NativeGraphNodeId(CircuitComponent component)
        {
            if (component == null)
                return 0;

            uint shifted = component.UniqueId + 1u;
            if (shifted == 0u)
                return int.MaxValue;

            int graphId = unchecked((int)shifted);
            return graphId == 0 ? int.MaxValue : graphId;
        }

        private static bool TryGetNativeComponentId(
            AutomationGraphNode node,
            out uint componentId)
        {
            componentId = 0u;
            if (node?.NativeComponent is CircuitComponent component)
            {
                componentId = component.UniqueId;
                return true;
            }

            return false;
        }

        private bool HasPendingNativeNodeDraft(AutomationGraphNode node)
        {
            return TryGetNativeComponentId(node, out uint componentId) &&
                   (_pendingNativeNodeDrafts.ContainsKey(componentId) ||
                    _pendingNativeNodeRects.ContainsKey(componentId));
        }

        private bool IsPendingNativeRemoval(CircuitComponent component)
        {
            return component != null &&
                   _pendingNativeNodeRemovals.Contains(component.UniqueId);
        }

        private bool IsPendingNativeRemoval(AutomationGraphNode node)
        {
            return TryGetNativeComponentId(node, out uint componentId) &&
                   _pendingNativeNodeRemovals.Contains(componentId);
        }

        private void StorePendingNativeNodeDraft(AutomationGraphNode node)
        {
            if (!TryGetNativeComponentId(node, out uint componentId))
                return;

            if (_pendingNativeNodeRemovals.Contains(componentId))
                return;

            _pendingNativeNodeDrafts[componentId] = new AutomationGraphNodeDraft(node);
        }

        private void ClearPendingNativeNodeDraft(AutomationGraphNode node)
        {
            if (TryGetNativeComponentId(node, out uint componentId))
            {
                _pendingNativeNodeDrafts.Remove(componentId);
                _pendingNativeNodeRects.Remove(componentId);
            }
        }

        private int ApplyPendingNativeNodeDraftsToNative(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            if (graph == null ||
                breadboard == null ||
                _pendingNativeNodeDrafts.Count == 0 &&
                _pendingNativeNodeRects.Count == 0)
                return 0;

            int applied = 0;
            HashSet<CircuitComponent> ownedComponents = EsuOwnedNativeComponentsFor(breadboard);
            foreach (AutomationGraphNode node in graph.Nodes
                         .Where(node => node?.NativeComponent is CircuitComponent component &&
                                        ownedComponents.Contains(component))
                         .ToList())
            {
                if (!TryGetNativeComponentId(node, out uint componentId))
                    continue;

                bool changed = false;
                if (_pendingNativeNodeDrafts.TryGetValue(componentId, out AutomationGraphNodeDraft draft))
                {
                    draft.ApplyTo(node);
                    ApplyNativeNodeToNativeComponent(node);
                    changed = true;
                }

                if (_pendingNativeNodeRects.TryGetValue(componentId, out Rect rect))
                {
                    node.Rect = rect;
                    ApplyNativeNodeRect((CircuitComponent)node.NativeComponent, rect);
                    changed = true;
                }

                if (!changed)
                    continue;

                _pendingNativeNodeDrafts.Remove(componentId);
                _pendingNativeNodeRects.Remove(componentId);
                applied++;
            }

            return applied;
        }

        private void ApplyNativeNodeToNativeComponent(AutomationGraphNode node)
        {
            if (!(node?.NativeComponent is CircuitComponent component))
                return;

            if (component is GenericBlockGetter getter)
            {
                if (TryGetBoundTargetBlock(node, out Block target))
                {
                    var context = new NativeBuildContext(_selectedBreadboard, _links);
                    string filter = TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
                        ? EnsureExactBlockFilterName(breadboard, target, context)
                        : CurrentCustomName(target);
                    ConfigureGetterTarget(getter, target, filter);
                    TryConfigureGetterProperty(getter, target.GetType(), node.Property);
                    return;
                }

                Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
                if (blockType != null)
                    TryConfigureGetterProperty(getter, blockType, node.Property);
            }
            else if (component is GenericBlockSetter setter)
            {
                if (TryGetBoundTargetBlock(node, out Block target))
                {
                    var context = new NativeBuildContext(_selectedBreadboard, _links);
                    string filter = TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
                        ? EnsureExactBlockFilterName(breadboard, target, context)
                        : CurrentCustomName(target);
                    ConfigureSetterTarget(setter, target, filter);
                    TryConfigureSetterProperty(setter, target.GetType(), node.Property);
                    return;
                }

                Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
                if (blockType != null)
                    TryConfigureSetterProperty(setter, blockType, node.Property);
            }
            else if (component is Evaluator evaluator)
            {
                evaluator.Expression.Us = string.IsNullOrWhiteSpace(node.ValueText)
                    ? node.Label
                    : node.ValueText;
            }
            else if (component is NativeSwitch switchComponent)
            {
                if (node.Kind == AutomationNodeKind.IfCondition)
                {
                    TryParseLabeledAutomationFloat(
                        node.ValueText,
                        "else",
                        out float failValue);
                    switchComponent.Threshold.Us = 0.5f;
                    switchComponent.FailValue.Us = failValue;
                }
                else
                {
                    TryParseLabeledAutomationFloat(
                        node.ValueText,
                        "threshold",
                        out float threshold);
                    TryParseLabeledAutomationFloat(
                        node.ValueText,
                        "else",
                        out float failValue);
                    switchComponent.Threshold.Us = threshold;
                    switchComponent.FailValue.Us = failValue;
                }
            }
            else if (component is LogicGate logicGate &&
                     IsLogicGateKind(node.Kind))
            {
                logicGate.SelectedGate.Us = LogicGateType(node.Kind);
                logicGate.TrueLogic.Us = TrueType.GreaterZero;
            }
            else if (component is FuzzyThreshold fuzzyThreshold &&
                     IsFuzzyThresholdKind(node.Kind))
            {
                ApplyFuzzyThresholdEdits(fuzzyThreshold, node.Kind, node.ValueText);
            }
            else if (component is MaxMin maxMin &&
                     IsMaxMinKind(node.Kind))
            {
                maxMin.SelectedOp.Us = MaxMinOpType(node.Kind);
            }
            else if (component is ConstantInput constant)
            {
                constant.Type.Us = ConstantInput.ConstantType.ItsAFloat;
                constant.InputValue.Us = ParseFloat(node.ValueText, constant.InputValue.Us);
            }
            else if (component is RandomInput random)
            {
                ParseRange(node.ValueText, random.RandomLimits.Lower, random.RandomLimits.Upper, out float lower, out float upper);
                random.RandomLimits.Lower = lower;
                random.RandomLimits.Upper = upper;
            }
            else if (component is Clamp clamp)
            {
                ParseRange(node.ValueText, clamp.MinMax.Lower, clamp.MinMax.Upper, out float lower, out float upper);
                clamp.MinMax.Lower = lower;
                clamp.MinMax.Upper = upper;
            }
            else if (component is Delay delay)
            {
                delay.DelayTime.Us = ParseSeconds(node.ValueText, delay.DelayTime.Us);
            }
            else if (component is NativeComment comment)
            {
                string commentText = string.IsNullOrWhiteSpace(node.ValueText)
                    ? node.Label
                    : node.ValueText;
                comment.InputValue.Us = node.Kind == AutomationNodeKind.Forever
                    ? NativeForeverMarkerText(commentText)
                    : commentText;
            }
        }

        private bool RemoveNativeGraphNode(AutomationGraphNode node)
        {
            ClearPendingNativeNodeDraft(node);
            if (node?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out _))
            {
                if (IsEsuOwnedNativeComponent(component))
                {
                    _pendingNativeNodeRemovals.Add(component.UniqueId);
                    return true;
                }

                NotifyNativeApply(
                    "Imported native graph blocks are read-only in Automation Builder.",
                    EsuHudNotificationKind.Warning,
                    "Remove imported native blocks in the native breadboard editor if needed.");
                return false;
            }

            return false;
        }

        private static bool RemoveNativeComponentPackage(
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            if (breadboard == null || component == null)
                return false;

            bool present = breadboard.Packages.Contains(component) ||
                           breadboard.Components.Contains(component);
            if (!present)
                return false;

            try
            {
                new DeleteComponentCommand(breadboard, component).Execute();
            }
            catch
            {
                try
                {
                    component.Delete();
                    breadboard.RemovePackage(component);
                }
                catch
                {
                    return false;
                }
            }

            return !breadboard.Packages.Contains(component) &&
                   !breadboard.Components.Contains(component);
        }

        private void ApplyNativeNodeEdits(
            AutomationGraphNode node,
            string label,
            string property,
            string value)
        {
            if (node == null)
                return;

            if (node.NativeComponent is CircuitComponent &&
                !IsEsuOwnedNativeNode(node))
            {
                NotifyNativeApply(
                    "Imported native graph blocks are read-only in Automation Builder.",
                    EsuHudNotificationKind.Warning,
                    "Use the vanilla breadboard editor for that component. ESU preserves it and its existing wires.");
                return;
            }

            node.Label = label ?? string.Empty;
            node.Property = ResolveNodePropertyForEdit(node, property);
            node.SetBindingProperty(node.Property);
            SyncStagedLinkPropertyForNode(node);
            node.ValueText = value ?? string.Empty;
            InvalidateAutomationDisplayCache();
            if (!(node.NativeComponent is CircuitComponent))
                return;

            if (IsEsuOwnedNativeNode(node))
            {
                StorePendingNativeNodeDraft(node);
                return;
            }

            ApplyNativeNodeToNativeComponent(node);
            InvalidateNativeAutomationCache();
        }

        private bool TryApplyNativeLinkTarget(
            AutomationGraphNode node,
            AutomationLink link,
            out string message)
        {
            message = null;
            if (node == null || link == null)
            {
                message = "Select a native graph block and linked target first.";
                return false;
            }

            if (node.IsStaged)
                return TryBindStagedLinkTarget(node, link, out message);

            if (IsEsuOwnedNativeNode(node))
            {
                bool bound = TryBindStagedLinkTarget(node, link, out message);
                if (bound)
                    StorePendingNativeNodeDraft(node);
                return bound;
            }

            message = "Imported native graph blocks are read-only in Automation Builder. Use the vanilla breadboard editor to retarget them.";
            return false;
        }

        private void ClearStagedLinkTargetBinding(AutomationGraphNode node)
        {
            if (node == null || !IsEsuOwnedNativeNode(node))
                return;

            node.BindTarget((AutomationGraphTargetBinding)null);
            node.Label = DefaultNodeLabel(node.Kind);
            node.Property = NormalizeNodeProperty(node.Kind, string.Empty);
            node.ValueText = DefaultValue(node.Kind);
            StorePendingNativeNodeDraft(node);
        }

        private bool TryBindStagedLinkTarget(
            AutomationGraphNode node,
            AutomationLink link,
            out string message)
        {
            message = null;
            AutomationBlockRef targetRef;
            if (node.Kind == AutomationNodeKind.InputGetter &&
                link.Kind == AutomationLinkKind.InputToBreadboard)
            {
                targetRef = link.Source;
            }
            else if (node.Kind == AutomationNodeKind.OutputSetter &&
                     link.Kind == AutomationLinkKind.BreadboardToOutput)
            {
                targetRef = link.Target;
            }
            else
            {
                message = "That linked target does not match the selected block direction.";
                return false;
            }

            if (targetRef == null ||
                !targetRef.TryGetBlock(out Block target))
            {
                message = "The selected linked target is no longer available.";
                return false;
            }

            string property = TryResolveNativePropertyLabel(
                node.Kind,
                target.GetType(),
                link.Property,
                out string resolvedProperty)
                ? resolvedProperty
                : string.Empty;
            node.BindTarget(link, property);
            node.Label = targetRef.Name;
            node.Property = property;
            node.ValueText = node.Kind == AutomationNodeKind.InputGetter
                ? "staged signal"
                : "incoming signal";
            if (link.IsStaged && !string.IsNullOrWhiteSpace(property))
                link.SetProperty(property);
            message = node.Kind == AutomationNodeKind.InputGetter
                ? "Automation staged input block now reads " + targetRef.Name + "."
                : "Automation staged output block now sets " + targetRef.Name + ".";
            return true;
        }

        private void SyncStagedLinkPropertyForNode(AutomationGraphNode node)
        {
            if (node?.TargetBinding == null ||
                string.IsNullOrWhiteSpace(node.Property))
            {
                return;
            }

            foreach (AutomationLink link in _links.Where(link =>
                         link?.IsStaged == true &&
                         NodeBindingTargetsMatchLink(node, link)))
            {
                link.SetProperty(node.Property);
            }
        }

        private string ResolveNodePropertyForEdit(
            AutomationGraphNode node,
            string property)
        {
            if (node == null)
                return NormalizeNodeProperty(AutomationNodeKind.Comment, property);

            string normalized = NormalizeNodeProperty(node.Kind, property);
            if (string.IsNullOrWhiteSpace(normalized))
                return normalized;

            if (TryGetBoundTargetBlock(node, out Block targetBlock) &&
                TryResolveNativePropertyLabel(node.Kind, targetBlock.GetType(), normalized, out string resolvedFromTarget))
            {
                return resolvedFromTarget;
            }

            Type blockType = null;
            if (node.NativeComponent is GenericBlockGetter getter)
                blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
            else if (node.NativeComponent is GenericBlockSetter setter)
                blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);

            return blockType != null &&
                   TryResolveNativePropertyLabel(node.Kind, blockType, normalized, out string resolvedFromNative)
                ? resolvedFromNative
                : normalized;
        }

        private IEnumerable<string> NativePropertyOptionsForNode(AutomationGraphNode node)
        {
            if (TryGetBoundTargetBlock(node, out Block targetBlock))
            {
                if (node.Kind == AutomationNodeKind.InputGetter)
                    return EnumerateGetterPropertyLabels(targetBlock.GetType());
                if (node.Kind == AutomationNodeKind.OutputSetter)
                    return EnumerateSetterPropertyLabels(targetBlock.GetType());
            }

            if (node?.NativeComponent is GenericBlockGetter getter)
            {
                Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
                return blockType == null
                    ? Enumerable.Empty<string>()
                    : EnumerateGetterPropertyLabels(blockType);
            }

            if (node?.NativeComponent is GenericBlockSetter setter)
            {
                Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
                return blockType == null
                    ? Enumerable.Empty<string>()
                    : EnumerateSetterPropertyLabels(blockType);
            }

            return Enumerable.Empty<string>();
        }

        private static bool IsResolvedNativeProperty(
            AutomationNodeKind kind,
            Type blockType,
            string property)
        {
            return TryResolveNativePropertyLabel(kind, blockType, property, out _);
        }

        private static bool TryResolveNativePropertyLabel(
            AutomationNodeKind kind,
            Type blockType,
            string property,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            if (kind == AutomationNodeKind.InputGetter)
            {
                if (TryMatchGetterReadable(blockType, property, out uint readableId) &&
                    TryGetReadableLabel(blockType, readableId, out label))
                {
                    return true;
                }

                if (TryMatchGetterVariable(blockType, property, out uint setId, out uint propertyId) &&
                    TryGetVariableLabel(blockType, editableOnly: false, setId, propertyId, out label))
                {
                    return true;
                }

                return false;
            }

            if (kind == AutomationNodeKind.OutputSetter)
            {
                if (TryMatchSetterVariable(blockType, property, out uint setId, out uint propertyId) &&
                    TryGetVariableLabel(blockType, editableOnly: true, setId, propertyId, out label))
                {
                    return true;
                }

                return false;
            }

            label = NormalizeNodeProperty(kind, property);
            return true;
        }

        private static bool StagedNodeHasResolvedProperty(AutomationGraphNode node)
        {
            return TryGetBoundTargetBlock(node, out Block targetBlock) &&
                   IsResolvedNativeProperty(node.Kind, targetBlock.GetType(), node.Property);
        }

        private bool ApplyGraphToNativeBoard()
        {
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                NotifyNativeApply("Select a live breadboard before applying an automation graph.", EsuHudNotificationKind.Warning);
                return false;
            }

            NativeApplyResult result;
            try
            {
                result = ValidateAndConnectNativeGraph(breadboard);
            }
            catch (Exception exception)
            {
                NotifyNativeApply(
                    "Apply stopped before native automation could be changed.",
                    EsuHudNotificationKind.Warning,
                    exception.GetType().Name + ": " + exception.Message);
                return false;
            }
            NotifyNativeApply(result.Message, result.Kind, result.Detail);
            if (result.Kind == EsuHudNotificationKind.Info)
            {
                ClearAutomationDirty();
                ResetGraphEditHistory();
                return true;
            }

            return false;
        }

        private void CheckNativeGraphPlan()
        {
            RefreshNativeAutomationCache(force: true);
            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            NativePlan plan = BuildSelectedNativePlan(graph);
            NotifyNativeApply(
                plan.HasErrors ? "Check found blocking automation plan errors." : "Check produced a native automation plan.",
                plan.HasErrors ? EsuHudNotificationKind.Warning : EsuHudNotificationKind.Info,
                plan.Detail);
        }

        private bool RevertEsuOwnedNativeGraph()
        {
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                NotifyNativeApply("Select a live breadboard before reverting ESU-owned native automation.", EsuHudNotificationKind.Warning);
                return false;
            }

            if (_automationDirty)
            {
                NotifyNativeApply(
                    "Revert requires a clean Automation Builder draft.",
                    EsuHudNotificationKind.Warning,
                    "Apply the current block draft or close it with explicit discard before removing the already-applied ESU-owned native graph.");
                return false;
            }

            List<NativeOwnerRecord> records = NativeOwnerRecords(breadboard).ToList();
            if (records.Count == 0)
            {
                NotifyNativeApply("No ESU-owned generated native automation was found on this breadboard.", EsuHudNotificationKind.Info);
                return true;
            }

            List<NativeInputConnectionState> connectionsBeforeRevert =
                CaptureNativeInputConnections(breadboard);
            List<NativeComponentEditState> componentsBeforeRevert =
                CaptureOwnedNativeComponentStates(breadboard);
            var ownedComponents = new HashSet<CircuitComponent>(records
                .Where(record => record?.Target != null &&
                                 !ReferenceEquals(record.Target, record.Marker))
                .Select(record => record.Target));
            int expectedWires = CountNativeInputConnectionsFromSources(
                breadboard,
                ownedComponents);
            int wiresRemoved = RemoveNativeInputConnectionsFromSources(
                breadboard,
                ownedComponents);
            if (wiresRemoved != expectedWires)
            {
                bool restored = RestoreNativeInputConnections(
                    breadboard,
                    connectionsBeforeRevert);
                NotifyNativeApply(
                    "Revert stopped because vanilla did not release every ESU-owned wire.",
                    EsuHudNotificationKind.Warning,
                    "No owned component was removed. Previous input connections were " +
                    (restored ? "restored." : "not fully restorable; inspect the native board."));
                return false;
            }

            NativeRemovalBatchResult removal = RemoveNativeOwnerRecordsAtomically(
                breadboard,
                records);
            if (!removal.Success)
            {
                bool packagesRestored = RestoreNativeComponentPackages(
                    breadboard,
                    removal.RemovedPackages);
                packagesRestored &= NativeOwnerRecordsMatch(
                    breadboard,
                    records);
                bool componentsRestored = RestoreOwnedNativeComponentStates(
                    componentsBeforeRevert);
                bool connectionsRestored = RestoreNativeInputConnections(
                    breadboard,
                    connectionsBeforeRevert);
                NotifyNativeApply(
                    "Revert rolled back because native package removal could not be verified.",
                    EsuHudNotificationKind.Warning,
                    CombineDetail(
                        removal.Detail,
                        "Removed packages were " +
                        (packagesRestored ? "restored." : "not fully restorable; inspect the native board."),
                        "Owned component fields were " +
                        (componentsRestored ? "restored." : "not fully restorable; inspect the native board."),
                        "Previous input connections were " +
                        (connectionsRestored ? "restored." : "not fully restorable; inspect the native board.")));
                return false;
            }

            try
            {
                RefreshNativeAutomationCache(force: true);
                if (_selectedBreadboard != null)
                    SyncedGraphFor(_selectedBreadboard, force: true);
            }
            catch (Exception exception)
            {
                bool packagesRestored = RestoreNativeComponentPackages(
                    breadboard,
                    removal.RemovedPackages);
                packagesRestored &= NativeOwnerRecordsMatch(
                    breadboard,
                    records);
                bool componentsRestored = RestoreOwnedNativeComponentStates(
                    componentsBeforeRevert);
                bool connectionsRestored = RestoreNativeInputConnections(
                    breadboard,
                    connectionsBeforeRevert);
                InvalidateNativeAutomationCache();
                NotifyNativeApply(
                    "Revert rolled back because the resulting native graph could not be refreshed.",
                    EsuHudNotificationKind.Warning,
                    exception.GetType().Name + ": " + exception.Message +
                    " Packages were " +
                    (packagesRestored ? "restored" : "not fully restored") +
                    ", component fields were " +
                    (componentsRestored ? "restored" : "not fully restored") +
                    " and input connections were " +
                    (connectionsRestored ? "restored." : "not fully restored; inspect the native board."));
                return false;
            }

            foreach (NativeOwnerRecord record in records)
            {
                _pendingNativeNodeDrafts.Remove(record.ComponentId);
                _pendingNativeNodeRects.Remove(record.ComponentId);
                _pendingNativeNodeRemovals.Remove(record.ComponentId);
            }

            ClearAutomationDirty();
            ResetGraphEditHistory();
            NotifyNativeApply(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Revert removed {0:N0} ESU-owned native component{1}, {2:N0} marker{3}, and {4:N0} native wire{5}.",
                    removal.RemovedNodes,
                    removal.RemovedNodes == 1 ? string.Empty : "s",
                    removal.RemovedMarkers,
                    removal.RemovedMarkers == 1 ? string.Empty : "s",
                    wiresRemoved,
                    wiresRemoved == 1 ? string.Empty : "s"),
                EsuHudNotificationKind.Info);
            return true;
        }

        private NativePlan BuildSelectedNativePlan(AutomationGraph graph)
        {
            NativeBreadBoard breadboard = null;
            string breadboardMessage = null;
            bool hasBreadboard = TryResolveNativeBreadboard(
                _selectedBreadboard,
                out breadboard,
                out breadboardMessage);
            NativePlan plan = BuildNativePlan(graph, hasBreadboard ? breadboard : null);
            if (!hasBreadboard)
                plan.AddError(breadboardMessage ?? "Select a live breadboard before checking an automation graph.");
            return plan;
        }

        private NativePlan BuildNativePlan(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            var plan = new NativePlan();
            if (graph == null || graph.Nodes.Count == 0)
            {
                plan.AddWarning("No ESU graph nodes are staged for native lowering.");
            }
            else
            {
                foreach (string issue in NativeGraphReadinessIssues(graph))
                    plan.AddError(issue);

                foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
                {
                    if (node == null)
                        continue;

                    if (node.IsStaged)
                    {
                        plan.AddCreate(NativePlanCreateLine(node));
                        CircuitComponent preview = CreateLooseNativeComponent(node.Kind);
                        if (preview == null)
                        {
                            plan.AddError(NodeTitle(node.Kind) + " has no native lowering adapter.");
                        }
                        else if (breadboard != null && !BoardAdvertisesNativeComponent(breadboard, preview))
                        {
                            plan.AddError(
                                "The selected vanilla breadboard does not advertise " +
                                preview.GetType().Name + " for " + NodeTitle(node.Kind) + ".");
                        }
                    }
                    else if (HasPendingNativeNodeDraft(node))
                        plan.AddUpdate("Apply pending ESU edits to " + NativePlanSentence(node) + ".");
                    else if (IsEsuOwnedNativeNode(node))
                        plan.AddUpdate(NativePlanUpdateLine(node));
                    else
                        plan.AddUpdate(NativePlanImportedLine(node));
                }

                foreach (AutomationLink link in _links.Where(link => link?.IsStaged == true))
                    plan.AddUpdate(NativePlanLinkBindingLine(graph, link));

                foreach (string line in NativeConnectionPreviewLines(graph, true))
                {
                    string trimmed = (line ?? string.Empty).Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("(", StringComparison.Ordinal))
                        continue;

                    plan.AddUpdate("Connect/update " + trimmed);
                }
            }

            if (breadboard != null)
            {
                int supportedNativeCount = breadboard.Components.Count(IsSupportedAutomationNativeComponent);
                if (supportedNativeCount == 0 &&
                    graph?.Nodes.Any(node => node?.IsStaged == true) != true)
                {
                    plan.AddWarning("The selected native breadboard has no supported automation components.");
                }

                foreach (NativeOwnerRecord record in OwnerRecordsFor(breadboard))
                {
                    if (_pendingNativeNodeRemovals.Contains(record.ComponentId))
                        plan.AddRemove("Apply will remove ESU-owned native " + NativeComponentName(record.Kind) + " #" + record.ComponentId.ToString(CultureInfo.InvariantCulture) + ".");

                    plan.AddRemove(NativePlanRemoveLine(record));
                }

                int ownedInputConnections = CountEsuOwnedNativeInputConnections(breadboard);
                if (ownedInputConnections > 0)
                {
                    plan.AddRemove(
                        "Apply will clear and rebuild " +
                        ownedInputConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " ESU-owned native input connection" +
                        (ownedInputConnections == 1 ? string.Empty : "s") +
                        ".");
                }

                int esuSourcedConnections = CountNativeInputConnectionsFromSources(
                    breadboard,
                    EsuOwnedNativeComponentsFor(breadboard));
                if (esuSourcedConnections > 0)
                {
                    plan.AddRemove(
                        "Revert can clear " +
                        esuSourcedConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " native input connection" +
                        (esuSourcedConnections == 1 ? string.Empty : "s") +
                        " sourced from ESU-owned components.");
                }
            }

            if (plan.Creates.Count == 0)
                plan.AddWarning("No staged ESU nodes need native creation; Check is only validating/importing existing native graph state.");

            return plan;
        }

        private static string NativePlanCreateLine(AutomationGraphNode node)
        {
            return "Create native " + NativeComponentName(node.Kind) +
                   " for " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanUpdateLine(AutomationGraphNode node)
        {
            return "Update existing native " + NativeComponentName(node.Kind) +
                   " #" + node.Id.ToString(CultureInfo.InvariantCulture) +
                   " from " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanImportedLine(AutomationGraphNode node)
        {
            return "Import read-only native " + NativeComponentName(node.Kind) +
                   " #" + node.Id.ToString(CultureInfo.InvariantCulture) +
                   " from " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanLinkBindingLine(
            AutomationGraph graph,
            AutomationLink link)
        {
            string direction = link.Kind == AutomationLinkKind.InputToBreadboard
                ? "input"
                : "output";
            string source = link.Source?.Name ?? "missing source";
            string target = link.Target?.Name ?? "missing target";
            AutomationGraphNode boundNode = graph?.Nodes.FirstOrDefault(node =>
                NodeBindingTargetsMatchLink(node, link));
            string propertyText = boundNode?.TargetBinding?.Property;
            if (string.IsNullOrWhiteSpace(propertyText))
                propertyText = boundNode?.Property;
            if (string.IsNullOrWhiteSpace(propertyText))
                propertyText = link.Property;
            string property = string.IsNullOrWhiteSpace(propertyText)
                ? "unresolved property"
                : propertyText;
            return "Bind staged " + direction + " target " + source + " -> " + target + " | " + property + ".";
        }

        private static string NativePlanRemoveLine(NativeOwnerRecord record)
        {
            if (record?.Target == null)
            {
                return "Revert stale ESU ownership marker for missing native component #" +
                       (record == null ? "unknown" : record.ComponentId.ToString(CultureInfo.InvariantCulture)) +
                       ".";
            }

            return "Revert can remove ESU-owned native " +
                   NativeComponentName(NativeKind(record.Target)) +
                   " #" +
                   record.ComponentId.ToString(CultureInfo.InvariantCulture) +
                   ".";
        }

        private static string FormatGraphPoint(Vector2 point)
        {
            return "(" +
                   point.x.ToString("0.#", CultureInfo.InvariantCulture) +
                   ", " +
                   point.y.ToString("0.#", CultureInfo.InvariantCulture) +
                   ")";
        }

        private static HashSet<CircuitComponent> EsuOwnedNativeComponents(NativeBreadBoard breadboard)
        {
            return new HashSet<CircuitComponent>(
                NativeOwnerRecords(breadboard)
                .Where(record => record?.Target != null &&
                                 !ReferenceEquals(record.Target, record.Marker))
                .Select(record => record.Target));
        }

        private IReadOnlyList<NativeOwnerRecord> OwnerRecordsFor(NativeBreadBoard breadboard)
        {
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            return snapshot == null
                ? Array.Empty<NativeOwnerRecord>()
                : (IReadOnlyList<NativeOwnerRecord>)snapshot.OwnerRecords;
        }

        private HashSet<CircuitComponent> EsuOwnedNativeComponentsFor(NativeBreadBoard breadboard)
        {
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            return snapshot == null
                ? new HashSet<CircuitComponent>()
                : new HashSet<CircuitComponent>(snapshot.OwnedComponents);
        }

        private static bool IsEsuOwnedNativeComponent(
            ISet<CircuitComponent> ownedComponents,
            CircuitComponent component)
        {
            return component != null &&
                   ownedComponents != null &&
                   ownedComponents.Contains(component);
        }

        private static int CountEsuOwnedNativeInputConnections(NativeBreadBoard breadboard)
        {
            HashSet<CircuitComponent> ownedComponents = EsuOwnedNativeComponents(breadboard);
            int count = 0;
            foreach (CircuitComponent component in ownedComponents)
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        ShouldManageOwnedInputConnection(input, ownedComponents))
                        count++;
                }
            }

            return count;
        }

        private static int RemoveEsuOwnedNativeInputConnections(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> ownedComponents)
        {
            if (breadboard == null || ownedComponents == null || ownedComponents.Count == 0)
                return 0;

            int removed = 0;
            foreach (CircuitComponent component in ownedComponents.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us.ToList())
                {
                    if (ShouldManageOwnedInputConnection(input, ownedComponents) &&
                        RemoveNativeInputConnection(breadboard, input))
                        removed++;
                }
            }

            return removed;
        }

        private static bool ShouldManageOwnedInputConnection(
            BInput input,
            ISet<CircuitComponent> ownedComponents)
        {
            CircuitComponent source = input?.OurOutput?.IsLatched == true
                ? input.OurOutput.Them?.OurComponent
                : null;
            return source != null &&
                   IsEsuOwnedNativeComponent(ownedComponents, source);
        }

        private static int CountNativeInputConnectionsFromSources(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> sourceComponents)
        {
            if (breadboard == null || sourceComponents == null || sourceComponents.Count == 0)
                return 0;

            int count = 0;
            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        IsEsuOwnedNativeComponent(sourceComponents, input.OurOutput.Them?.OurComponent))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int RemoveNativeInputConnectionsFromSources(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> sourceComponents)
        {
            if (breadboard == null || sourceComponents == null || sourceComponents.Count == 0)
                return 0;

            int removed = 0;
            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us.ToList())
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        IsEsuOwnedNativeComponent(sourceComponents, input.OurOutput.Them?.OurComponent) &&
                        RemoveNativeInputConnection(breadboard, input))
                    {
                        removed++;
                    }
                }
            }

            return removed;
        }

        private NativeRemovalBatchResult ApplyPendingNativeNodeRemovalsToNative(
            NativeBreadBoard breadboard)
        {
            if (breadboard == null || _pendingNativeNodeRemovals.Count == 0)
                return NativeRemovalBatchResult.Empty;

            List<uint> componentIds = _pendingNativeNodeRemovals.ToList();
            List<NativeOwnerRecord> liveRecords = NativeOwnerRecords(breadboard).ToList();
            List<NativeOwnerRecord> records = componentIds
                .Select(componentId => liveRecords.FirstOrDefault(candidate =>
                    candidate.ComponentId == componentId))
                .Where(record => record != null)
                .ToList();
            NativeRemovalBatchResult result = RemoveNativeOwnerRecordsAtomically(
                breadboard,
                records);
            if (!result.Success)
                return result;

            foreach (uint componentId in componentIds)
            {
                _pendingNativeNodeDrafts.Remove(componentId);
                _pendingNativeNodeRects.Remove(componentId);
                _pendingNativeNodeRemovals.Remove(componentId);
            }

            return result;
        }

        private static NativeRemovalBatchResult RemoveNativeOwnerRecordsAtomically(
            NativeBreadBoard breadboard,
            IReadOnlyList<NativeOwnerRecord> records)
        {
            if (breadboard == null)
            {
                return NativeRemovalBatchResult.Failed(
                    "The native breadboard was unavailable for component removal.",
                    restored: true);
            }

            if (records == null || records.Count == 0)
                return NativeRemovalBatchResult.Empty;

            var removedPackages = new List<CircuitComponent>();
            int removedNodes = 0;
            int removedMarkers = 0;
            string failure = null;
            try
            {
                foreach (NativeOwnerRecord record in records)
                {
                    if (record == null)
                        continue;

                    if (record.Target != null &&
                        !ReferenceEquals(record.Target, record.Marker) &&
                        IsNativeComponentPresent(breadboard, record.Target))
                    {
                        if (!RemoveNativeComponentPackage(breadboard, record.Target))
                        {
                            failure = "Vanilla did not remove owned component #" +
                                      record.ComponentId.ToString(CultureInfo.InvariantCulture) + ".";
                            break;
                        }

                        removedPackages.Add(record.Target);
                        removedNodes++;
                    }

                    if (record.Marker != null &&
                        IsNativeComponentPresent(breadboard, record.Marker))
                    {
                        if (!RemoveNativeComponentPackage(breadboard, record.Marker))
                        {
                            failure = "Vanilla did not remove the ownership marker for component #" +
                                      record.ComponentId.ToString(CultureInfo.InvariantCulture) + ".";
                            break;
                        }

                        removedPackages.Add(record.Marker);
                        removedMarkers++;
                    }
                }
            }
            catch (Exception exception)
            {
                failure = "Native removal threw " + exception.GetType().Name + ".";
            }

            try
            {
                if (failure == null)
                {
                    bool allAbsent = records.All(record =>
                        record == null ||
                        (record.Target == null ||
                         ReferenceEquals(record.Target, record.Marker) ||
                         !IsNativeComponentPresent(breadboard, record.Target)) &&
                        (record.Marker == null ||
                         !IsNativeComponentPresent(breadboard, record.Marker)));
                    if (allAbsent)
                    {
                        return NativeRemovalBatchResult.Completed(
                            removedNodes,
                            removedMarkers,
                            removedPackages);
                    }

                    failure = "Vanilla retained an owned component package after its removal command.";
                }
            }
            catch (Exception exception)
            {
                failure = "Native removal verification threw " + exception.GetType().Name + ".";
            }

            bool restored;
            try
            {
                restored = RestoreNativeComponentPackages(
                    breadboard,
                    removedPackages);
                restored &= NativeOwnerRecordsMatch(breadboard, records);
            }
            catch
            {
                restored = false;
            }

            return NativeRemovalBatchResult.Failed(
                failure,
                restored,
                removedPackages);
        }

        private static bool RestoreNativeComponentPackages(
            NativeBreadBoard breadboard,
            IReadOnlyList<CircuitComponent> components,
            IDictionary<uint, uint> componentIdRemap = null)
        {
            if (breadboard == null)
                return false;

            List<CircuitComponent> restore = (components ?? Array.Empty<CircuitComponent>())
                .Where(component => component != null)
                .Distinct()
                .ToList();
            List<CircuitComponent> available = breadboard.Components
                .Concat(breadboard.Packages)
                .Concat(restore)
                .Where(component => component != null)
                .Distinct()
                .ToList();
            var ownership = new List<NativeRestoreOwnership>();
            foreach (CircuitComponent marker in available.Where(IsNativeOwnerMarker))
            {
                if (!TryParseNativeOwnerMarker(marker, out uint oldTargetId, out AutomationNodeKind kind))
                    continue;

                CircuitComponent target = available.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, marker) &&
                    !IsNativeOwnerMarker(candidate) &&
                    candidate.UniqueId == oldTargetId);
                if (target != null)
                    ownership.Add(new NativeRestoreOwnership(
                        oldTargetId,
                        target,
                        marker,
                        kind));
            }

            bool restored = true;
            foreach (CircuitComponent component in restore.Where(component => !IsNativeOwnerMarker(component)))
            {
                if (IsNativeComponentPresent(breadboard, component))
                    continue;

                try
                {
                    new AddComponentCommand(breadboard, component).Execute();
                }
                catch
                {
                    // The postcondition below decides whether a command that
                    // threw after a partial add nevertheless restored the item.
                }

                restored &= IsNativeComponentPresent(breadboard, component);
            }

            foreach (NativeRestoreOwnership pair in ownership)
            {
                if (componentIdRemap != null)
                    componentIdRemap[pair.OriginalTargetId] = pair.Target.UniqueId;
                if (!(pair.Marker is NativeComment marker))
                {
                    restored = false;
                    continue;
                }

                marker.InputValue.Us = RewriteNativeOwnerMarkerComponentId(
                    marker.InputValue.Us,
                    pair.Target.UniqueId);
            }

            foreach (CircuitComponent component in restore.Where(IsNativeOwnerMarker))
            {
                if (IsNativeComponentPresent(breadboard, component))
                    continue;

                try
                {
                    new AddComponentCommand(breadboard, component).Execute();
                }
                catch
                {
                    // The postcondition below decides whether a command that
                    // threw after a partial add nevertheless restored the item.
                }

                restored &= IsNativeComponentPresent(breadboard, component);
            }

            List<NativeOwnerRecord> actualRecords = NativeOwnerRecords(breadboard).ToList();
            foreach (NativeRestoreOwnership pair in ownership)
            {
                restored &= actualRecords.Any(actual =>
                    actual.Kind == pair.Kind &&
                    ReferenceEquals(actual.Target, pair.Target) &&
                    ReferenceEquals(actual.Marker, pair.Marker));
            }

            return restored;
        }

        private static bool NativeOwnerRecordsMatch(
            NativeBreadBoard breadboard,
            IReadOnlyList<NativeOwnerRecord> expectedRecords)
        {
            try
            {
                List<NativeOwnerRecord> actualRecords = NativeOwnerRecords(breadboard).ToList();
                return (expectedRecords ?? Array.Empty<NativeOwnerRecord>()).All(expected =>
                    expected == null ||
                    actualRecords.Any(actual =>
                        actual.Kind == expected.Kind &&
                        ReferenceEquals(actual.Target, expected.Target) &&
                        ReferenceEquals(actual.Marker, expected.Marker)));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNativeComponentPresent(
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            return breadboard != null &&
                   component != null &&
                   (breadboard.Components.Contains(component) ||
                    breadboard.Packages.Contains(component));
        }

        private static bool RemoveNativeInputConnection(
            NativeBreadBoard breadboard,
            BInput input)
        {
            if (breadboard == null ||
                input?.OurOutput?.IsLatched != true)
            {
                return false;
            }

            try
            {
                new DeleteConnectionCommand(breadboard, input).Execute();
            }
            catch
            {
                // Fall through to the direct latch API below.
            }

            if (input.OurOutput?.IsLatched == true)
            {
                try
                {
                    input.OurOutput.Unlatch();
                }
                catch
                {
                    return false;
                }
            }

            return input.OurOutput?.IsLatched != true;
        }

        private static List<NativeInputConnectionState> CaptureNativeInputConnections(
            NativeBreadBoard breadboard)
        {
            var result = new List<NativeInputConnectionState>();
            if (breadboard == null)
                return result;

            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        input.OurOutput.Them != null)
                    {
                        result.Add(new NativeInputConnectionState(input, input.OurOutput.Them));
                    }
                }
            }

            return result;
        }

        private List<NativeComponentEditState> CaptureOwnedNativeComponentStates(
            NativeBreadBoard breadboard)
        {
            var result = new List<NativeComponentEditState>();
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            if (snapshot == null || _selectedBreadboard == null)
                return result;

            foreach (NativeOwnerRecord owner in snapshot.OwnerRecords)
            {
                if (owner == null)
                    continue;

                if (owner.Target != null && !ReferenceEquals(owner.Target, owner.Marker))
                    result.Add(NativeComponentEditState.Capture(owner.Target));
                if (owner.Marker != null && !ReferenceEquals(owner.Marker, owner.Target))
                    result.Add(NativeComponentEditState.Capture(owner.Marker));
            }

            return result;
        }

        private static bool RestoreOwnedNativeComponentStates(
            IReadOnlyList<NativeComponentEditState> snapshot)
        {
            bool restored = true;
            foreach (NativeComponentEditState state in snapshot ?? Array.Empty<NativeComponentEditState>())
            {
                if (state == null)
                    continue;

                restored &= state.TryRestore();
            }

            return restored;
        }

        private static bool RestoreNativeInputConnections(
            NativeBreadBoard breadboard,
            IReadOnlyList<NativeInputConnectionState> snapshot)
        {
            if (breadboard == null)
                return false;

            var expected = (snapshot ?? Array.Empty<NativeInputConnectionState>())
                .Where(state => state?.Input != null && state.Output != null)
                .ToDictionary(state => state.Input, state => state.Output);
            bool restored = true;
            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us.ToList())
                {
                    if (input?.OurOutput?.IsLatched != true)
                        continue;

                    if (!expected.TryGetValue(input, out AOutput output) ||
                        !ReferenceEquals(input.OurOutput.Them, output))
                    {
                        restored &= RemoveNativeInputConnection(breadboard, input);
                    }
                }
            }

            foreach (KeyValuePair<BInput, AOutput> pair in expected)
            {
                if (pair.Key?.OurOutput?.IsLatched == true &&
                    ReferenceEquals(pair.Key.OurOutput.Them, pair.Value))
                {
                    continue;
                }

                try
                {
                    new CreateConnectionCommand(breadboard, pair.Value, pair.Key).Execute();
                }
                catch
                {
                    restored = false;
                }

                restored &= pair.Key?.OurOutput?.IsLatched == true &&
                            ReferenceEquals(pair.Key.OurOutput.Them, pair.Value);
            }

            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched != true)
                        continue;

                    restored &= expected.TryGetValue(input, out AOutput output) &&
                                ReferenceEquals(input.OurOutput.Them, output);
                }
            }

            return restored;
        }

        private static CircuitComponent NativeValueForGraphSlot(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (IsStackFedPrimaryValueSlot(graph, host, slotKind))
                return null;

            AutomationGraphConnection connection = GraphValueConnection(graph, host, slotKind);
            return TryGetNativeComponent(
                connection?.From,
                out CircuitComponent component)
                ? component
                : null;
        }

        private NativeApplyResult ValidateAndConnectNativeGraph(NativeBreadBoard breadboard)
        {
            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            NativePlan plan = BuildNativePlan(graph, breadboard);
            if (plan.Errors.Count > 0)
            {
                return NativeApplyResult.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Apply blocked: fix {0:N0} automation readiness issue{1} first.",
                        plan.Errors.Count,
                        plan.Errors.Count == 1 ? string.Empty : "s"),
                    plan.Detail);
            }

            AutomationGraphEditSnapshot graphBeforeApply =
                CaptureGraphEditSnapshot(graph, _automationDirty);
            List<NativeInputConnectionState> connectionsBeforeApply =
                CaptureNativeInputConnections(breadboard);
            List<NativeComponentEditState> componentsBeforeApply =
                CaptureOwnedNativeComponentStates(breadboard);
            _nativeApplyNameChanges.Clear();
            NativeLoweringResult lowering = null;
            NativeRemovalBatchResult removal = NativeRemovalBatchResult.Empty;
            try
            {
                lowering = LowerStagedGraphNodesToNative(graph, breadboard);
                if (lowering.HasErrors)
                {
                    bool namesRestored = RestoreNativeApplyBlockNames();
                    return NativeApplyResult.Warning(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Apply blocked: {0:N0} staged block{1} could not be lowered.",
                            lowering.Errors.Count,
                            lowering.Errors.Count == 1 ? string.Empty : "s"),
                        CombineDetail(
                            lowering.Detail,
                            namesRestored
                                ? null
                                : "A linked block name could not be restored; inspect the target block."));
                }

                if (lowering.CreatedCount > 0)
                {
                    RefreshNativeAutomationCache(force: true);
                    graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
                    graph?.RebindConnectionsToCurrentNodes();
                    if (!CreatedNativeComponentsImported(
                            breadboard,
                            graph,
                            lowering.CreatedComponents,
                            out string importIssue))
                    {
                        bool restored = RollbackFailedNativeApply(
                            breadboard,
                            graph,
                            graphBeforeApply,
                            connectionsBeforeApply,
                            componentsBeforeApply,
                            lowering.CreatedComponents);
                        return NativeApplyResult.Warning(
                            "Apply rolled back because lowered native components could not be re-imported for verification.",
                            CombineDetail(
                                importIssue,
                                "Previous native state was " +
                                (restored ? "restored." : "not fully restorable; inspect the native board.")));
                    }
                }

                int removedNodes = 0;
                int appliedNodeEdits = ApplyPendingNativeNodeDraftsToNative(graph, breadboard);
                if (appliedNodeEdits > 0)
                {
                    RefreshNativeAutomationCache(force: true);
                    graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
                    graph?.RebindConnectionsToCurrentNodes();
                }

                graph?.RebindConnectionsToCurrentNodes();

                List<AutomationGraphNode> nativeNodes = graph?.Nodes
                    .Where(node => node?.NativeComponent is CircuitComponent component &&
                                   IsSupportedAutomationNativeComponent(component))
                    .OrderBy(node => node.Rect.y)
                    .ThenBy(node => node.Rect.x)
                    .ToList();
                if (nativeNodes == null || nativeNodes.Count == 0)
                {
                    if (lowering.CreatedCount > 0)
                    {
                        bool restored = RollbackFailedNativeApply(
                            breadboard,
                            graph,
                            graphBeforeApply,
                            connectionsBeforeApply,
                            componentsBeforeApply,
                            lowering.CreatedComponents,
                            removal.RemovedPackages);
                        return NativeApplyResult.Warning(
                            "Apply rolled back because lowered native components could not be re-imported for verification.",
                            "Previous native state was " +
                            (restored ? "restored." : "not fully restorable; inspect the native board."));
                    }

                    removal = ApplyPendingNativeNodeRemovalsToNative(breadboard);
                    if (!removal.Success)
                    {
                        bool restored = RollbackFailedNativeApply(
                            breadboard,
                            graph,
                            graphBeforeApply,
                            connectionsBeforeApply,
                            componentsBeforeApply,
                            lowering.CreatedComponents,
                            removal.RemovedPackages);
                        return NativeApplyResult.Warning(
                            "Apply rolled back because owned component removal could not be verified.",
                            CombineDetail(
                                removal.Detail,
                                "Removed packages were " +
                                (removal.Restored ? "restored." : "not fully restorable; inspect the native board."),
                                "Previous native state was " +
                                (restored ? "restored." : "not fully restorable; inspect the native board.")));
                    }

                    removedNodes = removal.RemovedNodes;
                    if (removedNodes > 0 || appliedNodeEdits > 0)
                    {
                        RefreshNativeAutomationCache(force: true);
                        graph = _selectedBreadboard == null
                            ? null
                            : SyncedGraphFor(_selectedBreadboard, force: true);
                        graph?.RebindConnectionsToCurrentNodes();
                        _nativeApplyNameChanges.Clear();
                        return new NativeApplyResult(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Native graph applied: {0:N0} staged block{1} lowered, {2:N0} owned edit{3} applied, {4:N0} owned node{5} removed.",
                                lowering.CreatedCount,
                                lowering.CreatedCount == 1 ? string.Empty : "s",
                                appliedNodeEdits,
                                appliedNodeEdits == 1 ? string.Empty : "s",
                                removedNodes,
                                removedNodes == 1 ? string.Empty : "s"),
                            EsuHudNotificationKind.Info,
                            lowering.Detail);
                    }

                    return NativeApplyResult.Warning("The native breadboard graph has no components to connect.");
                }

                HashSet<CircuitComponent> esuOwnedComponents = EsuOwnedNativeComponentsFor(breadboard);
                int expectedRemovedConnections = CountEsuOwnedNativeInputConnections(breadboard);
                int removedConnections = RemoveEsuOwnedNativeInputConnections(breadboard, esuOwnedComponents);
                if (removedConnections != expectedRemovedConnections)
                {
                    bool restored = RollbackFailedNativeApply(
                        breadboard,
                        graph,
                        graphBeforeApply,
                        connectionsBeforeApply,
                        componentsBeforeApply,
                        lowering.CreatedComponents,
                        removal.RemovedPackages);
                    return NativeApplyResult.Warning(
                        "Apply rolled back because stale ESU-owned native wires could not be cleared.",
                        "Removed " + removedConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " of " + expectedRemovedConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " expected wire" + (expectedRemovedConnections == 1 ? string.Empty : "s") +
                        ". Previous native state was " +
                        (restored ? "restored." : "not fully restorable; inspect the native board."));
                }
                int connected = 0;
                int alreadyConnected = 0;
                int valueConnections = 0;
                int failedConnections = 0;
                IReadOnlyList<AutomationGraphConnection> stackConnections = GraphConnections(graph, AutomationGraphConnectionKind.Stack);
                foreach (AutomationGraphConnection connection in stackConnections)
                {
                    if (!TryGetNativeComponent(connection?.From, out CircuitComponent from) ||
                        !TryGetNativeComponent(connection.To, out CircuitComponent to))
                    {
                        continue;
                    }

                    if (!IsEsuOwnedNativeComponent(esuOwnedComponents, to))
                        continue;

                    if (to is NativeSwitch switchTo)
                    {
                        if (!TryConnectComponentToInput(
                                breadboard,
                                from,
                                switchTo.Switcher,
                                ref connected,
                                ref alreadyConnected))
                        {
                            failedConnections++;
                        }
                        continue;
                    }

                    if (!TryConnectComponentToInputAt(
                            breadboard,
                            from,
                            to,
                            0,
                            ref connected,
                            ref alreadyConnected))
                    {
                        failedConnections++;
                    }
                }

                foreach (AutomationGraphNode hostNode in nativeNodes.Where(node =>
                             TryGetNativeComponent(node, out CircuitComponent component) &&
                             IsEsuOwnedNativeComponent(esuOwnedComponents, component) &&
                             AcceptsValueSlot(node.Kind)))
                {
                    if (!TryGetNativeComponent(hostNode, out CircuitComponent host))
                        continue;

                    if (host is NativeSwitch switchComponent)
                    {
                        AutomationNodeKind switchKind = hostNode.Kind;
                        if (switchKind == AutomationNodeKind.IfCondition)
                        {
                            switchComponent.Threshold.Us = 0.5f;
                        }
                        else
                        {
                            CircuitComponent thresholdValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Threshold);
                            RecordNativeValueApply(
                                thresholdValue,
                                TryApplySwitchThresholdValue(switchComponent, thresholdValue),
                                ref valueConnections,
                                ref failedConnections);
                        }

                        CircuitComponent passValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            passValue,
                            TryConnectComponentToInput(breadboard, passValue, switchComponent.Pass, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent elseValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Else);
                        RecordNativeValueApply(
                            elseValue,
                            TryApplySwitchElseValue(switchComponent, elseValue),
                            ref valueConnections,
                            ref failedConnections);
                        continue;
                    }

                    if (host is Evaluator evaluator &&
                        IsMathEvaluatorKind(hostNode.Kind))
                    {
                        AutomationNodeKind evaluatorKind = hostNode.Kind;
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToInputAt(breadboard, sourceValue, evaluator, 0, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent operandValue = NativeValueForGraphSlot(graph, hostNode, MathOperandSlotKind(evaluatorKind));
                        if (operandValue == null)
                        {
                            if (TryApplyEvaluatorMathOperand(evaluator, operandValue, evaluatorKind))
                                valueConnections++;
                        }
                        else
                        {
                            evaluator.Expression.Us = MathExpressionInputText(evaluatorKind);
                            RecordNativeValueApply(
                                operandValue,
                                TryConnectComponentToInputAt(breadboard, operandValue, evaluator, 1, ref connected, ref alreadyConnected),
                                ref valueConnections,
                                ref failedConnections);
                        }

                        continue;
                    }

                    if (host is LogicGate logicGate &&
                        IsLogicGateKind(hostNode.Kind))
                    {
                        AutomationNodeKind logicKind = hostNode.Kind;
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToInputAt(breadboard, sourceValue, logicGate, 0, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        if (logicKind != AutomationNodeKind.LogicNot)
                        {
                            CircuitComponent secondValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.LogicB);
                            RecordNativeValueApply(
                                secondValue,
                                TryConnectComponentToInputAt(breadboard, secondValue, logicGate, 1, ref connected, ref alreadyConnected),
                                ref valueConnections,
                                ref failedConnections);
                        }

                        continue;
                    }

                    if (host is FuzzyThreshold fuzzyThreshold &&
                        IsFuzzyThresholdKind(hostNode.Kind))
                    {
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent thresholdValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Threshold);
                        RecordNativeValueApply(
                            thresholdValue,
                            TryApplyFuzzyThresholdValue(fuzzyThreshold, thresholdValue),
                            ref valueConnections,
                            ref failedConnections);

                        continue;
                    }

                    if (host is MaxMin maxMin &&
                        IsMaxMinKind(hostNode.Kind))
                    {
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToInputAt(breadboard, sourceValue, maxMin, 0, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent secondValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.MathB);
                        RecordNativeValueApply(
                            secondValue,
                            TryConnectComponentToInputAt(breadboard, secondValue, maxMin, 1, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        continue;
                    }

                    if (host is Clamp clamp)
                    {
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent minValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Min);
                        CircuitComponent maxValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Max);
                        bool hasClampBoundValue = minValue != null || maxValue != null;
                        if (hasClampBoundValue)
                        {
                            if (TryApplyClampBounds(clamp, minValue, maxValue))
                                valueConnections += (minValue == null ? 0 : 1) + (maxValue == null ? 0 : 1);
                            else
                                failedConnections++;
                        }
                        continue;
                    }

                    if (host is Delay delay)
                    {
                        CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                        RecordNativeValueApply(
                            sourceValue,
                            TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected),
                            ref valueConnections,
                            ref failedConnections);

                        CircuitComponent secondsValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Seconds);
                        RecordNativeValueApply(
                            secondsValue,
                            TryApplyDelaySeconds(delay, secondsValue),
                            ref valueConnections,
                            ref failedConnections);
                        continue;
                    }

                    CircuitComponent value = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    RecordNativeValueApply(
                        value,
                        TryConnectComponentToHost(breadboard, value, host, ref connected, ref alreadyConnected),
                        ref valueConnections,
                        ref failedConnections);
                }

                if (failedConnections > 0)
                {
                    bool restored = RollbackFailedNativeApply(
                        breadboard,
                        graph,
                        graphBeforeApply,
                        connectionsBeforeApply,
                        componentsBeforeApply,
                        lowering.CreatedComponents);
                    return NativeApplyResult.Warning(
                        "Apply rolled back because native breadboard connections did not match the visible block program.",
                        failedConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " native connection" + (failedConnections == 1 ? string.Empty : "s") +
                        " failed. Previous connections were " + (restored ? "restored." : "not fully restorable; inspect the runtime log."));
                }

                removal = ApplyPendingNativeNodeRemovalsToNative(breadboard);
                if (!removal.Success)
                {
                    bool restored = RollbackFailedNativeApply(
                        breadboard,
                        graph,
                        graphBeforeApply,
                        connectionsBeforeApply,
                        componentsBeforeApply,
                        lowering.CreatedComponents,
                        removal.RemovedPackages);
                    return NativeApplyResult.Warning(
                        "Apply rolled back because owned component removal could not be verified.",
                        CombineDetail(
                            removal.Detail,
                            "Removed packages were " +
                            (removal.Restored ? "restored." : "not fully restorable; inspect the native board."),
                            "Previous native state was " +
                            (restored ? "restored." : "not fully restorable; inspect the native board.")));
                }

                removedNodes = removal.RemovedNodes;
                RefreshNativeAutomationCache(force: true);
                graph = _selectedBreadboard == null
                    ? null
                    : SyncedGraphFor(_selectedBreadboard, force: true);
                graph?.RebindConnectionsToCurrentNodes();
                if (!CreatedNativeComponentsImported(
                        breadboard,
                        graph,
                        lowering.CreatedComponents,
                        out string finalImportIssue))
                {
                    throw new InvalidOperationException(finalImportIssue);
                }

                string message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Native graph applied: {0:N0} staged block{1} lowered, {2:N0} owned edit{3} applied, {4:N0} owned node{5} removed, {6:N0} stale ESU connection{7} cleared, {8:N0} new connection{9}, {10:N0} already connected, {11:N0} value slot{12}.",
                    lowering.CreatedCount,
                    lowering.CreatedCount == 1 ? string.Empty : "s",
                    appliedNodeEdits,
                    appliedNodeEdits == 1 ? string.Empty : "s",
                    removedNodes,
                    removedNodes == 1 ? string.Empty : "s",
                    removedConnections,
                    removedConnections == 1 ? string.Empty : "s",
                    connected,
                    connected == 1 ? string.Empty : "s",
                    alreadyConnected,
                    valueConnections,
                    valueConnections == 1 ? string.Empty : "s");
                _nativeApplyNameChanges.Clear();
                return new NativeApplyResult(
                    message,
                    EsuHudNotificationKind.Info,
                    CombineDetail(
                        lowering.Detail,
                        "Apply is idempotent: it lowers staged ESU blocks into ESU-owned native components, then connects visual ESU stack chains top-to-bottom and visual socketed value blocks into native inputs without appending duplicates."));
            }
            catch (Exception exception)
            {
                bool restored = RollbackFailedNativeApply(
                    breadboard,
                    graph,
                    graphBeforeApply,
                    connectionsBeforeApply,
                    componentsBeforeApply,
                    lowering?.CreatedComponents,
                    removal?.RemovedPackages);
                return NativeApplyResult.Warning(
                    "Apply rolled back after vanilla rejected an automation operation.",
                    exception.GetType().Name + ": " + exception.Message +
                    " Previous native state was " +
                    (restored
                        ? "restored."
                        : "not fully restorable; inspect the native board and runtime log."));
            }
        }

        private static bool CreatedNativeComponentsImported(
            NativeBreadBoard breadboard,
            AutomationGraph graph,
            IReadOnlyList<CircuitComponent> createdComponents,
            out string issue)
        {
            issue = null;
            List<CircuitComponent> created = (createdComponents ?? Array.Empty<CircuitComponent>())
                .Where(component => component != null)
                .ToList();
            if (created.Count == 0)
                return true;

            try
            {
                List<NativeOwnerRecord> owners = NativeOwnerRecords(breadboard).ToList();
                foreach (CircuitComponent component in created)
                {
                    bool packagePresent = IsNativeComponentPresent(breadboard, component);
                    bool ownerPresent = owners.Any(owner =>
                        owner.ComponentId == component.UniqueId &&
                        ReferenceEquals(owner.Target, component));
                    bool graphPresent = graph?.Nodes.Any(node =>
                        ReferenceEquals(node?.NativeComponent, component)) == true;
                    if (packagePresent && ownerPresent && graphPresent)
                        continue;

                    issue = "Native component #" +
                            component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                            " was not present with the same package, owner marker, and graph node after refresh.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                issue = "Native component re-import verification threw " +
                        exception.GetType().Name + ".";
                return false;
            }
        }

        private static void RecordNativeValueApply(
            CircuitComponent source,
            bool success,
            ref int valueConnections,
            ref int failedConnections)
        {
            if (source == null)
                return;

            if (success)
                valueConnections++;
            else
                failedConnections++;
        }

        private bool RollbackFailedNativeApply(
            NativeBreadBoard breadboard,
            AutomationGraph graph,
            AutomationGraphEditSnapshot graphBeforeApply,
            IReadOnlyList<NativeInputConnectionState> connectionsBeforeApply,
            IReadOnlyList<NativeComponentEditState> componentsBeforeApply,
            IReadOnlyList<CircuitComponent> createdComponents,
            IReadOnlyList<CircuitComponent> removedPackages = null)
        {
            bool restored = true;
            var componentIdRemap = new Dictionary<uint, uint>();
            try
            {
                foreach (CircuitComponent component in (createdComponents ?? Array.Empty<CircuitComponent>())
                             .Where(component => component != null)
                             .Reverse())
                {
                    RemoveNativeOwnerMarkersForComponent(breadboard, component);
                    RemoveNativeComponentPackage(breadboard, component);
                    restored &= !IsNativeComponentPresent(breadboard, component) &&
                                !NativeOwnerRecords(breadboard).Any(record =>
                                    record.ComponentId == component.UniqueId);
                }
            }
            catch
            {
                restored = false;
            }

            try
            {
                restored &= RestoreNativeComponentPackages(
                    breadboard,
                    removedPackages,
                    componentIdRemap);
            }
            catch
            {
                restored = false;
            }

            try
            {
                restored &= RestoreNativeApplyBlockNames();
                restored &= RestoreOwnedNativeComponentStates(componentsBeforeApply);
                restored &= RestoreNativeInputConnections(breadboard, connectionsBeforeApply);
            }
            catch
            {
                restored = false;
            }

            try
            {
                InvalidateNativeAutomationCache();
                RefreshNativeAutomationCache(force: true);
            }
            catch
            {
                restored = false;
            }

            try
            {
                AutomationGraph current = _selectedBreadboard == null
                    ? graph
                    : GraphFor(_selectedBreadboard);
                if (current != null && graphBeforeApply != null)
                {
                    graphBeforeApply.Restore(current);
                    RestoreSnapshotStagedLinks(graphBeforeApply);
                    RestoreSnapshotPendingNativeState(
                        current,
                        graphBeforeApply,
                        componentIdRemap);
                }
            }
            catch
            {
                restored = false;
            }

            _automationDirty = graphBeforeApply?.Dirty == true || !restored;
            InvalidateAutomationDisplayCache();
            return restored;
        }

        private NativeLoweringResult LowerStagedGraphNodesToNative(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            var result = new NativeLoweringResult();
            if (graph == null || breadboard == null)
                return result;

            List<AutomationGraphNode> stagedNodes = graph.Nodes
                .Where(node => node?.IsStaged == true)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            if (stagedNodes.Count == 0)
                return result;

            foreach (AutomationGraphNode node in stagedNodes)
            {
                if (!CanLowerStagedNode(node, out string issue))
                    result.AddError(issue);
            }

            if (result.HasErrors)
                return result;

            var prepared = new List<PreparedNativeNode>();
            foreach (AutomationGraphNode node in stagedNodes)
            {
                try
                {
                    if (TryCreateNativeComponentFromStagedNode(
                            breadboard,
                            node,
                            out CircuitComponent component,
                            out string message,
                            out string warningDetail))
                    {
                        if (!BoardAdvertisesNativeComponent(breadboard, component))
                        {
                            result.AddError(
                                "The selected vanilla breadboard does not advertise " +
                                component.GetType().Name + " for " + NodeTitle(node.Kind) + ".");
                        }
                        else
                        {
                            prepared.Add(new PreparedNativeNode(node, component, warningDetail));
                        }
                    }
                    else
                    {
                        result.AddError(message ?? "Failed to lower staged " + NodeTitle(node.Kind) + ".");
                    }
                }
                catch (Exception exception)
                {
                    result.AddError(
                        "Failed to prepare staged " + NodeTitle(node.Kind) +
                        ": " + exception.GetType().Name + ".");
                }
            }

            if (result.HasErrors)
                return result;

            var committed = new List<PreparedNativeNode>();
            try
            {
                foreach (PreparedNativeNode item in prepared)
                {
                    var addCommand = new AddComponentCommand(breadboard, item.Component);
                    addCommand.Execute();
                    committed.Add(item);
                    AddNativeOwnerMarker(breadboard, item.Component, item.Node.Kind);
                    bool componentPresent = breadboard.Components.Contains(item.Component) ||
                                            breadboard.Packages.Contains(item.Component);
                    bool markerPresent = NativeOwnerRecords(breadboard)
                        .Any(record => record.ComponentId == item.Component.UniqueId &&
                                       ReferenceEquals(record.Target, item.Component));
                    if (!componentPresent || !markerPresent)
                    {
                        throw new InvalidOperationException(
                            "Vanilla breadboard did not retain the component ownership package.");
                    }
                }
            }
            catch (Exception exception)
            {
                bool cleanupSucceeded = true;
                foreach (PreparedNativeNode item in prepared.AsEnumerable().Reverse())
                {
                    RemoveNativeOwnerMarkersForComponent(breadboard, item.Component);
                    RemoveNativeComponentPackage(breadboard, item.Component);
                    cleanupSucceeded &= !IsNativeComponentPresent(breadboard, item.Component) &&
                                        !NativeOwnerRecords(breadboard).Any(record =>
                                            record.ComponentId == item.Component.UniqueId);
                }

                result.AddError(
                    "Native component transaction rolled back after " +
                    exception.GetType().Name +
                    (cleanupSucceeded
                        ? "."
                        : "; cleanup was incomplete, so inspect the native board."));
                return result;
            }

            var loweredIds = new HashSet<int>();
            var loweredIdMap = new Dictionary<int, int>();
            foreach (PreparedNativeNode item in committed)
            {
                loweredIds.Add(item.Node.Id);
                loweredIdMap[item.Node.Id] = NativeGraphNodeId(item.Component);
                result.AddCreated(
                    "Lowered " +
                    NativePlanSentence(item.Node) +
                    " into native " +
                    NativeComponentName(NativeKind(item.Component)) +
                    " #" +
                    item.Component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                    ".");
                result.CreatedComponents.Add(item.Component);
                result.AddWarning(item.WarningDetail);
            }

            if (loweredIds.Count > 0)
            {
                graph.RetargetConnectionNodeIds(loweredIdMap);
                graph.Nodes.RemoveAll(node => node != null && loweredIds.Contains(node.Id));
                if (loweredIds.Contains(graph.SelectedNodeId))
                    graph.SelectedNodeId = 0;
            }

            return result;
        }

        private static bool BoardAdvertisesNativeComponent(
            Board breadboard,
            CircuitComponent component)
        {
            if (breadboard == null || component == null)
                return false;

            if (breadboard.AvailableComponentTypes == null ||
                breadboard.AvailableComponentTypes.Count == 0)
            {
                return true;
            }

            Type componentType = component.GetType();
            Guid componentGuid = component.ComponentTypeId;
            return breadboard.AvailableComponentTypes.Any(candidate =>
                candidate != null &&
                candidate.Valid &&
                (candidate.Type == componentType ||
                 componentGuid != Guid.Empty && candidate.GuidOfComponent == componentGuid));
        }

        private bool CanLowerStagedNode(
            AutomationGraphNode node,
            out string issue)
        {
            issue = null;
            if (node == null)
            {
                issue = "Missing staged graph node.";
                return false;
            }

            if (!CanLowerStagedNodeKind(node.Kind))
            {
                issue = ShortText(BlockSentenceTitle(node), 38) +
                        " cannot be lowered into a supported native breadboard component yet.";
                return false;
            }

            if (node.Kind == AutomationNodeKind.InputGetter ||
                node.Kind == AutomationNodeKind.OutputSetter)
            {
                if (!TryGetBoundTargetBlock(node, out Block targetBlock))
                {
                    issue = ShortText(BlockSentenceTitle(node), 38) + " has no staged target binding.";
                    return false;
                }

                if (!IsResolvedNativeProperty(node.Kind, targetBlock.GetType(), node.Property))
                {
                    issue = ShortText(BlockSentenceTitle(node), 38) + " has no resolved native property.";
                    return false;
                }
            }

            return true;
        }

        private bool TryCreateNativeComponentFromStagedNode(
            NativeBreadBoard breadboard,
            AutomationGraphNode node,
            out CircuitComponent component,
            out string message,
            out string warningDetail)
        {
            component = null;
            message = null;
            warningDetail = null;
            if (breadboard == null || node == null)
            {
                message = "Missing native breadboard or staged graph node.";
                return false;
            }

            component = CreateLooseNativeComponent(node.Kind);
            if (component == null)
            {
                message = "Automation Builder does not know how to lower staged " + NodeTitle(node.Kind) + " yet.";
                return false;
            }

            if (!TryConfigureNativeComponentTargetFromStagedNode(
                    breadboard,
                    node,
                    component,
                    out message,
                    out warningDetail))
            {
                component = null;
                return false;
            }

            bool valueFootprint = DrawsAsValueBlock(node.Kind, node.Rect);
            Rect normalizedRect = NormalizeGraphNodeRect(
                node.Kind,
                node.Rect,
                valueFootprint);
            node.Rect = normalizedRect;
            component.OutlineColor.Us = NodeColor(node.Kind);
            ApplyNativeNodeRect(component, normalizedRect);
            var nativeNode = new AutomationGraphNode(
                NativeGraphNodeId(component),
                node.Kind,
                normalizedRect,
                node.Label,
                node.Property,
                node.ValueText,
                component);
            ApplyNativeNodeToNativeComponent(nativeNode);
            return true;
        }

        private bool TryConfigureNativeComponentTargetFromStagedNode(
            NativeBreadBoard breadboard,
            AutomationGraphNode node,
            CircuitComponent component,
            out string message,
            out string warningDetail)
        {
            message = null;
            warningDetail = null;
            if (node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return true;
            }

            if (!TryGetBoundTargetBlock(node, out Block targetBlock))
            {
                message = ShortText(BlockSentenceTitle(node), 38) + " has no live staged target block.";
                return false;
            }

            var context = new NativeBuildContext(_selectedBreadboard, _links);
            string filter = EnsureExactBlockFilterName(breadboard, targetBlock, context);
            if (component is GenericBlockGetter getter &&
                node.Kind == AutomationNodeKind.InputGetter)
            {
                ConfigureGetterTarget(getter, targetBlock, filter);
                if (!TryConfigureGetterProperty(getter, targetBlock.GetType(), node.Property))
                {
                    message = ShortText(BlockSentenceTitle(node), 38) + " has no readable native property selected.";
                    return false;
                }

                warningDetail = context.HasWarnings ? context.Detail : null;
                return true;
            }

            if (component is GenericBlockSetter setter &&
                node.Kind == AutomationNodeKind.OutputSetter)
            {
                ConfigureSetterTarget(setter, targetBlock, filter);
                if (!TryConfigureSetterProperty(setter, targetBlock.GetType(), node.Property))
                {
                    message = ShortText(BlockSentenceTitle(node), 38) + " has no writable native property selected.";
                    return false;
                }

                warningDetail = context.HasWarnings ? context.Detail : null;
                return true;
            }

            message = "Staged " + NodeTitle(node.Kind) + " did not create the expected native component type.";
            return false;
        }

        private static bool CanLowerStagedNodeKind(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                case AutomationNodeKind.OutputSetter:
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.Random:
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                case AutomationNodeKind.Clamp:
                case AutomationNodeKind.Smooth:
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.Comment:
                    return true;
                default:
                    return false;
            }
        }

        private static string CombineDetail(
            params string[] details)
        {
            return string.Join(
                "\n",
                (details ?? Array.Empty<string>())
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .Select(detail => detail.Trim())
                .Distinct()
                .ToArray());
        }

        private IEnumerable<string> NativeGraphReadinessIssues(AutomationGraph graph)
        {
            if (graph == null || graph.Nodes.Count == 0)
                yield break;

            foreach (string issue in ValueSocketConflictIssues(graph))
                yield return issue;

            foreach (string issue in UnsupportedMixedOwnershipConnectionIssues(graph))
                yield return issue;

            foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
            {
                if (node == null)
                    continue;

                foreach (string issue in NativeNodeReadinessIssues(graph, node))
                    yield return issue;
            }
        }

        private IEnumerable<string> UnsupportedMixedOwnershipConnectionIssues(
            AutomationGraph graph)
        {
            if (graph == null)
                yield break;

            foreach (AutomationGraphConnection connection in graph.Connections.Where(connection =>
                         connection != null &&
                         (connection.Kind == AutomationGraphConnectionKind.Stack ||
                          connection.Kind == AutomationGraphConnectionKind.Value)))
            {
                bool sourceWritable = IsGraphNodeApplyWritable(connection.From);
                bool targetWritable = IsGraphNodeApplyWritable(connection.To);
                if (CanManageEsuConnectionOwnership(sourceWritable, targetWritable) ||
                    !targetWritable)
                {
                    continue;
                }

                yield return ShortText(BlockSentenceTitle(connection.To), 38) +
                             " is fed by a read-only imported vanilla component. " +
                             "ESU cannot own that mixed wire safely; recreate the source as an editable ESU block or connect it in the vanilla breadboard editor.";
            }
        }

        private IEnumerable<string> ValueSocketConflictIssues(AutomationGraph graph)
        {
            if (graph == null)
                yield break;

            List<AutomationGraphConnection> editableValues = graph.Connections
                .Where(connection =>
                    connection?.Kind == AutomationGraphConnectionKind.Value &&
                    IsGraphNodeApplyWritable(connection.To))
                .ToList();
            foreach (IGrouping<string, AutomationGraphConnection> socket in editableValues
                         .GroupBy(connection =>
                             connection.ToNodeId.ToString(CultureInfo.InvariantCulture) + ":" +
                             connection.SlotKind))
            {
                if (socket.Count() <= 1)
                    continue;

                AutomationGraphConnection first = socket.First();
                yield return ShortText(BlockSentenceTitle(first.To), 38) + " " +
                             ValueSlotLabel(first.To.Kind, first.SlotKind) +
                             " socket has multiple editable value connections; disconnect all but one.";
            }

            foreach (IGrouping<int, AutomationGraphConnection> claim in editableValues
                         .GroupBy(connection => connection.FromNodeId))
            {
                if (claim.Count() <= 1)
                    continue;

                yield return ShortText(BlockSentenceTitle(claim.First().From), 38) +
                             " feeds multiple editable value sockets; keep one socket connection per value block.";
            }
        }

        private IEnumerable<string> NativeNodeReadinessIssues(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (node == null || !IsGraphNodeApplyWritable(node))
                yield break;

            string nodeName = ShortText(BlockSentenceTitle(node), 38);
            if (!TryValidateAutomationLiteral(
                    node.Kind,
                    node.ValueText,
                    out _,
                    out string literalIssue))
            {
                yield return nodeName + " " + literalIssue + ".";
            }

            foreach (string issue in ImportedNativeInputConflictIssues(
                         graph,
                         node,
                         nodeName))
            {
                yield return issue;
            }

            if (node.IsStaged)
            {
                foreach (string issue in StagedNodeReadinessIssues(graph, node, nodeName))
                    yield return issue;
                yield break;
            }

            if (node.NativeComponent is GenericBlockGetter getter)
            {
                if (IsEsuOwnedNativeNode(node))
                {
                    if (!TryGetBoundTargetBlock(node, out _))
                        yield return nodeName + " has no linked input target; click its target slot or link an input block.";
                    if (!StagedNodeHasResolvedProperty(node))
                        yield return nodeName + " has no readable native property selected.";
                }
                else
                {
                    if (!HasNativeBlockTarget(getter.BlockTypeName.Us))
                        yield return nodeName + " has no linked input target; click its target slot or link an input block.";
                    if (!GetterHasNativeProperty(getter))
                        yield return nodeName + " has no readable native property selected.";
                }

                yield break;
            }

            if (node.NativeComponent is GenericBlockSetter setter)
            {
                bool hasIncomingSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedValue = GraphValueNode(
                    graph,
                    node,
                    AutomationValueSlotKind.Pass) != null;
                if (IsEsuOwnedNativeNode(node))
                {
                    if (!TryGetBoundTargetBlock(node, out _))
                        yield return nodeName + " has no linked output target; click its target slot or link an output block.";
                    if (!StagedNodeHasResolvedProperty(node))
                        yield return nodeName + " has no writable native property selected.";
                }
                else
                {
                    if (!HasNativeBlockTarget(setter.BlockTypeName.Us))
                        yield return nodeName + " has no linked output target; click its target slot or link an output block.";
                    if (!SetterHasNativeProperty(setter))
                        yield return nodeName + " has no writable native property selected.";
                }

                if (!hasIncomingSignal && !hasSnappedValue)
                    yield return nodeName + " has no incoming signal. Snap a value block into the value socket or place a readable block directly above this setter.";
                if (hasIncomingSignal && hasSnappedValue)
                    yield return nodeName + " has both a stack signal and a value socket signal; remove one input path for unambiguous lowering.";
                yield break;
            }

            if (node.NativeComponent is NativeSwitch)
            {
                if (PreviousFlowNode(graph, node) == null)
                    yield return nodeName + " has no incoming signal to compare; place a read/value-producing block above it.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.Pass) == null)
                    yield return nodeName + " has no then value in the switch pass socket.";
                if (!HasGraphBodyChildren(graph, node) && NextFlowNode(graph, node) == null)
                    yield return nodeName + " switch output does not drive any action block yet; place a Set block in its body or below it.";
                yield break;
            }

            if (node.NativeComponent is LogicGate &&
                IsLogicGateKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first boolean input; place a read/value-producing block above it or snap/wire one into its " + LogicFirstInputLabel(node.Kind) + " socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and a " + LogicFirstInputLabel(node.Kind) + " socket input; remove one input path for clearer lowering.";
                if (node.Kind != AutomationNodeKind.LogicNot &&
                    GraphValueNode(graph, node, AutomationValueSlotKind.LogicB) == null)
                {
                    yield return nodeName + " has no second boolean input in the b socket.";
                }
                yield break;
            }

            if (node.NativeComponent is FuzzyThreshold &&
                IsFuzzyThresholdKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no signal to compare; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is MaxMin &&
                IsMaxMinKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first numeric input; place a read/value-producing block above it or snap/wire one into its a socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an A socket input; remove one input path for clearer lowering.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.MathB) == null)
                    yield return nodeName + " has no second numeric input in the b socket.";
                yield break;
            }

            if (node.NativeComponent is Evaluator &&
                IsMathEvaluatorKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Clamp)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Delay &&
                node.Kind == AutomationNodeKind.Smooth)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.Forever &&
                !HasGraphBodyChildren(graph, node))
            {
                yield return nodeName + " has no action blocks inside its body.";
            }
        }

        private static IEnumerable<string> ImportedNativeInputConflictIssues(
            AutomationGraph graph,
            AutomationGraphNode node,
            string nodeName)
        {
            if (graph == null || node == null)
                yield break;

            List<AutomationGraphConnection> importedInputs = ImportedNativeConnections(graph)
                .Where(connection =>
                    connection != null &&
                    ReferenceEquals(connection.To, node) &&
                    (connection.Kind == AutomationGraphConnectionKind.Stack ||
                     connection.Kind == AutomationGraphConnectionKind.Value))
                .ToList();
            if (importedInputs.Count == 0)
                yield break;

            List<AutomationGraphConnection> editableInputs = graph.Connections
                .Where(connection =>
                    connection != null &&
                    ReferenceEquals(connection.To, node) &&
                    (connection.Kind == AutomationGraphConnectionKind.Stack ||
                     connection.Kind == AutomationGraphConnectionKind.Value))
                .ToList();
            foreach (AutomationGraphConnection imported in importedInputs)
            {
                int importedInput = NativeInputIdentity(node.Kind, imported);
                foreach (AutomationGraphConnection editable in editableInputs)
                {
                    if (ReferenceEquals(imported.From, editable.From) ||
                        imported.FromNodeId == editable.FromNodeId ||
                        importedInput != NativeInputIdentity(node.Kind, editable))
                    {
                        continue;
                    }

                    yield return nodeName + " " +
                                 ValueSlotLabel(node.Kind, editable.SlotKind) +
                                 " input is already owned by a read-only imported vanilla wire; remove that wire in the native breadboard editor before replacing it.";
                }
            }
        }

        private static int NativeInputIdentity(
            AutomationNodeKind hostKind,
            AutomationGraphConnection connection)
        {
            if (connection == null)
                return int.MinValue;

            return NativeInputIdentity(
                hostKind,
                connection.Kind,
                connection.SlotKind);
        }

        private static int NativeInputIdentity(
            AutomationNodeKind hostKind,
            AutomationGraphConnectionKind connectionKind,
            AutomationValueSlotKind slotKind)
        {

            if (connectionKind == AutomationGraphConnectionKind.Stack)
            {
                return hostKind == AutomationNodeKind.IfCondition ||
                       hostKind == AutomationNodeKind.IfLessThan
                    ? 100
                    : 0;
            }

            switch (slotKind)
            {
                case AutomationValueSlotKind.Pass:
                    return 0;
                case AutomationValueSlotKind.LogicB:
                case AutomationValueSlotKind.MathB:
                    return 1;
                default:
                    return 200 + (int)slotKind;
            }
        }

        private static bool ImportedNativeInputOccupied(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationGraphConnectionKind connectionKind,
            AutomationValueSlotKind slotKind)
        {
            if (graph == null || host == null)
                return false;

            int inputIdentity = NativeInputIdentity(
                host.Kind,
                connectionKind,
                slotKind);
            return ImportedNativeConnections(graph).Any(connection =>
                connection != null &&
                ReferenceEquals(connection.To, host) &&
                NativeInputIdentity(host.Kind, connection) == inputIdentity);
        }

        private IEnumerable<string> StagedNodeReadinessIssues(
            AutomationGraph graph,
            AutomationGraphNode node,
            string nodeName)
        {
            if (!CanLowerStagedNodeKind(node.Kind))
            {
                yield return nodeName + " cannot be lowered into a supported native breadboard component yet.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.InputGetter)
            {
                if (!TryGetBoundTargetBlock(node, out _))
                    yield return nodeName + " has no staged input target binding.";
                if (!StagedNodeHasResolvedProperty(node))
                    yield return nodeName + " has no readable native property selected.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.OutputSetter)
            {
                bool hasIncomingSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedValue = GraphValueNode(
                    graph,
                    node,
                    AutomationValueSlotKind.Pass) != null;
                if (!TryGetBoundTargetBlock(node, out _))
                    yield return nodeName + " has no staged output target binding.";
                if (!StagedNodeHasResolvedProperty(node))
                    yield return nodeName + " has no writable native property selected.";
                if (!hasIncomingSignal && !hasSnappedValue)
                    yield return nodeName + " has no incoming signal. Snap a value block into the value socket or place a readable block directly above this setter.";
                if (hasIncomingSignal && hasSnappedValue)
                    yield return nodeName + " has both a stack signal and a value socket signal; remove one input path for unambiguous lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.IfCondition ||
                node.Kind == AutomationNodeKind.IfLessThan)
            {
                if (PreviousFlowNode(graph, node) == null)
                    yield return nodeName + " has no incoming signal to compare; place a read/value-producing block above it.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.Pass) == null)
                    yield return nodeName + " has no then value in the switch pass socket.";
                if (!HasGraphBodyChildren(graph, node) && NextFlowNode(graph, node) == null)
                    yield return nodeName + " switch output does not drive any action block yet; place a Set block in its body or below it.";
                yield break;
            }

            if (IsLogicGateKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first boolean input; place a read/value-producing block above it or snap/wire one into its " + LogicFirstInputLabel(node.Kind) + " socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and a " + LogicFirstInputLabel(node.Kind) + " socket input; remove one input path for clearer lowering.";
                if (node.Kind != AutomationNodeKind.LogicNot &&
                    GraphValueNode(graph, node, AutomationValueSlotKind.LogicB) == null)
                {
                    yield return nodeName + " has no second boolean input in the b socket.";
                }
                yield break;
            }

            if (IsFuzzyThresholdKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no signal to compare; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (IsMaxMinKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first numeric input; place a read/value-producing block above it or snap/wire one into its a socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an A socket input; remove one input path for clearer lowering.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.MathB) == null)
                    yield return nodeName + " has no second numeric input in the b socket.";
                yield break;
            }

            if (IsMathEvaluatorKind(node.Kind) ||
                node.Kind == AutomationNodeKind.Clamp ||
                node.Kind == AutomationNodeKind.Smooth)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.Forever &&
                !HasGraphBodyChildren(graph, node))
            {
                yield return nodeName + " has no action blocks inside its body.";
            }
        }

        private static bool HasNativeBlockTarget(string blockTypeName)
        {
            return !string.IsNullOrWhiteSpace(blockTypeName);
        }

        private static bool GetterHasNativeProperty(GenericBlockGetter getter)
        {
            if (getter == null)
                return false;

            Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
            if (getter.ReadableAttributeId.Us != NativeUnselectedCode)
                return TryGetReadableLabel(blockType, getter.ReadableAttributeId.Us, out _);

            return getter.BlockPropertyId.Us != NativeUnselectedCode &&
                   TryGetVariableLabel(blockType, editableOnly: false, getter.BlockSetId.Us, getter.BlockPropertyId.Us, out _);
        }

        private static bool SetterHasNativeProperty(GenericBlockSetter setter)
        {
            if (setter == null)
                return false;

            Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
            return setter.BlockPropertyId.Us != NativeUnselectedCode &&
                   TryGetVariableLabel(blockType, editableOnly: true, setter.BlockSetId.Us, setter.BlockPropertyId.Us, out _);
        }

        private static AutomationGraphNode PreviousFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null)
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Stack)
                .Concat(ImportedNativeConnections(graph).Where(connection =>
                    connection?.Kind == AutomationGraphConnectionKind.Stack))
                .FirstOrDefault(connection => ReferenceEquals(connection.To, node))
                ?.From;
        }

        private static AutomationGraphNode NextFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null)
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Stack)
                .Concat(ImportedNativeConnections(graph).Where(connection =>
                    connection?.Kind == AutomationGraphConnectionKind.Stack))
                .FirstOrDefault(connection => ReferenceEquals(connection.From, node))
                ?.To;
        }

        private static AutomationGraphNode GraphValueNode(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind = AutomationValueSlotKind.Pass)
        {
            if (IsStackFedPrimaryValueSlot(graph, host, slotKind))
                return null;

            return GraphValueConnection(graph, host, slotKind)?.From;
        }

        private static bool HasGraphBodyChildren(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            if (graph == null || host == null)
                return false;

            return GraphConnections(graph, AutomationGraphConnectionKind.Body)
                .Any(connection => ReferenceEquals(connection.From, host));
        }

        private static AutomationGraphNode PreviousFlowNode(
            IReadOnlyList<List<AutomationGraphNode>> flowChains,
            AutomationGraphNode node)
        {
            if (flowChains == null || node == null)
                return null;

            foreach (List<AutomationGraphNode> flow in flowChains)
            {
                int index = FlowNodeIndex(flow, node);
                if (index > 0)
                    return flow[index - 1];
            }

            return null;
        }

        private static AutomationGraphNode NextFlowNode(
            IReadOnlyList<List<AutomationGraphNode>> flowChains,
            AutomationGraphNode node)
        {
            if (flowChains == null || node == null)
                return null;

            foreach (List<AutomationGraphNode> flow in flowChains)
            {
                int index = FlowNodeIndex(flow, node);
                if (index >= 0 && index < flow.Count - 1)
                    return flow[index + 1];
            }

            return null;
        }

        private static int FlowNodeIndex(
            IReadOnlyList<AutomationGraphNode> flow,
            AutomationGraphNode node)
        {
            if (flow == null || node == null)
                return -1;

            for (int index = 0; index < flow.Count; index++)
            {
                if (ReferenceEquals(flow[index], node))
                    return index;
            }

            return -1;
        }

        private void NotifyNativeApply(
            string message,
            EsuHudNotificationKind kind,
            string detail = null)
        {
            EsuHudNotifications.ShowSystem(
                "Automation Builder",
                message,
                kind,
                detail);
        }

        private List<AutomationLink> BuildNativeLinks(NativeBreadBoard breadboard)
        {
            return BuildNativeLinks(SnapshotFor(breadboard));
        }

        private List<AutomationLink> BuildNativeLinks(NativeBreadboardSnapshot snapshot)
        {
            var links = new List<AutomationLink>();
            if (snapshot?.Breadboard == null || _selectedBreadboard == null)
                return links;

            foreach (CircuitComponent component in snapshot.Components)
            {
                if (IsPendingNativeRemoval(component))
                    continue;

                if (component is GenericBlockGetter getter)
                {
                    AutomationBlockRef target = ResolveNativeTargetBlock(snapshot, getter, getter.BlockTypeName.Us, getter.BlockFilter.Us, out string status);
                    if (target == null)
                    {
                        MaybeLogUnresolvedNativeLink(component, "input/getter", getter.BlockTypeName.Us, getter.BlockFilter.Us);
                        links.Add(CreateUnresolvedNativeLink(
                            component,
                            AutomationLinkKind.InputToBreadboard,
                            NativePropertyLabel(getter),
                            getter.BlockTypeName.Us,
                            getter.BlockFilter.Us,
                            status));
                        continue;
                    }

                    links.Add(new AutomationLink(
                        unchecked((int)component.UniqueId),
                        target,
                        _selectedBreadboard,
                        AutomationLinkKind.InputToBreadboard,
                        NativePropertyLabel(getter),
                        NativeComponentColor(component, AutomationNodeKind.InputGetter),
                        component,
                        status));
                }
                else if (component is GenericBlockSetter setter)
                {
                    AutomationBlockRef target = ResolveNativeTargetBlock(snapshot, setter, setter.BlockTypeName.Us, setter.BlockFilter.Us, out string status);
                    if (target == null)
                    {
                        MaybeLogUnresolvedNativeLink(component, "output/setter", setter.BlockTypeName.Us, setter.BlockFilter.Us);
                        links.Add(CreateUnresolvedNativeLink(
                            component,
                            AutomationLinkKind.BreadboardToOutput,
                            NativePropertyLabel(setter),
                            setter.BlockTypeName.Us,
                            setter.BlockFilter.Us,
                            status));
                        continue;
                    }

                    links.Add(new AutomationLink(
                        unchecked((int)component.UniqueId),
                        _selectedBreadboard,
                        target,
                        AutomationLinkKind.BreadboardToOutput,
                        NativePropertyLabel(setter),
                        NativeComponentColor(component, AutomationNodeKind.OutputSetter),
                        component,
                        status));
                }
            }

            return links;
        }

        private AutomationLink CreateUnresolvedNativeLink(
            CircuitComponent component,
            AutomationLinkKind kind,
            string property,
            string blockTypeName,
            string filter,
            string status)
        {
            string targetLabel = UnresolvedNativeTargetLabel(blockTypeName, filter);
            string stableKey =
                "native-unresolved:" +
                (component?.UniqueId.ToString(CultureInfo.InvariantCulture) ?? "unknown") +
                ":" +
                kind.ToString();
            AutomationBlockRef unresolved = AutomationBlockRef.Unresolved(targetLabel, stableKey);
            return new AutomationLink(
                unchecked((int)(component?.UniqueId ?? 0u)),
                kind == AutomationLinkKind.InputToBreadboard ? unresolved : _selectedBreadboard,
                kind == AutomationLinkKind.InputToBreadboard ? _selectedBreadboard : unresolved,
                kind,
                property,
                new Color(1f, 0.48f, 0.12f, 1f),
                component,
                UnresolvedNativeStatus(status));
        }

        private static string UnresolvedNativeTargetLabel(
            string blockTypeName,
            string filter)
        {
            string label = HumanizeTypeName(blockTypeName);
            string suffix = GeneratedAutomationBlockSuffix(filter);
            if (!string.IsNullOrWhiteSpace(suffix))
                label += " " + suffix;
            return label + " (unresolved)";
        }

        private static string UnresolvedNativeStatus(string status)
        {
            return string.IsNullOrWhiteSpace(status) ||
                   status.IndexOf("unresolved", StringComparison.OrdinalIgnoreCase) < 0
                ? "native unresolved"
                : status.Trim();
        }

        private void MaybeLogUnresolvedNativeLink(
            CircuitComponent component,
            string direction,
            string blockTypeName,
            string filter)
        {
            if (!SerializationHudProfile.DeveloperModeEnabled)
                return;

            float now = Time.unscaledTime;
            if (now < _nextNativeDiagnosticsLogTime)
                return;

            _nextNativeDiagnosticsLogTime = now + NativeDiagnosticsLogCooldownSeconds;
            EsuRuntimeLog.Warning(
                "Automation Builder",
                "Native automation " + direction + " link could not resolve its target block.",
                "component=" + (component?.UniqueId.ToString(CultureInfo.InvariantCulture) ?? "unknown") +
                "\ntype=" + (blockTypeName ?? string.Empty) +
                "\nfilter=" + (filter ?? string.Empty));
        }

        private AutomationGraphNode NativeComponentToNode(
            AutomationBlockRef breadboardRef,
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            return NativeComponentToNode(breadboardRef, SnapshotFor(breadboard), component);
        }

        private AutomationGraphNode NativeComponentToNode(
            AutomationBlockRef breadboardRef,
            NativeBreadboardSnapshot snapshot,
            CircuitComponent component)
        {
            NativeOwnerRecord owner = snapshot?.OwnerRecords
                .FirstOrDefault(record => record?.Target != null &&
                                          record.ComponentId == component.UniqueId);
            AutomationNodeKind kind = owner?.Kind ?? NativeKind(component);
            Rect rect = NativeComponentRect(component, kind);
            var node = new AutomationGraphNode(
                NativeGraphNodeId(component),
                kind,
                rect,
                kind == AutomationNodeKind.Forever ? "Forever" : NativeNodeLabel(component),
                kind == AutomationNodeKind.Forever ? "body" : NativeNodeProperty(component),
                kind == AutomationNodeKind.Forever && component is NativeComment foreverComment
                    ? NativeForeverDisplayText(foreverComment.InputValue.Us)
                    : NativeNodeValue(component),
                component);
            if (component is GenericBlockGetter getter)
            {
                AutomationBlockRef target = ResolveNativeTargetBlock(
                    snapshot,
                    getter,
                    getter.BlockTypeName.Us,
                    getter.BlockFilter.Us,
                    out _);
                if (target != null)
                    node.BindTarget(new AutomationLink(0, target, breadboardRef, AutomationLinkKind.InputToBreadboard, NativePropertyLabel(getter), Color.white, component));
            }
            else if (component is GenericBlockSetter setter)
            {
                AutomationBlockRef target = ResolveNativeTargetBlock(
                    snapshot,
                    setter,
                    setter.BlockTypeName.Us,
                    setter.BlockFilter.Us,
                    out _);
                if (target != null)
                    node.BindTarget(new AutomationLink(0, breadboardRef, target, AutomationLinkKind.BreadboardToOutput, NativePropertyLabel(setter), Color.white, component));
            }

            return node;
        }

        private CircuitComponent CreateLooseNativeComponent(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return new GenericBlockGetter();
                case AutomationNodeKind.OutputSetter:
                    return new GenericBlockSetter();
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                    return CreateNativeSwitch(DefaultValue(kind));
                case AutomationNodeKind.Constant:
                    return CreateNativeConstant(_constantText);
                case AutomationNodeKind.Random:
                    return CreateNativeRandom("0..1");
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return CreateNativeLogicGate(kind);
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return CreateNativeFuzzyThreshold(kind, DefaultValue(kind));
                case AutomationNodeKind.MathAdd:
                    return CreateNativeEvaluator("a + 0");
                case AutomationNodeKind.MathSubtract:
                    return CreateNativeEvaluator("a - 0");
                case AutomationNodeKind.MathMultiply:
                    return CreateNativeEvaluator("a * 1");
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return CreateNativeMaxMin(kind);
                case AutomationNodeKind.Clamp:
                    return CreateNativeClamp("0..100");
                case AutomationNodeKind.Smooth:
                    return CreateNativeDelay("0.25s");
                case AutomationNodeKind.Forever:
                    return CreateNativeComment(NativeForeverMarkerText("native breadboard evaluates continuously"));
                case AutomationNodeKind.Comment:
                    return CreateNativeComment("Note");
                default:
                    return null;
            }
        }

        private static CircuitComponent CreateNativeEvaluator(string expression)
        {
            var evaluator = new Evaluator();
            evaluator.Expression.Us = expression;
            return evaluator;
        }

        private static CircuitComponent CreateNativeSwitch(string value)
        {
            var switchComponent = new NativeSwitch();
            ParseIfSwitchValue(value, 10f, 0f, out float threshold, out float failValue);
            switchComponent.Threshold.Us = threshold;
            switchComponent.FailValue.Us = failValue;
            return switchComponent;
        }

        private static CircuitComponent CreateNativeConstant(string value)
        {
            var constant = new ConstantInput();
            constant.Type.Us = ConstantInput.ConstantType.ItsAFloat;
            constant.InputValue.Us = ParseFloat(value, 0f);
            return constant;
        }

        private static CircuitComponent CreateNativeRandom(string range)
        {
            var random = new RandomInput();
            ParseRange(range, 0f, 1f, out float lower, out float upper);
            random.RandomLimits.Lower = lower;
            random.RandomLimits.Upper = upper;
            return random;
        }

        private static CircuitComponent CreateNativeLogicGate(AutomationNodeKind kind)
        {
            var logicGate = new LogicGate();
            logicGate.SelectedGate.Us = LogicGateType(kind);
            logicGate.TrueLogic.Us = TrueType.GreaterZero;
            return logicGate;
        }

        private static CircuitComponent CreateNativeFuzzyThreshold(
            AutomationNodeKind kind,
            string value)
        {
            var fuzzyThreshold = new FuzzyThreshold();
            ApplyFuzzyThresholdEdits(fuzzyThreshold, kind, value);
            return fuzzyThreshold;
        }

        private static void ApplyFuzzyThresholdEdits(
            FuzzyThreshold fuzzyThreshold,
            AutomationNodeKind kind,
            string value)
        {
            if (fuzzyThreshold == null)
                return;

            float threshold = ParseThresholdValue(value, 10f);
            fuzzyThreshold.Above.Us = kind != AutomationNodeKind.CompareBelowThreshold;
            fuzzyThreshold.ThresholdLimits.Us = new Vector2(threshold, threshold);
        }

        private static float ParseThresholdValue(
            string value,
            float fallback)
        {
            return TryParseLabeledAutomationFloat(
                    value,
                    "threshold",
                    out float threshold)
                ? threshold
                : fallback;
        }

        private static float FuzzyThresholdValue(FuzzyThreshold fuzzyThreshold)
        {
            if (fuzzyThreshold == null)
                return 10f;

            return (fuzzyThreshold.ThresholdLimits.Lower + fuzzyThreshold.ThresholdLimits.Upper) * 0.5f;
        }

        private static CircuitComponent CreateNativeMaxMin(AutomationNodeKind kind)
        {
            var maxMin = new MaxMin();
            maxMin.SelectedOp.Us = MaxMinOpType(kind);
            return maxMin;
        }

        private static CircuitComponent CreateNativeClamp(string range)
        {
            var clamp = new Clamp();
            ParseRange(range, 0f, 100f, out float lower, out float upper);
            clamp.MinMax.Lower = lower;
            clamp.MinMax.Upper = upper;
            return clamp;
        }

        private static CircuitComponent CreateNativeDelay(string seconds)
        {
            var delay = new Delay();
            delay.DelayTime.Us = ParseSeconds(seconds, 0.25f);
            return delay;
        }

        private static CircuitComponent CreateNativeComment(string text)
        {
            var comment = new NativeComment();
            comment.InputValue.Us = text;
            return comment;
        }

        private void AddNativeOwnerMarker(
            NativeBreadBoard breadboard,
            CircuitComponent component,
            AutomationNodeKind kind)
        {
            if (breadboard == null ||
                component == null ||
                OwnerRecordsFor(breadboard).Any(record => record.ComponentId == component.UniqueId))
            {
                return;
            }

            var marker = (NativeComment)CreateNativeComment(NativeOwnerMarkerText(component, kind));
            marker.ClipText.Us = true;
            marker.ScaleWithZoom.Us = false;
            marker.OutlineColor.Us = new Color(0f, 0f, 0f, 0f);
            ApplyNativeNodeRect(marker, NativeOwnerMarkerRect(component));
            new AddComponentCommand(breadboard, marker).Execute();
        }

        private static Rect NativeOwnerMarkerRect(CircuitComponent component)
        {
            float x = component?.X.Us ?? 0f;
            float y = component?.Y.Us ?? 0f;
            return new Rect(x - 0.5f, y - 0.5f, 1f, 1f);
        }

        private static string NativeOwnerMarkerText(
            CircuitComponent component,
            AutomationNodeKind kind)
        {
            return NativeOwnerMarkerText(component?.UniqueId ?? 0u, kind);
        }

        private static string NativeOwnerMarkerText(
            uint componentId,
            AutomationNodeKind kind) =>
            NativeOwnerMarkerPrefix +
            "v1|component=" +
            componentId.ToString(CultureInfo.InvariantCulture) +
            "|kind=" +
            kind;

        internal static string RewriteNativeOwnerMarkerComponentId(
            string markerText,
            uint componentId)
        {
            return TryParseNativeOwnerMarkerText(markerText, out _, out AutomationNodeKind kind)
                ? NativeOwnerMarkerText(componentId, kind)
                : markerText;
        }

        private static bool IsNativeOwnerMarker(CircuitComponent component) =>
            TryParseNativeOwnerMarker(component, out _, out _);

        private static IEnumerable<NativeOwnerRecord> NativeOwnerRecords(NativeBreadBoard breadboard)
        {
            if (breadboard == null)
                yield break;

            foreach (NativeOwnerRecord record in BuildNativeOwnerRecords(breadboard.Components.ToList()))
                yield return record;
        }

        private static List<NativeOwnerRecord> BuildNativeOwnerRecords(
            IReadOnlyList<CircuitComponent> components)
        {
            var records = new List<NativeOwnerRecord>();
            if (components == null || components.Count == 0)
                return records;

            Dictionary<uint, CircuitComponent> componentsById = components
                .Where(component => component != null)
                .GroupBy(component => component.UniqueId)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (CircuitComponent marker in components.Where(IsNativeOwnerMarker))
            {
                if (!TryParseNativeOwnerMarker(marker, out uint componentId, out AutomationNodeKind kind))
                    continue;

                componentsById.TryGetValue(componentId, out CircuitComponent target);
                if (ReferenceEquals(target, marker))
                    target = null;

                records.Add(new NativeOwnerRecord(componentId, kind, target, marker));
            }

            return records;
        }

        private static int RemoveNativeOwnerMarkersForComponent(
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            if (breadboard == null || component == null)
                return 0;

            int removed = 0;
            foreach (NativeOwnerRecord record in NativeOwnerRecords(breadboard)
                         .Where(record => record.ComponentId == component.UniqueId)
                         .ToList())
            {
                if (RemoveNativeComponentPackage(breadboard, record.Marker))
                    removed++;
            }

            return removed;
        }

        private static bool TryParseNativeOwnerMarker(
            CircuitComponent marker,
            out uint componentId,
            out AutomationNodeKind kind)
        {
            componentId = 0u;
            kind = AutomationNodeKind.Comment;
            if (!(marker is NativeComment comment))
                return false;

            string text = comment.InputValue.Us ?? string.Empty;
            if (!TryParseNativeOwnerMarkerText(text, out componentId, out kind))
                return false;

            try
            {
                return comment.ClipText.Us &&
                       !comment.ScaleWithZoom.Us &&
                       comment.OutlineColor.Us.a <= 0.001f;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryParseNativeOwnerMarkerText(
            string text,
            out uint componentId,
            out AutomationNodeKind kind)
        {
            componentId = 0u;
            kind = AutomationNodeKind.Comment;
            string value = text ?? string.Empty;
            if (!value.StartsWith(NativeOwnerMarkerPrefix, StringComparison.Ordinal))
                return false;

            string[] parts = value.Substring(NativeOwnerMarkerPrefix.Length)
                .Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length != 3 ||
                !string.Equals(parts[0], "v1", StringComparison.Ordinal) ||
                !parts[1].StartsWith("component=", StringComparison.Ordinal) ||
                !parts[2].StartsWith("kind=", StringComparison.Ordinal))
            {
                return false;
            }

            bool componentParsed = uint.TryParse(
                parts[1].Substring("component=".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out componentId);
            bool kindParsed = Enum.TryParse(
                parts[2].Substring("kind=".Length),
                ignoreCase: false,
                out kind);
            if (!componentParsed ||
                !kindParsed ||
                !CanLowerStagedNodeKind(kind))
            {
                return false;
            }

            return true;
        }

        private bool TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
        {
            return TryResolveNativeBreadboard(
                _selectedBreadboard,
                out breadboard,
                out _);
        }

        private bool TryResolveNativeBreadboard(
            AutomationBlockRef breadboardRef,
            out NativeBreadBoard breadboard,
            out string message)
        {
            breadboard = null;
            message = null;
            if (breadboardRef == null)
            {
                message = "Select a breadboard before using native automation.";
                return false;
            }

            if (!breadboardRef.IsBreadboard)
            {
                message = "The selected block is not a breadboard.";
                return false;
            }

            if (!breadboardRef.TryGetBlock(out Block block))
            {
                message = "The selected breadboard block is no longer available.";
                return false;
            }

            if (!TryResolveNativeBreadboardBoard(block, out breadboard))
            {
                message = breadboardRef.Name + " does not expose an editable native breadboard board.";
                return false;
            }

            return true;
        }

        private static bool TryResolveNativeBreadboardBoard(
            Block block,
            out NativeBreadBoard breadboard)
        {
            breadboard = null;
            if (block == null || block.IsDeleted)
                return false;

            if (block is NativeBasicBreadBoardBlock basicBreadboard)
            {
                breadboard = basicBreadboard.Board;
                return breadboard != null;
            }

            if (block is NativeAiBreadBoardBlock aiBreadboard)
            {
                breadboard = aiBreadboard.Board;
                return breadboard != null;
            }

            try
            {
                PropertyInfo boardProperty = block.GetType().GetProperty(
                    "Board",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (boardProperty?.GetValue(block, null) is NativeBreadBoard reflectedBreadboard)
                {
                    breadboard = reflectedBreadboard;
                    return true;
                }
            }
            catch
            {
                // Unknown block types are treated as non-editable breadboards.
            }

            return false;
        }

        private static void ConfigureGetterTarget(
            GenericBlockGetter getter,
            Block target,
            string filter)
        {
            getter.BlockType = target.GetType();
            getter.BlockTypeName.Us = target.GetType().Name;
            getter.BlockFilter.Us = filter ?? string.Empty;
            getter.PotentiallyAffectedBlocks.Clear();
            getter.PotentiallyAffectedBlocks.Add(target);
        }

        private static void ConfigureSetterTarget(
            GenericBlockSetter setter,
            Block target,
            string filter)
        {
            setter.BlockType = target.GetType();
            setter.BlockTypeName.Us = target.GetType().Name;
            setter.BlockFilter.Us = filter ?? string.Empty;
            setter.PotentiallyAffectedBlocks.Clear();
            setter.PotentiallyAffectedBlocks.Add(new BlockStub(target));
        }

        private string EnsureExactBlockFilterName(
            NativeBreadBoard breadboard,
            Block target,
            NativeBuildContext context)
        {
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            string existing = CurrentCustomName(target);
            if (!string.IsNullOrWhiteSpace(existing) &&
                IsSafeTextOnlyBlockFilterName(existing) &&
                !IsLegacyAutoGeneratedBlockName(existing) &&
                string.Equals(CurrentCustomName(target), existing, StringComparison.Ordinal) &&
                IsUniqueBlockName(snapshot, target, existing))
            {
                return existing;
            }

            string generated = GenerateStableBlockName(target);
            int suffix = 0;
            string candidate = generated;
            while (!IsUniqueBlockName(snapshot, target, candidate))
                candidate = ComposeAutoBlockName(generated, AlphabeticSuffix(suffix++));

            string previousName = string.IsNullOrWhiteSpace(existing)
                ? AutomationBreadboardCatalog.BlockName(target)
                : existing;
            try
            {
                target.IdSet.Name.Us = candidate;
            }
            catch (Exception exception)
            {
                context.Warn("Could not auto-name linked block for exact vanilla filtering: " + exception.Message);
                return existing ?? string.Empty;
            }

            string stored = CurrentCustomName(target);
            if (string.IsNullOrWhiteSpace(stored))
            {
                context.Warn("Could not auto-name linked block for exact vanilla filtering: the stored block name was blank after assignment.");
                return existing ?? string.Empty;
            }

            if (!string.Equals(stored, candidate, StringComparison.Ordinal))
            {
                context.Warn("Auto-name was normalized by vanilla from " + candidate + " to " + stored + "; using stored name for exact filtering.");
            }

            _nativeApplyNameChanges.Add(
                new NativeBlockNameChange(target, existing, stored));

            context.Warn("Auto-named linked block '" + previousName + "' as " + stored + " for exact vanilla filtering.");
            return stored;
        }

        private static string CurrentCustomName(Block block)
        {
            try
            {
                string name = block?.IdSet?.Name;
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool RestoreNativeApplyBlockNames()
        {
            bool restored = true;
            foreach (NativeBlockNameChange change in _nativeApplyNameChanges.AsEnumerable().Reverse())
                restored &= change?.RestoreIfUnchanged() != false;
            _nativeApplyNameChanges.Clear();
            return restored;
        }

        private static string GenerateStableBlockName(Block target)
        {
            Vector3i cell = target.LocalPosition;
            string hashInput =
                (target.GetType().FullName ?? target.GetType().Name) + "|" +
                ConstructIdentityHash(target).ToString(CultureInfo.InvariantCulture) + "|" +
                cell.x.ToString(CultureInfo.InvariantCulture) + "|" +
                cell.y.ToString(CultureInfo.InvariantCulture) + "|" +
                cell.z.ToString(CultureInfo.InvariantCulture);
            return ComposeAutoBlockName(
                LettersOnly(target.GetType().Name),
                AlphabeticToken(StableNameHash(hashInput), AutoNameTokenLength));
        }

        private static string ComposeAutoBlockName(
            string baseText,
            string suffix)
        {
            string safeBase = LettersOnly(baseText);
            string safeSuffix = LettersOnly(suffix);
            if (safeBase.StartsWith(AutoNamePrefix, StringComparison.Ordinal))
                safeBase = safeBase.Substring(AutoNamePrefix.Length);
            int maxBaseLength = Math.Max(1, AutoNameMaxLength - AutoNamePrefix.Length - safeSuffix.Length);
            if (safeBase.Length > maxBaseLength)
                safeBase = safeBase.Substring(0, maxBaseLength);
            return AutoNamePrefix + safeBase + safeSuffix;
        }

        private static int ConstructIdentityHash(Block target)
        {
            try
            {
                object construct = target?.GetConstructableOrSubConstructable();
                return construct == null ? 0 : RuntimeHelpers.GetHashCode(construct);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsSafeTextOnlyBlockFilterName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Trim().All(character =>
                       character >= 'A' && character <= 'Z' ||
                       character >= 'a' && character <= 'z');
        }

        private static bool IsLegacyAutoGeneratedBlockName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   (name.Trim().StartsWith(LegacyAutoNamePrefix, StringComparison.Ordinal) ||
                    name.Trim().StartsWith(RetiredAutoNamePrefix, StringComparison.Ordinal));
        }

        private static string LettersOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Block";

            string letters = new string(text.Where(character =>
                character >= 'A' && character <= 'Z' ||
                character >= 'a' && character <= 'z').ToArray());
            return string.IsNullOrWhiteSpace(letters) ? "Block" : letters;
        }

        private static uint StableNameHash(string text)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;
            foreach (char character in text ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash == 0u ? offset : hash;
        }

        private static string AlphabeticToken(
            uint value,
            int length)
        {
            int count = Mathf.Max(1, length);
            var chars = new char[count];
            uint state = value == 0u ? 2166136261u : value;
            for (int index = 0; index < count; index++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                chars[index] = (char)('A' + state % 26u);
            }

            return new string(chars);
        }

        private static string AlphabeticSuffix(int index)
        {
            string[] suffixes =
            {
                "Alpha",
                "Beta",
                "Gamma",
                "Delta",
                "Epsilon",
                "Zeta",
                "Eta",
                "Theta",
                "Iota",
                "Kappa",
                "Lambda",
                "Mu",
                "Nu",
                "Xi",
                "Omicron",
                "Pi",
                "Rho",
                "Sigma",
                "Tau",
                "Upsilon",
                "Phi",
                "Chi",
                "Psi",
                "Omega"
            };

            if (index >= 0 && index < suffixes.Length)
                return suffixes[index];

            return AlphabeticToken((uint)Mathf.Max(0, index), 6);
        }

        private static bool IsUniqueBlockName(
            NativeBreadBoard breadboard,
            Block target,
            string name)
        {
            return IsUniqueBlockName(
                NativeBreadboardSnapshot.Create(breadboard),
                target,
                name);
        }

        private static bool IsUniqueBlockName(
            NativeBreadboardSnapshot snapshot,
            Block target,
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (snapshot == null || target == null)
                return false;

            snapshot.BlocksByTypeName.TryGetValue(target.GetType().Name, out List<Block> blocks);
            int matches = (blocks ?? new List<Block>())
                .Count(block =>
                    block != null &&
                    !block.IsDeleted &&
                    !ReferenceEquals(block, target) &&
                    string.Equals(CurrentCustomName(block), name, StringComparison.Ordinal));
            return matches == 0;
        }

        private static IEnumerable<Block> EnumerateConstructBlocks(NativeBreadBoard breadboard)
        {
            if (breadboard?.Construct == null)
                yield break;

            foreach (Block block in breadboard.Construct.AllBasics.AliveAndDead.Blocks)
                yield return block;
            foreach (SubConstruct subConstruct in breadboard.Construct.AllBasics.AllSubconstructsBelowUs)
            {
                foreach (Block block in subConstruct.AllBasics.AliveAndDead.Blocks)
                    yield return block;
            }
        }

        private NativeBreadboardSnapshot SnapshotFor(NativeBreadBoard breadboard)
        {
            if (breadboard == null)
                return null;

            return _nativeSnapshot != null &&
                   ReferenceEquals(_nativeSnapshot.Breadboard, breadboard)
                ? _nativeSnapshot
                : NativeBreadboardSnapshot.Create(breadboard);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockGetter getter,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), getter, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
            GenericBlockGetter getter,
            string blockTypeName,
            string filter,
            out string status)
        {
            Block live = getter.PotentiallyAffectedBlocks.FirstOrDefault(block => block != null && !block.IsDeleted);
            if (live != null)
            {
                status = ExactnessStatus(snapshot, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(snapshot, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockSetter setter,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), setter, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
            GenericBlockSetter setter,
            string blockTypeName,
            string filter,
            out string status)
        {
            Block live = setter.PotentiallyAffectedBlocks
                .Select(stub => stub?.Block)
                .FirstOrDefault(block => block != null && !block.IsDeleted);
            if (live != null)
            {
                status = ExactnessStatus(snapshot, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(snapshot, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
            string blockTypeName,
            string filter,
            out string status)
        {
            List<Block> candidates = (snapshot?.BlocksByTypeName.TryGetValue(blockTypeName ?? string.Empty, out List<Block> indexed) == true
                    ? indexed
                    : new List<Block>())
                .Where(block =>
                    block != null &&
                    !block.IsDeleted &&
                    MatchesNativeFilter(block, filter))
                .ToList();
            status = candidates.Count == 1
                ? "native exact"
                : candidates.Count == 0
                    ? "native unresolved"
                    : "native broad";
            Block target = candidates.FirstOrDefault();
            return target == null ? null : BlockRefFromBlock(target);
        }

        private static bool MatchesNativeFilter(Block block, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string customName = CurrentCustomName(block);
            if (string.Equals(customName, filter, StringComparison.Ordinal))
                return true;

            try
            {
                return (block.IdSet?.Name?.ToString() ?? string.Empty).Contains(filter);
            }
            catch
            {
                return false;
            }
        }

        private static string ExactnessStatus(
            NativeBreadBoard breadboard,
            Block target,
            string filter)
        {
            return ExactnessStatus(
                NativeBreadboardSnapshot.Create(breadboard),
                target,
                filter);
        }

        private static string ExactnessStatus(
            NativeBreadboardSnapshot snapshot,
            Block target,
            string filter)
        {
            return !string.IsNullOrWhiteSpace(filter) &&
                   string.Equals(CurrentCustomName(target), filter, StringComparison.Ordinal) &&
                   IsUniqueBlockName(snapshot, target, filter)
                ? "native exact"
                : "native broad";
        }

        private AutomationBlockRef BlockRefFromBlock(Block block)
        {
            AllConstruct construct = null;
            try
            {
                construct = block.GetConstructableOrSubConstructable() as AllConstruct;
            }
            catch
            {
                construct = null;
            }

            construct = construct ?? _selectedBreadboard?.Construct;
            return new AutomationBlockRef(
                construct,
                block.LocalPosition,
                AutomationBreadboardCatalog.BlockName(block),
                AutomationBreadboardCatalog.IsBreadboardBlock(block));
        }

        private static Rect NativeAppendRect(NativeBreadBoard breadboard)
        {
            int count = breadboard?.Components.Count ?? 0;
            int index = Math.Max(0, count);
            return new Rect(
                80f + (index % 2) * (GraphNodeWidth + 46f),
                48f + index * 128f,
                GraphNodeWidth,
                GraphNodeHeight);
        }

        private static Rect NativeComponentRect(CircuitComponent component)
        {
            return NativeComponentRect(component, NativeKind(component));
        }

        private static Rect NativeComponentRect(
            CircuitComponent component,
            AutomationNodeKind kind)
        {
            bool valueFootprint = NativeComponentUsesValueFootprint(component);
            float minWidth = valueFootprint
                ? GraphValueNodeWidth
                : GraphNodeWidthForKind(kind);
            float minHeight = valueFootprint
                ? BlockShape(kind) == AutomationBlockShape.Value
                    ? GraphNodeHeightForKind(kind)
                    : GraphNodeHeightForKind(AutomationNodeKind.Constant)
                : GraphNodeHeightForKind(kind);
            float width = Mathf.Max(minWidth, component.Width.Us);
            float height = Mathf.Max(minHeight, component.Height.Us);
            return NormalizeGraphNodeRect(
                kind,
                new Rect(
                component.X.Us - width * 0.5f,
                component.Y.Us - height * 0.5f,
                width,
                    height),
                valueFootprint,
                preserveCenter: true);
        }

        private static void ApplyNativeNodeRect(
            CircuitComponent component,
            Rect rect)
        {
            component.X.Us = rect.center.x;
            component.Y.Us = rect.center.y;
            component.Width.Us = rect.width;
            component.Height.Us = rect.height;
        }

        private static AutomationNodeKind NativeKind(CircuitComponent component)
        {
            if (component is GenericBlockGetter)
                return AutomationNodeKind.InputGetter;
            if (component is GenericBlockSetter)
                return AutomationNodeKind.OutputSetter;
            if (component is NativeSwitch switchComponent)
                return NativeSwitchKind(switchComponent);
            if (component is LogicGate logicGate)
                return NativeLogicGateKind(logicGate);
            if (component is FuzzyThreshold fuzzyThreshold)
                return NativeFuzzyThresholdKind(fuzzyThreshold);
            if (component is MaxMin maxMin)
                return NativeMaxMinKind(maxMin);
            if (component is ConstantInput constant)
                return constant.Type.Us == ConstantInput.ConstantType.ItsAFloat
                    ? AutomationNodeKind.Constant
                    : AutomationNodeKind.NativeUnsupported;
            if (component is RandomInput)
                return AutomationNodeKind.Random;
            if (component is Clamp)
                return AutomationNodeKind.Clamp;
            if (component is Delay)
                return AutomationNodeKind.Smooth;
            if (component is NativeComment comment)
                return IsForeverComment(comment.InputValue.Us)
                    ? AutomationNodeKind.Forever
                    : AutomationNodeKind.Comment;
            if (component is Evaluator evaluator)
                return NativeEvaluatorKind(evaluator);
            return AutomationNodeKind.NativeUnsupported;
        }

        private static bool IsSupportedAutomationNativeComponent(CircuitComponent component)
        {
            if (component == null)
                return false;
            if (IsNativeOwnerMarker(component))
                return false;

            if (component is GenericBlockGetter ||
                component is GenericBlockSetter ||
                component is NativeSwitch ||
                component is LogicGate ||
                component is FuzzyThreshold ||
                component is MaxMin ||
                component is RandomInput ||
                component is Clamp ||
                component is Delay ||
                component is NativeComment)
            {
                return true;
            }

            if (component is ConstantInput constant)
                return constant.Type.Us == ConstantInput.ConstantType.ItsAFloat;

            return component is Evaluator evaluator &&
                   IsSupportedNativeEvaluator(evaluator);
        }

        private static AutomationNodeKind NativeLogicGateKind(LogicGate logicGate)
        {
            if (logicGate == null)
                return AutomationNodeKind.LogicAnd;

            switch (logicGate.SelectedGate.Us)
            {
                case GateType.NOT:
                    return AutomationNodeKind.LogicNot;
                case GateType.OR:
                    return AutomationNodeKind.LogicOr;
                case GateType.XOR:
                    return AutomationNodeKind.LogicXor;
                case GateType.NAND:
                    return AutomationNodeKind.LogicNand;
                case GateType.NOR:
                    return AutomationNodeKind.LogicNor;
                case GateType.XNOR:
                    return AutomationNodeKind.LogicXnor;
                default:
                    return AutomationNodeKind.LogicAnd;
            }
        }

        private static AutomationNodeKind NativeSwitchKind(NativeSwitch switchComponent)
        {
            return switchComponent != null &&
                   Mathf.Abs(switchComponent.Threshold.Us - 0.5f) <= 0.001f
                ? AutomationNodeKind.IfCondition
                : AutomationNodeKind.IfLessThan;
        }

        private static GateType LogicGateType(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.LogicNot:
                    return GateType.NOT;
                case AutomationNodeKind.LogicOr:
                    return GateType.OR;
                case AutomationNodeKind.LogicXor:
                    return GateType.XOR;
                case AutomationNodeKind.LogicNand:
                    return GateType.NAND;
                case AutomationNodeKind.LogicNor:
                    return GateType.NOR;
                case AutomationNodeKind.LogicXnor:
                    return GateType.XNOR;
                default:
                    return GateType.AND;
            }
        }

        private static AutomationNodeKind NativeFuzzyThresholdKind(FuzzyThreshold fuzzyThreshold)
        {
            return fuzzyThreshold != null && !fuzzyThreshold.Above.Us
                ? AutomationNodeKind.CompareBelowThreshold
                : AutomationNodeKind.CompareAboveThreshold;
        }

        private static AutomationNodeKind NativeMaxMinKind(MaxMin maxMin)
        {
            return maxMin != null && maxMin.SelectedOp.Us == OpType.Min
                ? AutomationNodeKind.MathMin
                : AutomationNodeKind.MathMax;
        }

        private static OpType MaxMinOpType(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMin
                ? OpType.Min
                : OpType.Max;
        }

        private static AutomationNodeKind NativeEvaluatorKind(Evaluator evaluator)
        {
            string expression = CompactNativeExpression(evaluator?.Expression.Us);
            if (IsSupportedNativeMathExpression(expression, "*"))
                return AutomationNodeKind.MathMultiply;

            if (IsSupportedNativeMathExpression(expression, "-"))
                return AutomationNodeKind.MathSubtract;

            return IsSupportedNativeMathExpression(expression, "+")
                ? AutomationNodeKind.MathAdd
                : AutomationNodeKind.Comment;
        }

        private static bool IsSupportedNativeEvaluator(Evaluator evaluator)
        {
            string expression = CompactNativeExpression(evaluator?.Expression.Us);
            return IsSupportedNativeMathExpression(expression, "+") ||
                   IsSupportedNativeMathExpression(expression, "-") ||
                   IsSupportedNativeMathExpression(expression, "*");
        }

        private static bool IsSupportedNativeMathExpression(
            string expression,
            string operation)
        {
            if (string.IsNullOrWhiteSpace(expression) ||
                string.IsNullOrWhiteSpace(operation))
            {
                return false;
            }

            string prefix = "a" + operation;
            if (!expression.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string operand = expression.Substring(prefix.Length);
            return string.Equals(operand, "b", StringComparison.Ordinal) ||
                   TryParseFiniteAutomationFloat(operand, out _);
        }

        private static string CompactNativeExpression(string expression)
        {
            return new string((expression ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .ToArray());
        }

        private static string NativeNodeLabel(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return "Read " + SafeFilterName(getter.BlockFilter.Us, getter.BlockTypeName.Us);
            if (component is GenericBlockSetter setter)
                return "Set " + SafeFilterName(setter.BlockFilter.Us, setter.BlockTypeName.Us);
            if (component is NativeSwitch switchComponent)
            {
                if (NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition)
                    return "If true";

                return "Switch > " + switchComponent.Threshold.Us.ToString("G9", CultureInfo.InvariantCulture);
            }
            if (component is LogicGate logicGate)
                return "Logic " + logicGate.SelectedGate.Us;
            if (component is FuzzyThreshold fuzzyThreshold)
                return (fuzzyThreshold.Above.Us ? "Above " : "Below ") +
                       FuzzyThresholdValue(fuzzyThreshold).ToString("G9", CultureInfo.InvariantCulture);
            if (component is MaxMin maxMin)
                return MaxMinName(NativeMaxMinKind(maxMin));
            if (component is Evaluator evaluator)
                return evaluator.Expression.Us;
            if (component is ConstantInput constant)
                return "Constant " + constant.InputValue.Us.ToString("G9", CultureInfo.InvariantCulture);
            if (component is RandomInput random)
                return "Random " +
                       random.RandomLimits.Lower.ToString("G9", CultureInfo.InvariantCulture) + ".." +
                       random.RandomLimits.Upper.ToString("G9", CultureInfo.InvariantCulture);
            if (component is NativeComment comment)
            {
                if (IsForeverComment(comment.InputValue.Us))
                    return "Forever";
                return comment.InputValue.Us;
            }
            return component.GetType().Name;
        }

        private static string NativeNodeProperty(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return NativePropertyLabel(getter);
            if (component is GenericBlockSetter setter)
                return NativePropertyLabel(setter);
            if (component is NativeSwitch switchComponent)
                return NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition
                    ? "condition"
                    : "threshold";
            if (component is LogicGate)
                return "logic";
            if (component is FuzzyThreshold)
                return "threshold";
            if (component is MaxMin)
                return "operation";
            if (component is Evaluator)
                return "expression";
            if (component is ConstantInput)
                return "number";
            if (component is RandomInput)
                return "range";
            if (component is Clamp)
                return "range";
            if (component is Delay)
                return "seconds";
            if (component is NativeComment comment)
            {
                if (IsForeverComment(comment.InputValue.Us))
                    return "body";
                return "note";
            }
            return "value";
        }

        private static string NativeNodeValue(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return "native signal: " + NativePropertyLabel(getter);
            if (component is GenericBlockSetter)
                return "incoming signal";
            if (component is NativeSwitch switchComponent)
            {
                if (NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition)
                    return SwitchValueText(0.5f, switchComponent.FailValue.Us);

                return "threshold " +
                       switchComponent.Threshold.Us.ToString("G9", CultureInfo.InvariantCulture) +
                       " else " +
                       switchComponent.FailValue.Us.ToString("G9", CultureInfo.InvariantCulture);
            }
            if (component is LogicGate logicGate)
                return logicGate.SelectedGate.Us.ToString();
            if (component is FuzzyThreshold fuzzyThreshold)
                return ThresholdValueText(FuzzyThresholdValue(fuzzyThreshold));
            if (component is MaxMin maxMin)
                return MaxMinName(NativeMaxMinKind(maxMin));
            if (component is Evaluator evaluator)
                return evaluator.Expression.Us;
            if (component is ConstantInput constant)
                return constant.InputValue.Us.ToString("G9", CultureInfo.InvariantCulture);
            if (component is RandomInput random)
                return random.RandomLimits.Lower.ToString("G9", CultureInfo.InvariantCulture) + ".." +
                       random.RandomLimits.Upper.ToString("G9", CultureInfo.InvariantCulture);
            if (component is Clamp clamp)
                return clamp.MinMax.Lower.ToString("G9", CultureInfo.InvariantCulture) + ".." +
                       clamp.MinMax.Upper.ToString("G9", CultureInfo.InvariantCulture);
            if (component is Delay delay)
                return delay.DelayTime.Us.ToString("G9", CultureInfo.InvariantCulture) + "s";
            if (component is NativeComment comment)
                return IsForeverComment(comment.InputValue.Us)
                    ? NativeForeverDisplayText(comment.InputValue.Us)
                    : comment.InputValue.Us;
            return string.Empty;
        }

        private string InputGetterCurrentValueText(AutomationGraphNode node)
        {
            return CachedLiveValueText(node, "getter", ComputeInputGetterCurrentValueText);
        }

        private string OutputSetterCurrentValueText(AutomationGraphNode node)
        {
            return CachedLiveValueText(node, "setter", ComputeOutputSetterCurrentValueText);
        }

        private string CachedLiveValueText(
            AutomationGraphNode node,
            string prefix,
            Func<AutomationGraphNode, string> compute)
        {
            if (node == null || compute == null)
                return string.Empty;

            float now = Time.unscaledTime;
            if (_livePreviewNativeVersion != _nativeAutomationCacheVersion ||
                now >= _nextLivePreviewRefreshTime)
            {
                InvalidateLivePreviewCache();
                _livePreviewNativeVersion = _nativeAutomationCacheVersion;
                _nextLivePreviewRefreshTime = now + LivePreviewIntervalSeconds;
            }

            string key = prefix + "|" + node.Id.ToString(CultureInfo.InvariantCulture);
            if (_liveValuePreviewCache.TryGetValue(key, out string cached))
                return cached;

            string value = compute(node);
            _liveValuePreviewCache[key] = value;
            return value;
        }

        private string ComputeInputGetterCurrentValueText(AutomationGraphNode node)
        {
            if (TryGetNativeGetterPreview(node, out string preview))
                return preview;

            return string.IsNullOrWhiteSpace(node?.ValueText)
                ? "native signal"
                : node.ValueText;
        }

        private string ComputeOutputSetterCurrentValueText(AutomationGraphNode node) =>
            TryGetNativeSetterPreview(node, out string preview)
                ? preview
                : "target value";

        private bool TryGetNativeGetterPreview(
            AutomationGraphNode node,
            out string preview)
        {
            preview = null;
            if (!(node?.NativeComponent is GenericBlockGetter getter) ||
                !TryResolveGetterPreviewTarget(getter, out Block block) ||
                !TryReadGetterLiveValue(getter, block, out object value))
            {
                return false;
            }

            preview = "live " + FormatNativePreviewValue(value);
            return true;
        }

        private bool TryGetNativeSetterPreview(
            AutomationGraphNode node,
            out string preview)
        {
            preview = null;
            if (!(node?.NativeComponent is GenericBlockSetter setter) ||
                !TryResolveSetterPreviewTarget(setter, out Block block) ||
                !TryReadSetterLiveValue(setter, block, out object value))
            {
                return false;
            }

            preview = "live " + FormatNativePreviewValue(value);
            return true;
        }

        private bool TryResolveGetterPreviewTarget(
            GenericBlockGetter getter,
            out Block block)
        {
            block = getter?.PotentiallyAffectedBlocks
                .FirstOrDefault(candidate => candidate != null && !candidate.IsDeleted);
            if (block != null)
                return true;

            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                return false;
            }

            AutomationBlockRef target = ResolveNativeTargetBlock(
                breadboard,
                getter,
                getter.BlockTypeName.Us,
                getter.BlockFilter.Us,
                out _);
            return target != null && target.TryGetBlock(out block);
        }

        private bool TryResolveSetterPreviewTarget(
            GenericBlockSetter setter,
            out Block block)
        {
            block = setter?.PotentiallyAffectedBlocks
                .Select(stub => stub?.Block)
                .FirstOrDefault(candidate => candidate != null && !candidate.IsDeleted);
            if (block != null)
                return true;

            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                return false;
            }

            AutomationBlockRef target = ResolveNativeTargetBlock(
                breadboard,
                setter,
                setter.BlockTypeName.Us,
                setter.BlockFilter.Us,
                out _);
            return target != null && target.TryGetBlock(out block);
        }

        private static bool TryReadGetterLiveValue(
            GenericBlockGetter getter,
            Block block,
            out object value)
        {
            value = null;
            if (getter == null || block == null)
                return false;

            Type blockType = block.GetType();
            try
            {
                if (getter.ReadableAttributeId.Us != NativeUnselectedCode &&
                    TryGetReadablePropertyInfo(blockType, getter.ReadableAttributeId.Us, out PropertyInfo readableProperty))
                {
                    value = readableProperty.GetValue(block, null);
                    return true;
                }

                if (getter.BlockPropertyId.Us != NativeUnselectedCode &&
                    TryGetNativeVariablePropertyInfo(
                        blockType,
                        getter.BlockSetId.Us,
                        getter.BlockPropertyId.Us,
                        out PropertyInfo packageProperty,
                        out PropertyInfo variableProperty))
                {
                    object package = packageProperty.GetValue(block, null);
                    if (package == null)
                        return false;

                    value = variableProperty.GetValue(package, null);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryReadSetterLiveValue(
            GenericBlockSetter setter,
            Block block,
            out object value)
        {
            value = null;
            if (setter == null || block == null ||
                setter.BlockPropertyId.Us == NativeUnselectedCode)
            {
                return false;
            }

            try
            {
                if (!TryGetNativeVariablePropertyInfo(
                        block.GetType(),
                        setter.BlockSetId.Us,
                        setter.BlockPropertyId.Us,
                        out PropertyInfo packageProperty,
                        out PropertyInfo variableProperty))
                {
                    return false;
                }

                object package = packageProperty.GetValue(block, null);
                if (package == null)
                    return false;

                value = variableProperty.GetValue(package, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetReadablePropertyInfo(
            Type blockType,
            uint readableId,
            out PropertyInfo readableProperty)
        {
            readableProperty = null;
            if (blockType == null)
                return false;

            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (candidate.Id != readableId || candidate.Property == null)
                    continue;

                readableProperty = candidate.Property;
                return true;
            }

            return false;
        }

        private static bool TryGetNativeVariablePropertyInfo(
            Type blockType,
            uint setId,
            uint propertyId,
            out PropertyInfo packageProperty,
            out PropertyInfo variableProperty)
        {
            packageProperty = null;
            variableProperty = null;
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (candidate.SetId != setId ||
                    candidate.PropertyId != propertyId ||
                    candidate.PackageProperty == null ||
                    candidate.VariableProperty == null)
                {
                    continue;
                }

                packageProperty = candidate.PackageProperty;
                variableProperty = candidate.VariableProperty;
                return true;
            }

            return false;
        }

        private static string FormatNativePreviewValue(object value)
        {
            if (value == null)
                return "null";

            if (value is float floatValue)
                return floatValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double doubleValue)
                return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is decimal decimalValue)
                return decimalValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text)
                ? value.GetType().Name
                : ShortText(text, 32);
        }

        private static string SafeFilterName(string filter, string typeName)
        {
            if (!string.IsNullOrWhiteSpace(filter))
                return filter;
            return string.IsNullOrWhiteSpace(typeName) ? "block" : typeName;
        }

        private static bool IsForeverComment(string text)
        {
            return (text ?? string.Empty).StartsWith(
                NativeForeverMarkerPrefix,
                StringComparison.Ordinal);
        }

        private static string NativeForeverMarkerText(string displayText)
        {
            string value = displayText ?? string.Empty;
            if (IsForeverComment(value))
                return value;

            return NativeForeverMarkerPrefix + value;
        }

        private static string NativeForeverDisplayText(string markerText)
        {
            string value = markerText ?? string.Empty;
            return IsForeverComment(value)
                ? value.Substring(NativeForeverMarkerPrefix.Length)
                : value;
        }

        private static string NativePropertyLabel(GenericBlockGetter getter)
        {
            Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
            if (getter.ReadableAttributeId.Us != NativeUnselectedCode)
            {
                if (TryGetReadableLabel(blockType, getter.ReadableAttributeId.Us, out string label))
                    return label;

                return "unresolved property";
            }

            if (getter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: false, getter.BlockSetId.Us, getter.BlockPropertyId.Us, out string label))
                    return label;

                return "unresolved property";
            }

            return "unresolved property";
        }

        private static string NativePropertyLabel(GenericBlockSetter setter)
        {
            Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
            if (setter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: true, setter.BlockSetId.Us, setter.BlockPropertyId.Us, out string label))
                    return label;

                return "unresolved property";
            }

            return "unresolved property";
        }

        private static bool TryGetReadableLabel(
            Type blockType,
            uint readableId,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (candidate.Id != readableId)
                    continue;

                label = candidate.Label;
                return !string.IsNullOrWhiteSpace(label);
            }

            return false;
        }

        private static bool TryGetVariableLabel(
            Type blockType,
            bool editableOnly,
            uint setId,
            uint propertyId,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly))
            {
                if (candidate.SetId != setId || candidate.PropertyId != propertyId)
                    continue;

                label = candidate.Label;
                return !string.IsNullOrWhiteSpace(label);
            }

            return false;
        }

        private static Color NativeComponentColor(
            CircuitComponent component,
            AutomationNodeKind fallbackKind)
        {
            try
            {
                Color color = component.OutlineColor.Us;
                if (color.a > 0.001f)
                    return color;
            }
            catch
            {
                // Use kind color below.
            }

            return NodeColor(fallbackKind);
        }

        private static bool ComponentAcceptsInput(CircuitComponent component) =>
            component != null && component.MaxInputs > 0;

        private static bool ComponentProducesOutput(CircuitComponent component) =>
            component != null && component.MaxOutputs > 0;

        private static bool NativeComponentUsesValueFootprint(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return BlockShape(kind) == AutomationBlockShape.Value ||
                   component.Width.Us > 1f &&
                   CanProduceValueBlock(kind) &&
                   IsValueFootprint(new Rect(0f, 0f, component.Width.Us, component.Height.Us));
        }

        private static bool CanNativeComponentFeedValueSlot(
            CircuitComponent component,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (component == null ||
                !CanProduceValueBlock(NativeKind(component)) ||
                !ComponentProducesOutput(component))
            {
                return false;
            }

            if (CanFeedSignalValueSlot(hostKind, slotKind))
                return true;

            return component is ConstantInput &&
                   IsConstantOnlyValueSlot(hostKind, slotKind);
        }

        private static bool IsStackFlowComponent(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return !NativeComponentUsesValueFootprint(component) &&
                   kind != AutomationNodeKind.Comment &&
                   kind != AutomationNodeKind.Forever &&
                   (ComponentProducesOutput(component) || ComponentAcceptsInput(component));
        }

        private static IEnumerable<CircuitComponent> OrderedNativeFlowComponents(
            IReadOnlyList<CircuitComponent> components)
        {
            return OrderedNativeFlowChains(components).SelectMany(chain => chain);
        }

        private static List<List<CircuitComponent>> OrderedNativeFlowChains(
            IReadOnlyList<CircuitComponent> components)
        {
            if (components == null)
                return new List<List<CircuitComponent>>();

            var result = new List<List<CircuitComponent>>();
            List<CircuitComponent> topLevel = components
                .Where(component => component != null)
                .Where(component => NativeBodyParent(components, component) == null)
                .Where(component => NativeValueHost(components, component) == null)
                .Where(component => IsStackFlowComponent(component) || AcceptsControlBody(NativeKind(component)))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .ToList();

            var visited = new HashSet<CircuitComponent>();
            List<CircuitComponent> roots = topLevel
                .Where(component => PreviousSnappedNativeStackComponent(components, topLevel, component) == null)
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .ToList();
            foreach (CircuitComponent component in roots)
                AppendNativeFlowChain(components, topLevel, component, visited, result);
            foreach (CircuitComponent component in topLevel)
                AppendNativeFlowChain(components, topLevel, component, visited, result);

            return result;
        }

        private static void AppendNativeFlowChain(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent start,
            HashSet<CircuitComponent> visited,
            List<List<CircuitComponent>> result)
        {
            if (start == null || visited == null || result == null || visited.Contains(start))
                return;

            var chain = new List<CircuitComponent>();
            CircuitComponent current = start;
            while (current != null && visited.Add(current))
            {
                AppendNativeFlowComponent(components, current, chain);
                current = NextSnappedNativeStackComponent(components, candidates, current, visited);
            }

            if (chain.Count > 0)
                result.Add(chain);
        }

        private static void AppendNativeFlowComponent(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent component,
            List<CircuitComponent> result)
        {
            if (component == null || result == null)
                return;

            if (IsStackFlowComponent(component))
                result.Add(component);

            foreach (CircuitComponent child in NativeBodyChildrenForHost(components, component))
                AppendNativeFlowComponent(components, child, result);
        }

        private static CircuitComponent PreviousSnappedNativeStackComponent(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent component)
        {
            if (components == null || candidates == null || component == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, component))
                .Where(candidate => AreNativeStackComponentsSnapped(components, candidate, component))
                .OrderBy(candidate => Mathf.Abs(NativeComponentRect(component).y - (NativeComponentRect(candidate).yMax - 2f)))
                .ThenBy(candidate => Mathf.Abs(NativeComponentRect(component).x - NativeComponentRect(candidate).x))
                .FirstOrDefault();
        }

        private static CircuitComponent NextSnappedNativeStackComponent(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent component,
            HashSet<CircuitComponent> visited)
        {
            if (components == null || candidates == null || component == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, component))
                .Where(candidate => visited == null || !visited.Contains(candidate))
                .Where(candidate => AreNativeStackComponentsSnapped(components, component, candidate))
                .OrderBy(candidate => Mathf.Abs(NativeComponentRect(candidate).y - (NativeComponentRect(component).yMax - 2f)))
                .ThenBy(candidate => Mathf.Abs(NativeComponentRect(candidate).x - NativeComponentRect(component).x))
                .FirstOrDefault();
        }

        private static bool AreNativeStackComponentsSnapped(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent from,
            CircuitComponent to)
        {
            if (components == null ||
                from == null ||
                to == null ||
                !IsStackFlowComponent(from) && !AcceptsControlBody(NativeKind(from)) ||
                !IsStackFlowComponent(to) && !AcceptsControlBody(NativeKind(to)) ||
                !ReferenceEquals(NativeBodyParent(components, from), NativeBodyParent(components, to)))
            {
                return false;
            }

            Rect fromRect = NativeComponentRect(from);
            Rect toRect = NativeComponentRect(to);
            if (IsNativeConnected(from, to))
                return true;

            float expectedY = fromRect.yMax - 2f;
            return Mathf.Abs(toRect.x - fromRect.x) <= 14f &&
                   Mathf.Abs(toRect.y - expectedY) <= 14f;
        }

        private static CircuitComponent NativeValueHost(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent valueComponent)
        {
            if (components == null || valueComponent == null)
                return null;

            foreach (CircuitComponent host in components)
            {
                if (host == null ||
                    ReferenceEquals(host, valueComponent) ||
                    !AcceptsValueSlot(NativeKind(host)))
                {
                    continue;
                }

                foreach (AutomationValueSlotKind slotKind in ValueSlotKinds(NativeKind(host)))
                {
                    if (IsNativeStackFedPrimaryValueSlot(components, host, slotKind))
                        continue;

                    if (ReferenceEquals(FindSnappedValueComponent(host, components, slotKind), valueComponent))
                        return host;
                }
            }

            return null;
        }

        private static bool IsNativeStackFedPrimaryValueSlot(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent host,
            AutomationValueSlotKind slotKind)
        {
            return components != null &&
                   host != null &&
                   slotKind == AutomationValueSlotKind.Pass &&
                   UsesStackAsPrimaryInput(NativeKind(host)) &&
                   PreviousSnappedNativeStackComponent(components, components, host) != null;
        }

        private static IEnumerable<CircuitComponent> NativeBodyChildrenForHost(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent host)
        {
            if (components == null ||
                host == null ||
                !AcceptsControlBody(NativeKind(host)))
            {
                return Enumerable.Empty<CircuitComponent>();
            }

            return components
                .Where(component => component != null && !ReferenceEquals(component, host) && IsBodyFlowComponent(component))
                .Where(component => ReferenceEquals(NativeBodyParent(components, component), host))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us);
        }

        private static CircuitComponent NativeBodyParent(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent component)
        {
            if (components == null || component == null || !IsBodyFlowComponent(component))
                return null;

            Vector2 center = NativeComponentRect(component).center;
            return components
                .Where(host => host != null &&
                               !ReferenceEquals(host, component) &&
                               AcceptsControlBody(NativeKind(host)))
                .Select(host => new
                {
                    Host = host,
                    Body = ControlBodyRect(NativeComponentRect(host), NativeKind(host))
                })
                .Where(candidate => candidate.Body.Contains(center))
                .OrderBy(candidate => Mathf.Max(0f, candidate.Body.width) * Mathf.Max(0f, candidate.Body.height))
                .ThenBy(candidate => candidate.Body.width)
                .ThenBy(candidate => candidate.Body.height)
                .ThenBy(candidate => candidate.Host.UniqueId)
                .Select(candidate => candidate.Host)
                .FirstOrDefault();
        }

        private static bool IsBodyFlowComponent(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return !NativeComponentUsesValueFootprint(component) &&
                   kind != AutomationNodeKind.Comment &&
                   kind != AutomationNodeKind.Forever &&
                   (ComponentProducesOutput(component) || ComponentAcceptsInput(component));
        }

        private static CircuitComponent FindSnappedValueComponent(
            CircuitComponent host,
            IReadOnlyList<CircuitComponent> components)
        {
            return FindSnappedValueComponent(host, components, AutomationValueSlotKind.Pass);
        }

        private static CircuitComponent FindSnappedValueComponent(
            CircuitComponent host,
            IReadOnlyList<CircuitComponent> components,
            AutomationValueSlotKind slotKind)
        {
            if (host == null || components == null)
                return null;

            AutomationNodeKind hostKind = NativeKind(host);
            if (!AcceptsValueSlot(hostKind))
                return null;

            Rect slot = ValueSlotRect(NativeComponentRect(host), hostKind, slotKind);
            float threshold = 52f;
            CircuitComponent best = null;
            float bestDistance = threshold;
            foreach (CircuitComponent candidate in components)
            {
                if (candidate == null ||
                    ReferenceEquals(candidate, host) ||
                    !CanNativeComponentFeedValueSlot(candidate, hostKind, slotKind))
                {
                    continue;
                }

                float distance = Vector2.Distance(NativeComponentRect(candidate).center, slot.center);
                if (distance >= bestDistance)
                    continue;

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private static bool TryApplySwitchElseValue(
            NativeSwitch switchComponent,
            CircuitComponent valueComponent)
        {
            if (switchComponent == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            switchComponent.FailValue.Us = value;
            return true;
        }

        private static bool TryApplySwitchThresholdValue(
            NativeSwitch switchComponent,
            CircuitComponent valueComponent)
        {
            if (switchComponent == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            switchComponent.Threshold.Us = value;
            return true;
        }

        private static bool TryApplyFuzzyThresholdValue(
            FuzzyThreshold fuzzyThreshold,
            CircuitComponent valueComponent)
        {
            if (fuzzyThreshold == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            fuzzyThreshold.ThresholdLimits.Us = new Vector2(value, value);
            return true;
        }

        private static bool TryApplyEvaluatorAddAmount(
            Evaluator evaluator,
            CircuitComponent valueComponent)
        {
            return TryApplyEvaluatorMathOperand(evaluator, valueComponent, AutomationNodeKind.MathAdd);
        }

        private static bool TryApplyEvaluatorMathOperand(
            Evaluator evaluator,
            CircuitComponent valueComponent,
            AutomationNodeKind kind)
        {
            if (evaluator == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            evaluator.Expression.Us = MathExpressionText(kind, FormatGraphFloat(value));
            return true;
        }

        private static bool TryApplyClampMinimum(
            Clamp clamp,
            CircuitComponent valueComponent)
        {
            if (clamp == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            float upper = Mathf.Max(value, clamp.MinMax.Upper);
            clamp.MinMax.Lower = Mathf.Min(value, upper);
            clamp.MinMax.Upper = upper;
            return true;
        }

        private static bool TryApplyClampBounds(
            Clamp clamp,
            CircuitComponent minimumComponent,
            CircuitComponent maximumComponent)
        {
            if (clamp == null)
                return false;

            float lower = clamp.MinMax.Lower;
            float upper = clamp.MinMax.Upper;
            if (minimumComponent != null &&
                !TryReadValueComponentFloat(minimumComponent, out lower))
            {
                return false;
            }

            if (maximumComponent != null &&
                !TryReadValueComponentFloat(maximumComponent, out upper))
            {
                return false;
            }

            if (lower > upper)
            {
                float swap = lower;
                lower = upper;
                upper = swap;
            }

            clamp.MinMax.Lower = lower;
            clamp.MinMax.Upper = upper;
            return true;
        }

        private static bool TryApplyClampMaximum(
            Clamp clamp,
            CircuitComponent valueComponent)
        {
            if (clamp == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            float lower = Mathf.Min(clamp.MinMax.Lower, value);
            clamp.MinMax.Lower = lower;
            clamp.MinMax.Upper = Mathf.Max(value, lower);
            return true;
        }

        private static bool TryApplyDelaySeconds(
            Delay delay,
            CircuitComponent valueComponent)
        {
            if (delay == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value) ||
                value < 0f)
            {
                return false;
            }

            delay.DelayTime.Us = value;
            return true;
        }

        private static bool TryReadValueComponentFloat(
            CircuitComponent component,
            out float value)
        {
            value = 0f;
            if (component is ConstantInput constant)
            {
                value = constant.InputValue.Us;
                return IsFiniteAutomationFloat(value);
            }

            return false;
        }

        private static bool TryGetNativeComponent(
            AutomationGraphNode node,
            out CircuitComponent component)
        {
            component = node?.NativeComponent as CircuitComponent;
            return component != null;
        }

        private void AppendImportedRawNativeConnections(
            IReadOnlyList<AutomationGraphNode> nativeNodes,
            List<AutomationGraphConnection> editableConnections,
            List<AutomationGraphConnection> importedConnections)
        {
            if (nativeNodes == null || importedConnections == null)
                return;

            var nodesByComponent = nativeNodes
                .Where(node => node?.NativeComponent is CircuitComponent)
                .GroupBy(node => (CircuitComponent)node.NativeComponent)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (AutomationGraphNode targetNode in nativeNodes)
            {
                if (!(targetNode?.NativeComponent is CircuitComponent target) ||
                    target.BInputs == null)
                {
                    continue;
                }

                for (int inputIndex = 0; inputIndex < target.BInputs.Count; inputIndex++)
                {
                    BInput input = target.BInputs[inputIndex];
                    CircuitComponent source = input?.OurOutput?.IsLatched == true
                        ? input.OurOutput.Them?.OurComponent
                        : null;
                    if (source == null ||
                        !nodesByComponent.TryGetValue(source, out AutomationGraphNode sourceNode) ||
                        IsEsuOwnedNativeNode(sourceNode) && IsEsuOwnedNativeNode(targetNode))
                    {
                        continue;
                    }

                    DescribeImportedNativeInput(
                        targetNode,
                        target,
                        input,
                        inputIndex,
                        out AutomationGraphConnectionKind kind,
                        out AutomationValueSlotKind slotKind);
                    AddNativeGraphConnection(
                        editableConnections,
                        importedConnections,
                        new AutomationGraphConnection(
                            kind,
                            sourceNode,
                            targetNode,
                            slotKind,
                            AutomationGraphWireOrigin.NativeImported,
                            nativeInputIndex: inputIndex));
                }
            }
        }

        private static void DescribeImportedNativeInput(
            AutomationGraphNode targetNode,
            CircuitComponent target,
            BInput input,
            int inputIndex,
            out AutomationGraphConnectionKind kind,
            out AutomationValueSlotKind slotKind)
        {
            kind = AutomationGraphConnectionKind.Stack;
            slotKind = AutomationValueSlotKind.Pass;
            if (target is NativeSwitch switchComponent)
            {
                if (ReferenceEquals(input, switchComponent.Pass))
                    kind = AutomationGraphConnectionKind.Value;
                return;
            }

            if (target is LogicGate && inputIndex == 1)
            {
                kind = AutomationGraphConnectionKind.Value;
                slotKind = AutomationValueSlotKind.LogicB;
                return;
            }

            if (target is Evaluator && inputIndex == 1)
            {
                kind = AutomationGraphConnectionKind.Value;
                slotKind = MathOperandSlotKind(targetNode.Kind);
                return;
            }

            if (target is MaxMin && inputIndex == 1)
            {
                kind = AutomationGraphConnectionKind.Value;
                slotKind = AutomationValueSlotKind.MathB;
            }
        }

        private static bool NativeStackConnectionExists(
            AutomationGraphNode from,
            AutomationGraphNode to)
        {
            if (!TryGetNativeComponent(from, out CircuitComponent fromComponent) ||
                !TryGetNativeComponent(to, out CircuitComponent toComponent) ||
                !ComponentProducesOutput(fromComponent) ||
                !ComponentAcceptsInput(toComponent))
            {
                return false;
            }

            if (toComponent is NativeSwitch switchComponent)
                return IsNativeConnectedToInput(fromComponent, switchComponent.Switcher);

            // Secondary/native-specialised ports must never be reinterpreted as
            // the Scratch stack input. Opaque vanilla blocks keep their exact
            // native port topology read-only instead of guessing a primary port.
            if (!IsSupportedAutomationNativeComponent(toComponent))
                return false;

            return IsNativeConnectedToInputAt(fromComponent, toComponent, 0);
        }

        private static bool NativeValueConnectionExists(
            AutomationGraphNode value,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (!TryGetNativeComponent(value, out CircuitComponent valueComponent) ||
                !TryGetNativeComponent(host, out CircuitComponent hostComponent))
            {
                return false;
            }

            AutomationNodeKind hostKind = NativeKind(hostComponent);
            if (!CanNativeComponentFeedValueSlot(valueComponent, hostKind, slotKind))
                return false;

            if (hostComponent is NativeSwitch switchComponent)
                return slotKind == AutomationValueSlotKind.Pass &&
                       IsNativeConnectedToInput(valueComponent, switchComponent.Pass);

            if (hostComponent is Evaluator &&
                IsMathEvaluatorKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostComponent is LogicGate &&
                IsLogicGateKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostComponent is MaxMin &&
                IsMaxMinKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostKind == AutomationNodeKind.OutputSetter ||
                IsFuzzyThresholdKind(hostKind) ||
                hostComponent is Clamp ||
                hostComponent is Delay)
            {
                return slotKind == AutomationValueSlotKind.Pass &&
                       IsNativeConnected(valueComponent, hostComponent);
            }

            return slotKind == AutomationValueSlotKind.Pass &&
                   IsNativeConnected(valueComponent, hostComponent);
        }

        private static bool TryConnectComponentToHost(
            Board board,
            CircuitComponent from,
            CircuitComponent to,
            ref int connected,
            ref int alreadyConnected)
        {
            return TryConnectComponentToInputAt(
                board,
                from,
                to,
                0,
                ref connected,
                ref alreadyConnected);
        }

        private static bool TryConnectComponentToInput(
            Board board,
            CircuitComponent from,
            BInput input,
            ref int connected,
            ref int alreadyConnected)
        {
            if (board == null ||
                from == null ||
                input == null ||
                !ComponentProducesOutput(from))
            {
                return false;
            }

            if (IsNativeConnectedToInput(from, input))
            {
                alreadyConnected++;
                return true;
            }

            // A remaining latch was not removed by ESU ownership cleanup, so
            // it belongs to imported vanilla data. Never replace that wire.
            if (input.OurOutput?.IsLatched == true)
                return false;

            AOutput output = from.AOutputs?.Us.FirstOrDefault();
            if (output == null)
                return false;

            try
            {
                new CreateConnectionCommand(board, output, input).Execute();
            }
            catch
            {
                return false;
            }
            if (IsNativeConnectedToInput(from, input))
            {
                connected++;
                return true;
            }

            return false;
        }

        private static bool TryConnectComponentToInputAt(
            Board board,
            CircuitComponent from,
            CircuitComponent to,
            int inputIndex,
            ref int connected,
            ref int alreadyConnected)
        {
            if (to == null ||
                inputIndex < 0 ||
                to.BInputs == null ||
                to.BInputs.Count <= inputIndex)
            {
                return false;
            }

            return TryConnectComponentToInput(
                board,
                from,
                to.BInputs[inputIndex],
                ref connected,
                ref alreadyConnected);
        }

        private static bool IsNativeConnected(
            CircuitComponent from,
            CircuitComponent to)
        {
            if (from == null || to == null)
                return false;

            foreach (BInput input in to.BInputs.Us)
            {
                if (input?.OurOutput?.IsLatched == true &&
                    ReferenceEquals(input.OurOutput.Them?.OurComponent, from))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNativeConnectedToInput(
            CircuitComponent from,
            BInput input)
        {
            return from != null &&
                   input?.OurOutput?.IsLatched == true &&
                   ReferenceEquals(input.OurOutput.Them?.OurComponent, from);
        }

        private static bool IsNativeConnectedToInputAt(
            CircuitComponent from,
            CircuitComponent to,
            int inputIndex)
        {
            if (from == null ||
                to == null ||
                inputIndex < 0 ||
                to.BInputs == null ||
                to.BInputs.Count <= inputIndex)
            {
                return false;
            }

            return IsNativeConnectedToInput(from, to.BInputs[inputIndex]);
        }

        private static bool TryConfigureGetterProperty(
            GenericBlockGetter getter,
            Type blockType,
            string query)
        {
            if (TryMatchGetterReadable(blockType, query, out uint readableId))
            {
                getter.ReadableAttributeId.Us = readableId;
                getter.BlockPropertyId.Us = NativeUnselectedCode;
                getter.BlockSetId.Us = NativeUnselectedCode;
                return true;
            }

            if (TryMatchGetterVariable(blockType, query, out uint setId, out uint propertyId))
            {
                getter.ReadableAttributeId.Us = NativeUnselectedCode;
                getter.BlockSetId.Us = setId;
                getter.BlockPropertyId.Us = propertyId;
                return true;
            }

            return false;
        }

        private static bool TryConfigureSetterProperty(
            GenericBlockSetter setter,
            Type blockType,
            string query)
        {
            if (!TryMatchSetterVariable(blockType, query, out uint setId, out uint propertyId))
                return false;

            setter.BlockSetId.Us = setId;
            setter.BlockPropertyId.Us = propertyId;
            return true;
        }

        private static IEnumerable<string> EnumerateGetterPropertyLabels(Type blockType)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string label in EnumerateReadableLabels(blockType))
            {
                if (!string.IsNullOrWhiteSpace(label) && seen.Add(label))
                    yield return label;
            }

            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label) && seen.Add(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static IEnumerable<string> EnumerateSetterPropertyLabels(Type blockType)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: true))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label) && seen.Add(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static IEnumerable<string> EnumerateReadableLabels(Type blockType)
        {
            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static bool TryMatchGetterReadable(
            Type blockType,
            string query,
            out uint readableId)
        {
            readableId = NativeUnselectedCode;
            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                readableId = candidate.Id;
                return readableId != NativeUnselectedCode;
            }

            return false;
        }

        private static IEnumerable<NativeReadableCandidate> EnumerateReadableCandidates(Type blockType)
        {
            if (blockType == null)
                return Enumerable.Empty<NativeReadableCandidate>();

            if (s_readableCandidatesByType.TryGetValue(blockType, out List<NativeReadableCandidate> cached))
                return cached;

            var candidates = new List<NativeReadableCandidate>();
            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            IEnumerable items = method?.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items != null)
            {
                foreach (object item in items)
                {
                    PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                    object attribute = GetMemberValue(item, "attribute", "Item2");
                    if (!TryGetUInt(attribute, "Index", out uint id))
                        continue;

                    candidates.Add(new NativeReadableCandidate(
                        id,
                        CandidateText(property?.Name, attribute),
                        CandidateLabel(property?.Name, attribute),
                        property));
                }
            }

            s_readableCandidatesByType[blockType] = candidates;
            return candidates;
        }

        private static bool TryMatchGetterVariable(
            Type blockType,
            string query,
            out uint setId,
            out uint propertyId)
        {
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                setId = candidate.SetId;
                propertyId = candidate.PropertyId;
                return true;
            }

            setId = NativeUnselectedCode;
            propertyId = NativeUnselectedCode;
            return false;
        }

        private static bool TryMatchSetterVariable(
            Type blockType,
            string query,
            out uint setId,
            out uint propertyId)
        {
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: true))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                setId = candidate.SetId;
                propertyId = candidate.PropertyId;
                return true;
            }

            if (IsDefaultPropertyQuery(query) &&
                TryGetSingleEditableNativeVariable(blockType, out NativeVariableCandidate single))
            {
                setId = single.SetId;
                propertyId = single.PropertyId;
                return true;
            }

            setId = NativeUnselectedCode;
            propertyId = NativeUnselectedCode;
            return false;
        }

        private static bool IsDefaultPropertyQuery(string query)
        {
            string trimmed = query?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ||
                   string.Equals(trimmed, "value", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetSingleEditableNativeVariable(
            Type blockType,
            out NativeVariableCandidate single)
        {
            single = default;
            List<NativeVariableCandidate> candidates = EnumerateNativeVariables(blockType, editableOnly: true)
                .GroupBy(candidate => candidate.SetId.ToString(CultureInfo.InvariantCulture) + "|" + candidate.PropertyId.ToString(CultureInfo.InvariantCulture))
                .Select(group => group.First())
                .Take(2)
                .ToList();
            if (candidates.Count != 1)
                return false;

            single = candidates[0];
            return true;
        }

        private static IEnumerable<NativeVariableCandidate> EnumerateNativeVariables(
            Type blockType,
            bool editableOnly)
        {
            if (blockType == null)
                return Enumerable.Empty<NativeVariableCandidate>();

            string cacheKey = blockType.AssemblyQualifiedName + "|" + (editableOnly ? "1" : "0");
            if (s_nativeVariablesByType.TryGetValue(cacheKey, out List<NativeVariableCandidate> cached))
                return cached;

            var candidates = new List<NativeVariableCandidate>();
            Type setFinder = FindType("BrilliantSkies.DataManagement.Finders.SetFinder");
            Type saveAndLoad = FindType("BrilliantSkies.DataManagement.Saving.DataPackageSaveAndLoad");
            MethodInfo packagesMethod = setFinder?.GetMethod(
                "GetAllPackagesFromType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo gatherMethod = saveAndLoad?.GetMethod(
                "Gather",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Type) },
                null);
            if (packagesMethod == null || gatherMethod == null)
            {
                s_nativeVariablesByType[cacheKey] = candidates;
                return candidates;
            }

            object record = packagesMethod.Invoke(null, new object[] { blockType });
            IEnumerable dataPackages = GetMemberValue(record, "OurDataPackageProperties") as IEnumerable;
            if (dataPackages == null)
            {
                s_nativeVariablesByType[cacheKey] = candidates;
                return candidates;
            }

            foreach (object dataPackage in dataPackages)
            {
                if (!TryGetUInt(dataPackage, "Index", out uint setId))
                    continue;

                PropertyInfo packageProperty = GetMemberValue(dataPackage, "PropertyInfo") as PropertyInfo;
                Type packageType = packageProperty?.PropertyType;
                if (packageType == null)
                    continue;

                object varSet = gatherMethod.Invoke(null, new object[] { packageType });
                IEnumerable attributes = GetMemberValue(varSet, "Attributes") as IEnumerable;
                if (attributes == null)
                    continue;

                foreach (object variable in attributes)
                {
                    if (editableOnly && GetMemberValue(variable, "EditableAttribute") == null)
                        continue;
                    if (!TryGetUInt(variable, "SaveIndex", out uint propertyId))
                        continue;

                    object attribute = GetMemberValue(variable, "Attribute");
                    string propertyName = GetMemberString(variable, "PropertyName");
                    PropertyInfo variableProperty = GetMemberValue(variable, "PropertyInfo") as PropertyInfo;
                    if (variableProperty == null && !string.IsNullOrWhiteSpace(propertyName))
                    {
                        variableProperty = packageType.GetProperty(
                            propertyName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    candidates.Add(new NativeVariableCandidate(
                        setId,
                        propertyId,
                        CandidateText(packageProperty.Name, attribute, propertyName),
                        CandidateLabel(packageProperty.Name, attribute, propertyName),
                        packageProperty,
                        variableProperty));
                }
            }

            s_nativeVariablesByType[cacheKey] = candidates;
            return candidates;
        }

        private static Type FindBlockTypeByName(string blockTypeName)
        {
            if (string.IsNullOrWhiteSpace(blockTypeName))
                return null;

            if (s_blockTypeByNameCache.TryGetValue(blockTypeName, out Type cached))
                return cached;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type match = assembly.GetTypesSafe()
                    .FirstOrDefault(type =>
                        typeof(Block).IsAssignableFrom(type) &&
                        string.Equals(type.Name, blockTypeName, StringComparison.Ordinal));
                if (match != null)
                {
                    s_blockTypeByNameCache[blockTypeName] = match;
                    return match;
                }
            }

            s_blockTypeByNameCache[blockTypeName] = null;
            return null;
        }

        private static string CandidateText(
            string primary,
            object attribute,
            string secondary = null)
        {
            return string.Join(
                " ",
                new[]
                {
                    primary,
                    secondary,
                    GetMemberString(attribute, "Name"),
                    GetMemberString(attribute, "Description"),
                    attribute?.ToString()
                }.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray());
        }

        private static string CandidateLabel(
            string primary,
            object attribute,
            string secondary = null)
        {
            string name = GetMemberString(attribute, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (!string.IsNullOrWhiteSpace(secondary))
                return secondary.Trim();

            if (!string.IsNullOrWhiteSpace(primary))
                return primary.Trim();

            string fallback = attribute?.ToString();
            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private static bool MatchesQuery(string query, string candidate)
        {
            string normalizedQuery = NormalizeTokenText(query);
            string normalizedCandidate = NormalizeTokenText(candidate);
            if (string.IsNullOrEmpty(normalizedQuery) ||
                string.IsNullOrEmpty(normalizedCandidate))
            {
                return false;
            }

            if (normalizedCandidate.Contains(normalizedQuery) ||
                normalizedQuery.Contains(normalizedCandidate))
            {
                return true;
            }

            string[] words = (query ?? string.Empty)
                .Split(new[] { ' ', '_', '-', '.', '/', '\\', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeTokenText)
                .Where(word => word.Length > 1)
                .ToArray();
            return words.Length > 0 && words.All(word => normalizedCandidate.Contains(word));
        }

        private static string NormalizeTokenText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            char[] chars = text
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray();
            return new string(chars);
        }

        private static object GetMemberValue(
            object instance,
            params string[] names)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null)
                    return property.GetValue(instance, null);

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(instance);
            }

            return null;
        }

        private static string GetMemberString(
            object instance,
            string name)
        {
            object value = GetMemberValue(instance, name);
            return value == null ? string.Empty : value.ToString();
        }

        private static bool TryGetUInt(
            object instance,
            string name,
            out uint value)
        {
            value = 0u;
            object raw = GetMemberValue(instance, name);
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            if (s_typeByNameCache.TryGetValue(fullName, out Type cached))
                return cached;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    s_typeByNameCache[fullName] = type;
                    return type;
                }
            }

            Type resolved = Type.GetType(fullName, throwOnError: false);
            s_typeByNameCache[fullName] = resolved;
            return resolved;
        }

        private static float ParseFloat(
            string text,
            float fallback)
        {
            if (float.TryParse(
                    (text ?? string.Empty).Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value) &&
                IsFiniteAutomationFloat(value))
            {
                return value;
            }

            return fallback;
        }

        internal static bool TryValidateAutomationLiteral(
            AutomationNodeKind kind,
            string text,
            out string normalized,
            out string issue)
        {
            normalized = text ?? string.Empty;
            issue = null;
            switch (kind)
            {
                case AutomationNodeKind.Constant:
                    if (!TryParseFiniteAutomationFloat(text, out float constant))
                        return InvalidLiteral("number must be one finite invariant value", out issue);
                    normalized = FormatAutomationFloat(constant);
                    return true;

                case AutomationNodeKind.Random:
                case AutomationNodeKind.Clamp:
                    if (!TryParseFiniteAutomationRange(text, out float lower, out float upper))
                        return InvalidLiteral("range must contain two finite values, for example 0..1", out issue);
                    if (lower > upper)
                    {
                        float swap = lower;
                        lower = upper;
                        upper = swap;
                    }
                    normalized = FormatAutomationFloat(lower) + ".." + FormatAutomationFloat(upper);
                    return true;

                case AutomationNodeKind.Smooth:
                    string secondsText = TrimSingleSecondsSuffix(text);
                    if (!TryParseFiniteAutomationFloat(secondsText, out float seconds) || seconds < 0f)
                        return InvalidLiteral("seconds must be a finite value greater than or equal to zero", out issue);
                    normalized = FormatAutomationFloat(seconds) + "s";
                    return true;

                case AutomationNodeKind.IfCondition:
                    int conditionIndex = 0;
                    string conditionText = text ?? string.Empty;
                    SkipAutomationWhitespace(conditionText, ref conditionIndex);
                    if (AutomationLabelAt(conditionText, conditionIndex, "threshold"))
                    {
                        if (!TryConsumeLabeledAutomationFloat(
                                conditionText,
                                ref conditionIndex,
                                "threshold",
                                out float fixedThreshold) ||
                            fixedThreshold != 0.5f)
                        {
                            return InvalidLiteral("If True threshold must remain exactly 0.5", out issue);
                        }
                    }
                    if (!TryConsumeLabeledAutomationFloat(
                            conditionText,
                            ref conditionIndex,
                            "else",
                            out float conditionElse) ||
                        !AutomationTextConsumed(conditionText, conditionIndex))
                        return InvalidLiteral("else value must be finite", out issue);
                    normalized = "threshold 0.5 else " + FormatAutomationFloat(conditionElse);
                    return true;

                case AutomationNodeKind.IfLessThan:
                    int switchIndex = 0;
                    string switchText = text ?? string.Empty;
                    if (!TryConsumeLabeledAutomationFloat(
                            switchText,
                            ref switchIndex,
                            "threshold",
                            out float threshold) ||
                        !TryConsumeLabeledAutomationFloat(
                            switchText,
                            ref switchIndex,
                            "else",
                            out float failValue) ||
                        !AutomationTextConsumed(switchText, switchIndex))
                    {
                        return InvalidLiteral("threshold and else values must both be finite", out issue);
                    }
                    normalized = "threshold " + FormatAutomationFloat(threshold) +
                                 " else " + FormatAutomationFloat(failValue);
                    return true;

                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    int compareIndex = 0;
                    string compareText = text ?? string.Empty;
                    if (!TryConsumeLabeledAutomationFloat(
                            compareText,
                            ref compareIndex,
                            "threshold",
                            out float compareThreshold) ||
                        !AutomationTextConsumed(compareText, compareIndex))
                        return InvalidLiteral("threshold must be finite", out issue);
                    normalized = "threshold " + FormatAutomationFloat(compareThreshold);
                    return true;

                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                    string compact = CompactNativeExpression(text);
                    string operation = kind == AutomationNodeKind.MathAdd
                        ? "+"
                        : kind == AutomationNodeKind.MathSubtract ? "-" : "*";
                    string prefix = "a" + operation;
                    if (!compact.StartsWith(prefix, StringComparison.Ordinal))
                        return InvalidLiteral("math expression must use the visible a " + operation + " value shape", out issue);
                    string operandText = compact.Substring(prefix.Length);
                    if (string.Equals(operandText, "b", StringComparison.Ordinal))
                    {
                        normalized = "a " + operation + " b";
                        return true;
                    }
                    if (!TryParseFiniteAutomationFloat(operandText, out float operand))
                        return InvalidLiteral("math operand must be finite", out issue);
                    normalized = "a " + operation + " " + FormatAutomationFloat(operand);
                    return true;

                default:
                    return true;
            }
        }

        private static bool InvalidLiteral(
            string message,
            out string issue)
        {
            issue = message;
            return false;
        }

        private static bool TryParseFiniteAutomationFloat(
            string text,
            out float value)
        {
            return float.TryParse(
                       (text ?? string.Empty).Trim(),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   IsFiniteAutomationFloat(value);
        }

        private static bool TryParseFiniteAutomationRange(
            string text,
            out float lower,
            out float upper)
        {
            lower = 0f;
            upper = 0f;
            string[] parts = (text ?? string.Empty)
                .Split(new[] { "..", ",", ";" }, StringSplitOptions.None);
            return parts.Length == 2 &&
                   TryParseFiniteAutomationFloat(parts[0], out lower) &&
                   TryParseFiniteAutomationFloat(parts[1], out upper);
        }

        private static bool TryParseLabeledAutomationFloat(
            string text,
            string label,
            out float value)
        {
            value = 0f;
            string source = text ?? string.Empty;
            int start = source.IndexOf(label ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            start += (label ?? string.Empty).Length;
            while (start < source.Length &&
                   (char.IsWhiteSpace(source[start]) || source[start] == ':' || source[start] == '='))
            {
                start++;
            }

            int end = start;
            while (end < source.Length &&
                   !char.IsWhiteSpace(source[end]) &&
                   source[end] != ',' &&
                   source[end] != ';')
            {
                end++;
            }

            return start < end &&
                   TryParseFiniteAutomationFloat(source.Substring(start, end - start), out value);
        }

        private static bool TryConsumeLabeledAutomationFloat(
            string text,
            ref int index,
            string label,
            out float value)
        {
            value = 0f;
            string source = text ?? string.Empty;
            SkipAutomationWhitespace(source, ref index);
            if (!AutomationLabelAt(source, index, label))
                return false;

            index += label.Length;
            SkipAutomationWhitespace(source, ref index);
            if (index < source.Length && (source[index] == ':' || source[index] == '='))
            {
                index++;
                SkipAutomationWhitespace(source, ref index);
            }

            int start = index;
            while (index < source.Length &&
                   (char.IsDigit(source[index]) ||
                    source[index] == '+' ||
                    source[index] == '-' ||
                    source[index] == '.' ||
                    source[index] == 'e' ||
                    source[index] == 'E'))
            {
                index++;
            }

            if (start == index ||
                !TryParseFiniteAutomationFloat(source.Substring(start, index - start), out value))
            {
                return false;
            }

            return index >= source.Length || char.IsWhiteSpace(source[index]);
        }

        private static bool AutomationLabelAt(
            string text,
            int index,
            string label)
        {
            string source = text ?? string.Empty;
            string expected = label ?? string.Empty;
            return index >= 0 &&
                   index + expected.Length <= source.Length &&
                   string.Compare(
                       source,
                       index,
                       expected,
                       0,
                       expected.Length,
                       StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool AutomationTextConsumed(
            string text,
            int index)
        {
            string source = text ?? string.Empty;
            SkipAutomationWhitespace(source, ref index);
            return index == source.Length;
        }

        private static void SkipAutomationWhitespace(
            string text,
            ref int index)
        {
            string source = text ?? string.Empty;
            index = Math.Max(0, index);
            while (index < source.Length && char.IsWhiteSpace(source[index]))
                index++;
        }

        private static string TrimSingleSecondsSuffix(string text)
        {
            string value = (text ?? string.Empty).Trim();
            return value.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 1).TrimEnd(Array.Empty<char>())
                : value;
        }

        private static bool IsFiniteAutomationFloat(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static string FormatAutomationFloat(float value) =>
            value.ToString("G9", CultureInfo.InvariantCulture);

        private static float ParseSeconds(
            string text,
            float fallback)
        {
            string cleaned = TrimSingleSecondsSuffix(text);
            return ParseFloat(cleaned, fallback);
        }

        private static void ParseRange(
            string text,
            float fallbackLower,
            float fallbackUpper,
            out float lower,
            out float upper)
        {
            lower = fallbackLower;
            upper = fallbackUpper;
            string value = text ?? string.Empty;
            string[] parts = value.Split(new[] { "..", ",", ";" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                lower = ParseFloat(parts[0], fallbackLower);
                upper = ParseFloat(parts[1], fallbackUpper);
                if (lower > upper)
                {
                    float swap = lower;
                    lower = upper;
                    upper = swap;
                }
            }
        }

        private static void ParseIfSwitchValue(
            string text,
            float fallbackThreshold,
            float fallbackFailValue,
            out float threshold,
            out float failValue)
        {
            threshold = fallbackThreshold;
            failValue = fallbackFailValue;
            List<float> numbers = ExtractFloatTokens(text).ToList();
            if (numbers.Count > 0)
                threshold = numbers[0];
            if (numbers.Count > 2)
                failValue = numbers[2];
            else if ((text ?? string.Empty).IndexOf("else", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     numbers.Count > 1)
                failValue = numbers[1];
        }

        private static IEnumerable<float> ExtractFloatTokens(string text)
        {
            string value = text ?? string.Empty;
            var token = new List<char>();
            for (int index = 0; index <= value.Length; index++)
            {
                char c = index < value.Length ? value[index] : ' ';
                bool numeric = char.IsDigit(c) ||
                               c == '-' ||
                               c == '+' ||
                               c == '.' ||
                               c == 'e' ||
                               c == 'E';
                if (numeric)
                {
                    token.Add(c);
                    continue;
                }

                if (token.Count == 0)
                    continue;

                string candidate = new string(token.ToArray());
                token.Clear();
                if (float.TryParse(
                        candidate,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                    out float parsed) &&
                    IsFiniteAutomationFloat(parsed))
                {
                    yield return parsed;
                }
            }
        }

        private readonly struct NativeVariableCandidate
        {
            internal NativeVariableCandidate(
                uint setId,
                uint propertyId,
                string candidateText,
                string label,
                PropertyInfo packageProperty = null,
                PropertyInfo variableProperty = null)
            {
                SetId = setId;
                PropertyId = propertyId;
                CandidateText = candidateText ?? string.Empty;
                Label = label ?? string.Empty;
                PackageProperty = packageProperty;
                VariableProperty = variableProperty;
            }

            internal uint SetId { get; }

            internal uint PropertyId { get; }

            internal string CandidateText { get; }

            internal string Label { get; }

            internal PropertyInfo PackageProperty { get; }

            internal PropertyInfo VariableProperty { get; }
        }

        private readonly struct NativeReadableCandidate
        {
            internal NativeReadableCandidate(
                uint id,
                string candidateText,
                string label,
                PropertyInfo property)
            {
                Id = id;
                CandidateText = candidateText ?? string.Empty;
                Label = label ?? string.Empty;
                Property = property;
            }

            internal uint Id { get; }

            internal string CandidateText { get; }

            internal string Label { get; }

            internal PropertyInfo Property { get; }
        }

        private sealed class NativeBreadboardSnapshot
        {
            private NativeBreadboardSnapshot(
                NativeBreadBoard breadboard,
                List<CircuitComponent> components,
                List<NativeOwnerRecord> ownerRecords,
                List<Block> constructBlocks,
                int signature)
            {
                Breadboard = breadboard;
                Components = components ?? new List<CircuitComponent>();
                ComponentsById = Components
                    .Where(component => component != null)
                    .GroupBy(component => component.UniqueId)
                    .ToDictionary(group => group.Key, group => group.First());
                OwnerRecords = ownerRecords ?? new List<NativeOwnerRecord>();
                OwnedComponents = new HashSet<CircuitComponent>(
                    OwnerRecords
                    .Where(record => record?.Target != null &&
                                     !ReferenceEquals(record.Target, record.Marker))
                    .Select(record => record.Target));
                _constructBlocks = constructBlocks;
                Signature = signature;
            }

            private List<Block> _constructBlocks;
            private Dictionary<string, List<Block>> _blocksByTypeName;

            internal NativeBreadBoard Breadboard { get; }

            internal List<CircuitComponent> Components { get; }

            internal Dictionary<uint, CircuitComponent> ComponentsById { get; }

            internal List<NativeOwnerRecord> OwnerRecords { get; }

            internal HashSet<CircuitComponent> OwnedComponents { get; }

            internal List<Block> ConstructBlocks =>
                _constructBlocks ?? (_constructBlocks = EnumerateConstructBlocks(Breadboard)
                    .Where(block => block != null)
                    .ToList());

            internal Dictionary<string, List<Block>> BlocksByTypeName =>
                _blocksByTypeName ?? (_blocksByTypeName = ConstructBlocks
                    .Where(block => block != null)
                    .GroupBy(block => block.GetType().Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal));

            internal int Signature { get; }

            internal static NativeBreadboardSnapshot Create(NativeBreadBoard breadboard)
            {
                if (breadboard == null)
                    return null;

                List<CircuitComponent> components = breadboard.Components.ToList();
                List<NativeOwnerRecord> ownerRecords = BuildNativeOwnerRecords(components);
                int signature = ComputeSignature(components, ownerRecords);
                return new NativeBreadboardSnapshot(
                    breadboard,
                    components,
                    ownerRecords,
                    constructBlocks: null,
                    signature);
            }

            private static int ComputeSignature(
                IReadOnlyList<CircuitComponent> components,
                IReadOnlyList<NativeOwnerRecord> ownerRecords)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (CircuitComponent component in components ?? Array.Empty<CircuitComponent>())
                    {
                        if (component == null)
                            continue;

                        hash = hash * 31 + component.UniqueId.GetHashCode();
                        hash = hash * 31 + (component.GetType().FullName ?? component.GetType().Name).GetHashCode();
                        hash = hash * 31 + Mathf.RoundToInt(component.X.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Y.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Width.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Height.Us * 10f);
                        hash = hash * 31 + NativeComponentSignature(component);
                    }

                    foreach (NativeOwnerRecord record in ownerRecords ?? Array.Empty<NativeOwnerRecord>())
                    {
                        if (record == null)
                            continue;

                        hash = hash * 31 + record.ComponentId.GetHashCode();
                        hash = hash * 31 + record.Kind.GetHashCode();
                    }

                    return hash;
                }
            }

            private static int NativeComponentSignature(CircuitComponent component)
            {
                unchecked
                {
                    int hash = 23;
                    if (component is GenericBlockGetter getter)
                    {
                        hash = hash * 31 + (getter.BlockTypeName.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + (getter.BlockFilter.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + getter.ReadableAttributeId.Us.GetHashCode();
                        hash = hash * 31 + getter.BlockSetId.Us.GetHashCode();
                        hash = hash * 31 + getter.BlockPropertyId.Us.GetHashCode();
                    }
                    else if (component is GenericBlockSetter setter)
                    {
                        hash = hash * 31 + (setter.BlockTypeName.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + (setter.BlockFilter.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + setter.BlockSetId.Us.GetHashCode();
                        hash = hash * 31 + setter.BlockPropertyId.Us.GetHashCode();
                    }
                    else if (component is ConstantInput constant)
                    {
                        hash = hash * 31 + constant.Type.Us.GetHashCode();
                        hash = hash * 31 + constant.InputValue.Us.GetHashCode();
                    }
                    else if (component is NativeSwitch switchComponent)
                    {
                        hash = hash * 31 + switchComponent.Threshold.Us.GetHashCode();
                        hash = hash * 31 + switchComponent.FailValue.Us.GetHashCode();
                    }
                    else if (component is LogicGate logicGate)
                    {
                        hash = hash * 31 + logicGate.SelectedGate.Us.GetHashCode();
                        hash = hash * 31 + logicGate.TrueLogic.Us.GetHashCode();
                    }
                    else if (component is FuzzyThreshold fuzzyThreshold)
                    {
                        hash = hash * 31 + fuzzyThreshold.Above.Us.GetHashCode();
                        hash = hash * 31 + fuzzyThreshold.ThresholdLimits.Lower.GetHashCode();
                        hash = hash * 31 + fuzzyThreshold.ThresholdLimits.Upper.GetHashCode();
                    }
                    else if (component is MaxMin maxMin)
                    {
                        hash = hash * 31 + maxMin.SelectedOp.Us.GetHashCode();
                    }
                    else if (component is Evaluator evaluator)
                    {
                        hash = hash * 31 + (evaluator.Expression.Us ?? string.Empty).GetHashCode();
                    }
                    else if (component is RandomInput random)
                    {
                        hash = hash * 31 + random.RandomLimits.Lower.GetHashCode();
                        hash = hash * 31 + random.RandomLimits.Upper.GetHashCode();
                    }
                    else if (component is Clamp clamp)
                    {
                        hash = hash * 31 + clamp.MinMax.Lower.GetHashCode();
                        hash = hash * 31 + clamp.MinMax.Upper.GetHashCode();
                    }
                    else if (component is Delay delay)
                    {
                        hash = hash * 31 + delay.DelayTime.Us.GetHashCode();
                    }
                    else if (component is NativeComment comment)
                    {
                        hash = hash * 31 + (comment.InputValue.Us ?? string.Empty).GetHashCode();
                    }

                    try
                    {
                        if (component.BInputs != null)
                        {
                            hash = hash * 31 + component.BInputs.Count;
                            int inputIndex = 0;
                            foreach (BInput input in component.BInputs.Us)
                            {
                                hash = hash * 31 + inputIndex++;
                                hash = hash * 31 + (input?.OurOutput?.IsLatched == true ? 1 : 0);
                                if (input?.OurOutput?.IsLatched == true)
                                    hash = hash * 31 + (input.OurOutput.Them?.OurComponent?.UniqueId ?? 0u).GetHashCode();
                            }
                        }
                    }
                    catch
                    {
                        // Signature is an invalidation hint; missing a native input is not fatal.
                    }

                    return hash;
                }
            }
        }

        private sealed class NativeBuildContext
        {
            private readonly List<string> _warnings = new List<string>();

            internal NativeBuildContext(
                AutomationBlockRef breadboard,
                IReadOnlyList<AutomationLink> links)
            {
            }

            internal bool HasWarnings => _warnings.Count > 0;

            internal string Detail => string.Join("\n", _warnings.Distinct().ToArray());

            internal void Warn(string warning)
            {
                if (!string.IsNullOrWhiteSpace(warning))
                    _warnings.Add(warning.Trim());
            }
        }

        private sealed class NativeOwnerRecord
        {
            internal NativeOwnerRecord(
                uint componentId,
                AutomationNodeKind kind,
                CircuitComponent target,
                CircuitComponent marker)
            {
                ComponentId = componentId;
                Kind = kind;
                Target = target;
                Marker = marker;
            }

            internal uint ComponentId { get; }

            internal AutomationNodeKind Kind { get; }

            internal CircuitComponent Target { get; }

            internal CircuitComponent Marker { get; }
        }

        private sealed class NativeRestoreOwnership
        {
            internal NativeRestoreOwnership(
                uint originalTargetId,
                CircuitComponent target,
                CircuitComponent marker,
                AutomationNodeKind kind)
            {
                OriginalTargetId = originalTargetId;
                Target = target;
                Marker = marker;
                Kind = kind;
            }

            internal uint OriginalTargetId { get; }

            internal CircuitComponent Target { get; }

            internal CircuitComponent Marker { get; }

            internal AutomationNodeKind Kind { get; }
        }

        private sealed class NativeRemovalBatchResult
        {
            private NativeRemovalBatchResult(
                bool success,
                int removedNodes,
                int removedMarkers,
                bool restored,
                string detail,
                IReadOnlyList<CircuitComponent> removedPackages)
            {
                Success = success;
                RemovedNodes = removedNodes;
                RemovedMarkers = removedMarkers;
                Restored = restored;
                Detail = detail;
                RemovedPackages = removedPackages ?? Array.Empty<CircuitComponent>();
            }

            internal static NativeRemovalBatchResult Empty { get; } =
                new NativeRemovalBatchResult(
                    true,
                    0,
                    0,
                    true,
                    null,
                    Array.Empty<CircuitComponent>());

            internal bool Success { get; }

            internal int RemovedNodes { get; }

            internal int RemovedMarkers { get; }

            internal bool Restored { get; }

            internal string Detail { get; }

            internal IReadOnlyList<CircuitComponent> RemovedPackages { get; }

            internal static NativeRemovalBatchResult Completed(
                int removedNodes,
                int removedMarkers,
                IReadOnlyList<CircuitComponent> removedPackages) =>
                new NativeRemovalBatchResult(
                    true,
                    removedNodes,
                    removedMarkers,
                    true,
                    null,
                    removedPackages);

            internal static NativeRemovalBatchResult Failed(
                string detail,
                bool restored,
                IReadOnlyList<CircuitComponent> removedPackages = null) =>
                new NativeRemovalBatchResult(
                    false,
                    0,
                    0,
                    restored,
                    detail,
                    removedPackages);
        }

        private sealed class NativeLoweringResult
        {
            internal List<string> Errors { get; } = new List<string>();

            internal List<string> Warnings { get; } = new List<string>();

            internal List<string> Created { get; } = new List<string>();

            internal List<CircuitComponent> CreatedComponents { get; } = new List<CircuitComponent>();

            internal int CreatedCount => Created.Count;

            internal bool HasErrors => Errors.Count > 0;

            internal string Detail => string.Join("\n", DetailLines().ToArray());

            internal void AddError(string text) => AddUnique(Errors, text);

            internal void AddWarning(string text) => AddUnique(Warnings, text);

            internal void AddCreated(string text) => AddUnique(Created, text);

            private IEnumerable<string> DetailLines()
            {
                foreach (string line in SectionLines("Lowered", Created))
                    yield return line;
                foreach (string line in SectionLines("Lowering warnings", Warnings))
                    yield return line;
                foreach (string line in SectionLines("Lowering errors", Errors))
                    yield return line;
            }

            private static IEnumerable<string> SectionLines(
                string title,
                IReadOnlyList<string> lines)
            {
                if (lines == null || lines.Count == 0)
                    yield break;

                yield return title;
                foreach (string line in lines)
                    yield return "  - " + line;
            }

            private static void AddUnique(
                ICollection<string> lines,
                string text)
            {
                if (lines == null || string.IsNullOrWhiteSpace(text))
                    return;

                string trimmed = text.Trim();
                if (!lines.Contains(trimmed))
                    lines.Add(trimmed);
            }
        }

        private sealed class PreparedNativeNode
        {
            internal PreparedNativeNode(
                AutomationGraphNode node,
                CircuitComponent component,
                string warningDetail)
            {
                Node = node;
                Component = component;
                WarningDetail = warningDetail;
            }

            internal AutomationGraphNode Node { get; }

            internal CircuitComponent Component { get; }

            internal string WarningDetail { get; }
        }

        private sealed class NativeInputConnectionState
        {
            internal NativeInputConnectionState(BInput input, AOutput output)
            {
                Input = input;
                Output = output;
            }

            internal BInput Input { get; }

            internal AOutput Output { get; }
        }

        private sealed class NativeComponentEditState
        {
            private NativeComponentEditState(
                CircuitComponent component,
                Action restore,
                Func<bool> verify)
            {
                Component = component;
                _restore = restore;
                _verify = verify;
            }

            internal CircuitComponent Component { get; }

            private readonly Action _restore;

            private readonly Func<bool> _verify;

            internal static NativeComponentEditState Capture(
                CircuitComponent component)
            {
                if (component == null)
                    return new NativeComponentEditState(null, null, null);

                float x = component.X.Us;
                float y = component.Y.Us;
                float width = component.Width.Us;
                float height = component.Height.Us;
                Color outlineColor = component.OutlineColor.Us;
                Action restoreSpecific = () => { };
                Func<bool> verifySpecific = () => true;

                if (component is GenericBlockGetter getter)
                {
                    Type blockType = getter.BlockType;
                    string blockTypeName = getter.BlockTypeName.Us;
                    string blockFilter = getter.BlockFilter.Us;
                    uint readableAttributeId = getter.ReadableAttributeId.Us;
                    uint blockSetId = getter.BlockSetId.Us;
                    uint blockPropertyId = getter.BlockPropertyId.Us;
                    var affectedBlocks = getter.PotentiallyAffectedBlocks.ToList();
                    restoreSpecific = () =>
                    {
                        getter.BlockType = blockType;
                        getter.BlockTypeName.Us = blockTypeName;
                        getter.BlockFilter.Us = blockFilter;
                        getter.ReadableAttributeId.Us = readableAttributeId;
                        getter.BlockSetId.Us = blockSetId;
                        getter.BlockPropertyId.Us = blockPropertyId;
                        getter.PotentiallyAffectedBlocks.Clear();
                        foreach (Block block in affectedBlocks)
                            getter.PotentiallyAffectedBlocks.Add(block);
                    };
                    verifySpecific = () =>
                        getter.BlockType == blockType &&
                        string.Equals(getter.BlockTypeName.Us, blockTypeName, StringComparison.Ordinal) &&
                        string.Equals(getter.BlockFilter.Us, blockFilter, StringComparison.Ordinal) &&
                        getter.ReadableAttributeId.Us == readableAttributeId &&
                        getter.BlockSetId.Us == blockSetId &&
                        getter.BlockPropertyId.Us == blockPropertyId &&
                        getter.PotentiallyAffectedBlocks.SequenceEqual(affectedBlocks);
                }
                else if (component is GenericBlockSetter setter)
                {
                    Type blockType = setter.BlockType;
                    string blockTypeName = setter.BlockTypeName.Us;
                    string blockFilter = setter.BlockFilter.Us;
                    uint blockSetId = setter.BlockSetId.Us;
                    uint blockPropertyId = setter.BlockPropertyId.Us;
                    var affectedBlocks = setter.PotentiallyAffectedBlocks.ToList();
                    restoreSpecific = () =>
                    {
                        setter.BlockType = blockType;
                        setter.BlockTypeName.Us = blockTypeName;
                        setter.BlockFilter.Us = blockFilter;
                        setter.BlockSetId.Us = blockSetId;
                        setter.BlockPropertyId.Us = blockPropertyId;
                        setter.PotentiallyAffectedBlocks.Clear();
                        foreach (BlockStub block in affectedBlocks)
                            setter.PotentiallyAffectedBlocks.Add(block);
                    };
                    verifySpecific = () =>
                        setter.BlockType == blockType &&
                        string.Equals(setter.BlockTypeName.Us, blockTypeName, StringComparison.Ordinal) &&
                        string.Equals(setter.BlockFilter.Us, blockFilter, StringComparison.Ordinal) &&
                        setter.BlockSetId.Us == blockSetId &&
                        setter.BlockPropertyId.Us == blockPropertyId &&
                        setter.PotentiallyAffectedBlocks.SequenceEqual(affectedBlocks);
                }
                else if (component is Evaluator evaluator)
                {
                    string expression = evaluator.Expression.Us;
                    restoreSpecific = () => evaluator.Expression.Us = expression;
                    verifySpecific = () => string.Equals(
                        evaluator.Expression.Us,
                        expression,
                        StringComparison.Ordinal);
                }
                else if (component is NativeSwitch switchComponent)
                {
                    float threshold = switchComponent.Threshold.Us;
                    float failValue = switchComponent.FailValue.Us;
                    restoreSpecific = () =>
                    {
                        switchComponent.Threshold.Us = threshold;
                        switchComponent.FailValue.Us = failValue;
                    };
                    verifySpecific = () =>
                        switchComponent.Threshold.Us == threshold &&
                        switchComponent.FailValue.Us == failValue;
                }
                else if (component is LogicGate logicGate)
                {
                    GateType gate = logicGate.SelectedGate.Us;
                    TrueType trueLogic = logicGate.TrueLogic.Us;
                    restoreSpecific = () =>
                    {
                        logicGate.SelectedGate.Us = gate;
                        logicGate.TrueLogic.Us = trueLogic;
                    };
                    verifySpecific = () =>
                        logicGate.SelectedGate.Us == gate &&
                        logicGate.TrueLogic.Us == trueLogic;
                }
                else if (component is FuzzyThreshold fuzzyThreshold)
                {
                    bool above = fuzzyThreshold.Above.Us;
                    Vector2 limits = fuzzyThreshold.ThresholdLimits.Us;
                    restoreSpecific = () =>
                    {
                        fuzzyThreshold.Above.Us = above;
                        fuzzyThreshold.ThresholdLimits.Us = limits;
                    };
                    verifySpecific = () =>
                        fuzzyThreshold.Above.Us == above &&
                        fuzzyThreshold.ThresholdLimits.Us == limits;
                }
                else if (component is MaxMin maxMin)
                {
                    OpType operation = maxMin.SelectedOp.Us;
                    restoreSpecific = () => maxMin.SelectedOp.Us = operation;
                    verifySpecific = () => maxMin.SelectedOp.Us == operation;
                }
                else if (component is ConstantInput constant)
                {
                    ConstantInput.ConstantType type = constant.Type.Us;
                    float value = constant.InputValue.Us;
                    restoreSpecific = () =>
                    {
                        constant.Type.Us = type;
                        constant.InputValue.Us = value;
                    };
                    verifySpecific = () =>
                        constant.Type.Us == type &&
                        constant.InputValue.Us == value;
                }
                else if (component is RandomInput random)
                {
                    Vector2 limits = random.RandomLimits.Us;
                    restoreSpecific = () => random.RandomLimits.Us = limits;
                    verifySpecific = () => random.RandomLimits.Us == limits;
                }
                else if (component is Clamp clamp)
                {
                    Vector2 limits = clamp.MinMax.Us;
                    restoreSpecific = () => clamp.MinMax.Us = limits;
                    verifySpecific = () => clamp.MinMax.Us == limits;
                }
                else if (component is Delay delay)
                {
                    float seconds = delay.DelayTime.Us;
                    restoreSpecific = () => delay.DelayTime.Us = seconds;
                    verifySpecific = () => delay.DelayTime.Us == seconds;
                }
                else if (component is NativeComment comment)
                {
                    string text = comment.InputValue.Us;
                    bool clipText = comment.ClipText.Us;
                    bool scaleWithZoom = comment.ScaleWithZoom.Us;
                    restoreSpecific = () =>
                    {
                        comment.InputValue.Us = text;
                        comment.ClipText.Us = clipText;
                        comment.ScaleWithZoom.Us = scaleWithZoom;
                    };
                    verifySpecific = () => string.Equals(
                                              comment.InputValue.Us,
                                              text,
                                              StringComparison.Ordinal) &&
                                         comment.ClipText.Us == clipText &&
                                         comment.ScaleWithZoom.Us == scaleWithZoom;
                }

                return new NativeComponentEditState(
                    component,
                    () =>
                    {
                        component.X.Us = x;
                        component.Y.Us = y;
                        component.Width.Us = width;
                        component.Height.Us = height;
                        component.OutlineColor.Us = outlineColor;
                        restoreSpecific();
                    },
                    () =>
                        component.X.Us == x &&
                        component.Y.Us == y &&
                        component.Width.Us == width &&
                        component.Height.Us == height &&
                        component.OutlineColor.Us == outlineColor &&
                        verifySpecific());
            }

            internal bool TryRestore()
            {
                if (Component == null || _restore == null || _verify == null)
                    return false;

                try
                {
                    _restore();
                    return _verify();
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class NativeBlockNameChange
        {
            internal NativeBlockNameChange(
                Block block,
                string previousName,
                string appliedName)
            {
                Block = block;
                PreviousName = previousName ?? string.Empty;
                AppliedName = appliedName ?? string.Empty;
            }

            internal Block Block { get; }

            internal string PreviousName { get; }

            internal string AppliedName { get; }

            internal bool RestoreIfUnchanged()
            {
                if (Block == null)
                    return false;

                string current = CurrentCustomName(Block);
                if (!string.Equals(current, AppliedName, StringComparison.Ordinal))
                    return string.Equals(current, PreviousName, StringComparison.Ordinal);

                try
                {
                    Block.IdSet.Name.Us = PreviousName;
                    return string.Equals(
                        CurrentCustomName(Block),
                        PreviousName,
                        StringComparison.Ordinal);
                }
                catch
                {
                    // Rollback continues for graph/components even if vanilla
                    // rejects restoring a changed target name.
                    return false;
                }
            }
        }

        private readonly struct NativeApplyResult
        {
            internal NativeApplyResult(
                string message,
                EsuHudNotificationKind kind,
                string detail = null)
            {
                Message = message;
                Kind = kind;
                Detail = detail;
            }

            internal string Message { get; }

            internal EsuHudNotificationKind Kind { get; }

            internal string Detail { get; }

            internal static NativeApplyResult Warning(
                string message,
                string detail = null) =>
                new NativeApplyResult(message, EsuHudNotificationKind.Warning, detail);
        }

        private sealed class NativePlan
        {
            internal List<string> Errors { get; } = new List<string>();

            internal List<string> Warnings { get; } = new List<string>();

            internal List<string> Creates { get; } = new List<string>();

            internal List<string> Updates { get; } = new List<string>();

            internal List<string> Removes { get; } = new List<string>();

            internal bool HasErrors => Errors.Count > 0;

            internal string Summary =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:N0} error{1}, {2:N0} warning{3}, {4:N0} create, {5:N0} update, {6:N0} remove.",
                    Errors.Count,
                    Errors.Count == 1 ? string.Empty : "s",
                    Warnings.Count,
                    Warnings.Count == 1 ? string.Empty : "s",
                    Creates.Count,
                    Updates.Count,
                    Removes.Count);

            internal string Detail => string.Join("\n", DetailLines().ToArray());

            internal void AddError(string text) => AddUnique(Errors, text);

            internal void AddWarning(string text) => AddUnique(Warnings, text);

            internal void AddCreate(string text) => AddUnique(Creates, text);

            internal void AddUpdate(string text) => AddUnique(Updates, text);

            internal void AddRemove(string text) => AddUnique(Removes, text);

            internal IEnumerable<string> DetailLines()
            {
                yield return "Errors";
                foreach (string line in SectionLines(Errors))
                    yield return line;
                yield return "Warnings";
                foreach (string line in SectionLines(Warnings))
                    yield return line;
                yield return "Create";
                foreach (string line in SectionLines(Creates))
                    yield return line;
                yield return "Update";
                foreach (string line in SectionLines(Updates))
                    yield return line;
                yield return "Remove";
                foreach (string line in SectionLines(Removes))
                    yield return line;
            }

            private static IEnumerable<string> SectionLines(IReadOnlyList<string> lines)
            {
                if (lines == null || lines.Count == 0)
                {
                    yield return "  (none)";
                    yield break;
                }

                foreach (string line in lines)
                    yield return "  - " + line;
            }

            private static void AddUnique(
                ICollection<string> lines,
                string text)
            {
                if (lines == null || string.IsNullOrWhiteSpace(text))
                    return;

                string trimmed = text.Trim();
                if (!lines.Contains(trimmed))
                    lines.Add(trimmed);
            }
        }
    }

    internal static class AutomationBuilderReflectionExtensions
    {
        internal static IEnumerable<Type> GetTypesSafe(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
