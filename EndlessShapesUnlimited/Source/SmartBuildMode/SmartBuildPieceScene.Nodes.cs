using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildPieceScene
    {
        private readonly Dictionary<int, ISmartBuildSceneNode> _editableNodes =
            new Dictionary<int, ISmartBuildSceneNode>();
        private readonly Dictionary<int, CachedNodeExpansion> _nodeExpansionCache =
            new Dictionary<int, CachedNodeExpansion>();
        private long _geometryRevision = 1L;
        private long _selectionRevision = 1L;
        private long _presentationRevision = 1L;

        internal long GeometryRevision => _geometryRevision;

        internal long SelectionRevision => _selectionRevision;

        internal long PresentationRevision => _presentationRevision;

        internal IReadOnlyList<ISmartBuildSceneNode> Nodes =>
            _pieces
                .Where(piece => piece != null)
                .Select(NodeForPiece)
                .ToArray();

        internal int EditableNodeCount => _editableNodes.Count;

        internal ISmartBuildSceneNode SelectedNode =>
            SelectedPiece == null ? null : NodeForPiece(SelectedPiece);

        internal bool IsEditablePattern(int pieceId) =>
            _editableNodes.TryGetValue(pieceId, out ISmartBuildSceneNode node) &&
            node.Kind == SmartBuildSceneNodeKind.Pattern;

        internal bool IsEditableRegion(int pieceId) =>
            _editableNodes.TryGetValue(pieceId, out ISmartBuildSceneNode node) &&
            node.Kind == SmartBuildSceneNodeKind.Region;

        internal bool TryCreatePatternFromSelection(
            SmartBuildPatternDefinition definition,
            out SmartBuildPatternNode pattern,
            out string reason)
        {
            pattern = null;
            SmartBuildPiece[] selected = SelectedPieces.ToArray();
            if (selected.Length == 0 || SelectedPiece == null)
            {
                reason = "Select one or more primitive Smart Builder pieces first.";
                return false;
            }
            if (selected.Any(piece => _editableNodes.ContainsKey(piece.Id)))
            {
                reason = "Nested editable patterns are not supported. Bake the selected pattern first.";
                return false;
            }
            if (definition == null)
            {
                reason = "No editable pattern definition was supplied.";
                return false;
            }
            if (!definition.TryValidate(selected.Length, out _, out reason))
                return false;

            SmartBuildPiece host = SelectedPiece;
            SmartBuildPatternNode staged;
            try
            {
                staged = new SmartBuildPatternNode(host, selected, definition);
                if (!staged.TryExpand(
                        new SmartBuildExpansionBudget(),
                        out _,
                        out reason))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "The editable pattern could not be created: " + exception.Message;
                return false;
            }

            var selectedIds = new HashSet<int>(selected.Select(piece => piece.Id));
            _pieces.RemoveAll(piece => piece != null && piece.Id != host.Id && selectedIds.Contains(piece.Id));
            _editableNodes[host.Id] = staged;
            _nodeExpansionCache.Remove(host.Id);
            SetSelection(new[] { host.Id }, host.Id);
            MarkGeometryChanged();
            pattern = staged;
            reason = null;
            return true;
        }

        internal bool TryCreateRegionFromSelection(
            SmartBuildRegionKind kind,
            IEnumerable<Vector3i> cells,
            out SmartBuildRegionNode region,
            out string reason)
        {
            region = null;
            SmartBuildPiece host = SelectedPiece;
            if (SelectionCount != 1 || host == null ||
                host.ShapeKind != SmartBuildShapeKind.Cuboid ||
                host.Bounds.Size.x != 1 || host.Bounds.Size.y != 1 || host.Bounds.Size.z != 1)
            {
                reason = "Editable regions require exactly one selected 1m cuboid.";
                return false;
            }
            if (_editableNodes.ContainsKey(host.Id))
            {
                reason = "Bake or dissolve the selected editable node before creating a region.";
                return false;
            }

            try
            {
                var staged = new SmartBuildRegionNode(host, kind, cells);
                if (!staged.TryExpand(
                        new SmartBuildExpansionBudget(),
                        out _,
                        out reason))
                {
                    return false;
                }
                _editableNodes[host.Id] = staged;
                _nodeExpansionCache.Remove(host.Id);
                MarkGeometryChanged();
                region = staged;
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = "The editable region could not be created: " + exception.Message;
                return false;
            }
        }

        internal bool TryUpdateSelectedPattern(
            SmartBuildPatternDefinition definition,
            out string reason)
        {
            if (!(SelectedNode is SmartBuildPatternNode pattern))
            {
                reason = "Select an editable pattern first.";
                return false;
            }
            SmartBuildPatternDefinition previous = pattern.Definition;
            if (!pattern.TrySetDefinition(definition, out reason))
                return false;
            if (!pattern.TryExpand(new SmartBuildExpansionBudget(), out _, out reason))
            {
                string expansionReason = reason;
                pattern.TrySetDefinition(previous, out _);
                reason = expansionReason;
                return false;
            }
            _nodeExpansionCache.Remove(pattern.Id);
            MarkGeometryChanged();
            return true;
        }

        internal bool TryUpdateSelectedPatternSource(
            int sourceIndex,
            SmartBuildPiece replacement,
            out string reason)
        {
            if (!(SelectedNode is SmartBuildPatternNode pattern))
            {
                reason = "Select an editable pattern first.";
                return false;
            }
            if (!pattern.TrySetSourcePiece(sourceIndex, replacement, out reason))
                return false;
            _nodeExpansionCache.Remove(pattern.Id);
            MarkGeometryChanged();
            return true;
        }

        internal bool TryReplaceSelectedRegionCells(
            IEnumerable<Vector3i> cells,
            out string reason)
        {
            if (!(SelectedNode is SmartBuildRegionNode region))
            {
                reason = "Select an editable region first.";
                return false;
            }
            if (!region.TryReplaceCells(cells, out reason))
                return false;
            _nodeExpansionCache.Remove(region.Id);
            MarkGeometryChanged();
            return true;
        }

        internal bool TryBakeSelectedNode(
            out IReadOnlyList<SmartBuildPiece> baked,
            out string reason)
        {
            baked = Array.Empty<SmartBuildPiece>();
            ISmartBuildSceneNode node = SelectedNode;
            if (node == null || node.Kind == SmartBuildSceneNodeKind.Primitive)
            {
                reason = "Select an editable pattern or region to bake.";
                return false;
            }

            int available = MaximumScenePieces - (_pieces.Count - 1);
            if (!node.TryBake(available, out IReadOnlyList<SmartBuildPiece> staged, out reason))
                return false;
            if (staged.Count == 0 || staged.Count > available)
            {
                reason = "Baking would exceed the Smart Builder scene cap of " +
                         MaximumScenePieces.ToString(CultureInfo.InvariantCulture) + " pieces.";
                return false;
            }

            int hostIndex = _pieces.FindIndex(piece => piece != null && piece.Id == node.Id);
            if (hostIndex < 0)
            {
                reason = "The editable node host is no longer in the scene.";
                return false;
            }
            _pieces.RemoveAt(hostIndex);
            _pieces.InsertRange(hostIndex, staged);
            _editableNodes.Remove(node.Id);
            _nodeExpansionCache.Remove(node.Id);
            SetSelection(staged.Select(piece => piece.Id), staged[staged.Count - 1].Id);
            MarkGeometryChanged();
            baked = staged;
            reason = null;
            return true;
        }

        internal bool TryDissolveSelectedNode(
            out IReadOnlyList<SmartBuildPiece> sources,
            out string reason)
        {
            sources = Array.Empty<SmartBuildPiece>();
            ISmartBuildSceneNode node = SelectedNode;
            if (node == null || node.Kind == SmartBuildSceneNodeKind.Primitive)
            {
                reason = "Select an editable pattern or region to dissolve.";
                return false;
            }
            IReadOnlyList<SmartBuildPiece> staged = node.Dissolve();
            int available = MaximumScenePieces - (_pieces.Count - 1);
            if (staged.Count == 0 || staged.Count > available)
            {
                reason = "Dissolving would exceed the Smart Builder scene cap.";
                return false;
            }

            int hostIndex = _pieces.FindIndex(piece => piece != null && piece.Id == node.Id);
            if (hostIndex < 0)
            {
                reason = "The editable node host is no longer in the scene.";
                return false;
            }
            _pieces.RemoveAt(hostIndex);
            _pieces.InsertRange(hostIndex, staged);
            _editableNodes.Remove(node.Id);
            _nodeExpansionCache.Remove(node.Id);
            SetSelection(staged.Select(piece => piece.Id), staged[staged.Count - 1].Id);
            MarkGeometryChanged();
            sources = staged;
            reason = null;
            return true;
        }

        internal bool TryExpandSceneNodes(
            out IReadOnlyList<SmartBuildPiece> pieces,
            out IReadOnlyList<string> warnings,
            out string reason) =>
            TryExpandSceneNodes(out pieces, out _, out warnings, out reason);

        internal bool TryExpandSceneNodes(
            out IReadOnlyList<SmartBuildPiece> pieces,
            out IReadOnlyDictionary<int, SmartBuildNodeProvenance> provenanceByPieceId,
            out IReadOnlyList<string> warnings,
            out string reason)
        {
            var budget = new SmartBuildExpansionBudget();
            var expanded = new List<SmartBuildPiece>();
            var provenance = new Dictionary<int, SmartBuildNodeProvenance>();
            var expansionWarnings = new List<string>();
            long reservedPreviewCells = 0L;
            foreach (SmartBuildPiece piece in _pieces)
            {
                if (piece == null)
                    continue;
                if (!_editableNodes.TryGetValue(piece.Id, out ISmartBuildSceneNode node))
                {
                    if (!budget.TryReserve(1, out reason))
                    {
                        pieces = Array.Empty<SmartBuildPiece>();
                        provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                        warnings = Array.Empty<string>();
                        return false;
                    }
                    if (!piece.TryReservePreviewEnumeration(
                            ref reservedPreviewCells,
                            SmartBuildLimits.MaximumPlannerInputCells,
                            out reason))
                    {
                        pieces = Array.Empty<SmartBuildPiece>();
                        provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                        warnings = Array.Empty<string>();
                        return false;
                    }
                    expanded.Add(piece);
                    provenance[piece.Id] = new SmartBuildNodeProvenance(piece.Id, 0);
                    continue;
                }

                long hostFingerprint = HostFingerprint(piece);
                if (_nodeExpansionCache.TryGetValue(piece.Id, out CachedNodeExpansion cached) &&
                    cached.Revision == node.Revision &&
                    cached.HostFingerprint == hostFingerprint)
                {
                    if (!budget.TryReserve(cached.Expansion.Pieces.Count, out reason))
                    {
                        pieces = Array.Empty<SmartBuildPiece>();
                        provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                        warnings = Array.Empty<string>();
                        return false;
                    }
                    foreach (SmartBuildPiece cachedPiece in cached.Expansion.Pieces)
                    {
                        if (cachedPiece == null ||
                            !cachedPiece.TryReservePreviewEnumeration(
                                ref reservedPreviewCells,
                                SmartBuildLimits.MaximumPlannerInputCells,
                                out reason))
                        {
                            pieces = Array.Empty<SmartBuildPiece>();
                            provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                            warnings = Array.Empty<string>();
                            return false;
                        }
                    }
                    expanded.AddRange(cached.Expansion.Pieces);
                    AddExpansionProvenance(node, cached.Expansion.Pieces, provenance);
                    expansionWarnings.AddRange(cached.Expansion.Warnings);
                    continue;
                }

                if (!node.TryExpand(budget, out SmartBuildNodeExpansion nodeExpansion, out reason))
                {
                    pieces = Array.Empty<SmartBuildPiece>();
                    provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                    warnings = Array.Empty<string>();
                    return false;
                }
                foreach (SmartBuildPiece expandedPiece in nodeExpansion.Pieces)
                {
                    if (expandedPiece == null ||
                        !expandedPiece.TryReservePreviewEnumeration(
                            ref reservedPreviewCells,
                            SmartBuildLimits.MaximumPlannerInputCells,
                            out reason))
                    {
                        pieces = Array.Empty<SmartBuildPiece>();
                        provenanceByPieceId = new Dictionary<int, SmartBuildNodeProvenance>();
                        warnings = Array.Empty<string>();
                        return false;
                    }
                }
                _nodeExpansionCache[piece.Id] = new CachedNodeExpansion(
                    node.Revision,
                    HostFingerprint(piece),
                    nodeExpansion);
                expanded.AddRange(nodeExpansion.Pieces);
                AddExpansionProvenance(node, nodeExpansion.Pieces, provenance);
                expansionWarnings.AddRange(nodeExpansion.Warnings);
            }

            pieces = expanded;
            provenanceByPieceId = provenance;
            warnings = expansionWarnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            reason = null;
            return true;
        }

        private static void AddExpansionProvenance(
            ISmartBuildSceneNode node,
            IReadOnlyList<SmartBuildPiece> pieces,
            IDictionary<int, SmartBuildNodeProvenance> provenance)
        {
            int sourceCount = Math.Max(1, node?.SourcePieces?.Count ?? 1);
            for (int index = 0; index < (pieces?.Count ?? 0); index++)
            {
                SmartBuildPiece piece = pieces[index];
                if (piece != null)
                    provenance[piece.Id] = new SmartBuildNodeProvenance(
                        node?.Id ?? piece.Id,
                        index / sourceCount);
            }
        }

        internal void InvalidateNodeExpansion(int pieceId)
        {
            _nodeExpansionCache.Remove(pieceId);
            MarkGeometryChanged();
        }

        internal void MarkGeometryChanged()
        {
            unchecked
            {
                _geometryRevision++;
                if (_geometryRevision <= 0L)
                    _geometryRevision = 1L;
            }
        }

        internal void MarkSelectionChanged()
        {
            unchecked
            {
                _selectionRevision++;
                if (_selectionRevision <= 0L)
                    _selectionRevision = 1L;
            }
        }

        internal void MarkPresentationChanged()
        {
            unchecked
            {
                _presentationRevision++;
                if (_presentationRevision <= 0L)
                    _presentationRevision = 1L;
            }
        }

        private ISmartBuildSceneNode NodeForPiece(SmartBuildPiece piece)
        {
            if (piece != null && _editableNodes.TryGetValue(piece.Id, out ISmartBuildSceneNode node))
                return node;
            return piece == null ? null : new SmartBuildPrimitiveNode(piece);
        }

        private void RemoveEditableNode(int pieceId)
        {
            _editableNodes.Remove(pieceId);
            _nodeExpansionCache.Remove(pieceId);
        }

        private void ClearEditableNodes()
        {
            _editableNodes.Clear();
            _nodeExpansionCache.Clear();
        }

        private static long HostFingerprint(SmartBuildPiece piece)
            => piece?.StructuralHash ?? 0L;

        private sealed class CachedNodeExpansion
        {
            internal CachedNodeExpansion(
                long revision,
                long hostFingerprint,
                SmartBuildNodeExpansion expansion)
            {
                Revision = revision;
                HostFingerprint = hostFingerprint;
                Expansion = expansion;
            }

            internal long Revision { get; }

            internal long HostFingerprint { get; }

            internal SmartBuildNodeExpansion Expansion { get; }
        }
    }
}
