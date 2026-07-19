using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildPathArrayMode
    {
        PolylineVertices,
        SteppedCells
    }

    internal enum SmartBuildRectanglePatternMode
    {
        FilledPlane,
        PerimeterWall
    }

    /// <summary>
    /// A copy transform relative to the original selected group. Rotation is
    /// applied around the group's shared pivot before Offset is applied.
    /// </summary>
    internal readonly struct SmartBuildGroupTransform
    {
        internal SmartBuildGroupTransform(
            Vector3i offset,
            DecorationEditAxis rotationAxis = DecorationEditAxis.None,
            int quarterTurns = 0)
        {
            Offset = offset;
            RotationAxis = rotationAxis;
            QuarterTurns = NormalizeQuarterTurns(quarterTurns);
        }

        internal Vector3i Offset { get; }

        internal DecorationEditAxis RotationAxis { get; }

        internal int QuarterTurns { get; }

        internal bool IsIdentity =>
            Offset.x == 0 && Offset.y == 0 && Offset.z == 0 &&
            QuarterTurns == 0;

        private static int NormalizeQuarterTurns(int turns) =>
            ((turns % 4) + 4) % 4;
    }

    /// <summary>
    /// Pure, bounded planners for Smart Builder copy patterns and planar cell
    /// tools. Copy planners never include the identity/original. Rectangle and
    /// flood planners return absolute cells and do include their origin/seed.
    /// Every failed plan returns an empty result.
    /// </summary>
    internal static class SmartBuildAdvancedToolPlanner
    {
        internal const int MaximumPatternCopies =
            SmartBuildLimits.MaximumPatternCopies;
        internal const int MaximumRectangleCells =
            SmartBuildLimits.MaximumRectangleCells;
        internal const int MaximumFloodFillCells =
            SmartBuildLimits.MaximumFloodFillCells;
        internal const int MaximumPathControlPoints =
            SmartBuildLimits.MaximumPathControlPoints;
        internal const int MaximumCoordinateMagnitude =
            SmartBuildLimits.MaximumCoordinateMagnitude;

        internal static bool TryPlanLinear(
            Vector3i step,
            int copyCount,
            out IReadOnlyList<SmartBuildGroupTransform> transforms,
            out string reason)
        {
            transforms = Array.Empty<SmartBuildGroupTransform>();
            if (copyCount < 1 || copyCount > MaximumPatternCopies)
            {
                reason = CopyLimitReason(copyCount);
                return false;
            }

            if (IsZero(step))
            {
                reason = "Linear array spacing must move at least one grid cell.";
                return false;
            }

            var planned = new List<SmartBuildGroupTransform>(copyCount);
            for (int copy = 1; copy <= copyCount; copy++)
            {
                if (!TryScale(step, copy, out Vector3i offset))
                {
                    reason = CoordinateLimitReason();
                    return false;
                }

                planned.Add(new SmartBuildGroupTransform(offset));
            }

            transforms = planned;
            reason = null;
            return true;
        }

        /// <summary>
        /// Plans a two-dimensional grid. totalColumns and totalRows include the
        /// original at column 0, row 0; the returned transforms exclude it.
        /// </summary>
        internal static bool TryPlanGrid(
            Vector3i columnStep,
            int totalColumns,
            Vector3i rowStep,
            int totalRows,
            out IReadOnlyList<SmartBuildGroupTransform> transforms,
            out string reason)
        {
            transforms = Array.Empty<SmartBuildGroupTransform>();
            if (totalColumns < 1 || totalRows < 1)
            {
                reason = "Grid columns and rows must each be at least one.";
                return false;
            }

            long copyCount = (long)totalColumns * totalRows - 1L;
            if (copyCount < 1L || copyCount > MaximumPatternCopies)
            {
                reason = CopyLimitReason(copyCount);
                return false;
            }

            if ((totalColumns > 1 && IsZero(columnStep)) ||
                (totalRows > 1 && IsZero(rowStep)))
            {
                reason = "Each active grid direction must move at least one grid cell.";
                return false;
            }

            if (totalColumns > 1 && totalRows > 1 && AreParallel(columnStep, rowStep))
            {
                reason = "Grid column and row spacing must use independent directions.";
                return false;
            }

            var planned = new List<SmartBuildGroupTransform>((int)copyCount);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int row = 0; row < totalRows; row++)
            {
                for (int column = 0; column < totalColumns; column++)
                {
                    if (column == 0 && row == 0)
                        continue;

                    if (!TryCombinedOffset(columnStep, column, rowStep, row, out Vector3i offset))
                    {
                        reason = CoordinateLimitReason();
                        return false;
                    }

                    if (!seen.Add(CellKey(offset)))
                    {
                        reason = "Grid spacing produces overlapping copy positions.";
                        return false;
                    }

                    planned.Add(new SmartBuildGroupTransform(offset));
                }
            }

            transforms = planned;
            reason = null;
            return true;
        }

        /// <summary>
        /// Plans up to three additional cardinal positions around a centre.
        /// armFromCenter is the vector from the radial centre to the original
        /// group pivot. Each copy advances another positive 90-degree turn.
        /// </summary>
        internal static bool TryPlanCardinalRadial(
            Vector3i armFromCenter,
            DecorationEditAxis rotationAxis,
            int copyCount,
            bool rotateCopies,
            out IReadOnlyList<SmartBuildGroupTransform> transforms,
            out string reason)
        {
            transforms = Array.Empty<SmartBuildGroupTransform>();
            if (!IsCardinalAxis(rotationAxis))
            {
                reason = "A radial array needs an X, Y, or Z rotation axis.";
                return false;
            }

            if (copyCount < 1 || copyCount > 3)
            {
                reason = "A cardinal radial array supports one to three additional positions.";
                return false;
            }

            if (IsZero(armFromCenter))
            {
                reason = "The radial centre must differ from the selected group pivot.";
                return false;
            }

            if (AxisComponent(armFromCenter, rotationAxis) != 0)
            {
                reason = "The radial arm must lie in the plane perpendicular to its axis.";
                return false;
            }

            if (!IsSafeCoordinate(armFromCenter))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            var planned = new List<SmartBuildGroupTransform>(copyCount);
            for (int copy = 1; copy <= copyCount; copy++)
            {
                Vector3i rotatedArm = RotateCardinal(armFromCenter, rotationAxis, copy);
                if (!TrySubtract(rotatedArm, armFromCenter, out Vector3i offset))
                {
                    reason = CoordinateLimitReason();
                    return false;
                }

                planned.Add(
                    rotateCopies
                        ? new SmartBuildGroupTransform(offset, rotationAxis, copy)
                        : new SmartBuildGroupTransform(offset));
            }

            transforms = planned;
            reason = null;
            return true;
        }

        /// <summary>
        /// Plans copies along absolute control points. PolylineVertices uses the
        /// supplied vertices only. SteppedCells rasterizes every segment with a
        /// deterministic grid DDA. Returned offsets are relative to points[0].
        /// </summary>
        internal static bool TryPlanPath(
            IEnumerable<Vector3i> points,
            SmartBuildPathArrayMode mode,
            out IReadOnlyList<SmartBuildGroupTransform> transforms,
            out string reason)
        {
            transforms = Array.Empty<SmartBuildGroupTransform>();
            Vector3i[] controls;
            try
            {
                if (!SmartBuildLimits.TryMaterializeBounded(
                        points,
                        MaximumPathControlPoints,
                        out controls,
                        out _))
                {
                    reason =
                        "A path array supports at most " +
                        MaximumPathControlPoints +
                        " control points.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "Path control points could not be enumerated safely: " + exception.Message;
                return false;
            }

            if (controls.Length < 2)
            {
                reason = "A path array needs at least two control points.";
                return false;
            }

            if (mode != SmartBuildPathArrayMode.PolylineVertices &&
                mode != SmartBuildPathArrayMode.SteppedCells)
            {
                reason = "The requested path sampling mode is not supported.";
                return false;
            }

            if (controls.Any(control => !IsSafeCoordinate(control)))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            Vector3i origin = controls[0];
            var planned = new List<SmartBuildGroupTransform>();
            var seen = new HashSet<string>(StringComparer.Ordinal)
            {
                CellKey(new Vector3i(0, 0, 0))
            };

            if (mode == SmartBuildPathArrayMode.PolylineVertices)
            {
                for (int index = 1; index < controls.Length; index++)
                {
                    if (!TryAppendPathPoint(controls[index], origin, planned, seen, out reason))
                        return false;
                }
            }
            else
            {
                for (int segment = 1; segment < controls.Length; segment++)
                {
                    Vector3i start = controls[segment - 1];
                    Vector3i end = controls[segment];
                    int steps = Math.Max(
                        Math.Abs(end.x - start.x),
                        Math.Max(Math.Abs(end.y - start.y), Math.Abs(end.z - start.z)));
                    if (steps == 0)
                        continue;

                    for (int step = 1; step <= steps; step++)
                    {
                        Vector3i point = InterpolateGridPoint(start, end, step, steps);
                        if (!TryAppendPathPoint(point, origin, planned, seen, out reason))
                            return false;
                    }
                }
            }

            if (planned.Count == 0)
            {
                reason = "The path does not contain a copy position beyond its origin.";
                return false;
            }

            transforms = planned;
            reason = null;
            return true;
        }

        internal static bool TryPlanRectanglePlane(
            Vector3i origin,
            Vector3i columnStep,
            int totalColumns,
            Vector3i rowStep,
            int totalRows,
            out IReadOnlyList<Vector3i> cells,
            out string reason) =>
            TryPlanRectangle(
                origin,
                columnStep,
                totalColumns,
                rowStep,
                totalRows,
                SmartBuildRectanglePatternMode.FilledPlane,
                out cells,
                out reason);

        internal static bool TryPlanRectangleWall(
            Vector3i origin,
            Vector3i columnStep,
            int totalColumns,
            Vector3i rowStep,
            int totalRows,
            out IReadOnlyList<Vector3i> cells,
            out string reason) =>
            TryPlanRectangle(
                origin,
                columnStep,
                totalColumns,
                rowStep,
                totalRows,
                SmartBuildRectanglePatternMode.PerimeterWall,
                out cells,
                out reason);

        internal static bool TryPlanRectangle(
            Vector3i origin,
            Vector3i columnStep,
            int totalColumns,
            Vector3i rowStep,
            int totalRows,
            SmartBuildRectanglePatternMode mode,
            out IReadOnlyList<Vector3i> cells,
            out string reason)
        {
            cells = Array.Empty<Vector3i>();
            if (totalColumns < 1 || totalRows < 1)
            {
                reason = "Rectangle columns and rows must each be at least one.";
                return false;
            }

            if (mode != SmartBuildRectanglePatternMode.FilledPlane &&
                mode != SmartBuildRectanglePatternMode.PerimeterWall)
            {
                reason = "The requested rectangle pattern is not supported.";
                return false;
            }

            if ((totalColumns > 1 && !IsCardinalVector(columnStep)) ||
                (totalRows > 1 && !IsCardinalVector(rowStep)))
            {
                reason = "Rectangle spacing must follow cardinal grid directions.";
                return false;
            }

            if (totalColumns > 1 && totalRows > 1 &&
                Dot(columnStep, rowStep) != 0L)
            {
                reason = "Rectangle column and row directions must be perpendicular.";
                return false;
            }

            long filledCount = (long)totalColumns * totalRows;
            long plannedCount = mode == SmartBuildRectanglePatternMode.FilledPlane
                ? filledCount
                : RectanglePerimeterCount(totalColumns, totalRows);
            if (plannedCount < 1L || plannedCount > MaximumRectangleCells)
            {
                reason =
                    "Rectangle cell count " + plannedCount +
                    " exceeds the safe limit of " + MaximumRectangleCells + ".";
                return false;
            }

            if (!IsSafeCoordinate(origin))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            var planned = new List<Vector3i>((int)plannedCount);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int row = 0; row < totalRows; row++)
            {
                for (int column = 0; column < totalColumns; column++)
                {
                    bool perimeter = column == 0 || column == totalColumns - 1 ||
                                     row == 0 || row == totalRows - 1;
                    if (mode == SmartBuildRectanglePatternMode.PerimeterWall && !perimeter)
                        continue;

                    if (!TryAbsoluteGridCell(
                            origin,
                            columnStep,
                            column,
                            rowStep,
                            row,
                            out Vector3i cell))
                    {
                        reason = CoordinateLimitReason();
                        return false;
                    }

                    if (!seen.Add(CellKey(cell)))
                    {
                        reason = "Rectangle spacing produces overlapping cells.";
                        return false;
                    }

                    planned.Add(cell);
                }
            }

            cells = planned;
            reason = null;
            return true;
        }

        /// <summary>
        /// Fills only a four-connected region on seed's plane. Any reachable
        /// path beyond searchBounds proves that the region is open and rejects
        /// the whole plan. Crossing maximumCells likewise rejects atomically.
        /// </summary>
        internal static bool TryPlanEnclosedPlanarFloodFill(
            Vector3i seed,
            SmartBuildAxis planeNormal,
            SmartBuildBounds searchBounds,
            Func<Vector3i, bool> isBoundary,
            int maximumCells,
            out IReadOnlyList<Vector3i> cells,
            out string reason)
        {
            cells = Array.Empty<Vector3i>();
            if (!IsSmartAxis(planeNormal))
            {
                reason = "A planar flood fill needs an X, Y, or Z normal axis.";
                return false;
            }

            if (isBoundary == null)
            {
                reason = "A planar flood fill needs a boundary predicate.";
                return false;
            }

            if (maximumCells < 1 || maximumCells > MaximumFloodFillCells)
            {
                reason =
                    "Flood fill cap must be between 1 and " +
                    MaximumFloodFillCells + ".";
                return false;
            }

            if (!Contains(searchBounds, seed))
            {
                reason = "The flood fill seed lies outside its search bounds.";
                return false;
            }

            if (!IsSafeCoordinate(searchBounds.Min) || !IsSafeCoordinate(searchBounds.Max))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            if (isBoundary(seed))
            {
                reason = "The flood fill seed is a boundary cell.";
                return false;
            }

            SmartBuildAxis[] planeAxes = SmartBuildAxisHelper.PlaneAxes(planeNormal);
            Vector3i[] directions =
            {
                SmartBuildAxisHelper.ToVector3i(planeAxes[0], 1),
                SmartBuildAxisHelper.ToVector3i(planeAxes[0], -1),
                SmartBuildAxisHelper.ToVector3i(planeAxes[1], 1),
                SmartBuildAxisHelper.ToVector3i(planeAxes[1], -1)
            };
            // List + head index keeps the standalone net472 verifier compatible
            // with the game's Vector3i type while preserving FIFO traversal.
            var queue = new List<Vector3i>();
            int queueHead = 0;
            var planned = new List<Vector3i>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            queue.Add(seed);
            seen.Add(CellKey(seed));

            while (queueHead < queue.Count)
            {
                Vector3i cell = queue[queueHead++];
                planned.Add(cell);
                if (planned.Count > maximumCells)
                {
                    reason =
                        "The enclosed flood region exceeds the requested cap of " +
                        maximumCells + " cells.";
                    return false;
                }

                for (int index = 0; index < directions.Length; index++)
                {
                    if (!TryAdd(cell, directions[index], out Vector3i neighbor))
                    {
                        reason = "The planar flood region is open beyond the safe coordinate range.";
                        return false;
                    }

                    if (!InsidePlaneBounds(searchBounds, neighbor, planeNormal))
                    {
                        reason = "The planar flood region is open at the search boundary.";
                        return false;
                    }

                    string key = CellKey(neighbor);
                    if (seen.Contains(key) || isBoundary(neighbor))
                        continue;

                    seen.Add(key);
                    queue.Add(neighbor);
                    if (seen.Count > maximumCells)
                    {
                        reason =
                            "The enclosed flood region exceeds the requested cap of " +
                            maximumCells + " cells.";
                        return false;
                    }
                }
            }

            cells = planned;
            reason = null;
            return true;
        }

        internal static bool IsValidTransform(
            SmartBuildGroupTransform transform,
            out string reason)
        {
            if (!IsSafeCoordinate(transform.Offset))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            if (transform.RotationAxis == DecorationEditAxis.Free ||
                (transform.QuarterTurns != 0 && !IsCardinalAxis(transform.RotationAxis)))
            {
                reason = "Copy rotations must use a cardinal X, Y, or Z axis.";
                return false;
            }

            if (transform.IsIdentity)
            {
                reason = "Copy transforms may not duplicate the identity/original position.";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryAppendPathPoint(
            Vector3i point,
            Vector3i origin,
            List<SmartBuildGroupTransform> planned,
            HashSet<string> seen,
            out string reason)
        {
            if (!TrySubtract(point, origin, out Vector3i offset))
            {
                reason = CoordinateLimitReason();
                return false;
            }

            if (!seen.Add(CellKey(offset)))
            {
                reason = null;
                return true;
            }

            if (planned.Count >= MaximumPatternCopies)
            {
                reason = CopyLimitReason(planned.Count + 1L);
                return false;
            }

            planned.Add(new SmartBuildGroupTransform(offset));
            reason = null;
            return true;
        }

        private static Vector3i InterpolateGridPoint(
            Vector3i start,
            Vector3i end,
            int step,
            int steps) =>
            new Vector3i(
                RoundRatio((long)start.x * steps + (long)(end.x - start.x) * step, steps),
                RoundRatio((long)start.y * steps + (long)(end.y - start.y) * step, steps),
                RoundRatio((long)start.z * steps + (long)(end.z - start.z) * step, steps));

        private static int RoundRatio(long numerator, int denominator)
        {
            long rounded = numerator >= 0L
                ? (numerator + denominator / 2L) / denominator
                : -((-numerator + denominator / 2L) / denominator);
            return (int)rounded;
        }

        private static long RectanglePerimeterCount(int columns, int rows)
        {
            if (columns == 1 || rows == 1)
                return (long)columns * rows;
            return 2L * columns + 2L * rows - 4L;
        }

        private static bool TryAbsoluteGridCell(
            Vector3i origin,
            Vector3i firstStep,
            int firstMultiplier,
            Vector3i secondStep,
            int secondMultiplier,
            out Vector3i cell)
        {
            long x = (long)origin.x +
                     (long)firstStep.x * firstMultiplier +
                     (long)secondStep.x * secondMultiplier;
            long y = (long)origin.y +
                     (long)firstStep.y * firstMultiplier +
                     (long)secondStep.y * secondMultiplier;
            long z = (long)origin.z +
                     (long)firstStep.z * firstMultiplier +
                     (long)secondStep.z * secondMultiplier;
            return TryCell(x, y, z, out cell);
        }

        private static bool TryCombinedOffset(
            Vector3i firstStep,
            int firstMultiplier,
            Vector3i secondStep,
            int secondMultiplier,
            out Vector3i offset)
        {
            long x = (long)firstStep.x * firstMultiplier +
                     (long)secondStep.x * secondMultiplier;
            long y = (long)firstStep.y * firstMultiplier +
                     (long)secondStep.y * secondMultiplier;
            long z = (long)firstStep.z * firstMultiplier +
                     (long)secondStep.z * secondMultiplier;
            return TryCell(x, y, z, out offset);
        }

        private static bool TryScale(Vector3i value, int multiplier, out Vector3i scaled) =>
            TryCell(
                (long)value.x * multiplier,
                (long)value.y * multiplier,
                (long)value.z * multiplier,
                out scaled);

        private static bool TryAdd(Vector3i left, Vector3i right, out Vector3i sum) =>
            TryCell(
                (long)left.x + right.x,
                (long)left.y + right.y,
                (long)left.z + right.z,
                out sum);

        private static bool TrySubtract(Vector3i left, Vector3i right, out Vector3i difference) =>
            TryCell(
                (long)left.x - right.x,
                (long)left.y - right.y,
                (long)left.z - right.z,
                out difference);

        private static bool TryCell(long x, long y, long z, out Vector3i value)
        {
            if (Math.Abs(x) > MaximumCoordinateMagnitude ||
                Math.Abs(y) > MaximumCoordinateMagnitude ||
                Math.Abs(z) > MaximumCoordinateMagnitude)
            {
                value = new Vector3i(0, 0, 0);
                return false;
            }

            value = new Vector3i((int)x, (int)y, (int)z);
            return true;
        }

        private static Vector3i RotateCardinal(
            Vector3i value,
            DecorationEditAxis axis,
            int quarterTurns)
        {
            Vector3i result = value;
            int turns = ((quarterTurns % 4) + 4) % 4;
            for (int turn = 0; turn < turns; turn++)
            {
                switch (axis)
                {
                    case DecorationEditAxis.X:
                        result = new Vector3i(result.x, -result.z, result.y);
                        break;
                    case DecorationEditAxis.Y:
                        result = new Vector3i(result.z, result.y, -result.x);
                        break;
                    case DecorationEditAxis.Z:
                        result = new Vector3i(-result.y, result.x, result.z);
                        break;
                }
            }

            return result;
        }

        private static bool AreParallel(Vector3i left, Vector3i right) =>
            (long)left.y * right.z - (long)left.z * right.y == 0L &&
            (long)left.z * right.x - (long)left.x * right.z == 0L &&
            (long)left.x * right.y - (long)left.y * right.x == 0L;

        private static long Dot(Vector3i left, Vector3i right) =>
            (long)left.x * right.x +
            (long)left.y * right.y +
            (long)left.z * right.z;

        private static bool IsCardinalVector(Vector3i value)
        {
            int nonZero = 0;
            if (value.x != 0)
                nonZero++;
            if (value.y != 0)
                nonZero++;
            if (value.z != 0)
                nonZero++;
            return nonZero == 1 && IsSafeCoordinate(value);
        }

        private static bool IsZero(Vector3i value) =>
            value.x == 0 && value.y == 0 && value.z == 0;

        private static bool IsCardinalAxis(DecorationEditAxis axis) =>
            axis == DecorationEditAxis.X ||
            axis == DecorationEditAxis.Y ||
            axis == DecorationEditAxis.Z;

        private static bool IsSmartAxis(SmartBuildAxis axis) =>
            axis == SmartBuildAxis.X || axis == SmartBuildAxis.Y || axis == SmartBuildAxis.Z;

        private static int AxisComponent(Vector3i value, DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return value.x;
                case DecorationEditAxis.Y:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private static bool Contains(SmartBuildBounds bounds, Vector3i cell) =>
            cell.x >= bounds.Min.x && cell.x <= bounds.Max.x &&
            cell.y >= bounds.Min.y && cell.y <= bounds.Max.y &&
            cell.z >= bounds.Min.z && cell.z <= bounds.Max.z;

        private static bool InsidePlaneBounds(
            SmartBuildBounds bounds,
            Vector3i cell,
            SmartBuildAxis normal)
        {
            switch (normal)
            {
                case SmartBuildAxis.X:
                    return cell.y >= bounds.Min.y && cell.y <= bounds.Max.y &&
                           cell.z >= bounds.Min.z && cell.z <= bounds.Max.z;
                case SmartBuildAxis.Y:
                    return cell.x >= bounds.Min.x && cell.x <= bounds.Max.x &&
                           cell.z >= bounds.Min.z && cell.z <= bounds.Max.z;
                default:
                    return cell.x >= bounds.Min.x && cell.x <= bounds.Max.x &&
                           cell.y >= bounds.Min.y && cell.y <= bounds.Max.y;
            }
        }

        private static bool IsSafeCoordinate(Vector3i value) =>
            Math.Abs((long)value.x) <= MaximumCoordinateMagnitude &&
            Math.Abs((long)value.y) <= MaximumCoordinateMagnitude &&
            Math.Abs((long)value.z) <= MaximumCoordinateMagnitude;

        private static string CellKey(Vector3i value) =>
            value.x + ":" + value.y + ":" + value.z;

        private static string CopyLimitReason(long requested) =>
            "Pattern copy count " + requested +
            " exceeds the safe limit of " + MaximumPatternCopies + ".";

        private static string CoordinateLimitReason() =>
            "Pattern coordinates exceed the safe +/-" +
            MaximumCoordinateMagnitude + " grid-cell range.";
    }
}
