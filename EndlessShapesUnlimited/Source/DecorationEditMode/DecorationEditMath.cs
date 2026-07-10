using System;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum DecorationEditAxis
    {
        None,
        Free,
        X,
        Y,
        Z
    }

    internal readonly struct DecorationGizmoProjections
    {
        internal DecorationGizmoProjections(
            Vector2? origin,
            Vector2? xEnd,
            Vector2? yEnd,
            Vector2? zEnd)
            : this(
                origin,
                xEnd,
                null,
                yEnd,
                null,
                zEnd,
                null)
        {
        }

        internal DecorationGizmoProjections(
            Vector2? origin,
            Vector2? xPositiveEnd,
            Vector2? xNegativeEnd,
            Vector2? yPositiveEnd,
            Vector2? yNegativeEnd,
            Vector2? zPositiveEnd,
            Vector2? zNegativeEnd)
        {
            Origin = origin;
            XPositiveEnd = xPositiveEnd;
            XNegativeEnd = xNegativeEnd;
            YPositiveEnd = yPositiveEnd;
            YNegativeEnd = yNegativeEnd;
            ZPositiveEnd = zPositiveEnd;
            ZNegativeEnd = zNegativeEnd;
        }

        internal Vector2? Origin { get; }

        internal Vector2? XPositiveEnd { get; }

        internal Vector2? XNegativeEnd { get; }

        internal Vector2? YPositiveEnd { get; }

        internal Vector2? YNegativeEnd { get; }

        internal Vector2? ZPositiveEnd { get; }

        internal Vector2? ZNegativeEnd { get; }

        internal bool TryGetSegment(
            DecorationEditAxis axis,
            int sign,
            out Vector2 start,
            out Vector2 end)
        {
            start = Vector2.zero;
            end = Vector2.zero;
            if (sign == 0 ||
                !Origin.HasValue ||
                !DecorationEditMath.IsFinite(Origin.Value))
            {
                return false;
            }

            Vector2? candidate = EndFor(axis, sign);
            if (!candidate.HasValue ||
                !DecorationEditMath.IsFinite(candidate.Value))
            {
                return false;
            }

            start = Origin.Value;
            end = candidate.Value;
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            return DecorationEditMath.IsFinite(delta) &&
                   DecorationEditMath.IsFinite(lengthSquared) &&
                   lengthSquared >= DecorationEditMath.MinimumProjectedAxisLengthPixels *
                   DecorationEditMath.MinimumProjectedAxisLengthPixels;
        }

        private Vector2? EndFor(DecorationEditAxis axis, int sign)
        {
            bool negative = sign < 0;
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return negative ? XNegativeEnd : XPositiveEnd;
                case DecorationEditAxis.Y:
                    return negative ? YNegativeEnd : YPositiveEnd;
                case DecorationEditAxis.Z:
                    return negative ? ZNegativeEnd : ZPositiveEnd;
                default:
                    return null;
            }
        }
    }

    internal readonly struct DecorationGizmoPick
    {
        internal DecorationGizmoPick(
            DecorationEditAxis axis,
            int sign,
            float distancePixels)
        {
            Axis = axis;
            Sign = sign;
            DistancePixels = distancePixels;
        }

        internal DecorationEditAxis Axis { get; }

        internal int Sign { get; }

        internal float DistancePixels { get; }

        internal bool IsHit => Axis != DecorationEditAxis.None;
    }

    internal static class DecorationEditMath
    {
        internal const float MoveSnapMetres = 0.001f;
        internal const float PositionLimitMetres = 10f;
        internal const float MinimumProjectedAxisLengthPixels = 4f;

        internal static float Snap(float value, float snap = MoveSnapMetres)
        {
            if (!IsFinite(value) || !IsFinite(snap) || snap <= 0f)
                return value;
            return Mathf.Round(value / snap) * snap;
        }

        internal static Vector3 Snap(Vector3 value, float snap = MoveSnapMetres) =>
            new Vector3(Snap(value.x, snap), Snap(value.y, snap), Snap(value.z, snap));

        internal static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        internal static bool IsFinite(Vector2 value) =>
            IsFinite(value.x) && IsFinite(value.y);

        internal static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool IsWithinPositionLimit(Vector3 value) =>
            IsFinite(value) &&
            Mathf.Abs(value.x) <= PositionLimitMetres &&
            Mathf.Abs(value.y) <= PositionLimitMetres &&
            Mathf.Abs(value.z) <= PositionLimitMetres;

        internal static Vector3 AxisVector(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return Vector3.right;
                case DecorationEditAxis.Y:
                    return Vector3.up;
                case DecorationEditAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.zero;
            }
        }

        internal static Color AxisColor(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Color(1f, 0.2f, 0.2f, 1f);
                case DecorationEditAxis.Y:
                    return new Color(0.2f, 1f, 0.2f, 1f);
                case DecorationEditAxis.Z:
                    return new Color(0.25f, 0.55f, 1f, 1f);
                default:
                    return Color.white;
            }
        }

        internal static DecorationEditAxis PickAxis(
            Vector2 mouse,
            Vector2 origin,
            Vector2 xEnd,
            Vector2 yEnd,
            Vector2 zEnd,
            float thresholdPixels)
        {
            return PickGizmo(
                mouse,
                new DecorationGizmoProjections(origin, xEnd, yEnd, zEnd),
                thresholdPixels,
                freeCorePixels: 0f,
                allowFree: false).Axis;
        }

        internal static DecorationGizmoPick PickGizmo(
            Vector2 mouse,
            DecorationGizmoProjections projections,
            float hitAreaPixels,
            float freeCorePixels,
            bool allowFree)
        {
            if (!IsFinite(mouse) ||
                !IsFinite(hitAreaPixels) ||
                hitAreaPixels <= 0f ||
                !projections.Origin.HasValue ||
                !IsFinite(projections.Origin.Value))
            {
                return new DecorationGizmoPick(
                    DecorationEditAxis.None,
                    0,
                    float.PositiveInfinity);
            }

            float centerDistance = Vector2.Distance(mouse, projections.Origin.Value);
            if (allowFree &&
                IsFinite(freeCorePixels) &&
                freeCorePixels > 0f &&
                centerDistance <= freeCorePixels)
            {
                return new DecorationGizmoPick(
                    DecorationEditAxis.Free,
                    0,
                    centerDistance);
            }

            DecorationEditAxis bestAxis = DecorationEditAxis.None;
            int bestSign = 0;
            float bestDistance = float.PositiveInfinity;
            TryPick(
                DecorationEditAxis.X,
                1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            TryPick(
                DecorationEditAxis.X,
                -1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            TryPick(
                DecorationEditAxis.Y,
                1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            TryPick(
                DecorationEditAxis.Y,
                -1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            TryPick(
                DecorationEditAxis.Z,
                1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            TryPick(
                DecorationEditAxis.Z,
                -1,
                mouse,
                projections,
                hitAreaPixels,
                ref bestAxis,
                ref bestSign,
                ref bestDistance);
            return new DecorationGizmoPick(bestAxis, bestSign, bestDistance);
        }

        internal static float ProjectMouseDeltaToAxis(
            Vector2 mouseDelta,
            Vector2 origin,
            Vector2 axisEnd,
            float axisLengthLocal)
        {
            if (!IsFinite(mouseDelta) ||
                !IsFinite(origin) ||
                !IsFinite(axisEnd) ||
                !IsFinite(axisLengthLocal))
            {
                return 0f;
            }

            Vector2 screenAxis = axisEnd - origin;
            float pixels = screenAxis.magnitude;
            if (pixels < 0.001f || axisLengthLocal <= 0f)
                return 0f;
            return Vector2.Dot(mouseDelta, screenAxis / pixels) * axisLengthLocal / pixels;
        }

        internal static bool TryProjectMouseDeltaToAxisInvariant(
            Vector2 mouseDelta,
            Vector2 origin,
            Vector2 visualEnd,
            Vector2 referenceEnd,
            float referenceLengthLocal,
            out float projectedDelta)
        {
            projectedDelta = 0f;
            if (!IsFinite(mouseDelta) ||
                !IsFinite(origin) ||
                !IsFinite(visualEnd) ||
                !IsFinite(referenceEnd) ||
                !IsFinite(referenceLengthLocal) ||
                referenceLengthLocal <= 0f)
            {
                return false;
            }

            Vector2 visualAxis = visualEnd - origin;
            Vector2 referenceAxis = referenceEnd - origin;
            float visualPixels = visualAxis.magnitude;
            float referencePixels = referenceAxis.magnitude;
            if (!IsFinite(visualAxis) ||
                !IsFinite(referenceAxis) ||
                !IsFinite(visualPixels) ||
                !IsFinite(referencePixels) ||
                visualPixels < MinimumProjectedAxisLengthPixels ||
                referencePixels < MinimumProjectedAxisLengthPixels)
            {
                return false;
            }

            projectedDelta = Vector2.Dot(mouseDelta, visualAxis / visualPixels) *
                             referenceLengthLocal /
                             referencePixels;
            return IsFinite(projectedDelta);
        }

        internal static bool TryProjectMouseDeltaToAxis(
            Vector2 mouseDelta,
            DecorationGizmoProjections projections,
            DecorationEditAxis axis,
            int sign,
            float axisLengthLocal,
            out float projectedDelta)
        {
            projectedDelta = 0f;
            if (!projections.TryGetSegment(axis, sign, out Vector2 origin, out Vector2 end) ||
                !IsFinite(mouseDelta) ||
                !IsFinite(axisLengthLocal) ||
                axisLengthLocal <= 0f)
            {
                return false;
            }

            projectedDelta = ProjectMouseDeltaToAxis(
                mouseDelta,
                origin,
                end,
                axisLengthLocal);
            return IsFinite(projectedDelta);
        }

        private static void TryPick(
            DecorationEditAxis axis,
            int sign,
            Vector2 mouse,
            DecorationGizmoProjections projections,
            float hitAreaPixels,
            ref DecorationEditAxis bestAxis,
            ref int bestSign,
            ref float bestDistance)
        {
            if (!projections.TryGetSegment(axis, sign, out Vector2 start, out Vector2 end))
                return;

            float distance = DistanceToSegment(mouse, start, end);
            if (distance <= hitAreaPixels && distance < bestDistance)
            {
                bestDistance = distance;
                bestAxis = axis;
                bestSign = sign;
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (!IsFinite(segment) || !IsFinite(lengthSquared))
                return float.PositiveInfinity;
            if (lengthSquared <= 0.0001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }
    }
}
