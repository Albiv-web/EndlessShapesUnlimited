using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.DecorationEditMode;
using EndlessShapes2.Polygon;
using Newtonsoft.Json;
using UnityEngine;

namespace DecoLimitLifter.Presets
{
    internal struct EsuPresetVector3
    {
        [JsonConstructor]
        internal EsuPresetVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [JsonProperty("x")]
        public float X { get; private set; }

        [JsonProperty("y")]
        public float Y { get; private set; }

        [JsonProperty("z")]
        public float Z { get; private set; }

        internal bool IsFinite =>
            IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

        internal Vector3 ToVector3() => new Vector3(X, Y, Z);

        internal static EsuPresetVector3 From(Vector3 value) =>
            new EsuPresetVector3(value.x, value.y, value.z);

        private static bool IsFiniteValue(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal struct EsuPresetCell
    {
        [JsonConstructor]
        internal EsuPresetCell(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [JsonProperty("x")]
        public int X { get; private set; }

        [JsonProperty("y")]
        public int Y { get; private set; }

        [JsonProperty("z")]
        public int Z { get; private set; }

        internal Vector3i ToCell() => new Vector3i(X, Y, Z);

        internal static EsuPresetCell From(Vector3i value) =>
            new EsuPresetCell(value.x, value.y, value.z);
    }

    internal sealed class EsuSurfacePresetFace
    {
        [JsonConstructor]
        internal EsuSurfacePresetFace(int a, int b, int c, int color)
        {
            A = a;
            B = b;
            C = c;
            Color = color;
        }

        [JsonProperty("a")]
        public int A { get; private set; }

        [JsonProperty("b")]
        public int B { get; private set; }

        [JsonProperty("c")]
        public int C { get; private set; }

        [JsonProperty("color")]
        public int Color { get; private set; }

        internal SurfaceFace ToFace() => new SurfaceFace(A, B, C);

        internal SurfaceFaceStyle ToStyle() => new SurfaceFaceStyle(Color);
    }

    internal sealed class EsuSurfaceDraftPresetPayload
    {
        internal const int SchemaVersion = 1;
        private const int MaximumPoints = 100000;
        private const int MaximumFaces = 100000;

        [JsonProperty("schema_version")]
        public int Version { get; private set; } = SchemaVersion;

        [JsonProperty("reference")]
        public EsuPresetVector3 Reference { get; private set; }

        [JsonProperty("points")]
        public EsuPresetVector3[] Points { get; private set; } = Array.Empty<EsuPresetVector3>();

        [JsonProperty("faces")]
        public EsuSurfacePresetFace[] Faces { get; private set; } = Array.Empty<EsuSurfacePresetFace>();

        [JsonProperty("manual_face_selection")]
        public int[] ManualFaceSelection { get; private set; } = Array.Empty<int>();

        [JsonProperty("free_triangle_selection")]
        public int[] FreeTriangleSelection { get; private set; } = Array.Empty<int>();

        [JsonProperty("bridge_edge_selection")]
        public EsuPresetCell[] BridgeEdgeSelection { get; private set; } = Array.Empty<EsuPresetCell>();

        [JsonProperty("selection_kind")]
        public SurfaceSelectionKind SelectionKind { get; private set; }

        [JsonProperty("selected_point")]
        public int SelectedPoint { get; private set; } = -1;

        [JsonProperty("selected_face")]
        public int SelectedFace { get; private set; } = -1;

        [JsonProperty("selected_edge")]
        public EsuPresetCell SelectedEdge { get; private set; } = new EsuPresetCell(-1, -1, 0);

        [JsonProperty("has_shared_anchor")]
        public bool HasSharedAnchor { get; private set; }

        [JsonProperty("shared_anchor_offset")]
        public EsuPresetCell SharedAnchorOffset { get; private set; }

        [JsonProperty("shared_anchor_selected")]
        public bool SharedAnchorSelected { get; private set; }

        [JsonProperty("structure_material")]
        public StructureBlockType StructureMaterial { get; private set; }

        [JsonProperty("face_thickness")]
        public float FaceThickness { get; private set; }

        [JsonProperty("color")]
        public int Color { get; private set; }

        [JsonProperty("normal_reversal")]
        public bool NormalReversal { get; private set; }

        [JsonProperty("nearest_anchor")]
        public bool NearestAnchor { get; private set; }

        [JsonConstructor]
        private EsuSurfaceDraftPresetPayload()
        {
        }

        internal static EsuSurfaceDraftPresetPayload Capture(
            SurfaceDraft draft,
            bool preserveSelection)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));

            SurfaceDraftSnapshot snapshot = draft.CreateSnapshot();
            Vector3 reference = ChooseReference(snapshot.Points, snapshot.HasSharedAnchor, snapshot.SharedAnchor);
            Vector3i referenceCell = RoundCell(reference);
            SurfaceDecorationSettingsSnapshot settings = snapshot.Settings ??
                                                        new SurfaceDecorationSettingsSnapshot(
                                                            new SurfaceDecorationSettings());

            return new EsuSurfaceDraftPresetPayload
            {
                Version = SchemaVersion,
                Reference = EsuPresetVector3.From(reference),
                Points = (snapshot.Points ?? Array.Empty<Vector3>())
                    .Select(point => EsuPresetVector3.From(point - reference))
                    .ToArray(),
                Faces = Enumerable.Range(0, snapshot.Faces?.Length ?? 0)
                    .Select(index =>
                    {
                        SurfaceFace face = snapshot.Faces[index];
                        int color = snapshot.FaceStyles != null && index < snapshot.FaceStyles.Length
                            ? snapshot.FaceStyles[index].ColorIndex
                            : settings.ColorIndex;
                        return new EsuSurfacePresetFace(face.A, face.B, face.C, color);
                    })
                    .ToArray(),
                ManualFaceSelection = preserveSelection
                    ? (snapshot.ManualFaceSelection ?? Array.Empty<int>()).ToArray()
                    : Array.Empty<int>(),
                FreeTriangleSelection = preserveSelection
                    ? (snapshot.FreeTriangleSelection ?? Array.Empty<int>()).ToArray()
                    : Array.Empty<int>(),
                BridgeEdgeSelection = preserveSelection
                    ? (snapshot.BridgeEdgeSelection ?? Array.Empty<SurfaceEdge>())
                        .Select(edge => new EsuPresetCell(edge.A, edge.B, 0))
                        .ToArray()
                    : Array.Empty<EsuPresetCell>(),
                SelectionKind = preserveSelection ? snapshot.SelectionKind : SurfaceSelectionKind.None,
                SelectedPoint = preserveSelection ? snapshot.SelectedPoint : -1,
                SelectedFace = preserveSelection ? snapshot.SelectedFace : -1,
                SelectedEdge = preserveSelection
                    ? new EsuPresetCell(snapshot.SelectedEdge.A, snapshot.SelectedEdge.B, 0)
                    : new EsuPresetCell(-1, -1, 0),
                HasSharedAnchor = snapshot.HasSharedAnchor,
                SharedAnchorOffset = snapshot.HasSharedAnchor
                    ? EsuPresetCell.From(Subtract(snapshot.SharedAnchor, referenceCell))
                    : default,
                SharedAnchorSelected = preserveSelection && snapshot.SharedAnchorSelected,
                StructureMaterial = settings.StructureBlockType,
                FaceThickness = settings.FaceThickness,
                Color = settings.ColorIndex,
                NormalReversal = settings.NormalReversal,
                NearestAnchor = settings.NearestAnchor
            };
        }

