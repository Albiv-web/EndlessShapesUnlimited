using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum DecorationLayoutAxis
    {
        X,
        Y,
        Z
    }

    internal enum DecorationAlignmentMode
    {
        MinimumEdge,
        Center,
        MaximumEdge
    }

    internal sealed class DecorationLayoutItem
    {
        internal DecorationLayoutItem(
            string key,
            Vector3 center,
            Vector3 extents,
            Vector3 rotation,
            Vector3 scale,
            bool editable = true)
        {
            Key = key ?? string.Empty;
            Center = center;
            Extents = extents;
            Rotation = rotation;
            Scale = scale;
            Editable = editable;
        }

        internal string Key { get; }

        internal Vector3 Center { get; }

        internal Vector3 Extents { get; }

        internal Vector3 Rotation { get; }

        internal Vector3 Scale { get; }

        internal bool Editable { get; }

        internal DecorationLayoutItem WithTransform(
            Vector3 center,
            Vector3 rotation,
            Vector3 scale) =>
            new DecorationLayoutItem(Key, center, Extents, rotation, scale, Editable);
    }

    internal sealed class DecorationLayoutPlan
    {
        internal DecorationLayoutPlan(
            string label,
            IReadOnlyList<DecorationLayoutItem> before,
            IReadOnlyList<DecorationLayoutItem> after,
            int skippedLocked)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Layout decorations" : label;
            Before = (before ?? Array.Empty<DecorationLayoutItem>()).ToArray();
            After = (after ?? Array.Empty<DecorationLayoutItem>()).ToArray();
            SkippedLocked = Math.Max(0, skippedLocked);
        }

        internal string Label { get; }

        internal DecorationLayoutItem[] Before { get; }

        internal DecorationLayoutItem[] After { get; }

        internal int SkippedLocked { get; }

        internal int ChangedCount => Enumerable.Range(0, Math.Min(Before.Length, After.Length))
            .Count(index => !SameTransform(Before[index], After[index]));

        internal bool IsValid =>
            Before.Length == After.Length &&
            Before.Select(item => item.Key).SequenceEqual(After.Select(item => item.Key)) &&
            After.All(DecorationLayoutTools.IsValidItem);

        private static bool SameTransform(DecorationLayoutItem left, DecorationLayoutItem right) =>
            left != null && right != null &&
            SameVector(left.Center, right.Center) &&
            SameVector(left.Rotation, right.Rotation) &&
            SameVector(left.Scale, right.Scale);

        private static bool SameVector(Vector3 left, Vector3 right) =>
            left.x == right.x && left.y == right.y && left.z == right.z;
    }

    internal static class DecorationLayoutTools
    {
        internal const int MaximumArrayOutput = 100000;
        private const float Epsilon = 0.000001f;

        internal static bool TryAlign(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            DecorationLayoutAxis axis,
            DecorationAlignmentMode mode,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, minimumCount: 2, out DecorationLayoutItem[] source, out message))
                return false;
            if (!TryFindReference(source, referenceKey, out DecorationLayoutItem reference, out message))
                return false;

            float target = AlignmentCoordinate(reference, axis, mode);
            var after = new DecorationLayoutItem[source.Length];
            int skipped = 0;
            for (int index = 0; index < source.Length; index++)
            {
                DecorationLayoutItem item = source[index];
                if (!item.Editable)
                {
                    after[index] = item;
                    skipped++;
                    continue;
                }

                float current = AlignmentCoordinate(item, axis, mode);
                Vector3 center = item.Center;
                SetAxis(ref center, axis, Axis(center, axis) + target - current);
                after[index] = item.WithTransform(center, item.Rotation, item.Scale);
            }

            plan = new DecorationLayoutPlan(
                "Align " + mode + " on " + axis,
                source,
                after,
                skipped);
            if (!plan.IsValid || plan.ChangedCount == 0)
            {
                message = plan.IsValid
                    ? "The selected decorations are already aligned."
                    : "Alignment produced an invalid transform plan.";
                plan = null;
                return false;
            }

            message = "Aligned " + plan.ChangedCount.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations on " + axis + "." + SkippedSuffix(skipped);
            return true;
        }

        internal static bool TryDistribute(
            IReadOnlyList<DecorationLayoutItem> items,
            DecorationLayoutAxis axis,
            bool useEdges,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryValidateItems(items, minimumCount: 3, out DecorationLayoutItem[] source, out message))
                return false;
            if (source.Any(item => !item.Editable))
            {
                message = "Distribution requires every selected decoration to be editable.";
                return false;
            }

            int[] order = Enumerable.Range(0, source.Length)
                .OrderBy(index => useEdges
                    ? MinimumEdge(source[index], axis)
                    : Axis(source[index].Center, axis))
                .ThenBy(index => source[index].Key, StringComparer.Ordinal)
                .ToArray();
            var after = source.ToArray();
            if (useEdges)
            {
                float first = MinimumEdge(source[order[0]], axis);
                float last = MaximumEdge(source[order[order.Length - 1]], axis);
                float occupied = order.Sum(index => Math.Abs(Axis(source[index].Extents, axis)) * 2f);
                float gap = (last - first - occupied) / (order.Length - 1);
                if (!IsFinite(gap))
                {
                    message = "Edge distribution spacing is outside the supported numeric range.";
                    return false;
                }

                float cursor = first;
                foreach (int index in order)
                {
                    DecorationLayoutItem item = source[index];
                    float extent = Math.Abs(Axis(item.Extents, axis));
                    Vector3 center = item.Center;
                    SetAxis(ref center, axis, cursor + extent);
                    after[index] = item.WithTransform(center, item.Rotation, item.Scale);
                    cursor += extent * 2f + gap;
                }
            }
            else
            {
                float first = Axis(source[order[0]].Center, axis);
                float last = Axis(source[order[order.Length - 1]].Center, axis);
                float step = (last - first) / (order.Length - 1);
                if (!IsFinite(step))
                {
                    message = "Center distribution spacing is outside the supported numeric range.";
                    return false;
                }

                for (int rank = 0; rank < order.Length; rank++)
                {
                    int index = order[rank];
                    DecorationLayoutItem item = source[index];
                    Vector3 center = item.Center;
                    SetAxis(ref center, axis, first + step * rank);
                    after[index] = item.WithTransform(center, item.Rotation, item.Scale);
                }
            }

            plan = new DecorationLayoutPlan(
                useEdges ? "Distribute edges on " + axis : "Distribute centers on " + axis,
                source,
                after,
                skippedLocked: 0);
            if (!plan.IsValid || plan.ChangedCount == 0)
            {
                message = plan.IsValid
                    ? "The selected decorations are already evenly distributed."
                    : "Distribution produced an invalid transform plan.";
                plan = null;
                return false;
            }

            message = "Distributed " + source.Length.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations on " + axis + ".";
            return true;
        }

        internal static bool TryMatchTransform(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            bool matchRotation,
            bool matchScale,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!matchRotation && !matchScale)
            {
                message = "Choose rotation and/or scale to match.";
                return false;
            }
            if (!TryValidateItems(items, minimumCount: 2, out DecorationLayoutItem[] source, out message) ||
                !TryFindReference(source, referenceKey, out DecorationLayoutItem reference, out message))
            {
                return false;
            }

            var after = new DecorationLayoutItem[source.Length];
            int skipped = 0;
            for (int index = 0; index < source.Length; index++)
            {
                DecorationLayoutItem item = source[index];
                if (!item.Editable || string.Equals(item.Key, reference.Key, StringComparison.Ordinal))
                {
                    after[index] = item;
                    if (!item.Editable)
                        skipped++;
                    continue;
                }

                after[index] = item.WithTransform(
                    item.Center,
                    matchRotation ? reference.Rotation : item.Rotation,
                    matchScale ? reference.Scale : item.Scale);
            }

            string label = matchRotation && matchScale
                ? "Match rotation and scale"
                : matchRotation ? "Match rotation" : "Match scale";
            plan = new DecorationLayoutPlan(label, source, after, skipped);
            if (!plan.IsValid || plan.ChangedCount == 0)
            {
                message = plan.IsValid
                    ? "The selected decorations already match the reference transform."
                    : "Transform matching produced an invalid plan.";
                plan = null;
                return false;
            }

            message = label + " applied to " +
                      plan.ChangedCount.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations." + SkippedSuffix(skipped);
            return true;
        }

        internal static bool TryCreateLinearArray(
            IReadOnlyList<DecorationLayoutItem> template,
            int count,
            Vector3 step,
            out IReadOnlyList<DecorationLayoutItem[]> copies,
            out string message)
        {
            copies = Array.Empty<DecorationLayoutItem[]>();
            if (!TryValidateTemplate(template, count, out DecorationLayoutItem[] source, out message) ||
                !IsFinite(step))
            {
                if (message == null)
                    message = "Linear-array step must be finite.";
                return false;
            }

            var result = new List<DecorationLayoutItem[]>(count);
            for (int copyIndex = 0; copyIndex < count; copyIndex++)
            {
                Vector3 offset = step * copyIndex;
                result.Add(source.Select(item =>
                    new DecorationLayoutItem(
                        item.Key + "@linear-" + copyIndex.ToString(CultureInfo.InvariantCulture),
                        item.Center + offset,
                        item.Extents,
                        item.Rotation,
                        item.Scale,
                        editable: true)).ToArray());
            }

            if (result.SelectMany(group => group).Any(item => !IsValidItem(item)))
            {
                message = "Linear array would produce an invalid transform.";
                return false;
            }

            copies = result;
            message = "Linear array prepared " +
                      (source.Length * count).ToString("N0", CultureInfo.InvariantCulture) +
                      " decoration transforms.";
            return true;
        }

        internal static bool TryCreateRadialArray(
            IReadOnlyList<DecorationLayoutItem> template,
            int count,
            Vector3 pivot,
            Vector3 axis,
            float totalDegrees,
            bool rotateCopies,
            out IReadOnlyList<DecorationLayoutItem[]> copies,
            out string message)
        {
            copies = Array.Empty<DecorationLayoutItem[]>();
            if (!TryValidateTemplate(template, count, out DecorationLayoutItem[] source, out message) ||
                !IsFinite(pivot) ||
                !IsFinite(axis) ||
                axis.sqrMagnitude <= Epsilon ||
                !IsFinite(totalDegrees))
            {
                if (message == null)
                    message = "Radial-array pivot, axis, and angle must be finite and the axis non-zero.";
                return false;
            }

            float step = count <= 1 ? 0f : totalDegrees / count;
            Vector3 normalizedAxis = Normalize(axis);
            var result = new List<DecorationLayoutItem[]>(count);
            for (int copyIndex = 0; copyIndex < count; copyIndex++)
            {
                float angle = step * copyIndex;
                result.Add(source.Select(item =>
                {
                    Vector3 center = pivot + RotateAroundAxis(
                        item.Center - pivot,
                        normalizedAxis,
                        angle);
                    Vector3 euler = rotateCopies
                        ? RotateEulerManaged(item.Rotation, normalizedAxis, angle)
                        : item.Rotation;
                    return new DecorationLayoutItem(
                        item.Key + "@radial-" + copyIndex.ToString(CultureInfo.InvariantCulture),
                        center,
                        item.Extents,
                        euler,
                        item.Scale,
                        editable: true);
                }).ToArray());
            }

            if (result.SelectMany(group => group).Any(item => !IsValidItem(item)))
            {
                message = "Radial array would produce an invalid transform.";
                return false;
            }

            copies = result;
            message = "Radial array prepared " +
                      (source.Length * count).ToString("N0", CultureInfo.InvariantCulture) +
                      " decoration transforms.";
            return true;
        }

        internal static bool IsValidItem(DecorationLayoutItem item) =>
            item != null &&
            !string.IsNullOrWhiteSpace(item.Key) &&
            IsFinite(item.Center) &&
            IsFinite(item.Extents) &&
            item.Extents.x >= 0f && item.Extents.y >= 0f && item.Extents.z >= 0f &&
            IsFinite(item.Rotation) &&
            IsFinite(item.Scale);

        private static bool TryValidateTemplate(
            IReadOnlyList<DecorationLayoutItem> template,
            int count,
            out DecorationLayoutItem[] source,
            out string message)
        {
            source = Array.Empty<DecorationLayoutItem>();
            message = null;
            if (!TryValidateItems(template, minimumCount: 1, out source, out message))
                return false;
            if (count < 1 || (long)source.Length * count > MaximumArrayOutput)
            {
                message = "Array output must contain 1 through " +
                          MaximumArrayOutput.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration transforms.";
                return false;
            }
            return true;
        }

        private static bool TryValidateItems(
            IReadOnlyList<DecorationLayoutItem> items,
            int minimumCount,
            out DecorationLayoutItem[] source,
            out string message)
        {
            source = (items ?? Array.Empty<DecorationLayoutItem>()).ToArray();
            if (source.Length < minimumCount)
            {
                message = "Select at least " +
                          minimumCount.ToString(CultureInfo.InvariantCulture) +
                          " decorations for this layout operation.";
                return false;
            }
            if (source.Any(item => !IsValidItem(item)))
            {
                message = "Layout selection contains an invalid transform or bounds value.";
                return false;
            }
            if (source.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != source.Length)
            {
                message = "Layout selection contains duplicate item keys.";
                return false;
            }

            message = "Layout selection is valid.";
            return true;
        }

        private static bool TryFindReference(
            IEnumerable<DecorationLayoutItem> source,
            string referenceKey,
            out DecorationLayoutItem reference,
            out string message)
        {
            reference = source.FirstOrDefault(item =>
                string.Equals(item.Key, referenceKey, StringComparison.Ordinal));
            if (reference == null)
            {
                message = "The primary/reference decoration is not part of the selection.";
                return false;
            }

            message = "Reference decoration found.";
            return true;
        }

        private static float AlignmentCoordinate(
            DecorationLayoutItem item,
            DecorationLayoutAxis axis,
            DecorationAlignmentMode mode)
        {
            switch (mode)
            {
                case DecorationAlignmentMode.MinimumEdge:
                    return MinimumEdge(item, axis);
                case DecorationAlignmentMode.MaximumEdge:
                    return MaximumEdge(item, axis);
                default:
                    return Axis(item.Center, axis);
            }
        }

        private static float MinimumEdge(DecorationLayoutItem item, DecorationLayoutAxis axis) =>
            Axis(item.Center, axis) - Math.Abs(Axis(item.Extents, axis));

        private static float MaximumEdge(DecorationLayoutItem item, DecorationLayoutAxis axis) =>
            Axis(item.Center, axis) + Math.Abs(Axis(item.Extents, axis));

        private static float Axis(Vector3 value, DecorationLayoutAxis axis)
        {
            switch (axis)
            {
                case DecorationLayoutAxis.X:
                    return value.x;
                case DecorationLayoutAxis.Y:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private static void SetAxis(ref Vector3 value, DecorationLayoutAxis axis, float component)
        {
            switch (axis)
            {
                case DecorationLayoutAxis.X:
                    value.x = component;
                    break;
                case DecorationLayoutAxis.Y:
                    value.y = component;
                    break;
                default:
                    value.z = component;
                    break;
            }
        }

        private static string SkippedSuffix(int skipped) =>
            skipped <= 0
                ? string.Empty
                : " " + skipped.ToString("N0", CultureInfo.InvariantCulture) + " locked decorations skipped.";

        // Rodrigues' formula keeps radial position planning independent from Unity's
        // native Quaternion ECalls, which also makes the pure planner usable by the
        // standalone verifier and future headless tooling.
        private static Vector3 RotateAroundAxis(Vector3 value, Vector3 axis, float degrees)
        {
            double radians = degrees * (Math.PI / 180d);
            float cosine = (float)Math.Cos(radians);
            float sine = (float)Math.Sin(radians);
            float dot = value.x * axis.x + value.y * axis.y + value.z * axis.z;
            Vector3 cross = new Vector3(
                axis.y * value.z - axis.z * value.y,
                axis.z * value.x - axis.x * value.z,
                axis.x * value.y - axis.y * value.x);
            return value * cosine + cross * sine + axis * (dot * (1f - cosine));
        }

        private static Vector3 RotateEulerManaged(
            Vector3 euler,
            Vector3 normalizedAxis,
            float degrees)
        {
            LayoutQuaternion radial = LayoutQuaternion.AxisAngle(normalizedAxis, degrees);
            // Unity composes Euler input as Z, then X, then Y for column vectors.
            LayoutQuaternion original =
                LayoutQuaternion.AxisAngle(Vector3.up, euler.y) *
                LayoutQuaternion.AxisAngle(Vector3.right, euler.x) *
                LayoutQuaternion.AxisAngle(Vector3.forward, euler.z);
            return (radial * original).ToUnityEuler();
        }

        private readonly struct LayoutQuaternion
        {
            private readonly float _x;
            private readonly float _y;
            private readonly float _z;
            private readonly float _w;

            private LayoutQuaternion(float x, float y, float z, float w)
            {
                _x = x;
                _y = y;
                _z = z;
                _w = w;
            }

            internal static LayoutQuaternion AxisAngle(Vector3 normalizedAxis, float degrees)
            {
                double halfRadians = degrees * (Math.PI / 360d);
                float sine = (float)Math.Sin(halfRadians);
                return new LayoutQuaternion(
                    normalizedAxis.x * sine,
                    normalizedAxis.y * sine,
                    normalizedAxis.z * sine,
                    (float)Math.Cos(halfRadians));
            }

            public static LayoutQuaternion operator *(
                LayoutQuaternion left,
                LayoutQuaternion right) =>
                new LayoutQuaternion(
                    left._w * right._x + left._x * right._w + left._y * right._z - left._z * right._y,
                    left._w * right._y - left._x * right._z + left._y * right._w + left._z * right._x,
                    left._w * right._z + left._x * right._y - left._y * right._x + left._z * right._w,
                    left._w * right._w - left._x * right._x - left._y * right._y - left._z * right._z);

            internal Vector3 ToUnityEuler()
            {
                double lengthSquared =
                    (double)_x * _x +
                    (double)_y * _y +
                    (double)_z * _z +
                    (double)_w * _w;
                if (lengthSquared <= Epsilon ||
                    double.IsNaN(lengthSquared) ||
                    double.IsInfinity(lengthSquared))
                {
                    return new Vector3(float.NaN, float.NaN, float.NaN);
                }

                double inverse = 1d / Math.Sqrt(lengthSquared);
                double x = _x * inverse;
                double y = _y * inverse;
                double z = _z * inverse;
                double w = _w * inverse;
                double m00 = 1d - 2d * (y * y + z * z);
                double m01 = 2d * (x * y - z * w);
                double m02 = 2d * (x * z + y * w);
                double m10 = 2d * (x * y + z * w);
                double m11 = 1d - 2d * (x * x + z * z);
                double m12 = 2d * (y * z - x * w);
                double m22 = 1d - 2d * (x * x + y * y);

                double sinX = Math.Max(-1d, Math.Min(1d, -m12));
                double xRadians = Math.Asin(sinX);
                double yRadians;
                double zRadians;
                if (Math.Abs(Math.Cos(xRadians)) > Epsilon)
                {
                    yRadians = Math.Atan2(m02, m22);
                    zRadians = Math.Atan2(m10, m11);
                }
                else
                {
                    zRadians = 0d;
                    yRadians = sinX >= 0d
                        ? Math.Atan2(m01, m00)
                        : Math.Atan2(-m01, m00);
                }

                return new Vector3(
                    NormalizeDegrees(xRadians * 180d / Math.PI),
                    NormalizeDegrees(yRadians * 180d / Math.PI),
                    NormalizeDegrees(zRadians * 180d / Math.PI));
            }

            private static float NormalizeDegrees(double degrees)
            {
                double normalized = degrees % 360d;
                if (normalized < 0d)
                    normalized += 360d;
                return Math.Abs(normalized) <= 0.00001d ||
                       Math.Abs(normalized - 360d) <= 0.00001d
                    ? 0f
                    : (float)normalized;
            }
        }

        private static Vector3 Normalize(Vector3 value)
        {
            double length = Math.Sqrt(
                (double)value.x * value.x +
                (double)value.y * value.y +
                (double)value.z * value.z);
            if (length <= Epsilon)
                return Vector3.zero;
            float inverse = (float)(1d / length);
            return value * inverse;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
