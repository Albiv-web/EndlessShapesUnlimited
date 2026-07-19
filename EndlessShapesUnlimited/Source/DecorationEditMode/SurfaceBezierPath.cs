using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    /// <summary>
    /// Pure, bounded cubic-Bezier sampling for generator paths. Interior handles are
    /// derived from neighbouring knots, so moving an existing path point keeps a
    /// continuously tangent path without introducing a second hidden selection model.
    /// </summary>
    internal static class SurfaceBezierPath
    {
        internal const int MinimumKnots = 3;
        internal const int MinimumSubdivisions = 2;
        internal const int MaximumSubdivisions = 64;
        internal const int MaximumSamplePoints = 100001;
        internal const float MinimumTension = 0f;
        internal const float MaximumTension = 2f;
        private const float EdgeLengthSquaredEpsilon = 0.000000000001f;

        internal static bool IsValidSubdivisions(int subdivisions) =>
            subdivisions >= MinimumSubdivisions && subdivisions <= MaximumSubdivisions;

        internal static bool IsValidTension(float tension) =>
            IsFinite(tension) && tension >= MinimumTension && tension <= MaximumTension;

        internal static bool TrySampleAutomatic(
            IReadOnlyList<Vector3> knots,
            int subdivisions,
            float tension,
            out IReadOnlyList<Vector3> samples,
            out string message)
        {
            samples = Array.Empty<Vector3>();
            Vector3[] source = (knots ?? Array.Empty<Vector3>()).ToArray();
            if (source.Length < MinimumKnots)
            {
                message = "Smooth Bezier paths require at least three editable path points.";
                return false;
            }
            if (!IsValidSubdivisions(subdivisions))
            {
                message = "Bezier subdivisions must be " +
                          MinimumSubdivisions.ToString(CultureInfo.InvariantCulture) + " through " +
                          MaximumSubdivisions.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }
            if (!IsValidTension(tension))
            {
                message = "Bezier tension must be finite and between 0 and 2.";
                return false;
            }
            if (source.Any(point => !IsFinite(point)))
            {
                message = "Bezier path points must be finite.";
                return false;
            }
            for (int index = 0; index < source.Length - 1; index++)
            {
                if (!TryFiniteSquaredMagnitude(source[index + 1] - source[index], out float squared) ||
                    squared <= EdgeLengthSquaredEpsilon)
                {
                    message = "Bezier path contains a zero-length or unsupported segment after point " +
                              (index + 1).ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }
            }

            long requested = (long)(source.Length - 1) * subdivisions + 1L;
            if (requested > MaximumSamplePoints)
            {
                message = "Smooth Bezier path would exceed the 100,001-point sampling safety limit.";
                return false;
            }

            var result = new List<Vector3>((int)requested);
            for (int segment = 0; segment < source.Length - 1; segment++)
            {
                Vector3 start = source[segment];
                Vector3 end = source[segment + 1];
                Vector3 previous = segment > 0
                    ? source[segment - 1]
                    : start + (start - end);
                Vector3 next = segment + 2 < source.Length
                    ? source[segment + 2]
                    : end + (end - start);
                Vector3 controlA = start + (end - previous) * (tension / 6f);
                Vector3 controlB = end - (next - start) * (tension / 6f);
                if (!IsFinite(controlA) || !IsFinite(controlB))
                {
                    message = "Bezier handles are outside the supported numeric range.";
                    return false;
                }

                for (int step = 0; step < subdivisions; step++)
                {
                    float t = step / (float)subdivisions;
                    if (!TryEvaluateCubic(start, controlA, controlB, end, t, out Vector3 point))
                    {
                        message = "Bezier sampling produced an unsupported coordinate.";
                        return false;
                    }
                    if (result.Count > 0 &&
                        (!TryFiniteSquaredMagnitude(point - result[result.Count - 1], out float squared) ||
                         squared <= EdgeLengthSquaredEpsilon))
                    {
                        message = "Bezier sampling produced a zero-length segment; reduce tension or move the path points.";
                        return false;
                    }
                    result.Add(point);
                }
            }

            Vector3 final = source[source.Length - 1];
            if (!TryFiniteSquaredMagnitude(final - result[result.Count - 1], out float finalSquared) ||
                finalSquared <= EdgeLengthSquaredEpsilon)
            {
                message = "Bezier sampling produced a zero-length final segment; reduce tension or move the path points.";
                return false;
            }
            result.Add(final);
            samples = result;
            message = "Smooth Bezier path sampled into " +
                      (result.Count - 1).ToString("N0", CultureInfo.InvariantCulture) +
                      " preview segment(s).";
            return true;
        }

        internal static bool TryEvaluateCubic(
            Vector3 start,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 end,
            float t,
            out Vector3 point)
        {
            point = Vector3.zero;
            if (!IsFinite(start) || !IsFinite(controlA) || !IsFinite(controlB) ||
                !IsFinite(end) || !IsFinite(t) || t < 0f || t > 1f)
            {
                return false;
            }

            float inverse = 1f - t;
            float inverseSquared = inverse * inverse;
            float tSquared = t * t;
            point = start * (inverseSquared * inverse) +
                    controlA * (3f * inverseSquared * t) +
                    controlB * (3f * inverse * tSquared) +
                    end * (tSquared * t);
            return IsFinite(point);
        }

        private static bool TryFiniteSquaredMagnitude(Vector3 value, out float squared)
        {
            squared = value.sqrMagnitude;
            return IsFinite(value) && IsFinite(squared);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed partial class DecorationGeneratorDraft
    {
        internal bool TrySetSmoothBezierPath(
            bool enabled,
            int subdivisions,
            float tension,
            out string message)
        {
            if (!UsesPath(Tool))
            {
                message = "Smooth Bezier is available for Path and Tube generators.";
                return false;
            }
            if (!SurfaceBezierPath.IsValidSubdivisions(subdivisions) ||
                !SurfaceBezierPath.IsValidTension(tension))
            {
                message = "Smooth Bezier settings are outside their supported range.";
                return false;
            }
            if (enabled)
            {
                if (!SurfaceBezierPath.TrySampleAutomatic(
                        _pathPoints,
                        subdivisions,
                        tension,
                        out IReadOnlyList<Vector3> _,
                        out message))
                {
                    return false;
                }
            }
            if (SmoothBezierPath == enabled &&
                BezierSubdivisions == subdivisions &&
                Math.Abs(BezierTension - tension) <= 0.0001f)
            {
                message = enabled
                    ? "Smooth Bezier is already enabled with these settings."
                    : "The generator path is already linear.";
                return false;
            }

            SmoothBezierPath = enabled;
            BezierSubdivisions = subdivisions;
            BezierTension = tension;
            message = enabled
                ? "Smooth Bezier path enabled; existing points remain editable knots."
                : "Linear path restored; the editable path points were preserved.";
            return true;
        }

        internal bool TryGetEffectivePathPoints(
            out IReadOnlyList<Vector3> points,
            out string message)
        {
            if (!SmoothBezierPath)
            {
                points = _pathPoints.ToArray();
                message = "Linear generator path is ready.";
                return true;
            }
            return SurfaceBezierPath.TrySampleAutomatic(
                _pathPoints,
                BezierSubdivisions,
                BezierTension,
                out points,
                out message);
        }
    }
}