        internal bool TryCreateSnapshot(
            AllConstruct construct,
            Vector3 targetReference,
            out SurfaceDraftSnapshot snapshot,
            out string message)
        {
            snapshot = null;
            if (!TryValidate(out message))
                return false;
            if (construct == null)
            {
                message = "A target construct is required to restore a Surface preset.";
                return false;
            }
            if (!DecorationEditMath.IsFinite(targetReference))
            {
                message = "Surface preset target must be finite.";
                return false;
            }

            Vector3[] points = Points.Select(point => targetReference + point.ToVector3()).ToArray();
            if (points.Any(point => !DecorationEditMath.IsFinite(point)))
            {
                message = "Surface preset target would produce non-finite points.";
                return false;
            }

            SurfaceFace[] faces = Faces.Select(face => face.ToFace()).ToArray();
            SurfaceFaceStyle[] styles = Faces.Select(face => face.ToStyle()).ToArray();
            var settings = new SurfaceDecorationSettings
            {
                StructureBlockType = StructureMaterial,
                FaceThickness = FaceThickness,
                ColorIndex = Color,
                NormalReversal = NormalReversal,
                NearestAnchor = NearestAnchor
            };
            if (!settings.IsValid(out message))
                return false;

            Vector3i targetCell = RoundCell(targetReference);
            snapshot = new SurfaceDraftSnapshot(
                construct,
                points,
                faces,
                styles,
                ManualFaceSelection ?? Array.Empty<int>(),
                FreeTriangleSelection ?? Array.Empty<int>(),
                (BridgeEdgeSelection ?? Array.Empty<EsuPresetCell>())
                    .Select(edge => new SurfaceEdge(edge.X, edge.Y))
                    .ToArray(),
                SelectionKind,
                SelectedPoint,
                SelectedFace,
                new SurfaceEdge(SelectedEdge.X, SelectedEdge.Y),
                HasSharedAnchor,
                HasSharedAnchor ? Add(targetCell, SharedAnchorOffset.ToCell()) : default,
                SharedAnchorSelected,
                new SurfaceDecorationSettingsSnapshot(settings));
            message = "Surface preset is ready to restore.";
            return true;
        }

