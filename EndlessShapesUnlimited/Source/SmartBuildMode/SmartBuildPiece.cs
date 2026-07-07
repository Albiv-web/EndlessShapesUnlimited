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
        Pole,
        DownSlope,
        FacingDownSlope,
        TriangleCorner,
        InvertedTriangleCorner,
        Wedge,
        WedgeFront,
        WedgeBack,
        SquareCorner,
        SquareBackedCorner,
        SlopeTransition,
        SlopeInverseTransition,
        OffsetSlope,
        GeneratedCircle,
        GeneratedPolygon,
        GeneratedSphere
    }

    internal enum SmartBuildSlopeSupportMode
    {
        Full,
        Step
    }

    internal enum SmartBuildGeneratorFillMode
    {
        OutlineShell,
        Filled
    }

    internal enum SmartBuildGeneratorSmoothingMode
    {
        None,
        PerimeterDownSlope
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
        private int _fixedForwardTiles;
        private int _fixedForwardCells;
        private int _fixedRightTiles;
        private int _fixedDropTiles;
        private int _generatorSides;
        private SmartBuildGeneratorFillMode _generatorFillMode;
        private SmartBuildGeneratorSmoothingMode _generatorSmoothingMode;
        private bool _generatorRoundLock;

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
            string shapeDescriptorKey = null,
            int selectedLength = 1,
            int fixedForwardTiles = 1,
            int fixedRightTiles = 1,
            int fixedDropTiles = 1,
            int fixedForwardCells = 0,
            SmartBuildSlopeSupportMode supportMode = SmartBuildSlopeSupportMode.Full,
            int generatorSides = 0,
            SmartBuildGeneratorFillMode generatorFillMode = SmartBuildGeneratorFillMode.OutlineShell,
            SmartBuildGeneratorSmoothingMode generatorSmoothingMode = SmartBuildGeneratorSmoothingMode.None,
            bool generatorRoundLock = true)
        {
            Id = id;
            Construct = construct;
            ShapeKind = shapeKind;
            ShapeDescriptorKey = string.IsNullOrWhiteSpace(shapeDescriptorKey)
                ? DescriptorKeyForKind(shapeKind)
                : shapeDescriptorKey;
            Origin = origin;
            _cuboidSize = SmartBuildDraft.ClampSize(cuboidSize);
            DrawPlane = drawPlane;
            SlopeLength = Mathf.Clamp(slopeLength, 1, 4);
            SlopeSteps = Math.Max(1, slopeSteps);
            SlopeWidth = Math.Max(1, slopeWidth);
            SelectedLength = Mathf.Clamp(selectedLength, 1, 4);
            _fixedForwardTiles = Math.Max(1, fixedForwardTiles);
            _fixedForwardCells = Math.Max(
                1,
                fixedForwardCells > 0
                    ? fixedForwardCells
                    : _fixedForwardTiles * SelectedLength);
            _fixedRightTiles = Math.Max(1, fixedRightTiles);
            _fixedDropTiles = Math.Max(1, fixedDropTiles);
            SupportMode = supportMode;
            _generatorSides = ClampGeneratorSides(generatorSides > 0 ? generatorSides : DefaultGeneratorSides(shapeKind));
            _generatorFillMode = generatorFillMode;
            _generatorSmoothingMode = generatorSmoothingMode;
            _generatorRoundLock = generatorRoundLock;
            SetForward(forwardAxis, forwardSign);
        }

        internal int Id { get; }

        internal AllConstruct Construct { get; }

        internal SmartBuildShapeKind ShapeKind { get; private set; }

        internal string ShapeDescriptorKey { get; private set; }

        internal Vector3i Origin { get; private set; }

        internal SmartBuildDrawPlane DrawPlane { get; set; }

        internal int SlopeLength { get; private set; }

        internal int SlopeSteps { get; private set; }

        internal int SlopeWidth { get; private set; }

        internal int SelectedLength { get; private set; }

        internal int FixedForwardTiles => _fixedForwardTiles;

        internal int FixedForwardCells => _fixedForwardCells;

        internal int FixedRightTiles => _fixedRightTiles;

        internal int FixedDropTiles => _fixedDropTiles;

        internal SmartBuildSlopeSupportMode SupportMode { get; private set; }

        internal int GeneratorSides => _generatorSides;

        internal SmartBuildGeneratorFillMode GeneratorFillMode => _generatorFillMode;

        internal SmartBuildGeneratorSmoothingMode GeneratorSmoothingMode => _generatorSmoothingMode;

        internal bool GeneratorRoundLock => _generatorRoundLock;

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

        internal bool IsFixedGeometry =>
            Descriptor()?.IsFixedGeometry == true;

        internal bool IsGeneratedShape =>
            Descriptor()?.IsGenerator == true ||
            IsGeneratedShapeKind(ShapeKind);

        internal SmartBuildShapeDescriptor Descriptor() =>
            SmartBuildShapeDescriptors.ByKey(ShapeDescriptorKey) ??
            SmartBuildShapeDescriptors.ByKey(DescriptorKeyForKind(ShapeKind)) ??
            SmartBuildShapeDescriptors.Cuboid;

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
                shapeDescriptorKey: SmartBuildShapeDescriptors.DownSlopeKey,
                selectedLength: slopeLength,
                supportMode: supportMode);

        internal static SmartBuildPiece CreateFixedShape(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildShapeDescriptor descriptor,
            int selectedLength,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane) =>
            new SmartBuildPiece(
                ++s_nextId,
                construct,
                descriptor?.Kind ?? SmartBuildShapeKind.Wedge,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane,
                slopeLength: 1,
                slopeSteps: 1,
                slopeWidth: 1,
                NormalizeForwardAxis(forwardAxis),
                forwardSign,
                shapeDescriptorKey: descriptor?.Key,
                selectedLength: selectedLength,
                fixedForwardTiles: 1,
                fixedRightTiles: Math.Max(1, width),
                fixedDropTiles: 1);

        internal static SmartBuildPiece CreateFixedShapePreview(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildShapeDescriptor descriptor,
            int selectedLength,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane) =>
            new SmartBuildPiece(
                0,
                construct,
                descriptor?.Kind ?? SmartBuildShapeKind.Wedge,
                origin,
                new Vector3i(1, 1, 1),
                drawPlane,
                slopeLength: 1,
                slopeSteps: 1,
                slopeWidth: 1,
                NormalizeForwardAxis(forwardAxis),
                forwardSign,
                shapeDescriptorKey: descriptor?.Key,
                selectedLength: selectedLength,
                fixedForwardTiles: 1,
                fixedRightTiles: Math.Max(1, width),
                fixedDropTiles: 1);

        internal static SmartBuildPiece CreateGeneratedShape(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildShapeDescriptor descriptor,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane)
        {
            return CreateGeneratedShapeCore(
                ++s_nextId,
                construct,
                origin,
                descriptor,
                forwardAxis,
                forwardSign,
                width,
                drawPlane);
        }

        internal static SmartBuildPiece CreateGeneratedShapeForPreview(
            SmartBuildShapeDescriptor descriptor,
            int width)
        {
            return CreateGeneratedShapePreview(
                null,
                new Vector3i(0, 0, 0),
                descriptor,
                SmartBuildAxis.X,
                1,
                width,
                SmartBuildDrawPlane.Camera);
        }

        internal static SmartBuildPiece CreateGeneratedShapePreview(
            AllConstruct construct,
            Vector3i origin,
            SmartBuildShapeDescriptor descriptor,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane)
        {
            return CreateGeneratedShapeCore(
                0,
                construct,
                origin,
                descriptor,
                forwardAxis,
                forwardSign,
                width,
                drawPlane);
        }

        private static SmartBuildPiece CreateGeneratedShapeCore(
            int id,
            AllConstruct construct,
            Vector3i origin,
            SmartBuildShapeDescriptor descriptor,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            SmartBuildDrawPlane drawPlane)
        {
            SmartBuildShapeKind kind = descriptor?.Kind ?? SmartBuildShapeKind.GeneratedCircle;
            if (!IsGeneratedShapeKind(kind))
                kind = SmartBuildShapeKind.GeneratedCircle;

            int diameter = Math.Max(3, Math.Max(1, width));
            if (diameter % 2 == 0)
                diameter++;

            Vector3i size = kind == SmartBuildShapeKind.GeneratedSphere
                ? new Vector3i(diameter, diameter, diameter)
                : new Vector3i(diameter, diameter, 1);
            return new SmartBuildPiece(
                id,
                construct,
                kind,
                origin,
                size,
                drawPlane,
                slopeLength: 1,
                slopeSteps: 1,
                slopeWidth: 1,
                NormalizeForwardAxis(forwardAxis),
                forwardSign,
                shapeDescriptorKey: descriptor?.Key,
                selectedLength: 1,
                generatorSides: descriptor?.GeneratorSidesPreset ?? DefaultGeneratorSides(kind),
                generatorRoundLock: kind == SmartBuildShapeKind.GeneratedCircle ||
                                    kind == SmartBuildShapeKind.GeneratedSphere);
        }

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
                ShapeDescriptorKey,
                SelectedLength,
                _fixedForwardTiles,
                _fixedRightTiles,
                _fixedDropTiles,
                _fixedForwardCells,
                SupportMode,
                _generatorSides,
                _generatorFillMode,
                _generatorSmoothingMode,
                _generatorRoundLock);
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
                ShapeDescriptorKey,
                SelectedLength,
                _fixedForwardTiles,
                _fixedRightTiles,
                _fixedDropTiles,
                _fixedForwardCells,
                SupportMode,
                _generatorSides,
                _generatorFillMode,
                _generatorSmoothingMode,
                _generatorRoundLock);
            duplicate.SetBasis(_forwardStep, _dropStep, _rightStep);
            return duplicate;
        }

        internal void CopyFrom(SmartBuildPiece source)
        {
            if (source == null || source.Id != Id)
                return;

            ShapeKind = source.ShapeKind;
            ShapeDescriptorKey = source.ShapeDescriptorKey;
            Origin = source.Origin;
            _cuboidSize = source._cuboidSize;
            DrawPlane = source.DrawPlane;
            SlopeLength = source.SlopeLength;
            SlopeSteps = source.SlopeSteps;
            SlopeWidth = source.SlopeWidth;
            SelectedLength = source.SelectedLength;
            _fixedForwardTiles = source._fixedForwardTiles;
            _fixedForwardCells = source._fixedForwardCells;
            _fixedRightTiles = source._fixedRightTiles;
            _fixedDropTiles = source._fixedDropTiles;
            SupportMode = source.SupportMode;
            _generatorSides = source._generatorSides;
            _generatorFillMode = source._generatorFillMode;
            _generatorSmoothingMode = source._generatorSmoothingMode;
            _generatorRoundLock = source._generatorRoundLock;
            SetBasis(source._forwardStep, source._dropStep, source._rightStep);
        }

        internal void SetSupportMode(SmartBuildSlopeSupportMode mode)
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            SupportMode = mode;
        }

        internal void SetGeneratorFillMode(SmartBuildGeneratorFillMode mode)
        {
            if (!IsGeneratedShape)
                return;

            _generatorFillMode = mode;
        }

        internal void SetGeneratorSmoothingMode(SmartBuildGeneratorSmoothingMode mode)
        {
            if (!IsGeneratedShape)
                return;

            _generatorSmoothingMode = mode;
        }

        internal void SetGeneratorRoundLock(bool enabled)
        {
            if (!IsGeneratedShape)
                return;

            _generatorRoundLock = enabled;
            if (enabled &&
                (ShapeKind == SmartBuildShapeKind.GeneratedCircle ||
                 ShapeKind == SmartBuildShapeKind.GeneratedSphere))
            {
                ApplyRoundGeneratorSize();
            }
        }

        internal void SetGeneratorSides(int sides)
        {
            if (ShapeKind != SmartBuildShapeKind.GeneratedPolygon)
                return;

            _generatorSides = ClampGeneratorSides(sides);
        }

        internal string ShapeLabel()
        {
            SmartBuildShapeDescriptor descriptor = Descriptor();
            if (IsGeneratedShape)
                return GeneratedShapeLabel(descriptor);
            if (descriptor?.ProceduralDownSlope == true)
                return SlopeLength.ToString(CultureInfo.InvariantCulture) + "m down slope";
            if (descriptor != null && descriptor.IsFixedGeometry)
                return descriptor.UsesLengthSelector
                    ? SelectedLength.ToString(CultureInfo.InvariantCulture) + "m " + descriptor.Label.ToLowerInvariant()
                    : descriptor.Label;

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
            SmartBuildShapeDescriptor descriptor = Descriptor();
            if (IsGeneratedShape)
                return descriptor?.Label ?? ShapeKind.ToString();
            if (descriptor?.ProceduralDownSlope == true)
                return "S" + SlopeLength.ToString(CultureInfo.InvariantCulture);
            if (descriptor != null && descriptor.IsFixedGeometry)
                return descriptor.Label;

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

            if (IsGeneratedShape)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} | {1} x {2} x {3}{4}{5}",
                    _generatorFillMode == SmartBuildGeneratorFillMode.Filled ? "Filled" : "Shell",
                    _cuboidSize.x,
                    _cuboidSize.y,
                    _cuboidSize.z,
                    ShapeKind == SmartBuildShapeKind.GeneratedPolygon
                        ? " | " + _generatorSides.ToString(CultureInfo.InvariantCulture) + " sides"
                        : string.Empty,
                    _generatorSmoothingMode == SmartBuildGeneratorSmoothingMode.PerimeterDownSlope
                        ? " | smooth"
                        : string.Empty);
            }

            if (IsFixedGeometry)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}m pack x {1} forward x {2} wide x {3} deep",
                    SelectedLength,
                    _fixedForwardCells,
                    _fixedRightTiles,
                    _fixedDropTiles);
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

            if (IsGeneratedShape)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0} | {1} | {2} x {3} x {4}",
                    Id,
                    ShapeCode(),
                    _cuboidSize.x,
                    _cuboidSize.y,
                    _cuboidSize.z);
            }

            if (IsFixedGeometry)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0} | {1} | {2}m pack x {3}x{4}x{5}",
                    Id,
                    Descriptor()?.Label ?? ShapeKind.ToString(),
                    SelectedLength,
                    _fixedForwardCells,
                    _fixedRightTiles,
                    _fixedDropTiles);
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

        internal SmartBuildVolume ToVolume(SmartBuildSource source)
        {
            if (!IsFixedGeometry && !IsGeneratedShape)
                return ToVolume();

            SmartBuildBounds bounds = BoundsFromCells(EnumeratePreviewCells(source));
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

            if (IsFixedGeometry)
            {
                SmartBuildBounds fixedResized = ResizeBounds(Bounds, axis, sign, delta);
                ApplyFixedGeometryBounds(fixedResized, axis, sign);
                return;
            }

            if (IsGeneratedShape)
            {
                SmartBuildBounds generatedResized = ResizeBounds(GeneratorControlBounds(), axis, sign, delta);
                ApplyGeneratedBounds(generatedResized, axis, sign);
                return;
            }

            SmartBuildBounds resized = ResizeBounds(Bounds, axis, sign, delta);
            ApplyDownSlopeBounds(resized, axis, sign);
        }

        internal void ResizeDownSlopeCardinalFromHandle(
            DecorationEditAxis axis,
            int sign,
            int delta)
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope)
            {
                ResizeFromHandle(axis, sign, delta);
                return;
            }

            if (axis == DecorationEditAxis.None ||
                axis == DecorationEditAxis.Free ||
                delta == 0)
            {
                return;
            }

            SmartBuildAxis changed = SmartBuildDraft.ToSmartAxis(axis);
            if (changed == DropAxis)
            {
                ResizeDownSlopeAnchoredForward(HandleExtentDelta(sign, delta) * SlopeLength);
                return;
            }

            if (changed == ForwardAxis)
            {
                ResizeDownSlopeAnchoredForward(AnchoredHandleDelta(sign, ForwardSign, delta));
                return;
            }

            if (changed == RightAxis)
            {
                ResizeDownSlopeAnchoredWidth(AnchoredHandleDelta(sign, RightSign, delta));
                return;
            }

            ResizeFromHandle(axis, sign, delta);
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
            if (ShapeKind == SmartBuildShapeKind.DownSlope)
                RecenterDownSlopeAround(pivot);
            else if (IsGeneratedShape)
                RecenterGeneratedAround(pivot);
            else
                RecenterFixedGeometryAround(pivot);
        }

        internal void FlipForward()
        {
            if (ShapeKind != SmartBuildShapeKind.DownSlope && !IsFixedGeometry && !IsGeneratedShape)
                return;

            Vector3 pivot = RotationPivotLocal;
            Vector3i forward = Multiply(_forwardStep, -1);
            SetBasis(forward, _dropStep, Cross(forward, _dropStep));
            if (ShapeKind == SmartBuildShapeKind.DownSlope)
                RecenterDownSlopeAround(pivot);
            else if (IsGeneratedShape)
                RecenterGeneratedAround(pivot);
            else
                RecenterFixedGeometryAround(pivot);
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

            if (IsFixedGeometry)
                return EnumerateFixedGeometryCells(null);

            if (IsGeneratedShape)
                return EnumerateGeneratedCells();

            return EnumerateDownSlopeCells(includeSupport: true);
        }

        internal IEnumerable<Vector3i> EnumeratePreviewCells(SmartBuildSource source)
        {
            if (ShapeKind == SmartBuildShapeKind.Cuboid)
                return Bounds.EnumerateCells();

            if (IsFixedGeometry)
                return EnumerateFixedGeometryCells(source);

            if (IsGeneratedShape)
                return EnumerateGeneratedCells();

            return EnumerateDownSlopeCells(includeSupport: true);
        }

        internal IEnumerable<Vector3i> EnumeratePlacementCells()
        {
            if (ShapeKind == SmartBuildShapeKind.Cuboid)
                return Bounds.EnumerateCells();

            if (IsFixedGeometry)
                return EnumerateFixedGeometryCells(null);

            if (IsGeneratedShape)
                return EnumerateGeneratedCells();

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
            if (IsFixedGeometry)
                return BuildFixedGeometryPlacements(source, out reason);

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

        private IReadOnlyList<SmartBuildPlacement> BuildFixedGeometryPlacements(
            SmartBuildSource source,
            out string reason)
        {
            reason = null;
            SmartBuildShapeDescriptor descriptor = Descriptor();
            SmartBlockFamily family = source?.FamilyForShape(descriptor);
            if (family == null || !family.IsSupported)
            {
                reason = family?.UnsupportedReason ??
                         descriptor?.Label + " blocks are unavailable for this material.";
                return Array.Empty<SmartBuildPlacement>();
            }

            SmartBlockCandidate candidate = family.CandidateForLength(SelectedLength);
            if (candidate == null)
            {
                reason = descriptor?.Label + " " +
                         SelectedLength.ToString(CultureInfo.InvariantCulture) +
                         "m is unavailable for this material.";
                return Array.Empty<SmartBuildPlacement>();
            }

            Quaternion rotation = FixedGeometryRotation();
            Vector3i[] baseFootprint = candidate.CoveredCellsFrom(Origin, rotation).ToArray();
            if (baseFootprint.Length == 0)
                baseFootprint = new[] { Origin };

            int rightStride = Math.Max(1, FootprintExtent(baseFootprint, RightAxis));
            int dropStride = Math.Max(1, FootprintExtent(baseFootprint, DropAxis));
            IReadOnlyList<SmartBlockCandidate> run = PackFixedGeometryRun(family, _fixedForwardCells);
            if (run.Count == 0)
            {
                reason = descriptor?.Label + " cannot pack " +
                         _fixedForwardCells.ToString(CultureInfo.InvariantCulture) +
                         " forward cell(s) with available length variants.";
                return Array.Empty<SmartBuildPlacement>();
            }

            var placements = new List<SmartBuildPlacement>();
            foreach (Vector3i laneOrigin in EnumerateFixedGeometryLanes(rightStride, dropStride))
                AddFixedGeometryRunPlacements(placements, laneOrigin, run, rotation);

            return placements;
        }

        private static IReadOnlyList<SmartBlockCandidate> PackFixedGeometryRun(
            SmartBlockFamily family,
            int forwardCells)
        {
            SmartBlockCandidate[] candidates = family?.Candidates
                .Where(candidate => candidate != null && candidate.Length >= 1)
                .GroupBy(candidate => candidate.Length)
                .Select(group => group.First())
                .OrderByDescending(candidate => candidate.Length)
                .ToArray() ?? Array.Empty<SmartBlockCandidate>();
            if (candidates.Length == 0)
                return Array.Empty<SmartBlockCandidate>();

            int remaining = Math.Max(1, forwardCells);
            var run = new List<SmartBlockCandidate>();
            while (remaining > 0)
            {
                SmartBlockCandidate chosen = candidates
                    .FirstOrDefault(candidate => candidate.Length <= remaining);
                if (chosen == null)
                    return Array.Empty<SmartBlockCandidate>();

                run.Add(chosen);
                remaining -= chosen.Length;
            }

            return run;
        }

        private void AddFixedGeometryRunPlacements(
            List<SmartBuildPlacement> placements,
            Vector3i laneOrigin,
            IReadOnlyList<SmartBlockCandidate> run,
            Quaternion rotation)
        {
            int forwardOffset = 0;
            foreach (SmartBlockCandidate runCandidate in run ?? Array.Empty<SmartBlockCandidate>())
            {
                Vector3i tileOrigin = laneOrigin + Multiply(_forwardStep, forwardOffset);
                Vector3i[] cells = runCandidate.CoveredCellsFrom(tileOrigin, rotation).ToArray();
                placements.Add(new SmartBuildPlacement(
                    tileOrigin,
                    runCandidate,
                    ForwardAxis,
                    ForwardSign,
                    rotation,
                    cells,
                    runCandidate.DisplayName));
                forwardOffset += Math.Max(1, runCandidate.Length);
            }
        }

        internal IReadOnlyList<SmartBuildPlacement> BuildGeneratedSmoothingPlacements(
            SmartBuildSource source,
            out IReadOnlyList<Vector3i> replacedCells,
            out IReadOnlyList<string> warnings)
        {
            replacedCells = Array.Empty<Vector3i>();
            warnings = Array.Empty<string>();
            if (!IsGeneratedShape ||
                _generatorSmoothingMode != SmartBuildGeneratorSmoothingMode.PerimeterDownSlope)
            {
                return Array.Empty<SmartBuildPlacement>();
            }

            var warningList = new List<string>();
            SmartBlockFamily family = source?.DownSlopeFamily;
            SmartBlockCandidate candidate = family?.CandidateForLength(1);
            if (family == null || !family.IsSupported || candidate == null || candidate.Length != 1)
            {
                warningList.Add("Generator perimeter smoothing skipped; 1m down-slope blocks are unavailable for this material.");
                warnings = warningList;
                return Array.Empty<SmartBuildPlacement>();
            }

            if (DecoLimitLifter.EsuSymmetry.ActivePlanes.Keys.Any(axis => axis == DecorationEditAxis.Y))
            {
                warningList.Add("Generator perimeter smoothing skipped while Y symmetry is active.");
                warnings = warningList;
                return Array.Empty<SmartBuildPlacement>();
            }

            Vector3i[] perimeter = EnumerateGeneratedPerimeterCells().ToArray();
            if (perimeter.Length == 0)
            {
                warnings = warningList;
                return Array.Empty<SmartBuildPlacement>();
            }

            var placements = new List<SmartBuildPlacement>();
            var replaced = new List<Vector3i>();
            var seen = new HashSet<string>();
            foreach (Vector3i cell in perimeter)
            {
                if (!TryGeneratedOutwardAxis(cell, out SmartBuildAxis axis, out int sign))
                    continue;

                string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                if (!seen.Add(key))
                    continue;

                placements.Add(new SmartBuildPlacement(
                    cell,
                    candidate,
                    axis,
                    sign,
                    GeneratedDownSlopeRotation(axis, sign),
                    new[] { cell },
                    candidate.DisplayName));
                replaced.Add(cell);
            }

            if (placements.Count == 0)
                warningList.Add("Generator perimeter smoothing found no outward horizontal perimeter cells.");

            replacedCells = replaced;
            warnings = warningList;
            return placements;
        }

        private IEnumerable<Vector3i> EnumerateGeneratedCells()
        {
            if (!IsGeneratedShape)
                return Array.Empty<Vector3i>();

            switch (ShapeKind)
            {
                case SmartBuildShapeKind.GeneratedSphere:
                    return EnumerateGeneratedSphereCells();
                case SmartBuildShapeKind.GeneratedPolygon:
                    return EnumerateGeneratedPolygonCells();
                default:
                    return EnumerateGeneratedCircleCells();
            }
        }

        private IEnumerable<Vector3i> EnumerateGeneratedCellsFrom(Vector3i origin)
        {
            Vector3i current = Origin;
            foreach (Vector3i cell in EnumerateGeneratedCells())
                yield return origin + (cell - current);
        }

        private IEnumerable<Vector3i> EnumerateGeneratedCircleCells()
        {
            for (int drop = 0; drop < _cuboidSize.z; drop++)
            {
                for (int right = 0; right < _cuboidSize.y; right++)
                {
                    for (int forward = 0; forward < _cuboidSize.x; forward++)
                    {
                        if (!CircleInside(forward, right))
                            continue;
                        if (_generatorFillMode == SmartBuildGeneratorFillMode.OutlineShell &&
                            !CircleBoundary(forward, right))
                        {
                            continue;
                        }

                        yield return GeneratedCell(forward, right, drop);
                    }
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateGeneratedPolygonCells()
        {
            int sides = ClampGeneratorSides(_generatorSides);
            if (sides <= 1)
                return EnumerateGeneratedRayCells();
            if (sides == 2)
                return EnumerateGeneratedDiameterCells();

            Vector2[] vertices = PolygonVertices(sides);
            return EnumerateGeneratedPolygonCells(vertices);
        }

        private IEnumerable<Vector3i> EnumerateGeneratedRayCells()
        {
            int centerRight = Mathf.Clamp(Mathf.RoundToInt((_cuboidSize.y - 1) * 0.5f), 0, _cuboidSize.y - 1);
            int start = Mathf.Clamp(Mathf.RoundToInt((_cuboidSize.x - 1) * 0.5f), 0, _cuboidSize.x - 1);
            for (int drop = 0; drop < _cuboidSize.z; drop++)
                for (int forward = start; forward < _cuboidSize.x; forward++)
                    yield return GeneratedCell(forward, centerRight, drop);
        }

        private IEnumerable<Vector3i> EnumerateGeneratedDiameterCells()
        {
            int centerRight = Mathf.Clamp(Mathf.RoundToInt((_cuboidSize.y - 1) * 0.5f), 0, _cuboidSize.y - 1);
            for (int drop = 0; drop < _cuboidSize.z; drop++)
                for (int forward = 0; forward < _cuboidSize.x; forward++)
                    yield return GeneratedCell(forward, centerRight, drop);
        }

        private IEnumerable<Vector3i> EnumerateGeneratedPolygonCells(Vector2[] vertices)
        {
            for (int drop = 0; drop < _cuboidSize.z; drop++)
            {
                for (int right = 0; right < _cuboidSize.y; right++)
                {
                    for (int forward = 0; forward < _cuboidSize.x; forward++)
                    {
                        if (!PolygonInside(vertices, forward, right))
                            continue;
                        if (_generatorFillMode == SmartBuildGeneratorFillMode.OutlineShell &&
                            !PolygonBoundary(vertices, forward, right))
                        {
                            continue;
                        }

                        yield return GeneratedCell(forward, right, drop);
                    }
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateGeneratedSphereCells()
        {
            for (int drop = 0; drop < _cuboidSize.z; drop++)
            {
                for (int right = 0; right < _cuboidSize.y; right++)
                {
                    for (int forward = 0; forward < _cuboidSize.x; forward++)
                    {
                        if (!SphereInside(forward, right, drop))
                            continue;
                        if (_generatorFillMode == SmartBuildGeneratorFillMode.OutlineShell &&
                            !SphereBoundary(forward, right, drop))
                        {
                            continue;
                        }

                        yield return GeneratedCell(forward, right, drop);
                    }
                }
            }
        }

        private IEnumerable<Vector3i> EnumerateGeneratedPerimeterCells()
        {
            Vector3i[] cells = EnumerateGeneratedCells().ToArray();
            if (cells.Length == 0)
                return Array.Empty<Vector3i>();

            var all = new HashSet<string>(cells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
            return cells.Where(cell =>
            {
                Vector3i delta = cell - Origin;
                int forward = Dot(delta, _forwardStep);
                int right = Dot(delta, _rightStep);
                return !all.Contains(DecoLimitLifter.EsuSymmetry.CellKey(GeneratedCell(forward + 1, right, Dot(delta, _dropStep)))) ||
                       !all.Contains(DecoLimitLifter.EsuSymmetry.CellKey(GeneratedCell(forward - 1, right, Dot(delta, _dropStep)))) ||
                       !all.Contains(DecoLimitLifter.EsuSymmetry.CellKey(GeneratedCell(forward, right + 1, Dot(delta, _dropStep)))) ||
                       !all.Contains(DecoLimitLifter.EsuSymmetry.CellKey(GeneratedCell(forward, right - 1, Dot(delta, _dropStep))));
            });
        }

        private bool CircleInside(int forward, int right)
        {
            NormalizedEllipsePoint(forward, right, out float x, out float y);
            return x * x + y * y <= 1.0001f;
        }

        private bool CircleBoundary(int forward, int right) =>
            !CircleInside(forward + 1, right) ||
            !CircleInside(forward - 1, right) ||
            !CircleInside(forward, right + 1) ||
            !CircleInside(forward, right - 1);

        private void NormalizedEllipsePoint(int forward, int right, out float x, out float y)
        {
            float radiusForward = Math.Max(0.5f, (_cuboidSize.x - 1) * 0.5f);
            float radiusRight = Math.Max(0.5f, (_cuboidSize.y - 1) * 0.5f);
            x = (forward - (_cuboidSize.x - 1) * 0.5f) / radiusForward;
            y = (right - (_cuboidSize.y - 1) * 0.5f) / radiusRight;
        }

        private Vector2[] PolygonVertices(int sides)
        {
            sides = ClampGeneratorSides(sides);
            var vertices = new Vector2[sides];
            float centerForward = (_cuboidSize.x - 1) * 0.5f;
            float centerRight = (_cuboidSize.y - 1) * 0.5f;
            float radiusForward = Math.Max(0.5f, (_cuboidSize.x - 1) * 0.5f);
            float radiusRight = Math.Max(0.5f, (_cuboidSize.y - 1) * 0.5f);
            for (int index = 0; index < sides; index++)
            {
                float angle = Mathf.PI * 2f * index / sides;
                vertices[index] = new Vector2(
                    centerForward + Mathf.Cos(angle) * radiusForward,
                    centerRight + Mathf.Sin(angle) * radiusRight);
            }

            return vertices;
        }

        private static bool PolygonInside(Vector2[] vertices, int forward, int right)
        {
            if (vertices == null || vertices.Length < 3)
                return false;

            float x = forward;
            float y = right;
            bool inside = false;
            for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
            {
                Vector2 vi = vertices[i];
                Vector2 vj = vertices[j];
                float denominator = vj.y - vi.y;
                if (Math.Abs(denominator) <= 0.0001f)
                    denominator = denominator >= 0f ? 0.0001f : -0.0001f;
                if (((vi.y > y) != (vj.y > y)) &&
                    x < (vj.x - vi.x) * (y - vi.y) / denominator + vi.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool PolygonBoundary(Vector2[] vertices, int forward, int right) =>
            !PolygonInside(vertices, forward + 1, right) ||
            !PolygonInside(vertices, forward - 1, right) ||
            !PolygonInside(vertices, forward, right + 1) ||
            !PolygonInside(vertices, forward, right - 1);

        private bool SphereInside(int forward, int right, int drop)
        {
            NormalizedSpherePoint(forward, right, drop, out float x, out float y, out float z);
            return x * x + y * y + z * z <= 1.0001f;
        }

        private bool SphereBoundary(int forward, int right, int drop) =>
            !SphereInside(forward + 1, right, drop) ||
            !SphereInside(forward - 1, right, drop) ||
            !SphereInside(forward, right + 1, drop) ||
            !SphereInside(forward, right - 1, drop) ||
            !SphereInside(forward, right, drop + 1) ||
            !SphereInside(forward, right, drop - 1);

        private void NormalizedSpherePoint(int forward, int right, int drop, out float x, out float y, out float z)
        {
            float radiusForward = Math.Max(0.5f, (_cuboidSize.x - 1) * 0.5f);
            float radiusRight = Math.Max(0.5f, (_cuboidSize.y - 1) * 0.5f);
            float radiusDrop = Math.Max(0.5f, (_cuboidSize.z - 1) * 0.5f);
            x = (forward - (_cuboidSize.x - 1) * 0.5f) / radiusForward;
            y = (right - (_cuboidSize.y - 1) * 0.5f) / radiusRight;
            z = (drop - (_cuboidSize.z - 1) * 0.5f) / radiusDrop;
        }

        private Vector3i GeneratedCell(int forward, int right, int drop) =>
            Origin +
            Multiply(_forwardStep, forward) +
            Multiply(_rightStep, right) +
            Multiply(_dropStep, drop);

        private bool TryGeneratedOutwardAxis(Vector3i cell, out SmartBuildAxis axis, out int sign)
        {
            Vector3i delta = cell - Origin;
            int forward = Dot(delta, _forwardStep);
            int right = Dot(delta, _rightStep);
            float centerForward = (_cuboidSize.x - 1) * 0.5f;
            float centerRight = (_cuboidSize.y - 1) * 0.5f;
            float forwardOffset = forward - centerForward;
            float rightOffset = right - centerRight;
            if (Math.Abs(forwardOffset) <= 0.001f && Math.Abs(rightOffset) <= 0.001f)
            {
                axis = ForwardAxis;
                sign = ForwardSign;
                return false;
            }

            float forwardWeight = Math.Abs(forwardOffset) / Math.Max(1f, centerForward);
            float rightWeight = Math.Abs(rightOffset) / Math.Max(1f, centerRight);
            if (forwardWeight >= rightWeight)
            {
                axis = ForwardAxis;
                sign = forwardOffset >= 0f ? ForwardSign : -ForwardSign;
                return axis != SmartBuildAxis.Y;
            }

            axis = RightAxis;
            sign = rightOffset >= 0f ? RightSign : -RightSign;
            return axis != SmartBuildAxis.Y;
        }

        private static Quaternion GeneratedDownSlopeRotation(SmartBuildAxis axis, int sign)
        {
            Vector3i forward = SmartBuildAxisHelper.ToVector3i(
                axis == SmartBuildAxis.X ? SmartBuildAxis.X : SmartBuildAxis.Z,
                sign >= 0 ? 1 : -1);
            Vector3i drop = new Vector3i(0, -1, 0);
            Vector3i right = Cross(forward, drop);
            return QuaternionFromBasis(right, Multiply(drop, -1), forward);
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

        private void ResizeDownSlopeAnchoredForward(int delta)
        {
            int currentRun = Math.Max(SlopeLength, SlopeSteps * SlopeLength);
            int nextRun = Math.Max(SlopeLength, currentRun + delta);
            SlopeSteps = Math.Max(1, (nextRun + SlopeLength - 1) / SlopeLength);
        }

        private void ResizeDownSlopeAnchoredWidth(int delta)
        {
            SlopeWidth = Math.Max(1, SlopeWidth + delta);
        }

        private static int HandleExtentDelta(int sign, int delta) =>
            sign >= 0 ? delta : -delta;

        private static int AnchoredHandleDelta(int sign, int anchoredSign, int delta)
        {
            int extentDelta = HandleExtentDelta(sign, delta);
            return sign == anchoredSign ? extentDelta : -extentDelta;
        }

        private void ApplyFixedGeometryBounds(
            SmartBuildBounds bounds,
            DecorationEditAxis resizedAxis,
            int resizedSign)
        {
            bool hasResizedAxis = resizedAxis != DecorationEditAxis.None &&
                                  resizedAxis != DecorationEditAxis.Free;
            SmartBuildAxis changed = SmartBuildDraft.ToSmartAxis(resizedAxis);
            int baseForward = Math.Max(1, SelectedLength);
            int runExtent = Math.Max(1, bounds.Extent(ForwardAxis));
            int rightExtent = Math.Max(1, bounds.Extent(RightAxis));
            int dropExtent = Math.Max(1, bounds.Extent(DropAxis));

            if (!hasResizedAxis || changed == ForwardAxis)
            {
                _fixedForwardCells = runExtent;
                _fixedForwardTiles = Math.Max(1, (runExtent + baseForward - 1) / baseForward);
            }
            if (!hasResizedAxis || changed == RightAxis)
                _fixedRightTiles = Math.Max(1, rightExtent);
            if (!hasResizedAxis || changed == DropAxis)
                _fixedDropTiles = Math.Max(1, dropExtent);

            int finalRun = Math.Max(1, _fixedForwardCells);
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
                _fixedRightTiles,
                hasResizedAxis && changed == RightAxis ? resizedSign : 0);
            int startDrop = StartComponentFromBounds(
                bounds,
                DropAxis,
                DropSign,
                _fixedDropTiles,
                hasResizedAxis && changed == DropAxis ? resizedSign : 0);

            Vector3i origin = new Vector3i(0, 0, 0);
            origin = SmartBuildAxisHelper.Set(origin, ForwardAxis, startForward);
            origin = SmartBuildAxisHelper.Set(origin, RightAxis, startRight);
            origin = SmartBuildAxisHelper.Set(origin, DropAxis, startDrop);
            Origin = origin;
        }

        private void ApplyGeneratedBounds(
            SmartBuildBounds bounds,
            DecorationEditAxis resizedAxis,
            int resizedSign)
        {
            bool hasResizedAxis = resizedAxis != DecorationEditAxis.None &&
                                  resizedAxis != DecorationEditAxis.Free;
            SmartBuildAxis changed = SmartBuildDraft.ToSmartAxis(resizedAxis);
            int forwardExtent = Math.Max(1, bounds.Extent(ForwardAxis));
            int rightExtent = Math.Max(1, bounds.Extent(RightAxis));
            int dropExtent = Math.Max(1, bounds.Extent(DropAxis));

            if (_generatorRoundLock && ShapeKind == SmartBuildShapeKind.GeneratedCircle)
            {
                int diameter = Math.Max(forwardExtent, rightExtent);
                forwardExtent = diameter;
                rightExtent = diameter;
            }
            else if (_generatorRoundLock && ShapeKind == SmartBuildShapeKind.GeneratedSphere)
            {
                int diameter = Math.Max(forwardExtent, Math.Max(rightExtent, dropExtent));
                forwardExtent = diameter;
                rightExtent = diameter;
                dropExtent = diameter;
            }

            int startForward = StartGeneratedComponent(
                bounds,
                ForwardAxis,
                ForwardSign,
                forwardExtent,
                hasResizedAxis && changed == ForwardAxis,
                resizedSign);
            int startRight = StartGeneratedComponent(
                bounds,
                RightAxis,
                RightSign,
                rightExtent,
                hasResizedAxis && changed == RightAxis,
                resizedSign);
            int startDrop = StartGeneratedComponent(
                bounds,
                DropAxis,
                DropSign,
                dropExtent,
                hasResizedAxis && changed == DropAxis,
                resizedSign);

            Vector3i origin = new Vector3i(0, 0, 0);
            origin = SmartBuildAxisHelper.Set(origin, ForwardAxis, startForward);
            origin = SmartBuildAxisHelper.Set(origin, RightAxis, startRight);
            origin = SmartBuildAxisHelper.Set(origin, DropAxis, startDrop);
            Origin = origin;
            _cuboidSize = new Vector3i(forwardExtent, rightExtent, dropExtent);
        }

        private SmartBuildBounds GeneratorControlBounds()
        {
            Vector3i forward = Multiply(_forwardStep, Math.Max(0, _cuboidSize.x - 1));
            Vector3i right = Multiply(_rightStep, Math.Max(0, _cuboidSize.y - 1));
            Vector3i drop = Multiply(_dropStep, Math.Max(0, _cuboidSize.z - 1));
            return BoundsFromCells(new[]
            {
                Origin,
                Origin + forward,
                Origin + right,
                Origin + drop,
                Origin + forward + right,
                Origin + forward + drop,
                Origin + right + drop,
                Origin + forward + right + drop
            });
        }

        private static int StartGeneratedComponent(
            SmartBuildBounds bounds,
            SmartBuildAxis axis,
            int directionSign,
            int extent,
            bool draggedAxis,
            int resizedSign)
        {
            if (draggedAxis)
            {
                return StartComponentFromBounds(
                    bounds,
                    axis,
                    directionSign,
                    extent,
                    resizedSign);
            }

            return StartComponentCenteredOnBounds(bounds, axis, directionSign, extent);
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

        private void RecenterFixedGeometryAround(Vector3 targetCenter)
        {
            SmartBuildBounds relativeBounds = BoundsFromCells(
                EnumerateFixedGeometryCellsFrom(new Vector3i(0, 0, 0), null));
            Vector3 origin = targetCenter - relativeBounds.Center;
            Origin = RoundToCell(origin);
        }

        private void RecenterGeneratedAround(Vector3 targetCenter)
        {
            Vector3 origin = targetCenter - BoundsFromCells(EnumerateGeneratedCellsFrom(new Vector3i(0, 0, 0))).Center;
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

        private static int StartComponentCenteredOnBounds(
            SmartBuildBounds bounds,
            SmartBuildAxis axis,
            int directionSign,
            int extent)
        {
            int min = SmartBuildAxisHelper.Get(bounds.Min, axis);
            int max = SmartBuildAxisHelper.Get(bounds.Max, axis);
            float center = (min + max) * 0.5f;
            int direction = directionSign >= 0 ? 1 : -1;
            float start = center - direction * (Math.Max(1, extent) - 1) * 0.5f;
            return Mathf.RoundToInt(start);
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

        private IEnumerable<Vector3i> EnumerateFixedGeometryCells(SmartBuildSource source)
        {
            return EnumerateFixedGeometryCellsFrom(Origin, source);
        }

        private IEnumerable<Vector3i> EnumerateFixedGeometryCellsFrom(
            Vector3i origin,
            SmartBuildSource source)
        {
            if (source != null)
            {
                IReadOnlyList<SmartBuildPlacement> placements = BuildFixedGeometryPlacements(
                    source,
                    out string reason);
                if (placements.Count > 0 && string.IsNullOrWhiteSpace(reason))
                {
                    var placed = new HashSet<string>();
                    foreach (SmartBuildPlacement placement in placements)
                    {
                        foreach (Vector3i cell in placement.CoveredCells())
                        {
                            if (placed.Add(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                                yield return cell;
                        }
                    }

                    yield break;
                }
            }

            foreach (Vector3i cell in FallbackFixedGeometryFootprint(origin))
                yield return cell;
        }

        private IEnumerable<Vector3i> FallbackFixedGeometryFootprint(Vector3i origin)
        {
            int length = Math.Max(1, _fixedForwardCells);
            for (int drop = 0; drop < _fixedDropTiles; drop++)
                for (int right = 0; right < _fixedRightTiles; right++)
                    for (int index = 0; index < length; index++)
                    {
                        yield return origin +
                                     Multiply(_forwardStep, index) +
                                     Multiply(_rightStep, right) +
                                     Multiply(_dropStep, drop);
                    }
        }

        private IEnumerable<Vector3i> EnumerateFixedGeometryOrigins(
            int forwardStride,
            int rightStride,
            int dropStride)
        {
            return EnumerateFixedGeometryOrigins(Origin, forwardStride, rightStride, dropStride);
        }

        private IEnumerable<Vector3i> EnumerateFixedGeometryOrigins(
            Vector3i origin,
            int forwardStride,
            int rightStride,
            int dropStride)
        {
            for (int drop = 0; drop < _fixedDropTiles; drop++)
                for (int right = 0; right < _fixedRightTiles; right++)
                    for (int forward = 0; forward < _fixedForwardTiles; forward++)
                    {
                        yield return origin +
                                     Multiply(_forwardStep, forward * Math.Max(1, forwardStride)) +
                                     Multiply(_rightStep, right * Math.Max(1, rightStride)) +
                                     Multiply(_dropStep, drop * Math.Max(1, dropStride));
                    }
        }

        private IEnumerable<Vector3i> EnumerateFixedGeometryLanes(
            int rightStride,
            int dropStride)
        {
            for (int drop = 0; drop < _fixedDropTiles; drop++)
                for (int right = 0; right < _fixedRightTiles; right++)
                {
                    yield return Origin +
                                 Multiply(_rightStep, right * Math.Max(1, rightStride)) +
                                 Multiply(_dropStep, drop * Math.Max(1, dropStride));
                }
        }

        private Quaternion FixedGeometryRotation() => DownSlopeRotation();

        private static int FootprintExtent(IEnumerable<Vector3i> cells, SmartBuildAxis axis)
        {
            Vector3i[] values = cells?.ToArray() ?? Array.Empty<Vector3i>();
            if (values.Length == 0)
                return 1;

            int min = SmartBuildAxisHelper.Get(values[0], axis);
            int max = min;
            for (int index = 1; index < values.Length; index++)
            {
                int component = SmartBuildAxisHelper.Get(values[index], axis);
                min = Math.Min(min, component);
                max = Math.Max(max, component);
            }

            return max - min + 1;
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

        private static string DescriptorKeyForKind(SmartBuildShapeKind kind)
        {
            switch (kind)
            {
                case SmartBuildShapeKind.DownSlope:
                    return SmartBuildShapeDescriptors.DownSlopeKey;
                case SmartBuildShapeKind.Cuboid:
                    return SmartBuildShapeDescriptors.CuboidKey;
                case SmartBuildShapeKind.GeneratedCircle:
                    return "generated-circle";
                case SmartBuildShapeKind.GeneratedPolygon:
                    return "generated-polygon";
                case SmartBuildShapeKind.GeneratedSphere:
                    return "generated-sphere";
                default:
                    return kind.ToString();
            }
        }

        private static bool IsGeneratedShapeKind(SmartBuildShapeKind kind) =>
            kind == SmartBuildShapeKind.GeneratedCircle ||
            kind == SmartBuildShapeKind.GeneratedPolygon ||
            kind == SmartBuildShapeKind.GeneratedSphere;

        private static int DefaultGeneratorSides(SmartBuildShapeKind kind) =>
            kind == SmartBuildShapeKind.GeneratedPolygon ? 8 : 0;

        private static int ClampGeneratorSides(int sides) =>
            Mathf.Clamp(sides <= 0 ? 8 : sides, 1, 64);

        private string GeneratedShapeLabel(SmartBuildShapeDescriptor descriptor)
        {
            if (ShapeKind == SmartBuildShapeKind.GeneratedPolygon)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}-sided polygon",
                    _generatorSides);
            }

            return descriptor?.Label ?? ShapeKind.ToString();
        }

        private void ApplyRoundGeneratorSize()
        {
            if (ShapeKind == SmartBuildShapeKind.GeneratedCircle)
            {
                int diameter = Math.Max(_cuboidSize.x, _cuboidSize.y);
                _cuboidSize = new Vector3i(diameter, diameter, Math.Max(1, _cuboidSize.z));
            }
            else if (ShapeKind == SmartBuildShapeKind.GeneratedSphere)
            {
                int diameter = Math.Max(_cuboidSize.x, Math.Max(_cuboidSize.y, _cuboidSize.z));
                _cuboidSize = new Vector3i(diameter, diameter, diameter);
            }
        }

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
