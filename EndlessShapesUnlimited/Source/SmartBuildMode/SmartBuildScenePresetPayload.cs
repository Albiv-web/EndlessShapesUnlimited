using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.Presets;
using Newtonsoft.Json;

namespace DecoLimitLifter.SmartBuildMode
{
    internal struct SmartBuildPresetCell
    {
        [JsonConstructor]
        internal SmartBuildPresetCell(int x, int y, int z)
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

        internal static SmartBuildPresetCell From(Vector3i value) =>
            new SmartBuildPresetCell(value.x, value.y, value.z);
    }

    /// <summary>
    /// A construct-independent Smart Builder piece. Origin is relative to the scene anchor;
    /// all remaining values are primitive data so presets remain portable across sessions.
    /// </summary>
    internal sealed class SmartBuildPiecePresetPayload
    {
        [JsonProperty("shape")]
        public SmartBuildShapeKind ShapeKind { get; private set; }

        [JsonProperty("descriptor")]
        public string ShapeDescriptorKey { get; private set; }

        [JsonProperty("origin_offset")]
        public SmartBuildPresetCell OriginOffset { get; private set; }

        [JsonProperty("size")]
        public SmartBuildPresetCell CuboidSize { get; private set; }

        [JsonProperty("draw_plane")]
        public SmartBuildDrawPlane DrawPlane { get; private set; }

        [JsonProperty("slope_length")]
        public int SlopeLength { get; private set; }

        [JsonProperty("slope_steps")]
        public int SlopeSteps { get; private set; }

        [JsonProperty("slope_width")]
        public int SlopeWidth { get; private set; }

        [JsonProperty("selected_length")]
        public int SelectedLength { get; private set; }

        [JsonProperty("fixed_forward_tiles")]
        public int FixedForwardTiles { get; private set; }

        [JsonProperty("fixed_forward_cells")]
        public int FixedForwardCells { get; private set; }

        [JsonProperty("fixed_right_tiles")]
        public int FixedRightTiles { get; private set; }

        [JsonProperty("fixed_drop_tiles")]
        public int FixedDropTiles { get; private set; }

        [JsonProperty("support")]
        public SmartBuildSlopeSupportMode SupportMode { get; private set; }

        [JsonProperty("generator_sides")]
        public int GeneratorSides { get; private set; }

        [JsonProperty("generator_fill")]
        public SmartBuildGeneratorFillMode GeneratorFillMode { get; private set; }

        [JsonProperty("generator_smoothing")]
        public SmartBuildGeneratorSmoothingMode GeneratorSmoothingMode { get; private set; }

        [JsonProperty("generator_round_lock")]
        public bool GeneratorRoundLock { get; private set; }

        [JsonProperty("generator_arc_degrees")]
        public int GeneratorArcDegrees { get; private set; }

        [JsonProperty("generator_top_scale_percent")]
        public int GeneratorTopScalePercent { get; private set; }

        [JsonProperty("cuboid_hollow")]
        public bool CuboidHollow { get; private set; }

        [JsonProperty("forward_axis")]
        public SmartBuildAxis ForwardAxis { get; private set; }

        [JsonProperty("forward_sign")]
        public int ForwardSign { get; private set; }

        [JsonProperty("right_axis")]
        public SmartBuildAxis RightAxis { get; private set; }

        [JsonProperty("right_sign")]
        public int RightSign { get; private set; }

        [JsonProperty("drop_axis")]
        public SmartBuildAxis DropAxis { get; private set; }

        [JsonProperty("drop_sign")]
        public int DropSign { get; private set; }

        [JsonProperty("material")]
        public SmartBuildMaterial Material { get; private set; }

        [JsonConstructor]
        private SmartBuildPiecePresetPayload()
        {
        }

        internal static SmartBuildPiecePresetPayload Capture(
            SmartBuildPiece piece,
            Vector3i anchor,
            SmartBuildMaterial defaultMaterial)
        {
            if (piece == null)
                throw new ArgumentNullException(nameof(piece));

            return new SmartBuildPiecePresetPayload
            {
                ShapeKind = piece.ShapeKind,
                ShapeDescriptorKey = piece.ShapeDescriptorKey,
                OriginOffset = SmartBuildPresetCell.From(piece.Origin - anchor),
                CuboidSize = SmartBuildPresetCell.From(piece.PresetCuboidSize),
                DrawPlane = piece.DrawPlane,
                SlopeLength = piece.SlopeLength,
                SlopeSteps = piece.SlopeSteps,
                SlopeWidth = piece.SlopeWidth,
                SelectedLength = piece.SelectedLength,
                FixedForwardTiles = piece.FixedForwardTiles,
                FixedForwardCells = piece.FixedForwardCells,
                FixedRightTiles = piece.FixedRightTiles,
                FixedDropTiles = piece.FixedDropTiles,
                SupportMode = piece.SupportMode,
                GeneratorSides = piece.GeneratorSides,
                GeneratorFillMode = piece.GeneratorFillMode,
                GeneratorSmoothingMode = piece.GeneratorSmoothingMode,
                GeneratorRoundLock = piece.GeneratorRoundLock,
                GeneratorArcDegrees = piece.GeneratorArcDegrees,
                GeneratorTopScalePercent = piece.GeneratorTopScalePercent,
                CuboidHollow = piece.CuboidHollow,
                ForwardAxis = piece.ForwardAxis,
                ForwardSign = piece.ForwardSign,
                RightAxis = piece.RightAxis,
                RightSign = piece.RightSign,
                DropAxis = piece.DropAxis,
                DropSign = piece.DropSign,
                Material = piece.MaterialOverride ?? defaultMaterial
            };
        }