        internal bool TryValidate(out string message)
        {
            Points = Points ?? Array.Empty<EsuPresetVector3>();
            Faces = Faces ?? Array.Empty<EsuSurfacePresetFace>();
            ManualFaceSelection = ManualFaceSelection ?? Array.Empty<int>();
            FreeTriangleSelection = FreeTriangleSelection ?? Array.Empty<int>();
            BridgeEdgeSelection = BridgeEdgeSelection ?? Array.Empty<EsuPresetCell>();
            if (Version != SchemaVersion ||
                !Reference.IsFinite ||
                Points.Length > MaximumPoints ||
                Faces.Length > MaximumFaces ||
                Points.Any(point => !point.IsFinite))
            {
                message = "Surface preset schema, coordinates, or bounded counts are invalid.";
                return false;
            }
            if (Faces.Any(face =>
                    face == null ||
                    face.A < 0 || face.B < 0 || face.C < 0 ||
                    face.A >= Points.Length || face.B >= Points.Length || face.C >= Points.Length ||
                    face.A == face.B || face.B == face.C || face.C == face.A ||
                    face.Color < 0 || face.Color > 31))
            {
                message = "Surface preset contains an invalid face.";
                return false;
            }
            if (ManualFaceSelection.Any(index => index < 0 || index >= Points.Length) ||
                FreeTriangleSelection.Any(index => index < 0 || index >= Points.Length) ||
                BridgeEdgeSelection.Any(edge =>
                    edge.X < 0 || edge.Y < 0 || edge.X >= Points.Length || edge.Y >= Points.Length || edge.X == edge.Y))
            {
                message = "Surface preset contains invalid selection state.";
                return false;
            }

            var settings = new SurfaceDecorationSettings
            {
                StructureBlockType = StructureMaterial,
                FaceThickness = FaceThickness,
                ColorIndex = Color,
                NormalReversal = NormalReversal,
                NearestAnchor = NearestAnchor
            };
            if (!settings.IsValid(out message))
                return false;

            message = "Surface preset is valid.";
            return true;
        }

        private static Vector3 ChooseReference(
            IReadOnlyList<Vector3> points,
            bool hasAnchor,
            Vector3i anchor)
        {
            if (points != null && points.Count > 0)
                return points[0];
            return hasAnchor
                ? new Vector3(anchor.x, anchor.y, anchor.z)
                : Vector3.zero;
        }

        private static Vector3i RoundCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

        private static Vector3i Subtract(Vector3i left, Vector3i right) =>
            new Vector3i(left.x - right.x, left.y - right.y, left.z - right.z);

