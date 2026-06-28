using System;
using System.IO;
using BrilliantSkies.Core.Help;
using UnityEngine;

namespace EndlessShapes2.Polygon
{
    public static class MADCD_PolygonInput
    {
        public static bool NormalReversal { get; set; }

        public static float FaceThickness { get; set; } = 0.05f;

        public static float LineThickness { get; set; } = 0.05f;

        public static StructureBlockType SBType { get; set; } = StructureBlockType.Metal;

        public static Func<PolygonData, int> ColorSetting { get; set; }

        public static void Start(MimicAndDecorationCommonData madcd, PolygonData polygonData)
        {
            int colorIndex = ColorSetting?.Invoke(polygonData) ?? -1;
            Start(
                madcd,
                polygonData,
                new PolygonDecorationSettings(
                    NormalReversal,
                    FaceThickness,
                    LineThickness,
                    SBType),
                colorIndex);
        }

        internal static void Start(
            MimicAndDecorationCommonData madcd,
            PolygonData polygonData,
            PolygonDecorationSettings settings,
            int colorIndex)
        {
            if (madcd == null)
                throw new ArgumentNullException(nameof(madcd));
            if (polygonData == null)
                throw new ArgumentNullException(nameof(polygonData));
            settings.Validate(polygonData.SourceLine);

            StructureBlockGUID meshGuids = StructureBlockGUID.GetSBGUID(settings.StructureBlockType);
            SideData[] sides = polygonData.Sides;
            Vector3 normalOffset = polygonData.NormalVector *
                                   ((settings.NormalReversal
                                       ? -settings.FaceThickness
                                       : settings.FaceThickness) / 2f);

            Vector3 position;
            Vector3 scale;
            Vector3 angles;

            switch (polygonData.PolyType)
            {
                case PolygonType.RightTriangle:
                    position = sides[0].Midpoint - normalOffset;
                    scale = new Vector3(settings.FaceThickness, sides[2].Length, sides[1].Length);
                    angles = Quaternion.LookRotation(-sides[1].SideVector, sides[2].SideVector).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Slope,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.IsoscelesTriangle:
                    Vector3 apex = sides[1].TargetPosition;
                    Vector3 baseToApex = apex - sides[0].Midpoint;
                    position = (sides[0].Midpoint + apex) / 2f - normalOffset;
                    scale = new Vector3(sides[0].Length, settings.FaceThickness, baseToApex.magnitude);
                    angles = Quaternion.LookRotation(baseToApex, normalOffset).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Wedge,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.OtherTriangle_F:
                    Vector3 forwardFront = sides[0].SideVector;
                    float frontY = sides[1].Length * Mathf.Sin(
                        Vector3.Angle(-forwardFront, sides[1].SideVector) * Mathf.Deg2Rad);
                    float frontZ = PerpendicularComponent(sides[1].Length, frontY, polygonData.SourceLine);
                    position = sides[1].Midpoint - normalOffset;
                    scale = new Vector3(settings.FaceThickness, frontY, frontZ);
                    angles = Quaternion.LookRotation(forwardFront, sides[1].SideVector).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Slope,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.OtherTriangle_B:
                    Vector3 forwardBack = sides[0].SideVector;
                    float backY = sides[1].Length * Mathf.Sin(
                        Vector3.Angle(-forwardBack, sides[1].SideVector) * Mathf.Deg2Rad);
                    float backZ = PerpendicularComponent(sides[2].Length, backY, polygonData.SourceLine);
                    position = sides[2].Midpoint - normalOffset;
                    scale = new Vector3(settings.FaceThickness, backY, backZ);
                    angles = Quaternion.LookRotation(-forwardBack, -sides[2].SideVector).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Slope,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.Rectangle:
                    position = (sides[0].OriginPosition + sides[2].OriginPosition) / 2f - normalOffset;
                    scale = new Vector3(settings.FaceThickness, sides[1].Length, sides[0].Length);
                    angles = Quaternion.LookRotation(sides[0].SideVector, sides[1].SideVector).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Block,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.Ellipse:
                    Vector3 right = sides[8].OriginPosition - sides[0].OriginPosition;
                    Vector3 up = sides[12].OriginPosition - sides[4].OriginPosition;
                    Vector3 forward = Vector3.Cross(right, up);
                    Vector3 center = Vector3.zero;
                    foreach (SideData side in sides)
                        center += side.OriginPosition;
                    center /= 16f;
                    position = center - normalOffset;
                    scale = new Vector3(right.magnitude, up.magnitude, settings.FaceThickness);
                    angles = Quaternion.LookRotation(forward, up).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Pole,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                case PolygonType.Line:
                    position = (sides[0].OriginPosition + sides[0].TargetPosition) / 2f;
                    scale = new Vector3(settings.LineThickness, settings.LineThickness, sides[0].Length);
                    angles = Quaternion.LookRotation(sides[0].SideVector, Vector3.up).eulerAngles;
                    InputDecorationData(
                        madcd,
                        meshGuids.Pole,
                        position,
                        scale,
                        angles,
                        colorIndex,
                        polygonData.SourceLine);
                    break;

                default:
                    throw GeometryError(polygonData.SourceLine, $"unsupported polygon type {polygonData.PolyType}");
            }
        }

