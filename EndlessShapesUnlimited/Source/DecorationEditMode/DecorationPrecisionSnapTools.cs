using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationPrecisionMeasurement
    {
        internal DecorationPrecisionMeasurement(
            Vector3 delta,
            float centerDistance,
            Vector3 clearanceByAxis,
            float boundsClearance)
        {
            Delta = delta;
            CenterDistance = centerDistance;
            ClearanceByAxis = clearanceByAxis;
            BoundsClearance = boundsClearance;
        }

        internal Vector3 Delta { get; }

        internal float CenterDistance { get; }

        internal Vector3 ClearanceByAxis { get; }

        internal float BoundsClearance { get; }
    }

    /// <summary>
    /// Pure preflight planners for precision snapping. The primary decoration is
    /// moved to the requested target and every editable selection member receives
    /// the identical translation, preserving the group's internal layout. Locked
    /// members stay untouched and are reported by the resulting layout plan.
    /// </summary>
    internal static class DecorationPrecisionSnapTools
    {
        private const float Epsilon = 0.000001f;

        internal static bool TrySnapToSurface(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            Vector3 surfacePoint,
            Vector3 outwardNormal,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryPrepare(items, referenceKey, out DecorationLayoutItem[] source,
                    out DecorationLayoutItem reference, out message) ||
                !IsFinite(surfacePoint) ||
                !TryNormalize(outwardNormal, out Vector3 normal))
            {
                if (message == null)
                    message = "Surface snap requires a finite point and non-zero finite normal.";
                return false;
            }

            Vector3 extents = Abs(reference.Extents);
            float support = Math.Abs(normal.x) * extents.x +
                            Math.Abs(normal.y) * extents.y +
                            Math.Abs(normal.z) * extents.z;
            Vector3 targetCenter = surfacePoint + normal * support;
            if (!IsFinite(targetCenter))
            {
                message = "Surface snap target is outside the supported numeric range.";
                return false;
            }

            return TryTranslate(
                source,
                reference,
                targetCenter - reference.Center,
                "Snap selection to surface",
                "Snapped selection to the picked surface without crossing its support plane.",
                out plan,
                out message);
        }

        internal static bool TrySnapToAnchor(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            Vector3 anchorPoint,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryPrepare(items, referenceKey, out DecorationLayoutItem[] source,
                    out DecorationLayoutItem reference, out message) ||
                !IsFinite(anchorPoint))
            {
                if (message == null)
                    message = "Anchor snap requires a finite anchor point.";
                return false;
            }

            return TryTranslate(
                source,
                reference,
                anchorPoint - reference.Center,
                "Snap selection to anchor",
                "Snapped the primary center to the selected anchor.",
                out plan,
                out message);
        }

        internal static bool TrySnapToAxis(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            Vector3 axisOrigin,
            Vector3 axisDirection,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!TryPrepare(items, referenceKey, out DecorationLayoutItem[] source,
                    out DecorationLayoutItem reference, out message) ||
                !IsFinite(axisOrigin) ||
                !TryNormalize(axisDirection, out Vector3 axis))
            {
                if (message == null)
                    message = "Axis snap requires a finite origin and non-zero finite direction.";
                return false;
            }

            Vector3 relative = reference.Center - axisOrigin;
            float distanceAlong = Dot(relative, axis);
            Vector3 projected = axisOrigin + axis * distanceAlong;
            if (!IsFinite(projected))
            {
                message = "Axis projection is outside the supported numeric range.";
                return false;
            }

            return TryTranslate(
                source,
                reference,
                projected - reference.Center,
                "Snap selection to axis",
                "Snapped the primary center to the selected axis.",
                out plan,
                out message);
        }

        internal static bool TryMeasure(
            DecorationLayoutItem first,
            DecorationLayoutItem second,
            out DecorationPrecisionMeasurement measurement,
            out string message)
        {
            measurement = null;
            if (!DecorationLayoutTools.IsValidItem(first) ||
                !DecorationLayoutTools.IsValidItem(second))
            {
                message = "Ruler endpoints must contain valid finite transforms and bounds.";
                return false;
            }

            Vector3 delta = second.Center - first.Center;
            Vector3 extents = Abs(first.Extents) + Abs(second.Extents);
            Vector3 clearance = new Vector3(
                Math.Max(0f, Math.Abs(delta.x) - extents.x),
                Math.Max(0f, Math.Abs(delta.y) - extents.y),
                Math.Max(0f, Math.Abs(delta.z) - extents.z));
            if (!TryLength(delta, out float distance) ||
                !TryLength(clearance, out float boundsClearance))
            {
                message = "Ruler result is outside the supported numeric range.";
                return false;
            }

            measurement = new DecorationPrecisionMeasurement(
                delta,
                distance,
                clearance,
                boundsClearance);
            message = "Measured " + distance.ToString("0.#####", CultureInfo.InvariantCulture) +
                      "m center distance and " +
                      boundsClearance.ToString("0.#####", CultureInfo.InvariantCulture) +
                      "m bounds clearance.";
            return true;
        }

        private static bool TryPrepare(
            IReadOnlyList<DecorationLayoutItem> items,
            string referenceKey,
            out DecorationLayoutItem[] source,
            out DecorationLayoutItem reference,
            out string message)
        {
            source = (items ?? Array.Empty<DecorationLayoutItem>()).ToArray();
            reference = null;
            if (source.Length == 0 || source.Any(item => !DecorationLayoutTools.IsValidItem(item)))
            {
                message = "Select at least one decoration with valid finite transform and bounds data.";
                return false;
            }
            if (source.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != source.Length)
            {
                message = "Precision-snap selection contains duplicate item keys.";
                return false;
            }
            reference = source.FirstOrDefault(item =>
                string.Equals(item.Key, referenceKey, StringComparison.Ordinal));
            if (reference == null)
            {
                message = "The primary/reference decoration is not part of the snap selection.";
                return false;
            }
            if (!reference.Editable)
            {
                message = "Unlock the primary decoration before snapping the selection.";
                return false;
            }

            message = "Precision-snap selection is valid.";
            return true;
        }

        private static bool TryTranslate(
            DecorationLayoutItem[] source,
            DecorationLayoutItem reference,
            Vector3 translation,
            string label,
            string success,
            out DecorationLayoutPlan plan,
            out string message)
        {
            plan = null;
            if (!IsFinite(translation))
            {
                message = "Snap translation is outside the supported numeric range.";
                return false;
            }

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

                Vector3 center = item.Center + translation;
                if (!IsFinite(center))
                {
                    message = "Snap would move a decoration outside the supported numeric range.";
                    return false;
                }
                after[index] = item.WithTransform(center, item.Rotation, item.Scale);
            }

            var candidate = new DecorationLayoutPlan(label, source, after, skipped);
            if (!candidate.IsValid || candidate.ChangedCount == 0)
            {
                message = candidate.IsValid
                    ? "The primary decoration is already snapped to that target."
                    : "Snap produced an invalid transform plan.";
                return false;
            }

            plan = candidate;
            message = success +
                      (skipped > 0
                          ? " " + skipped.ToString("N0", CultureInfo.InvariantCulture) +
                            " locked selection member(s) were left unchanged."
                          : string.Empty);
            return true;
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!TryLength(value, out float length) || length <= Epsilon)
                return false;
            normalized = value / length;
            return IsFinite(normalized);
        }

        private static bool TryLength(Vector3 value, out float length)
        {
            length = 0f;
            if (!IsFinite(value))
                return false;
            double squared = (double)value.x * value.x +
                             (double)value.y * value.y +
                             (double)value.z * value.z;
            if (double.IsNaN(squared) || double.IsInfinity(squared))
                return false;
            length = (float)Math.Sqrt(squared);
            return IsFinite(length);
        }

        private static float Dot(Vector3 left, Vector3 right) =>
            left.x * right.x + left.y * right.y + left.z * right.z;

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z));

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