        private static Vector3i Add(Vector3i left, Vector3i right) =>
            new Vector3i(left.x + right.x, left.y + right.y, left.z + right.z);
    }

    internal sealed class EsuGeneratorDraftPresetPayload
    {
        internal const int SchemaVersion = 1;
        private const int MaximumPathPoints = 100000;

        [JsonProperty("schema_version")]
        public int Version { get; private set; } = SchemaVersion;

        [JsonProperty("tool")]
        public SurfaceExtraTool Tool { get; private set; }

        [JsonProperty("reference")]
        public EsuPresetVector3 Reference { get; private set; }

        [JsonProperty("path_points")]
        public EsuPresetVector3[] PathPoints { get; private set; } = Array.Empty<EsuPresetVector3>();

        [JsonProperty("has_center")]
        public bool HasCenter { get; private set; }

        [JsonProperty("center_offset")]
        public EsuPresetVector3 CenterOffset { get; private set; }

        [JsonProperty("normal")]
        public EsuPresetVector3 Normal { get; private set; }

        [JsonProperty("tangent_a")]
        public EsuPresetVector3 TangentA { get; private set; }

        [JsonProperty("tangent_b")]
        public EsuPresetVector3 TangentB { get; private set; }

        [JsonProperty("has_shared_anchor")]
        public bool HasSharedAnchor { get; private set; }

        [JsonProperty("shared_anchor_offset")]
        public EsuPresetCell SharedAnchorOffset { get; private set; }

        [JsonProperty("shared_anchor_selected")]
        public bool SharedAnchorSelected { get; private set; }

        [JsonProperty("selected_point")]
        public int SelectedPoint { get; private set; } = -1;

        [JsonProperty("smooth_bezier_path")]
        public bool SmoothBezierPath { get; private set; }

        [JsonProperty("bezier_subdivisions")]
        public int BezierSubdivisions { get; private set; } =
            DecorationGeneratorDraft.DefaultBezierSubdivisions;

        [JsonProperty("bezier_tension")]
        public float BezierTension { get; private set; } =
            DecorationGeneratorDraft.DefaultBezierTension;

        [JsonProperty("settings")]
        public EsuGeneratorSettingsPayload Settings { get; private set; }

        [JsonConstructor]
        private EsuGeneratorDraftPresetPayload()
        {
        }

        internal static EsuGeneratorDraftPresetPayload Capture(
            DecorationGeneratorDraft draft,
            DecorationGeneratorSettings settings,
            bool preserveSelection)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            DecorationGeneratorDraftSnapshot snapshot = draft.CreateSnapshot();
            Vector3 reference = ChooseReference(snapshot);
            Vector3i referenceCell = RoundCell(reference);
            return new EsuGeneratorDraftPresetPayload
            {
                Version = SchemaVersion,
                Tool = snapshot.Tool,
                Reference = EsuPresetVector3.From(reference),
                PathPoints = (snapshot.PathPoints ?? Array.Empty<Vector3>())
                    .Select(point => EsuPresetVector3.From(point - reference))
                    .ToArray(),
                HasCenter = snapshot.HasCircleCenter,
                CenterOffset = EsuPresetVector3.From(snapshot.CircleCenter - reference),
                Normal = EsuPresetVector3.From(snapshot.CircleNormal),
                TangentA = EsuPresetVector3.From(snapshot.CircleTangentA),
                TangentB = EsuPresetVector3.From(snapshot.CircleTangentB),
                HasSharedAnchor = snapshot.HasSharedAnchor,
                SharedAnchorOffset = snapshot.HasSharedAnchor
                    ? EsuPresetCell.From(Subtract(snapshot.SharedAnchor, referenceCell))
                    : default,
                SharedAnchorSelected = preserveSelection && snapshot.SharedAnchorSelected,
                SelectedPoint = preserveSelection ? snapshot.SelectedPoint : -1,
                SmoothBezierPath = snapshot.SmoothBezierPath,
                BezierSubdivisions = snapshot.BezierSubdivisions,
                BezierTension = snapshot.BezierTension,
                Settings = EsuGeneratorSettingsPayload.Capture(settings)
            };
        }

