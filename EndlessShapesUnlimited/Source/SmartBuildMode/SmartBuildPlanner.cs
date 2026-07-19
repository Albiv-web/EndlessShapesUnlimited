using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding.Types;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed class SmartBlockCandidate
    {
        internal SmartBlockCandidate(
            string displayName,
            int length,
            ItemDefinition definition)
            : this(displayName, length, definition, SmartBuildShapeKind.Cuboid, null)
        {
        }

        internal SmartBlockCandidate(
            string displayName,
            int length,
            ItemDefinition definition,
            SmartBuildShapeKind shapeKind,
            object geometry)
            : this(
                displayName,
                length,
                definition,
                shapeKind,
                geometry,
                DescriptorFor(shapeKind, geometry),
                geometry?.ToString())
        {
        }

        internal SmartBlockCandidate(
            string displayName,
            int length,
            ItemDefinition definition,
            SmartBuildShapeKind shapeKind,
            object geometry,
            SmartBuildShapeDescriptor descriptor,
            string geometryName)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "Selected block"
                : displayName;
            Length = Math.Max(1, length);
            Definition = definition;
            ShapeKind = shapeKind;
            Geometry = geometry;
            Descriptor = descriptor ??
                         SmartBuildShapeDescriptors.ByKey(SmartBuildShapeDescriptors.CuboidKey);
            GeometryName = string.IsNullOrWhiteSpace(geometryName)
                ? geometry?.ToString() ?? string.Empty
                : geometryName;
        }

        internal string DisplayName { get; }

        internal int Length { get; }

        internal ItemDefinition Definition { get; }

        internal SmartBuildShapeKind ShapeKind { get; }

        internal object Geometry { get; }

        internal SmartBuildShapeDescriptor Descriptor { get; }

        internal string GeometryName { get; }

        internal static SmartBlockCandidate ForTests(int length) =>
            new SmartBlockCandidate(length + "m test block", length, null);

        private static SmartBuildShapeDescriptor DescriptorFor(
            SmartBuildShapeKind shapeKind,
            object geometry)
        {
            if (SmartBuildShapeDescriptors.TryParseGeometry(geometry, out SmartBuildGeometryInfo info))
                return info.Descriptor;

            return SmartBuildShapeDescriptors.ByKey(
                shapeKind == SmartBuildShapeKind.DownSlope
                    ? SmartBuildShapeDescriptors.DownSlopeKey
                    : shapeKind == SmartBuildShapeKind.Cuboid
                        ? SmartBuildShapeDescriptors.CuboidKey
                        : null);
        }

        internal IEnumerable<Vector3i> CoveredCellsFrom(
            Vector3i position,
            Quaternion rotation)
        {
            var cells = new List<Vector3i>();
            try
            {
                if (Definition?.SizeInfo != null &&
                    Definition.SizeInfo.ArrayPositionsUsed > 0)
                {
                    if (Definition.SizeInfo.ArrayPositionsUsed >
                        SmartBuildLimits.MaximumConditionalFootprintCells)
                    {
                        return Array.Empty<Vector3i>();
                    }

                    var seen = new HashSet<string>();
                    for (int index = 0; index < Definition.SizeInfo.ArrayPositionsUsed; index++)
                    {
                        Vector3i cell = Definition.SizeInfo.GetPosition(index, position, rotation);
                        if (seen.Add(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                            cells.Add(cell);
                    }

                    if (cells.Count > 0)
                        return cells;
                }
            }
            catch
            {
                // Test candidates and unusual modded definitions fall back to line coverage.
            }

            SmartBuildAxis axis = SmartBuildAxisHelper.FromLargestComponent(rotation * Vector3.forward, out int sign);
            int direction = sign >= 0 ? 1 : -1;
            int fallbackLength = Math.Max(1, Length);
            if (fallbackLength > SmartBuildLimits.MaximumConditionalFootprintCells)
                return Array.Empty<Vector3i>();
            for (int index = 0; index < fallbackLength; index++)
                cells.Add(position + SmartBuildAxisHelper.ToVector3i(axis, index * direction));

            return cells;
        }
    }

    internal sealed class SmartBlockFamily
    {
        internal SmartBlockFamily(
            string displayName,
            IEnumerable<SmartBlockCandidate> candidates,
            string unsupportedReason = null)
        {
            DisplayName = displayName ?? "Selected block";
            Candidates = candidates?
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.Length)
                .ToArray() ?? Array.Empty<SmartBlockCandidate>();
            UnsupportedReason = unsupportedReason;
        }

        internal string DisplayName { get; }

        internal IReadOnlyList<SmartBlockCandidate> Candidates { get; }

        internal string UnsupportedReason { get; }

        internal bool IsSupported => Candidates.Count > 0 && string.IsNullOrWhiteSpace(UnsupportedReason);

        internal bool HasSingleCell => Candidates.Any(candidate => candidate.Length == 1);

        internal SmartBlockCandidate CandidateForLength(int length) =>
            Candidates
                .OrderBy(candidate => Math.Abs(candidate.Length - length))
                .ThenBy(candidate => candidate.Length)
                .FirstOrDefault();

        internal static SmartBlockFamily ForTests(params int[] lengths) =>
            new SmartBlockFamily(
                "test family",
                lengths.Select(SmartBlockCandidate.ForTests));

        internal static SmartBlockFamily Unsupported(string displayName, string reason) =>
            new SmartBlockFamily(displayName, Array.Empty<SmartBlockCandidate>(), reason);
    }

    internal enum SmartBuildCommitOperation
    {
        Place,
        Replace,
        Erase
    }

    internal sealed class SmartBuildPlannerOptions
    {
        internal bool SkipOccupiedCells { get; set; }

        internal bool AllowOccupiedCells { get; set; }

        internal int WarningPlacementCap { get; set; } =
            SmartBuildLimits.WarningPlacementCount;

        internal int HardPlacementCap { get; set; } =
            SmartBuildLimits.HardPlacementCount;

        internal bool AllowNullConstructForVerification { get; set; }
    }

    internal sealed class SmartBuildPlacement
    {
        private readonly Vector3i[] _coveredCells;

        internal SmartBuildPlacement(
            Vector3i position,
            SmartBlockCandidate candidate,
            SmartBuildAxis axis)
            : this(position, candidate, axis, 1)
        {
        }

        internal SmartBuildPlacement(
            Vector3i position,
            SmartBlockCandidate candidate,
            SmartBuildAxis axis,
            int axisSign)
        {
            Position = position;
            Candidate = candidate;
            Axis = axis;
            AxisSign = axisSign >= 0 ? 1 : -1;
            Rotation = RotationForAxis(axis, AxisSign);
            _coveredCells = EnumerateLine(position, axis, AxisSign, candidate?.Length ?? 1)
                .ToArray();
            DisplayName = candidate?.DisplayName ?? "Selected block";
        }

        internal SmartBuildPlacement(
            Vector3i position,
            SmartBlockCandidate candidate,
            SmartBuildAxis axis,
            int axisSign,
            Quaternion rotation,
            IEnumerable<Vector3i> coveredCells,
            string displayName = null)
        {
            Position = position;
            Candidate = candidate;
            Axis = axis;
            AxisSign = axisSign >= 0 ? 1 : -1;
            Rotation = rotation;
            _coveredCells = (coveredCells ?? EnumerateLine(position, axis, AxisSign, candidate?.Length ?? 1))
                .Distinct()
                .ToArray();
            if (_coveredCells.Length == 0)
                _coveredCells = new[] { position };
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? candidate?.DisplayName ?? "Selected block"
                : displayName;
        }

        internal Vector3i Position { get; }

        internal SmartBlockCandidate Candidate { get; }

        internal SmartBuildAxis Axis { get; }

        internal int AxisSign { get; }

        internal Quaternion Rotation { get; }

        internal string DisplayName { get; }

        internal int NodeId { get; private set; } = -1;

        internal int PatternInstanceId { get; private set; } = -1;

        internal int Length => _coveredCells.Length;

        internal static Quaternion RotationForAxis(SmartBuildAxis axis, int sign = 1)
        {
            const float halfSqrt = 0.70710678118f;
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return sign >= 0
                        ? new Quaternion(0f, halfSqrt, 0f, halfSqrt)
                        : new Quaternion(0f, -halfSqrt, 0f, halfSqrt);
                case SmartBuildAxis.Y:
                    return sign >= 0
                        ? new Quaternion(-halfSqrt, 0f, 0f, halfSqrt)
                        : new Quaternion(halfSqrt, 0f, 0f, halfSqrt);
                default:
                    return sign >= 0
                        ? new Quaternion(0f, 0f, 0f, 1f)
                        : new Quaternion(0f, 1f, 0f, 0f);
            }
        }

        internal IReadOnlyList<Vector3i> CoveredCells() => _coveredCells;

        internal SmartBuildPlacement WithProvenance(int nodeId, int patternInstanceId)
        {
            NodeId = nodeId;
            PatternInstanceId = patternInstanceId;
            return this;
        }

        private static IEnumerable<Vector3i> EnumerateLine(
            Vector3i position,
            SmartBuildAxis axis,
            int sign,
            int length)
        {
            int direction = sign >= 0 ? 1 : -1;
            for (int index = 0; index < Math.Max(1, length); index++)
                yield return position + SmartBuildAxisHelper.ToVector3i(axis, index * direction);
        }
    }

    internal sealed class SmartBuildPlan
    {
        private IReadOnlyList<SmartBuildCellDiagnostic> _diagnostics;
        private IReadOnlyDictionary<string, SmartBuildNodeProvenance> _cellProvenance =
            new Dictionary<string, SmartBuildNodeProvenance>(StringComparer.Ordinal);
        internal SmartBuildPlan(
            SmartBuildVolume volume,
            IReadOnlyList<SmartBuildPlacement> placements,
            IReadOnlyList<Vector3i> skippedCells,
            IReadOnlyList<string> warnings,
            bool canCommit,
            string failureReason)
        {
            Volume = volume;
            Construct = volume?.Construct;
            ConstructToken = SmartBuildConstructToken.Capture(Construct);
            Placements = placements ?? Array.Empty<SmartBuildPlacement>();
            SkippedCells = skippedCells ?? Array.Empty<Vector3i>();
            Warnings = warnings ?? Array.Empty<string>();
            CanCommit = canCommit;
            FailureReason = failureReason;
        }

        internal SmartBuildPlan(
            AllConstruct construct,
            SmartBuildVolume volume,
            IReadOnlyList<SmartBuildPlacement> placements,
            IReadOnlyList<Vector3i> skippedCells,
            IReadOnlyList<string> warnings,
            bool canCommit,
            string failureReason)
        {
            Construct = construct ?? volume?.Construct;
            ConstructToken = SmartBuildConstructToken.Capture(Construct);
            Volume = volume;
            Placements = placements ?? Array.Empty<SmartBuildPlacement>();
            SkippedCells = skippedCells ?? Array.Empty<Vector3i>();
            Warnings = warnings ?? Array.Empty<string>();
            CanCommit = canCommit;
            FailureReason = failureReason;
        }

        internal AllConstruct Construct { get; }

        internal SmartBuildConstructToken ConstructToken { get; private set; }

        internal SmartBuildVolume Volume { get; }

        internal IReadOnlyList<SmartBuildPlacement> Placements { get; }

        internal IReadOnlyList<Vector3i> SkippedCells { get; }

        internal IReadOnlyList<string> Warnings { get; }

        internal IReadOnlyList<SmartBuildCellDiagnostic> Diagnostics =>
            _diagnostics ??= SmartBuildDiagnosticBuilder.FromPlan(this);

        internal SmartBuildPlan WithCellProvenance(
            IReadOnlyDictionary<string, SmartBuildNodeProvenance> provenance)
        {
            _cellProvenance = provenance ??
                new Dictionary<string, SmartBuildNodeProvenance>(StringComparer.Ordinal);
            _diagnostics = null;
            return this;
        }

        internal SmartBuildPlan WithConstructToken(SmartBuildConstructToken token)
        {
            ConstructToken = token ?? SmartBuildConstructToken.Capture(Construct);
            return this;
        }

        internal bool TryGetCellProvenance(
            Vector3i cell,
            out SmartBuildNodeProvenance provenance) =>
            _cellProvenance.TryGetValue(
                DecoLimitLifter.EsuSymmetry.CellKey(cell),
                out provenance);

        internal SmartBuildCommitOperation CommitOperation { get; private set; } =
            SmartBuildCommitOperation.Place;

        internal IReadOnlyList<Vector3i> RemovalCells { get; private set; } =
            Array.Empty<Vector3i>();

        internal IReadOnlyList<Vector3i> RemovalTouchedCells { get; private set; } =
            Array.Empty<Vector3i>();

        internal IReadOnlyList<Vector3i> RemovalFootprintCells { get; private set; } =
            Array.Empty<Vector3i>();

        internal IReadOnlyList<SmartBuildRemovalItem> RemovalItems { get; private set; } =
            Array.Empty<SmartBuildRemovalItem>();

        internal bool CanCommit { get; private set; }

        internal string FailureReason { get; private set; }

        internal int EstimatedBlockCount =>
            CommitOperation == SmartBuildCommitOperation.Erase
                ? RemovalItems.Count
                : Placements.Count;

        internal int CoveredCellCount =>
            CommitOperation == SmartBuildCommitOperation.Erase
                ? RemovalFootprintCells.Count
                : Placements.Sum(placement => placement.Length);

        internal SmartBuildPlan WithCommitOperation(
            SmartBuildCommitOperation operation,
            IEnumerable<Vector3i> removalCells)
        {
            _diagnostics = null;
            Vector3i[] touched = DistinctSorted(removalCells);
            if (operation == SmartBuildCommitOperation.Place)
                return WithCommitOperation(operation, Array.Empty<SmartBuildRemovalItem>());

            if (Construct != null)
            {
                if (!SmartBuildRemovalPlanner.TryResolveRemovalItems(
                        Construct,
                        touched,
                        out IReadOnlyList<SmartBuildRemovalItem> resolved,
                        out string reason))
                {
                    CommitOperation = operation;
                    RemovalTouchedCells = touched;
                    RemovalCells = Array.Empty<Vector3i>();
                    RemovalFootprintCells = Array.Empty<Vector3i>();
                    RemovalItems = Array.Empty<SmartBuildRemovalItem>();
                    CanCommit = false;
                    FailureReason = reason ??
                        "The complete craft-item removal footprints could not be resolved.";
                    return this;
                }

                return WithCommitOperation(operation, resolved);
            }

            // Pure planner verification has no live craft. Preserve the legacy
            // single-cell interpretation while still exposing complete item data.
            return WithCommitOperation(
                operation,
                touched.Select(SmartBuildRemovalPlanner.ForUnresolvedVerificationCell));
        }

        internal SmartBuildPlan WithCommitOperation(
            SmartBuildCommitOperation operation,
            IEnumerable<SmartBuildRemovalItem> removalItems)
        {
            _diagnostics = null;
            CommitOperation = operation;
            SmartBuildRemovalItem[] items = (removalItems ?? Array.Empty<SmartBuildRemovalItem>())
                .Where(item => item?.Item != null)
                .GroupBy(item => item.Item.ItemKey, StringComparer.Ordinal)
                .Select(group => new SmartBuildRemovalItem(
                    group.First().Item,
                    group.SelectMany(item => item.TouchedCells)))
                .OrderBy(item => item.CommandCell.x)
                .ThenBy(item => item.CommandCell.y)
                .ThenBy(item => item.CommandCell.z)
                .ToArray();
            RemovalItems = items;
            RemovalCells = items.Select(item => item.CommandCell).ToArray();
            RemovalTouchedCells = DistinctSorted(items.SelectMany(item => item.TouchedCells));
            RemovalFootprintCells = DistinctSorted(items.SelectMany(item => item.FootprintCells));
            return this;
        }

        private static Vector3i[] DistinctSorted(IEnumerable<Vector3i> cells) =>
            (cells ?? Array.Empty<Vector3i>())
            .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
            .Select(group => group.First())
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z)
            .ToArray();

        internal static SmartBuildPlan Failed(
            SmartBuildVolume volume,
            string reason,
            IEnumerable<Vector3i> skippedCells = null,
            IEnumerable<string> warnings = null) =>
            new SmartBuildPlan(
                volume,
                Array.Empty<SmartBuildPlacement>(),
                (skippedCells ?? Array.Empty<Vector3i>()).ToArray(),
                (warnings ?? Array.Empty<string>()).ToArray(),
                canCommit: false,
                failureReason: reason);
    }

    internal static class SmartBuildPlanner
    {
        internal static SmartBuildPlan BuildPlan(
            SmartBuildVolume volume,
            SmartBlockFamily family,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options = null)
        {
            options ??= new SmartBuildPlannerOptions();
            if (volume == null)
                return SmartBuildPlan.Failed(null, "No preview volume is active.");
            if (!SmartBuildLimits.TryProductWithinLimit(
                    volume.LengthU,
                    volume.LengthV,
                    volume.Thickness,
                    SmartBuildLimits.MaximumPlannerInputCells,
                    out _))
            {
                return SmartBuildPlan.Failed(
                    volume,
                    PlannerInputLimitReason());
            }

            return BuildPlanFromCells(
                volume,
                volume.EnumerateCells(),
                volume.GrainAxis,
                family,
                isOccupied,
                options);
        }

        internal static SmartBuildPlan BuildPlanFromCells(
            SmartBuildVolume referenceVolume,
            IEnumerable<Vector3i> cells,
            SmartBuildAxis grain,
            SmartBlockFamily family,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options = null) =>
            BuildPlanFromCells(
                referenceVolume,
                cells,
                grain,
                1,
                family,
                isOccupied,
                options);

        internal static SmartBuildPlan BuildPlanFromCells(
            SmartBuildVolume referenceVolume,
            IEnumerable<Vector3i> cells,
            SmartBuildAxis grain,
            int grainSign,
            SmartBlockFamily family,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options = null)
        {
            options ??= new SmartBuildPlannerOptions();
            grainSign = grainSign >= 0 ? 1 : -1;
            if (referenceVolume == null)
                return SmartBuildPlan.Failed(null, "No preview volume is active.");
            if (family == null || !family.IsSupported)
                return SmartBuildPlan.Failed(
                    referenceVolume,
                    family?.UnsupportedReason ?? "The selected material or shape cannot be used by Smart Block Builder.");
            if (!family.HasSingleCell)
                return SmartBuildPlan.Failed(
                    referenceVolume,
                    "The selected family has no 1m fallback block.");

            int hardPlacementCap = SmartBuildLimits.BoundedPositiveLimit(
                options.HardPlacementCap,
                SmartBuildLimits.HardPlacementCount);
            int warningPlacementCap = Math.Min(
                hardPlacementCap,
                SmartBuildLimits.BoundedPositiveLimit(
                    options.WarningPlacementCap,
                    SmartBuildLimits.WarningPlacementCount));

            Vector3i[] boundedCells;
            try
            {
                if (!SmartBuildLimits.TryMaterializeBounded(
                        cells,
                        SmartBuildLimits.MaximumPlannerInputCells,
                        out boundedCells,
                        out _))
                {
                    return SmartBuildPlan.Failed(
                        referenceVolume,
                        PlannerInputLimitReason());
                }
            }
            catch (Exception exception)
            {
                return SmartBuildPlan.Failed(
                    referenceVolume,
                    "The preview cells could not be enumerated safely: " + exception.Message);
            }

            var target = new HashSet<Vector3i>();
            var skipped = new List<Vector3i>();
            foreach (Vector3i cell in boundedCells)
            {
                if (isOccupied != null && isOccupied(cell))
                {
                    if (options.AllowOccupiedCells)
                    {
                        target.Add(cell);
                        continue;
                    }
                    if (!options.SkipOccupiedCells)
                        return SmartBuildPlan.Failed(
                            referenceVolume,
                            "The preview intersects existing blocks.",
                            new[] { cell });
                    skipped.Add(cell);
                    continue;
                }

                target.Add(cell);
            }

            if (target.Count == 0)
                return SmartBuildPlan.Failed(
                    referenceVolume,
                    "No empty cells are available in the preview.",
                    skipped);

            var placements = new List<SmartBuildPlacement>();
            var covered = new HashSet<Vector3i>();
            SmartBlockCandidate[] candidates = family.Candidates
                .Where(candidate =>
                    candidate.Length >= 1 &&
                    candidate.Length <= SmartBuildLimits.MaximumNativeBlockLength)
                .OrderByDescending(candidate => candidate.Length)
                .ToArray();

            foreach (Vector3i cell in SortForPacking(target, grain, grainSign))
            {
                if (covered.Contains(cell))
                    continue;

                SmartBlockCandidate chosen = null;
                foreach (SmartBlockCandidate candidate in candidates)
                {
                    if (CanCover(cell, grain, grainSign, candidate.Length, target, covered))
                    {
                        chosen = candidate;
                        break;
                    }
                }

                if (chosen == null)
                    return SmartBuildPlan.Failed(
                        referenceVolume,
                        "The planner could not cover every target cell with legal blocks.",
                        skipped);

                var placement = new SmartBuildPlacement(cell, chosen, grain, grainSign);
                placements.Add(placement);
                if (placements.Count > hardPlacementCap)
                {
                    return new SmartBuildPlan(
                        referenceVolume,
                        placements,
                        skipped,
                        Array.Empty<string>(),
                        canCommit: false,
                        failureReason:
                        $"The plan needs more than {hardPlacementCap:N0} placements, above the hard cap.");
                }
                foreach (Vector3i coveredCell in placement.CoveredCells())
                    covered.Add(coveredCell);
            }

            var warnings = new List<string>();
            if (placements.Count > warningPlacementCap)
                warnings.Add(
                    $"Large plan: {placements.Count:N0} placements. Commit may hitch.");

            return new SmartBuildPlan(
                referenceVolume,
                placements,
                skipped,
                warnings,
                canCommit: true,
                failureReason: null);
        }

        private static string PlannerInputLimitReason() =>
            "The preview contains more than " +
            SmartBuildLimits.MaximumPlannerInputCells.ToString("N0") +
            " cells, above the bounded planning limit.";

        private static IEnumerable<Vector3i> SortForPacking(
            IEnumerable<Vector3i> cells,
            SmartBuildAxis grain,
            int grainSign)
        {
            switch (grain)
            {
                case SmartBuildAxis.X:
                    return grainSign >= 0
                        ? cells.OrderBy(cell => cell.y).ThenBy(cell => cell.z).ThenBy(cell => cell.x)
                        : cells.OrderBy(cell => cell.y).ThenBy(cell => cell.z).ThenByDescending(cell => cell.x);
                case SmartBuildAxis.Y:
                    return grainSign >= 0
                        ? cells.OrderBy(cell => cell.x).ThenBy(cell => cell.z).ThenBy(cell => cell.y)
                        : cells.OrderBy(cell => cell.x).ThenBy(cell => cell.z).ThenByDescending(cell => cell.y);
                default:
                    return grainSign >= 0
                        ? cells.OrderBy(cell => cell.x).ThenBy(cell => cell.y).ThenBy(cell => cell.z)
                        : cells.OrderBy(cell => cell.x).ThenBy(cell => cell.y).ThenByDescending(cell => cell.z);
            }
        }

        private static bool CanCover(
            Vector3i start,
            SmartBuildAxis axis,
            int axisSign,
            int length,
            HashSet<Vector3i> target,
            HashSet<Vector3i> covered)
        {
            int direction = axisSign >= 0 ? 1 : -1;
            for (int index = 0; index < length; index++)
            {
                Vector3i cell = start + SmartBuildAxisHelper.ToVector3i(axis, index * direction);
                if (!target.Contains(cell) || covered.Contains(cell))
                    return false;
            }

            return true;
        }
    }
}
