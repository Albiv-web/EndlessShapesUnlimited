using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Modding.Types;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// Immutable craft-block metadata captured during destructive-plan preflight.
    /// The footprint is the complete native FtD item footprint, not merely the
    /// preview cells which happened to touch the item.
    /// </summary>
    internal sealed class SmartBuildExistingItemSnapshot
    {
        internal SmartBuildExistingItemSnapshot(
            Vector3i origin,
            Quaternion rotation,
            ItemDefinition definition,
            Guid definitionGuid,
            SmartBuildMaterial? material,
            string shapeDescriptorKey,
            int length,
            IEnumerable<Vector3i> footprintCells)
        {
            Origin = origin;
            Rotation = rotation;
            Definition = definition;
            DefinitionGuid = definitionGuid;
            Material = material;
            ShapeDescriptorKey = shapeDescriptorKey ?? string.Empty;
            Length = Math.Max(1, length);
            FootprintCells = SortCells(footprintCells, origin);
            ItemKey = DecoLimitLifter.EsuSymmetry.CellKey(origin);
        }

        internal Vector3i Origin { get; }

        internal Quaternion Rotation { get; }

        internal ItemDefinition Definition { get; }

        internal Guid DefinitionGuid { get; }

        internal SmartBuildMaterial? Material { get; }

        internal string ShapeDescriptorKey { get; }

        internal int Length { get; }

        internal IReadOnlyList<Vector3i> FootprintCells { get; }

        internal string ItemKey { get; }

        private static IReadOnlyList<Vector3i> SortCells(
            IEnumerable<Vector3i> cells,
            Vector3i fallback)
        {
            Vector3i[] sorted = (cells ?? Array.Empty<Vector3i>())
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.First())
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ToArray();
            return sorted.Length == 0 ? new[] { fallback } : sorted;
        }
    }

    internal sealed class SmartBuildRemovalItem
    {
        internal SmartBuildRemovalItem(
            SmartBuildExistingItemSnapshot item,
            IEnumerable<Vector3i> touchedCells)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            TouchedCells = (touchedCells ?? Array.Empty<Vector3i>())
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.First())
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ToArray();
        }

        internal SmartBuildExistingItemSnapshot Item { get; }

        internal Vector3i CommandCell => Item.Origin;

        internal IReadOnlyList<Vector3i> TouchedCells { get; }

        internal IReadOnlyList<Vector3i> FootprintCells => Item.FootprintCells;
    }

    internal static class SmartBuildRemovalPlanner
    {
        internal static bool TryResolveRemovalItems(
            AllConstruct construct,
            IEnumerable<Vector3i> touchedCells,
            out IReadOnlyList<SmartBuildRemovalItem> removals,
            out string reason)
        {
            removals = Array.Empty<SmartBuildRemovalItem>();
            reason = null;
            if (construct?.AllBasics == null)
            {
                reason = "The construct block store is unavailable for destructive preflight.";
                return false;
            }

            var byItem = new Dictionary<string, RemovalBuilder>(StringComparer.Ordinal);
            foreach (Vector3i touched in DistinctSorted(touchedCells))
            {
                if (!TryResolveExistingItem(
                        construct,
                        touched,
                        classify: false,
                        out SmartBuildExistingItemSnapshot item,
                        out reason))
                {
                    removals = Array.Empty<SmartBuildRemovalItem>();
                    return false;
                }

                if (!byItem.TryGetValue(item.ItemKey, out RemovalBuilder builder))
                {
                    builder = new RemovalBuilder(item);
                    byItem.Add(item.ItemKey, builder);
                }
                else if (!SnapshotsDescribeSameItem(builder.Item, item))
                {
                    reason = "Destructive preflight observed inconsistent metadata for one craft item.";
                    removals = Array.Empty<SmartBuildRemovalItem>();
                    return false;
                }

                builder.TouchedCells.Add(touched);
            }

            removals = byItem.Values
                .OrderBy(builder => builder.Item.Origin.x)
                .ThenBy(builder => builder.Item.Origin.y)
                .ThenBy(builder => builder.Item.Origin.z)
                .Select(builder => new SmartBuildRemovalItem(builder.Item, builder.TouchedCells))
                .ToArray();
            return true;
        }

        internal static bool TryResolveExistingItem(
            AllConstruct construct,
            Vector3i touchedCell,
            bool classify,
            out SmartBuildExistingItemSnapshot item,
            out string reason)
        {
            item = null;
            reason = null;
            if (construct?.AllBasics == null)
            {
                reason = "The construct block store is unavailable.";
                return false;
            }

            Block block;
            try
            {
                block = construct.AllBasics.GetBlockViaLocalPosition(touchedCell);
            }
            catch (Exception exception)
            {
                reason = "Craft block lookup failed: " + exception.Message;
                return false;
            }

            if (block == null || block.IsDeleted)
            {
                reason = "A touched craft item disappeared before destructive preflight completed.";
                return false;
            }

            ItemDefinition definition = block.item;
            if (definition?.SizeInfo == null || definition.SizeInfo.ArrayPositionsUsed <= 0)
            {
                reason = "A touched craft item has no exact native footprint metadata.";
                return false;
            }

            Vector3i origin = block.LocalPosition;
            Quaternion rotation = block.LocalRotation;
            var footprint = new List<Vector3i>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int index = 0; index < definition.SizeInfo.ArrayPositionsUsed; index++)
                {
                    Vector3i cell = definition.SizeInfo.GetPosition(index, origin, rotation);
                    if (seen.Add(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                        footprint.Add(cell);
                }
            }
            catch (Exception exception)
            {
                reason = "Exact craft-item footprint resolution failed: " + exception.Message;
                return false;
            }

            if (footprint.Count == 0 ||
                !seen.Contains(DecoLimitLifter.EsuSymmetry.CellKey(touchedCell)))
            {
                reason = "The touched cell is not present in the craft item's native footprint.";
                return false;
            }

            Guid definitionGuid = DefinitionGuid(definition);
            foreach (Vector3i footprintCell in footprint)
            {
                Block occupant;
                try
                {
                    occupant = construct.AllBasics.GetBlockViaLocalPosition(footprintCell);
                }
                catch (Exception exception)
                {
                    reason = "Craft-item footprint validation failed: " + exception.Message;
                    return false;
                }

                if (occupant == null || occupant.IsDeleted ||
                    !occupant.LocalPosition.Equals(origin) ||
                    !SameDefinition(definition, definitionGuid, occupant.item))
                {
                    reason = "The craft changed while the complete removal footprint was being validated.";
                    return false;
                }
            }

            SmartBuildMaterial? material = null;
            string shapeKey = string.Empty;
            int length = footprint.Count;
            if (classify &&
                SmartBlockFamilyCatalog.TryIdentifyBlock(
                    definition,
                    out SmartBuildMaterial identifiedMaterial,
                    out _,
                    out SmartBlockCandidate candidate,
                    out _))
            {
                material = identifiedMaterial;
                shapeKey = candidate?.Descriptor?.Key ?? string.Empty;
                length = Math.Max(1, candidate?.Length ?? footprint.Count);
            }

            item = new SmartBuildExistingItemSnapshot(
                origin,
                rotation,
                definition,
                definitionGuid,
                material,
                shapeKey,
                length,
                footprint);
            return true;
        }

        internal static bool TryValidateRemovalItem(
            AllConstruct construct,
            SmartBuildRemovalItem planned,
            out string reason)
        {
            reason = null;
            if (planned?.Item == null)
            {
                reason = "The removal plan contains an invalid craft item.";
                return false;
            }

            if (!TryResolveExistingItem(
                    construct,
                    planned.CommandCell,
                    classify: false,
                    out SmartBuildExistingItemSnapshot current,
                    out reason))
            {
                return false;
            }

            if (!SnapshotsDescribeSameItem(planned.Item, current))
            {
                reason = "The craft item changed after the destructive preview was planned.";
                return false;
            }

            return true;
        }

        internal static SmartBuildRemovalItem ForUnresolvedVerificationCell(Vector3i cell) =>
            new SmartBuildRemovalItem(
                new SmartBuildExistingItemSnapshot(
                    cell,
                    Quaternion.identity,
                    null,
                    Guid.Empty,
                    null,
                    string.Empty,
                    1,
                    new[] { cell }),
                new[] { cell });

        private static bool SnapshotsDescribeSameItem(
            SmartBuildExistingItemSnapshot left,
            SmartBuildExistingItemSnapshot right)
        {
            if (left == null || right == null || !left.Origin.Equals(right.Origin))
                return false;
            if (!SameDefinition(left.Definition, left.DefinitionGuid, right.Definition))
                return false;
            if (Math.Abs(Quaternion.Dot(left.Rotation, right.Rotation)) < 0.99999f)
                return false;

            return left.FootprintCells
                .Select(DecoLimitLifter.EsuSymmetry.CellKey)
                .SequenceEqual(right.FootprintCells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
        }

        private static bool SameDefinition(
            ItemDefinition expected,
            Guid expectedGuid,
            ItemDefinition actual)
        {
            if (ReferenceEquals(expected, actual))
                return true;
            return expectedGuid != Guid.Empty && DefinitionGuid(actual) == expectedGuid;
        }

        private static Guid DefinitionGuid(ItemDefinition definition)
        {
            try
            {
                return definition?.ComponentId.Guid ?? Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        private static IEnumerable<Vector3i> DistinctSorted(IEnumerable<Vector3i> cells) =>
            (cells ?? Array.Empty<Vector3i>())
            .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
            .Select(group => group.First())
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z);

        private sealed class RemovalBuilder
        {
            internal RemovalBuilder(SmartBuildExistingItemSnapshot item)
            {
                Item = item;
            }

            internal SmartBuildExistingItemSnapshot Item { get; }

            internal List<Vector3i> TouchedCells { get; } = new List<Vector3i>();
        }
    }
}
