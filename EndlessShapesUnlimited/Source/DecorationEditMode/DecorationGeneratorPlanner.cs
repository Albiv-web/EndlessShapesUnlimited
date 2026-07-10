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
        Circle,
        PartialCircle,
        Sphere,
        PartialSphere,
        Cone,
        Frustum,
        Cone2D,
        Quad,
        Polygon,
        Tube
    }

    internal sealed class DecorationGeneratorSettings
    {
        internal static readonly Guid MetalPole1mGuid = new Guid("ad00935b-e95c-4345-8ea7-646846bc16db");

        internal DecorationGeneratorSettings()
        {
            MeshGuid = MetalPole1mGuid;
            LengthAxis = DecorationEditAxis.Z;
            Diameter = 0.05f;
            CircleRadius = 2f;
            CircleSegments = 24;
            ArcDegrees = 360f;
            ShapeHeight = 2f;
            TopRadius = 1f;
            RingCount = 6;
            QuadWidth = 4f;
            QuadHeight = 2f;
            PolygonSides = 6;
            TubeDiameter = 1f;
            TubeSegments = 8;
            ColorIndex = 0;
            NearestAnchor = true;
        }

        internal Guid MeshGuid { get; set; }

        internal DecorationEditAxis LengthAxis { get; set; }

        internal float Diameter { get; set; }

        internal float CircleRadius { get; set; }

        internal int CircleSegments { get; set; }

        internal float ArcDegrees { get; set; }

        internal float ShapeHeight { get; set; }

        internal float TopRadius { get; set; }

        internal int RingCount { get; set; }

        internal float QuadWidth { get; set; }

        internal float QuadHeight { get; set; }

        internal int PolygonSides { get; set; }

        internal float TubeDiameter { get; set; }

        internal int TubeSegments { get; set; }

        internal int ColorIndex { get; set; }

        internal bool NearestAnchor { get; set; }

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

            if (!DecorationEditMath.IsFinite(Diameter) || Diameter <= 0f ||
                !DecorationEditMath.IsFinite(Diameter * Diameter))
            {
                reason = "Generator diameter must be finite and greater than zero.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(CircleRadius) || CircleRadius <= 0f ||
                !DecorationEditMath.IsFinite(CircleRadius * CircleRadius))
            {
                reason = "Generator radius must be finite and greater than zero.";
                return false;
            }

            if (CircleSegments < 3 || CircleSegments > 256)
            {
                reason = "Circle segment count must be 3 through 256.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(ArcDegrees) || ArcDegrees <= 0f || ArcDegrees > 360f)
            {
                reason = "Generator arc must be greater than zero and no more than 360 degrees.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(ShapeHeight) || ShapeHeight <= 0f)
            {
                reason = "Generator height must be finite and greater than zero.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(TopRadius) || TopRadius < 0f)
            {
                reason = "Generator top radius must be finite and non-negative.";
                return false;
            }

            if (RingCount < 1 || RingCount > 64)
            {
                reason = "Generator ring count must be 1 through 64.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(QuadWidth) || QuadWidth <= 0f ||
                !DecorationEditMath.IsFinite(QuadWidth * QuadWidth) ||
                !DecorationEditMath.IsFinite(QuadHeight) || QuadHeight <= 0f ||
                !DecorationEditMath.IsFinite(QuadHeight * QuadHeight))
            {
                reason = "Generator quad width and height must be finite and greater than zero.";
                return false;
            }

            if (PolygonSides < 3 || PolygonSides > 12)
            {
                reason = "Generator polygon side count must be 3 through 12.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(TubeDiameter) || TubeDiameter <= 0f ||
                !DecorationEditMath.IsFinite(TubeDiameter * TubeDiameter))
            {
                reason = "Generator tube diameter must be finite and greater than zero.";
                return false;
            }

            if (TubeSegments < 3 || TubeSegments > 64)
            {
                reason = "Generator tube segment count must be 3 through 64.";
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

    internal sealed class DecorationGeneratorSettingsSnapshot
    {
        internal DecorationGeneratorSettingsSnapshot(DecorationGeneratorSettings settings)
        {
            if (settings == null)
                return;

            MeshGuid = settings.MeshGuid;
            LengthAxis = settings.LengthAxis;
            Diameter = settings.Diameter;
            CircleRadius = settings.CircleRadius;
            CircleSegments = settings.CircleSegments;
            ArcDegrees = settings.ArcDegrees;
            ShapeHeight = settings.ShapeHeight;
            TopRadius = settings.TopRadius;
            RingCount = settings.RingCount;
            QuadWidth = settings.QuadWidth;
            QuadHeight = settings.QuadHeight;
            PolygonSides = settings.PolygonSides;
            TubeDiameter = settings.TubeDiameter;
            TubeSegments = settings.TubeSegments;
            ColorIndex = settings.ColorIndex;
            NearestAnchor = settings.NearestAnchor;
            MaterialReplacement = settings.MaterialReplacement;
        }

        internal Guid MeshGuid { get; }

        internal DecorationEditAxis LengthAxis { get; }

        internal float Diameter { get; }

        internal float CircleRadius { get; }

        internal int CircleSegments { get; }

        internal float ArcDegrees { get; }

        internal float ShapeHeight { get; }

        internal float TopRadius { get; }

        internal int RingCount { get; }

        internal float QuadWidth { get; }

        internal float QuadHeight { get; }

        internal int PolygonSides { get; }

        internal float TubeDiameter { get; }

        internal int TubeSegments { get; }

        internal int ColorIndex { get; }

        internal bool NearestAnchor { get; }

        internal Guid MaterialReplacement { get; }

        internal void Restore(DecorationGeneratorSettings settings)
        {
            if (settings == null)
                return;

            settings.MeshGuid = MeshGuid;
            settings.LengthAxis = LengthAxis;
            settings.Diameter = Diameter;
            settings.CircleRadius = CircleRadius;
            settings.CircleSegments = CircleSegments;
            settings.ArcDegrees = ArcDegrees;
            settings.ShapeHeight = ShapeHeight;
            settings.TopRadius = TopRadius;
            settings.RingCount = RingCount;
            settings.QuadWidth = QuadWidth;
            settings.QuadHeight = QuadHeight;
            settings.PolygonSides = PolygonSides;
            settings.TubeDiameter = TubeDiameter;
            settings.TubeSegments = TubeSegments;
            settings.ColorIndex = ColorIndex;
            settings.NearestAnchor = NearestAnchor;
            settings.MaterialReplacement = MaterialReplacement;
        }

        internal bool SameAs(DecorationGeneratorSettingsSnapshot other) =>
            other != null &&
            MeshGuid == other.MeshGuid &&
            LengthAxis == other.LengthAxis &&
            SameFloat(Diameter, other.Diameter) &&
            SameFloat(CircleRadius, other.CircleRadius) &&
            CircleSegments == other.CircleSegments &&
            SameFloat(ArcDegrees, other.ArcDegrees) &&
            SameFloat(ShapeHeight, other.ShapeHeight) &&
            SameFloat(TopRadius, other.TopRadius) &&
            RingCount == other.RingCount &&
            SameFloat(QuadWidth, other.QuadWidth) &&
            SameFloat(QuadHeight, other.QuadHeight) &&
            PolygonSides == other.PolygonSides &&
            SameFloat(TubeDiameter, other.TubeDiameter) &&
            TubeSegments == other.TubeSegments &&
            ColorIndex == other.ColorIndex &&
            NearestAnchor == other.NearestAnchor &&
            MaterialReplacement == other.MaterialReplacement;

        private static bool SameFloat(float left, float right) =>
            Math.Abs(left - right) <= 0.0001f;
    }

    internal sealed class DecorationGeneratorEditSnapshot
    {
        internal DecorationGeneratorEditSnapshot(
            DecorationGeneratorDraftSnapshot draft,
            DecorationGeneratorSettingsSnapshot settings)
        {
            Draft = draft;
            Settings = settings;
        }

        internal DecorationGeneratorDraftSnapshot Draft { get; }

        internal DecorationGeneratorSettingsSnapshot Settings { get; }

        internal bool SameAs(DecorationGeneratorEditSnapshot other) =>
            other != null &&
            (Draft == null ? other.Draft == null : Draft.SameAs(other.Draft)) &&
            (Settings == null ? other.Settings == null : Settings.SameAs(other.Settings));
    }

    internal sealed class DecorationGeneratorDraftSnapshot
    {
        internal DecorationGeneratorDraftSnapshot(
            SurfaceExtraTool tool,
            AllConstruct construct,
            IReadOnlyList<Vector3> pathPoints,
            bool hasCircleCenter,
            Vector3 circleCenter,
            Vector3 circleNormal,
            Vector3 circleTangentA,
            Vector3 circleTangentB,
            bool hasSharedAnchor,
            Vector3i sharedAnchor,
            bool sharedAnchorSelected,
            int selectedPoint)
        {
            Tool = tool;
            Construct = construct;
            PathPoints = (pathPoints ?? Array.Empty<Vector3>()).ToArray();
            HasCircleCenter = hasCircleCenter;
            CircleCenter = circleCenter;
            CircleNormal = circleNormal;
            CircleTangentA = circleTangentA;
            CircleTangentB = circleTangentB;
            HasSharedAnchor = hasSharedAnchor;
            SharedAnchor = sharedAnchor;
            SharedAnchorSelected = sharedAnchorSelected;
            SelectedPoint = selectedPoint;
        }

        internal SurfaceExtraTool Tool { get; }

        internal AllConstruct Construct { get; }

        internal Vector3[] PathPoints { get; }

        internal bool HasCircleCenter { get; }

        internal Vector3 CircleCenter { get; }

        internal Vector3 CircleNormal { get; }

        internal Vector3 CircleTangentA { get; }

        internal Vector3 CircleTangentB { get; }

        internal bool HasSharedAnchor { get; }

        internal Vector3i SharedAnchor { get; }

        internal bool SharedAnchorSelected { get; }

        internal int SelectedPoint { get; }

        internal bool SameAs(DecorationGeneratorDraftSnapshot other)
        {
            if (other == null ||
                Tool != other.Tool ||
                !ReferenceEquals(Construct, other.Construct) ||
                HasCircleCenter != other.HasCircleCenter ||
                SelectedPoint != other.SelectedPoint ||
                !SameVector(CircleCenter, other.CircleCenter) ||
                !SameVector(CircleNormal, other.CircleNormal) ||
                !SameVector(CircleTangentA, other.CircleTangentA) ||
                !SameVector(CircleTangentB, other.CircleTangentB) ||
                HasSharedAnchor != other.HasSharedAnchor ||
                !SameCell(SharedAnchor, other.SharedAnchor) ||
                SharedAnchorSelected != other.SharedAnchorSelected ||
                PathPoints.Length != other.PathPoints.Length)
            {
                return false;
            }

            for (int index = 0; index < PathPoints.Length; index++)
            {
                if (!SameVector(PathPoints[index], other.PathPoints[index]))
                    return false;
            }

            return true;
        }

        private static bool SameVector(Vector3 left, Vector3 right) =>
            Math.Abs(left.x - right.x) <= 0.0001f &&
            Math.Abs(left.y - right.y) <= 0.0001f &&
            Math.Abs(left.z - right.z) <= 0.0001f;

        private static bool SameCell(Vector3i left, Vector3i right) =>
            left.x == right.x &&
            left.y == right.y &&
            left.z == right.z;
    }

    internal sealed class DecorationGeneratorDraft
    {
        private const float TypedCoordinateResolutionMetres = 0.001f;
        private const float TypedGeometryEpsilon = 0.000001f;
        private const float TypedEdgeLengthSquaredEpsilon =
            TypedGeometryEpsilon * TypedGeometryEpsilon;

        private readonly List<Vector3> _pathPoints = new List<Vector3>();

        internal SurfaceExtraTool Tool { get; private set; } = SurfaceExtraTool.Path;

        internal AllConstruct Construct { get; private set; }

        internal IReadOnlyList<Vector3> PathPoints => _pathPoints;

        internal bool HasCircleCenter { get; private set; }

        internal Vector3 CircleCenter { get; private set; }

        internal Vector3 CircleNormal { get; private set; } = Vector3.up;

        internal Vector3 CircleTangentA { get; private set; } = Vector3.right;

        internal Vector3 CircleTangentB { get; private set; } = Vector3.forward;

        internal bool HasSharedAnchor { get; private set; }

        internal Vector3i SharedAnchor { get; private set; }

        internal bool SharedAnchorSelected { get; private set; }

        internal int SelectedPoint { get; private set; } = -1;

        internal bool HasDraft =>
            _pathPoints.Count > 0 ||
            HasCircleCenter ||
            HasSharedAnchor;

        internal bool HasPlaceableGeometry =>
            UsesCenterTool
                ? HasCircleCenter
                : _pathPoints.Count >= 2;

        internal bool HasActiveSelection => SelectedPoint >= 0 || SharedAnchorSelected;

        internal int PointCount =>
            UsesCenter(Tool) && HasCircleCenter
                ? 1
                : _pathPoints.Count;

        internal bool UsesCenterTool => UsesCenter(Tool);

        internal static bool UsesCenter(SurfaceExtraTool tool) =>
            tool != SurfaceExtraTool.None &&
            tool != SurfaceExtraTool.Path &&
            tool != SurfaceExtraTool.Tube;

        internal static bool UsesPath(SurfaceExtraTool tool) =>
            tool == SurfaceExtraTool.Path ||
            tool == SurfaceExtraTool.Tube;

        internal void SetTool(SurfaceExtraTool tool)
        {
            if (tool == SurfaceExtraTool.None)
                tool = SurfaceExtraTool.Path;
            if (Tool == tool)
                return;
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
            CircleTangentA = Vector3.right;
            CircleTangentB = Vector3.forward;
            HasSharedAnchor = false;
            SharedAnchor = default;
            ClearSelection();
        }

        internal void ClearSelection() =>
            ClearSelectionFields();

        private void ClearSelectionFields()
        {
            SelectedPoint = -1;
            SharedAnchorSelected = false;
        }

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

            Tool = UsesPath(Tool) ? Tool : SurfaceExtraTool.Path;
            _pathPoints.Add(DecorationEditMath.Snap(local));
            SharedAnchorSelected = false;
            SelectedPoint = _pathPoints.Count - 1;
            message = _pathPoints.Count < 2
                ? "Path start point placed."
                : "Path point " + _pathPoints.Count.ToString(CultureInfo.InvariantCulture) + " placed.";
            return true;
        }

        internal bool TrySetSharedAnchor(
            AllConstruct construct,
            Vector3i anchor,
            out string message)
        {
            message = null;
            if (!TryAcceptConstruct(construct, out message))
                return false;

            HasSharedAnchor = true;
            SharedAnchor = anchor;
            ClearSelectionFields();
            SharedAnchorSelected = true;
            message = "Generator same anchor set to " + FormatCell(anchor) + ".";
            return true;
        }

        internal void ClearSharedAnchor()
        {
            HasSharedAnchor = false;
            SharedAnchor = default;
            SharedAnchorSelected = false;
        }

        internal bool TryMoveSharedAnchor(Vector3i anchor, out string message)
        {
            message = null;
            if (!HasSharedAnchor)
            {
                message = "Pick a shared generator anchor before moving it.";
                return false;
            }

            SharedAnchor = anchor;
            SharedAnchorSelected = true;
            return true;
        }

        internal void SelectSharedAnchor()
        {
            if (!HasSharedAnchor)
                return;

            ClearSelectionFields();
            SharedAnchorSelected = true;
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
                message = "Generator center must be finite.";
                return false;
            }

            Tool = UsesCenter(Tool) ? Tool : SurfaceExtraTool.Circle;
            CircleCenter = DecorationEditMath.Snap(local);
            SetCircleBasisFromNormal(normal);
            HasCircleCenter = true;
            SharedAnchorSelected = false;
            SelectedPoint = 0;
            message = "Generator center placed.";
            return true;
        }

        internal bool TrySetCircleCenterWithBasis(
            AllConstruct construct,
            Vector3 local,
            Vector3 tangentA,
            Vector3 tangentB,
            out string message)
        {
            message = null;
            if (!TryAcceptConstruct(construct, out message))
                return false;

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Generator center must be finite.";
                return false;
            }

            Tool = UsesCenter(Tool) ? Tool : SurfaceExtraTool.Circle;
            CircleCenter = DecorationEditMath.Snap(local);
            SetCircleBasis(tangentA, tangentB);
            HasCircleCenter = true;
            SharedAnchorSelected = false;
            SelectedPoint = 0;
            message = "Generator center placed.";
            return true;
        }

        internal bool TryMoveSelectedPoint(Vector3 local, Vector3 normal, out string message) =>
            TryMoveSelectedPoint(local, normal, DecorationEditMath.MoveSnapMetres, out message);

        internal bool TryMoveSelectedPoint(Vector3 local, Vector3 normal, float snap, out string message)
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

            if (UsesCenterTool)
            {
                if (!HasCircleCenter)
                {
                    message = "Place a generator center before moving it.";
                    return false;
                }

                CircleCenter = DecorationEditMath.Snap(local, snap);
                if (TryGetFiniteSquaredMagnitude(normal, out float normalSquared) &&
                    normalSquared > 0.0001f)
                    SetCircleBasisFromNormal(normal);
                return true;
            }

            if (SelectedPoint >= _pathPoints.Count)
            {
                message = "Selected path point is no longer available.";
                return false;
            }

            _pathPoints[SelectedPoint] = DecorationEditMath.Snap(local, snap);
            return true;
        }

        internal bool TrySetSelectedPointCoordinate(Vector3 local, out string message)
        {
            message = null;
            if (SelectedPoint < 0)
            {
                message = "Select a generator point before applying its coordinate.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Generator point coordinates must be finite.";
                return false;
            }

            Vector3 normalized = DecorationEditMath.Snap(local, TypedCoordinateResolutionMetres);
            if (!TryGetFiniteSquaredMagnitude(normalized, out float _))
            {
                message = "Generator point coordinates are outside the supported numeric range.";
                return false;
            }

            if (UsesCenterTool)
            {
                if (!HasCircleCenter || SelectedPoint != 0)
                {
                    message = "Selected generator center is no longer available.";
                    return false;
                }

                if (SameTypedCoordinate(CircleCenter, normalized))
                {
                    message = "Typed generator center already matches the selected point.";
                    return false;
                }

                CircleCenter = normalized;
                message = "Generator center coordinate applied.";
                return true;
            }

            if (SelectedPoint >= _pathPoints.Count)
            {
                message = "Selected path point is no longer available.";
                return false;
            }

            if (SelectedPoint > 0)
            {
                Vector3 previousDelta = _pathPoints[SelectedPoint - 1] - normalized;
                if (!TryGetFiniteSquaredMagnitude(previousDelta, out float previousSquared))
                {
                    message = "Generator path coordinate is outside the supported numeric range.";
                    return false;
                }

                if (previousSquared <= TypedEdgeLengthSquaredEpsilon)
                {
                    message = "Generator path coordinate would create a zero-length previous segment.";
                    return false;
                }
            }

            if (SelectedPoint + 1 < _pathPoints.Count)
            {
                Vector3 nextDelta = _pathPoints[SelectedPoint + 1] - normalized;
                if (!TryGetFiniteSquaredMagnitude(nextDelta, out float nextSquared))
                {
                    message = "Generator path coordinate is outside the supported numeric range.";
                    return false;
                }

                if (nextSquared <= TypedEdgeLengthSquaredEpsilon)
                {
                    message = "Generator path coordinate would create a zero-length next segment.";
                    return false;
                }
            }

            if (SameTypedCoordinate(_pathPoints[SelectedPoint], normalized))
            {
                message = "Typed generator coordinate already matches the selected path point.";
                return false;
            }

            _pathPoints[SelectedPoint] = normalized;
            message = "Generator path point coordinate applied.";
            return true;
        }

        private static bool TryGetFiniteSquaredMagnitude(Vector3 value, out float squaredMagnitude)
        {
            squaredMagnitude = 0f;
            if (!DecorationEditMath.IsFinite(value))
                return false;

            squaredMagnitude = value.sqrMagnitude;
            return DecorationEditMath.IsFinite(squaredMagnitude);
        }

        private static bool SameTypedCoordinate(Vector3 left, Vector3 right)
        {
            const float tolerance = TypedCoordinateResolutionMetres * 0.001f;
            return Math.Abs(left.x - right.x) <= tolerance &&
                   Math.Abs(left.y - right.y) <= tolerance &&
                   Math.Abs(left.z - right.z) <= tolerance;
        }

        internal bool TryDeleteSelection(out string message)
        {
            message = null;
            if (SharedAnchorSelected && HasSharedAnchor)
            {
                ClearSharedAnchor();
                message = "Generator shared anchor cleared.";
                return true;
            }

            if (SelectedPoint < 0)
            {
                message = "Select a generator point before deleting.";
                return false;
            }

            if (UsesCenterTool)
            {
                HasCircleCenter = false;
                CircleCenter = Vector3.zero;
                SelectedPoint = -1;
                message = "Generator center deleted.";
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
            SharedAnchorSelected = false;
            if (UsesCenterTool)
            {
                SelectedPoint = HasCircleCenter && index == 0 ? 0 : -1;
                return;
            }

            SelectedPoint = index >= 0 && index < _pathPoints.Count ? index : -1;
        }

        internal Vector3 PointAt(int index)
        {
            if (UsesCenterTool)
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
                Vector3 mirroredA = variant.Mirror(CircleCenter + CircleTangentA) - mirroredCenter;
                Vector3 mirroredB = variant.Mirror(CircleCenter + CircleTangentB) - mirroredCenter;
                mirrored.CircleCenter = mirroredCenter;
                mirrored.CircleNormal = NormalizeOrDefault(mirroredTip - mirroredCenter, CircleNormal);
                mirrored.SetCircleBasis(mirroredA, mirroredB);
                mirrored.HasCircleCenter = true;
            }

            if (HasSharedAnchor)
            {
                mirrored.SharedAnchor = variant.Mirror(SharedAnchor);
                mirrored.HasSharedAnchor = true;
                mirrored.SharedAnchorSelected = SharedAnchorSelected;
            }

            return mirrored;
        }

        internal DecorationGeneratorDraftSnapshot CreateSnapshot() =>
            new DecorationGeneratorDraftSnapshot(
                Tool,
                Construct,
                _pathPoints,
                HasCircleCenter,
                CircleCenter,
                CircleNormal,
                CircleTangentA,
                CircleTangentB,
                HasSharedAnchor,
                SharedAnchor,
                SharedAnchorSelected,
                SelectedPoint);

        internal void Restore(DecorationGeneratorDraftSnapshot snapshot)
        {
            Clear();
            if (snapshot == null)
                return;

            Tool = snapshot.Tool == SurfaceExtraTool.None ? SurfaceExtraTool.Path : snapshot.Tool;
            Construct = snapshot.Construct;
            _pathPoints.AddRange(snapshot.PathPoints ?? Array.Empty<Vector3>());
            HasCircleCenter = snapshot.HasCircleCenter;
            CircleCenter = snapshot.CircleCenter;
            if (UsesCenterTool && HasCircleCenter)
            {
                CircleNormal = NormalizeOrDefault(snapshot.CircleNormal, Vector3.up);
                SetCircleBasis(snapshot.CircleTangentA, snapshot.CircleTangentB);
            }
            else
            {
                // The basis is inactive in Path mode. Preserve it byte-for-byte so
                // undo/redo restores the complete draft snapshot without changing
                // the default right/forward/up state into a derived down normal.
                CircleNormal = snapshot.CircleNormal;
                CircleTangentA = snapshot.CircleTangentA;
                CircleTangentB = snapshot.CircleTangentB;
            }
            HasSharedAnchor = snapshot.HasSharedAnchor;
            SharedAnchor = snapshot.SharedAnchor;
            SelectPoint(snapshot.SelectedPoint);
            SharedAnchorSelected = snapshot.SharedAnchorSelected;
        }

        internal bool TryRotateCircle(DecorationEditAxis axis, float degrees, out string message)
        {
            message = null;
            if (!UsesCenterTool || !HasCircleCenter)
            {
                message = "Place a generator shape before rotating it.";
                return false;
            }

            if (axis != DecorationEditAxis.X &&
                axis != DecorationEditAxis.Y &&
                axis != DecorationEditAxis.Z)
            {
                message = "Generator shape rotation needs an X, Y, or Z axis.";
                return false;
            }

            Quaternion rotation = Quaternion.AngleAxis(degrees, DecorationEditMath.AxisVector(axis));
            SetCircleBasis(rotation * CircleTangentA, rotation * CircleTangentB);
            return true;
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

        private static string FormatCell(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + "," +
            value.y.ToString(CultureInfo.InvariantCulture) + "," +
            value.z.ToString(CultureInfo.InvariantCulture);

        private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
        {
            if (TryGetFiniteSquaredMagnitude(value, out float valueSquared) &&
                valueSquared > 0.0001f)
                return value.normalized;
            if (TryGetFiniteSquaredMagnitude(fallback, out float fallbackSquared) &&
                fallbackSquared > 0.0001f)
                return fallback.normalized;
            return Vector3.up;
        }

        private void SetCircleBasisFromNormal(Vector3 normal)
        {
            DecorationGeneratorPlanner.BuildCircleBasisForDraft(
                NormalizeOrDefault(normal, Vector3.up),
                out Vector3 tangentA,
                out Vector3 tangentB);
            SetCircleBasis(tangentA, tangentB);
        }

        private void SetCircleBasis(Vector3 tangentA, Vector3 tangentB)
        {
            tangentA = NormalizeOrDefault(tangentA, Vector3.right);
            Vector3 normal = Vector3.Cross(tangentA, tangentB);
            if (!TryGetFiniteSquaredMagnitude(normal, out float normalSquared) ||
                normalSquared <= 0.0001f)
            {
                normal = TryGetFiniteSquaredMagnitude(CircleNormal, out float circleNormalSquared) &&
                         circleNormalSquared > 0.0001f
                    ? CircleNormal
                    : Vector3.up;
                DecorationGeneratorPlanner.BuildCircleBasisForDraft(normal, out tangentA, out tangentB);
            }
            else
            {
                normal.Normalize();
                tangentB = Vector3.Cross(normal, tangentA);
                tangentB = NormalizeOrDefault(tangentB, Vector3.forward);
            }

            CircleTangentA = tangentA;
            CircleTangentB = tangentB;
            CircleNormal = NormalizeOrDefault(Vector3.Cross(CircleTangentA, CircleTangentB), normal);
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

    internal readonly struct DecorationGeneratorSegment
    {
        internal DecorationGeneratorSegment(Vector3 start, Vector3 end)
        {
            Start = start;
            End = end;
        }

        internal Vector3 Start { get; }

        internal Vector3 End { get; }
    }

    internal static class DecorationGeneratorPlanner
    {
        private const float GeometryEpsilon = 0.000001f;
        private const float GeometryLengthSquaredEpsilon = GeometryEpsilon * GeometryEpsilon;
        private const int MaximumGeneratedSegments = 100000;

        internal static bool TryPlan(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            out DecorationGeneratorPlan plan,
            out string message)
        {
            plan = null;
            if (!TryBuildValidatedSegmentBatch(
                    draft,
                    settings,
                    out ValidatedSegmentBatch batch,
                    out message))
            {
                return false;
            }

            var placements = new List<DecorationGeneratorPlacement>(batch.Count);
            var warnings = new List<string>();
            var anchorContext = new GeneratorAnchorContext(
                settings.NearestAnchor,
                anchorResolver,
                draft.HasSharedAnchor,
                draft.SharedAnchor);
            for (int index = 0; index < batch.Count; index++)
            {
                if (!TryCreatePlacement(
                        batch.Segments[index],
                        index,
                        settings,
                        anchorContext,
                        out DecorationGeneratorPlacement placement,
                        out message))
                {
                    plan = null;
                    return false;
                }

                placements.Add(placement);
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
            if (!TryBuildSymmetrySegmentBatches(
                    draft,
                    settings,
                    anchorResolver,
                    variants,
                    out ValidatedSegmentBatch _,
                    out List<SymmetrySegmentBatch> batches,
                    out int uniqueSegmentCount,
                    out message))
            {
                return false;
            }

            // The aggregate unique-segment cap is established before this list or
            // any DecorationGeneratorPlacement instance is allocated.
            var placements = new List<DecorationGeneratorPlacement>(uniqueSegmentCount);
            var placementKeys = new HashSet<PlacementValueKey>();
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                SymmetrySegmentBatch batch = batches[batchIndex];
                for (int segmentIndex = 0; segmentIndex < batch.Segments.Count; segmentIndex++)
                {
                    if (!TryCreatePlacement(
                            batch.Segments[segmentIndex],
                            segmentIndex,
                            settings,
                            batch.AnchorContext,
                            out DecorationGeneratorPlacement placement,
                            out string placementMessage))
                    {
                        message = batch.Variant.IsIdentity
                            ? placementMessage
                            : "Generator symmetry placement rejected: " + placementMessage;
                        return false;
                    }

                    if (placementKeys.Add(new PlacementValueKey(placement)))
                        placements.Add(placement);
                }
            }

            if (placements.Count == 0)
            {
                message = "Generator draft produced no decorations.";
                return false;
            }

            plan = new DecorationGeneratorPlan(draft.Construct, placements, Array.Empty<string>());
            message = batches.Count > 1
                ? "Generator symmetry: " +
                  placements.Count.ToString("N0", CultureInfo.InvariantCulture) +
                  " decoration(s) ready across " +
                  batches.Count.ToString("N0", CultureInfo.InvariantCulture) +
                  " variant(s)."
                : placements.Count.ToString("N0", CultureInfo.InvariantCulture) + " generated decoration(s) ready.";
            return true;
        }

        internal static bool TryBuildPreviewSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out IReadOnlyList<DecorationGeneratorSegment> segments,
            out string message)
        {
            segments = Array.Empty<DecorationGeneratorSegment>();
            if (!TryBuildValidatedSegmentBatch(
                    draft,
                    settings,
                    out ValidatedSegmentBatch batch,
                    out message))
            {
                return false;
            }

            segments = batch.Segments;
            return true;
        }

        internal static IReadOnlyList<DecorationGeneratorSegment> MirrorSegmentsForSymmetry(
            IReadOnlyList<DecorationGeneratorSegment> segments,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            if (segments == null || segments.Count == 0)
                return Array.Empty<DecorationGeneratorSegment>();
            if (variant.IsIdentity)
                return segments;

            var mirrored = new DecorationGeneratorSegment[segments.Count];
            for (int index = 0; index < segments.Count; index++)
                mirrored[index] = MirrorSegmentForSymmetry(segments[index], variant);
            return mirrored;
        }

        internal static bool TryBuildSymmetryPreviewSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            IEnumerable<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variants,
            out IReadOnlyList<DecorationGeneratorSegment> segments,
            out string message)
        {
            return TryBuildSymmetryPreviewSegments(
                draft,
                settings,
                anchorResolver,
                variants,
                out IReadOnlyList<DecorationGeneratorSegment> _,
                out segments,
                out message);
        }

        internal static bool TryBuildSymmetryPreviewSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            IEnumerable<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variants,
            out IReadOnlyList<DecorationGeneratorSegment> baseSegments,
            out IReadOnlyList<DecorationGeneratorSegment> segments,
            out string message)
        {
            baseSegments = Array.Empty<DecorationGeneratorSegment>();
            segments = Array.Empty<DecorationGeneratorSegment>();
            if (!TryBuildSymmetrySegmentBatches(
                    draft,
                    settings,
                    anchorResolver,
                    variants,
                    out ValidatedSegmentBatch baseBatch,
                    out List<SymmetrySegmentBatch> batches,
                    out int uniqueSegmentCount,
                    out message))
            {
                return false;
            }

            var flattened = new DecorationGeneratorSegment[uniqueSegmentCount];
            int targetIndex = 0;
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                IReadOnlyList<DecorationGeneratorSegment> batchSegments = batches[batchIndex].Segments;
                for (int segmentIndex = 0; segmentIndex < batchSegments.Count; segmentIndex++)
                    flattened[targetIndex++] = batchSegments[segmentIndex];
            }

            baseSegments = baseBatch.Segments;
            segments = flattened;
            message = uniqueSegmentCount.ToString("N0", CultureInfo.InvariantCulture) +
                      " generated preview segment(s) ready.";
            return true;
        }

        private static bool TryBuildSymmetrySegmentBatches(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            DecorationAnchorResolver anchorResolver,
            IEnumerable<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variants,
            out ValidatedSegmentBatch baseBatch,
            out List<SymmetrySegmentBatch> batches,
            out int uniqueSegmentCount,
            out string message)
        {
            baseBatch = null;
            batches = null;
            uniqueSegmentCount = 0;
            if (!TryBuildValidatedSegmentBatch(
                    draft,
                    settings,
                    out baseBatch,
                    out message))
            {
                return false;
            }

            List<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variantList =
                (variants ?? DecoLimitLifter.EsuSymmetry.Variants()).ToList();
            if (variantList.Count == 0)
            {
                variantList.Add(new DecoLimitLifter.EsuSymmetry.SymmetryVariant(
                    Array.Empty<DecorationEditAxis>()));
            }

            var result = new List<SymmetrySegmentBatch>(variantList.Count);
            var uniqueKeys = new HashSet<DirectedSegmentValueKey>();
            for (int variantIndex = 0; variantIndex < variantList.Count; variantIndex++)
            {
                DecoLimitLifter.EsuSymmetry.SymmetryVariant variant = variantList[variantIndex];
                bool hasExplicitSharedAnchor = draft.HasSharedAnchor;
                Vector3i explicitSharedAnchor = draft.SharedAnchor;
                if (hasExplicitSharedAnchor && !variant.IsIdentity)
                    explicitSharedAnchor = variant.Mirror(explicitSharedAnchor);

                var anchorContext = new GeneratorAnchorContext(
                    settings.NearestAnchor,
                    anchorResolver,
                    hasExplicitSharedAnchor,
                    explicitSharedAnchor);

                bool includeSharedAnchor = !settings.NearestAnchor;
                Vector3i sharedAnchor = default;
                if (includeSharedAnchor)
                {
                    DecorationGeneratorSegment firstSegment = MirrorSegmentForSymmetry(
                        baseBatch.Segments[0],
                        variant);
                    Vector3 firstCenter = (firstSegment.Start + firstSegment.End) * 0.5f;
                    if (!anchorContext.TryResolveAnchor(
                            firstCenter,
                            0,
                            out sharedAnchor,
                            out string anchorMessage))
                    {
                        message = variant.IsIdentity
                            ? anchorMessage
                            : "Generator symmetry placement rejected: " + anchorMessage;
                        return false;
                    }
                }

                var variantSegments = new List<DecorationGeneratorSegment>(baseBatch.Count);
                for (int segmentIndex = 0; segmentIndex < baseBatch.Count; segmentIndex++)
                {
                    DecorationGeneratorSegment segment = MirrorSegmentForSymmetry(
                        baseBatch.Segments[segmentIndex],
                        variant);
                    if (!TryValidateSegment(segment, draft.Tool, segmentIndex, out string validationMessage))
                    {
                        message = variant.IsIdentity
                            ? validationMessage
                            : "Generator symmetry geometry rejected: " + validationMessage;
                        return false;
                    }

                    var key = new DirectedSegmentValueKey(
                        segment,
                        includeSharedAnchor,
                        sharedAnchor);
                    if (!uniqueKeys.Add(key))
                        continue;

                    uniqueSegmentCount++;
                    if (uniqueSegmentCount > MaximumGeneratedSegments)
                    {
                        message = "Generator symmetry would generate more than " +
                                  MaximumGeneratedSegments.ToString("N0", CultureInfo.InvariantCulture) +
                                  " unique segments across all variants, above the 100,000-segment safety limit.";
                        return false;
                    }

                    variantSegments.Add(segment);
                }

                if (variantSegments.Count > 0)
                {
                    result.Add(new SymmetrySegmentBatch(
                        variant,
                        variantSegments,
                        anchorContext));
                }
            }

            if (result.Count == 0)
            {
                message = "Generator draft produced no unique segments.";
                return false;
            }

            batches = result;
            message = null;
            return true;
        }

        private static DecorationGeneratorSegment MirrorSegmentForSymmetry(
            DecorationGeneratorSegment segment,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant) =>
            variant.IsIdentity
                ? segment
                : new DecorationGeneratorSegment(
                    variant.Mirror(segment.Start),
                    variant.Mirror(segment.End));

        private static bool TryBuildValidatedSegmentBatch(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out ValidatedSegmentBatch batch,
            out string message)
        {
            batch = null;
            message = null;
            if (draft == null)
            {
                message = "Create a generator draft before previewing.";
                return false;
            }

            if (settings == null || !settings.IsValid(out message))
                return false;

            List<DecorationGeneratorSegment> segments;
            switch (draft.Tool)
            {
                case SurfaceExtraTool.Circle:
                case SurfaceExtraTool.PartialCircle:
                    segments = BuildCircleSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Sphere:
                case SurfaceExtraTool.PartialSphere:
                    segments = BuildSphereSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Cone:
                    segments = BuildConeSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Frustum:
                    segments = BuildFrustumSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Cone2D:
                    segments = BuildCone2DSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Quad:
                    segments = BuildQuadSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Polygon:
                    segments = BuildPolygonSegments(draft, settings, out message);
                    break;
                case SurfaceExtraTool.Tube:
                    segments = BuildTubeSegments(draft, settings, out message);
                    break;
                default:
                    segments = BuildPathSegments(draft, out message);
                    break;
            }

            if (segments == null)
                return false;
            if (segments.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(message))
                    message = "Generator draft produced no segments.";
                return false;
            }

            if (segments.Count > MaximumGeneratedSegments)
            {
                message = SegmentSafetyLimitMessage(
                    draft.Tool == SurfaceExtraTool.Tube ? "Tube" : "Generator",
                    segments.Count);
                return false;
            }

            for (int index = 0; index < segments.Count; index++)
            {
                if (!TryValidateSegment(segments[index], draft.Tool, index, out message))
                    return false;
            }

            batch = new ValidatedSegmentBatch(segments);
            return true;
        }

        private static List<DecorationGeneratorSegment> BuildPathSegments(
            DecorationGeneratorDraft draft,
            out string message)
        {
            message = null;
            if (draft.PathPoints.Count < 2)
            {
                message = "Place at least two path points before previewing.";
                return null;
            }

            var segments = new List<DecorationGeneratorSegment>(draft.PathPoints.Count - 1);
            for (int index = 0; index < draft.PathPoints.Count - 1; index++)
                segments.Add(new DecorationGeneratorSegment(draft.PathPoints[index], draft.PathPoints[index + 1]));
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildCircleSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a generator center before previewing.";
                return null;
            }

            bool closed = IsClosedArc(settings);
            List<Vector3> points = ArcPoints(
                draft.CircleCenter,
                BasisA(draft),
                BasisB(draft),
                settings.CircleRadius,
                settings,
                closed);
            var segments = new List<DecorationGeneratorSegment>(points.Count);
            AddPolylineSegments(segments, points, closed);
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildQuadSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a quad center before previewing.";
                return null;
            }

            Vector3 halfWidth = BasisA(draft) * (settings.QuadWidth * 0.5f);
            Vector3 halfHeight = BasisB(draft) * (settings.QuadHeight * 0.5f);
            var corners = new List<Vector3>(4)
            {
                draft.CircleCenter - halfWidth - halfHeight,
                draft.CircleCenter + halfWidth - halfHeight,
                draft.CircleCenter + halfWidth + halfHeight,
                draft.CircleCenter - halfWidth + halfHeight
            };
            var segments = new List<DecorationGeneratorSegment>(4);
            AddRequiredPolylineSegments(segments, corners, closed: true);
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildPolygonSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a polygon center before previewing.";
                return null;
            }

            int sides = Mathf.Clamp(settings.PolygonSides, 3, 12);
            Vector3 tangentA = BasisA(draft);
            Vector3 tangentB = BasisB(draft);
            var points = new List<Vector3>(sides);
            for (int index = 0; index < sides; index++)
            {
                float angle = index * Mathf.PI * 2f / sides;
                points.Add(
                    draft.CircleCenter +
                    (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                    settings.CircleRadius);
            }

            var segments = new List<DecorationGeneratorSegment>(sides);
            AddRequiredPolylineSegments(segments, points, closed: true);
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildTubeSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (draft.PathPoints.Count < 2)
            {
                message = "Place at least two tube path points before previewing.";
                return null;
            }

            if (!TryValidateTubePath(draft.PathPoints, out message))
                return null;

            int sides = Mathf.Clamp(settings.TubeSegments, 3, 64);
            float radius = settings.TubeDiameter * 0.5f;
            long requestedSegments =
                (long)draft.PathPoints.Count * sides +
                (long)(draft.PathPoints.Count - 1) * sides;
            if (requestedSegments > MaximumGeneratedSegments)
            {
                message = SegmentSafetyLimitMessage("Tube", requestedSegments);
                return null;
            }

            var segments = new List<DecorationGeneratorSegment>((int)requestedSegments);
            Vector3[] previousRing = null;
            Vector3 previousRadial = Vector3.zero;
            for (int pointIndex = 0; pointIndex < draft.PathPoints.Count; pointIndex++)
            {
                if (!TryGetTubePathTangent(draft.PathPoints, pointIndex, out Vector3 tangent))
                {
                    message = "Tube path point " +
                              (pointIndex + 1).ToString(CultureInfo.InvariantCulture) +
                              " has a zero-length adjacent segment.";
                    return null;
                }

                Vector3 radialA;
                Vector3 radialB;
                if (pointIndex == 0 ||
                    !TryProjectTubeRadial(previousRadial, tangent, out radialA))
                {
                    BuildCircleBasis(tangent, out radialA, out radialB);
                }
                else
                {
                    radialB = NormalizeOrFallback(
                        Vector3.Cross(tangent, radialA),
                        Vector3.forward);
                }

                if (pointIndex > 0 && Vector3.Dot(radialA, previousRadial) < 0f)
                {
                    radialA = -radialA;
                    radialB = -radialB;
                }

                var ring = new Vector3[sides];
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    ring[side] = draft.PathPoints[pointIndex] +
                                 (radialA * Mathf.Cos(angle) + radialB * Mathf.Sin(angle)) *
                                 radius;
                }

                AddRequiredPolylineSegments(segments, ring, closed: true);
                if (previousRing != null)
                {
                    for (int side = 0; side < sides; side++)
                    {
                        segments.Add(new DecorationGeneratorSegment(
                            previousRing[side],
                            ring[side]));
                    }
                }

                previousRing = ring;
                previousRadial = radialA;
            }

            return segments;
        }

        private static bool TryValidateTubePath(
            IReadOnlyList<Vector3> points,
            out string message)
        {
            message = null;
            Vector3 previousDirection = Vector3.zero;
            for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
            {
                Vector3 delta = points[segmentIndex + 1] - points[segmentIndex];
                if (!TryGetFiniteSquaredMagnitude(delta, out float lengthSquared))
                {
                    message = "Tube path edge after point " +
                              (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                              " is outside the supported numeric range.";
                    return false;
                }

                if (lengthSquared <= GeometryLengthSquaredEpsilon)
                {
                    message = "Tube path point " +
                              (segmentIndex + 2).ToString(CultureInfo.InvariantCulture) +
                              " creates an edge no longer than 0.000001 metres.";
                    return false;
                }

                Vector3 direction = delta / Mathf.Sqrt(lengthSquared);
                if (segmentIndex > 0)
                {
                    float directionDot = Vector3.Dot(previousDirection, direction);
                    if (!DecorationEditMath.IsFinite(directionDot) || directionDot <= -0.999f)
                    {
                        message = "Tube path point " +
                                  (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                                  " reverses direction too sharply; consecutive path directions must have a dot product greater than -0.999.";
                        return false;
                    }
                }

                previousDirection = direction;
            }

            return true;
        }

        private static bool TryGetTubePathTangent(
            IReadOnlyList<Vector3> points,
            int index,
            out Vector3 tangent)
        {
            tangent = Vector3.zero;
            if (points == null || points.Count < 2 || index < 0 || index >= points.Count)
                return false;

            if (index == 0)
            {
                tangent = points[1] - points[0];
            }
            else if (index == points.Count - 1)
            {
                tangent = points[index] - points[index - 1];
            }
            else
            {
                Vector3 incoming = points[index] - points[index - 1];
                Vector3 outgoing = points[index + 1] - points[index];
                if (!TryGetFiniteSquaredMagnitude(incoming, out float incomingSquared) ||
                    incomingSquared <= GeometryLengthSquaredEpsilon ||
                    !TryGetFiniteSquaredMagnitude(outgoing, out float outgoingSquared) ||
                    outgoingSquared <= GeometryLengthSquaredEpsilon)
                {
                    return false;
                }

                tangent = incoming / Mathf.Sqrt(incomingSquared) +
                          outgoing / Mathf.Sqrt(outgoingSquared);
                if (!TryGetFiniteSquaredMagnitude(tangent, out float joinedSquared) ||
                    joinedSquared <= GeometryLengthSquaredEpsilon)
                {
                    tangent = outgoing;
                }
            }

            if (!TryGetFiniteSquaredMagnitude(tangent, out float tangentSquared) ||
                tangentSquared <= GeometryLengthSquaredEpsilon)
            {
                return false;
            }

            tangent /= Mathf.Sqrt(tangentSquared);
            return DecorationEditMath.IsFinite(tangent);
        }

        private static bool TryProjectTubeRadial(
            Vector3 previousRadial,
            Vector3 tangent,
            out Vector3 projected)
        {
            projected = previousRadial - tangent * Vector3.Dot(previousRadial, tangent);
            if (!TryGetFiniteSquaredMagnitude(projected, out float squared) ||
                squared <= GeometryLengthSquaredEpsilon)
            {
                projected = Vector3.zero;
                return false;
            }

            projected /= Mathf.Sqrt(squared);
            return DecorationEditMath.IsFinite(projected);
        }

        private static List<DecorationGeneratorSegment> BuildSphereSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a sphere center before previewing.";
                return null;
            }

            bool closed = IsClosedArc(settings);
            int rings = Mathf.Clamp(settings.RingCount, 1, 64);
            Vector3 normal = BasisNormal(draft);
            Vector3 tangentA = BasisA(draft);
            Vector3 tangentB = BasisB(draft);
            var segments = new List<DecorationGeneratorSegment>();

            var latitudes = new List<float>(rings + 2);
            latitudes.Add(-Mathf.PI * 0.5f);
            for (int ring = 1; ring <= rings; ring++)
                latitudes.Add(-Mathf.PI * 0.5f + Mathf.PI * ring / (rings + 1));
            latitudes.Add(Mathf.PI * 0.5f);

            for (int ring = 1; ring < latitudes.Count - 1; ring++)
            {
                float latitude = latitudes[ring];
                float ringRadius = settings.CircleRadius * Mathf.Cos(latitude);
                Vector3 ringCenter = draft.CircleCenter + normal * (settings.CircleRadius * Mathf.Sin(latitude));
                List<Vector3> points = ArcPoints(ringCenter, tangentA, tangentB, ringRadius, settings, closed);
                AddPolylineSegments(segments, points, closed);
            }

            int meridians = closed
                ? Mathf.Clamp(settings.CircleSegments, 4, 64)
                : Mathf.Clamp(settings.CircleSegments + 1, 2, 65);
            for (int longitudeIndex = 0; longitudeIndex < meridians; longitudeIndex++)
            {
                float t = closed
                    ? longitudeIndex / (float)meridians
                    : longitudeIndex / (float)(meridians - 1);
                float angle = ArcRadians(settings) * t;
                Vector3 radial = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                Vector3 previous = draft.CircleCenter + normal * (-settings.CircleRadius);
                for (int latitudeIndex = 1; latitudeIndex < latitudes.Count; latitudeIndex++)
                {
                    float latitude = latitudes[latitudeIndex];
                    Vector3 current = draft.CircleCenter +
                                      normal * (settings.CircleRadius * Mathf.Sin(latitude)) +
                                      radial * (settings.CircleRadius * Mathf.Cos(latitude));
                    AddSegmentIfValid(segments, previous, current);
                    previous = current;
                }
            }

            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildConeSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a cone base center before previewing.";
                return null;
            }

            bool closed = IsClosedArc(settings);
            Vector3 tangentA = BasisA(draft);
            Vector3 tangentB = BasisB(draft);
            Vector3 apex = draft.CircleCenter + BasisNormal(draft) * settings.ShapeHeight;
            List<Vector3> basePoints = ArcPoints(draft.CircleCenter, tangentA, tangentB, settings.CircleRadius, settings, closed);
            var segments = new List<DecorationGeneratorSegment>();
            AddPolylineSegments(segments, basePoints, closed);
            foreach (Vector3 point in basePoints)
                AddSegmentIfValid(segments, point, apex);
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildFrustumSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a frustum base center before previewing.";
                return null;
            }

            bool closed = IsClosedArc(settings);
            Vector3 normal = BasisNormal(draft);
            Vector3 tangentA = BasisA(draft);
            Vector3 tangentB = BasisB(draft);
            Vector3 topCenter = draft.CircleCenter + normal * settings.ShapeHeight;
            List<Vector3> bottom = ArcPoints(draft.CircleCenter, tangentA, tangentB, settings.CircleRadius, settings, closed);
            List<Vector3> top = ArcPoints(topCenter, tangentA, tangentB, settings.TopRadius, settings, closed);
            var segments = new List<DecorationGeneratorSegment>();
            AddPolylineSegments(segments, bottom, closed);
            if (settings.TopRadius > GeometryEpsilon)
                AddPolylineSegments(segments, top, closed);
            for (int index = 0; index < bottom.Count && index < top.Count; index++)
                AddSegmentIfValid(segments, bottom[index], top[index]);
            return segments;
        }

        private static List<DecorationGeneratorSegment> BuildCone2DSegments(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            out string message)
        {
            message = null;
            if (!draft.HasCircleCenter)
            {
                message = "Place a 2D cone center before previewing.";
                return null;
            }

            bool closed = IsClosedArc(settings);
            List<Vector3> arc = ArcPoints(
                draft.CircleCenter,
                BasisA(draft),
                BasisB(draft),
                settings.CircleRadius,
                settings,
                closed);
            var segments = new List<DecorationGeneratorSegment>();
            AddPolylineSegments(segments, arc, closed);
            foreach (Vector3 point in arc)
                AddSegmentIfValid(segments, draft.CircleCenter, point);
            return segments;
        }

        private static List<Vector3> ArcPoints(
            Vector3 center,
            Vector3 tangentA,
            Vector3 tangentB,
            float radius,
            DecorationGeneratorSettings settings,
            bool closed)
        {
            int segments = Mathf.Clamp(settings.CircleSegments, 3, 256);
            int count = closed ? segments : segments + 1;
            float radians = ArcRadians(settings);
            var points = new List<Vector3>(count);
            for (int index = 0; index < count; index++)
            {
                float t = closed
                    ? index / (float)segments
                    : index / (float)(count - 1);
                float angle = radians * t;
                points.Add(center + (Mathf.Cos(angle) * tangentA + Mathf.Sin(angle) * tangentB) * radius);
            }
            return points;
        }

        private static void AddPolylineSegments(
            List<DecorationGeneratorSegment> segments,
            IReadOnlyList<Vector3> points,
            bool closed)
        {
            if (segments == null || points == null || points.Count < 2)
                return;

            for (int index = 0; index < points.Count - 1; index++)
                AddSegmentIfValid(segments, points[index], points[index + 1]);
            if (closed)
                AddSegmentIfValid(segments, points[points.Count - 1], points[0]);
        }

        private static void AddRequiredPolylineSegments(
            List<DecorationGeneratorSegment> segments,
            IReadOnlyList<Vector3> points,
            bool closed)
        {
            if (segments == null || points == null || points.Count < 2)
                return;

            for (int index = 0; index < points.Count - 1; index++)
                segments.Add(new DecorationGeneratorSegment(points[index], points[index + 1]));
            if (closed)
            {
                segments.Add(new DecorationGeneratorSegment(
                    points[points.Count - 1],
                    points[0]));
            }
        }

        private static void AddSegmentIfValid(
            List<DecorationGeneratorSegment> segments,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            if (TryGetFiniteSquaredMagnitude(delta, out float lengthSquared) &&
                lengthSquared <= GeometryLengthSquaredEpsilon)
                return;
            segments.Add(new DecorationGeneratorSegment(start, end));
        }

        private static bool IsClosedArc(DecorationGeneratorSettings settings) =>
            settings == null || settings.ArcDegrees >= 359.999f;

        private static float ArcRadians(DecorationGeneratorSettings settings) =>
            Mathf.Clamp(settings?.ArcDegrees ?? 360f, 0.001f, 360f) * Mathf.Deg2Rad;

        private static Vector3 BasisA(DecorationGeneratorDraft draft) =>
            NormalizeOrFallback(draft.CircleTangentA, Vector3.right);

        private static Vector3 BasisB(DecorationGeneratorDraft draft) =>
            NormalizeOrFallback(draft.CircleTangentB, Vector3.forward);

        private static Vector3 BasisNormal(DecorationGeneratorDraft draft) =>
            NormalizeOrFallback(draft.CircleNormal, Vector3.up);

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback) =>
            TryGetFiniteSquaredMagnitude(value, out float squaredMagnitude) &&
            squaredMagnitude > GeometryLengthSquaredEpsilon
                ? value.normalized
                : fallback;

        private static bool TryCreatePlacement(
            DecorationGeneratorSegment segment,
            int segmentIndex,
            DecorationGeneratorSettings settings,
            GeneratorAnchorContext anchorContext,
            out DecorationGeneratorPlacement placement,
            out string message)
        {
            placement = null;
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
            if (anchorContext == null ||
                !anchorContext.TryResolveAnchor(center, segmentIndex, out Vector3i anchor, out message))
            {
                return false;
            }

            Vector3 positioning = RoundPlacementPosition(center - ToVector3(anchor));
            if (!DecorationEditMath.IsWithinPositionLimit(positioning))
            {
                message = anchorContext.NearestAnchor
                    ? "Generator segment " +
                      (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                      " would exceed FTD's +/-10 positioning limit."
                    : "Generator same-anchor mode would exceed FTD's +/-10 positioning limit on segment " +
                      (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                      ".";
                return false;
            }

            Vector3 scaling = ScaleForAxis(settings.LengthAxis, length, settings.Diameter);
            Vector3 direction = delta / length;
            Vector3 orientation = settings.LengthAxis == DecorationEditAxis.Z
                ? LookRotationEuler(direction, Vector3.up)
                : EulerFromTo(
                    DecorationEditMath.AxisVector(settings.LengthAxis),
                    direction);

            placement = new DecorationGeneratorPlacement(
                anchor,
                settings.MeshGuid,
                positioning,
                scaling,
                orientation,
                settings.ColorIndex,
                settings.MaterialReplacement);
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

        internal static void BuildCircleBasisForDraft(Vector3 normal, out Vector3 tangentA, out Vector3 tangentB) =>
            BuildCircleBasis(normal, out tangentA, out tangentB);

        private static void BuildCircleBasis(Vector3 normal, out Vector3 tangentA, out Vector3 tangentB)
        {
            normal = NormalizeOrFallback(normal, Vector3.up);
            tangentA = Vector3.Cross(
                normal,
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f
                    ? Vector3.right
                    : Vector3.up);
            if (!TryGetFiniteSquaredMagnitude(tangentA, out float tangentSquared) ||
                tangentSquared <= GeometryLengthSquaredEpsilon)
                tangentA = Vector3.Cross(normal, Vector3.forward);
            tangentA = NormalizeOrFallback(tangentA, Vector3.right);
            tangentB = NormalizeOrFallback(Vector3.Cross(normal, tangentA), Vector3.forward);
        }

        private static Vector3 LookRotationEuler(Vector3 forward, Vector3 upwards)
        {
            if (!TryGetFiniteSquaredMagnitude(forward, out float forwardSquared) ||
                forwardSquared <= GeometryLengthSquaredEpsilon)
                forward = Vector3.forward;
            if (!TryGetFiniteSquaredMagnitude(upwards, out float upwardsSquared) ||
                upwardsSquared <= GeometryLengthSquaredEpsilon)
                upwards = Vector3.up;

            try
            {
                return Quaternion.LookRotation(forward.normalized, upwards.normalized).eulerAngles;
            }
            catch
            {
                return EulerFromTo(Vector3.forward, forward.normalized);
            }
        }

        private static Vector3 EulerFromTo(Vector3 from, Vector3 to)
        {
            from = NormalizeOrFallback(from, Vector3.right);
            to = NormalizeOrFallback(to, Vector3.right);
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
                if (!TryGetFiniteSquaredMagnitude(axis, out float axisSquared) ||
                    axisSquared <= GeometryLengthSquaredEpsilon)
                    axis = Vector3.Cross(from, Vector3.up);
                axis = NormalizeOrFallback(axis, Vector3.up);
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

        private static bool TryValidateSegment(
            DecorationGeneratorSegment segment,
            SurfaceExtraTool tool,
            int segmentIndex,
            out string message)
        {
            message = null;
            if (!TryGetFiniteSquaredMagnitude(segment.Start, out float _) ||
                !TryGetFiniteSquaredMagnitude(segment.End, out float _))
            {
                message = "Generator segment " +
                          (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          " contains a point outside the supported numeric range.";
                return false;
            }

            Vector3 delta = segment.End - segment.Start;
            if (!TryGetFiniteSquaredMagnitude(delta, out float lengthSquared))
            {
                message = "Generator segment " +
                          (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          " is outside the supported numeric range.";
                return false;
            }

            if (lengthSquared <= GeometryLengthSquaredEpsilon)
            {
                message = RequiredEdgeFailureMessage(tool, segmentIndex);
                return false;
            }

            return true;
        }

        private static string RequiredEdgeFailureMessage(
            SurfaceExtraTool tool,
            int segmentIndex)
        {
            string shape = null;
            switch (tool)
            {
                case SurfaceExtraTool.Quad:
                    shape = "Quad";
                    break;
                case SurfaceExtraTool.Polygon:
                    shape = "Polygon";
                    break;
                case SurfaceExtraTool.Tube:
                    shape = "Tube";
                    break;
            }

            string index = (segmentIndex + 1).ToString(CultureInfo.InvariantCulture);
            return shape == null
                ? "Generator segment " + index + " has zero length."
                : shape + " edge " + index + " must be longer than 0.000001 metres.";
        }

        private static string SegmentSafetyLimitMessage(string source, long segmentCount) =>
            source + " would generate " +
            segmentCount.ToString("N0", CultureInfo.InvariantCulture) +
            " segments, above the 100,000-segment safety limit.";

        private sealed class ValidatedSegmentBatch
        {
            internal ValidatedSegmentBatch(IReadOnlyList<DecorationGeneratorSegment> segments)
            {
                Segments = segments ?? Array.Empty<DecorationGeneratorSegment>();
            }

            internal IReadOnlyList<DecorationGeneratorSegment> Segments { get; }

            internal int Count => Segments.Count;
        }

        private sealed class SymmetrySegmentBatch
        {
            internal SymmetrySegmentBatch(
                DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
                IReadOnlyList<DecorationGeneratorSegment> segments,
                GeneratorAnchorContext anchorContext)
            {
                Variant = variant;
                Segments = segments ?? Array.Empty<DecorationGeneratorSegment>();
                AnchorContext = anchorContext;
            }

            internal DecoLimitLifter.EsuSymmetry.SymmetryVariant Variant { get; }

            internal IReadOnlyList<DecorationGeneratorSegment> Segments { get; }

            internal GeneratorAnchorContext AnchorContext { get; }
        }

        private readonly struct QuantizedVectorValue : IEquatable<QuantizedVectorValue>
        {
            private readonly float _x;
            private readonly float _y;
            private readonly float _z;

            internal QuantizedVectorValue(Vector3 value)
            {
                _x = Round(value.x);
                _y = Round(value.y);
                _z = Round(value.z);
            }

            public bool Equals(QuantizedVectorValue other) =>
                _x == other._x &&
                _y == other._y &&
                _z == other._z;

            public override bool Equals(object obj) =>
                obj is QuantizedVectorValue other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x.GetHashCode();
                    hash = hash * 397 ^ _y.GetHashCode();
                    hash = hash * 397 ^ _z.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct DirectedSegmentValueKey : IEquatable<DirectedSegmentValueKey>
        {
            private readonly QuantizedVectorValue _start;
            private readonly QuantizedVectorValue _end;
            private readonly bool _hasSharedAnchor;
            private readonly int _anchorX;
            private readonly int _anchorY;
            private readonly int _anchorZ;

            internal DirectedSegmentValueKey(
                DecorationGeneratorSegment segment,
                bool hasSharedAnchor,
                Vector3i sharedAnchor)
            {
                _start = new QuantizedVectorValue(segment.Start);
                _end = new QuantizedVectorValue(segment.End);
                _hasSharedAnchor = hasSharedAnchor;
                _anchorX = hasSharedAnchor ? sharedAnchor.x : 0;
                _anchorY = hasSharedAnchor ? sharedAnchor.y : 0;
                _anchorZ = hasSharedAnchor ? sharedAnchor.z : 0;
            }

            public bool Equals(DirectedSegmentValueKey other) =>
                _start.Equals(other._start) &&
                _end.Equals(other._end) &&
                _hasSharedAnchor == other._hasSharedAnchor &&
                _anchorX == other._anchorX &&
                _anchorY == other._anchorY &&
                _anchorZ == other._anchorZ;

            public override bool Equals(object obj) =>
                obj is DirectedSegmentValueKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _start.GetHashCode();
                    hash = hash * 397 ^ _end.GetHashCode();
                    hash = hash * 397 ^ _hasSharedAnchor.GetHashCode();
                    hash = hash * 397 ^ _anchorX;
                    hash = hash * 397 ^ _anchorY;
                    hash = hash * 397 ^ _anchorZ;
                    return hash;
                }
            }
        }

        private readonly struct PlacementValueKey : IEquatable<PlacementValueKey>
        {
            private readonly Guid _meshGuid;
            private readonly int _anchorX;
            private readonly int _anchorY;
            private readonly int _anchorZ;
            private readonly QuantizedVectorValue _positioning;
            private readonly QuantizedVectorValue _scaling;
            private readonly QuantizedVectorValue _orientation;

            internal PlacementValueKey(DecorationGeneratorPlacement placement)
            {
                _meshGuid = placement.MeshGuid;
                _anchorX = placement.Anchor.x;
                _anchorY = placement.Anchor.y;
                _anchorZ = placement.Anchor.z;
                _positioning = new QuantizedVectorValue(placement.Positioning);
                _scaling = new QuantizedVectorValue(placement.Scaling);
                _orientation = new QuantizedVectorValue(placement.Orientation);
            }

            public bool Equals(PlacementValueKey other) =>
                _meshGuid.Equals(other._meshGuid) &&
                _anchorX == other._anchorX &&
                _anchorY == other._anchorY &&
                _anchorZ == other._anchorZ &&
                _positioning.Equals(other._positioning) &&
                _scaling.Equals(other._scaling) &&
                _orientation.Equals(other._orientation);

            public override bool Equals(object obj) =>
                obj is PlacementValueKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _meshGuid.GetHashCode();
                    hash = hash * 397 ^ _anchorX;
                    hash = hash * 397 ^ _anchorY;
                    hash = hash * 397 ^ _anchorZ;
                    hash = hash * 397 ^ _positioning.GetHashCode();
                    hash = hash * 397 ^ _scaling.GetHashCode();
                    hash = hash * 397 ^ _orientation.GetHashCode();
                    return hash;
                }
            }
        }

        private static bool TryGetFiniteSquaredMagnitude(Vector3 value, out float squaredMagnitude)
        {
            squaredMagnitude = 0f;
            if (!DecorationEditMath.IsFinite(value))
                return false;

            squaredMagnitude = value.sqrMagnitude;
            return DecorationEditMath.IsFinite(squaredMagnitude);
        }

        private static Vector3 RoundPlacementPosition(Vector3 value) =>
            new Vector3(
                Round(value.x),
                Round(value.y),
                Round(value.z));

        private static float Round(float value) =>
            Mathf.Round(value * 10000f) / 10000f;

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);

        private sealed class GeneratorAnchorContext
        {
            private readonly DecorationAnchorResolver _resolver;
            private readonly bool _hasExplicitSharedAnchor;
            private readonly Vector3i _explicitSharedAnchor;
            private bool _hasSharedAnchor;
            private Vector3i _sharedAnchor;

            internal GeneratorAnchorContext(
                bool nearestAnchor,
                DecorationAnchorResolver resolver,
                bool hasExplicitSharedAnchor,
                Vector3i explicitSharedAnchor)
            {
                NearestAnchor = nearestAnchor;
                _resolver = resolver;
                _hasExplicitSharedAnchor = hasExplicitSharedAnchor;
                _explicitSharedAnchor = explicitSharedAnchor;
            }

            internal bool NearestAnchor { get; }

            internal bool TryResolveAnchor(
                Vector3 center,
                int segmentIndex,
                out Vector3i anchor,
                out string message)
            {
                anchor = new Vector3i(0, 0, 0);
                message = null;
                if (_resolver == null)
                {
                    message = "Generator anchor resolver is unavailable.";
                    return false;
                }

                if (NearestAnchor)
                {
                    if (_resolver.TryResolveAnchor(center, out anchor))
                        return true;

                    message = "Generator segment " +
                              (segmentIndex + 1).ToString(CultureInfo.InvariantCulture) +
                              " has no valid nearest anchor within +/-10m.";
                    return false;
                }

                if (!_hasSharedAnchor)
                {
                    if (_hasExplicitSharedAnchor)
                    {
                        if (!TryValidateExplicitSharedAnchor(out message))
                            return false;

                        _sharedAnchor = _explicitSharedAnchor;
                    }
                    else if (!_resolver.TryResolveAnchor(center, out _sharedAnchor))
                    {
                        message = "Generator same-anchor mode found no valid anchor within +/-10m.";
                        return false;
                    }

                    _hasSharedAnchor = true;
                }

                anchor = _sharedAnchor;
                return true;
            }

            private bool TryValidateExplicitSharedAnchor(out string message)
            {
                message = null;
                if (_resolver.TryResolveAnchor(ToVector3(_explicitSharedAnchor), out Vector3i resolved) &&
                    SameCell(resolved, _explicitSharedAnchor))
                {
                    return true;
                }

                message = "Generator shared anchor " + CellLabel(_explicitSharedAnchor) + " is not a valid craft block.";
                return false;
            }

            private static bool SameCell(Vector3i left, Vector3i right) =>
                left.x == right.x &&
                left.y == right.y &&
                left.z == right.z;

            private static string CellLabel(Vector3i value) =>
                value.x.ToString(CultureInfo.InvariantCulture) + "," +
                value.y.ToString(CultureInfo.InvariantCulture) + "," +
                value.z.ToString(CultureInfo.InvariantCulture);
        }

    }
}
