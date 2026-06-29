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
                    angles = LookRotationEuler(-sides[1].SideVector, sides[2].SideVector, polygonData.SourceLine);
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
                    angles = LookRotationEuler(baseToApex, normalOffset, polygonData.SourceLine);
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
                    angles = LookRotationEuler(forwardFront, sides[1].SideVector, polygonData.SourceLine);
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
                    angles = LookRotationEuler(-forwardBack, -sides[2].SideVector, polygonData.SourceLine);
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
                    angles = LookRotationEuler(sides[0].SideVector, sides[1].SideVector, polygonData.SourceLine);
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
                    angles = LookRotationEuler(forward, up, polygonData.SourceLine);
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
                    angles = LookRotationEuler(sides[0].SideVector, Vector3.up, polygonData.SourceLine);
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

            Vector3 roundedPosition = Round(position);
            Vector3 roundedScale = Round(scale);
            Vector3 roundedAngles = Round(angles);
            if (data.TrySetStandaloneData(
                    guid,
                    roundedPosition,
                    roundedScale,
                    roundedAngles,
                    colorIndex))
            {
                return;
            }

            data.MeshGuid = guid;
            data.Positioning = roundedPosition;
            data.Scaling = roundedScale;
            data.Orientation = roundedAngles;
            if (colorIndex >= 0)
                data.ColorIndex = colorIndex;
        }

        private static Vector3 LookRotationEuler(Vector3 forward, Vector3 upwards, int sourceLine)
        {
            EnsureFinite(forward, "forward vector", sourceLine);
            EnsureFinite(upwards, "up vector", sourceLine);
            if (forward.sqrMagnitude <= 0.000000000001f)
                throw GeometryError(sourceLine, "generated decoration forward vector has zero length");
            if (upwards.sqrMagnitude <= 0.000000000001f)
                throw GeometryError(sourceLine, "generated decoration up vector has zero length");

            try
            {
                return Quaternion.LookRotation(forward, upwards).eulerAngles;
            }
            catch (Exception exception) when (IsUnityECallUnavailable(exception))
            {
                return ManagedLookRotationEuler(forward, upwards, sourceLine);
            }
        }

        private static Vector3 ManagedLookRotationEuler(Vector3 forward, Vector3 upwards, int sourceLine)
        {
            Vector3 z = Normalize(forward, sourceLine, "forward vector");
            Vector3 x = Cross(upwards, z);
            if (x.sqrMagnitude <= 0.000000000001f)
                throw GeometryError(sourceLine, "generated decoration up vector is parallel to forward");
            x = Normalize(x, sourceLine, "right vector");
            Vector3 y = Cross(z, x);

            double m00 = x.x;
            double m01 = y.x;
            double m02 = z.x;
            double m10 = x.y;
            double m11 = y.y;
            double m12 = z.y;
            double m20 = x.z;
            double m21 = y.z;
            double m22 = z.z;

            double qw;
            double qx;
            double qy;
            double qz;
            double trace = m00 + m11 + m22;
            if (trace > 0d)
            {
                double s = Math.Sqrt(trace + 1d) * 2d;
                qw = 0.25d * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1d + m00 - m11 - m22) * 2d;
                qw = (m21 - m12) / s;
                qx = 0.25d * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1d + m11 - m00 - m22) * 2d;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25d * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1d + m22 - m00 - m11) * 2d;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25d * s;
            }

            double sinX = 2d * (qw * qx + qy * qz);
            double cosX = 1d - 2d * (qx * qx + qy * qy);
            double xDegrees = Math.Atan2(sinX, cosX) * Mathf.Rad2Deg;
            double sinY = 2d * (qw * qy - qz * qx);
            sinY = Math.Max(-1d, Math.Min(1d, sinY));
            double yDegrees = Math.Asin(sinY) * Mathf.Rad2Deg;
            double sinZ = 2d * (qw * qz + qx * qy);
            double cosZ = 1d - 2d * (qy * qy + qz * qz);
            double zDegrees = Math.Atan2(sinZ, cosZ) * Mathf.Rad2Deg;

            return new Vector3(
                NormalizeDegrees((float)xDegrees),
                NormalizeDegrees((float)yDegrees),
                NormalizeDegrees((float)zDegrees));
        }

        private static Vector3 Normalize(Vector3 vector, int sourceLine, string name)
        {
            float magnitude = vector.magnitude;
            if (!IsFinite(magnitude) || magnitude <= 0.000001f)
                throw GeometryError(sourceLine, $"generated decoration {name} has zero length");
            return vector / magnitude;
        }

        private static Vector3 Cross(Vector3 left, Vector3 right) =>
            new Vector3(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);

        private static float NormalizeDegrees(float value)
        {
            if (!IsFinite(value))
                return value;
            value %= 360f;
            if (value < 0f)
                value += 360f;
            return value;
        }

        private static bool IsUnityECallUnavailable(Exception exception) =>
            exception?.Message?.IndexOf("ECall", StringComparison.OrdinalIgnoreCase) >= 0;

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