        private static void InputDecorationData(
            MimicAndDecorationCommonData data,
            Guid guid,
            Vector3 position,
            Vector3 scale,
            Vector3 angles,
            int colorIndex,
            int sourceLine)
        {
            ValidateGeneratedTransform(position, scale, angles, sourceLine);

            data.MeshGuid = guid;
            data.Positioning = Round(position);
            data.Scaling = Round(scale);
            data.Orientation = Round(angles);
            if (colorIndex >= 0)
                data.ColorIndex = colorIndex;
        }

        internal static void ValidateGeneratedTransform(
            Vector3 position,
            Vector3 scale,
            Vector3 angles,
            int sourceLine)
        {
            EnsureFinite(position, "position", sourceLine);
            EnsureFinite(scale, "scale", sourceLine);
            EnsureFinite(angles, "orientation", sourceLine);
            if (Mathf.Abs(scale.x) < 0.000001f ||
                Mathf.Abs(scale.y) < 0.000001f ||
                Mathf.Abs(scale.z) < 0.000001f)
            {
                throw GeometryError(sourceLine, "generated decoration has a zero scale component");
            }
        }

        private static Vector3 Round(Vector3 value)
        {
            return new Vector3(Rounding.R4(value.x), Rounding.R4(value.y), Rounding.R4(value.z));
        }

        private static float PerpendicularComponent(float length, float component, int sourceLine)
        {
            if (!IsFinite(length) || length <= 0.000001f)
                throw GeometryError(sourceLine, "triangle contains a zero-length side");
            float ratio = Mathf.Clamp(component / length, -1f, 1f);
            return length * Mathf.Sin(Mathf.Acos(ratio));
        }

        private static void EnsureFinite(Vector3 value, string name, int sourceLine)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                throw GeometryError(sourceLine, $"generated decoration {name} is not finite");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static InvalidDataException GeometryError(int sourceLine, string message)
        {
            string prefix = sourceLine > 0 ? $"OBJ line {sourceLine}" : "OBJ geometry";
            return new InvalidDataException($"{prefix}: {message}.");
        }
    }

    internal readonly struct PolygonDecorationSettings
    {
        internal PolygonDecorationSettings(
            bool normalReversal,
            float faceThickness,
            float lineThickness,
            StructureBlockType structureBlockType)
        {
            NormalReversal = normalReversal;
            FaceThickness = faceThickness;
            LineThickness = lineThickness;
            StructureBlockType = structureBlockType;
        }

        internal bool NormalReversal { get; }

        internal float FaceThickness { get; }

        internal float LineThickness { get; }

        internal StructureBlockType StructureBlockType { get; }

        internal void Validate(int sourceLine)
        {
            if (!IsPositiveFinite(FaceThickness))
                throw Error(sourceLine, "face thickness must be finite and greater than zero");
            if (!IsPositiveFinite(LineThickness))
                throw Error(sourceLine, "line thickness must be finite and greater than zero");
            if ((int)StructureBlockType < 0 ||
                (int)StructureBlockType >
                (int)global::EndlessShapes2.Polygon.StructureBlockType.Rubber)
            {
                throw Error(sourceLine, "structure-block material is outside the supported range");
            }
        }

        private static bool IsPositiveFinite(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static InvalidDataException Error(int sourceLine, string message)
        {
            string prefix = sourceLine > 0 ? $"OBJ line {sourceLine}" : "Decoration settings";
            return new InvalidDataException($"{prefix}: {message}.");
        }
    }
}
