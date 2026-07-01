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

    internal enum SmartBuildSlopeSupportMode
    {
        Full,
        Step
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
        private Vector3i _forwardStep;
        private Vector3i _rightStep;
        private Vector3i _dropStep;

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
            SmartBuildSlopeSupportMode supportMode = SmartBuildSlopeSupportMode.Full)
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
            SupportMode = supportMode;
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

        internal SmartBuildSlopeSupportMode SupportMode { get; private set; }

        internal SmartBuildAxis ForwardAxis { get; private set; }

        internal int ForwardSign { get; private set; }

        internal SmartBuildAxis RightAxis { get; private set; }

        internal int RightSign { get; private set; }

        internal SmartBuildAxis DropAxis { get; private set; }

        internal int DropSign { get; private set; }

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

        internal Vector3 RotationPivotLocal => ToVector3(NearestCellToCenter(Bounds.Center));

        internal static SmartBuildPiece CreateCuboid(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildDrawPlane drawPlane) =>
            CreateCuboid(
                construct,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane);

        internal static SmartBuildPiece CreateCuboid(
            AllConstruct construct,
            Vector3i origin,
            Vector3i size,
            SmartBuildDrawPlane drawPlane) =>
            new SmartBuildPiece(
                ++s_nextId,
                construct,
                SmartBuildShapeKind.Cuboid,
                origin,
                SmartBuildDraft.ClampSize(size),
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
            SmartBuildSlopeSupportMode supportMode = SmartBuildSlopeSupportMode.Full) =>
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
                supportMode);

        internal SmartBuildPiece Clone()
        {
            SmartBuildPiece clone = new SmartBuildPiece(
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
                SupportMode);
            clone.SetBasis(_forwardStep, _dropStep, _rightStep);
            return clone;
        }

        internal SmartBuildPiece Duplicate(Vector3i offset)
        {
            SmartBuildPiece duplicate = new SmartBuildPiece(
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
                SupportMode);
            duplicate.SetBasis(_forwardStep, _dropStep, _rightStep);
            return duplicate;
        }

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
            SupportMode = source.SupportMode;
            SetBasis(source._forwardStep, source._dropStep, source._rightStep);
        }

        internal void SetSupportMode(SmartBuildSlopeSupportMode mode)
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            SupportMode = mode;
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
                    SupportMode == SmartBuildSlopeSupportMode.Step ? " | step support" : string.Empty);
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
            RotateAroundAxis(DecorationEditAxis.Y, 1);
        }

        internal void RotateAroundAxis(
            DecorationEditAxis axis,
            int quarterTurns)
        {
            int turns = NormalizeQuarterTurns(quarterTurns);
            if (axis == DecorationEditAxis.None ||
                axis == DecorationEditAxis.Free ||
                turns == 0)
            {
                return;
            }

            if (ShapeKind == SmartBuildShapeKind.Cuboid)
            {
                RotateCuboidAroundPivot(axis, turns, NearestCellToCenter(Bounds.Center));
                return;
            }

            Vector3 pivot = RotationPivotLocal;
            SetBasis(
                RotateUnit(_forwardStep, axis, turns),
                RotateUnit(_dropStep, axis, turns),
                RotateUnit(_rightStep, axis, turns));
            RecenterDownSlopeAround(pivot);
        }

        internal void FlipForward()
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            Vector3 pivot = RotationPivotLocal;
            Vector3i forward = Multiply(_forwardStep, -1);
            SetBasis(forward, _dropStep, Cross(forward, _dropStep));
            RecenterDownSlopeAround(pivot);
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

            return SupportMode == SmartBuildSlopeSupportMode.Full
                ? EnumerateDownSlopeSupportCells()
                : EnumerateDownSlopeStepSupportCells();
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
                    DownSlopeRotation(),
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
            int dropExtent = Math.Max(1, bounds.Extent(DropAxis));
            int nextSteps;
            if (hasResizedAxis && changed == DropAxis)
                nextSteps = dropExtent;
            else if (hasResizedAxis && changed == ForwardAxis)
                nextSteps = Math.Max(1, (runExtent + SlopeLength - 1) / SlopeLength);
            else
                nextSteps = Math.Max(dropExtent, Math.Max(1, (runExtent + SlopeLength - 1) / SlopeLength));

            SlopeSteps = Math.Max(1, nextSteps);
            SlopeWidth = Math.Max(1, bounds.Extent(RightAxis));
            int finalRun = SlopeSteps * SlopeLength;

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
            int startDrop = StartComponentFromBounds(
                bounds,
                DropAxis,
                DropSign,
                SlopeSteps,
                hasResizedAxis && changed == DropAxis ? resizedSign : 0);

            Vector3i origin = new Vector3i(0, 0, 0);
            origin = SmartBuildAxisHelper.Set(origin, ForwardAxis, startForward);
            origin = SmartBuildAxisHelper.Set(origin, RightAxis, startRight);
            origin = SmartBuildAxisHelper.Set(origin, DropAxis, startDrop);
            Origin = origin;
        }

        private void RotateCuboidAroundPivot(
            DecorationEditAxis axis,
            int turns,
            Vector3i pivot)
        {
            SmartBuildBounds rotated = BoundsFromCells(
                Bounds
                    .EnumerateCells()
                    .Select(cell => RotateCellAroundPivot(cell, pivot, axis, turns)));
            Origin = rotated.Min;
            _cuboidSize = rotated.Size;
        }

        private void RecenterDownSlopeAround(Vector3 targetCenter)
        {
            SmartBuildBounds relativeBounds = BoundsFromCells(
                EnumerateDownSlopeCellsFrom(new Vector3i(0, 0, 0), includeSupport: true));
            Vector3 origin = targetCenter - relativeBounds.Center;
            Origin = RoundToCell(origin);
        }

        private static Vector3i RotateCellAroundPivot(
            Vector3i cell,
            Vector3i pivot,
            DecorationEditAxis axis,
            int turns)
        {
            Vector3 delta = ToVector3(cell - pivot);
            for (int turn = 0; turn < NormalizeQuarterTurns(turns); turn++)
            {
                switch (axis)
                {
                    case DecorationEditAxis.X:
                        delta = new Vector3(delta.x, -delta.z, delta.y);
                        break;
                    case DecorationEditAxis.Y:
                        delta = new Vector3(delta.z, delta.y, -delta.x);
                        break;
                    case DecorationEditAxis.Z:
                        delta = new Vector3(-delta.y, delta.x, delta.z);
                        break;
                }
            }

            return RoundToCell(ToVector3(pivot) + delta);
        }

        private static Vector3i NearestCellToCenter(Vector3 center) =>
            new Vector3i(
                Mathf.FloorToInt(center.x + 0.5f),
                Mathf.FloorToInt(center.y + 0.5f),
                Mathf.FloorToInt(center.z + 0.5f));

        private static Vector3i RoundToCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

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
            return EnumerateDownSlopeCellsFrom(Origin, includeSupport);
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeCellsFrom(Vector3i origin, bool includeSupport)
        {
            var cells = new Dictionary<string, Vector3i>();
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines(origin))
            {
                foreach (Vector3i cell in line)
                    cells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;
            }

            if (includeSupport)
            {
                IEnumerable<Vector3i> support = SupportMode == SmartBuildSlopeSupportMode.Full
                    ? EnumerateDownSlopeSupportCells(origin)
                    : EnumerateDownSlopeStepSupportCells(origin);
                foreach (Vector3i cell in support)
                    cells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;
            }

            return cells.Values;
        }

        private IEnumerable<IReadOnlyList<Vector3i>> EnumerateDownSlopeLines()
        {
            return EnumerateDownSlopeLines(Origin);
        }

        private IEnumerable<IReadOnlyList<Vector3i>> EnumerateDownSlopeLines(Vector3i origin)
        {
            for (int step = 0; step < SlopeSteps; step++)
            {
                for (int width = 0; width < SlopeWidth; width++)
                {
                    Vector3i start = origin +
                                     Multiply(_forwardStep, step * SlopeLength) +
                                     Multiply(_dropStep, step) +
                                     Multiply(_rightStep, width);
                    var line = new List<Vector3i>(SlopeLength);
                    for (int index = 0; index < SlopeLength; index++)
                        line.Add(start + Multiply(_forwardStep, index));
                    yield return line;
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeSupportCells()
        {
            return EnumerateDownSlopeSupportCells(Origin);
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeSupportCells(Vector3i origin)
        {
            int bottomComponent = SmartBuildAxisHelper.Get(
                origin + Multiply(_dropStep, SlopeSteps - 1),
                DropAxis);
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines(origin))
            {
                foreach (Vector3i slopeCell in line)
                {
                    int slopeComponent = SmartBuildAxisHelper.Get(slopeCell, DropAxis);
                    for (int component = bottomComponent;
                         component != slopeComponent;
                         component -= DropSign)
                    {
                        yield return SmartBuildAxisHelper.Set(slopeCell, DropAxis, component);
                    }
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeStepSupportCells()
        {
            return EnumerateDownSlopeStepSupportCells(Origin);
        }

        private IEnumerable<Vector3i> EnumerateDownSlopeStepSupportCells(Vector3i origin)
        {
            var slopeKeys = new HashSet<string>(
                EnumerateDownSlopeLines(origin)
                    .SelectMany(line => line)
                    .Select(DecoLimitLifter.EsuSymmetry.CellKey));
            var seen = new HashSet<string>();
            foreach (IReadOnlyList<Vector3i> line in EnumerateDownSlopeLines(origin))
            {
                foreach (Vector3i slopeCell in line)
                {
                    Vector3i supportCell = slopeCell + _dropStep;
                    string key = DecoLimitLifter.EsuSymmetry.CellKey(supportCell);
                    if (!slopeKeys.Contains(key) && seen.Add(key))
                        yield return supportCell;
                }
            }
        }

        private void SetForward(SmartBuildAxis axis, int sign)
        {
            SmartBuildAxis forwardAxis = NormalizeForwardAxis(axis);
            Vector3i forward = SmartBuildAxisHelper.ToVector3i(forwardAxis, sign >= 0 ? 1 : -1);
            Vector3i drop = new Vector3i(0, -1, 0);
            SetBasis(forward, drop, Cross(forward, drop));
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

        private void SetBasis(
            Vector3i forward,
            Vector3i drop,
            Vector3i right)
        {
            _forwardStep = NormalizeUnit(forward, new Vector3i(0, 0, 1));
            _dropStep = NormalizeUnit(drop, new Vector3i(0, -1, 0));
            _rightStep = NormalizeUnit(right, Cross(_forwardStep, _dropStep));
            if (IsZero(_rightStep) || Dot(_rightStep, _forwardStep) != 0 || Dot(_rightStep, _dropStep) != 0)
                _rightStep = NormalizeUnit(Cross(_forwardStep, _dropStep), new Vector3i(1, 0, 0));

            VectorToAxis(_forwardStep, out SmartBuildAxis forwardAxis, out int forwardSign);
            VectorToAxis(_rightStep, out SmartBuildAxis rightAxis, out int rightSign);
            VectorToAxis(_dropStep, out SmartBuildAxis dropAxis, out int dropSign);
            ForwardAxis = forwardAxis;
            ForwardSign = forwardSign;
            RightAxis = rightAxis;
            RightSign = rightSign;
            DropAxis = dropAxis;
            DropSign = dropSign;
        }

        private Quaternion DownSlopeRotation()
        {
            if (IsZero(_forwardStep) || IsZero(_dropStep) || IsZero(_rightStep))
                return SmartBuildPlacement.RotationForAxis(ForwardAxis, ForwardSign);

            return QuaternionFromBasis(
                _rightStep,
                Multiply(_dropStep, -1),
                _forwardStep);
        }

        private static Quaternion QuaternionFromBasis(
            Vector3i right,
            Vector3i up,
            Vector3i forward)
        {
            float m00 = right.x;
            float m01 = up.x;
            float m02 = forward.x;
            float m10 = right.y;
            float m11 = up.y;
            float m12 = forward.y;
            float m20 = right.z;
            float m21 = up.z;
            float m22 = forward.z;
            float trace = m00 + m11 + m22;

            if (trace > 0f)
            {
                float scale = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quaternion(
                    (m21 - m12) / scale,
                    (m02 - m20) / scale,
                    (m10 - m01) / scale,
                    0.25f * scale);
            }

            if (m00 > m11 && m00 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quaternion(
                    0.25f * scale,
                    (m01 + m10) / scale,
                    (m02 + m20) / scale,
                    (m21 - m12) / scale);
            }

            if (m11 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quaternion(
                    (m01 + m10) / scale,
                    0.25f * scale,
                    (m12 + m21) / scale,
                    (m02 - m20) / scale);
            }

            {
                float scale = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                return new Quaternion(
                    (m02 + m20) / scale,
                    (m12 + m21) / scale,
                    0.25f * scale,
                    (m10 - m01) / scale);
            }
        }

        private static int NormalizeQuarterTurns(int turns) =>
            ((turns % 4) + 4) % 4;

        private static Vector3i RotateUnit(
            Vector3i value,
            DecorationEditAxis axis,
            int quarterTurns)
        {
            Vector3i result = value;
            for (int turn = 0; turn < NormalizeQuarterTurns(quarterTurns); turn++)
            {
                switch (axis)
                {
                    case DecorationEditAxis.X:
                        result = new Vector3i(result.x, -result.z, result.y);
                        break;
                    case DecorationEditAxis.Y:
                        result = new Vector3i(result.z, result.y, -result.x);
                        break;
                    case DecorationEditAxis.Z:
                        result = new Vector3i(-result.y, result.x, result.z);
                        break;
                }
            }

            return result;
        }

        private static Vector3i NormalizeUnit(
            Vector3i value,
            Vector3i fallback)
        {
            if (Math.Abs(value.x) >= Math.Abs(value.y) &&
                Math.Abs(value.x) >= Math.Abs(value.z) &&
                value.x != 0)
            {
                return new Vector3i(value.x >= 0 ? 1 : -1, 0, 0);
            }

            if (Math.Abs(value.y) >= Math.Abs(value.z) &&
                value.y != 0)
            {
                return new Vector3i(0, value.y >= 0 ? 1 : -1, 0);
            }

            if (value.z != 0)
                return new Vector3i(0, 0, value.z >= 0 ? 1 : -1);

            return fallback;
        }

        private static void VectorToAxis(
            Vector3i value,
            out SmartBuildAxis axis,
            out int sign)
        {
            if (value.x != 0)
            {
                axis = SmartBuildAxis.X;
                sign = value.x >= 0 ? 1 : -1;
                return;
            }

            if (value.y != 0)
            {
                axis = SmartBuildAxis.Y;
                sign = value.y >= 0 ? 1 : -1;
                return;
            }

            axis = SmartBuildAxis.Z;
            sign = value.z >= 0 ? 1 : -1;
        }

        private static Vector3i Cross(Vector3i a, Vector3i b) =>
            new Vector3i(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);

        private static int Dot(Vector3i a, Vector3i b) =>
            a.x * b.x + a.y * b.y + a.z * b.z;

        private static bool IsZero(Vector3i value) =>
            value.x == 0 && value.y == 0 && value.z == 0;

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);

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
