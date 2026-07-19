using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// Immutable scene state used for recovery and for constructing compact history deltas.
    /// Node payloads own their clones, so a later editor mutation cannot alter history.
    /// </summary>
    internal sealed class SmartBuildSceneState
    {
        internal SmartBuildSceneState(
            AllConstruct construct,
            IEnumerable<SmartBuildSceneNodeState> nodes,
            IEnumerable<int> selectedNodeIds,
            int primaryNodeId)
        {
            Construct = construct;
            Nodes = (nodes ?? Array.Empty<SmartBuildSceneNodeState>())
                .Where(node => node != null)
                .ToArray();
            SelectedNodeIds = (selectedNodeIds ?? Array.Empty<int>())
                .Distinct()
                .ToArray();
            PrimaryNodeId = primaryNodeId;
        }

        internal AllConstruct Construct { get; }

        internal IReadOnlyList<SmartBuildSceneNodeState> Nodes { get; }

        internal IReadOnlyList<int> SelectedNodeIds { get; }

        internal int PrimaryNodeId { get; }

        internal SmartBuildSceneState Apply(SmartBuildSceneStateDelta delta, bool forward)
        {
            if (delta == null)
                return this;

            var live = Nodes.ToDictionary(node => node.Id);
            IReadOnlyDictionary<int, SmartBuildSceneNodeState> remove = forward
                ? delta.BeforeNodes
                : delta.AfterNodes;
            IReadOnlyDictionary<int, SmartBuildSceneNodeState> add = forward
                ? delta.AfterNodes
                : delta.BeforeNodes;
            foreach (int id in remove.Keys)
                live.Remove(id);
            foreach (KeyValuePair<int, SmartBuildSceneNodeState> pair in add)
                live[pair.Key] = pair.Value;

            IReadOnlyList<int> order = forward ? delta.AfterOrder : delta.BeforeOrder;
            SmartBuildSceneNodeState[] ordered = order
                .Where(live.ContainsKey)
                .Select(id => live[id])
                .ToArray();
            IReadOnlyList<int> selected = forward
                ? delta.AfterSelectedNodeIds
                : delta.BeforeSelectedNodeIds;
            int primary = forward ? delta.AfterPrimaryNodeId : delta.BeforePrimaryNodeId;
            return new SmartBuildSceneState(
                forward ? delta.AfterConstruct : delta.BeforeConstruct,
                ordered,
                selected,
                primary);
        }
    }

    internal sealed class SmartBuildSceneNodeState
    {
        internal SmartBuildSceneNodeState(SmartBuildPiece primitive)
        {
            Kind = SmartBuildSceneNodeKind.Primitive;
            HostPiece = primitive?.Clone() ?? throw new ArgumentNullException(nameof(primitive));
            SourcePieces = Array.Empty<SmartBuildPiece>();
            RegionSpans = Array.Empty<SmartBuildRegionSpan>();
        }

        internal SmartBuildSceneNodeState(
            SmartBuildPiece host,
            IEnumerable<SmartBuildPiece> sources,
            SmartBuildPatternDefinition definition)
        {
            Kind = SmartBuildSceneNodeKind.Pattern;
            HostPiece = host?.Clone() ?? throw new ArgumentNullException(nameof(host));
            SourcePieces = (sources ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece != null)
                .Select(piece => piece.Clone())
                .ToArray();
            PatternDefinition = definition?.Clone() ??
                                throw new ArgumentNullException(nameof(definition));
            RegionSpans = Array.Empty<SmartBuildRegionSpan>();
        }

        internal SmartBuildSceneNodeState(
            SmartBuildPiece host,
            SmartBuildRegionKind regionKind,
            IEnumerable<SmartBuildRegionSpan> spans)
        {
            Kind = SmartBuildSceneNodeKind.Region;
            HostPiece = host?.Clone() ?? throw new ArgumentNullException(nameof(host));
            RegionKind = regionKind;
            RegionSpans = (spans ?? Array.Empty<SmartBuildRegionSpan>())
                .Select(span => new SmartBuildRegionSpan(span.Y, span.Z, span.StartX, span.Length))
                .ToArray();
            SourcePieces = Array.Empty<SmartBuildPiece>();
        }

        internal int Id => HostPiece.Id;

        internal SmartBuildSceneNodeKind Kind { get; }

        internal SmartBuildPiece HostPiece { get; }

        internal IReadOnlyList<SmartBuildPiece> SourcePieces { get; }

        internal SmartBuildPatternDefinition PatternDefinition { get; }

        internal SmartBuildRegionKind RegionKind { get; }

        internal IReadOnlyList<SmartBuildRegionSpan> RegionSpans { get; }

        internal IEnumerable<Vector3i> RegionCells =>
            RegionSpans.SelectMany(span => span.Cells());

        internal bool ContentEquals(SmartBuildSceneNodeState other)
        {
            if (other == null || Kind != other.Kind || !PieceEquals(HostPiece, other.HostPiece))
                return false;
            if (Kind == SmartBuildSceneNodeKind.Pattern)
            {
                return PatternEquals(PatternDefinition, other.PatternDefinition) &&
                       SequenceEqual(SourcePieces, other.SourcePieces);
            }
            if (Kind == SmartBuildSceneNodeKind.Region)
            {
                return RegionKind == other.RegionKind &&
                       RegionSpans.Count == other.RegionSpans.Count &&
                       RegionSpans.Zip(other.RegionSpans, SpanEquals).All(equal => equal);
            }
            return true;
        }

        private static bool SequenceEqual(
            IReadOnlyList<SmartBuildPiece> left,
            IReadOnlyList<SmartBuildPiece> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int index = 0; index < left.Count; index++)
                if (!PieceEquals(left[index], right[index]))
                    return false;
            return true;
        }

        private static bool SpanEquals(SmartBuildRegionSpan left, SmartBuildRegionSpan right) =>
            left.Y == right.Y && left.Z == right.Z &&
            left.StartX == right.StartX && left.Length == right.Length;

        private static bool PatternEquals(
            SmartBuildPatternDefinition left,
            SmartBuildPatternDefinition right)
        {
            if (left == null || right == null)
                return left == right;
            return left.Kind == right.Kind &&
                   left.PrimaryStep.Equals(right.PrimaryStep) &&
                   left.SecondaryStep.Equals(right.SecondaryStep) &&
                   left.PrimaryBefore == right.PrimaryBefore &&
                   left.PrimaryAfter == right.PrimaryAfter &&
                   left.SecondaryBefore == right.SecondaryBefore &&
                   left.SecondaryAfter == right.SecondaryAfter &&
                   left.RadialPivot.Equals(right.RadialPivot) &&
                   left.RadialAxis == right.RadialAxis &&
                   Math.Abs(left.RadialAngleStepDegrees - right.RadialAngleStepDegrees) <= 0.0001f &&
                   left.RadialOrientation == right.RadialOrientation &&
                   left.PathMode == right.PathMode &&
                   left.PathSpacingCells == right.PathSpacingCells &&
                   (left.PathPoints ?? Array.Empty<Vector3i>())
                       .SequenceEqual(right.PathPoints ?? Array.Empty<Vector3i>());
        }

        private static bool PieceEquals(SmartBuildPiece left, SmartBuildPiece right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.Id == right.Id &&
                   ReferenceEquals(left.Construct, right.Construct) &&
                   left.ShapeKind == right.ShapeKind &&
                   string.Equals(left.ShapeDescriptorKey, right.ShapeDescriptorKey, StringComparison.Ordinal) &&
                   left.Origin.Equals(right.Origin) &&
                   left.PresetCuboidSize.Equals(right.PresetCuboidSize) &&
                   left.DrawPlane == right.DrawPlane &&
                   left.SlopeLength == right.SlopeLength &&
                   left.SlopeSteps == right.SlopeSteps &&
                   left.SlopeWidth == right.SlopeWidth &&
                   left.SelectedLength == right.SelectedLength &&
                   left.FixedForwardTiles == right.FixedForwardTiles &&
                   left.FixedForwardCells == right.FixedForwardCells &&
                   left.FixedRightTiles == right.FixedRightTiles &&
                   left.FixedDropTiles == right.FixedDropTiles &&
                   left.SupportMode == right.SupportMode &&
                   left.GeneratorSides == right.GeneratorSides &&
                   left.GeneratorFillMode == right.GeneratorFillMode &&
                   left.GeneratorSmoothingMode == right.GeneratorSmoothingMode &&
                   left.GeneratorRoundLock == right.GeneratorRoundLock &&
                   left.GeneratorArcDegrees == right.GeneratorArcDegrees &&
                   left.GeneratorTopScalePercent == right.GeneratorTopScalePercent &&
                   left.CuboidHollow == right.CuboidHollow &&
                   left.MaterialOverride == right.MaterialOverride &&
                   left.ForwardAxis == right.ForwardAxis &&
                   left.ForwardSign == right.ForwardSign &&
                   left.RightAxis == right.RightAxis &&
                   left.RightSign == right.RightSign &&
                   left.DropAxis == right.DropAxis &&
                   left.DropSign == right.DropSign;
        }
    }

    /// <summary>
    /// Stored history representation. Only changed/added/removed nodes are retained;
    /// order and selection are small integer vectors.
    /// </summary>
    internal sealed class SmartBuildSceneStateDelta
    {
        private SmartBuildSceneStateDelta(
            SmartBuildSceneState before,
            SmartBuildSceneState after,
            IDictionary<int, SmartBuildSceneNodeState> beforeNodes,
            IDictionary<int, SmartBuildSceneNodeState> afterNodes)
        {
            BeforeConstruct = before.Construct;
            AfterConstruct = after.Construct;
            BeforeNodes = new Dictionary<int, SmartBuildSceneNodeState>(beforeNodes);
            AfterNodes = new Dictionary<int, SmartBuildSceneNodeState>(afterNodes);
            BeforeOrder = before.Nodes.Select(node => node.Id).ToArray();
            AfterOrder = after.Nodes.Select(node => node.Id).ToArray();
            BeforeSelectedNodeIds = before.SelectedNodeIds.ToArray();
            AfterSelectedNodeIds = after.SelectedNodeIds.ToArray();
            BeforePrimaryNodeId = before.PrimaryNodeId;
            AfterPrimaryNodeId = after.PrimaryNodeId;
        }

        internal AllConstruct BeforeConstruct { get; }
        internal AllConstruct AfterConstruct { get; }
        internal IReadOnlyDictionary<int, SmartBuildSceneNodeState> BeforeNodes { get; }
        internal IReadOnlyDictionary<int, SmartBuildSceneNodeState> AfterNodes { get; }
        internal IReadOnlyList<int> BeforeOrder { get; }
        internal IReadOnlyList<int> AfterOrder { get; }
        internal IReadOnlyList<int> BeforeSelectedNodeIds { get; }
        internal IReadOnlyList<int> AfterSelectedNodeIds { get; }
        internal int BeforePrimaryNodeId { get; }
        internal int AfterPrimaryNodeId { get; }

        internal bool HasSceneChanges =>
            BeforeNodes.Count > 0 ||
            AfterNodes.Count > 0 ||
            !BeforeOrder.SequenceEqual(AfterOrder) ||
            !BeforeSelectedNodeIds.SequenceEqual(AfterSelectedNodeIds) ||
            BeforePrimaryNodeId != AfterPrimaryNodeId ||
            !ReferenceEquals(BeforeConstruct, AfterConstruct);

        internal static SmartBuildSceneStateDelta Create(
            SmartBuildSceneState before,
            SmartBuildSceneState after)
        {
            before = before ?? new SmartBuildSceneState(null, null, null, -1);
            after = after ?? new SmartBuildSceneState(null, null, null, -1);
            var beforeById = before.Nodes.ToDictionary(node => node.Id);
            var afterById = after.Nodes.ToDictionary(node => node.Id);
            var changedBefore = new Dictionary<int, SmartBuildSceneNodeState>();
            var changedAfter = new Dictionary<int, SmartBuildSceneNodeState>();
            foreach (int id in beforeById.Keys.Union(afterById.Keys))
            {
                bool hadBefore = beforeById.TryGetValue(id, out SmartBuildSceneNodeState oldNode);
                bool hasAfter = afterById.TryGetValue(id, out SmartBuildSceneNodeState newNode);
                if (hadBefore && hasAfter && oldNode.ContentEquals(newNode))
                    continue;
                if (hadBefore)
                    changedBefore[id] = oldNode;
                if (hasAfter)
                    changedAfter[id] = newNode;
            }
            return new SmartBuildSceneStateDelta(before, after, changedBefore, changedAfter);
        }
    }

    internal sealed partial class SmartBuildPieceScene
    {
        internal SmartBuildSceneState CaptureState()
        {
            var nodes = new List<SmartBuildSceneNodeState>(_pieces.Count);
            foreach (SmartBuildPiece piece in _pieces.Where(piece => piece != null))
            {
                if (!_editableNodes.TryGetValue(piece.Id, out ISmartBuildSceneNode node))
                {
                    nodes.Add(new SmartBuildSceneNodeState(piece));
                }
                else if (node is SmartBuildPatternNode pattern)
                {
                    nodes.Add(new SmartBuildSceneNodeState(
                        piece,
                        pattern.SourcePieces,
                        pattern.Definition));
                }
                else if (node is SmartBuildRegionNode region)
                {
                    nodes.Add(new SmartBuildSceneNodeState(
                        piece,
                        region.RegionKind,
                        region.Spans));
                }
                else
                {
                    nodes.Add(new SmartBuildSceneNodeState(piece));
                }
            }
            return new SmartBuildSceneState(
                Construct,
                nodes,
                SelectedPieceIds,
                SelectedPiece?.Id ?? -1);
        }

        internal static SmartBuildPieceScene RestoreState(SmartBuildSceneState state)
        {
            if (state == null || state.Construct == null || state.Nodes.Count == 0)
                return null;

            var scene = new SmartBuildPieceScene(state.Construct);
            foreach (SmartBuildSceneNodeState nodeState in state.Nodes)
            {
                SmartBuildPiece host = nodeState.HostPiece.Clone();
                scene._pieces.Add(host);
                if (nodeState.Kind == SmartBuildSceneNodeKind.Pattern)
                {
                    scene._editableNodes[host.Id] = new SmartBuildPatternNode(
                        host,
                        nodeState.SourcePieces,
                        nodeState.PatternDefinition);
                }
                else if (nodeState.Kind == SmartBuildSceneNodeKind.Region)
                {
                    scene._editableNodes[host.Id] = new SmartBuildRegionNode(
                        host,
                        nodeState.RegionKind,
                        nodeState.RegionCells);
                }
            }
            scene.SetSelection(state.SelectedNodeIds, state.PrimaryNodeId);
            scene.MarkGeometryChanged();
            return scene;
        }
    }
}
