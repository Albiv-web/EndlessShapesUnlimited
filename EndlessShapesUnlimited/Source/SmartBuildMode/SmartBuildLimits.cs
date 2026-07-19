using System;
using System.Collections.Generic;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// One safety contract for every pure Smart Builder planner and persisted
    /// scene validator. Enumerators must stop at their applicable cap plus one;
    /// callers must not materialize an untrusted sequence before applying it.
    /// </summary>
    internal static class SmartBuildLimits
    {
        internal const int MaximumSceneNodes = 512;
        internal const int WarningPlacementCount = 1_000;
        internal const int HardPlacementCount = 10_000;

        internal const int MaximumNativeBlockLength = 4;
        internal const int MaximumPlannerInputCells =
            HardPlacementCount * MaximumNativeBlockLength;
        internal const int MaximumOccupancyFingerprintCells =
            MaximumConditionalScopeCells;
        internal const int MaximumPersistedSceneEnumerationCells =
            MaximumConditionalScopeCells;

        internal const int MaximumPatternCopies = MaximumSceneNodes - 1;
        internal const int MaximumRectangleCells = 4_096;
        internal const int MaximumFloodFillCells = 4_096;
        internal const int MaximumPathControlPoints = 256;
        internal const int MaximumCoordinateMagnitude = 1_000_000;
        internal const int MaximumPresetExtent = 2_048;

        // These match the stricter limits used by the live replacement session.
        internal const int MaximumConditionalScopeCells = 100_000;
        internal const int MaximumConditionalItems = 4_096;
        internal const int MaximumConditionalFootprintCells = 65_536;

        internal static int BoundedPositiveLimit(int requested, int maximum)
        {
            int safeMaximum = Math.Max(1, maximum);
            return Math.Max(1, Math.Min(requested, safeMaximum));
        }

        /// <summary>
        /// Materializes no more than maximumItems. On overflow, the source has
        /// been advanced exactly once beyond the cap and no partial result is
        /// returned.
        /// </summary>
        internal static bool TryMaterializeBounded<T>(
            IEnumerable<T> source,
            int maximumItems,
            out T[] items,
            out int observedCount)
        {
            int limit = Math.Max(0, maximumItems);
            var bounded = new List<T>(Math.Min(limit, 4_096));
            foreach (T item in source ?? Array.Empty<T>())
            {
                if (bounded.Count >= limit)
                {
                    items = Array.Empty<T>();
                    observedCount = limit == int.MaxValue ? int.MaxValue : limit + 1;
                    return false;
                }

                bounded.Add(item);
            }

            items = bounded.ToArray();
            observedCount = items.Length;
            return true;
        }

        internal static bool TryProductWithinLimit(
            int first,
            int second,
            int third,
            long limit,
            out long product)
        {
            product = 0L;
            if (first < 0 || second < 0 || third < 0 || limit < 0L)
                return false;
            if (!TryMultiplyWithinLimit(first, second, limit, out long partial))
            {
                product = LimitExceededValue(limit);
                return false;
            }

            if (!TryMultiplyWithinLimit(partial, third, limit, out product))
            {
                product = LimitExceededValue(limit);
                return false;
            }

            return true;
        }

        internal static bool TryMultiplyWithinLimit(
            long first,
            long second,
            long limit,
            out long product)
        {
            product = 0L;
            if (first < 0L || second < 0L || limit < 0L)
                return false;
            if (first == 0L || second == 0L)
                return true;
            if (first > limit || second > limit || first > limit / second)
            {
                product = LimitExceededValue(limit);
                return false;
            }

            product = first * second;
            return true;
        }

        internal static bool TryAddWithinLimit(
            long first,
            long second,
            long limit,
            out long sum)
        {
            sum = 0L;
            if (first < 0L || second < 0L || limit < 0L ||
                first > limit || second > limit - first)
            {
                sum = LimitExceededValue(limit);
                return false;
            }

            sum = first + second;
            return true;
        }

        internal static bool IsPortableCoordinate(int value) =>
            Math.Abs((long)value) <= MaximumCoordinateMagnitude;

        private static long LimitExceededValue(long limit) =>
            limit == long.MaxValue ? long.MaxValue : limit + 1L;
    }
}