        internal bool TryValidate(int index, out string reason)
        {
            string prefix = "Piece " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": ";
            if (!Enum.IsDefined(typeof(SmartBuildShapeKind), ShapeKind) ||
                !Enum.IsDefined(typeof(SmartBuildDrawPlane), DrawPlane) ||
                !Enum.IsDefined(typeof(SmartBuildSlopeSupportMode), SupportMode) ||
                !Enum.IsDefined(typeof(SmartBuildGeneratorFillMode), GeneratorFillMode) ||
                !Enum.IsDefined(typeof(SmartBuildGeneratorSmoothingMode), GeneratorSmoothingMode) ||
                !Enum.IsDefined(typeof(SmartBuildMaterial), Material) ||
                !ValidAxis(ForwardAxis) ||
                !ValidAxis(RightAxis) ||
                !ValidAxis(DropAxis))
            {
                reason = prefix + "contains an unsupported enum value.";
                return false;
            }

            if (ForwardAxis == RightAxis || ForwardAxis == DropAxis || RightAxis == DropAxis ||
                !ValidSign(ForwardSign) || !ValidSign(RightSign) || !ValidSign(DropSign))
            {
                reason = prefix + "has an invalid orientation basis.";
                return false;
            }

            Vector3i size = CuboidSize.ToCell();
            if (!ValidExtent(size.x) || !ValidExtent(size.y) || !ValidExtent(size.z) ||
                !ValidExtent(SlopeSteps) || !ValidExtent(SlopeWidth) ||
                !ValidExtent(FixedForwardTiles) || !ValidExtent(FixedForwardCells) ||
                !ValidExtent(FixedRightTiles) || !ValidExtent(FixedDropTiles) ||
                SlopeLength < 1 || SlopeLength > 4 ||
                SelectedLength < 1 || SelectedLength > 4 ||
                GeneratorSides < 0 || GeneratorSides > 64)
            {
                reason = prefix + "has dimensions outside the Smart Builder safety limits.";
                return false;
            }

            if (ShapeKind == SmartBuildShapeKind.GeneratedArc &&
                (GeneratorArcDegrees < 15 || GeneratorArcDegrees > 360))
            {
                reason = prefix + "has an invalid arc sweep.";
                return false;
            }
            if (ShapeKind == SmartBuildShapeKind.GeneratedFrustum &&
                (GeneratorTopScalePercent < 5 || GeneratorTopScalePercent > 95))
            {
                reason = prefix + "has an invalid frustum top scale.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ShapeDescriptorKey) ||
                ShapeDescriptorKey.Length > 128)
            {
                reason = prefix + "has no valid shape descriptor.";
                return false;
            }

            SmartBuildShapeDescriptor descriptor = SmartBuildShapeDescriptors.ByKey(ShapeDescriptorKey);
            if (ShapeKind != SmartBuildShapeKind.Cuboid && descriptor == null)
            {
                reason = prefix + "uses shape '" + ShapeDescriptorKey + "', which is unavailable.";
                return false;
            }

            Vector3i offset = OriginOffset.ToCell();
            if (!ValidCoordinate(offset.x) || !ValidCoordinate(offset.y) || !ValidCoordinate(offset.z))
            {
                reason = prefix + "origin is outside the portable scene coordinate limit.";
                return false;
            }

            reason = null;
            return true;
        }

        internal bool TryEstimateEnumerationCells(
            int index,
            out long estimatedCells,
            out string reason)
        {
            const long limit = SmartBuildLimits.MaximumPersistedSceneEnumerationCells;
            string prefix = "Piece " +
                            (index + 1).ToString(CultureInfo.InvariantCulture) +
                            ": ";
            Vector3i size = CuboidSize.ToCell();

            if (ShapeKind == SmartBuildShapeKind.Cuboid || IsGeneratedShape(ShapeKind))
            {
                if (SmartBuildLimits.TryProductWithinLimit(
                        size.x,
                        size.y,
                        size.z,
                        limit,
                        out estimatedCells))
                {
                    reason = null;
                    return true;
                }

                reason = prefix + "estimated cell enumeration exceeds the persisted-scene safety limit.";
                return false;
            }

            if (ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                if (!SmartBuildLimits.TryProductWithinLimit(
                        SlopeLength,
                        SlopeWidth,
                        SlopeSteps,
                        limit,
                        out long slopeCells))
                {
                    estimatedCells = limit + 1L;
                    reason = prefix + "estimated slope enumeration exceeds the persisted-scene safety limit.";
                    return false;
                }

                if (SupportMode == SmartBuildSlopeSupportMode.Step)
                {
                    if (SmartBuildLimits.TryMultiplyWithinLimit(
                            slopeCells,
                            2L,
                            limit,
                            out estimatedCells))
                    {
                        reason = null;
                        return true;
                    }
                }
                else
                {
                    long triangularFirst = SlopeSteps;
                    long triangularSecond = (long)SlopeSteps + 1L;
                    if ((triangularFirst & 1L) == 0L)
                        triangularFirst /= 2L;
                    else
                        triangularSecond /= 2L;

                    if (SmartBuildLimits.TryMultiplyWithinLimit(
                            triangularFirst,
                            triangularSecond,
                            limit,
                            out long stepLayers) &&
                        SmartBuildLimits.TryMultiplyWithinLimit(
                            (long)SlopeLength * SlopeWidth,
                            stepLayers,
                            limit,
                            out estimatedCells))
                    {
                        reason = null;
                        return true;
                    }
                }

                estimatedCells = limit + 1L;
                reason = prefix + "estimated slope/support enumeration exceeds the persisted-scene safety limit.";
                return false;
            }

            if (SmartBuildLimits.TryProductWithinLimit(
                    FixedForwardCells,
                    FixedRightTiles,
                    FixedDropTiles,
                    limit,
                    out estimatedCells))
            {
                reason = null;
                return true;
            }

            reason = prefix + "estimated fixed-shape enumeration exceeds the persisted-scene safety limit.";
            return false;
        }

        private static bool ValidAxis(SmartBuildAxis axis) =>
            Enum.IsDefined(typeof(SmartBuildAxis), axis);

        private static bool ValidSign(int sign) => sign == -1 || sign == 1;

        private static bool ValidExtent(int value) =>
            value >= 1 && value <= SmartBuildLimits.MaximumPresetExtent;

        private static bool ValidCoordinate(int value) =>
            SmartBuildLimits.IsPortableCoordinate(value);

        private static bool IsGeneratedShape(SmartBuildShapeKind shapeKind) =>
            shapeKind == SmartBuildShapeKind.GeneratedCircle ||
            shapeKind == SmartBuildShapeKind.GeneratedPolygon ||
            shapeKind == SmartBuildShapeKind.GeneratedSphere ||
            shapeKind == SmartBuildShapeKind.GeneratedArc ||
            shapeKind == SmartBuildShapeKind.GeneratedCone ||
            shapeKind == SmartBuildShapeKind.GeneratedFrustum ||
            shapeKind == SmartBuildShapeKind.GeneratedTube;
    }

    internal static class SmartBuildScenePresetDiscriminators
    {
        internal static string Node(SmartBuildSceneNodeKind kind)
        {
            switch (kind)
            {
                case SmartBuildSceneNodeKind.Pattern:
                    return "pattern";
                case SmartBuildSceneNodeKind.Region:
                    return "region";
                default:
                    return "primitive";
            }
        }

