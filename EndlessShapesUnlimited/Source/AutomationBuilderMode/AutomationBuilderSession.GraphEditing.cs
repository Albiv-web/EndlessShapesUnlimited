using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;
using CircuitComponent = BrilliantSkies.Common.Circuits.Component;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed partial class AutomationBuilderSession
    {
        private const int GraphEditHistoryLimit = 64;
        private readonly List<AutomationGraphEditSnapshot> _graphEditHistory =
            new List<AutomationGraphEditSnapshot>();
        private int _graphEditHistoryIndex = -1;
        private bool _restoringGraphEditHistory;
        private AutomationGraphClipboard _graphClipboard;
        private string _graphPasteCascadeKey = string.Empty;
        private int _graphPasteCascadeOrdinal;

        private bool CanUndoGraphEdit => _graphEditHistoryIndex > 0;

        private bool CanRedoGraphEdit => _graphEditHistoryIndex >= 0 &&
                                         _graphEditHistoryIndex + 1 < _graphEditHistory.Count;

        private void ResetGraphEditHistory()
        {
            _graphEditHistory.Clear();
            _graphEditHistoryIndex = -1;
            AutomationGraph graph = CurrentSelectedGraph();
            if (graph == null)
                return;

            _graphEditHistory.Add(CaptureGraphEditSnapshot(graph, _automationDirty));
            _graphEditHistoryIndex = 0;
        }

        private void RecordGraphEditHistoryState()
        {
            if (_restoringGraphEditHistory || !_canvasOpen)
                return;

            AutomationGraph graph = CurrentSelectedGraph();
            if (graph == null)
                return;

            if (_graphEditHistoryIndex < 0 ||
                _graphEditHistory.Count == 0 ||
                !string.Equals(
                    _graphEditHistory[_graphEditHistoryIndex].GraphKey,
                    graph.Key,
                    StringComparison.Ordinal))
            {
                ResetGraphEditHistory();
            }

            var next = CaptureGraphEditSnapshot(graph, dirty: true);
            if (_graphEditHistoryIndex >= 0 &&
                string.Equals(
                    _graphEditHistory[_graphEditHistoryIndex].Signature,
                    next.Signature,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (_graphEditHistoryIndex + 1 < _graphEditHistory.Count)
            {
                _graphEditHistory.RemoveRange(
                    _graphEditHistoryIndex + 1,
                    _graphEditHistory.Count - _graphEditHistoryIndex - 1);
            }

            _graphEditHistory.Add(next);
            if (_graphEditHistory.Count > GraphEditHistoryLimit)
                _graphEditHistory.RemoveAt(0);
            _graphEditHistoryIndex = _graphEditHistory.Count - 1;
        }

        private bool UndoGraphEdit()
        {
            if (_graphEditHistoryIndex <= 0 || _graphEditHistory.Count == 0)
                return false;

            return RestoreGraphEditSnapshot(--_graphEditHistoryIndex, "Undo");
        }

        private bool RedoGraphEdit()
        {
            if (_graphEditHistoryIndex < 0 ||
                _graphEditHistoryIndex + 1 >= _graphEditHistory.Count)
            {
                return false;
            }

            return RestoreGraphEditSnapshot(++_graphEditHistoryIndex, "Redo");
        }

        private bool RestoreGraphEditSnapshot(
            int index,
            string action)
        {
            if (index < 0 || index >= _graphEditHistory.Count)
                return false;

            AutomationGraph graph = CurrentSelectedGraph();
            AutomationGraphEditSnapshot snapshot = _graphEditHistory[index];
            if (graph == null ||
                !string.Equals(graph.Key, snapshot.GraphKey, StringComparison.Ordinal))
            {
                return false;
            }

            CancelActiveCanvasInteraction(graph, restoreNode: true);
            CloseGraphSlotMenu();
            CloseGraphContextMenu();
            CloseGraphReadinessPopover();
            CloseGraphPropertyPicker();
            _restoringGraphEditHistory = true;
            try
            {
                snapshot.Restore(graph);
                RestoreSnapshotStagedLinks(snapshot);
                RestoreSnapshotPendingNativeState(graph, snapshot);
                _automationDirty = snapshot.Dirty;
                InvalidateAutomationDisplayCache();
            }
            finally
            {
                _restoringGraphEditHistory = false;
            }

            InfoStore.Add("Automation blocks: " + action + ".");
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            return true;
        }

        private AutomationGraphEditSnapshot CaptureGraphEditSnapshot(
            AutomationGraph graph,
            bool dirty)
        {
            return new AutomationGraphEditSnapshot(
                graph,
                _links.Where(link => link?.IsStaged == true),
                _pendingNativeNodeDrafts.Keys,
                _pendingNativeNodeRects.Keys,
                _pendingNativeNodeRemovals,
                dirty);
        }

        private void RestoreSnapshotStagedLinks(AutomationGraphEditSnapshot snapshot)
        {
            List<AutomationLink> nativeLinks = _links
                .Where(link => link?.IsStaged != true)
                .ToList();
            _links.Clear();
            _links.AddRange(nativeLinks);
            _links.AddRange(snapshot.StagedLinks.Select(link => link.Clone()));
            _selectedLink = _selectedLink?.IsStaged == true
                ? _links.FirstOrDefault(link => link.Id == _selectedLink.Id && link.IsStaged)
                : _selectedLink;
        }

        private void RestoreSnapshotPendingNativeState(
            AutomationGraph graph,
            AutomationGraphEditSnapshot snapshot,
            IReadOnlyDictionary<uint, uint> componentIdRemap = null)
        {
            _pendingNativeNodeDrafts.Clear();
            _pendingNativeNodeRects.Clear();
            foreach (uint componentId in snapshot.PendingDraftIds)
            {
                uint restoredComponentId = ResolveRestoredNativeComponentId(
                    componentId,
                    componentIdRemap,
                    snapshot.RemapNativeComponentId(componentId));
                AutomationGraphNode node = graph.Nodes.FirstOrDefault(candidate =>
                    TryGetNativeComponentId(candidate, out uint id) && id == restoredComponentId);
                if (node != null)
                    _pendingNativeNodeDrafts[restoredComponentId] = new AutomationGraphNodeDraft(node);
            }

            foreach (uint componentId in snapshot.PendingRectIds)
            {
                uint restoredComponentId = ResolveRestoredNativeComponentId(
                    componentId,
                    componentIdRemap,
                    snapshot.RemapNativeComponentId(componentId));
                AutomationGraphNode node = graph.Nodes.FirstOrDefault(candidate =>
                    TryGetNativeComponentId(candidate, out uint id) && id == restoredComponentId);
                if (node != null)
                    _pendingNativeNodeRects[restoredComponentId] = node.Rect;
            }

            _pendingNativeNodeRemovals.Clear();
            foreach (uint componentId in snapshot.PendingRemovalIds)
            {
                _pendingNativeNodeRemovals.Add(ResolveRestoredNativeComponentId(
                    componentId,
                    componentIdRemap,
                    snapshot.RemapNativeComponentId(componentId)));
            }
        }

        internal static uint ResolveRestoredNativeComponentId(
            uint capturedComponentId,
            IReadOnlyDictionary<uint, uint> componentIdRemap,
            uint snapshotComponentId)
        {
            return componentIdRemap != null &&
                   componentIdRemap.TryGetValue(capturedComponentId, out uint restoredComponentId)
                ? restoredComponentId
                : snapshotComponentId;
        }

        private bool CopySelectedGraphStack()
        {
            AutomationGraph graph = CurrentSelectedGraph();
            AutomationGraphNode selected = graph?.SelectedNode;
            if (selected == null || !IsGraphNodeApplyWritable(selected))
                return false;

            List<AutomationGraphNode> nodes = CollectGraphDragGroup(graph, selected)
                .Where(IsGraphNodeApplyWritable)
                .ToList();
            if (nodes.Count == 0)
                return false;

            _graphClipboard = AutomationGraphClipboard.Capture(graph, nodes, selected.Id);
            InfoStore.Add(
                "Automation blocks copied " +
                nodes.Count.ToString("N0", CultureInfo.InvariantCulture) +
                (nodes.Count == 1 ? " block." : " blocks."));
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            return true;
        }

        private bool PasteGraphStack()
        {
            AutomationGraph graph = CurrentSelectedGraph();
            if (graph == null || _graphClipboard == null || _graphClipboard.Nodes.Count == 0)
                return false;

            Rect target = VisibleWorkspaceDropGraphRect(_graphClipboard.Nodes[0].Kind);
            Vector2 cascade = NextGraphPasteCascadeOffset(
                graph.Key,
                EsuHudLayout.Scale(24f));
            Vector2 delta = target.position - _graphClipboard.Origin +
                            new Vector2(EsuHudLayout.Scale(18f), EsuHudLayout.Scale(18f)) +
                            cascade;
            Dictionary<int, AutomationGraphNode> pasted = _graphClipboard.InstantiateNodes(
                graph,
                delta,
                _selectedBreadboard);
            if (pasted.Count == 0)
                return false;

            List<AutomationGraphNode> pastedNodes = pasted.Values.ToList();
            AutomationGraphLayout.AvoidOverlap(
                graph,
                pastedNodes,
                allowedOverlap: null);
            _graphClipboard.AppendConnections(graph, pasted);
            var pastedNodeIds = new HashSet<int>(pasted.Values.Select(node => node.Id));
            RefreshGraphConnections(
                graph,
                touchedNodeIds: pastedNodeIds);
            AutomationGraphLayout.ExpandControlBodiesForChildren(graph, IsGraphNodeApplyWritable);
            SyncGraphNodeRects(graph);
            if (pasted.TryGetValue(_graphClipboard.RootNodeId, out AutomationGraphNode root))
                graph.SelectedNodeId = root.Id;
            else
                graph.SelectedNodeId = pasted.Values.First().Id;
            MarkAutomationDirty();
            InfoStore.Add(
                "Automation blocks pasted " +
                pasted.Count.ToString("N0", CultureInfo.InvariantCulture) +
                (pasted.Count == 1 ? " block." : " blocks."));
            return true;
        }

        private Vector2 NextGraphPasteCascadeOffset(
            string graphKey,
            float step)
        {
            string normalizedKey = graphKey ?? string.Empty;
            if (!string.Equals(_graphPasteCascadeKey, normalizedKey, StringComparison.Ordinal))
            {
                _graphPasteCascadeKey = normalizedKey;
                _graphPasteCascadeOrdinal = 0;
            }

            Vector2 offset = ResolveGraphPasteCascadeOffset(
                _graphPasteCascadeOrdinal,
                step);
            if (_graphPasteCascadeOrdinal < int.MaxValue)
                _graphPasteCascadeOrdinal++;
            return offset;
        }

        internal static Vector2 ResolveGraphPasteCascadeOffset(
            int pasteOrdinal,
            float step)
        {
            int ordinal = Math.Max(0, pasteOrdinal);
            float safeStep = float.IsNaN(step) || float.IsInfinity(step)
                ? 0f
                : Math.Max(0f, step);
            return new Vector2(ordinal * safeStep, ordinal * safeStep);
        }

        internal static bool ClipboardTargetBindingCompatible(
            string sourceGraphKey,
            string destinationGraphKey,
            bool sourceResolved,
            bool targetResolved,
            bool sourceOnDestinationConstruct,
            bool targetOnDestinationConstruct)
        {
            if (!sourceResolved ||
                !targetResolved ||
                !sourceOnDestinationConstruct ||
                !targetOnDestinationConstruct)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(sourceGraphKey) &&
                   !string.IsNullOrWhiteSpace(destinationGraphKey);
        }

        internal static bool ClipboardTargetBindingNeedsBreadboardRebind(
            string sourceGraphKey,
            string destinationGraphKey)
        {
            return !string.Equals(
                sourceGraphKey ?? string.Empty,
                destinationGraphKey ?? string.Empty,
                StringComparison.Ordinal);
        }

        private bool DuplicateSelectedGraphStack()
        {
            if (!CopySelectedGraphStack())
                return false;
            return PasteGraphStack();
        }

        private bool HandleGraphEditKeyboard()
        {
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (control && Input.GetKeyDown(KeyCode.Z))
            {
                bool handled = shift ? RedoGraphEdit() : UndoGraphEdit();
                if (!handled)
                    InfoStore.Add(shift ? "Automation blocks: nothing to redo." : "Automation blocks: nothing to undo.");
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            if (control && Input.GetKeyDown(KeyCode.Y))
            {
                if (!RedoGraphEdit())
                    InfoStore.Add("Automation blocks: nothing to redo.");
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            if (control && Input.GetKeyDown(KeyCode.C))
            {
                if (!CopySelectedGraphStack())
                    InfoStore.Add("Select an editable Automation block before copying.");
                return true;
            }

            if (control && Input.GetKeyDown(KeyCode.V))
            {
                if (!PasteGraphStack())
                    InfoStore.Add("Copy an editable Automation block stack before pasting.");
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            if (control && Input.GetKeyDown(KeyCode.D))
            {
                if (!DuplicateSelectedGraphStack())
                    InfoStore.Add("Select an editable Automation block before duplicating.");
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                AutomationGraph graph = CurrentSelectedGraph();
                AutomationGraphNode node = graph?.SelectedNode;
                if (node != null && IsGraphNodeApplyWritable(node))
                {
                    RemoveGraphNode(graph, node);
                    MarkAutomationDirty();
                    InfoStore.Add("Automation block removed. Ctrl+Z restores it.");
                }
                else if (node != null)
                {
                    InfoStore.Add("That vanilla component is read-only in Automation Builder.");
                }
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            Vector2 nudge = Vector2.zero;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                nudge.x = -1f;
            else if (Input.GetKeyDown(KeyCode.RightArrow))
                nudge.x = 1f;
            else if (Input.GetKeyDown(KeyCode.UpArrow))
                nudge.y = -1f;
            else if (Input.GetKeyDown(KeyCode.DownArrow))
                nudge.y = 1f;
            if (nudge != Vector2.zero)
            {
                AutomationGraph graph = CurrentSelectedGraph();
                AutomationGraphNode node = graph?.SelectedNode;
                if (node != null && IsGraphNodeApplyWritable(node))
                {
                    float step = EsuHudLayout.Scale(shift ? 10f : 2f);
                    IReadOnlyList<AutomationGraphNode> group = CollectGraphDragGroup(graph, node)
                        .Where(IsGraphNodeApplyWritable)
                        .ToList();
                    AutomationGraphLayout.OffsetGroup(group, nudge * step);
                    foreach (AutomationGraphNode moved in group)
                        AutomationGraphLayout.NormalizeNode(moved);
                    AutomationGraphLayout.ExpandControlBodiesForChildren(graph, IsGraphNodeApplyWritable);
                    RefreshGraphConnections(
                        graph,
                        touchedNodeIds: new HashSet<int>(group.Select(moved => moved.Id)));
                    SyncGraphNodeRects(graph);
                    MarkAutomationDirty();
                }
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return true;
            }

            return false;
        }

        private sealed class AutomationGraphEditSnapshot
        {
            internal AutomationGraphEditSnapshot(
                AutomationGraph graph,
                IEnumerable<AutomationLink> stagedLinks,
                IEnumerable<uint> pendingDraftIds,
                IEnumerable<uint> pendingRectIds,
                IEnumerable<uint> pendingRemovalIds,
                bool dirty)
            {
                GraphKey = graph?.Key ?? string.Empty;
                Nodes = graph?.Nodes
                    .Where(node => node != null)
                    .Select(node => new AutomationGraphNodeCopy(node, Vector2.zero))
                    .ToList() ?? new List<AutomationGraphNodeCopy>();
                Connections = AutomationGraphConnectionCopy.Capture(graph?.Connections);
                ImportedConnections = AutomationGraphConnectionCopy.Capture(graph?.ImportedNativeConnections);
                StagedLinks = (stagedLinks ?? Enumerable.Empty<AutomationLink>())
                    .Where(link => link != null)
                    .Select(link => AutomationLinkCopy.Capture(link))
                    .ToList();
                PendingDraftIds = new HashSet<uint>(pendingDraftIds ?? Enumerable.Empty<uint>());
                PendingRectIds = new HashSet<uint>(pendingRectIds ?? Enumerable.Empty<uint>());
                PendingRemovalIds = new HashSet<uint>(pendingRemovalIds ?? Enumerable.Empty<uint>());
                SelectedNodeId = graph?.SelectedNodeId ?? 0;
                NextNodeId = graph?._nextNodeId ?? 1;
                NextStagedNodeId = graph?._nextStagedNodeId ?? -1;
                ConnectionsInitialized = graph?._connectionsInitialized == true;
                NativeSyncVersion = graph?.NativeSyncVersion ?? -1;
                Dirty = dirty;
                Signature = BuildSignature();
            }

            internal string GraphKey { get; }
            internal List<AutomationGraphNodeCopy> Nodes { get; }
            internal List<AutomationGraphConnectionCopy> Connections { get; }
            internal List<AutomationGraphConnectionCopy> ImportedConnections { get; }
            internal List<AutomationLinkCopy> StagedLinks { get; }
            internal HashSet<uint> PendingDraftIds { get; }
            internal HashSet<uint> PendingRectIds { get; }
            internal HashSet<uint> PendingRemovalIds { get; }
            internal int SelectedNodeId { get; }
            internal int NextNodeId { get; }
            internal int NextStagedNodeId { get; }
            internal bool ConnectionsInitialized { get; }
            internal int NativeSyncVersion { get; }
            internal bool Dirty { get; }
            internal string Signature { get; }

            internal void Restore(AutomationGraph graph)
            {
                var nodesByOldId = new Dictionary<int, AutomationGraphNode>();
                graph.Nodes.Clear();
                foreach (AutomationGraphNodeCopy copy in Nodes)
                {
                    AutomationGraphNode node = copy.Instantiate(copy.Rect.position, preserveId: true);
                    graph.Nodes.Add(node);
                    nodesByOldId[copy.SourceId] = node;
                }

                graph.Connections.Clear();
                graph.Connections.AddRange(Connections
                    .Select(copy => copy.Instantiate(nodesByOldId))
                    .Where(connection => connection != null));
                graph.ImportedNativeConnections.Clear();
                graph.ImportedNativeConnections.AddRange(ImportedConnections
                    .Select(copy => copy.Instantiate(nodesByOldId))
                    .Where(connection => connection != null));
                graph.SelectedNodeId = nodesByOldId.TryGetValue(
                    SelectedNodeId,
                    out AutomationGraphNode selected)
                    ? selected.Id
                    : SelectedNodeId;
                graph._nextNodeId = NextNodeId;
                graph._nextStagedNodeId = NextStagedNodeId;
                graph._connectionsInitialized = ConnectionsInitialized;
                graph.NativeSyncVersion = NativeSyncVersion;
            }

            internal uint RemapNativeComponentId(uint capturedComponentId)
            {
                AutomationGraphNodeCopy copy = Nodes.FirstOrDefault(candidate =>
                    candidate?.CapturedNativeComponentId == capturedComponentId);
                return copy?.NativeComponent is CircuitComponent component
                    ? component.UniqueId
                    : capturedComponentId;
            }

            private string BuildSignature()
            {
                return string.Join(";", Nodes.Select(node => node.Signature)) +
                       "|" + string.Join(";", Connections.Select(connection => connection.Signature)) +
                       "|" + string.Join(";", ImportedConnections.Select(connection => connection.Signature)) +
                       "|" + string.Join(";", StagedLinks.Select(link => link.Signature)) +
                       "|d=" + string.Join(",", PendingDraftIds.OrderBy(id => id)) +
                       "|x=" + string.Join(",", PendingRectIds.OrderBy(id => id)) +
                       "|r=" + string.Join(",", PendingRemovalIds.OrderBy(id => id)) +
                       "|s=" + SelectedNodeId.ToString(CultureInfo.InvariantCulture);
            }
        }

        private sealed class AutomationGraphClipboard
        {
            private AutomationGraphClipboard(
                List<AutomationGraphNodeCopy> nodes,
                List<AutomationGraphConnectionCopy> connections,
                int rootNodeId,
                Vector2 origin,
                string sourceGraphKey)
            {
                Nodes = nodes;
                Connections = connections;
                RootNodeId = rootNodeId;
                Origin = origin;
                SourceGraphKey = sourceGraphKey ?? string.Empty;
            }

            internal List<AutomationGraphNodeCopy> Nodes { get; }
            internal List<AutomationGraphConnectionCopy> Connections { get; }
            internal int RootNodeId { get; }
            internal Vector2 Origin { get; }
            internal string SourceGraphKey { get; }

            internal static AutomationGraphClipboard Capture(
                AutomationGraph graph,
                IReadOnlyList<AutomationGraphNode> nodes,
                int rootNodeId)
            {
                var ids = new HashSet<int>(nodes.Select(node => node.Id));
                float minX = nodes.Min(node => node.Rect.xMin);
                float minY = nodes.Min(node => node.Rect.yMin);
                Vector2 origin = new Vector2(minX, minY);
                return new AutomationGraphClipboard(
                    nodes.Select(node => new AutomationGraphNodeCopy(node, -origin)).ToList(),
                    AutomationGraphConnectionCopy.Capture(graph.Connections
                        .Where(connection => ids.Contains(connection.FromNodeId) &&
                                             ids.Contains(connection.ToNodeId))),
                    rootNodeId,
                    Vector2.zero,
                    graph?.Key);
            }

            internal Dictionary<int, AutomationGraphNode> InstantiateNodes(
                AutomationGraph graph,
                Vector2 delta,
                AutomationBlockRef destinationBreadboard)
            {
                var map = new Dictionary<int, AutomationGraphNode>();
                foreach (AutomationGraphNodeCopy copy in Nodes)
                {
                    Rect rect = copy.Rect;
                    rect.position += delta;
                    AutomationGraphNode node = graph.AddStagedNode(copy.Kind, rect);
                    copy.Apply(
                        node,
                        ResolveTargetBinding(
                            copy.TargetBinding,
                            graph,
                            destinationBreadboard));
                    node.Rect = rect;
                    map[copy.SourceId] = node;
                }

                return map;
            }

            internal void AppendConnections(
                AutomationGraph graph,
                IReadOnlyDictionary<int, AutomationGraphNode> map)
            {
                if (graph == null || map == null || map.Count == 0)
                    return;

                List<AutomationGraphConnection> connections = graph.Connections.ToList();
                connections.AddRange(Connections
                    .Select(copy => copy.Instantiate(map))
                    .Where(connection => connection != null));
                graph.RebuildConnections(connections);
            }

            private AutomationGraphTargetBinding ResolveTargetBinding(
                AutomationGraphTargetBinding binding,
                AutomationGraph destinationGraph,
                AutomationBlockRef destinationBreadboard)
            {
                if (binding == null ||
                    destinationGraph == null ||
                    destinationBreadboard?.Construct == null)
                {
                    return null;
                }

                bool sourceResolved = binding.Source?.TryGetBlock(out _) == true;
                bool targetResolved = binding.Target?.TryGetBlock(out _) == true;
                bool sourceOnDestinationConstruct =
                    ReferenceEquals(binding.Source?.Construct, destinationBreadboard.Construct);
                bool targetOnDestinationConstruct =
                    ReferenceEquals(binding.Target?.Construct, destinationBreadboard.Construct);
                if (!ClipboardTargetBindingCompatible(
                        SourceGraphKey,
                        destinationGraph.Key,
                        sourceResolved,
                        targetResolved,
                        sourceOnDestinationConstruct,
                        targetOnDestinationConstruct))
                {
                    return null;
                }

                return ClipboardTargetBindingNeedsBreadboardRebind(
                    SourceGraphKey,
                    destinationGraph.Key)
                    ? binding.CloneForBreadboard(destinationBreadboard)
                    : binding.Clone();
            }
        }

        private sealed class AutomationGraphNodeCopy
        {
            internal AutomationGraphNodeCopy(
                AutomationGraphNode node,
                Vector2 offset)
            {
                SourceId = node.Id;
                Kind = node.Kind;
                Rect = node.Rect;
                Rect rect = Rect;
                rect.position += offset;
                Rect = rect;
                Label = node.Label;
                Property = node.Property;
                ValueText = node.ValueText;
                NativeComponent = node.NativeComponent;
                CapturedNativeComponentId = node.NativeComponent is CircuitComponent component
                    ? (uint?)component.UniqueId
                    : null;
                TargetBinding = node.TargetBinding?.Clone();
            }

            internal int SourceId { get; }
            internal AutomationNodeKind Kind { get; }
            internal Rect Rect { get; private set; }
            internal string Label { get; }
            internal string Property { get; }
            internal string ValueText { get; }
            internal object NativeComponent { get; }
            internal uint? CapturedNativeComponentId { get; }
            internal AutomationGraphTargetBinding TargetBinding { get; }
            internal string Signature =>
                SourceId.ToString(CultureInfo.InvariantCulture) + ":" + Kind + ":" +
                Rect.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                Rect.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                Rect.width.ToString("R", CultureInfo.InvariantCulture) + "," +
                Rect.height.ToString("R", CultureInfo.InvariantCulture) + ":" +
                Label + ":" + Property + ":" + ValueText + ":" +
                (TargetBinding?.Kind.ToString() ?? string.Empty) + ":" +
                (TargetBinding?.Source?.StableKey ?? string.Empty) + ">" +
                (TargetBinding?.Target?.StableKey ?? string.Empty) + ":" +
                (TargetBinding?.Property ?? string.Empty);

            internal AutomationGraphNode Instantiate(
                Vector2 position,
                bool preserveId)
            {
                Rect rect = Rect;
                rect.position = position;
                int restoredId = preserveId && NativeComponent is CircuitComponent component
                    ? NativeGraphNodeId(component)
                    : preserveId
                        ? SourceId
                        : 0;
                var node = new AutomationGraphNode(
                    restoredId,
                    Kind,
                    rect,
                    Label,
                    Property,
                    ValueText,
                    NativeComponent);
                node.BindTarget(TargetBinding);
                return node;
            }

            internal void Apply(
                AutomationGraphNode node,
                AutomationGraphTargetBinding targetBinding)
            {
                if (node == null)
                    return;
                node.Label = Label;
                node.Property = NormalizeNodeProperty(node.Kind, Property);
                node.ValueText = ValueText;
                node.BindTarget(targetBinding);
            }
        }

        private sealed class AutomationGraphConnectionCopy
        {
            private AutomationGraphConnectionCopy(AutomationGraphConnection connection)
            {
                Kind = connection.Kind;
                FromNodeId = connection.FromNodeId;
                ToNodeId = connection.ToNodeId;
                SlotKind = connection.SlotKind;
                Origin = connection.Origin;
                NativeInputIndex = connection.NativeInputIndex;
            }

            internal AutomationGraphConnectionKind Kind { get; }
            internal int FromNodeId { get; }
            internal int ToNodeId { get; }
            internal AutomationValueSlotKind SlotKind { get; }
            internal AutomationGraphWireOrigin Origin { get; }
            internal int NativeInputIndex { get; }
            internal string Signature => Kind + ":" + FromNodeId + ">" + ToNodeId + ":" + SlotKind + ":" + Origin + ":" + NativeInputIndex;

            internal static List<AutomationGraphConnectionCopy> Capture(
                IEnumerable<AutomationGraphConnection> connections)
            {
                return (connections ?? Enumerable.Empty<AutomationGraphConnection>())
                    .Where(connection => connection?.From != null && connection.To != null)
                    .Select(connection => new AutomationGraphConnectionCopy(connection))
                    .ToList();
            }

            internal AutomationGraphConnection Instantiate(
                IReadOnlyDictionary<int, AutomationGraphNode> nodesByOldId)
            {
                if (nodesByOldId == null ||
                    !nodesByOldId.TryGetValue(FromNodeId, out AutomationGraphNode from) ||
                    !nodesByOldId.TryGetValue(ToNodeId, out AutomationGraphNode to))
                {
                    return null;
                }

                return new AutomationGraphConnection(
                    Kind,
                    from,
                    to,
                    SlotKind,
                    Origin,
                    nativeInputIndex: NativeInputIndex);
            }
        }

        private sealed class AutomationLinkCopy
        {
            private AutomationLinkCopy(AutomationLink link)
            {
                Id = link.Id;
                Source = link.Source;
                Target = link.Target;
                Kind = link.Kind;
                Property = link.Property;
                Color = link.Color;
            }

            internal int Id { get; }
            internal AutomationBlockRef Source { get; }
            internal AutomationBlockRef Target { get; }
            internal AutomationLinkKind Kind { get; }
            internal string Property { get; }
            internal Color Color { get; }
            internal string Signature => Id + ":" + Kind + ":" + Source?.StableKey + ">" + Target?.StableKey + ":" + Property;

            internal static AutomationLinkCopy Capture(AutomationLink link) =>
                new AutomationLinkCopy(link);

            internal AutomationLink Clone() =>
                new AutomationLink(Id, Source, Target, Kind, Property, Color);
        }
    }
}
