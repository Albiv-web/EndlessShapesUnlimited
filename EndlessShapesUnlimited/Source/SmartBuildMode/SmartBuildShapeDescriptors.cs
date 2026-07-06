using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DecoLimitLifter.DecorationEditMode;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildShapeCategory
    {
        Basic,
        Slopes,
        Corners,
        Wedges,
        Transitions,
        Generated
    }

    internal enum SmartBuildShapeHandedness
    {
        None,
        Left,
        Right
    }

    internal sealed class SmartBuildShapeDescriptor
    {
        internal SmartBuildShapeDescriptor(
            string key,
            SmartBuildShapeKind kind,
            SmartBuildShapeCategory category,
            string label,
            string tooltip,
            bool usesLengthSelector,
            bool proceduralDownSlope = false,
            SmartBuildShapeHandedness handedness = SmartBuildShapeHandedness.None,
            string mirrorKey = null,
            int transitionFrom = 0,
            int transitionTo = 0,
            int generatorSidesPreset = 0)
        {
            Key = key;
            Kind = kind;
            Category = category;
            Label = label;
            Tooltip = tooltip;
            UsesLengthSelector = usesLengthSelector;
            ProceduralDownSlope = proceduralDownSlope;
            Handedness = handedness;
            MirrorKey = mirrorKey;
            TransitionFrom = transitionFrom;
            TransitionTo = transitionTo;
            GeneratorSidesPreset = generatorSidesPreset;
        }

        internal string Key { get; }

        internal SmartBuildShapeKind Kind { get; }

        internal SmartBuildShapeCategory Category { get; }

        internal string Label { get; }

        internal string Tooltip { get; }

        internal bool UsesLengthSelector { get; }

        internal bool ProceduralDownSlope { get; }

        internal SmartBuildShapeHandedness Handedness { get; }

        internal string MirrorKey { get; }

        internal int TransitionFrom { get; }

        internal int TransitionTo { get; }

        internal int GeneratorSidesPreset { get; }

        internal bool IsCuboid => Kind == SmartBuildShapeKind.Cuboid;

        internal bool IsGenerator =>
            Kind == SmartBuildShapeKind.GeneratedCircle ||
            Kind == SmartBuildShapeKind.GeneratedPolygon ||
            Kind == SmartBuildShapeKind.GeneratedSphere;

        internal bool IsFixedGeometry => !IsCuboid && !ProceduralDownSlope && !IsGenerator;

        internal SmartBuildShapeDescriptor MirrorDescriptor() =>
            string.IsNullOrWhiteSpace(MirrorKey)
                ? this
                : SmartBuildShapeDescriptors.ByKey(MirrorKey) ?? this;
    }

    internal readonly struct SmartBuildGeometryInfo
    {
        internal SmartBuildGeometryInfo(
            SmartBuildShapeDescriptor descriptor,
            int length,
            string geometryName)
        {
            Descriptor = descriptor;
            Length = Math.Max(1, length);
            GeometryName = geometryName ?? string.Empty;
        }

        internal SmartBuildShapeDescriptor Descriptor { get; }

        internal int Length { get; }

        internal string GeometryName { get; }
    }

    internal static class SmartBuildShapeDescriptors
    {
        internal const string CuboidKey = "cuboid";
        internal const string DownSlopeKey = "down-slope";

        private static readonly SmartBuildShapeDescriptor CuboidDescriptor =
            new SmartBuildShapeDescriptor(
                CuboidKey,
                SmartBuildShapeKind.Cuboid,
                SmartBuildShapeCategory.Basic,
                "Block",
                "Place normal cuboids and beams.",
                usesLengthSelector: false);

        private static readonly SmartBuildShapeDescriptor[] OrderedDescriptors =
        {
            CuboidDescriptor,
            Descriptor("generated-circle", SmartBuildShapeKind.GeneratedCircle, SmartBuildShapeCategory.Generated, "Circle", "Generate a block circle or filled disk/cylinder.", false),
            Descriptor("generated-polygon", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Polygon", "Generate a regular n-sided block polygon.", false, generatorSidesPreset: 8),
            Descriptor("generated-triangle", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Triangle", "Generate a 3-sided block polygon preset.", false, generatorSidesPreset: 3),
            Descriptor("generated-square", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Square", "Generate a 4-sided block polygon preset.", false, generatorSidesPreset: 4),
            Descriptor("generated-hex", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Hex", "Generate a 6-sided block polygon preset.", false, generatorSidesPreset: 6),
            Descriptor("generated-octagon", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Octagon", "Generate an 8-sided block polygon preset.", false, generatorSidesPreset: 8),
            Descriptor("generated-decagon", SmartBuildShapeKind.GeneratedPolygon, SmartBuildShapeCategory.Generated, "Decagon", "Generate a 10-sided block polygon preset.", false, generatorSidesPreset: 10),
            Descriptor("generated-sphere", SmartBuildShapeKind.GeneratedSphere, SmartBuildShapeCategory.Generated, "Sphere", "Generate a block sphere shell or solid sphere.", false),
            Descriptor(DownSlopeKey, SmartBuildShapeKind.DownSlope, SmartBuildShapeCategory.Slopes, "Slope", "Place 1m to 4m down-slope ramp segments.", true, proceduralDownSlope: true),
            Descriptor("pole", SmartBuildShapeKind.Pole, SmartBuildShapeCategory.Basic, "Pole", "Place 1m to 4m rounded pole beams.", true),
            Descriptor("facing-down-slope-left", SmartBuildShapeKind.FacingDownSlope, SmartBuildShapeCategory.Slopes, "Beam slope L", "Place left-handed diagonal beam-slope blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "facing-down-slope-right"),
            Descriptor("facing-down-slope-right", SmartBuildShapeKind.FacingDownSlope, SmartBuildShapeCategory.Slopes, "Beam slope R", "Place right-handed diagonal beam-slope blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "facing-down-slope-left"),
            Descriptor("offset-slope-left", SmartBuildShapeKind.OffsetSlope, SmartBuildShapeCategory.Slopes, "Offset slope L", "Place left-handed offset slope blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "offset-slope-right"),
            Descriptor("offset-slope-right", SmartBuildShapeKind.OffsetSlope, SmartBuildShapeCategory.Slopes, "Offset slope R", "Place right-handed offset slope blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "offset-slope-left"),
            Descriptor("wedge", SmartBuildShapeKind.Wedge, SmartBuildShapeCategory.Wedges, "Wedge", "Place centered wedge blocks.", true),
            Descriptor("wedge-front", SmartBuildShapeKind.WedgeFront, SmartBuildShapeCategory.Wedges, "Wedge front", "Place wedge-front cap blocks.", true),
            Descriptor("wedge-back", SmartBuildShapeKind.WedgeBack, SmartBuildShapeCategory.Wedges, "Wedge back", "Place wedge-back cap blocks.", true),
            Descriptor("square-corner-left", SmartBuildShapeKind.SquareCorner, SmartBuildShapeCategory.Corners, "Square corner L", "Place left-handed square corner blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "square-corner-right"),
            Descriptor("square-corner-right", SmartBuildShapeKind.SquareCorner, SmartBuildShapeCategory.Corners, "Square corner R", "Place right-handed square corner blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "square-corner-left"),
            Descriptor("square-backed-corner-left", SmartBuildShapeKind.SquareBackedCorner, SmartBuildShapeCategory.Corners, "Square-backed L", "Place left-handed square-backed corner blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "square-backed-corner-right"),
            Descriptor("square-backed-corner-right", SmartBuildShapeKind.SquareBackedCorner, SmartBuildShapeCategory.Corners, "Square-backed R", "Place right-handed square-backed corner blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "square-backed-corner-left"),
            Descriptor("triangle-corner-left", SmartBuildShapeKind.TriangleCorner, SmartBuildShapeCategory.Corners, "Triangle corner L", "Place left-handed triangle corner blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "triangle-corner-right"),
            Descriptor("triangle-corner-right", SmartBuildShapeKind.TriangleCorner, SmartBuildShapeCategory.Corners, "Triangle corner R", "Place right-handed triangle corner blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "triangle-corner-left"),
            Descriptor("inverted-triangle-corner-left", SmartBuildShapeKind.InvertedTriangleCorner, SmartBuildShapeCategory.Corners, "Inverted L", "Place left-handed inverted triangle corner blocks.", true, handedness: SmartBuildShapeHandedness.Left, mirrorKey: "inverted-triangle-corner-right"),
            Descriptor("inverted-triangle-corner-right", SmartBuildShapeKind.InvertedTriangleCorner, SmartBuildShapeCategory.Corners, "Inverted R", "Place right-handed inverted triangle corner blocks.", true, handedness: SmartBuildShapeHandedness.Right, mirrorKey: "inverted-triangle-corner-left"),
            Transition("slope-transition-1-2-left", "1-2 transition L", false, 1, 2, SmartBuildShapeHandedness.Left, "slope-transition-1-2-right"),
            Transition("slope-transition-1-2-right", "1-2 transition R", false, 1, 2, SmartBuildShapeHandedness.Right, "slope-transition-1-2-left"),
            Transition("slope-transition-1-3-left", "1-3 transition L", false, 1, 3, SmartBuildShapeHandedness.Left, "slope-transition-1-3-right"),
            Transition("slope-transition-1-3-right", "1-3 transition R", false, 1, 3, SmartBuildShapeHandedness.Right, "slope-transition-1-3-left"),
            Transition("slope-transition-1-4-left", "1-4 transition L", false, 1, 4, SmartBuildShapeHandedness.Left, "slope-transition-1-4-right"),
            Transition("slope-transition-1-4-right", "1-4 transition R", false, 1, 4, SmartBuildShapeHandedness.Right, "slope-transition-1-4-left"),
            Transition("slope-transition-2-3-left", "2-3 transition L", false, 2, 3, SmartBuildShapeHandedness.Left, "slope-transition-2-3-right"),
            Transition("slope-transition-2-3-right", "2-3 transition R", false, 2, 3, SmartBuildShapeHandedness.Right, "slope-transition-2-3-left"),
            Transition("slope-transition-2-4-left", "2-4 transition L", false, 2, 4, SmartBuildShapeHandedness.Left, "slope-transition-2-4-right"),
            Transition("slope-transition-2-4-right", "2-4 transition R", false, 2, 4, SmartBuildShapeHandedness.Right, "slope-transition-2-4-left"),
            Transition("slope-transition-3-4-left", "3-4 transition L", false, 3, 4, SmartBuildShapeHandedness.Left, "slope-transition-3-4-right"),
            Transition("slope-transition-3-4-right", "3-4 transition R", false, 3, 4, SmartBuildShapeHandedness.Right, "slope-transition-3-4-left"),
            Transition("slope-inverse-transition-1-2-left", "1-2 inverse L", true, 1, 2, SmartBuildShapeHandedness.Left, "slope-inverse-transition-1-2-right"),
            Transition("slope-inverse-transition-1-2-right", "1-2 inverse R", true, 1, 2, SmartBuildShapeHandedness.Right, "slope-inverse-transition-1-2-left"),
            Transition("slope-inverse-transition-1-3-left", "1-3 inverse L", true, 1, 3, SmartBuildShapeHandedness.Left, "slope-inverse-transition-1-3-right"),
            Transition("slope-inverse-transition-1-3-right", "1-3 inverse R", true, 1, 3, SmartBuildShapeHandedness.Right, "slope-inverse-transition-1-3-left"),
            Transition("slope-inverse-transition-1-4-left", "1-4 inverse L", true, 1, 4, SmartBuildShapeHandedness.Left, "slope-inverse-transition-1-4-right"),
            Transition("slope-inverse-transition-1-4-right", "1-4 inverse R", true, 1, 4, SmartBuildShapeHandedness.Right, "slope-inverse-transition-1-4-left"),
            Transition("slope-inverse-transition-2-3-left", "2-3 inverse L", true, 2, 3, SmartBuildShapeHandedness.Left, "slope-inverse-transition-2-3-right"),
            Transition("slope-inverse-transition-2-3-right", "2-3 inverse R", true, 2, 3, SmartBuildShapeHandedness.Right, "slope-inverse-transition-2-3-left"),
            Transition("slope-inverse-transition-2-4-left", "2-4 inverse L", true, 2, 4, SmartBuildShapeHandedness.Left, "slope-inverse-transition-2-4-right"),
            Transition("slope-inverse-transition-2-4-right", "2-4 inverse R", true, 2, 4, SmartBuildShapeHandedness.Right, "slope-inverse-transition-2-4-left"),
            Transition("slope-inverse-transition-3-4-left", "3-4 inverse L", true, 3, 4, SmartBuildShapeHandedness.Left, "slope-inverse-transition-3-4-right"),
            Transition("slope-inverse-transition-3-4-right", "3-4 inverse R", true, 3, 4, SmartBuildShapeHandedness.Right, "slope-inverse-transition-3-4-left")
        };

        private static readonly Dictionary<string, SmartBuildShapeDescriptor> ByDescriptorKey =
            OrderedDescriptors.ToDictionary(descriptor => descriptor.Key, StringComparer.OrdinalIgnoreCase);

        internal static IReadOnlyList<SmartBuildShapeDescriptor> All => OrderedDescriptors;

        internal static SmartBuildShapeDescriptor Cuboid => CuboidDescriptor;

        internal static SmartBuildShapeDescriptor DownSlope => ByKey(DownSlopeKey);

        internal static SmartBuildShapeDescriptor ByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return ByDescriptorKey.TryGetValue(key, out SmartBuildShapeDescriptor descriptor)
                ? descriptor
                : null;
        }

        internal static bool TryParseGeometry(
            object geometry,
            out SmartBuildGeometryInfo info)
        {
            return TryParseGeometryName(geometry?.ToString(), out info);
        }

        internal static bool TryParseGeometryName(
            string geometryName,
            out SmartBuildGeometryInfo info)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(geometryName) ||
                geometryName.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string name = geometryName.Trim();
            if (TryParseSimpleLength(name, "Cuboid", "m", out int cuboidLength))
                return Parsed(CuboidKey, cuboidLength, name, out info);
            if (TryParseSimpleLength(name, "Pole", "m", out int poleLength))
                return Parsed("pole", poleLength, name, out info);
            if (TryParseSimpleLength(name, "DownSlope", "m", out int downSlopeLength))
                return Parsed(DownSlopeKey, downSlopeLength, name, out info);
            if (TryParseSimpleLength(name, "Wedge", "m", out int wedgeLength))
                return Parsed("wedge", wedgeLength, name, out info);
            if (TryParseAffixedLength(name, "Wedge", "mFront", out int wedgeFrontLength))
                return Parsed("wedge-front", wedgeFrontLength, name, out info);
            if (TryParseAffixedLength(name, "Wedge", "mBack", out int wedgeBackLength))
                return Parsed("wedge-back", wedgeBackLength, name, out info);
            if (TryParseAffixedLength(name, "LeftSquareCorner", "m", out int leftSquareLength))
                return Parsed("square-corner-left", leftSquareLength, name, out info);
            if (TryParseAffixedLength(name, "RightSquareCorner", "m", out int rightSquareLength))
                return Parsed("square-corner-right", rightSquareLength, name, out info);
            if (TryParseAffixedLength(name, "SquareBacked", "mCornerLeft", out int backedLeftLength))
                return Parsed("square-backed-corner-left", backedLeftLength, name, out info);
            if (TryParseAffixedLength(name, "SquareBacked", "mCornerRight", out int backedRightLength))
                return Parsed("square-backed-corner-right", backedRightLength, name, out info);
            if (TryParseAffixedLength(name, "LeftTriangleCorner", "m", out int leftTriangleLength))
                return Parsed("triangle-corner-left", leftTriangleLength, name, out info);
            if (TryParseAffixedLength(name, "RightTriangleCorner", "m", out int rightTriangleLength))
                return Parsed("triangle-corner-right", rightTriangleLength, name, out info);
            if (TryParseAffixedLength(name, "LeftInvertedTriangleCorner", "m", out int leftInvertedLength))
                return Parsed("inverted-triangle-corner-left", leftInvertedLength, name, out info);
            if (TryParseAffixedLength(name, "RightInvertedTriangleCorner", "m", out int rightInvertedLength))
                return Parsed("inverted-triangle-corner-right", rightInvertedLength, name, out info);
            if (TryParseAffixedLength(name, "LeftFacingDownSlope1m_", "mLong", out int leftFacingLength))
                return Parsed("facing-down-slope-left", leftFacingLength, name, out info);
            if (TryParseAffixedLength(name, "RightFacingDownSlope1m_", "mLong", out int rightFacingLength))
                return Parsed("facing-down-slope-right", rightFacingLength, name, out info);
            if (TryParseAffixedLength(name, "Offset", "mSlopeLeft", out int leftOffsetLength))
                return Parsed("offset-slope-left", leftOffsetLength, name, out info);
            if (TryParseAffixedLength(name, "Offset", "mSlopeRight", out int rightOffsetLength))
                return Parsed("offset-slope-right", rightOffsetLength, name, out info);
            if (TryParseTransition(name, inverse: false, out string transitionKey, out int transitionLength))
                return Parsed(transitionKey, transitionLength, name, out info);
            if (TryParseTransition(name, inverse: true, out string inverseKey, out int inverseLength))
                return Parsed(inverseKey, inverseLength, name, out info);

            return false;
        }

        internal static bool IsOddMirror(SmartBuildShapeDescriptor descriptor, IEnumerable<DecorationEditAxis> axes)
        {
            if (descriptor == null ||
                descriptor.Handedness == SmartBuildShapeHandedness.None ||
                string.IsNullOrWhiteSpace(descriptor.MirrorKey))
            {
                return false;
            }

            return axes != null && axes.Count() % 2 == 1;
        }

        private static SmartBuildShapeDescriptor Descriptor(
            string key,
            SmartBuildShapeKind kind,
            SmartBuildShapeCategory category,
            string label,
            string tooltip,
            bool usesLengthSelector,
            bool proceduralDownSlope = false,
            SmartBuildShapeHandedness handedness = SmartBuildShapeHandedness.None,
            string mirrorKey = null,
            int generatorSidesPreset = 0) =>
            new SmartBuildShapeDescriptor(
                key,
                kind,
                category,
                label,
                tooltip,
                usesLengthSelector,
                proceduralDownSlope,
                handedness,
                mirrorKey,
                generatorSidesPreset: generatorSidesPreset);

        private static SmartBuildShapeDescriptor Transition(
            string key,
            string label,
            bool inverse,
            int from,
            int to,
            SmartBuildShapeHandedness handedness,
            string mirrorKey) =>
            new SmartBuildShapeDescriptor(
                key,
                inverse ? SmartBuildShapeKind.SlopeInverseTransition : SmartBuildShapeKind.SlopeTransition,
                SmartBuildShapeCategory.Transitions,
                label,
                inverse
                    ? "Place inverse slope transition blocks."
                    : "Place slope transition blocks.",
                usesLengthSelector: false,
                handedness: handedness,
                mirrorKey: mirrorKey,
                transitionFrom: from,
                transitionTo: to);

        private static bool Parsed(
            string descriptorKey,
            int length,
            string geometryName,
            out SmartBuildGeometryInfo info)
        {
            SmartBuildShapeDescriptor descriptor = ByKey(descriptorKey);
            if (descriptor == null)
            {
                info = default;
                return false;
            }

            info = new SmartBuildGeometryInfo(descriptor, length, geometryName);
            return true;
        }

        private static bool TryParseSimpleLength(
            string name,
            string prefix,
            string suffix,
            out int length) =>
            TryParseAffixedLength(name, prefix, suffix, out length);

        private static bool TryParseAffixedLength(
            string name,
            string prefix,
            string suffix,
            out int length)
        {
            length = 0;
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string number = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) &&
                   length >= 1 &&
                   length <= 4;
        }

        private static bool TryParseTransition(
            string name,
            bool inverse,
            out string key,
            out int length)
        {
            key = null;
            length = 0;
            const string prefix = "Slope";
            string middle = inverse ? "mTo" : "mTo";
            string leftSuffix = inverse ? "mInverseTransitionLeft" : "mTransitionLeft";
            string rightSuffix = inverse ? "mInverseTransitionRight" : "mTransitionRight";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix;
            string handed;
            if (name.EndsWith(leftSuffix, StringComparison.OrdinalIgnoreCase))
            {
                suffix = leftSuffix;
                handed = "left";
            }
            else if (name.EndsWith(rightSuffix, StringComparison.OrdinalIgnoreCase))
            {
                suffix = rightSuffix;
                handed = "right";
            }
            else
            {
                return false;
            }

            string core = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            int separator = core.IndexOf(middle, StringComparison.OrdinalIgnoreCase);
            if (separator < 0)
                return false;

            string fromText = core.Substring(0, separator);
            string toText = core.Substring(separator + middle.Length);
            if (!int.TryParse(fromText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int from) ||
                !int.TryParse(toText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int to))
            {
                return false;
            }

            length = Math.Max(from, to);
            string kind = inverse ? "slope-inverse-transition" : "slope-transition";
            key = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-{2}-{3}",
                kind,
                from,
                to,
                handed);
            return ByKey(key) != null;
        }
    }
}
