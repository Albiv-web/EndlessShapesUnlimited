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

    internal static class DecorationEditMath
    {
        internal const float MoveSnapMetres = 0.001f;
        internal const float PositionLimitMetres = 10f;

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
            DecorationEditAxis bestAxis = DecorationEditAxis.None;
            float bestDistance = thresholdPixels;
            TryPick(DecorationEditAxis.X, mouse, origin, xEnd, ref bestAxis, ref bestDistance);
            TryPick(DecorationEditAxis.Y, mouse, origin, yEnd, ref bestAxis, ref bestDistance);
            TryPick(DecorationEditAxis.Z, mouse, origin, zEnd, ref bestAxis, ref bestDistance);
            return bestAxis;
        }

        internal static float ProjectMouseDeltaToAxis(
            Vector2 mouseDelta,
            Vector2 origin,
            Vector2 axisEnd,
            float axisLengthLocal)
        {
            Vector2 screenAxis = axisEnd - origin;
            float pixels = screenAxis.magnitude;
            if (pixels < 0.001f || axisLengthLocal <= 0f)
                return 0f;
            return Vector2.Dot(mouseDelta, screenAxis / pixels) * axisLengthLocal / pixels;
        }

        private static void TryPick(
            DecorationEditAxis axis,
            Vector2 mouse,
            Vector2 start,
            Vector2 end,
            ref DecorationEditAxis bestAxis,
            ref float bestDistance)
        {
            float distance = DistanceToSegment(mouse, start, end);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAxis = axis;
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }
    }
}