        internal static bool TryNode(string value, out SmartBuildSceneNodeKind kind)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "primitive":
                    kind = SmartBuildSceneNodeKind.Primitive;
                    return true;
                case "pattern":
                    kind = SmartBuildSceneNodeKind.Pattern;
                    return true;
                case "region":
                    kind = SmartBuildSceneNodeKind.Region;
                    return true;
                default:
                    kind = SmartBuildSceneNodeKind.Primitive;
                    return false;
            }
        }

        internal static string Pattern(SmartBuildEditablePatternKind kind)
        {
            switch (kind)
            {
                case SmartBuildEditablePatternKind.Grid:
                    return "grid";
                case SmartBuildEditablePatternKind.Radial:
                    return "radial";
                case SmartBuildEditablePatternKind.Polyline:
                    return "polyline";
                default:
                    return "linear";
            }
        }

        internal static bool TryPattern(string value, out SmartBuildEditablePatternKind kind)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "linear":
                    kind = SmartBuildEditablePatternKind.Linear;
                    return true;
                case "grid":
                    kind = SmartBuildEditablePatternKind.Grid;
                    return true;
                case "radial":
                    kind = SmartBuildEditablePatternKind.Radial;
                    return true;
                case "polyline":
                    kind = SmartBuildEditablePatternKind.Polyline;
                    return true;
                default:
                    kind = SmartBuildEditablePatternKind.Linear;
                    return false;
            }
        }

        internal static string Region(SmartBuildRegionKind kind)
        {
            switch (kind)
            {
                case SmartBuildRegionKind.Wall:
                    return "wall";
                case SmartBuildRegionKind.Plane:
                    return "plane";
                case SmartBuildRegionKind.Brush:
                    return "brush";
                case SmartBuildRegionKind.Flood:
                    return "flood";
                default:
                    return "rectangle";
            }
        }

        internal static bool TryRegion(string value, out SmartBuildRegionKind kind)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "rectangle":
                    kind = SmartBuildRegionKind.Rectangle;
                    return true;
                case "wall":
                    kind = SmartBuildRegionKind.Wall;
                    return true;
                case "plane":
                    kind = SmartBuildRegionKind.Plane;
                    return true;
                case "brush":
                    kind = SmartBuildRegionKind.Brush;
                    return true;
                case "flood":
                    kind = SmartBuildRegionKind.Flood;
                    return true;
                default:
                    kind = SmartBuildRegionKind.Rectangle;
                    return false;
            }
        }

        internal static string Axis(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return "x";
                case DecorationEditAxis.Z:
                    return "z";
                default:
                    return "y";
            }
        }

        internal static bool TryAxis(string value, out DecorationEditAxis axis)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "x":
                    axis = DecorationEditAxis.X;
                    return true;
                case "y":
                    axis = DecorationEditAxis.Y;
                    return true;
                case "z":
                    axis = DecorationEditAxis.Z;
                    return true;
                default:
                    axis = DecorationEditAxis.None;
                    return false;
            }
        }

        internal static string RadialOrientation(SmartBuildRadialOrientationMode mode) =>
            mode == SmartBuildRadialOrientationMode.Keep
                ? "keep"
                : "rotate_cardinal";

        internal static bool TryRadialOrientation(
            string value,
            out SmartBuildRadialOrientationMode mode)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "keep":
                    mode = SmartBuildRadialOrientationMode.Keep;
                    return true;
                case "rotate_cardinal":
                    mode = SmartBuildRadialOrientationMode.RotateCardinal;
                    return true;
                default:
                    mode = SmartBuildRadialOrientationMode.Keep;
                    return false;
            }
        }

        internal static string PathMode(SmartBuildPathArrayMode mode) =>
            mode == SmartBuildPathArrayMode.PolylineVertices
                ? "polyline_vertices"
                : "stepped_cells";

        internal static bool TryPathMode(string value, out SmartBuildPathArrayMode mode)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "polyline_vertices":
                    mode = SmartBuildPathArrayMode.PolylineVertices;
                    return true;
                case "stepped_cells":
                    mode = SmartBuildPathArrayMode.SteppedCells;
                    return true;
                default:
                    mode = SmartBuildPathArrayMode.PolylineVertices;
                    return false;
            }
        }

        internal static string PathOrientation(SmartBuildPolylineOrientationMode mode) =>
            mode == SmartBuildPolylineOrientationMode.CardinalTangent
                ? "cardinal_tangent"
                : "keep";

        internal static bool TryPathOrientation(
            string value,
            out SmartBuildPolylineOrientationMode mode)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "keep":
                    mode = SmartBuildPolylineOrientationMode.Keep;
                    return true;
                case "cardinal_tangent":
                    mode = SmartBuildPolylineOrientationMode.CardinalTangent;
                    return true;
                default:
                    mode = SmartBuildPolylineOrientationMode.Keep;
                    return false;
            }
        }
    }

    internal static class SmartBuildScenePresetCoordinates
    {
        internal static bool IsPortable(SmartBuildPresetCell value)
        {
            Vector3i cell = value.ToCell();
            return SmartBuildLimits.IsPortableCoordinate(cell.x) &&
                   SmartBuildLimits.IsPortableCoordinate(cell.y) &&
                   SmartBuildLimits.IsPortableCoordinate(cell.z);
        }

        internal static bool TryAdd(
            Vector3i anchor,
            SmartBuildPresetCell offset,
            out Vector3i result)
        {
            Vector3i value = offset.ToCell();
            long x = (long)anchor.x + value.x;
            long y = (long)anchor.y + value.y;
            long z = (long)anchor.z + value.z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            {
                result = new Vector3i(0, 0, 0);
                return false;
            }

            result = new Vector3i((int)x, (int)y, (int)z);
            return true;
        }

        internal static SmartBuildPresetCell Relative(Vector3i value, Vector3i anchor)
        {
            long x = (long)value.x - anchor.x;
            long y = (long)value.y - anchor.y;
            long z = (long)value.z - anchor.z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "A Smart Builder node coordinate cannot be represented relative to its scene anchor.");
            }

            return new SmartBuildPresetCell((int)x, (int)y, (int)z);
        }
    }

    internal sealed class SmartBuildPatternDefinitionPresetPayload
    {
        [JsonProperty("kind")]
        public string Kind { get; private set; }

        [JsonProperty("primary_step")]
        public SmartBuildPresetCell PrimaryStep { get; private set; }

        [JsonProperty("secondary_step")]
        public SmartBuildPresetCell SecondaryStep { get; private set; }

        [JsonProperty("primary_before")]
        public int PrimaryBefore { get; private set; }

        [JsonProperty("primary_after")]
        public int PrimaryAfter { get; private set; }

        [JsonProperty("secondary_before")]
        public int SecondaryBefore { get; private set; }

        [JsonProperty("secondary_after")]
        public int SecondaryAfter { get; private set; }

        [JsonProperty("radial_pivot_offset")]
        public SmartBuildPresetCell RadialPivotOffset { get; private set; }

        [JsonProperty("radial_axis")]
        public string RadialAxis { get; private set; }

        [JsonProperty("radial_angle_degrees")]
        public float RadialAngleStepDegrees { get; private set; }

        [JsonProperty("radial_orientation")]
        public string RadialOrientation { get; private set; }

        [JsonProperty("path_point_offsets")]
        public SmartBuildPresetCell[] PathPointOffsets { get; private set; } =
            Array.Empty<SmartBuildPresetCell>();

        [JsonProperty("path_mode")]
        public string PathMode { get; private set; }

        [JsonProperty("path_orientation")]
        public string PathOrientation { get; private set; }

        [JsonProperty("path_spacing_cells")]
        public int PathSpacingCells { get; private set; }

        [JsonConstructor]
        private SmartBuildPatternDefinitionPresetPayload()
        {
        }

        internal static SmartBuildPatternDefinitionPresetPayload Capture(
            SmartBuildPatternDefinition definition,
            Vector3i anchor)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            return new SmartBuildPatternDefinitionPresetPayload
            {
                Kind = SmartBuildScenePresetDiscriminators.Pattern(definition.Kind),
                PrimaryStep = SmartBuildPresetCell.From(definition.PrimaryStep),
                SecondaryStep = SmartBuildPresetCell.From(definition.SecondaryStep),
                PrimaryBefore = definition.PrimaryBefore,
                PrimaryAfter = definition.PrimaryAfter,
                SecondaryBefore = definition.SecondaryBefore,
                SecondaryAfter = definition.SecondaryAfter,
                RadialPivotOffset = SmartBuildScenePresetCoordinates.Relative(
                    definition.RadialPivot,
                    anchor),
                RadialAxis = SmartBuildScenePresetDiscriminators.Axis(definition.RadialAxis),
                RadialAngleStepDegrees = definition.RadialAngleStepDegrees,
                RadialOrientation = SmartBuildScenePresetDiscriminators.RadialOrientation(
                    definition.RadialOrientation),
                PathPointOffsets = (definition.PathPoints ?? Array.Empty<Vector3i>())
                    .Select(point => SmartBuildScenePresetCoordinates.Relative(point, anchor))
                    .ToArray(),
                PathMode = SmartBuildScenePresetDiscriminators.PathMode(definition.PathMode),
                PathOrientation = SmartBuildScenePresetDiscriminators.PathOrientation(
                    definition.PathOrientation),
                PathSpacingCells = definition.PathSpacingCells
            };
        }

        internal bool TryRestore(
            Vector3i anchor,
            int sourceCount,
            out SmartBuildPatternDefinition definition,
            out string reason)
        {
            definition = null;
            PathPointOffsets = PathPointOffsets ?? Array.Empty<SmartBuildPresetCell>();
            if (!SmartBuildScenePresetDiscriminators.TryPattern(
                    Kind,
                    out SmartBuildEditablePatternKind kind) ||
                !SmartBuildScenePresetDiscriminators.TryAxis(
                    RadialAxis,
                    out DecorationEditAxis radialAxis) ||
                !SmartBuildScenePresetDiscriminators.TryRadialOrientation(
                    RadialOrientation,
                    out SmartBuildRadialOrientationMode radialOrientation) ||
                !SmartBuildScenePresetDiscriminators.TryPathMode(
                    PathMode,
                    out SmartBuildPathArrayMode pathMode) ||
                !SmartBuildScenePresetDiscriminators.TryPathOrientation(
                    PathOrientation,
                    out SmartBuildPolylineOrientationMode pathOrientation))
            {
                reason = "An editable pattern uses an unsupported string discriminator.";
                return false;
            }
            if (PrimaryBefore < 0 ||
                PrimaryBefore > SmartBuildLimits.MaximumPatternCopies ||
                PrimaryAfter < 0 ||
                PrimaryAfter > SmartBuildLimits.MaximumPatternCopies ||
                SecondaryBefore < 0 ||
                SecondaryBefore > SmartBuildLimits.MaximumPatternCopies ||
                SecondaryAfter < 0 ||
                SecondaryAfter > SmartBuildLimits.MaximumPatternCopies ||
                float.IsNaN(RadialAngleStepDegrees) ||
                float.IsInfinity(RadialAngleStepDegrees) ||
                PathSpacingCells < 1)
            {
                reason = "Editable pattern counts or numeric settings exceed the persisted limits.";
                return false;
            }
            if (!SmartBuildScenePresetCoordinates.IsPortable(PrimaryStep) ||
                !SmartBuildScenePresetCoordinates.IsPortable(SecondaryStep) ||
                !SmartBuildScenePresetCoordinates.IsPortable(RadialPivotOffset) ||
                PathPointOffsets.Any(point => !SmartBuildScenePresetCoordinates.IsPortable(point)) ||
                PathPointOffsets.Length > SmartBuildLimits.MaximumPathControlPoints)
            {
                reason = "Editable pattern coordinates exceed the portable scene limit.";
                return false;
            }
            if (!SmartBuildScenePresetCoordinates.TryAdd(
                    anchor,
                    RadialPivotOffset,
                    out Vector3i radialPivot))
            {
                reason = "The editable radial pivot exceeds the cell coordinate range.";
                return false;
            }

            var pathPoints = new Vector3i[PathPointOffsets.Length];
            for (int index = 0; index < PathPointOffsets.Length; index++)
            {
                if (!SmartBuildScenePresetCoordinates.TryAdd(
                        anchor,
                        PathPointOffsets[index],
                        out pathPoints[index]))
                {
                    reason = "An editable path point exceeds the cell coordinate range.";
                    return false;
                }
            }

            var restored = new SmartBuildPatternDefinition
            {
                Kind = kind,
                PrimaryStep = PrimaryStep.ToCell(),
                SecondaryStep = SecondaryStep.ToCell(),
                PrimaryBefore = PrimaryBefore,
                PrimaryAfter = PrimaryAfter,
                SecondaryBefore = SecondaryBefore,
                SecondaryAfter = SecondaryAfter,
                RadialPivot = radialPivot,
                RadialAxis = radialAxis,
                RadialAngleStepDegrees = RadialAngleStepDegrees,
                RadialOrientation = radialOrientation,
                PathPoints = pathPoints,
                PathMode = pathMode,
                PathOrientation = pathOrientation,
                PathSpacingCells = PathSpacingCells
            };
            if (!restored.TryValidate(sourceCount, out _, out reason))
                return false;

            definition = restored;
            reason = null;
            return true;
        }
    }

    internal struct SmartBuildRegionSpanPresetPayload
    {
        [JsonConstructor]
        internal SmartBuildRegionSpanPresetPayload(
            int yOffset,
            int zOffset,
            int startXOffset,
            int length)
        {
            YOffset = yOffset;
            ZOffset = zOffset;
            StartXOffset = startXOffset;
            Length = length;
        }

        [JsonProperty("y_offset")]
        public int YOffset { get; private set; }

        [JsonProperty("z_offset")]
        public int ZOffset { get; private set; }

        [JsonProperty("start_x_offset")]
        public int StartXOffset { get; private set; }

        [JsonProperty("length")]
        public int Length { get; private set; }
    }

    internal sealed class SmartBuildRegionDefinitionPresetPayload
    {
        [JsonProperty("kind")]
        public string Kind { get; private set; }

        [JsonProperty("spans")]
        public SmartBuildRegionSpanPresetPayload[] Spans { get; private set; } =
            Array.Empty<SmartBuildRegionSpanPresetPayload>();

        [JsonConstructor]
        private SmartBuildRegionDefinitionPresetPayload()
        {
        }

        internal static SmartBuildRegionDefinitionPresetPayload Capture(
            SmartBuildRegionNode region,
            Vector3i anchor)
        {
            if (region == null)
                throw new ArgumentNullException(nameof(region));
            return new SmartBuildRegionDefinitionPresetPayload
            {
                Kind = SmartBuildScenePresetDiscriminators.Region(region.RegionKind),
                Spans = region.Spans
                    .Select(span => new SmartBuildRegionSpanPresetPayload(
                        checked(span.Y - anchor.y),
                        checked(span.Z - anchor.z),
                        checked(span.StartX - anchor.x),
                        span.Length))
                    .ToArray()
            };
        }

        internal bool TryRestore(
            Vector3i anchor,
            out SmartBuildRegionKind kind,
            out IReadOnlyList<Vector3i> cells,
            out string reason)
        {
            cells = Array.Empty<Vector3i>();
            Spans = Spans ?? Array.Empty<SmartBuildRegionSpanPresetPayload>();
            if (!SmartBuildScenePresetDiscriminators.TryRegion(Kind, out kind))
            {
                reason = "An editable region uses an unsupported string discriminator.";
                return false;
            }
            if (Spans.Length == 0 ||
                Spans.Length > SmartBuildLimits.MaximumFloodFillCells)
            {
                reason = "Editable regions must contain bounded non-empty spans.";
                return false;
            }

            long totalCells = 0L;
            var restored = new List<Vector3i>();
            foreach (SmartBuildRegionSpanPresetPayload span in Spans)
            {
                if (span.Length < 1 ||
                    !SmartBuildLimits.IsPortableCoordinate(span.YOffset) ||
                    !SmartBuildLimits.IsPortableCoordinate(span.ZOffset) ||
                    !SmartBuildLimits.IsPortableCoordinate(span.StartXOffset) ||
                    !SmartBuildLimits.TryAddWithinLimit(
                        totalCells,
                        span.Length,
                        SmartBuildLimits.MaximumFloodFillCells,
                        out totalCells))
                {
                    reason = "An editable region span exceeds the persisted region limits.";
                    return false;
                }

                long relativeEnd = (long)span.StartXOffset + span.Length - 1L;
                if (Math.Abs(relativeEnd) > SmartBuildLimits.MaximumCoordinateMagnitude)
                {
                    reason = "An editable region span exceeds the portable coordinate limit.";
                    return false;
                }
                long y = (long)anchor.y + span.YOffset;
                long z = (long)anchor.z + span.ZOffset;
                long startX = (long)anchor.x + span.StartXOffset;
                long endX = startX + span.Length - 1L;
                if (y < int.MinValue || y > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue ||
                    startX < int.MinValue || startX > int.MaxValue ||
                    endX < int.MinValue || endX > int.MaxValue)
                {
                    reason = "An editable region span exceeds the cell coordinate range.";
                    return false;
                }

                for (int offset = 0; offset < span.Length; offset++)
                    restored.Add(new Vector3i((int)startX + offset, (int)y, (int)z));
            }

            cells = restored;
            reason = null;
            return true;
        }
    }

    internal sealed class SmartBuildSceneNodePresetPayload
    {
        [JsonProperty("kind")]
        public string Kind { get; private set; }

        [JsonProperty("sources")]
        public SmartBuildPiecePresetPayload[] Sources { get; private set; } =
            Array.Empty<SmartBuildPiecePresetPayload>();

        [JsonProperty("host_source_index")]
        public int HostSourceIndex { get; private set; }

        [JsonProperty("pattern", NullValueHandling = NullValueHandling.Ignore)]
        public SmartBuildPatternDefinitionPresetPayload Pattern { get; private set; }

        [JsonProperty("region", NullValueHandling = NullValueHandling.Ignore)]
        public SmartBuildRegionDefinitionPresetPayload Region { get; private set; }

        [JsonConstructor]
        private SmartBuildSceneNodePresetPayload()
        {
        }

        internal static SmartBuildSceneNodePresetPayload Primitive(
            SmartBuildPiecePresetPayload source) =>
            new SmartBuildSceneNodePresetPayload
            {
                Kind = "primitive",
                Sources = new[] { source },
                HostSourceIndex = 0
            };

        internal static SmartBuildSceneNodePresetPayload Capture(
            ISmartBuildSceneNode node,
            Vector3i anchor,
            SmartBuildMaterial defaultMaterial)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            SmartBuildPiece[] sources = (node.SourcePieces ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece != null)
                .ToArray();
            int hostIndex = Array.FindIndex(sources, piece => piece.Id == node.Id);
            if (hostIndex < 0)
                throw new InvalidOperationException("A Smart Builder node has no persisted host source.");

            return new SmartBuildSceneNodePresetPayload
            {
                Kind = SmartBuildScenePresetDiscriminators.Node(node.Kind),
                Sources = sources
                    .Select(piece => SmartBuildPiecePresetPayload.Capture(
                        piece,
                        anchor,
                        defaultMaterial))
                    .ToArray(),
                HostSourceIndex = hostIndex,
                Pattern = node is SmartBuildPatternNode pattern
                    ? SmartBuildPatternDefinitionPresetPayload.Capture(pattern.Definition, anchor)
                    : null,
                Region = node is SmartBuildRegionNode region
                    ? SmartBuildRegionDefinitionPresetPayload.Capture(region, anchor)
                    : null
            };
        }

        internal bool TryValidate(
            int nodeIndex,
            out int sourceCount,
            out long estimatedCells,
            out string reason)
        {
            sourceCount = 0;
            estimatedCells = 0L;
            reason = null;
            Sources = Sources ?? Array.Empty<SmartBuildPiecePresetPayload>();
            string prefix = "Node " +
                            (nodeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            ": ";
            if (!SmartBuildScenePresetDiscriminators.TryNode(
                    Kind,
                    out SmartBuildSceneNodeKind nodeKind) ||
                Sources.Length == 0 ||
                Sources.Length > SmartBuildLimits.MaximumSceneNodes ||
                Sources.Any(source => source == null) ||
                HostSourceIndex < 0 ||
                HostSourceIndex >= Sources.Length)
            {
                reason = prefix + "contains invalid node metadata or primitive sources.";
                return false;
            }

            for (int index = 0; index < Sources.Length; index++)
            {
                if (!Sources[index].TryValidate(index, out reason) ||
                    !Sources[index].TryEstimateEnumerationCells(
                        index,
                        out long sourceCells,
                        out reason))
                {
                    reason = prefix + reason;
                    return false;
                }
                if (!SmartBuildLimits.TryAddWithinLimit(
                        estimatedCells,
                        sourceCells,
                        SmartBuildLimits.MaximumPersistedSceneEnumerationCells,
                        out estimatedCells))
                {
                    reason = prefix + "primitive sources exceed the persisted-scene cell limit.";
                    return false;
                }
            }

            switch (nodeKind)
            {
                case SmartBuildSceneNodeKind.Primitive:
                    if (Sources.Length != 1 || Pattern != null || Region != null)
                    {
                        reason = prefix + "a primitive node must contain exactly one source and no definition.";
                        return false;
                    }
                    break;

                case SmartBuildSceneNodeKind.Pattern:
                    if (Pattern == null || Region != null ||
                        !Pattern.TryRestore(
                            new Vector3i(0, 0, 0),
                            Sources.Length,
                            out SmartBuildPatternDefinition patternDefinition,
                            out reason))
                    {
                        reason = prefix + (reason ?? "has no valid editable pattern definition.");
                        return false;
                    }
                    if (!patternDefinition.TryValidate(
                            Sources.Length,
                            out int instanceCount,
                            out reason) ||
                        !SmartBuildLimits.TryMultiplyWithinLimit(
                            estimatedCells,
                            instanceCount,
                            SmartBuildLimits.MaximumPersistedSceneEnumerationCells,
                            out estimatedCells))
                    {
                        reason = prefix +
                                 (reason ??
                                  "expanded pattern cells exceed the persisted-scene cell limit.");
                        return false;
                    }
                    break;

                case SmartBuildSceneNodeKind.Region:
                    SmartBuildPiecePresetPayload host = Sources[HostSourceIndex];
                    Vector3i hostSize = host.CuboidSize.ToCell();
                    if (Sources.Length != 1 || Pattern != null || Region == null ||
                        host.ShapeKind != SmartBuildShapeKind.Cuboid ||
                        hostSize.x != 1 || hostSize.y != 1 || hostSize.z != 1 ||
                        !Region.TryRestore(
                            new Vector3i(0, 0, 0),
                            out _,
                            out IReadOnlyList<Vector3i> regionCells,
                            out reason) ||
                        !regionCells.Any(cell => cell.Equals(host.OriginOffset.ToCell())))
                    {
                        reason = prefix + (reason ?? "has no valid editable region host or spans.");
                        return false;
                    }
                    estimatedCells = regionCells.Count;
                    break;
            }

            sourceCount = Sources.Length;
            reason = null;
            return true;
        }

        internal bool TryGetKind(out SmartBuildSceneNodeKind kind) =>
            SmartBuildScenePresetDiscriminators.TryNode(Kind, out kind);
    }

    /// <summary>
    /// Versioned, portable Smart Builder scene payload used by named presets and recovery.
    /// </summary>
    internal sealed class SmartBuildScenePresetPayload
    {
        internal const int LegacyPrimitiveSchemaVersion = 1;
        internal const int CurrentSchemaVersion = 2;
        internal const int MaximumPieces = SmartBuildLimits.MaximumSceneNodes;

        [JsonProperty("schema_version")]
        public int SchemaVersion { get; private set; } = CurrentSchemaVersion;

        [JsonProperty("anchor")]
        public SmartBuildPresetCell Anchor { get; private set; }

        [JsonProperty("pieces")]
        public SmartBuildPiecePresetPayload[] Pieces { get; private set; } =
            Array.Empty<SmartBuildPiecePresetPayload>();

        [JsonProperty("nodes")]
        public SmartBuildSceneNodePresetPayload[] Nodes { get; private set; } =
            Array.Empty<SmartBuildSceneNodePresetPayload>();

        [JsonProperty("selected_indexes")]
        public int[] SelectedIndexes { get; private set; } = Array.Empty<int>();

        [JsonProperty("primary_index")]
        public int PrimaryIndex { get; private set; } = -1;

        [JsonProperty("active_material")]
        public SmartBuildMaterial ActiveMaterial { get; private set; }

        [JsonProperty("tool")]
        public SmartBuildTool Tool { get; private set; }

        [JsonProperty("selected_shape")]
        public SmartBuildShapeKind SelectedShape { get; private set; }

        [JsonProperty("selected_descriptor")]
        public string SelectedShapeDescriptorKey { get; private set; }

        [JsonProperty("selected_length")]
        public int SelectedSlopeLength { get; private set; }

        [JsonProperty("draw_plane")]
        public SmartBuildDrawPlane DrawPlane { get; private set; }

        [JsonProperty("handle_mode")]
        public SmartBuildEditHandleMode EditHandleMode { get; private set; }

        [JsonProperty("default_support")]
        public SmartBuildSlopeSupportMode DefaultSlopeSupportMode { get; private set; }

        [JsonProperty("preview_mode")]
        public SmartBuildPreviewMode PreviewMode { get; private set; }

        [JsonProperty("occupancy_mode")]
        public SmartBuildOccupancyMode OccupancyMode { get; private set; }

        [JsonConstructor]
        private SmartBuildScenePresetPayload()
        {
        }

        public bool ShouldSerializePieces() =>
            SchemaVersion == LegacyPrimitiveSchemaVersion &&
            Pieces != null &&
            Pieces.Length > 0;

        public bool ShouldSerializeNodes() =>
            SchemaVersion >= CurrentSchemaVersion &&
            Nodes != null &&
            Nodes.Length > 0;

        internal static SmartBuildScenePresetPayload Capture(
            SmartBuildPieceScene scene,
            SmartBuildMaterial activeMaterial,
            SmartBuildTool tool,
            SmartBuildShapeKind selectedShape,
            string selectedShapeDescriptorKey,
            int selectedSlopeLength,
            SmartBuildDrawPlane drawPlane,
            SmartBuildEditHandleMode editHandleMode,
            SmartBuildSlopeSupportMode defaultSlopeSupportMode,
            SmartBuildPreviewMode previewMode,
            SmartBuildOccupancyMode occupancyMode)
        {
            ISmartBuildSceneNode[] nodes = scene?.Nodes?
                .Where(node => node?.HostPiece != null)
                .ToArray() ?? Array.Empty<ISmartBuildSceneNode>();
            foreach (ISmartBuildSceneNode node in nodes)
            {
                if (node.Kind != SmartBuildSceneNodeKind.Primitive &&
                    !node.TryExpand(
                        new SmartBuildExpansionBudget(),
                        out _,
                        out string synchronizationReason))
                {
                    // Synchronizes host translation/source edits before their
                    // portable definitions and spans are captured.
                    throw new InvalidOperationException(
                        "A Smart Builder editable node could not be synchronized for persistence: " +
                        synchronizationReason);
                }
            }

            SmartBuildPiece[] hosts = nodes.Select(node => node.HostPiece).ToArray();
            Vector3i anchor = hosts.Length == 0
                ? new Vector3i(0, 0, 0)
                : new Vector3i(
                    hosts.Min(piece => piece.Origin.x),
                    hosts.Min(piece => piece.Origin.y),
                    hosts.Min(piece => piece.Origin.z));
            var indexById = hosts
                .Select((piece, index) => new { piece.Id, Index = index })
                .ToDictionary(pair => pair.Id, pair => pair.Index);
            int[] selectedIndexes = (scene?.SelectedPieceIds ?? Array.Empty<int>())
                .Where(indexById.ContainsKey)
                .Select(id => indexById[id])
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            int primaryIndex = scene?.SelectedPiece != null &&
                               indexById.TryGetValue(scene.SelectedPiece.Id, out int selectedIndex)
                ? selectedIndex
                : -1;

            return new SmartBuildScenePresetPayload
            {
                SchemaVersion = CurrentSchemaVersion,
                Anchor = SmartBuildPresetCell.From(anchor),
                Pieces = Array.Empty<SmartBuildPiecePresetPayload>(),
                Nodes = nodes
                    .Select(node => SmartBuildSceneNodePresetPayload.Capture(
                        node,
                        anchor,
                        activeMaterial))
                    .ToArray(),
                SelectedIndexes = selectedIndexes,
                PrimaryIndex = primaryIndex,
                ActiveMaterial = activeMaterial,
                Tool = tool,
                SelectedShape = selectedShape,
                SelectedShapeDescriptorKey = string.IsNullOrWhiteSpace(selectedShapeDescriptorKey)
                    ? SmartBuildShapeDescriptors.CuboidKey
                    : selectedShapeDescriptorKey,
                SelectedSlopeLength = selectedSlopeLength,
                DrawPlane = drawPlane,
                EditHandleMode = editHandleMode,
                DefaultSlopeSupportMode = defaultSlopeSupportMode,
                PreviewMode = previewMode,
                OccupancyMode = occupancyMode
            };
        }

        internal bool TryRestore(
            AllConstruct construct,
            Vector3i? anchorOverride,
            out IReadOnlyList<SmartBuildPiece> restoredPieces,
            out IReadOnlyList<int> selectedPieceIds,
            out int primaryPieceId,
            out string reason)
        {
            restoredPieces = Array.Empty<SmartBuildPiece>();
            selectedPieceIds = Array.Empty<int>();
            primaryPieceId = -1;
            if (!TryRestoreScene(
                    construct,
                    anchorOverride,
                    out SmartBuildPieceScene scene,
                    out selectedPieceIds,
                    out primaryPieceId,
                    out reason))
            {
                return false;
            }

            restoredPieces = scene.Pieces.ToArray();
            return true;
        }

        internal bool TryRestoreScene(
            AllConstruct construct,
            Vector3i? anchorOverride,
            out SmartBuildPieceScene restoredScene,
            out IReadOnlyList<int> selectedPieceIds,
            out int primaryPieceId,
            out string reason)
        {
            restoredScene = null;
            selectedPieceIds = Array.Empty<int>();
            primaryPieceId = -1;
            if (construct == null)
            {
                reason = "Focus a construct before loading a Smart Builder scene.";
                return false;
            }
            if (!TryValidate(out reason))
                return false;

            Vector3i anchor = anchorOverride ?? Anchor.ToCell();
            var scene = new SmartBuildPieceScene(construct);
            var hostIds = new List<int>(Nodes.Length);
            try
            {
                for (int nodeIndex = 0; nodeIndex < Nodes.Length; nodeIndex++)
                {
                    SmartBuildSceneNodePresetPayload nodePayload = Nodes[nodeIndex];
                    if (!nodePayload.TryGetKind(out SmartBuildSceneNodeKind nodeKind))
                    {
                        reason = "A Smart Builder node discriminator changed during restore.";
                        return false;
                    }

                    SmartBuildPiece[] sources = RestoreSources(
                        construct,
                        anchor,
                        nodePayload.Sources);
                    SmartBuildPiece host = sources[nodePayload.HostSourceIndex];
                    switch (nodeKind)
                    {
                        case SmartBuildSceneNodeKind.Primitive:
                            if (!scene.Add(host))
                            {
                                reason = "The restored primitive exceeds the Smart Builder scene cap.";
                                return false;
                            }
                            break;

                        case SmartBuildSceneNodeKind.Pattern:
                            foreach (SmartBuildPiece source in sources)
                            {
                                if (!scene.Add(source))
                                {
                                    reason = "The restored pattern sources exceed the Smart Builder scene cap.";
                                    return false;
                                }
                            }
                            scene.SetSelection(sources.Select(piece => piece.Id), host.Id);
                            if (!nodePayload.Pattern.TryRestore(
                                    anchor,
                                    sources.Length,
                                    out SmartBuildPatternDefinition definition,
                                    out reason) ||
                                !scene.TryCreatePatternFromSelection(
                                    definition,
                                    out _,
                                    out reason))
                            {
                                reason = "Smart Builder pattern restore failed: " + reason;
                                return false;
                            }
                            break;

                        case SmartBuildSceneNodeKind.Region:
                            if (!scene.Add(host))
                            {
                                reason = "The restored region host exceeds the Smart Builder scene cap.";
                                return false;
                            }
                            scene.Select(host.Id);
                            if (!nodePayload.Region.TryRestore(
                                    anchor,
                                    out SmartBuildRegionKind regionKind,
                                    out IReadOnlyList<Vector3i> regionCells,
                                    out reason) ||
                                !scene.TryCreateRegionFromSelection(
                                    regionKind,
                                    regionCells,
                                    out _,
                                    out reason))
                            {
                                reason = "Smart Builder region restore failed: " + reason;
                                return false;
                            }
                            break;
                    }

                    hostIds.Add(host.Id);
                }
            }
            catch (Exception exception)
            {
                reason = "Smart Builder scene could not be restored: " + exception.Message;
                return false;
            }

            int[] selected = SelectedIndexes
                .Select(index => hostIds[index])
                .Distinct()
                .ToArray();
            int primary = PrimaryIndex >= 0
                ? hostIds[PrimaryIndex]
                : selected.Length == 0
                    ? -1
                    : selected[selected.Length - 1];
            scene.SetSelection(selected, primary);
            restoredScene = scene;
            selectedPieceIds = selected;
            primaryPieceId = primary;
            reason = null;
            return true;
        }

        internal bool TryValidate(out string reason)
        {
            Pieces = Pieces ?? Array.Empty<SmartBuildPiecePresetPayload>();
            Nodes = Nodes ?? Array.Empty<SmartBuildSceneNodePresetPayload>();
            SelectedIndexes = SelectedIndexes ?? Array.Empty<int>();
            if (SchemaVersion != LegacyPrimitiveSchemaVersion &&
                SchemaVersion != CurrentSchemaVersion)
            {
                reason = "Smart Builder scene schema " +
                         SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                         " is not supported.";
                return false;
            }
            if (SchemaVersion == LegacyPrimitiveSchemaVersion)
            {
                if (!TryMigrateLegacyPieces(out reason))
                    return false;
            }
            else if (Pieces.Length != 0)
            {
                reason = "Schema-v2 Smart Builder scenes cannot contain legacy primitive pieces.";
                return false;
            }

            if (Nodes.Length == 0 ||
                Nodes.Length > MaximumPieces ||
                Nodes.Any(node => node == null))
            {
                reason = "Smart Builder scenes must contain 1 through " +
                         MaximumPieces.ToString(CultureInfo.InvariantCulture) + " nodes.";
                return false;
            }

            int aggregatePrimitiveSources = 0;
            long aggregateEnumerationCells = 0L;
            for (int index = 0; index < Nodes.Length; index++)
            {
                if (!Nodes[index].TryValidate(
                        index,
                        out int sourceCount,
                        out long nodeEnumerationCells,
                        out reason))
                {
                    return false;
                }
                if ((long)aggregatePrimitiveSources + sourceCount > MaximumPieces)
                {
                    reason = "Smart Builder scene nodes contain more than " +
                             MaximumPieces.ToString(CultureInfo.InvariantCulture) +
                             " explicit primitive sources.";
                    return false;
                }
                aggregatePrimitiveSources += sourceCount;
                if (!SmartBuildLimits.TryAddWithinLimit(
                        aggregateEnumerationCells,
                        nodeEnumerationCells,
                        SmartBuildLimits.MaximumPersistedSceneEnumerationCells,
                        out aggregateEnumerationCells))
                {
                    reason =
                        "Smart Builder scene estimated cell enumeration exceeds the " +
                        SmartBuildLimits.MaximumPersistedSceneEnumerationCells
                            .ToString("N0", CultureInfo.InvariantCulture) +
                        "-cell safety limit.";
                    return false;
                }
            }

            if (SelectedIndexes.Any(index => index < 0 || index >= Nodes.Length) ||
                SelectedIndexes.Distinct().Count() != SelectedIndexes.Length ||
                PrimaryIndex < -1 || PrimaryIndex >= Nodes.Length ||
                (PrimaryIndex >= 0 && !SelectedIndexes.Contains(PrimaryIndex)))
            {
                reason = "Smart Builder scene selection data is invalid.";
                return false;
            }

            if (!Enum.IsDefined(typeof(SmartBuildMaterial), ActiveMaterial) ||
                !Enum.IsDefined(typeof(SmartBuildTool), Tool) ||
                !Enum.IsDefined(typeof(SmartBuildShapeKind), SelectedShape) ||
                !Enum.IsDefined(typeof(SmartBuildDrawPlane), DrawPlane) ||
                !Enum.IsDefined(typeof(SmartBuildEditHandleMode), EditHandleMode) ||
                !Enum.IsDefined(typeof(SmartBuildSlopeSupportMode), DefaultSlopeSupportMode) ||
                !Enum.IsDefined(typeof(SmartBuildPreviewMode), PreviewMode) ||
                !Enum.IsDefined(typeof(SmartBuildOccupancyMode), OccupancyMode) ||
                SelectedSlopeLength < 1 || SelectedSlopeLength > 4 ||
                string.IsNullOrWhiteSpace(SelectedShapeDescriptorKey) ||
                SelectedShapeDescriptorKey.Length > 128)
            {
                reason = "Smart Builder scene editor settings are invalid.";
                return false;
            }

            Vector3i anchor = Anchor.ToCell();
            if (!SmartBuildLimits.IsPortableCoordinate(anchor.x) ||
                !SmartBuildLimits.IsPortableCoordinate(anchor.y) ||
                !SmartBuildLimits.IsPortableCoordinate(anchor.z))
            {
                reason = "Smart Builder scene anchor is outside the portable coordinate limit.";
                return false;
            }

            reason = null;
            return true;
        }

        internal string ContentSignature()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.None);
            return EsuPresetLibrary.HashPayload(json);
        }

        private bool TryMigrateLegacyPieces(out string reason)
        {
            if (Pieces.Length == 0 ||
                Pieces.Length > MaximumPieces ||
                Pieces.Any(piece => piece == null))
            {
                reason = "Legacy Smart Builder scenes must contain 1 through " +
                         MaximumPieces.ToString(CultureInfo.InvariantCulture) +
                         " primitive pieces.";
                return false;
            }
            if (Nodes.Length != 0)
            {
                reason = "Legacy Smart Builder scenes cannot also contain schema-v2 nodes.";
                return false;
            }

            Nodes = Pieces
                .Select(SmartBuildSceneNodePresetPayload.Primitive)
                .ToArray();
            Pieces = Array.Empty<SmartBuildPiecePresetPayload>();
            SchemaVersion = CurrentSchemaVersion;
            reason = null;
            return true;
        }

        private static SmartBuildPiece[] RestoreSources(
            AllConstruct construct,
            Vector3i anchor,
            IEnumerable<SmartBuildPiecePresetPayload> sourcePayloads)
        {
            return (sourcePayloads ?? Array.Empty<SmartBuildPiecePresetPayload>())
                .Select(payload => SmartBuildPiece.RestoreFromPreset(
                    construct,
                    CheckedAdd(anchor, payload.OriginOffset.ToCell()),
                    payload))
                .ToArray();
        }

        private static Vector3i CheckedAdd(Vector3i first, Vector3i second)
        {
            long x = (long)first.x + second.x;
            long y = (long)first.y + second.y;
            long z = (long)first.z + second.z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            {
                throw new InvalidOperationException("A restored piece origin exceeds the cell coordinate range.");
            }

            return new Vector3i((int)x, (int)y, (int)z);
        }
    }
}