        internal bool TryCreateSnapshot(
            AllConstruct construct,
            Vector3 targetReference,
            out DecorationGeneratorEditSnapshot snapshot,
            out string message)
        {
            snapshot = null;
            if (!TryValidate(out message))
                return false;
            if (construct == null)
            {
                message = "A target construct is required to restore a generator preset.";
                return false;
            }
            if (!DecorationEditMath.IsFinite(targetReference))
            {
                message = "Generator preset target must be finite.";
                return false;
            }

            Vector3[] points = PathPoints.Select(point => targetReference + point.ToVector3()).ToArray();
            Vector3 center = targetReference + CenterOffset.ToVector3();
            if (points.Any(point => !DecorationEditMath.IsFinite(point)) ||
                !DecorationEditMath.IsFinite(center))
            {
                message = "Generator preset target would produce non-finite points.";
                return false;
            }

            DecorationGeneratorSettings restoredSettings = Settings.ToSettings();
            if (!restoredSettings.IsValid(out message))
                return false;

            Vector3i targetCell = RoundCell(targetReference);
            snapshot = new DecorationGeneratorEditSnapshot(
                new DecorationGeneratorDraftSnapshot(
                    Tool,
                    construct,
                    points,
                    HasCenter,
                    center,
                    Normal.ToVector3(),
                    TangentA.ToVector3(),
                    TangentB.ToVector3(),
                    HasSharedAnchor,
                    HasSharedAnchor ? Add(targetCell, SharedAnchorOffset.ToCell()) : default,
                    SharedAnchorSelected,
                    SelectedPoint,
                    SmoothBezierPath,
                    BezierSubdivisions,
                    BezierTension),
                new DecorationGeneratorSettingsSnapshot(restoredSettings));
            message = "Generator preset is ready to restore.";
            return true;
        }

        internal bool TryValidate(out string message)
        {
            PathPoints = PathPoints ?? Array.Empty<EsuPresetVector3>();
            if (Version != SchemaVersion ||
                !Enum.IsDefined(typeof(SurfaceExtraTool), Tool) ||
                Tool == SurfaceExtraTool.None ||
                !Reference.IsFinite ||
                PathPoints.Length > MaximumPathPoints ||
                PathPoints.Any(point => !point.IsFinite) ||
                !CenterOffset.IsFinite ||
                !Normal.IsFinite ||
                !TangentA.IsFinite ||
                !TangentB.IsFinite ||
                Settings == null)
            {
                message = "Generator preset schema, tool, coordinates, or bounded counts are invalid.";
                return false;
            }
            if (!SurfaceBezierPath.IsValidSubdivisions(BezierSubdivisions) ||
                !SurfaceBezierPath.IsValidTension(BezierTension) ||
                (SmoothBezierPath &&
                 (!DecorationGeneratorDraft.UsesPath(Tool) ||
                  PathPoints.Length < SurfaceBezierPath.MinimumKnots)))
            {
                message = "Generator preset contains invalid smooth Bezier path settings.";
                return false;
            }
            if (SelectedPoint < -1 ||
                (SelectedPoint >= 0 &&
                 (DecorationGeneratorDraft.UsesCenter(Tool)
                     ? SelectedPoint != 0 || !HasCenter
                     : SelectedPoint >= PathPoints.Length)))
            {
                message = "Generator preset contains invalid selection state.";
                return false;
            }
            if (!Settings.TryValidate(out message))
                return false;

            message = "Generator preset is valid.";
            return true;
        }

        private static Vector3 ChooseReference(DecorationGeneratorDraftSnapshot snapshot)
        {
            if (snapshot.HasCircleCenter)
                return snapshot.CircleCenter;
            if (snapshot.PathPoints != null && snapshot.PathPoints.Length > 0)
                return snapshot.PathPoints[0];
            if (snapshot.HasSharedAnchor)
                return new Vector3(snapshot.SharedAnchor.x, snapshot.SharedAnchor.y, snapshot.SharedAnchor.z);
            return Vector3.zero;
        }

        private static Vector3i RoundCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

        private static Vector3i Subtract(Vector3i left, Vector3i right) =>
            new Vector3i(left.x - right.x, left.y - right.y, left.z - right.z);

