using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    internal static class SmartBuildAxisHelper
    {
        internal static SmartBuildAxis FromLargestComponent(Vector3 value, out int sign)
        {
            float ax = Mathf.Abs(value.x);
            float ay = Mathf.Abs(value.y);
            float az = Mathf.Abs(value.z);
            if (ax >= ay && ax >= az)
            {
                sign = value.x >= 0f ? 1 : -1;
                return SmartBuildAxis.X;
            }

            if (ay >= ax && ay >= az)
            {
                sign = value.y >= 0f ? 1 : -1;
                return SmartBuildAxis.Y;
            }

            sign = value.z >= 0f ? 1 : -1;
            return SmartBuildAxis.Z;
        }

        internal static SmartBuildAxis[] PlaneAxes(SmartBuildAxis normal) =>
            new[] { SmartBuildAxis.X, SmartBuildAxis.Y, SmartBuildAxis.Z }
                .Where(axis => axis != normal)
                .ToArray();

        internal static Vector3i ToVector3i(SmartBuildAxis axis, int amount)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return new Vector3i(amount, 0, 0);
                case SmartBuildAxis.Y:
                    return new Vector3i(0, amount, 0);
                default:
                    return new Vector3i(0, 0, amount);
            }
        }

        internal static Vector3 ToVector3(SmartBuildAxis axis)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return Vector3.right;
                case SmartBuildAxis.Y:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        internal static int Get(Vector3i value, SmartBuildAxis axis)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return value.x;
                case SmartBuildAxis.Y:
                    return value.y;
                default:
                    return value.z;
            }
        }

        internal static float Get(Vector3 value, SmartBuildAxis axis)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return value.x;
                case SmartBuildAxis.Y:
                    return value.y;
                default:
                    return value.z;
            }
        }

        internal static Vector3i Set(Vector3i value, SmartBuildAxis axis, int component)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    value.x = component;
                    break;
                case SmartBuildAxis.Y:
                    value.y = component;
                    break;
                default:
                    value.z = component;
                    break;
            }

            return value;
        }
    }

    internal sealed class SmartBuildVolume
    {
        internal SmartBuildVolume(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildAxis axisU,
            SmartBuildAxis axisV,
            SmartBuildAxis axisN,
            int lengthU,
            int lengthV,
            int thickness)
        {
            Construct = construct;
            Origin = origin;
            AxisU = axisU;
            AxisV = axisV;
            AxisN = axisN;
            LengthU = Math.Max(1, lengthU);
            LengthV = Math.Max(1, lengthV);
            Thickness = Math.Max(1, thickness);
            GrainAxis = DetermineGrainAxis();
        }

        internal AllConstruct Construct { get; }

        internal Vector3i Origin { get; }

        internal SmartBuildAxis AxisU { get; }

        internal SmartBuildAxis AxisV { get; }

        internal SmartBuildAxis AxisN { get; }

        internal int LengthU { get; }

        internal int LengthV { get; }

        internal int Thickness { get; }

        internal SmartBuildAxis GrainAxis { get; }

        internal int CellCount => checked(LengthU * LengthV * Thickness);

        internal Vector3i MinCell => Origin;

        internal Vector3i MaxCell =>
            Origin +
            SmartBuildAxisHelper.ToVector3i(AxisU, LengthU - 1) +
            SmartBuildAxisHelper.ToVector3i(AxisV, LengthV - 1) +
            SmartBuildAxisHelper.ToVector3i(AxisN, Thickness - 1);

        internal IEnumerable<Vector3i> EnumerateCells()
        {
            for (int n = 0; n < Thickness; n++)
                for (int v = 0; v < LengthV; v++)
                    for (int u = 0; u < LengthU; u++)
                    {
                        Vector3i cell = Origin;
                        cell += SmartBuildAxisHelper.ToVector3i(AxisU, u);
                        cell += SmartBuildAxisHelper.ToVector3i(AxisV, v);
                        cell += SmartBuildAxisHelper.ToVector3i(AxisN, n);
                        yield return cell;
                    }
        }

        internal string FormatDimensions() =>
            $"{LengthU} x {LengthV} x {Thickness}";

        internal static SmartBuildVolume FromDrag(
            AllConstruct construct,
            Vector3i anchorCell,
            SmartBuildAxis normalAxis,
            int normalSign,
            Vector3 currentLocalHit,
            int thickness)
        {
            SmartBuildAxis[] plane = SmartBuildAxisHelper.PlaneAxes(normalAxis);
            SmartBuildAxis axisU = plane[0];
            SmartBuildAxis axisV = plane[1];
            Vector3i start = anchorCell + SmartBuildAxisHelper.ToVector3i(normalAxis, normalSign);
            Vector3i origin = start;

            int uStart = SmartBuildAxisHelper.Get(start, axisU);
            int vStart = SmartBuildAxisHelper.Get(start, axisV);
            int nStart = SmartBuildAxisHelper.Get(start, normalAxis);
            int uEnd = Mathf.RoundToInt(SmartBuildAxisHelper.Get(currentLocalHit, axisU));
            int vEnd = Mathf.RoundToInt(SmartBuildAxisHelper.Get(currentLocalHit, axisV));

            int uMin = Math.Min(uStart, uEnd);
            int uMax = Math.Max(uStart, uEnd);
            int vMin = Math.Min(vStart, vEnd);
            int vMax = Math.Max(vStart, vEnd);
            int nMin = normalSign >= 0
                ? nStart
                : nStart - Math.Max(1, thickness) + 1;

            origin = SmartBuildAxisHelper.Set(origin, axisU, uMin);
            origin = SmartBuildAxisHelper.Set(origin, axisV, vMin);
            origin = SmartBuildAxisHelper.Set(origin, normalAxis, nMin);

            return new SmartBuildVolume(
                construct,
                origin,
                axisU,
                axisV,
                normalAxis,
                uMax - uMin + 1,
                vMax - vMin + 1,
                thickness);
        }

        private SmartBuildAxis DetermineGrainAxis()
        {
            int best = LengthU;
            SmartBuildAxis axis = AxisU;
            if (LengthV > best)
            {
                best = LengthV;
                axis = AxisV;
            }

            if (Thickness > best)
                axis = AxisN;

            return axis;
        }

        internal Vector3[] GetWorldCorners()
        {
            Vector3[] corners = GetLocalCorners();
            for (int index = 0; index < corners.Length; index++)
                corners[index] = Construct.SafeLocalToGlobal(corners[index]);
            return corners;
        }

        internal Vector3[] GetLocalCorners()
        {
            Vector3i min = MinCell;
            Vector3i max = MaxCell;
            Vector3 localMin = new Vector3(min.x - 0.5f, min.y - 0.5f, min.z - 0.5f);
            Vector3 localMax = new Vector3(max.x + 0.5f, max.y + 0.5f, max.z + 0.5f);
            return new[]
            {
                new Vector3(localMin.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMax.z)
            };
        }

        internal static SmartBuildVolume FromBounds(
            AllConstruct construct,
            Vector3i min,
            Vector3i max)
        {
            Vector3i origin = new Vector3i(
                Math.Min(min.x, max.x),
                Math.Min(min.y, max.y),
                Math.Min(min.z, max.z));
            Vector3i end = new Vector3i(
                Math.Max(min.x, max.x),
                Math.Max(min.y, max.y),
                Math.Max(min.z, max.z));
            return new SmartBuildVolume(
                construct,
                origin,
                SmartBuildAxis.X,
                SmartBuildAxis.Y,
                SmartBuildAxis.Z,
                end.x - origin.x + 1,
                end.y - origin.y + 1,
                end.z - origin.z + 1);
        }
    }
}
