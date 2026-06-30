using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum SurfaceExtraTool
    {
        None,
        Path,
        Circle
    }

    internal enum DecorationGeneratorStyle
    {
        Pipe,
        Cable,
        Rail
    }

    internal sealed class DecorationGeneratorSettings
    {
        internal DecorationGeneratorSettings()
        {
            Style = DecorationGeneratorStyle.Pipe;
            LengthAxis = DecorationEditAxis.X;
            Diameter = 0.1f;
            CircleRadius = 2f;
            CircleSegments = 24;
            ColorIndex = 0;
        }

        internal DecorationGeneratorStyle Style { get; set; }

        internal Guid MeshGuid { get; set; }

        internal bool UseSelectedMesh { get; set; }

        internal DecorationEditAxis LengthAxis { get; set; }

        internal float Diameter { get; set; }

        internal float CircleRadius { get; set; }

        internal int CircleSegments { get; set; }

        internal int ColorIndex { get; set; }

        internal Guid MaterialReplacement { get; set; }

        internal bool IsValid(out string reason)
        {
            reason = null;
            if (MeshGuid == Guid.Empty)
            {
                reason = "Choose a valid generator mesh before previewing.";
                return false;
            }

            if (LengthAxis != DecorationEditAxis.X &&
                LengthAxis != DecorationEditAxis.Y &&
                LengthAxis != DecorationEditAxis.Z)
            {
                reason = "Generator length axis must be X, Y, or Z.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(Diameter) || Diameter <= 0f)
            {
                reason = "Generator diameter must be finite and greater than zero.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(CircleRadius) || CircleRadius <= 0f)
            {
                reason = "Circle radius must be finite and greater than zero.";
                return false;
            }

            if (CircleSegments < 3 || CircleSegments > 256)
            {
                reason = "Circle segment count must be 3 through 256.";
                return false;
            }

            if (ColorIndex < 0 || ColorIndex > 31)
            {
                reason = "Generator color must be 0 through 31.";
                return false;
            }

            return true;
        }
    }

    internal sealed class DecorationGeneratorDraft
    {
        private readonly List<Vector3> _pathPoints = new List<Vector3>();

        internal SurfaceExtraTool Tool { get; private set; } = SurfaceExtraTool.Path;

        internal AllConstruct Construct { get; private set; }

        internal IReadOnlyList<Vector3> PathPoints => _pathPoints;

        internal bool HasCircleCenter { get; private set; }

        internal Vector3 CircleCenter { get; private set; }

        internal Vector3 CircleNormal { get; private set; } = Vector3.up;

        internal int SelectedPoint { get; private set; } = -1;

        internal bool HasDraft =>
            _pathPoints.Count > 0 ||
            HasCircleCenter;

        internal bool HasActiveSelection => SelectedPoint >= 0;

        internal int PointCount =>
            Tool == SurfaceExtraTool.Circle && HasCircleCenter
                ? 1
                : _pathPoints.Count;

        internal void SetTool(SurfaceExtraTool tool)
        {
            if (tool == SurfaceExtraTool.None)
                tool = SurfaceExtraTool.Path;
            Tool = tool;
            ClearSelection();
        }

        internal void Clear()
        {
            Construct = null;
            _pathPoints.Clear();
            HasCircleCenter = false;
            CircleCenter = Vector3.zero;
            CircleNormal = Vector3.up;
            ClearSelection();
        }

        internal void ClearSelection() =>
            SelectedPoint = -1;

        internal bool TryAddPathPoint(AllConstruct construct, Vector3 local, out string message)
        {
            message = null;
            if (!TryAcceptConstruct(construct, out message))
                return false;

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Path point must be finite.";
                return false;
            }

            Tool = SurfaceExtraTool.Path;
            _pathPoints.Add(DecorationEditMath.Snap(local));
            SelectedPoint = _pathPoints.Count - 1;
            message = _pathPoints.Count < 2
                ? "Path start point placed."
                : "Path point " + _pathPoints.Count.ToString(CultureInfo.InvariantCulture) + " placed.";
            return true;
        }

        internal bool TrySetCircleCenter(
            AllConstruct construct,
            Vector3 local,
            Vector3 normal,
            out string message)
        {
            message = null;
            if (!TryAcceptConstruct(construct, out message))
                return false;

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Circle center must be finite.";
                return false;
            }

            Tool = SurfaceExtraTool.Circle;
            CircleCenter = DecorationEditMath.Snap(local);
            CircleNormal = NormalizeOrDefault(normal, Vector3.up);
            HasCircleCenter = true;
            SelectedPoint = 0;
            message = "Circle center placed.";
            return true;
        }

        internal bool TryMoveSelectedPoint(Vector3 local, Vector3 normal, out string message)
        {
            message = null;
            if (SelectedPoint < 0)
            {
                message = "Select a generator point before moving it.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Generator point move rejected because the position is not finite.";
                return false;
            }

            if (Tool == SurfaceExtraTool.Circle)
            {
                if (!HasCircleCenter)
                {
                    message = "Place a circle center before moving it.";
                    return false;
                }

                CircleCenter = DecorationEditMath.Snap(local);
                if (DecorationEditMath.IsFinite(normal) && normal.sqrMagnitude > 0.0001f)
                    CircleNormal = normal.normalized;
                return true;
            }

            if (SelectedPoint >= _pathPoints.Count)
            {
                message = "Selected path point is no longer available.";
                return false;
            }

            _pathPoints[SelectedPoint] = DecorationEditMath.Snap(local);
            return true;
        }

        internal bool TryDeleteSelection(out string message)
        {
            message = null;
            if (SelectedPoint < 0)
            {
                message = "Select a generator point before deleting.";
                return false;
            }

            if (Tool == SurfaceExtraTool.Circle)
            {
                HasCircleCenter = false;
                CircleCenter = Vector3.zero;
                SelectedPoint = -1;
                message = "Circle center deleted.";
                return true;
            }

            if (SelectedPoint >= _pathPoints.Count)
            {
                SelectedPoint = -1;
                message = "Selected path point is no longer available.";
                return false;
            }

            _pathPoints.RemoveAt(SelectedPoint);
            SelectedPoint = -1;
            message = "Path point deleted.";
            return true;
        }

        internal void SelectPoint(int index)
        {
            if (Tool == SurfaceExtraTool.Circle)
            {
                SelectedPoint = HasCircleCenter && index == 0 ? 0 : -1;
                return;
            }

            SelectedPoint = index >= 0 && index < _pathPoints.Count ? index : -1;
        }

        internal Vector3 PointAt(int index)
        {
            if (Tool == SurfaceExtraTool.Circle)
                return CircleCenter;
            return _pathPoints[index];
        }

        internal DecorationGeneratorDraft CreateMirroredForSymmetry(
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            var mirrored = new DecorationGeneratorDraft();
            mirrored.Tool = Tool;
            mirrored.Construct = Construct;
            for (int index = 0; index < _pathPoints.Count; index++)
                mirrored._pathPoints.Add(variant.Mirror(_pathPoints[index]));

            if (HasCircleCenter)
            {
                Vector3 mirroredCenter = variant.Mirror(CircleCenter);
                Vector3 mirroredTip = variant.Mirror(CircleCenter + CircleNormal);
                mirrored.CircleCenter = mirroredCenter;
                mirrored.CircleNormal = NormalizeOrDefault(mirroredTip - mirroredCenter, CircleNormal);
                mirrored.HasCircleCenter = true;
            }

            return mirrored;
        }

        private bool TryAcceptConstruct(AllConstruct construct, out string message)
        {
            message = null;
            if (construct == null)
            {
                if (Construct == null)
                    return true;
                message = "Generator points must stay on the same construct.";
                return false;
            }

            if (Construct == null)
            {
                Construct = construct;
                return true;
            }

            if (ReferenceEquals(Construct, construct))
                return true;

            message = "Generator points must stay on the same construct.";
            return false;
        }

        private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
        {
            if (DecorationEditMath.IsFinite(value) && value.sqrMagnitude > 0.0001f)
                return value.normalized;
            if (DecorationEditMath.IsFinite(fallback) && fallback.sqrMagnitude > 0.0001f)
                return fallback.normalized;
            return Vector3.up;
        }
    }

    internal sealed class DecorationGeneratorPlan
    {
        internal DecorationGeneratorPlan(
            AllConstruct construct,
            IReadOnlyList<DecorationGeneratorPlacement> placements,
            IReadOnlyList<string> warnings)
        {
            Construct = construct;
            Placements = placements ?? Array.Empty<DecorationGeneratorPlacement>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        internal AllConstruct Construct { get; }

        internal IReadOnlyList<DecorationGeneratorPlacement> Placements { get; }

        internal IReadOnlyList<string> Warnings { get; }

        internal int DecorationCount => Placements.Count;
    }

    internal sealed class DecorationGeneratorPlacement
    {
        internal DecorationGeneratorPlacement(
            Vector3i anchor,
            Guid meshGuid,
            Vector3 positioning,
            Vector3 scaling,
            Vector3 orientation,
            int color,
            Guid materialReplacement)
        {
            Anchor = anchor;
            MeshGuid = meshGuid;
            Positioning = positioning;
            Scaling = scaling;
            Orientation = orientation;
            Color = color;
            MaterialReplacement = materialReplacement;
        }

        internal Vector3i Anchor { get; }

        internal Guid MeshGuid { get; }

        internal Vector3 Positioning { get; }

        internal Vector3 Scaling { get; }

        internal Vector3 Orientation { get; }

        internal int Color { get; }

        internal Guid MaterialReplacement { get; }
    }

    internal static class DecorationGeneratorPlanner
    {
        private const float GeometryEpsilon = 0.000001f;

        internal static bool TryPlan(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            out DecorationGeneratorPlan plan,
            out string message)
        {
            plan = null;
            message = null;
            if (draft == null)
            {
                message = "Create a generator draft before previewing.";
                return false;
            }

            if (settings == null || !settings.IsValid(out message))
                return false;

            List<GeneratorSegment> segments = BuildSegments(draft, settings, out message);
            if (segments == null || segments.Count == 0)
                return false;

            var placements = new List<DecorationGeneratorPlacement>(segments.Count);
            var warnings = new List<string>();
            for (int index = 0; index < segments.Count; index++)
            {
                if (!TryCreatePlacement(
                        segments[index],
                        index,
                        settings,
                        anchorResolver,
                        placements,
                        out message))
                {
                    plan = null;
                    return false;
                }
            }

            plan = new DecorationGeneratorPlan(draft.Construct, placements, warnings);
            message = placements.Count.ToString("N0", CultureInfo.InvariantCulture) + " generated decoration(s) ready.";
            return true;
        }

        internal static bool TryPlanWithSymmetry(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            out DecorationGeneratorPlan plan,
            out string message)
        {
            plan = null;
            if (draft == null)
            {
                message = "Create a generator draft before previewing.";
                return false;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(draft.Construct, out message))
                return false;

            return TryPlanMirroredVariants(
                draft,
                settings,
                anchorResolver,
                DecoLimitLifter.EsuSymmetry.Variants(),
                out plan,
                out message);
        }

        internal static bool TryPlanMirroredVariants(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            IEnumerable<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variants,
            out DecorationGeneratorPlan plan,
            out string message)
        {
            plan = null;
            message = null;
            if (draft == null)
            {
                message = "Create a generator draft before previewing.";
                return false;
            }

            List<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variantList =
                (variants ?? DecoLimitLifter.EsuSymmetry.Variants()).ToList();
            if (variantList.Count == 0)
                variantList.Add(new DecoLimitLifter.EsuSymmetry.SymmetryVariant(Array.Empty<DecorationEditAxis>()));

            var placements = new List<DecorationGeneratorPlacement>();
            var placementKeys = new HashSet<string>();
            var geometryKeys = new HashSet<string>();
            int plannedVariants = 0;
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in variantList)
            {
                DecorationGeneratorDraft variantDraft = variant.IsIdentity
                    ? draft
                    : draft.CreateMirroredForSymmetry(variant);
                if (!geometryKeys.Add(GeometryKey(variantDraft, settings)))
                    continue;

                if (!TryPlan(variantDraft, settings, anchorResolver, out DecorationGeneratorPlan variantPlan, out string variantMessage))
                {
                    message = variant.IsIdentity
                        ? variantMessage
                        : "Generator symmetry placement rejected: " + variantMessage;
                    return false;
                }

                plannedVariants++;
                foreach (DecorationGeneratorPlacement placement in variantPlan.Placements)
                {
                    if (placementKeys.Add(PlacementKey(placement)))
                        placements.Add(placement);
                }
            }

            if (placements.Count == 0)
            {
                message = "Generator draft produced no decorations.";
                return false;
            }

            plan = new DecorationGeneratorPlan(draft.Construct, placements, Array.Empty<string>());
            message = plannedVariants > 1
                ? "Generator symmetry: " +
                  placements.Count.ToString("N0", CultureInfo.InvariantCulture) +
                  " decoration(s) ready across " +
                  plannedVariants.ToString("N0", CultureInfo.InvariantCulture) +
                  " variant(s)."
                : placements.Count.ToString("N0", CultureInfo.InvariantCulture) + " generated decoration(s) ready.";
            return true;
        }

        private static List<GeneratorSegment> BuildSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (draft.Tool == SurfaceExtraTool.Circle)
                return BuildCircleSegments(draft, settings, out message);
            return BuildPathSegments(draft, out message);
        }

        private static List<GeneratorSegment> BuildPathSegments(
            DecorationGeneratorDraft draft,
            out string message)
        {
            message = null;
            if (draft.PathPoints.Count < 2)
            {
                message = "Place at least two path points before previewing.";
                return null;
            }

            var segments = new List<GeneratorSegment>(draft.PathPoints.Count - 1);
            for (int index = 0; index < draft.PathPoints.Count - 1; index++)
                segments.Add(new GeneratorSegment(draft.PathPoints[index], draft.PathPoints[index + 1]));
            return segments;
        }

        private static List<GeneratorSegment> BuildCircleSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a circle center before previewing.";
                return null;
            }

            Vector3 normal = draft.CircleNormal.sqrMagnitude > GeometryEpsilon
                ? draft.CircleNormal.normalized
                : Vector3.up;
            BuildCircleBasis(normal, out Vector3 tangentA, out Vector3 tangentB);
            var points = new Vector3[settings.CircleSegments];
            for (int index = 0; index < points.Length; index++)
            {
                float angle = Mathf.PI * 2f * index / points.Length;
                points[index] = draft.CircleCenter +
                                (Mathf.Cos(angle) * tangentA + Mathf.Sin(angle) * tangentB) *
                                settings.CircleRadius;
            }

            var segments = new List<GeneratorSegment>(points.Length);
            for (int index = 0; index < points.Length; index++)
                segments.Add(new GeneratorSegment(points[index], points[(index + 1) % points.Length]));
            return segments;
        }

        private static bool TryCreatePlacement(
            GeneratorSegment segment,
            int segmentIndex,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            List<DecorationGeneratorPlacement> placements,
            out string message)
        {
            message = null;
            if (!DecorationEditMath.IsFinite(segment.Start) ||
                !DecorationEditMath.IsFinite(segment.End))
            {
                message = "Generator segment " + (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) + " contains a non-finite point.";
                return false;
            }

            Vector3 delta = segment.End - segment.Start;
            float length = delta.magnitude;
            if (!DecorationEditMath.IsFinite(length) || length <= GeometryEpsilon)
            {
                message = "Generator segment " + (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) + " has zero length.";
                return false;
            }

            Vector3 center = (segment.Start + segment.End) * 0.5f;
            if (anchorResolver == null ||
                !anchorResolver.TryResolveAnchor(center, out Vector3i anchor))
            {
                message = "Generator segment " +
                          (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          " has no valid nearest anchor within +/-10m.";
                return false;
            }

            Vector3 positioning = RoundPlacementPosition(center - ToVector3(anchor));
            if (!DecorationEditMath.IsWithinPositionLimit(positioning))
            {
                message = "Generator segment " +
                          (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          " would exceed FTD's +/-10 positioning limit.";
                return false;
            }

            Vector3 scaling = ScaleForAxis(settings.LengthAxis, length, settings.Diameter);
            Vector3 orientation = EulerFromTo(
                DecorationEditMath.AxisVector(settings.LengthAxis),
                delta / length);

            placements.Add(new DecorationGeneratorPlacement(
                anchor,
                settings.MeshGuid,
                positioning,
                scaling,
                orientation,
                settings.ColorIndex,
                settings.MaterialReplacement));
            return true;
        }

        private static Vector3 ScaleForAxis(DecorationEditAxis axis, float length, float diameter)
        {
            switch (axis)
            {
                case DecorationEditAxis.Y:
                    return new Vector3(diameter, length, diameter);
                case DecorationEditAxis.Z:
                    return new Vector3(diameter, diameter, length);
                default:
                    return new Vector3(length, diameter, diameter);
            }
        }

        private static void BuildCircleBasis(Vector3 normal, out Vector3 tangentA, out Vector3 tangentB)
        {
            normal = normal.sqrMagnitude > GeometryEpsilon ? normal.normalized : Vector3.up;
            tangentA = Vector3.Cross(
                normal,
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f
                    ? Vector3.right
                    : Vector3.up);
            if (tangentA.sqrMagnitude <= GeometryEpsilon)
                tangentA = Vector3.Cross(normal, Vector3.forward);
            tangentA = tangentA.normalized;
            tangentB = Vector3.Cross(normal, tangentA).normalized;
        }

        private static Vector3 EulerFromTo(Vector3 from, Vector3 to)
        {
            from = from.sqrMagnitude > GeometryEpsilon ? from.normalized : Vector3.right;
            to = to.sqrMagnitude > GeometryEpsilon ? to.normalized : Vector3.right;
            float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            if (dot > 0.999999f)
                return Vector3.zero;

            Vector3 axis;
            float x;
            float y;
            float z;
            float w;
            if (dot < -0.999999f)
            {
                axis = Vector3.Cross(from, Vector3.right);
                if (axis.sqrMagnitude <= GeometryEpsilon)
                    axis = Vector3.Cross(from, Vector3.up);
                axis = axis.normalized;
                x = axis.x;
                y = axis.y;
                z = axis.z;
                w = 0f;
            }
            else
            {
                axis = Vector3.Cross(from, to);
                float s = Mathf.Sqrt((1f + dot) * 2f);
                float invS = 1f / s;
                x = axis.x * invS;
                y = axis.y * invS;
                z = axis.z * invS;
                w = s * 0.5f;
            }

            return QuaternionToEulerDegrees(x, y, z, w);
        }

        private static Vector3 QuaternionToEulerDegrees(float x, float y, float z, float w)
        {
            double sinrCosp = 2d * (w * x + y * z);
            double cosrCosp = 1d - 2d * (x * x + y * y);
            double roll = Math.Atan2(sinrCosp, cosrCosp);

            double sinp = 2d * (w * y - z * x);
            double pitch = Math.Abs(sinp) >= 1d
                ? (sinp >= 0d ? Math.PI / 2d : -Math.PI / 2d)
                : Math.Asin(sinp);

            double sinyCosp = 2d * (w * z + x * y);
            double cosyCosp = 1d - 2d * (y * y + z * z);
            double yaw = Math.Atan2(sinyCosp, cosyCosp);

            const double radiansToDegrees = 57.29577951308232d;
            return new Vector3(
                NormalizeDegrees((float)(roll * radiansToDegrees)),
                NormalizeDegrees((float)(pitch * radiansToDegrees)),
                NormalizeDegrees((float)(yaw * radiansToDegrees)));
        }

        private static float NormalizeDegrees(float degrees)
        {
            if (!DecorationEditMath.IsFinite(degrees))
                return 0f;
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        private static string GeometryKey(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings)
        {
            if (draft == null)
                return string.Empty;

            List<GeneratorSegment> segments = BuildSegments(draft, settings, out string _);
            if (segments == null)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            foreach (GeneratorSegment segment in segments)
            {
                builder.Append(VectorKey(segment.Start)).Append(">")
                    .Append(VectorKey(segment.End)).Append(";");
            }

            return builder.ToString();
        }

        private static string PlacementKey(DecorationGeneratorPlacement placement) =>
            placement.MeshGuid.ToString("N") + "|" +
            CellKey(placement.Anchor) + "|" +
            VectorKey(placement.Positioning) + "|" +
            VectorKey(placement.Scaling) + "|" +
            VectorKey(placement.Orientation);

        private static string CellKey(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + ":" +
            value.y.ToString(CultureInfo.InvariantCulture) + ":" +
            value.z.ToString(CultureInfo.InvariantCulture);

        private static string VectorKey(Vector3 value) =>
            FloatKey(value.x) + ":" + FloatKey(value.y) + ":" + FloatKey(value.z);

        private static string FloatKey(float value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

        private static Vector3 RoundPlacementPosition(Vector3 value) =>
            new Vector3(
                Round(value.x),
                Round(value.y),
                Round(value.z));

        private static float Round(float value) =>
            Mathf.Round(value * 10000f) / 10000f;

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);

        private readonly struct GeneratorSegment
        {
            internal GeneratorSegment(Vector3 start, Vector3 end)
            {
                Start = start;
                End = end;
            }

            internal Vector3 Start { get; }

            internal Vector3 End { get; }
        }
    }
}
