using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal interface ISmartBuildPattern
    {
        SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source);
    }

    internal sealed class SmartBuildPatternResult
    {
        internal SmartBuildPatternResult(
            IEnumerable<Vector3i> cuboidCells,
            IEnumerable<SmartBuildPlacement> fixedPlacements,
            string failureReason = null,
            IEnumerable<string> warnings = null)
        {
            CuboidCells = (cuboidCells ?? Array.Empty<Vector3i>()).ToArray();
            FixedPlacements = (fixedPlacements ?? Array.Empty<SmartBuildPlacement>()).ToArray();
            FailureReason = failureReason;
            Warnings = (warnings ?? Array.Empty<string>())
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .ToArray();
        }

        internal IReadOnlyList<Vector3i> CuboidCells { get; }

        internal IReadOnlyList<SmartBuildPlacement> FixedPlacements { get; }

        internal string FailureReason { get; }

        internal IReadOnlyList<string> Warnings { get; }

        internal bool Success => string.IsNullOrWhiteSpace(FailureReason);

        internal IEnumerable<Vector3i> PreviewCells =>
            CuboidCells.Concat(FixedPlacements.SelectMany(placement => placement.CoveredCells()));
    }

    internal sealed class SmartBuildPreviewSnapshot
    {
        internal SmartBuildPreviewSnapshot(
            IReadOnlyList<Vector3i> cells,
            IReadOnlyList<IReadOnlyList<Vector3i>> cellSets,
            IReadOnlyList<SmartBuildVolume> volumes)
        {
            Cells = cells ?? Array.Empty<Vector3i>();
            CellSets = cellSets ?? Array.Empty<IReadOnlyList<Vector3i>>();
            Volumes = volumes ?? Array.Empty<SmartBuildVolume>();
        }

        internal IReadOnlyList<Vector3i> Cells { get; }

        internal IReadOnlyList<IReadOnlyList<Vector3i>> CellSets { get; }

        internal IReadOnlyList<SmartBuildVolume> Volumes { get; }

        internal static SmartBuildPreviewSnapshot Empty =>
            new SmartBuildPreviewSnapshot(
                Array.Empty<Vector3i>(),
                Array.Empty<IReadOnlyList<Vector3i>>(),
                Array.Empty<SmartBuildVolume>());
    }

    internal sealed partial class SmartBuildPieceScene
    {
        internal const int MaximumScenePieces = SmartBuildLimits.MaximumSceneNodes;
        private readonly List<SmartBuildPiece> _pieces = new List<SmartBuildPiece>();
        private readonly HashSet<int> _selectedPieceIds = new HashSet<int>();

        internal SmartBuildPieceScene(AllConstruct construct)
        {
            Construct = construct;
        }

        internal AllConstruct Construct { get; }

        internal IReadOnlyList<SmartBuildPiece> Pieces => _pieces;

        internal SmartBuildPiece SelectedPiece { get; private set; }

        internal IReadOnlyList<SmartBuildPiece> SelectedPieces =>
            _pieces.Where(piece => piece != null && _selectedPieceIds.Contains(piece.Id)).ToArray();

        internal IReadOnlyList<int> SelectedPieceIds =>
            _pieces
                .Where(piece => piece != null && _selectedPieceIds.Contains(piece.Id))
                .Select(piece => piece.Id)
                .ToArray();

        internal int SelectionCount => _selectedPieceIds.Count;

        internal int Count => _pieces.Count;

        internal bool HasSelection => SelectedPiece != null;

        internal bool IsSelected(int id) => _selectedPieceIds.Contains(id);

        internal bool HasDownSlope =>
            Nodes
                .Where(node => node != null)
                .SelectMany(node => node.SourcePieces)
                .Any(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope);

        internal bool HasFixedGeometry =>
            Nodes
                .Where(node => node != null)
                .SelectMany(node => node.SourcePieces)
                .Any(piece => piece.IsFixedGeometry);

        internal bool CanAddPieces(int additionalCount, out string reason)
        {
            if (additionalCount < 0)
            {
                reason = "The requested Smart Builder piece count is invalid.";
                return false;
            }

            if (_pieces.Count > MaximumScenePieces ||
                (long)_pieces.Count + additionalCount > MaximumScenePieces)
            {
                reason =
                    "The operation would exceed the Smart Builder scene cap of " +
                    MaximumScenePieces + " pieces.";
                return false;
            }

            reason = null;
            return true;
        }

        internal bool Add(SmartBuildPiece piece)
        {
            if (piece == null)
                return false;
            if (!CanAddPieces(1, out _))
                return false;

            _pieces.Add(piece);
            MarkGeometryChanged();
            Select(piece.Id);
            return true;
        }

        internal bool Select(int id)
        {
            SmartBuildPiece piece = _pieces.FirstOrDefault(candidate => candidate.Id == id);
            if (piece == null)
                return false;

            _selectedPieceIds.Clear();
            _selectedPieceIds.Add(piece.Id);
            SelectedPiece = piece;
            MarkSelectionChanged();
            return true;
        }

        internal bool AddToSelection(int id, bool makePrimary = true)
        {
            SmartBuildPiece piece = _pieces.FirstOrDefault(candidate => candidate.Id == id);
            if (piece == null)
                return false;

            _selectedPieceIds.Add(piece.Id);
            if (makePrimary || SelectedPiece == null)
                SelectedPiece = piece;
            MarkSelectionChanged();
            return true;
        }

        internal bool ToggleSelection(int id)
        {
            SmartBuildPiece piece = _pieces.FirstOrDefault(candidate => candidate.Id == id);
            if (piece == null)
                return false;

            if (!_selectedPieceIds.Remove(id))
            {
                _selectedPieceIds.Add(id);
                SelectedPiece = piece;
                MarkSelectionChanged();
                return true;
            }

            if (ReferenceEquals(SelectedPiece, piece))
            {
                SelectedPiece = _pieces.LastOrDefault(
                    candidate => candidate != null && _selectedPieceIds.Contains(candidate.Id));
            }
            MarkSelectionChanged();
            return true;
        }

        internal bool SelectRange(int anchorId, int targetId, bool additive)
        {
            int anchorIndex = _pieces.FindIndex(piece => piece != null && piece.Id == anchorId);
            int targetIndex = _pieces.FindIndex(piece => piece != null && piece.Id == targetId);
            if (anchorIndex < 0 || targetIndex < 0)
                return false;

            if (!additive)
                _selectedPieceIds.Clear();

            int start = Math.Min(anchorIndex, targetIndex);
            int end = Math.Max(anchorIndex, targetIndex);
            for (int index = start; index <= end; index++)
            {
                SmartBuildPiece piece = _pieces[index];
                if (piece != null)
                    _selectedPieceIds.Add(piece.Id);
            }

            SelectedPiece = _pieces[targetIndex];
            MarkSelectionChanged();
            return true;
        }

        internal void SetSelection(IEnumerable<int> selectedIds, int primaryId)
        {
            var liveIds = new HashSet<int>(_pieces.Where(piece => piece != null).Select(piece => piece.Id));
            _selectedPieceIds.Clear();
            foreach (int id in selectedIds ?? Array.Empty<int>())
            {
                if (liveIds.Contains(id))
                    _selectedPieceIds.Add(id);
            }

            SelectedPiece = _pieces.FirstOrDefault(
                piece => piece != null && piece.Id == primaryId && _selectedPieceIds.Contains(piece.Id));
            if (SelectedPiece == null)
            {
                SelectedPiece = _pieces.LastOrDefault(
                    piece => piece != null && _selectedPieceIds.Contains(piece.Id));
            }
            MarkSelectionChanged();
        }

        internal void ClearSelection()
        {
            _selectedPieceIds.Clear();
            SelectedPiece = null;
            MarkSelectionChanged();
        }

        internal SmartBuildPiece DuplicateSelected(Vector3i offset)
        {
            if (SelectedPiece == null || !CanAddPieces(1, out _))
                return null;

            SmartBuildPiece duplicate = SelectedPiece.Duplicate(offset);
            if (!Add(duplicate))
                return null;
            return duplicate;
        }

        internal IReadOnlyList<SmartBuildPiece> DuplicateSelection(Vector3i offset)
        {
            return TryDuplicateSelection(offset, out IReadOnlyList<SmartBuildPiece> copies, out _)
                ? copies
                : Array.Empty<SmartBuildPiece>();
        }

        internal bool TryDuplicateSelection(
            Vector3i offset,
            out IReadOnlyList<SmartBuildPiece> copies,
            out string reason)
        {
            copies = Array.Empty<SmartBuildPiece>();
            SmartBuildPiece[] sources = SelectedPieces.ToArray();
            if (sources.Length == 0)
            {
                reason = "Select at least one Smart Builder piece to duplicate.";
                return false;
            }
            if (!CanAddPieces(sources.Length, out reason))
                return false;

            int primaryId = SelectedPiece?.Id ?? -1;
            var staged = new List<SmartBuildPiece>(sources.Length);
            SmartBuildPiece primaryDuplicate = null;
            try
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    SmartBuildPiece duplicate = sources[index].Duplicate(offset);
                    staged.Add(duplicate);
                    if (sources[index].Id == primaryId)
                        primaryDuplicate = duplicate;
                }
            }
            catch (Exception exception)
            {
                reason = "The selected pieces could not be duplicated: " + exception.Message;
                return false;
            }

            // Commit only after every copy has been created successfully.
            _pieces.AddRange(staged);
            MarkGeometryChanged();
            SetSelection(
                staged.Select(piece => piece.Id),
                primaryDuplicate?.Id ?? staged[staged.Count - 1].Id);
            copies = staged;
            reason = null;
            return true;
        }

        internal IReadOnlyList<SmartBuildPiece> DuplicateSelectionArray(
            Vector3i step,
            int copyCount)
        {
            return TryDuplicateSelectionArray(
                    step,
                    copyCount,
                    out IReadOnlyList<SmartBuildPiece> copies,
                    out _)
                ? copies
                : Array.Empty<SmartBuildPiece>();
        }

        internal bool TryDuplicateSelectionArray(
            Vector3i step,
            int copyCount,
            out IReadOnlyList<SmartBuildPiece> copies,
            out string reason)
        {
            copies = Array.Empty<SmartBuildPiece>();
            SmartBuildPiece[] sources = SelectedPieces.ToArray();
            if (sources.Length == 0)
            {
                reason = "Select at least one Smart Builder piece to array.";
                return false;
            }
            if (copyCount < 1 || copyCount > 64)
            {
                reason = "Smart Builder arrays require between 1 and 64 complete copy layers.";
                return false;
            }
            if (step.x == 0 && step.y == 0 && step.z == 0)
            {
                reason = "Smart Builder array spacing cannot be zero.";
                return false;
            }
            long requestedCount = (long)sources.Length * copyCount;
            if (requestedCount > int.MaxValue)
            {
                reason = "The requested Smart Builder array is too large.";
                return false;
            }
            if (!CanAddPieces((int)requestedCount, out reason))
                return false;

            int primarySourceId = SelectedPiece?.Id ?? sources[sources.Length - 1].Id;
            var staged = new List<SmartBuildPiece>((int)requestedCount);
            SmartBuildPiece primaryDuplicate = null;
            try
            {
                for (int copy = 1; copy <= copyCount; copy++)
                {
                    Vector3i offset = new Vector3i(
                        step.x * copy,
                        step.y * copy,
                        step.z * copy);
                    foreach (SmartBuildPiece source in sources)
                    {
                        SmartBuildPiece duplicate = source.Duplicate(offset);
                        staged.Add(duplicate);
                        if (source.Id == primarySourceId)
                            primaryDuplicate = duplicate;
                    }
                }
            }
            catch (Exception exception)
            {
                reason = "The complete Smart Builder array could not be created: " + exception.Message;
                return false;
            }

            // Never commit a truncated array: all requested layers are staged first.
            _pieces.AddRange(staged);
            MarkGeometryChanged();
            SetSelection(
                sources.Select(piece => piece.Id).Concat(staged.Select(piece => piece.Id)),
                primaryDuplicate?.Id ?? staged[staged.Count - 1].Id);
            copies = staged;
            reason = null;
            return true;
        }

        internal bool TryConvertSelectionTo(
            SmartBuildShapeDescriptor descriptor,
            int selectedLength,
            out int convertedCount,
            out string reason)
        {
            convertedCount = 0;
            SmartBuildPiece[] selected = SelectedPieces.ToArray();
            if (selected.Length == 0)
            {
                reason = "Select at least one Smart Builder piece to convert.";
                return false;
            }

            var staged = new List<SmartBuildPiece>(selected.Length);
            for (int index = 0; index < selected.Length; index++)
            {
                SmartBuildPiece converted = selected[index].Clone();
                if (!converted.TryConvertTo(descriptor, selectedLength, out reason))
                {
                    convertedCount = 0;
                    return false;
                }
                staged.Add(converted);
            }

            // Applying CopyFrom cannot fail after every conversion has passed on a clone.
            for (int index = 0; index < selected.Length; index++)
                selected[index].CopyFrom(staged[index]);
            MarkGeometryChanged();

            convertedCount = selected.Length;
            reason = null;
            return true;
        }

        internal bool DeleteSelected()
        {
            if (SelectedPiece == null)
                return false;

            int index = _pieces.IndexOf(SelectedPiece);
            if (index < 0)
                return false;

            int deletedId = SelectedPiece.Id;
            _pieces.RemoveAt(index);
            RemoveEditableNode(deletedId);
            MarkGeometryChanged();
            _selectedPieceIds.Remove(deletedId);
            if (_pieces.Count == 0)
            {
                ClearSelection();
                return true;
            }

            SelectedPiece = _pieces.LastOrDefault(
                piece => piece != null && _selectedPieceIds.Contains(piece.Id));
            if (SelectedPiece == null)
                Select(_pieces[Math.Min(index, _pieces.Count - 1)].Id);
            return true;
        }

        internal int DeleteSelection()
        {
            if (_selectedPieceIds.Count == 0)
                return 0;

            int fallbackIndex = _pieces.FindIndex(
                piece => piece != null && _selectedPieceIds.Contains(piece.Id));
            int removed = _pieces.RemoveAll(
                piece => piece != null && _selectedPieceIds.Contains(piece.Id));
            foreach (int removedId in _selectedPieceIds.ToArray())
                RemoveEditableNode(removedId);
            if (removed > 0)
                MarkGeometryChanged();
            _selectedPieceIds.Clear();
            if (_pieces.Count == 0)
            {
                SelectedPiece = null;
                return removed;
            }

            fallbackIndex = Math.Max(0, Math.Min(fallbackIndex, _pieces.Count - 1));
            Select(_pieces[fallbackIndex].Id);
            return removed;
        }

        internal void Clear()
        {
            _pieces.Clear();
            ClearEditableNodes();
            MarkGeometryChanged();
            ClearSelection();
        }

        internal void ReplaceWith(
            IEnumerable<SmartBuildPiece> pieces,
            int selectedId)
        {
            ReplaceWith(
                pieces,
                selectedId >= 0 ? new[] { selectedId } : Array.Empty<int>(),
                selectedId);
            if (selectedId >= 0 && SelectedPiece == null && _pieces.Count > 0)
                Select(_pieces[_pieces.Count - 1].Id);
        }

        internal void ReplaceWith(
            IEnumerable<SmartBuildPiece> pieces,
            IEnumerable<int> selectedIds,
            int primarySelectedId)
        {
            _pieces.Clear();
            ClearEditableNodes();
            foreach (SmartBuildPiece piece in pieces ?? Array.Empty<SmartBuildPiece>())
            {
                if (piece != null)
                    _pieces.Add(piece);
            }

            MarkGeometryChanged();
            SetSelection(selectedIds, primarySelectedId);
        }

        internal SmartBuildPreviewSnapshot BuildPreview(SmartBuildSource source = null)
        {
            TryBuildPreviewWithSources(
                _ => source,
                out SmartBuildPreviewSnapshot preview,
                out _);
            return preview;
        }

        internal SmartBuildPreviewSnapshot BuildPreviewWithSources(
            Func<SmartBuildPiece, SmartBuildSource> sourceForPiece)
        {
            TryBuildPreviewWithSources(sourceForPiece, out SmartBuildPreviewSnapshot preview, out _);
            return preview;
        }

        private bool TryBuildPreviewWithSources(
            Func<SmartBuildPiece, SmartBuildSource> sourceForPiece,
            out SmartBuildPreviewSnapshot preview,
            out string reason)
        {
            preview = SmartBuildPreviewSnapshot.Empty;
            reason = null;
            if (Construct == null || _pieces.Count == 0)
                return true;
            if (!TryExpandSceneNodes(
                    out IReadOnlyList<SmartBuildPiece> previewPieces,
                    out _,
                    out reason))
            {
                return false;
            }

            var allCells = new HashSet<Vector3i>();
            var sets = new List<IReadOnlyList<Vector3i>>();
            var volumes = new List<SmartBuildVolume>();
            var seenSets = new List<HashSet<Vector3i>>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                var variantCells = new HashSet<Vector3i>();
                foreach (SmartBuildPiece piece in previewPieces)
                {
                    SmartBuildSource source = sourceForPiece?.Invoke(piece);
                    foreach (Vector3i baseCell in piece.EnumeratePreviewCells(source))
                    {
                        Vector3i cell = variant.Mirror(baseCell);
                        if (!variantCells.Add(cell) || allCells.Contains(cell))
                            continue;
                        if (allCells.Count >= SmartBuildLimits.MaximumPlannerInputCells)
                        {
                            reason =
                                "Symmetry expansion exceeds the " +
                                SmartBuildLimits.MaximumPlannerInputCells.ToString("N0", CultureInfo.InvariantCulture) +
                                "-cell bounded planning limit; reduce the scene or symmetry planes.";
                            preview = SmartBuildPreviewSnapshot.Empty;
                            return false;
                        }
                        allCells.Add(cell);
                    }
                }

                if (variantCells.Count == 0 ||
                    seenSets.Any(seen => seen.SetEquals(variantCells)))
                {
                    continue;
                }

                seenSets.Add(variantCells);
                Vector3i[] cells = variantCells
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray();
                sets.Add(cells);

                SmartBuildVolume volume = VolumeFromCells(Construct, cells);
                if (volume != null)
                    volumes.Add(volume);
            }

            preview = new SmartBuildPreviewSnapshot(
                allCells
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray(),
                sets,
                volumes);
            return true;
        }

        internal SmartBuildPlan BuildPlan(
            SmartBuildSource source,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options,
            out SmartBuildPreviewSnapshot preview)
        {
            if (!TryBuildPreviewWithSources(
                    _ => source,
                    out preview,
                    out string previewFailure))
            {
                return Failed(previewFailure, preview);
            }
            options ??= new SmartBuildPlannerOptions();
            if (Construct == null && !options.AllowNullConstructForVerification)
                return Failed("No valid construct is available.", preview);
            if (_pieces.Count == 0)
                return Failed("No preview piece is active.", preview);
            if (source == null)
                return Failed("Selected Smart Builder material is unavailable.", preview);
            if (source.Family == null || !source.Family.IsSupported)
                return Failed(
                    source.Family?.UnsupportedReason ??
                    "The selected material cannot be used by Smart Block Builder.",
                    preview);
            if (!TryExpandSceneNodes(
                    out IReadOnlyList<SmartBuildPiece> planningPieces,
                    out IReadOnlyDictionary<int, SmartBuildNodeProvenance> provenanceByPieceId,
                    out IReadOnlyList<string> nodeWarnings,
                    out string expansionReason))
            {
                return Failed(expansionReason, preview);
            }
            bool planningHasDownSlope =
                planningPieces.Any(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope);
            if (planningHasDownSlope &&
                !source.HasDownSlopeLength(planningPieces.Where(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope)
                    .Select(piece => piece.SlopeLength)))
            {
                return Failed("One or more down slope sizes are unavailable for this material.", preview);
            }

            string collision = FirstBaseCollision(planningPieces, source);
            if (!string.IsNullOrWhiteSpace(collision))
                return Failed(collision, preview);

            var fixedPlacements = new List<SmartBuildPlacement>();
            var cuboidCells = new Dictionary<string, Vector3i>();
            var packingGroups = new Dictionary<string, CuboidPackingGroup>(StringComparer.Ordinal);
            var skipped = new List<Vector3i>();
            var patternWarnings = new List<string>(nodeWarnings);
            var targetKeys = new HashSet<string>();
            var fixedSignatures = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                foreach (SmartBuildPiece piece in planningPieces)
                {
                    SmartBuildPatternResult result = PatternFor(piece).Build(piece, source);
                    if (!result.Success)
                        return Failed(result.FailureReason, preview);
                    patternWarnings.AddRange(result.Warnings);

                    SmartBuildVolume pieceVolume = piece.ShapeKind == SmartBuildShapeKind.Cuboid
                        ? piece.ToVolume()
                        : null;
                    bool directionLocked = pieceVolume != null &&
                                           piece.ForwardAxis == pieceVolume.GrainAxis;
                    SmartBuildAxis packingAxis = directionLocked
                        ? piece.ForwardAxis
                        : SmartBuildAxis.Z;
                    int packingSign = directionLocked ? piece.ForwardSign : 1;
                    if (directionLocked &&
                        variant.Axes.Any(axis => ToSmartAxis(axis) == packingAxis))
                    {
                        packingSign *= -1;
                    }
                    string packingKey = directionLocked
                        ? packingAxis + ":" + (packingSign >= 0 ? "+" : "-")
                        : "auto";
                    if (!packingGroups.TryGetValue(packingKey, out CuboidPackingGroup packingGroup))
                    {
                        packingGroup = new CuboidPackingGroup(
                            packingKey,
                            directionLocked,
                            packingAxis,
                            packingSign);
                        packingGroups.Add(packingKey, packingGroup);
                    }

                    foreach (Vector3i cell in result.CuboidCells)
                    {
                        Vector3i mirrored = variant.Mirror(cell);
                        string key = DecoLimitLifter.EsuSymmetry.CellKey(mirrored);
                        if (targetKeys.Add(key))
                        {
                            cuboidCells[key] = mirrored;
                            packingGroup.Cells[key] = mirrored;
                        }
                    }

                    foreach (SmartBuildPlacement placement in result.FixedPlacements)
                    {
                        SmartBuildPlacement mirrored = MirrorPlacement(placement, variant, source);
                        if (mirrored == null)
                        {
                            return Failed(
                                "A mirrored Smart Builder shape is unavailable for this material.",
                                preview,
                                skipped);
                        }

                        string signature = PlacementSignature(mirrored);
                        if (!fixedSignatures.Add(signature))
                            continue;

                        Vector3i[] occupied = mirrored.CoveredCells()
                            .Where(cell => isOccupied != null && isOccupied(cell))
                            .ToArray();
                        if (occupied.Length > 0)
                        {
                            if (!options.AllowOccupiedCells)
                                skipped.AddRange(occupied);
                            if (!options.AllowOccupiedCells && !options.SkipOccupiedCells)
                                return Failed("The preview intersects existing blocks.", preview, skipped);
                            if (!options.AllowOccupiedCells)
                                continue;
                        }

                        bool duplicate = false;
                        foreach (Vector3i cell in mirrored.CoveredCells())
                        {
                            string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                            if (!targetKeys.Add(key))
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (duplicate)
                            return Failed("Smart Builder preview pieces overlap each other.", preview, skipped);

                        fixedPlacements.Add(mirrored);
                    }
                }
            }

            List<SmartBuildPlacement> placements = new List<SmartBuildPlacement>(fixedPlacements);
            IReadOnlyList<Vector3i> supportCells = cuboidCells.Values
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ToArray();
            SmartBuildVolume referenceVolume = VolumeFromCells(
                Construct,
                preview.Cells.Count > 0
                    ? preview.Cells
                    : supportCells.Concat(fixedPlacements.SelectMany(placement => placement.CoveredCells())).ToArray());

            foreach (CuboidPackingGroup packingGroup in
                     packingGroups.Values.OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Vector3i[] groupCells = packingGroup.Cells.Values
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray();
                if (groupCells.Length == 0)
                    continue;

                SmartBuildVolume supportVolume = VolumeFromCells(Construct, groupCells);
                if (supportVolume == null)
                    continue;

                SmartBuildPlan supportPlan = SmartBuildPlanner.BuildPlanFromCells(
                    supportVolume,
                    groupCells,
                    packingGroup.DirectionLocked ? packingGroup.Axis : supportVolume.GrainAxis,
                    packingGroup.DirectionLocked ? packingGroup.Sign : 1,
                    source.Family,
                    isOccupied,
                    options);
                skipped.AddRange(supportPlan.SkippedCells);
                patternWarnings.AddRange(supportPlan.Warnings);
                if (!supportPlan.CanCommit)
                {
                    bool emptySkippedGroup = options.SkipOccupiedCells &&
                                             supportPlan.Placements.Count == 0 &&
                                             string.Equals(
                                                 supportPlan.FailureReason,
                                                 "No empty cells are available in the preview.",
                                                 StringComparison.Ordinal);
                    if (emptySkippedGroup)
                        continue;

                    return new SmartBuildPlan(
                        Construct,
                        referenceVolume,
                        Array.Empty<SmartBuildPlacement>(),
                        skipped,
                        supportPlan.Warnings,
                        canCommit: false,
                        failureReason: supportPlan.FailureReason);
                }

                placements.AddRange(supportPlan.Placements);
            }

            if (placements.Count == 0)
                return Failed("No empty cells are available in the preview.", preview, skipped);

            var warnings = patternWarnings
                .GroupBy(warning => warning, StringComparer.Ordinal)
                .Select(group => group.Key)
                .ToList();
            if (placements.Count > options.HardPlacementCap)
            {
                return new SmartBuildPlan(
                    Construct,
                    referenceVolume,
                    placements,
                    skipped,
                    warnings,
                    canCommit: false,
                    failureReason:
                    $"The plan needs {placements.Count:N0} placements, above the {options.HardPlacementCap:N0} hard cap.");
            }

            if (placements.Count > options.WarningPlacementCap)
                warnings.Add($"Large plan: {placements.Count:N0} placements. Commit may hitch.");

            return new SmartBuildPlan(
                Construct,
                referenceVolume,
                placements,
                skipped
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray(),
                warnings,
                canCommit: true,
                failureReason: null);
        }

        internal SmartBuildPlan BuildPlanWithSources(
            Func<SmartBuildPiece, SmartBuildSource> sourceForPiece,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options,
            out SmartBuildPreviewSnapshot preview)
        {
            if (!TryBuildPreviewWithSources(
                    sourceForPiece,
                    out preview,
                    out string previewFailure))
            {
                return Failed(previewFailure, preview);
            }
            options ??= new SmartBuildPlannerOptions();
            if (Construct == null && !options.AllowNullConstructForVerification)
                return Failed("No valid construct is available.", preview);
            if (_pieces.Count == 0)
                return Failed("No preview piece is active.", preview);

            if (!TryExpandSceneNodes(
                    out IReadOnlyList<SmartBuildPiece> planningPieces,
                    out IReadOnlyDictionary<int, SmartBuildNodeProvenance> provenanceByPieceId,
                    out IReadOnlyList<string> nodeWarnings,
                    out string expansionReason))
            {
                return Failed(expansionReason, preview);
            }
            bool planningHasDownSlope =
                planningPieces.Any(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope);

            string collision = FirstBaseCollisionWithSources(planningPieces, sourceForPiece);
            if (!string.IsNullOrWhiteSpace(collision))
                return Failed(collision, preview);

            var groups = new Dictionary<SmartBuildSource, List<SmartBuildPiece>>();
            var cellProvenance = new Dictionary<string, SmartBuildNodeProvenance>(StringComparer.Ordinal);
            foreach (SmartBuildPiece piece in planningPieces)
            {
                SmartBuildSource source = sourceForPiece?.Invoke(piece);
                if (source?.Family == null || !source.Family.IsSupported)
                {
                    return Failed(
                        source?.Family?.UnsupportedReason ??
                        "One or more Smart Builder piece materials are unavailable.",
                        preview);
                }

                if (piece.ShapeKind == SmartBuildShapeKind.DownSlope &&
                    !source.HasDownSlopeLength(new[] { piece.SlopeLength }))
                {
                    return Failed(
                        piece.SlopeLength.ToString(CultureInfo.InvariantCulture) +
                        "m down slopes are unavailable for " + source.DisplayName + ".",
                        preview);
                }

                if (provenanceByPieceId.TryGetValue(
                        piece.Id,
                        out SmartBuildNodeProvenance pieceProvenance))
                {
                    foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                             DecoLimitLifter.EsuSymmetry.Variants())
                    {
                        foreach (Vector3i cell in piece.EnumeratePreviewCells(source).Select(variant.Mirror))
                        {
                            string cellKey = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                            if (!cellProvenance.ContainsKey(cellKey))
                                cellProvenance[cellKey] = pieceProvenance;
                        }
                    }
                }

                if (!groups.TryGetValue(source, out List<SmartBuildPiece> pieces))
                {
                    pieces = new List<SmartBuildPiece>();
                    groups[source] = pieces;
                }
                pieces.Add(piece);
            }

            var placements = new List<SmartBuildPlacement>();
            var skipped = new List<Vector3i>();
            var warnings = new List<string>(nodeWarnings);
            var occupiedByPlan = new HashSet<string>();
            foreach (KeyValuePair<SmartBuildSource, List<SmartBuildPiece>> group in groups)
            {
                var subScene = new SmartBuildPieceScene(Construct);
                subScene.ReplaceWith(group.Value, -1);
                SmartBuildPlan subPlan = subScene.BuildPlan(
                    group.Key,
                    isOccupied,
                    options,
                    out _);
                skipped.AddRange(subPlan.SkippedCells);
                warnings.AddRange(subPlan.Warnings);
                if (!subPlan.CanCommit)
                {
                    bool emptySkippedGroup = options.SkipOccupiedCells &&
                                             subPlan.Placements.Count == 0 &&
                                             string.Equals(
                                                 subPlan.FailureReason,
                                                 "No empty cells are available in the preview.",
                                                 StringComparison.Ordinal);
                    if (emptySkippedGroup)
                        continue;
                    return Failed(subPlan.FailureReason, preview, skipped);
                }

                foreach (SmartBuildPlacement placement in subPlan.Placements)
                {
                    SmartBuildNodeProvenance placementProvenance = default;
                    bool hasPlacementProvenance = false;
                    foreach (Vector3i cell in placement.CoveredCells())
                    {
                        if (!hasPlacementProvenance &&
                            cellProvenance.TryGetValue(
                                DecoLimitLifter.EsuSymmetry.CellKey(cell),
                                out placementProvenance))
                        {
                            hasPlacementProvenance = true;
                        }
                        if (!occupiedByPlan.Add(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                            return Failed("Smart Builder preview pieces overlap each other.", preview, skipped);
                    }
                    if (hasPlacementProvenance)
                    {
                        placement.WithProvenance(
                            placementProvenance.NodeId,
                            placementProvenance.PatternInstanceId);
                    }
                    placements.Add(placement);
                }
            }

            if (placements.Count == 0)
                return Failed("No empty cells are available in the preview.", preview, skipped)
                    .WithCellProvenance(cellProvenance);

            warnings = warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (placements.Count > options.HardPlacementCap)
            {
                return new SmartBuildPlan(
                    Construct,
                    VolumeFromCells(Construct, preview.Cells),
                    placements,
                    skipped,
                    warnings,
                    canCommit: false,
                    failureReason:
                    $"The plan needs {placements.Count:N0} placements, above the {options.HardPlacementCap:N0} hard cap.")
                    .WithCellProvenance(cellProvenance);
            }
            if (placements.Count > options.WarningPlacementCap)
                warnings.Add($"Large plan: {placements.Count:N0} placements. Commit may hitch.");

            return new SmartBuildPlan(
                Construct,
                VolumeFromCells(Construct, preview.Cells),
                placements,
                skipped
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray(),
                warnings,
                canCommit: true,
                failureReason: null)
                .WithCellProvenance(cellProvenance);
        }

        private static string FirstBaseCollisionWithSources(
            IEnumerable<SmartBuildPiece> pieces,
            Func<SmartBuildPiece, SmartBuildSource> sourceForPiece)
        {
            var seen = new Dictionary<string, int>();
            foreach (SmartBuildPiece piece in pieces ?? Array.Empty<SmartBuildPiece>())
            {
                foreach (Vector3i cell in piece.EnumeratePreviewCells(sourceForPiece?.Invoke(piece)))
                {
                    string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                    if (seen.ContainsKey(key))
                        return "Smart Builder preview pieces overlap each other.";
                    seen[key] = piece.Id;
                }
            }

            return null;
        }

        private static string FirstBaseCollision(
            IEnumerable<SmartBuildPiece> pieces,
            SmartBuildSource source)
        {
            var seen = new Dictionary<string, int>();
            foreach (SmartBuildPiece piece in pieces ?? Array.Empty<SmartBuildPiece>())
            {
                foreach (Vector3i cell in piece.EnumeratePreviewCells(source))
                {
                    string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                    if (seen.ContainsKey(key))
                        return "Smart Builder preview pieces overlap each other.";
                    seen[key] = piece.Id;
                }
            }

            return null;
        }

        private SmartBuildPlan Failed(
            string reason,
            SmartBuildPreviewSnapshot preview,
            IEnumerable<Vector3i> skippedCells = null) =>
            new SmartBuildPlan(
                Construct,
                VolumeFromCells(Construct, preview?.Cells),
                Array.Empty<SmartBuildPlacement>(),
                (skippedCells ?? Array.Empty<Vector3i>()).ToArray(),
                Array.Empty<string>(),
                canCommit: false,
                failureReason: reason);

        private static ISmartBuildPattern PatternFor(SmartBuildPiece piece)
        {
            switch (piece.ShapeKind)
            {
                case SmartBuildShapeKind.DownSlope:
                    return DownSlopePattern.Instance;
                case SmartBuildShapeKind.GeneratedCircle:
                case SmartBuildShapeKind.GeneratedPolygon:
                case SmartBuildShapeKind.GeneratedSphere:
                case SmartBuildShapeKind.GeneratedArc:
                case SmartBuildShapeKind.GeneratedCone:
                case SmartBuildShapeKind.GeneratedFrustum:
                case SmartBuildShapeKind.GeneratedTube:
                    return GeneratedPattern.Instance;
                default:
                    if (piece.IsFixedGeometry)
                        return FixedGeometryPattern.Instance;
                    return CuboidPattern.Instance;
            }
        }

        private static SmartBuildPlacement MirrorPlacement(
            SmartBuildPlacement placement,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            return MirrorPlacement(placement, variant, null);
        }

        internal static SmartBuildPlacement MirrorPlacement(
            SmartBuildPlacement placement,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            SmartBuildSource source)
        {
            if (variant.Axes.Count == 0)
                return placement;

            int sign = placement.AxisSign;
            if (variant.Axes.Any(axis => ToSmartAxis(axis) == placement.Axis))
                sign *= -1;

            SmartBlockCandidate candidate = MirrorCandidate(placement.Candidate, variant, source);
            if (candidate == null)
                return null;

            return new SmartBuildPlacement(
                variant.Mirror(placement.Position),
                candidate,
                placement.Axis,
                sign,
                MirrorRotation(placement.Rotation, variant),
                placement.CoveredCells().Select(variant.Mirror),
                candidate.DisplayName)
                .WithProvenance(placement.NodeId, placement.PatternInstanceId);
        }

        private static SmartBlockCandidate MirrorCandidate(
            SmartBlockCandidate candidate,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            SmartBuildSource source)
        {
            if (candidate == null)
                return null;

            SmartBuildShapeDescriptor descriptor = candidate.Descriptor;
            if (!SmartBuildShapeDescriptors.IsOddMirror(descriptor, variant.Axes))
                return candidate;

            SmartBuildShapeDescriptor mirror = descriptor.MirrorDescriptor();
            if (mirror == null || mirror.Key == descriptor.Key)
                return candidate;

            SmartBlockCandidate mirrored = source?
                .FamilyForShape(mirror)?
                .CandidateForLength(candidate.Length);
            return mirrored;
        }

        private static Quaternion MirrorRotation(
            Quaternion rotation,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            Vector3 forward = MirrorDirection(rotation * Vector3.forward, variant);
            Vector3 up = MirrorDirection(rotation * Vector3.up, variant);
            forward = Normalize(forward);
            up = Normalize(up);
            Vector3 right = Normalize(Cross(up, forward));
            if (forward.sqrMagnitude <= 0.0001f ||
                up.sqrMagnitude <= 0.0001f ||
                right.sqrMagnitude <= 0.0001f)
            {
                return rotation;
            }

            up = Normalize(Cross(forward, right));
            return QuaternionFromBasis(right, up, forward);
        }

        private static Vector3 MirrorDirection(
            Vector3 direction,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            foreach (DecorationEditAxis axis in variant.Axes)
            {
                switch (axis)
                {
                    case DecorationEditAxis.X:
                        direction.x = -direction.x;
                        break;
                    case DecorationEditAxis.Y:
                        direction.y = -direction.y;
                        break;
                    case DecorationEditAxis.Z:
                        direction.z = -direction.z;
                        break;
                }
            }

            return direction;
        }

        private static Vector3 Normalize(Vector3 value)
        {
            float squared = value.x * value.x + value.y * value.y + value.z * value.z;
            if (squared <= 0.0001f)
                return Vector3.zero;

            float scale = 1f / (float)Math.Sqrt(squared);
            return new Vector3(value.x * scale, value.y * scale, value.z * scale);
        }

        private static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);

        private static Quaternion QuaternionFromBasis(
            Vector3 right,
            Vector3 up,
            Vector3 forward)
        {
            float m00 = right.x;
            float m01 = up.x;
            float m02 = forward.x;
            float m10 = right.y;
            float m11 = up.y;
            float m12 = forward.y;
            float m20 = right.z;
            float m21 = up.z;
            float m22 = forward.z;
            float trace = m00 + m11 + m22;

            if (trace > 0f)
            {
                float scale = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quaternion(
                    (m21 - m12) / scale,
                    (m02 - m20) / scale,
                    (m10 - m01) / scale,
                    0.25f * scale);
            }

            if (m00 > m11 && m00 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quaternion(
                    0.25f * scale,
                    (m01 + m10) / scale,
                    (m02 + m20) / scale,
                    (m21 - m12) / scale);
            }

            if (m11 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quaternion(
                    (m01 + m10) / scale,
                    0.25f * scale,
                    (m12 + m21) / scale,
                    (m02 - m20) / scale);
            }

            {
                float scale = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                return new Quaternion(
                    (m02 + m20) / scale,
                    (m12 + m21) / scale,
                    0.25f * scale,
                    (m10 - m01) / scale);
            }
        }

        private static SmartBuildAxis ToSmartAxis(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return SmartBuildAxis.X;
                case DecorationEditAxis.Y:
                    return SmartBuildAxis.Y;
                default:
                    return SmartBuildAxis.Z;
            }
        }

        private static string PlacementSignature(SmartBuildPlacement placement) =>
            string.Join(
                "|",
                placement.CoveredCells()
                    .Select(DecoLimitLifter.EsuSymmetry.CellKey)
                    .OrderBy(key => key)
                    .ToArray()) +
            "|" +
            placement.Candidate?.DisplayName;

        private static SmartBuildVolume VolumeFromCells(
            AllConstruct construct,
            IEnumerable<Vector3i> rawCells)
        {
            Vector3i[] cells = rawCells?.ToArray() ?? Array.Empty<Vector3i>();
            if (cells.Length == 0)
                return null;

            Vector3i min = cells[0];
            Vector3i max = cells[0];
            for (int index = 1; index < cells.Length; index++)
            {
                Vector3i cell = cells[index];
                min.x = Math.Min(min.x, cell.x);
                min.y = Math.Min(min.y, cell.y);
                min.z = Math.Min(min.z, cell.z);
                max.x = Math.Max(max.x, cell.x);
                max.y = Math.Max(max.y, cell.y);
                max.z = Math.Max(max.z, cell.z);
            }

            return SmartBuildVolume.FromBounds(construct, min, max);
        }

        private sealed class CuboidPackingGroup
        {
            internal CuboidPackingGroup(
                string key,
                bool directionLocked,
                SmartBuildAxis axis,
                int sign)
            {
                Key = key;
                DirectionLocked = directionLocked;
                Axis = axis;
                Sign = sign >= 0 ? 1 : -1;
            }

            internal string Key { get; }

            internal bool DirectionLocked { get; }

            internal SmartBuildAxis Axis { get; }

            internal int Sign { get; }

            internal Dictionary<string, Vector3i> Cells { get; } =
                new Dictionary<string, Vector3i>(StringComparer.Ordinal);
        }

        private sealed class CuboidPattern : ISmartBuildPattern
        {
            internal static readonly CuboidPattern Instance = new CuboidPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source) =>
                new SmartBuildPatternResult(piece.EnumeratePreviewCells(), Array.Empty<SmartBuildPlacement>());
        }

        private sealed class DownSlopePattern : ISmartBuildPattern
        {
            internal static readonly DownSlopePattern Instance = new DownSlopePattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(
                    source,
                    out string reason);
                if (!string.IsNullOrWhiteSpace(reason))
                    return new SmartBuildPatternResult(
                        Array.Empty<Vector3i>(),
                        Array.Empty<SmartBuildPlacement>(),
                        reason);

                return new SmartBuildPatternResult(
                    piece.EnumerateSupportCells(),
                    placements);
            }
        }

        private sealed class FixedGeometryPattern : ISmartBuildPattern
        {
            internal static readonly FixedGeometryPattern Instance = new FixedGeometryPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(
                    source,
                    out string reason);
                if (!string.IsNullOrWhiteSpace(reason))
                    return new SmartBuildPatternResult(
                        Array.Empty<Vector3i>(),
                        Array.Empty<SmartBuildPlacement>(),
                        reason);

                return new SmartBuildPatternResult(
                    Array.Empty<Vector3i>(),
                    placements);
            }
        }

        private sealed class GeneratedPattern : ISmartBuildPattern
        {
            internal static readonly GeneratedPattern Instance = new GeneratedPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                Vector3i[] generatedCells = piece.EnumeratePreviewCells(source).ToArray();
                IReadOnlyList<SmartBuildPlacement> smoothingPlacements =
                    piece.BuildGeneratedSmoothingPlacements(
                        source,
                        out IReadOnlyList<Vector3i> replacedCells,
                        out IReadOnlyList<string> warnings);
                if (smoothingPlacements.Count == 0 || replacedCells.Count == 0)
                {
                    return new SmartBuildPatternResult(
                        generatedCells,
                        Array.Empty<SmartBuildPlacement>(),
                        warnings: warnings);
                }

                var replaced = new HashSet<string>(replacedCells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
                Vector3i[] cuboidCells = generatedCells
                    .Where(cell => !replaced.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                    .ToArray();
                return new SmartBuildPatternResult(
                    cuboidCells,
                    smoothingPlacements,
                    warnings: warnings);
            }
        }
    }
}
