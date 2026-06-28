using System;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildTool
    {
        Draw,
        Move,
        Scale
    }

    internal enum SmartBuildDrawPlane
    {
        Camera,
        XY,
        XZ,
        YZ
    }

    internal enum SmartBuildOccupancyMode
    {
        SkipOccupied,
        BlockOnOverlap
    }

    internal sealed class SmartBuildDraft
    {
        internal SmartBuildDraft(
            AllConstruct construct,
            Vector3i origin,
            Vector3i size,
            SmartBuildDrawPlane drawPlane)
        {
            Construct = construct;
            Origin = origin;
            Size = ClampSize(size);
            DrawPlane = drawPlane;
        }

        internal AllConstruct Construct { get; }

        internal Vector3i Origin { get; private set; }

        internal Vector3i Size { get; private set; }

        internal SmartBuildDrawPlane DrawPlane { get; set; }

        internal Vector3i Max =>
            new Vector3i(
                Origin.x + Size.x - 1,
                Origin.y + Size.y - 1,
                Origin.z + Size.z - 1);

        internal Vector3 CenterLocal =>
            new Vector3(
                Origin.x + (Size.x - 1) * 0.5f,
                Origin.y + (Size.y - 1) * 0.5f,
                Origin.z + (Size.z - 1) * 0.5f);

        internal string FormatDimensions() =>
            $"{Size.x} x {Size.y} x {Size.z}";

        internal SmartBuildVolume ToVolume() =>
            new SmartBuildVolume(
                Construct,
                Origin,
                SmartBuildAxis.X,
                SmartBuildAxis.Y,
                SmartBuildAxis.Z,
                Size.x,
                Size.y,
                Size.z);

        internal void MoveBy(Vector3i delta)
        {
            Origin += delta;
        }

        internal void SetTransform(Vector3i origin, Vector3i size)
        {
            Origin = origin;
            Size = ClampSize(size);
        }

        internal void ResizeFromHandle(
            DecorationEditAxis axis,
            int sign,
            int delta)
        {
            if (axis == DecorationEditAxis.None ||
                axis == DecorationEditAxis.Free ||
                delta == 0)
            {
                return;
            }

            Vector3i min = Origin;
            Vector3i max = Max;
            SmartBuildAxis smartAxis = ToSmartAxis(axis);
            if (sign >= 0)
            {
                int next = SmartBuildAxisHelper.Get(max, smartAxis) + delta;
                next = Math.Max(next, SmartBuildAxisHelper.Get(min, smartAxis));
                max = SmartBuildAxisHelper.Set(max, smartAxis, next);
            }
            else
            {
                int next = SmartBuildAxisHelper.Get(min, smartAxis) + delta;
                next = Math.Min(next, SmartBuildAxisHelper.Get(max, smartAxis));
                min = SmartBuildAxisHelper.Set(min, smartAxis, next);
            }

            Origin = min;
            Size = new Vector3i(
                max.x - min.x + 1,
                max.y - min.y + 1,
                max.z - min.z + 1);
        }

        internal static SmartBuildDraft CreateSeed(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildDrawPlane drawPlane) =>
            new SmartBuildDraft(
                construct,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane);

        internal static Vector3i ClampSize(Vector3i size) =>
            new Vector3i(
                Math.Max(1, size.x),
                Math.Max(1, size.y),
                Math.Max(1, size.z));

        internal static SmartBuildAxis ToSmartAxis(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return SmartBuildAxis.X;
                case DecorationEditAxis.Y:
                    return SmartBuildAxis.Y;
                default:
                    return SmartBuildAxis.Z;
            }
        }

        internal static DecorationEditAxis ToDecorationAxis(SmartBuildAxis axis)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return DecorationEditAxis.X;
                case SmartBuildAxis.Y:
                    return DecorationEditAxis.Y;
                default:
                    return DecorationEditAxis.Z;
            }
        }
    }
}
