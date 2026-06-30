using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildShapeKind
    {
        Cuboid,
        DownSlope,
        TriangleCorner,
        Wedge,
        SquareCorner
    }

    internal readonly struct SmartBuildBounds
    {
        internal SmartBuildBounds(Vector3i min, Vector3i max)
        {
            Min = new Vector3i(
                Math.Min(min.x, max.x),
                Math.Min(min.y, max.y),
                Math.Min(min.z, max.z));
            Max = new Vector3i(
                Math.Max(min.x, max.x),
                Math.Max(min.y, max.y),
                Math.Max(min.z, max.z));
        }

        internal Vector3i Min { get; }

        internal Vector3i Max { get; }

        internal Vector3i Size =>
            new Vector3i(
                Max.x - Min.x + 1,
                Max.y - Min.y + 1,
                Max.z - Min.z + 1);

        internal Vector3 Center =>
            new Vector3(
                (Min.x + Max.x) * 0.5f,
                (Min.y + Max.y) * 0.5f,
                (Min.z + Max.z) * 0.5f);

        internal int Extent(SmartBuildAxis axis) =>
            SmartBuildAxisHelper.Get(Max, axis) -
            SmartBuildAxisHelper.Get(Min, axis) + 1;

        internal IEnumerable<Vector3i> EnumerateCells()
        {
            for (int z = Min.z; z <= Max.z; z++)
                for (int y = Min.y; y <= Max.y; y++)
                    for (int x = Min.x; x <= Max.x; x++)
                        yield return new Vector3i(x, y, z);
        }
    }

    internal sealed class SmartBuildPiece
    {
        private static int s_nextId;
        private Vector3i _cuboidSize;

        private SmartBuildPiece(
            int id,
            AllConstruct construct,
            SmartBuildShapeKind shapeKind,
            Vector3i origin,
            Vector3i cuboidSize,
            SmartBuildDrawPlane drawPlane,
            int slopeLength,
            int slopeSteps,
            int slopeWidth,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            bool includeSupportFill = true)
        {
            Id = id;
            Construct = construct;
            ShapeKind = shapeKind;
            Origin = origin;
            _cuboidSize = SmartBuildDraft.ClampSize(cuboidSize);
            DrawPlane = drawPlane;
            SlopeLength = Mathf.Clamp(slopeLength, 1, 4);
            SlopeSteps = Math.Max(1, slopeSteps);
            SlopeWidth = Math.Max(1, slopeWidth);
            IncludeSupportFill = includeSupportFill;
            SetForward(forwardAxis, forwardSign);
        }

        internal int Id { get; }

        internal AllConstruct Construct { get; }

        internal SmartBuildShapeKind ShapeKind { get; private set; }

        internal Vector3i Origin { get; private set; }

        internal SmartBuildDrawPlane DrawPlane { get; set; }

        internal int SlopeLength { get; private set; }

        internal int SlopeSteps { get; private set; }

        internal int SlopeWidth { get; private set; }

        internal bool IncludeSupportFill { get; private set; }

        internal SmartBuildAxis ForwardAxis { get; private set; }

        internal int ForwardSign { get; private set; }

        internal SmartBuildAxis RightAxis { get; private set; }

        internal int RightSign { get; private set; }

        internal Vector3i Size => Bounds.Size;

        internal SmartBuildBounds Bounds
        {
            get
            {
                if (ShapeKind == SmartBuildShapeKind.Cuboid)
                    return new SmartBuildBounds(
                        Origin,
                        Origin + new Vector3i(
                            _cuboidSize.x - 1,
                            _cuboidSize.y - 1,
                            _cuboidSize.z - 1));

                return BoundsFromCells(EnumeratePreviewCells());
            }
        }

        internal Vector3 CenterLocal => Bounds.Center;

        internal static SmartBuildPiece CreateCuboid(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildDrawPlane drawPlane) =>
            new SmartBuildPiece(
                ++s_nextId,
                construct,
                SmartBuildShapeKind.Cuboid,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane,
                slopeLength: 1,
                slopeSteps: 1,
                slopeWidth: 1,
                forwardAxis: SmartBuildAxis.Z,
                forwardSign: 1);

        internal static SmartBuildPiece CreateDownSlope(
            AllConstruct construct,
            Vector3i origin,
            int slopeLength,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane,
            bool includeSupportFill = true) =>
            new SmartBuildPiece(
                ++s_nextId,
                construct,
                SmartBuildShapeKind.DownSlope,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane,
                slopeLength,
                slopeSteps: 1,
                slopeWidth: Math.Max(1, width),
                NormalizeForwardAxis(forwardAxis),
                forwardSign,
                includeSupportFill);

        internal SmartBuildPiece Clone() =>
            new SmartBuildPiece(
                Id,
                Construct,
                ShapeKind,
                Origin,
                _cuboidSize,
                DrawPlane,
                SlopeLength,
                SlopeSteps,
                SlopeWidth,
                ForwardAxis,
                ForwardSign,
                IncludeSupportFill);

        internal SmartBuildPiece Duplicate(Vector3i offset) =>
            new SmartBuildPiece(
                ++s_nextId,
                Construct,
                ShapeKind,
                Origin + offset,
                _cuboidSize,
                DrawPlane,
                SlopeLength,
                SlopeSteps,
                SlopeWidth,
                ForwardAxis,
                ForwardSign,
                IncludeSupportFill);

        internal void CopyFrom(SmartBuildPiece source)
        {
            if (source == null || source.Id != Id)
                return;

            ShapeKind = source.ShapeKind;
            Origin = source.Origin;
            _cuboidSize = source._cuboidSize;
            DrawPlane = source.DrawPlane;
            SlopeLength = source.SlopeLength;
            SlopeSteps = source.SlopeSteps;
            SlopeWidth = source.SlopeWidth;
            IncludeSupportFill = source.IncludeSupportFill;
            SetForward(source.ForwardAxis, source.ForwardSign);
        }

        internal void SetSupportFill(bool include)
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            IncludeSupportFill = include;
        }

        internal string ShapeLabel()
        {
            switch (ShapeKind)
            {
                case SmartBuildShapeKind.DownSlope:
                    return SlopeLength.ToString(CultureInfo.InvariantCulture) + "m down slope";
                default:
                    return "Block";
            }
        }

        internal string ShapeCode()
        {
            switch (ShapeKind)
            {
                case SmartBuildShapeKind.DownSlope:
                    return "S" + SlopeLength.ToString(CultureInfo.InvariantCulture);
                default:
                    return "Block";
            }
        }

        internal string FormatDimensions()
        {
            if (ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}m slope x {1} step(s) x {2} wide{3}",
                    SlopeLength,
                    SlopeSteps,
                    SlopeWidth,
                    IncludeSupportFill ? string.Empty : " | flat support");
            }

            Vector3i size = Size;
            return $"{size.x} x {size.y} x {size.z}";
        }

        internal string CompactSceneLabel()
        {
            if (ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0} | S{1} | {2} step(s) x {3} wide",
                    Id,
                    SlopeLength,
                    SlopeSteps,
                    SlopeWidth);
            }

            Vector3i size = Size;
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0} | Block | {1} x {2} x {3}",
                Id,
                size.x,
                size.y,
                size.z);
        }

        internal SmartBuildVolume ToVolume()
        {
            SmartBuildBounds bounds = Bounds;
            return SmartBuildVolume.FromBounds(Construct, bounds.Min, bounds.Max);
        }

        internal void MoveBy(Vector3i delta)
        {
            Origin += delta;
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

            if (ShapeKind == SmartBuildShapeKind.Cuboid)
            {
                ResizeCuboid(axis, sign, delta);
                return;
            }

            SmartBuildBounds resized = ResizeBounds(Bounds, axis, sign, delta);
            ApplyDownSlopeBounds(resized, axis, sign);
        }

        internal void RotateYaw()
        {
            if (ShapeKind == SmartBuildShapeKind.Cuboid)
            {
                _cuboidSize = new Vector3i(_cuboidSize.z, _cuboidSize.y, _cuboidSize.x);
                return;
            }

            SmartBuildBounds bounds = Bounds;
            SetForward(RightAxis, RightSign);
            ApplyDownSlopeBounds(bounds, DecorationEditAxis.None, 1);
        }

        internal void FlipForward()
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            SmartBuildBounds bounds = Bounds;
            SetForward(ForwardAxis, -ForwardSign);
            ApplyDownSlopeBounds(bounds, DecorationEditAxis.None, 1);
        }

        internal int ExtentAlong(SmartBuildAxis axis) => Bounds.Extent(axis);

        internal int FaceHorizontalWidth(SmartBuildAxis normalAxis)
        {
            SmartBuildBounds bounds = Bounds;
            if (normalAxis == SmartBuildAxis.X)
                return bounds.Extent(SmartBuildAxis.Z);
            if (normalAxis == SmartBuildAxis.Z)
                return bounds.Extent(SmartBuildAxis.X);
            return Math.Max(bounds.Extent(SmartBuildAxis.X), bounds.Extent(SmartBuildAxis.Z));
        }

        internal IEnumerable<Vector3i> EnumeratePreviewCells()
        {
            if (ShapeKind == SmartBuildShapeKind.Cuboid)
                return Bounds.EnumerateCells();

            return EnumerateDownSlopeCells(includeSupport: true);
        }

        internal IEnumerable<Vector3i> EnumeratePlacementCells()
        {
            if (ShapeKind == SmartBuildShapeKind.Cuboid)
                return Bounds.EnumerateCells();

            return EnumerateDownSlopeCells(includeSupport: false);
        }

        internal IEnumerable<Vector3i> EnumerateSupportCells()
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return Array.Empty<Vector3i>();

            return IncludeSupportFill
                ? EnumerateDownSlopeSupportCells()
                : EnumerateDownSlopeFlatSupportCells();
        }

        internal IEnumerable<IReadOnlyList<Vector3i>> EnumerateSlopeLines()
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return Array.Empty<IReadOnlyList<Vector3i>>();

            return EnumerateDownSlopeLines();
        }

        internal IReadOnlyList<SmartBuildPlacement> BuildFixedPlacements(
            SmartBuildSource source,
            out string reason)
        {
            reason = null;
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return Array.Empty<SmartBuildPlacement>();

            SmartBlockFamily family = source?.DownSlopeFamily;
            if (family == null || !family.IsSupported)
            {
                reason = family?.UnsupportedReason ?? "Down slope blocks are unavailable for this material.";
                return Array.Empty<SmartBuildPlacement>();
            }

            SmartBlockCandidate candidate = family.CandidateForLength(SlopeLength);
            if (candidate == null || candidate.Length != SlopeLength)
            {
                reason = $"{SlopeLength}m down slope is unavailable for this material.";
                return Array.Empty<SmartBuildPlacement>();
            }

            var placements = new List<SmartBuildPlacement>();
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines())
            {
                if (line.Count == 0)
                    continue;

                placements.Add(new SmartBuildPlacement(
                    line[0],
                    candidate,
                    ForwardAxis,
                    ForwardSign,
                    SmartBuildPlacement.RotationForAxis(ForwardAxis, ForwardSign),
                    line,
                    candidate.DisplayName));
            }

            return placements;
        }

        private void ResizeCuboid(
            DecorationEditAxis axis,
            int sign,
            int delta)
        {
            Vector3i min = Origin;
            Vector3i max = Bounds.Max;
            SmartBuildAxis smartAxis = SmartBuildDraft.ToSmartAxis(axis);
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
            _cuboidSize = new Vector3i(
                max.x - min.x + 1,
                max.y - min.y + 1,
                max.z - min.z + 1);
        }

        private void ApplyDownSlopeBounds(
            SmartBuildBounds bounds,
            DecorationEditAxis resizedAxis,
            int resizedSign)
        {
            bool hasResizedAxis = resizedAxis != DecorationEditAxis.None &&
                                  resizedAxis != DecorationEditAxis.Free;
            SmartBuildAxis changed = SmartBuildDraft.ToSmartAxis(resizedAxis);
            int runExtent = Math.Max(1, bounds.Extent(ForwardAxis));
            int heightExtent = Math.Max(1, bounds.Extent(SmartBuildAxis.Y));
            int nextSteps;
            if (resizedAxis == DecorationEditAxis.Y)
                nextSteps = heightExtent;
            else if (hasResizedAxis && changed == ForwardAxis)
                nextSteps = Math.Max(1, (runExtent + SlopeLength - 1) / SlopeLength);
            else
                nextSteps = Math.Max(heightExtent, Math.Max(1, (runExtent + SlopeLength - 1) / SlopeLength));

            SlopeSteps = Math.Max(1, nextSteps);
            SlopeWidth = Math.Max(1, bounds.Extent(RightAxis));
            int finalRun = SlopeSteps * SlopeLength;

            int topY = bounds.Max.y;
            if (resizedAxis == DecorationEditAxis.Y && resizedSign > 0)
                topY = bounds.Min.y + SlopeSteps - 1;

            int startForward = StartComponentFromBounds(
                bounds,
                ForwardAxis,
                ForwardSign,
                finalRun,
                hasResizedAxis && changed == ForwardAxis ? resizedSign : 0);
            int startRight = StartComponentFromBounds(
                bounds,
                RightAxis,
                RightSign,
                SlopeWidth,
                hasResizedAxis && changed == RightAxis ? resizedSign : 0);

            Vector3i origin = new Vector3i(0, topY, 0);
            origin = SmartBuildAxisHelper.Set(origin, ForwardAxis, startForward);
            origin = SmartBuildAxisHelper.Set(origin, RightAxis, startRight);
            Origin = origin;
        }

        private static int StartComponentFromBounds(
            SmartBuildBounds bounds,
            SmartBuildAxis axis,
            int directionSign,
            int extent,
            int resizedSign)
        {
            int min = SmartBuildAxisHelper.Get(bounds.Min, axis);
            int max = SmartBuildAxisHelper.Get(bounds.Max, axis);
            int direction = directionSign >= 0 ? 1 : -1;
            if (resizedSign != 0 && resizedSign != direction)
            {
                int far = direction >= 0 ? max : min;
                return far - direction * (extent - 1);
            }

            return direction >= 0 ? min : max;
        }

        private static SmartBuildBounds ResizeBounds(
            SmartBuildBounds bounds,
            DecorationEditAxis axis,
            int sign,
            int delta)
        {
            Vector3i min = bounds.Min;
            Vector3i max = bounds.Max;
            SmartBuildAxis smartAxis = SmartBuildDraft.ToSmartAxis(axis);
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

            return new SmartBuildBounds(min, max);
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeCells(bool includeSupport)
        {
            var cells = new Dictionary<string, Vector3i>();
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines())
            {
                foreach (Vector3i cell in line)
                    cells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;
            }

            if (includeSupport)
            {
                foreach (Vector3i cell in EnumerateSupportCells())
                    cells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;
            }

            return cells.Values;
        }

        private IEnumerable<IReadOnlyList<Vector3i>> EnumerateDownSlopeLines()
        {
            Vector3i forward = SmartBuildAxisHelper.ToVector3i(ForwardAxis, ForwardSign);
            Vector3i right = SmartBuildAxisHelper.ToVector3i(RightAxis, RightSign);
            for (int step = 0; step < SlopeSteps; step++)
            {
                for (int width = 0; width < SlopeWidth; width++)
                {
                    Vector3i start = Origin +
                                     Multiply(forward, step * SlopeLength) +
                                     new Vector3i(0, -step, 0) +
                                     Multiply(right, width);
                    var line = new List<Vector3i>(SlopeLength);
                    for (int index = 0; index < SlopeLength; index++)
                        line.Add(start + Multiply(forward, index));
                    yield return line;
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeSupportCells()
        {
            int bottomY = Origin.y - SlopeSteps + 1;
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines())
            {
                foreach (Vector3i slopeCell in line)
                {
                    for (int y = bottomY; y < slopeCell.y; y++)
                        yield return new Vector3i(slopeCell.x, y, slopeCell.z);
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeFlatSupportCells()
        {
            int bottomY = Origin.y - SlopeSteps + 1;
            var seen = new HashSet<string>();
            foreach (Vector3i supportCell in EnumerateDownSlopeSupportCells())
            {
                if (supportCell.y != bottomY)
                    continue;

                if (seen.Add(DecoLimitLifter.EsuSymmetry.CellKey(supportCell)))
                    yield return supportCell;
            }
        }

        private void SetForward(SmartBuildAxis axis, int sign)
        {
            ForwardAxis = NormalizeForwardAxis(axis);
            ForwardSign = sign >= 0 ? 1 : -1;
            DeriveRight(ForwardAxis, ForwardSign, out SmartBuildAxis rightAxis, out int rightSign);
            RightAxis = rightAxis;
            RightSign = rightSign;
        }

        private static SmartBuildAxis NormalizeForwardAxis(SmartBuildAxis axis) =>
            axis == SmartBuildAxis.X ? SmartBuildAxis.X : SmartBuildAxis.Z;

        private static void DeriveRight(
            SmartBuildAxis forwardAxis,
            int forwardSign,
            out SmartBuildAxis rightAxis,
            out int rightSign)
        {
            if (forwardAxis == SmartBuildAxis.X)
            {
                rightAxis = SmartBuildAxis.Z;
                rightSign = forwardSign >= 0 ? -1 : 1;
                return;
            }

            rightAxis = SmartBuildAxis.X;
            rightSign = forwardSign >= 0 ? 1 : -1;
        }

        private static Vector3i Multiply(Vector3i value, int amount) =>
            new Vector3i(value.x * amount, value.y * amount, value.z * amount);

        private static SmartBuildBounds BoundsFromCells(IEnumerable<Vector3i> rawCells)
        {
            Vector3i[] cells = rawCells?.ToArray() ?? Array.Empty<Vector3i>();
            if (cells.Length == 0)
                return new SmartBuildBounds(new Vector3i(0, 0, 0), new Vector3i(0, 0, 0));

            Vector3i min = cells[0];
            Vector3i max = cells[0];
            for (int index = 1; index < cells.Length; index++)
            {
                Vector3i cell = cells[index];
                min.x = Math.Min(min.x, cell.x);
                min.y = Math.Min(min.y, cell.y);
                min.z = Math.Min(min.z, cell.z);
                max.x = Math.Max(max.x, cell.x);
                max.y = Math.Max(max.y, cell.y);
                max.z = Math.Max(max.z, cell.z);
            }

            return new SmartBuildBounds(min, max);
        }
    }
}
