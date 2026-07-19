using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildPrecisionPositionMode
    {
        AbsoluteCraftLocal,
        RelativeCraftLocal
    }

    internal enum SmartBuildPrecisionAlignment
    {
        Minimum,
        Center,
        Maximum
    }

    internal enum SmartBuildPrecisionPivotMode
    {
        Primary,
        SelectionBoundsCenter,
        CustomCraftLocal
    }

    internal readonly struct SmartBuildPrecisionItem
    {
        internal SmartBuildPrecisionItem(
            int id,
            Vector3i origin,
            SmartBuildBounds bounds)
        {
            Id = id;
            Origin = origin;
            Bounds = bounds;
        }

        internal int Id { get; }

        internal Vector3i Origin { get; }

        internal SmartBuildBounds Bounds { get; }
    }

    internal readonly struct SmartBuildPrecisionMove
    {
        internal SmartBuildPrecisionMove(int pieceId, Vector3i delta)
        {
            PieceId = pieceId;
            Delta = delta;
        }

        internal int PieceId { get; }

        internal Vector3i Delta { get; }
    }

    internal sealed class SmartBuildPrecisionLayoutPlan
    {
        internal SmartBuildPrecisionLayoutPlan(
            string label,
            IEnumerable<SmartBuildPrecisionMove> moves,
            int selectionCount,
            bool roundedToWholeCells = false)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Precision layout" : label;
            Moves = (moves ?? Array.Empty<SmartBuildPrecisionMove>()).ToArray();
            SelectionCount = Math.Max(0, selectionCount);
            RoundedToWholeCells = roundedToWholeCells;
        }

        internal string Label { get; }

        internal IReadOnlyList<SmartBuildPrecisionMove> Moves { get; }

        internal int SelectionCount { get; }

        /// <summary>
        /// The grid is integral, so an indivisible free span is apportioned across
        /// gaps deterministically. In that case adjacent gaps differ by at most one cell.
        /// </summary>
        internal bool RoundedToWholeCells { get; }
    }

    internal sealed class SmartBuildPrecisionSelectionMetrics
    {
        internal SmartBuildPrecisionSelectionMetrics(
            SmartBuildBounds bounds,
            int primaryId,
            int nearestPieceId,
            Vector3i measuredClearGapCells)
        {
            Bounds = bounds;
            PrimaryId = primaryId;
            NearestPieceId = nearestPieceId;
            MeasuredClearGapCells = measuredClearGapCells;
        }

        internal SmartBuildBounds Bounds { get; }

        internal Vector3i Span => Bounds.Size;

        internal int PrimaryId { get; }

        internal int NearestPieceId { get; }

        internal bool HasMeasuredGap => NearestPieceId >= 0;

        /// <summary>
        /// Empty whole cells separating the primary from its nearest selected neighbor.
        /// Touching or overlapping occupied bounds report zero on that axis.
        /// </summary>
        internal Vector3i MeasuredClearGapCells { get; }
    }

    /// <summary>
    /// Pure Smart Builder precision math. Inputs and results are construct-local grid
    /// cells; this layer never mutates a scene or touches live construct state.
    /// </summary>
    internal static class SmartBuildPrecisionTools
    {
        internal const int MaximumCoordinateMagnitude =
            SmartBuildLimits.MaximumCoordinateMagnitude;

        internal static bool TryParseCellVector(
            string x,
            string y,
            string z,
            out Vector3i value,
            out string message)
        {
            value = default;
            if (!TryParseCell(x, out int parsedX) ||
                !TryParseCell(y, out int parsedY) ||
                !TryParseCell(z, out int parsedZ))
            {
                message = "Craft-local X, Y, and Z must be whole grid-cell numbers.";
                return false;
            }

            value = new Vector3i(parsedX, parsedY, parsedZ);
            if (!IsSafeCoordinate(value))
            {
                message = SafeCoordinateMessage();
                value = default;
                return false;
            }

            message = null;
            return true;
        }

        internal static bool TryTranslate(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            int primaryId,
            Vector3i value,
            SmartBuildPrecisionPositionMode mode,
            out SmartBuildPrecisionLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, 1, out SmartBuildPrecisionItem[] source, out message))
                return false;

            int primaryIndex = Array.FindIndex(source, item => item.Id == primaryId);
            if (primaryIndex < 0)
            {
                message = "The primary Smart Builder piece is not part of the selection.";
                return false;
            }

            Vector3i delta;
            if (mode == SmartBuildPrecisionPositionMode.RelativeCraftLocal)
            {
                delta = value;
            }
            else if (!TrySubtract(value, source[primaryIndex].Origin, out delta))
            {
                message = SafeCoordinateMessage();
                return false;
            }

            var moves = new List<SmartBuildPrecisionMove>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                if (!TryAddMove(source[index], delta, moves, out message))
                    return false;
            }

            if (moves.Count == 0)
            {
                message = mode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                    ? "The craft-local move is zero cells."
                    : "The primary piece is already at that craft-local origin.";
                return false;
            }

            plan = new SmartBuildPrecisionLayoutPlan(
                mode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                    ? "Move selection by craft-local delta"
                    : "Set primary craft-local origin",
                moves,
                source.Length);
            message = mode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                ? "Moved " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                  " selected piece(s) by " + FormatVector(delta) + " cells."
                : "Set the primary craft-local origin to " + FormatVector(value) +
                  " and preserved the selected group offsets.";
            return true;
        }

        internal static bool TryAlign(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            int primaryId,
            SmartBuildAxis axis,
            SmartBuildPrecisionAlignment alignment,
            out SmartBuildPrecisionLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, 2, out SmartBuildPrecisionItem[] source, out message))
                return false;

            int primaryIndex = Array.FindIndex(source, item => item.Id == primaryId);
            if (primaryIndex < 0)
            {
                message = "The primary Smart Builder piece is not part of the selection.";
                return false;
            }

            long targetTwice = AlignmentCoordinateTwice(source[primaryIndex].Bounds, axis, alignment);
            var moves = new List<SmartBuildPrecisionMove>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                SmartBuildPrecisionItem item = source[index];
                long currentTwice = AlignmentCoordinateTwice(item.Bounds, axis, alignment);
                long deltaTwice = targetTwice - currentTwice;
                if ((deltaTwice & 1L) != 0L)
                {
                    message =
                        "Center alignment cannot place every selected piece on whole cells; " +
                        "the occupied sizes have different parity on " + axis + ".";
                    return false;
                }

                long axisDelta = deltaTwice / 2L;
                if (axisDelta < int.MinValue || axisDelta > int.MaxValue)
                {
                    message = SafeCoordinateMessage();
                    return false;
                }

                Vector3i delta = AxisVector(axis, (int)axisDelta);
                if (!TryAddMove(item, delta, moves, out message))
                    return false;
            }

            if (moves.Count == 0)
            {
                message = "The selected pieces are already aligned on " + axis + ".";
                return false;
            }

            plan = new SmartBuildPrecisionLayoutPlan(
                "Align " + alignment + " on craft-local " + axis,
                moves,
                source.Length);
            message = "Aligned " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                      " selected piece(s) to the primary piece's " +
                      AlignmentLabel(alignment) + " on craft-local " + axis + ".";
            return true;
        }

        internal static bool TryDistributeEqualGaps(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            SmartBuildAxis axis,
            out SmartBuildPrecisionLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, 3, out SmartBuildPrecisionItem[] source, out message))
                return false;

            SmartBuildPrecisionItem[] ordered = source
                .OrderBy(item => CenterCoordinateTwice(item.Bounds, axis))
                .ThenBy(item => item.Id)
                .ToArray();

            long firstMinimum = Axis(ordered[0].Bounds.Min, axis);
            long lastMaximum = Axis(ordered[ordered.Length - 1].Bounds.Max, axis);
            long occupied = ordered.Sum(item => (long)item.Bounds.Extent(axis));
            long gapCount = ordered.Length - 1L;
            long totalGapCells = lastMaximum - firstMinimum + 1L - occupied;
            bool rounded = totalGapCells % gapCount != 0L;

            var targetMinimumById = new Dictionary<int, long>(ordered.Length);
            long occupiedBefore = 0L;
            for (int rank = 0; rank < ordered.Length; rank++)
            {
                long apportionedGap = RoundDivide(totalGapCells * rank, gapCount);
                long targetMinimum = firstMinimum + occupiedBefore + apportionedGap;
                targetMinimumById.Add(ordered[rank].Id, targetMinimum);
                occupiedBefore += ordered[rank].Bounds.Extent(axis);
            }

            var moves = new List<SmartBuildPrecisionMove>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                SmartBuildPrecisionItem item = source[index];
                long deltaOnAxis = targetMinimumById[item.Id] - Axis(item.Bounds.Min, axis);
                if (deltaOnAxis < int.MinValue || deltaOnAxis > int.MaxValue)
                {
                    message = SafeCoordinateMessage();
                    return false;
                }

                if (!TryAddMove(
                        item,
                        AxisVector(axis, (int)deltaOnAxis),
                        moves,
                        out message))
                {
                    return false;
                }
            }

            if (moves.Count == 0)
            {
                message = "The selected pieces already have equal whole-cell gaps on " + axis + ".";
                return false;
            }

            plan = new SmartBuildPrecisionLayoutPlan(
                "Distribute equal gaps on craft-local " + axis,
                moves,
                source.Length,
                rounded);
            message = "Distributed " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                      " selected piece(s) between the outer pair on craft-local " + axis + "." +
                      (rounded
                          ? " The indivisible span was rounded to whole cells; neighboring gaps differ by at most one cell."
                          : string.Empty);
            return true;
        }

        internal static bool TryResolvePivot(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            int primaryId,
            SmartBuildPrecisionPivotMode mode,
            Vector3i customCraftLocalPivot,
            out Vector3i pivot,
            out string message)
        {
            pivot = default;
            if (!TryValidateItems(items, 1, out SmartBuildPrecisionItem[] source, out message))
                return false;

            int primaryIndex = Array.FindIndex(source, item => item.Id == primaryId);
            if (primaryIndex < 0)
            {
                message = "The primary Smart Builder piece is not part of the selection.";
                return false;
            }

            switch (mode)
            {
                case SmartBuildPrecisionPivotMode.Primary:
                    pivot = RoundedBoundsCenter(source[primaryIndex].Bounds);
                    break;
                case SmartBuildPrecisionPivotMode.SelectionBoundsCenter:
                    pivot = RoundedBoundsCenter(AggregateBounds(source));
                    break;
                case SmartBuildPrecisionPivotMode.CustomCraftLocal:
                    pivot = customCraftLocalPivot;
                    break;
                default:
                    message = "The Smart Builder precision pivot mode is unsupported.";
                    return false;
            }

            if (!IsSafeCoordinate(pivot))
            {
                message = SafeCoordinateMessage();
                pivot = default;
                return false;
            }

            message = mode == SmartBuildPrecisionPivotMode.Primary
                ? "Using the primary piece pivot cell."
                : mode == SmartBuildPrecisionPivotMode.SelectionBoundsCenter
                    ? "Using the selection-bounds center pivot cell."
                    : "Using the custom craft-local pivot cell.";
            return true;
        }

        internal static bool TryRotatePieces(
            IReadOnlyList<SmartBuildPiece> pieces,
            DecorationEditAxis axis,
            int quarterTurns,
            Vector3i pivot,
            out IReadOnlyList<SmartBuildPiece> rotated,
            out string message)
        {
            rotated = Array.Empty<SmartBuildPiece>();
            SmartBuildPiece[] source = (pieces ?? Array.Empty<SmartBuildPiece>()).ToArray();
            if (source.Length == 0)
            {
                message = "Select at least one primitive Smart Builder piece to rotate.";
                return false;
            }
            if (source.Any(piece => piece == null) ||
                source.Select(piece => piece.Id).Distinct().Count() != source.Length)
            {
                message = "The rotation selection contains a missing or duplicate primitive.";
                return false;
            }
            if (axis != DecorationEditAxis.X &&
                axis != DecorationEditAxis.Y &&
                axis != DecorationEditAxis.Z)
            {
                message = "Quarter-turn rotation requires craft-local X, Y, or Z.";
                return false;
            }
            if (quarterTurns != 1 && quarterTurns != -1)
            {
                message = "Precision rotation accepts one positive or negative quarter turn.";
                return false;
            }
            if (!IsSafeCoordinate(pivot) || source.Any(piece => !IsSafeBounds(piece.Bounds)))
            {
                message = SafeCoordinateMessage();
                return false;
            }

            var staged = new List<SmartBuildPiece>(source.Length);
            try
            {
                foreach (SmartBuildPiece piece in source)
                {
                    SmartBuildPiece clone = piece.Clone();
                    clone.RotateAroundAxis(axis, quarterTurns, pivot);
                    if (!IsSafeBounds(clone.Bounds))
                    {
                        message = SafeCoordinateMessage();
                        return false;
                    }
                    staged.Add(clone);
                }
            }
            catch (Exception exception)
            {
                message = "The selected group could not be quarter-turned: " + exception.Message;
                return false;
            }

            rotated = staged;
            message = "Rotated " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                      " primitive piece(s) " + (quarterTurns > 0 ? "+90" : "-90") +
                      " degrees around craft-local " + axis + " at " + FormatVector(pivot) + ".";
            return true;
        }

        internal static bool TryResizePrimitive(
            SmartBuildPiece piece,
            Vector3i dimensions,
            out SmartBuildPiece resized,
            out string message)
        {
            resized = null;
            if (piece == null)
            {
                message = "Select one primitive Smart Builder piece to edit dimensions.";
                return false;
            }
            if (dimensions.x < 1 || dimensions.y < 1 || dimensions.z < 1)
            {
                message = "Primitive X, Y, and Z dimensions must each be at least one cell.";
                return false;
            }

            SmartBuildBounds current = piece.GroupScaleBounds;
            if (!IsSafeBounds(current))
            {
                message = SafeCoordinateMessage();
                return false;
            }
            if (current.Size.Equals(dimensions))
            {
                message = "The selected primitive already has those dimensions.";
                return false;
            }

            Vector3 pivot = new Vector3(
                current.Min.x - 0.5f,
                current.Min.y - 0.5f,
                current.Min.z - 0.5f);
            Vector3 factors = new Vector3(
                dimensions.x / (float)current.Size.x,
                dimensions.y / (float)current.Size.y,
                dimensions.z / (float)current.Size.z);
            SmartBuildPiece clone;
            string reason;
            try
            {
                clone = piece.Clone();
                if (!clone.TryScaleAboutPivot(pivot, factors, out reason))
                {
                    message = string.IsNullOrWhiteSpace(reason)
                        ? "That shape cannot represent the requested exact dimensions."
                        : reason;
                    return false;
                }
            }
            catch (Exception exception)
            {
                message = "The primitive dimensions could not be prepared: " + exception.Message;
                return false;
            }

            SmartBuildBounds result = clone.GroupScaleBounds;
            if (!result.Min.Equals(current.Min) ||
                !result.Size.Equals(dimensions) ||
                !IsSafeBounds(result))
            {
                message = "That shape cannot represent the requested exact anchored dimensions.";
                return false;
            }

            resized = clone;
            message = PrimitiveDimensionLabel(piece) + " dimensions set to " +
                      FormatVector(dimensions) + " craft-local cells.";
            return true;
        }

        internal static string PrimitiveDimensionLabel(SmartBuildPiece piece)
        {
            if (piece == null)
                return "Primitive";
            if (piece.ShapeKind == SmartBuildShapeKind.Cuboid)
                return "Cuboid";
            if (piece.ShapeKind == SmartBuildShapeKind.DownSlope)
                return "Slope occupied bounds";
            if (piece.IsGeneratedShape)
                return "Generator control bounds";
            if (piece.IsFixedGeometry)
                return "Structural-shape occupied bounds";
            return "Primitive occupied bounds";
        }

        internal static bool TrySnapSelectionToFace(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            SmartBuildAxis faceAxis,
            int faceSign,
            Vector3i adjacentCraftCell,
            out SmartBuildPrecisionLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, 1, out SmartBuildPrecisionItem[] source, out message))
                return false;
            if ((faceAxis != SmartBuildAxis.X &&
                 faceAxis != SmartBuildAxis.Y &&
                 faceAxis != SmartBuildAxis.Z) ||
                (faceSign != -1 && faceSign != 1))
            {
                message = "Craft-face snapping requires an exact +/-X, +/-Y, or +/-Z face.";
                return false;
            }
            if (!IsSafeCoordinate(adjacentCraftCell))
            {
                message = SafeCoordinateMessage();
                return false;
            }

            SmartBuildBounds selection = AggregateBounds(source);
            int target = Axis(adjacentCraftCell, faceAxis);
            int current = faceSign >= 0
                ? Axis(selection.Min, faceAxis)
                : Axis(selection.Max, faceAxis);
            long axisDelta = (long)target - current;
            if (axisDelta < int.MinValue || axisDelta > int.MaxValue)
            {
                message = SafeCoordinateMessage();
                return false;
            }

            Vector3i delta = AxisVector(faceAxis, (int)axisDelta);
            var moves = new List<SmartBuildPrecisionMove>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                if (!TryAddMove(source[index], delta, moves, out message))
                    return false;
            }
            if (moves.Count == 0)
            {
                message = "The selected group is already flush with that craft face.";
                return false;
            }

            plan = new SmartBuildPrecisionLayoutPlan(
                "Snap selection to pointed craft face",
                moves,
                source.Length);
            message = "Snapped " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                      " selected piece(s) flush outside the pointed craft face on " +
                      (faceSign >= 0 ? "+" : "-") + faceAxis + ".";
            return true;
        }

        internal static bool TryMeasureSelection(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            int primaryId,
            out SmartBuildPrecisionSelectionMetrics metrics,
            out string message)
        {
            metrics = null;
            if (!TryValidateItems(items, 1, out SmartBuildPrecisionItem[] source, out message))
                return false;

            int primaryIndex = Array.FindIndex(source, item => item.Id == primaryId);
            if (primaryIndex < 0)
            {
                message = "The primary Smart Builder piece is not part of the measurement selection.";
                return false;
            }

            SmartBuildPrecisionItem primary = source[primaryIndex];
            SmartBuildPrecisionItem? nearest = null;
            long nearestDistance = long.MaxValue;
            foreach (SmartBuildPrecisionItem candidate in source)
            {
                if (candidate.Id == primary.Id)
                    continue;

                long dx = CenterCoordinateTwice(candidate.Bounds, SmartBuildAxis.X) -
                          CenterCoordinateTwice(primary.Bounds, SmartBuildAxis.X);
                long dy = CenterCoordinateTwice(candidate.Bounds, SmartBuildAxis.Y) -
                          CenterCoordinateTwice(primary.Bounds, SmartBuildAxis.Y);
                long dz = CenterCoordinateTwice(candidate.Bounds, SmartBuildAxis.Z) -
                          CenterCoordinateTwice(primary.Bounds, SmartBuildAxis.Z);
                long distance = dx * dx + dy * dy + dz * dz;
                if (distance < nearestDistance ||
                    (distance == nearestDistance &&
                     (!nearest.HasValue || candidate.Id < nearest.Value.Id)))
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            Vector3i gap = nearest.HasValue
                ? new Vector3i(
                    ClearGap(primary.Bounds.Min.x, primary.Bounds.Max.x, nearest.Value.Bounds.Min.x, nearest.Value.Bounds.Max.x),
                    ClearGap(primary.Bounds.Min.y, primary.Bounds.Max.y, nearest.Value.Bounds.Min.y, nearest.Value.Bounds.Max.y),
                    ClearGap(primary.Bounds.Min.z, primary.Bounds.Max.z, nearest.Value.Bounds.Min.z, nearest.Value.Bounds.Max.z))
                : new Vector3i(0, 0, 0);
            metrics = new SmartBuildPrecisionSelectionMetrics(
                AggregateBounds(source),
                primary.Id,
                nearest?.Id ?? -1,
                gap);
            message = "Measured the selected occupied bounds in craft-local cells.";
            return true;
        }

        internal static bool IsSafeBounds(SmartBuildBounds bounds) =>
            IsSafeCoordinate(bounds.Min) && IsSafeCoordinate(bounds.Max);

        internal static bool IsSafeCoordinate(Vector3i value) =>
            Math.Abs((long)value.x) <= MaximumCoordinateMagnitude &&
            Math.Abs((long)value.y) <= MaximumCoordinateMagnitude &&
            Math.Abs((long)value.z) <= MaximumCoordinateMagnitude;

        private static bool TryValidateItems(
            IReadOnlyList<SmartBuildPrecisionItem> items,
            int minimumCount,
            out SmartBuildPrecisionItem[] source,
            out string message)
        {
            source = (items ?? Array.Empty<SmartBuildPrecisionItem>()).ToArray();
            if (source.Length < minimumCount)
            {
                message = "Select at least " + minimumCount.ToString(CultureInfo.InvariantCulture) +
                          " Smart Builder piece(s) for this precision operation.";
                return false;
            }

            if (source.Select(item => item.Id).Distinct().Count() != source.Length)
            {
                message = "The precision selection contains duplicate piece identities.";
                return false;
            }

            if (source.Any(item =>
                    !IsSafeCoordinate(item.Origin) ||
                    !IsSafeBounds(item.Bounds)))
            {
                message = SafeCoordinateMessage();
                return false;
            }

            message = null;
            return true;
        }

        private static SmartBuildBounds AggregateBounds(
            IReadOnlyList<SmartBuildPrecisionItem> items)
        {
            SmartBuildBounds first = items[0].Bounds;
            int minX = first.Min.x;
            int minY = first.Min.y;
            int minZ = first.Min.z;
            int maxX = first.Max.x;
            int maxY = first.Max.y;
            int maxZ = first.Max.z;
            for (int index = 1; index < items.Count; index++)
            {
                SmartBuildBounds bounds = items[index].Bounds;
                minX = Math.Min(minX, bounds.Min.x);
                minY = Math.Min(minY, bounds.Min.y);
                minZ = Math.Min(minZ, bounds.Min.z);
                maxX = Math.Max(maxX, bounds.Max.x);
                maxY = Math.Max(maxY, bounds.Max.y);
                maxZ = Math.Max(maxZ, bounds.Max.z);
            }

            return new SmartBuildBounds(
                new Vector3i(minX, minY, minZ),
                new Vector3i(maxX, maxY, maxZ));
        }

        private static Vector3i RoundedBoundsCenter(SmartBuildBounds bounds) =>
            new Vector3i(
                RoundedAxisCenter(bounds.Min.x, bounds.Max.x),
                RoundedAxisCenter(bounds.Min.y, bounds.Max.y),
                RoundedAxisCenter(bounds.Min.z, bounds.Max.z));

        private static int RoundedAxisCenter(int minimum, int maximum) =>
            (int)((long)minimum + ((long)maximum - minimum + 1L) / 2L);

        private static int ClearGap(
            int firstMinimum,
            int firstMaximum,
            int secondMinimum,
            int secondMaximum)
        {
            if (firstMaximum < secondMinimum)
                return Math.Max(0, secondMinimum - firstMaximum - 1);
            if (secondMaximum < firstMinimum)
                return Math.Max(0, firstMinimum - secondMaximum - 1);
            return 0;
        }

        private static bool TryAddMove(
            SmartBuildPrecisionItem item,
            Vector3i delta,
            ICollection<SmartBuildPrecisionMove> moves,
            out string message)
        {
            if (!TryAdd(item.Bounds.Min, delta, out Vector3i targetMinimum) ||
                !TryAdd(item.Bounds.Max, delta, out Vector3i targetMaximum) ||
                !IsSafeCoordinate(targetMinimum) ||
                !IsSafeCoordinate(targetMaximum))
            {
                message = SafeCoordinateMessage();
                return false;
            }

            if (delta.x != 0 || delta.y != 0 || delta.z != 0)
                moves.Add(new SmartBuildPrecisionMove(item.Id, delta));

            message = null;
            return true;
        }

        private static long AlignmentCoordinateTwice(
            SmartBuildBounds bounds,
            SmartBuildAxis axis,
            SmartBuildPrecisionAlignment alignment)
        {
            switch (alignment)
            {
                case SmartBuildPrecisionAlignment.Minimum:
                    return 2L * Axis(bounds.Min, axis);
                case SmartBuildPrecisionAlignment.Maximum:
                    return 2L * Axis(bounds.Max, axis);
                default:
                    return CenterCoordinateTwice(bounds, axis);
            }
        }

        private static long CenterCoordinateTwice(SmartBuildBounds bounds, SmartBuildAxis axis) =>
            (long)Axis(bounds.Min, axis) + Axis(bounds.Max, axis);

        private static int Axis(Vector3i value, SmartBuildAxis axis)
        {
            switch (axis)
            {
                case SmartBuildAxis.Y:
                    return value.y;
                case SmartBuildAxis.Z:
                    return value.z;
                default:
                    return value.x;
            }
        }

        private static Vector3i AxisVector(SmartBuildAxis axis, int value)
        {
            switch (axis)
            {
                case SmartBuildAxis.Y:
                    return new Vector3i(0, value, 0);
                case SmartBuildAxis.Z:
                    return new Vector3i(0, 0, value);
                default:
                    return new Vector3i(value, 0, 0);
            }
        }

        private static bool TryParseCell(string text, out int value) =>
            int.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);

        private static bool TrySubtract(Vector3i left, Vector3i right, out Vector3i result)
        {
            long x = (long)left.x - right.x;
            long y = (long)left.y - right.y;
            long z = (long)left.z - right.z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            {
                result = default;
                return false;
            }

            result = new Vector3i((int)x, (int)y, (int)z);
            return true;
        }

        private static bool TryAdd(Vector3i left, Vector3i right, out Vector3i result)
        {
            long x = (long)left.x + right.x;
            long y = (long)left.y + right.y;
            long z = (long)left.z + right.z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            {
                result = default;
                return false;
            }

            result = new Vector3i((int)x, (int)y, (int)z);
            return true;
        }

        private static long RoundDivide(long numerator, long positiveDenominator)
        {
            long half = positiveDenominator / 2L;
            return numerator >= 0L
                ? (numerator + half) / positiveDenominator
                : -((-numerator + half) / positiveDenominator);
        }

        private static string AlignmentLabel(SmartBuildPrecisionAlignment alignment)
        {
            switch (alignment)
            {
                case SmartBuildPrecisionAlignment.Minimum:
                    return "minimum edge";
                case SmartBuildPrecisionAlignment.Maximum:
                    return "maximum edge";
                default:
                    return "center";
            }
        }

        private static string FormatVector(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + ", " +
            value.y.ToString(CultureInfo.InvariantCulture) + ", " +
            value.z.ToString(CultureInfo.InvariantCulture);

        private static string SafeCoordinateMessage() =>
            "Precision transforms must stay inside the safe +/-" +
            MaximumCoordinateMagnitude.ToString("N0", CultureInfo.InvariantCulture) +
            " craft-local grid-cell range.";
    }
}