        private static Vector3i Add(Vector3i left, Vector3i right) =>
            new Vector3i(left.x + right.x, left.y + right.y, left.z + right.z);
    }

    internal sealed class EsuGeneratorSettingsPayload
    {
        [JsonProperty("mesh_guid")]
        public string MeshGuid { get; private set; }

        [JsonProperty("length_axis")]
        public DecorationEditAxis LengthAxis { get; private set; }

        [JsonProperty("diameter")]
        public float Diameter { get; private set; }

        [JsonProperty("circle_radius")]
        public float CircleRadius { get; private set; }

        [JsonProperty("circle_segments")]
        public int CircleSegments { get; private set; }

        [JsonProperty("arc_degrees")]
        public float ArcDegrees { get; private set; }

        [JsonProperty("shape_height")]
        public float ShapeHeight { get; private set; }

        [JsonProperty("top_radius")]
        public float TopRadius { get; private set; }

        [JsonProperty("ring_count")]
        public int RingCount { get; private set; }

        [JsonProperty("quad_width")]
        public float QuadWidth { get; private set; }

        [JsonProperty("quad_height")]
        public float QuadHeight { get; private set; }

        [JsonProperty("polygon_sides")]
        public int PolygonSides { get; private set; }

        [JsonProperty("tube_diameter")]
        public float TubeDiameter { get; private set; }

        [JsonProperty("tube_segments")]
        public int TubeSegments { get; private set; }

        [JsonProperty("color")]
        public int Color { get; private set; }

        [JsonProperty("nearest_anchor")]
        public bool NearestAnchor { get; private set; }

        [JsonProperty("material_guid")]
        public string MaterialGuid { get; private set; }

        [JsonConstructor]
        private EsuGeneratorSettingsPayload()
        {
        }

        internal static EsuGeneratorSettingsPayload Capture(DecorationGeneratorSettings settings) =>
            new EsuGeneratorSettingsPayload
            {
                MeshGuid = settings.MeshGuid.ToString("D"),
                LengthAxis = settings.LengthAxis,
                Diameter = settings.Diameter,
                CircleRadius = settings.CircleRadius,
                CircleSegments = settings.CircleSegments,
                ArcDegrees = settings.ArcDegrees,
                ShapeHeight = settings.ShapeHeight,
                TopRadius = settings.TopRadius,
                RingCount = settings.RingCount,
                QuadWidth = settings.QuadWidth,
                QuadHeight = settings.QuadHeight,
                PolygonSides = settings.PolygonSides,
                TubeDiameter = settings.TubeDiameter,
                TubeSegments = settings.TubeSegments,
                Color = settings.ColorIndex,
                NearestAnchor = settings.NearestAnchor,
                MaterialGuid = settings.MaterialReplacement.ToString("D")
            };

        internal DecorationGeneratorSettings ToSettings()
        {
            var settings = new DecorationGeneratorSettings
            {
                MeshGuid = Guid.Parse(MeshGuid),
                LengthAxis = LengthAxis,
                Diameter = Diameter,
                CircleRadius = CircleRadius,
                CircleSegments = CircleSegments,
                ArcDegrees = ArcDegrees,
                ShapeHeight = ShapeHeight,
                TopRadius = TopRadius,
                RingCount = RingCount,
                QuadWidth = QuadWidth,
                QuadHeight = QuadHeight,
                PolygonSides = PolygonSides,
                TubeDiameter = TubeDiameter,
                TubeSegments = TubeSegments,
                ColorIndex = Color,
                NearestAnchor = NearestAnchor,
                MaterialReplacement = Guid.Parse(MaterialGuid)
            };
            return settings;
        }

        internal bool TryValidate(out string message)
        {
            if (!Guid.TryParse(MeshGuid, out Guid mesh) || mesh == Guid.Empty ||
                !Guid.TryParse(MaterialGuid, out _))
            {
                message = "Generator preset contains an invalid mesh or material GUID.";
                return false;
            }

            DecorationGeneratorSettings settings = ToSettings();
            return settings.IsValid(out message);
        }
    }
}
