using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// Immutable source predicate captured from the exact craft-block eyedropper.
    /// Any populated field participates in the match, allowing material-only,
    /// shape-only, material-and-shape, or exact-definition replacement.
    /// </summary>
    internal sealed class SmartBuildExistingItemMatch
    {
        internal SmartBuildExistingItemMatch(
            SmartBuildMaterial? material,
            string shapeDescriptorKey,
            Guid definitionGuid = default)
        {
            Material = material;
            ShapeDescriptorKey = shapeDescriptorKey ?? string.Empty;
            DefinitionGuid = definitionGuid;
        }

        internal SmartBuildMaterial? Material { get; }

        internal string ShapeDescriptorKey { get; }

        internal Guid DefinitionGuid { get; }

        internal bool IsEmpty =>
            !Material.HasValue &&
            string.IsNullOrWhiteSpace(ShapeDescriptorKey) &&
            DefinitionGuid == Guid.Empty;

        internal bool Matches(SmartBuildExistingItemSnapshot item)
        {
            if (item == null || IsEmpty)
                return false;
            if (Material.HasValue && item.Material != Material)
                return false;
            if (!string.IsNullOrWhiteSpace(ShapeDescriptorKey) &&
                !string.Equals(
                    ShapeDescriptorKey,
                    item.ShapeDescriptorKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return DefinitionGuid == Guid.Empty || item.DefinitionGuid == DefinitionGuid;
        }

        internal static SmartBuildExistingItemMatch ForMaterial(SmartBuildMaterial material) =>
            new SmartBuildExistingItemMatch(material, null);

        internal static SmartBuildExistingItemMatch ForShape(string shapeDescriptorKey) =>
            new SmartBuildExistingItemMatch(null, shapeDescriptorKey);

        internal static SmartBuildExistingItemMatch ForMaterialAndShape(
            SmartBuildMaterial material,
            string shapeDescriptorKey) =>
            new SmartBuildExistingItemMatch(material, shapeDescriptorKey);

        internal static SmartBuildExistingItemMatch ForExactDefinition(Guid definitionGuid) =>
            new SmartBuildExistingItemMatch(null, null, definitionGuid);
    }

    internal sealed class SmartBuildReplacementTarget
    {
        internal SmartBuildReplacementTarget(
            SmartBuildSource source,
            SmartBuildShapeDescriptor shape)
        {
            Source = source;
            Shape = shape;
        }

        internal SmartBuildSource Source { get; }

        internal SmartBuildShapeDescriptor Shape { get; }

        internal string Label =>
            (Source?.DisplayName ?? "Selected material") + " " +
            (Shape?.Label ?? "shape");

        internal bool TryCandidateFor(
            SmartBuildExistingItemSnapshot existing,
            out SmartBlockCandidate candidate,
            out string reason)
        {
            candidate = null;
            if (Source == null || Shape == null)
            {
                reason = "Choose a replacement material and shape before planning.";
                return false;
            }
            if (Shape.IsGenerator)
            {
                reason = "Conditional craft replacement requires one native structural block shape.";
                return false;
            }

            SmartBlockFamily family = Source.FamilyForShape(Shape);
            if (family?.IsSupported != true)
            {
                reason = family?.UnsupportedReason ??
                    "The replacement material/shape family is unavailable.";
                return false;
            }

            candidate = family.Candidates.FirstOrDefault(
                possible => possible != null && possible.Length == existing.Length);
            if (candidate == null)
            {
                reason =
                    $"{Label} has no exact {existing.Length:N0}m item for the matched craft block.";
                return false;
            }

            reason = null;
            return true;
        }
    }

    internal sealed class SmartBuildConditionalReplacementLimits
    {
        internal int HardScopeCellCap { get; set; } =
            SmartBuildLimits.MaximumConditionalScopeCells;

        internal int HardItemCap { get; set; } =
            SmartBuildLimits.MaximumConditionalItems;

        internal int HardFootprintCellCap { get; set; } =
            SmartBuildLimits.MaximumConditionalFootprintCells;
    }

    internal sealed class SmartBuildConditionalReplacementPlan
    {
        internal SmartBuildConditionalReplacementPlan(
            SmartBuildExistingItemMatch sourceMatch,
            IReadOnlyList<SmartBuildRemovalItem> removals,
            IReadOnlyList<SmartBuildPlacement> placements,
            IReadOnlyList<Vector3i> removedFootprintCells,
            IReadOnlyList<Vector3i> replacementFootprintCells)
        {
            SourceMatch = sourceMatch;
            Removals = removals ?? Array.Empty<SmartBuildRemovalItem>();
            Placements = placements ?? Array.Empty<SmartBuildPlacement>();
            RemovedFootprintCells = removedFootprintCells ?? Array.Empty<Vector3i>();
            ReplacementFootprintCells = replacementFootprintCells ?? Array.Empty<Vector3i>();
        }

        internal SmartBuildExistingItemMatch SourceMatch { get; }

        internal IReadOnlyList<SmartBuildRemovalItem> Removals { get; }

        internal IReadOnlyList<SmartBuildPlacement> Placements { get; }

        internal IReadOnlyList<Vector3i> RemovedFootprintCells { get; }

        internal IReadOnlyList<Vector3i> ReplacementFootprintCells { get; }

        internal int MatchedItemCount => Removals.Count;

        internal SmartBuildPlan ToSmartBuildPlan(
            AllConstruct construct,
            SmartBuildVolume referenceVolume)
        {
            var plan = new SmartBuildPlan(
                construct,
                referenceVolume,
                Placements,
                Array.Empty<Vector3i>(),
                new[]
                {
                    $"Conditional replace removes {Removals.Count:N0} complete craft item(s) before placement."
                },
                canCommit: Removals.Count > 0 && Placements.Count > 0,
                failureReason: Removals.Count == 0 || Placements.Count == 0
                    ? "Conditional replacement produced no complete item pairs."
                    : null);
            return plan.WithCommitOperation(SmartBuildCommitOperation.Replace, Removals);
        }
    }

    internal static class SmartBuildConditionalReplacementPlanner
    {
        internal static bool TryBuildCraftPlan(
            AllConstruct construct,
            SmartBuildVolume referenceVolume,
            IEnumerable<Vector3i> scopeCells,
            SmartBuildExistingItemMatch sourceMatch,
            SmartBuildReplacementTarget target,
            SmartBuildConditionalReplacementLimits limits,
            out SmartBuildPlan plan,
            out SmartBuildConditionalReplacementPlan details,
            out string reason)
        {
            plan = null;
            details = null;
            reason = null;
            if (construct?.AllBasics == null || referenceVolume == null)
            {
                reason = "A live construct and Smart Builder preview volume are required.";
                return false;
            }
            if (target == null)
            {
                reason = "Choose a replacement material and shape before planning.";
                return false;
            }

            limits ??= new SmartBuildConditionalReplacementLimits();
            int resolutionFootprintCap = SmartBuildLimits.BoundedPositiveLimit(
                limits.HardFootprintCellCap,
                SmartBuildLimits.MaximumConditionalFootprintCells);

            string craftResolutionFailure = null;
            string targetResolutionFailure = null;
            var resolvedByCell = new Dictionary<string, SmartBuildExistingItemSnapshot>(
                StringComparer.Ordinal);

            SmartBuildExistingItemSnapshot Resolve(Vector3i cell)
            {
                string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                if (resolvedByCell.TryGetValue(key, out SmartBuildExistingItemSnapshot cached))
                    return cached;

                try
                {
                    if (construct.AllBasics.GetBlockViaLocalPosition(cell) == null)
                        return null;
                }
                catch (Exception exception)
                {
                    craftResolutionFailure ??=
                        "Craft block lookup failed during conditional preflight: " + exception.Message;
                    return null;
                }

                if (!SmartBuildRemovalPlanner.TryResolveExistingItem(
                        construct,
                        cell,
                        classify: true,
                        out SmartBuildExistingItemSnapshot item,
                        out string resolveReason))
                {
                    craftResolutionFailure ??= resolveReason ??
                        "An occupied craft item could not be resolved exactly.";
                    return null;
                }

                if (item.FootprintCells.Count > resolutionFootprintCap)
                {
                    craftResolutionFailure ??=
                        $"A craft item footprint exceeds the {resolutionFootprintCap:N0}-cell replacement cap.";
                    return null;
                }

                foreach (Vector3i footprintCell in item.FootprintCells)
                {
                    resolvedByCell[DecoLimitLifter.EsuSymmetry.CellKey(footprintCell)] = item;
                }

                return item;
            }

            bool Occupied(Vector3i cell)
            {
                try
                {
                    return construct.AllBasics.GetBlockViaLocalPosition(cell) != null;
                }
                catch (Exception exception)
                {
                    // An unreadable cell is conservatively occupied so atomic
                    // preflight rejects it rather than replacing around it.
                    craftResolutionFailure ??=
                        "Craft occupancy lookup failed during conditional preflight: " + exception.Message;
                    return true;
                }
            }

            bool planned = TryPlanSnapshots(
                    referenceVolume,
                    scopeCells,
                    Resolve,
                    Occupied,
                    sourceMatch,
                    existing =>
                    {
                        if (!target.TryCandidateFor(
                                existing,
                                out SmartBlockCandidate candidate,
                                out string targetReason))
                        {
                            targetResolutionFailure ??= targetReason;
                            return null;
                        }
                        return candidate?.Definition == null ? null : candidate;
                    },
                    limits,
                    out details,
                    out reason);
            if (!string.IsNullOrWhiteSpace(craftResolutionFailure))
            {
                plan = null;
                details = null;
                reason = craftResolutionFailure;
                return false;
            }
            if (!planned)
            {
                if (!string.IsNullOrWhiteSpace(targetResolutionFailure))
                    reason = targetResolutionFailure;
                return false;
            }

            plan = details.ToSmartBuildPlan(construct, referenceVolume);
            return plan.CanCommit;
        }

        internal static bool TryPlanSnapshots(
            SmartBuildVolume referenceVolume,
            IEnumerable<Vector3i> scopeCells,
            Func<Vector3i, SmartBuildExistingItemSnapshot> resolveItem,
            Func<Vector3i, bool> isOccupied,
            SmartBuildExistingItemMatch sourceMatch,
            Func<SmartBuildExistingItemSnapshot, SmartBlockCandidate> replacementForItem,
            SmartBuildConditionalReplacementLimits limits,
            out SmartBuildConditionalReplacementPlan plan,
            out string reason)
        {
            plan = null;
            reason = null;
            limits ??= new SmartBuildConditionalReplacementLimits();
            if (referenceVolume == null)
            {
                reason = "No Smart Builder preview volume bounds the replacement scan.";
                return false;
            }
            if (sourceMatch == null || sourceMatch.IsEmpty)
            {
                reason = "Choose at least one source material, shape, or exact definition filter.";
                return false;
            }
            if (resolveItem == null || replacementForItem == null)
            {
                reason = "Conditional replacement resolvers are unavailable.";
                return false;
            }

            int scopeCap = SmartBuildLimits.BoundedPositiveLimit(
                limits.HardScopeCellCap,
                SmartBuildLimits.MaximumConditionalScopeCells);
            int itemCap = SmartBuildLimits.BoundedPositiveLimit(
                limits.HardItemCap,
                SmartBuildLimits.MaximumConditionalItems);
            int footprintCap = SmartBuildLimits.BoundedPositiveLimit(
                limits.HardFootprintCellCap,
                SmartBuildLimits.MaximumConditionalFootprintCells);

            Vector3i[] boundedScope;
            try
            {
                if (!SmartBuildLimits.TryMaterializeBounded(
                        scopeCells,
                        scopeCap,
                        out boundedScope,
                        out _))
                {
                    reason =
                        $"Conditional replacement scan has more than {scopeCap:N0} cells, above the hard cap.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "Conditional replacement scope could not be enumerated safely: " + exception.Message;
                return false;
            }

            Vector3i[] scope = DistinctSorted(boundedScope);
            if (scope.Length == 0)
            {
                reason = "The selected Smart Builder preview volumes contain no scan cells.";
                return false;
            }
            if (scope.Length > scopeCap)
            {
                reason =
                    $"Conditional replacement scan has {scope.Length:N0} cells, above the {scopeCap:N0} hard cap.";
                return false;
            }

            var matched = new Dictionary<string, SnapshotAndTouches>(StringComparer.Ordinal);
            try
            {
                foreach (Vector3i cell in scope)
                {
                    SmartBuildExistingItemSnapshot item = resolveItem(cell);
                    if (item == null || !sourceMatch.Matches(item))
                        continue;

                    if (!matched.TryGetValue(item.ItemKey, out SnapshotAndTouches entry))
                    {
                        if (matched.Count >= itemCap)
                        {
                            reason =
                                $"Conditional replacement matched more than {itemCap:N0} items, above the hard cap.";
                            return false;
                        }

                        entry = new SnapshotAndTouches(item);
                        matched.Add(item.ItemKey, entry);
                    }
                    else if (!SameSnapshot(entry.Item, item))
                    {
                        reason = "Conditional preflight observed inconsistent metadata for one craft item.";
                        return false;
                    }

                    entry.Touched.Add(cell);
                }
            }
            catch (Exception exception)
            {
                reason = "Conditional replacement scan failed: " + exception.Message;
                return false;
            }

            if (matched.Count == 0)
            {
                reason = "No complete craft items in the selected preview volumes match the source filter.";
                return false;
            }

            SnapshotAndTouches[] ordered = matched.Values
                .OrderBy(entry => entry.Item.Origin.x)
                .ThenBy(entry => entry.Item.Origin.y)
                .ThenBy(entry => entry.Item.Origin.z)
                .ToArray();

            long aggregateFootprintCells = 0L;
            foreach (SnapshotAndTouches entry in ordered)
            {
                if (!SmartBuildLimits.TryAddWithinLimit(
                        aggregateFootprintCells,
                        entry.Item.FootprintCells.Count,
                        footprintCap,
                        out aggregateFootprintCells))
                {
                    reason =
                        $"Conditional replacement removes more than {footprintCap:N0} cells, above the hard cap.";
                    return false;
                }
            }

            var removals = ordered
                .Select(entry => new SmartBuildRemovalItem(entry.Item, entry.Touched))
                .ToArray();
            Vector3i[] removedFootprint = DistinctSorted(
                removals.SelectMany(removal => removal.FootprintCells));
            if (removedFootprint.Length > footprintCap)
            {
                reason =
                    $"Conditional replacement removes {removedFootprint.Length:N0} cells, above the {footprintCap:N0} hard cap.";
                return false;
            }

            var placements = new List<SmartBuildPlacement>(ordered.Length);
            var replacementCellOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            var replacementCells = new Dictionary<string, Vector3i>(StringComparer.Ordinal);
            foreach (SnapshotAndTouches entry in ordered)
            {
                SmartBlockCandidate candidate;
                try
                {
                    candidate = replacementForItem(entry.Item);
                }
                catch (Exception exception)
                {
                    reason = "Replacement target resolution failed: " + exception.Message;
                    return false;
                }

                if (candidate == null)
                {
                    reason =
                        $"The replacement target has no exact {entry.Item.Length:N0}m item for a matched craft block.";
                    return false;
                }
                if (candidate.Length != entry.Item.Length)
                {
                    reason =
                        $"The replacement target resolved to {candidate.Length:N0}m instead of the required exact {entry.Item.Length:N0}m item.";
                    return false;
                }

                Vector3i[] targetCells = candidate
                    .CoveredCellsFrom(entry.Item.Origin, entry.Item.Rotation)
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray();
                if (targetCells.Length == 0)
                {
                    reason = "A replacement item has no native target footprint.";
                    return false;
                }
                if (targetCells.Length > footprintCap)
                {
                    reason =
                        $"A replacement item footprint exceeds the {footprintCap:N0}-cell hard cap.";
                    return false;
                }

                foreach (Vector3i targetCell in targetCells)
                {
                    string cellKey = DecoLimitLifter.EsuSymmetry.CellKey(targetCell);
                    bool alreadyOwned = replacementCellOwner.TryGetValue(
                        cellKey,
                        out string otherItem);
                    if (alreadyOwned &&
                        !string.Equals(otherItem, entry.Item.ItemKey, StringComparison.Ordinal))
                    {
                        reason = "Replacement target footprints overlap each other; nothing was changed.";
                        return false;
                    }
                    if (!alreadyOwned && replacementCellOwner.Count >= footprintCap)
                    {
                        reason =
                            $"Conditional replacement places more than {footprintCap:N0} cells, above the hard cap.";
                        return false;
                    }
                    replacementCellOwner[cellKey] = entry.Item.ItemKey;
                    replacementCells[cellKey] = targetCell;

                    bool occupied;
                    try
                    {
                        occupied = isOccupied != null && isOccupied(targetCell);
                    }
                    catch (Exception exception)
                    {
                        reason = "Replacement occupancy preflight failed: " + exception.Message;
                        return false;
                    }

                    if (!occupied)
                        continue;

                    SmartBuildExistingItemSnapshot occupant;
                    try
                    {
                        occupant = resolveItem(targetCell);
                    }
                    catch (Exception exception)
                    {
                        reason = "Replacement occupancy resolution failed: " + exception.Message;
                        return false;
                    }

                    if (occupant == null || !matched.ContainsKey(occupant.ItemKey))
                    {
                        reason =
                            "A replacement footprint intersects an occupied craft item outside the matched removal set.";
                        return false;
                    }
                }

                SmartBuildAxis axis = SmartBuildAxisHelper.FromLargestComponent(
                    entry.Item.Rotation * Vector3.forward,
                    out int sign);
                placements.Add(new SmartBuildPlacement(
                    entry.Item.Origin,
                    candidate,
                    axis,
                    sign,
                    entry.Item.Rotation,
                    targetCells,
                    candidate.DisplayName));
            }

            if (replacementCellOwner.Count > footprintCap)
            {
                reason =
                    $"Conditional replacement places {replacementCellOwner.Count:N0} cells, above the {footprintCap:N0} hard cap.";
                return false;
            }

            plan = new SmartBuildConditionalReplacementPlan(
                sourceMatch,
                removals,
                placements,
                removedFootprint,
                replacementCells.Values
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray());
            return true;
        }

        private static bool SameSnapshot(
            SmartBuildExistingItemSnapshot left,
            SmartBuildExistingItemSnapshot right)
        {
            if (left == null || right == null ||
                !left.Origin.Equals(right.Origin) ||
                left.DefinitionGuid != right.DefinitionGuid ||
                Math.Abs(Quaternion.Dot(left.Rotation, right.Rotation)) < 0.99999f)
            {
                return false;
            }

            return left.FootprintCells
                .Select(DecoLimitLifter.EsuSymmetry.CellKey)
                .SequenceEqual(right.FootprintCells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
        }

        private static Vector3i[] DistinctSorted(IEnumerable<Vector3i> cells) =>
            (cells ?? Array.Empty<Vector3i>())
            .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
            .Select(group => group.First())
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z)
            .ToArray();

        private sealed class SnapshotAndTouches
        {
            internal SnapshotAndTouches(SmartBuildExistingItemSnapshot item)
            {
                Item = item;
            }

            internal SmartBuildExistingItemSnapshot Item { get; }

            internal List<Vector3i> Touched { get; } = new List<Vector3i>();
        }
    }
}
