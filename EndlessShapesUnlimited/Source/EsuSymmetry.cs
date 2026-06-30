using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter
{
    internal static class EsuSymmetry
    {
        private static readonly Dictionary<DecorationEditAxis, int> Planes =
            new Dictionary<DecorationEditAxis, int>();

        private static AllConstruct _construct;
        private static AllConstruct _boundsCacheConstruct;
        private static Vector3 _boundsCacheMin;
        private static Vector3 _boundsCacheMax;
        private static float _boundsCacheUntil;
        private static bool _boundsCacheValid;

        private const float BoundsCacheSeconds = 0.5f;
        private const float MinimumPlaneSpan = 8f;

        internal static DecorationEditAxis PendingAxis { get; private set; } =
            DecorationEditAxis.None;

        internal static bool HasActivePlanes => Planes.Count > 0;

        internal static AllConstruct Construct => _construct;

        internal static IReadOnlyDictionary<DecorationEditAxis, int> ActivePlanes => Planes;

        internal static bool IsPending(DecorationEditAxis axis) =>
            PendingAxis == axis;

        internal static bool IsActive(DecorationEditAxis axis) =>
            Planes.ContainsKey(axis);

        internal static string FormatSummary()
        {
            if (PendingAxis != DecorationEditAxis.None)
                return "placing " + AxisName(PendingAxis);
            if (Planes.Count == 0)
                return "off";

            return string.Join(
                "+",
                Planes.Keys
                    .OrderBy(axis => axis)
                    .Select(AxisName));
        }

        internal static void ToggleAxis(DecorationEditAxis axis)
        {
            if (!IsSymmetryAxis(axis))
                return;

            if (PendingAxis == axis)
            {
                PendingAxis = DecorationEditAxis.None;
                return;
            }

            if (Planes.Remove(axis))
            {
                PendingAxis = DecorationEditAxis.None;
                if (Planes.Count == 0)
                    _construct = null;
                return;
            }

            PendingAxis = axis;
        }

        internal static void CancelPending() =>
            PendingAxis = DecorationEditAxis.None;

        internal static void Clear()
        {
            Planes.Clear();
            _construct = null;
            PendingAxis = DecorationEditAxis.None;
        }

        internal static void SetPlaneForTests(DecorationEditAxis axis, int coordinate)
        {
            if (!IsSymmetryAxis(axis))
                return;
            Planes[axis] = coordinate;
            PendingAxis = DecorationEditAxis.None;
        }

        internal static bool TryPlacePending(
            AllConstruct construct,
            Vector3i cell,
            out string reason)
        {
            reason = null;
            if (PendingAxis == DecorationEditAxis.None)
            {
                reason = "No symmetry axis is waiting for placement.";
                return false;
            }

            if (construct == null)
            {
                reason = "Point at the focused construct grid to place the symmetry plane.";
                return false;
            }

            if (_construct != null && !ReferenceEquals(_construct, construct))
            {
                reason = "Clear the existing symmetry planes before placing one on another construct.";
                return false;
            }

            _construct = construct;
            Planes[PendingAxis] = AxisComponent(cell, PendingAxis);
            PendingAxis = DecorationEditAxis.None;
            return true;
        }

        internal static bool CanUseWith(AllConstruct construct, out string reason)
        {
            reason = null;
            if (Planes.Count == 0)
                return true;

            if (construct == null)
            {
                reason = "A valid construct is required while symmetry is active.";
                return false;
            }

            if (!ReferenceEquals(_construct, construct))
            {
                reason = "Symmetry planes belong to another construct grid.";
                return false;
            }

            return true;
        }

        internal static Vector3i MirrorCell(Vector3i cell, DecorationEditAxis axis, int plane)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Vector3i(2 * plane - cell.x, cell.y, cell.z);
                case DecorationEditAxis.Y:
                    return new Vector3i(cell.x, 2 * plane - cell.y, cell.z);
                case DecorationEditAxis.Z:
                    return new Vector3i(cell.x, cell.y, 2 * plane - cell.z);
                default:
                    return cell;
            }
        }

        internal static Vector3 MirrorVector(Vector3 value, DecorationEditAxis axis, int plane)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    value.x = 2f * plane - value.x;
                    break;
                case DecorationEditAxis.Y:
                    value.y = 2f * plane - value.y;
                    break;
                case DecorationEditAxis.Z:
                    value.z = 2f * plane - value.z;
                    break;
            }

            return value;
        }

        internal static IReadOnlyList<SymmetryVariant> Variants()
        {
            if (Planes.Count == 0)
                return new[] { new SymmetryVariant(Array.Empty<DecorationEditAxis>()) };

            DecorationEditAxis[] axes = Planes.Keys
                .OrderBy(axis => axis)
                .ToArray();
            int count = 1 << axes.Length;
            var variants = new List<SymmetryVariant>(count);
            for (int mask = 0; mask < count; mask++)
            {
                var mirroredAxes = new List<DecorationEditAxis>(axes.Length);
                for (int index = 0; index < axes.Length; index++)
                {
                    if ((mask & (1 << index)) != 0)
                        mirroredAxes.Add(axes[index]);
                }

                variants.Add(new SymmetryVariant(mirroredAxes));
            }

            return variants;
        }

        internal static IEnumerable<Vector3i> MirrorCells(IEnumerable<Vector3i> cells)
        {
            if (cells == null)
                yield break;

            var seen = new HashSet<string>();
            foreach (Vector3i cell in cells)
            {
                foreach (SymmetryVariant variant in Variants())
                {
                    Vector3i mirrored = variant.Mirror(cell);
                    if (seen.Add(CellKey(mirrored)))
                        yield return mirrored;
                }
            }
        }

        internal static void DrawPlane(
            AllConstruct construct,
            DecorationEditAxis axis,
            int coordinate,
            Vector3 aroundLocal,
            bool pending)
        {
            if (construct == null || !IsSymmetryAxis(axis))
                return;

            Vector3 center = aroundLocal;
            float radius = pending ? 3.2f : 2.6f;
            float halfA = radius;
            float halfB = radius;
            if (TryGetConstructLocalBounds(construct, out Vector3 min, out Vector3 max))
            {
                center = (min + max) * 0.5f;
                BuildPlaneAxisPair(axis, out DecorationEditAxis axisA, out DecorationEditAxis axisB);
                halfA = PlaneHalfSpan(min, max, axisA);
                halfB = PlaneHalfSpan(min, max, axisB);
            }

            center = SetAxis(center, axis, coordinate);
            Color color = DecorationEditMath.AxisColor(axis);
            Color face = new Color(color.r, color.g, color.b, pending ? 0.12f : 0.07f);
            Color edge = new Color(color.r, color.g, color.b, pending ? 0.95f : 0.72f);
            BuildPlaneBasis(axis, out Vector3 a, out Vector3 b);
            Vector3 p0;
            Vector3 p1;
            Vector3 p2;
            Vector3 p3;
            Vector3 a0;
            Vector3 a1;
            Vector3 b0;
            Vector3 b1;
            try
            {
                p0 = construct.SafeLocalToGlobal(center - a * halfA - b * halfB);
                p1 = construct.SafeLocalToGlobal(center + a * halfA - b * halfB);
                p2 = construct.SafeLocalToGlobal(center + a * halfA + b * halfB);
                p3 = construct.SafeLocalToGlobal(center - a * halfA + b * halfB);
                a0 = construct.SafeLocalToGlobal(center - a * halfA);
                a1 = construct.SafeLocalToGlobal(center + a * halfA);
                b0 = construct.SafeLocalToGlobal(center - b * halfB);
                b1 = construct.SafeLocalToGlobal(center + b * halfB);
            }
            catch
            {
                return;
            }

            DecorationEditorOverlay.Quad(p0, p1, p2, p3, face);
            DecorationEditorOverlay.Line(p0, p1, edge, pending ? 3.2f : 2f);
            DecorationEditorOverlay.Line(p1, p2, edge, pending ? 3.2f : 2f);
            DecorationEditorOverlay.Line(p2, p3, edge, pending ? 3.2f : 2f);
            DecorationEditorOverlay.Line(p3, p0, edge, pending ? 3.2f : 2f);
            DecorationEditorOverlay.Line(
                a0,
                a1,
                edge,
                pending ? 3.4f : 2.2f);
            DecorationEditorOverlay.Line(
                b0,
                b1,
                edge,
                pending ? 3.4f : 2.2f);
        }

        internal static string AxisName(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return "X";
                case DecorationEditAxis.Y:
                    return "Y";
                case DecorationEditAxis.Z:
                    return "Z";
                default:
                    return "-";
            }
        }

        internal static bool IsSymmetryAxis(DecorationEditAxis axis) =>
            axis == DecorationEditAxis.X ||
            axis == DecorationEditAxis.Y ||
            axis == DecorationEditAxis.Z;

        internal static int AxisComponent(Vector3i cell, DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return cell.x;
                case DecorationEditAxis.Y:
                    return cell.y;
                case DecorationEditAxis.Z:
                    return cell.z;
                default:
                    return 0;
            }
        }

        internal static float AxisComponent(Vector3 value, DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return value.x;
                case DecorationEditAxis.Y:
                    return value.y;
                case DecorationEditAxis.Z:
                    return value.z;
                default:
                    return 0f;
            }
        }

        internal static string CellKey(Vector3i cell) =>
            cell.x + ":" + cell.y + ":" + cell.z;

        internal static Vector3 ToVector3(Vector3i cell) =>
            new Vector3(cell.x, cell.y, cell.z);

        private static Vector3 SetAxis(Vector3 value, DecorationEditAxis axis, float component)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    value.x = component;
                    break;
                case DecorationEditAxis.Y:
                    value.y = component;
                    break;
                case DecorationEditAxis.Z:
                    value.z = component;
                    break;
            }

            return value;
        }

        internal static bool TryGetConstructLocalBounds(
            AllConstruct construct,
            out Vector3 min,
            out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            if (construct == null)
                return false;

            float now = Time.unscaledTime;
            if (ReferenceEquals(_boundsCacheConstruct, construct) &&
                now <= _boundsCacheUntil)
            {
                min = _boundsCacheMin;
                max = _boundsCacheMax;
                return _boundsCacheValid;
            }

            _boundsCacheConstruct = construct;
            _boundsCacheUntil = now + BoundsCacheSeconds;
            _boundsCacheValid = false;
            try
            {
                Vector3i rawMin = construct.GetMin();
                Vector3i rawMax = construct.GetMax();
                int minX = Math.Min(rawMin.x, rawMax.x);
                int minY = Math.Min(rawMin.y, rawMax.y);
                int minZ = Math.Min(rawMin.z, rawMax.z);
                int maxX = Math.Max(rawMin.x, rawMax.x);
                int maxY = Math.Max(rawMin.y, rawMax.y);
                int maxZ = Math.Max(rawMin.z, rawMax.z);
                min = new Vector3(minX - 0.5f, minY - 0.5f, minZ - 0.5f);
                max = new Vector3(maxX + 0.5f, maxY + 0.5f, maxZ + 0.5f);
                if (max.x < min.x || max.y < min.y || max.z < min.z)
                    return false;

                _boundsCacheMin = min;
                _boundsCacheMax = max;
                _boundsCacheValid = true;
                return true;
            }
            catch
            {
                _boundsCacheMin = Vector3.zero;
                _boundsCacheMax = Vector3.zero;
                return false;
            }
        }

        private static float PlaneHalfSpan(Vector3 min, Vector3 max, DecorationEditAxis axis)
        {
            float span = AxisComponent(max, axis) - AxisComponent(min, axis);
            float padded = span + Mathf.Max(2f, span * 0.12f);
            return Mathf.Max(MinimumPlaneSpan, padded) * 0.5f;
        }

        private static void BuildPlaneAxisPair(
            DecorationEditAxis axis,
            out DecorationEditAxis a,
            out DecorationEditAxis b)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    a = DecorationEditAxis.Y;
                    b = DecorationEditAxis.Z;
                    break;
                case DecorationEditAxis.Y:
                    a = DecorationEditAxis.X;
                    b = DecorationEditAxis.Z;
                    break;
                default:
                    a = DecorationEditAxis.X;
                    b = DecorationEditAxis.Y;
                    break;
            }
        }

        private static void BuildPlaneBasis(
            DecorationEditAxis axis,
            out Vector3 a,
            out Vector3 b)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    a = Vector3.up;
                    b = Vector3.forward;
                    break;
                case DecorationEditAxis.Y:
                    a = Vector3.right;
                    b = Vector3.forward;
                    break;
                default:
                    a = Vector3.right;
                    b = Vector3.up;
                    break;
            }
        }

        internal readonly struct SymmetryVariant
        {
            private readonly DecorationEditAxis[] _axes;

            internal SymmetryVariant(IEnumerable<DecorationEditAxis> axes)
            {
                _axes = axes?.ToArray() ?? Array.Empty<DecorationEditAxis>();
            }

            internal IReadOnlyList<DecorationEditAxis> Axes => _axes;

            internal bool IsIdentity => _axes.Length == 0;

            internal int AxisCount => _axes.Length;

            internal Vector3i Mirror(Vector3i cell)
            {
                for (int index = 0; index < _axes.Length; index++)
                    cell = MirrorCell(cell, _axes[index], Planes[_axes[index]]);
                return cell;
            }

            internal Vector3 Mirror(Vector3 value)
            {
                for (int index = 0; index < _axes.Length; index++)
                    value = MirrorVector(value, _axes[index], Planes[_axes[index]]);
                return value;
            }
        }
    }
}
