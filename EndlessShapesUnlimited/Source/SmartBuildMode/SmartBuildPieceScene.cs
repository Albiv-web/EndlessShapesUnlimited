using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal interface ISmartBuildPattern
    {
        SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source);
    }

    internal sealed class SmartBuildPatternResult
    {
        internal SmartBuildPatternResult(
            IEnumerable<Vector3i> cuboidCells,
            IEnumerable<SmartBuildPlacement> fixedPlacements,
            string failureReason = null,
            IEnumerable<string> warnings = null)
        {
            CuboidCells = (cuboidCells ?? Array.Empty<Vector3i>()).ToArray();
            FixedPlacements = (fixedPlacements ?? Array.Empty<SmartBuildPlacement>()).ToArray();
            FailureReason = failureReason;
            Warnings = (warnings ?? Array.Empty<string>())
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .ToArray();
        }

        internal IReadOnlyList<Vector3i> CuboidCells { get; }

        internal IReadOnlyList<SmartBuildPlacement> FixedPlacements { get; }

        internal string FailureReason { get; }

        internal IReadOnlyList<string> Warnings { get; }

        internal bool Success => string.IsNullOrWhiteSpace(FailureReason);

        internal IEnumerable<Vector3i> PreviewCells =>
            CuboidCells.Concat(FixedPlacements.SelectMany(placement => placement.CoveredCells()));
    }

    internal sealed class SmartBuildPreviewSnapshot
    {
        internal SmartBuildPreviewSnapshot(
            IReadOnlyList<Vector3i> cells,
            IReadOnlyList<IReadOnlyList<Vector3i>> cellSets,
            IReadOnlyList<SmartBuildVolume> volumes)
        {
            Cells = cells ?? Array.Empty<Vector3i>();
            CellSets = cellSets ?? Array.Empty<IReadOnlyList<Vector3i>>();
            Volumes = volumes ?? Array.Empty<SmartBuildVolume>();
        }

        internal IReadOnlyList<Vector3i> Cells { get; }

        internal IReadOnlyList<IReadOnlyList<Vector3i>> CellSets { get; }

        internal IReadOnlyList<SmartBuildVolume> Volumes { get; }

        internal static SmartBuildPreviewSnapshot Empty =>
            new SmartBuildPreviewSnapshot(
                Array.Empty<Vector3i>(),
                Array.Empty<IReadOnlyList<Vector3i>>(),
                Array.Empty<SmartBuildVolume>());
    }

    internal sealed class SmartBuildPieceScene
    {
        private readonly List<SmartBuildPiece> _pieces = new List<SmartBuildPiece>();

        internal SmartBuildPieceScene(AllConstruct construct)
        {
            Construct = construct;
        }

        internal AllConstruct Construct { get; }

        internal IReadOnlyList<SmartBuildPiece> Pieces => _pieces;

        internal SmartBuildPiece SelectedPiece { get; private set; }

        internal int Count => _pieces.Count;

        internal bool HasSelection => SelectedPiece != null;

        internal bool HasDownSlope =>
            _pieces.Any(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope);

        internal bool HasFixedGeometry =>
            _pieces.Any(piece => piece.IsFixedGeometry);

        internal void Add(SmartBuildPiece piece)
        {
            if (piece == null)
                return;

            _pieces.Add(piece);
            SelectedPiece = piece;
        }

        internal bool Select(int id)
        {
            SmartBuildPiece piece = _pieces.FirstOrDefault(candidate => candidate.Id == id);
            if (piece == null)
                return false;

            SelectedPiece = piece;
            return true;
        }

        internal void ClearSelection() =>
            SelectedPiece = null;

        internal SmartBuildPiece DuplicateSelected(Vector3i offset)
        {
            if (SelectedPiece == null)
                return null;

            SmartBuildPiece duplicate = SelectedPiece.Duplicate(offset);
            Add(duplicate);
            return duplicate;
        }

        internal bool DeleteSelected()
        {
            if (SelectedPiece == null)
                return false;

            int index = _pieces.IndexOf(SelectedPiece);
            if (index < 0)
                return false;

            _pieces.RemoveAt(index);
            if (_pieces.Count == 0)
            {
                SelectedPiece = null;
                return true;
            }

            SelectedPiece = _pieces[Math.Min(index, _pieces.Count - 1)];
            return true;
        }

        internal void Clear()
        {
            _pieces.Clear();
            SelectedPiece = null;
        }

        internal void ReplaceWith(
            IEnumerable<SmartBuildPiece> pieces,
            int selectedId)
        {
            _pieces.Clear();
            foreach (SmartBuildPiece piece in pieces ?? Array.Empty<SmartBuildPiece>())
            {
                if (piece != null)
                    _pieces.Add(piece);
            }

            SelectedPiece = selectedId >= 0
                ? _pieces.FirstOrDefault(piece => piece.Id == selectedId) ??
                  _pieces.LastOrDefault()
                : null;
        }

        internal SmartBuildPreviewSnapshot BuildPreview(SmartBuildSource source = null)
        {
            if (Construct == null || _pieces.Count == 0)
                return SmartBuildPreviewSnapshot.Empty;

            var allCells = new Dictionary<string, Vector3i>();
            var sets = new List<IReadOnlyList<Vector3i>>();
            var volumes = new List<SmartBuildVolume>();
            var seenSets = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                Vector3i[] cells = _pieces
                    .SelectMany(piece => piece.EnumeratePreviewCells(source))
                    .Select(variant.Mirror)
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray();
                if (cells.Length == 0)
                    continue;

                string signature = string.Join(
                    "|",
                    cells.Select(DecoLimitLifter.EsuSymmetry.CellKey).ToArray());
                if (!seenSets.Add(signature))
                    continue;

                sets.Add(cells);
                foreach (Vector3i cell in cells)
                    allCells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;

                SmartBuildVolume volume = VolumeFromCells(Construct, cells);
                if (volume != null)
                    volumes.Add(volume);
            }

            return new SmartBuildPreviewSnapshot(
                allCells.Values
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray(),
                sets,
                volumes);
        }

        internal SmartBuildPlan BuildPlan(
            SmartBuildSource source,
            Func<Vector3i, bool> isOccupied,
            SmartBuildPlannerOptions options,
            out SmartBuildPreviewSnapshot preview)
        {
            preview = BuildPreview(source);
            options ??= new SmartBuildPlannerOptions();
            if (Construct == null && !options.AllowNullConstructForVerification)
                return Failed("No valid construct is available.", preview);
            if (_pieces.Count == 0)
                return Failed("No preview piece is active.", preview);
            if (source == null)
                return Failed("Selected Smart Builder material is unavailable.", preview);
            if (source.Family == null || !source.Family.IsSupported)
                return Failed(
                    source.Family?.UnsupportedReason ??
                    "The selected material cannot be used by Smart Block Builder.",
                    preview);
            if (HasDownSlope &&
                !source.HasDownSlopeLength(_pieces.Where(piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope)
                    .Select(piece => piece.SlopeLength)))
            {
                return Failed("One or more down slope sizes are unavailable for this material.", preview);
            }

            if (HasDownSlope &&
                DecoLimitLifter.EsuSymmetry.ActivePlanes.Keys.Any(axis => axis == DecorationEditAxis.Y))
            {
                return Failed("Down slope previews cannot be mirrored across Y in this first slope pass.", preview);
            }

            string collision = FirstBaseCollision(source);
            if (!string.IsNullOrWhiteSpace(collision))
                return Failed(collision, preview);

            var fixedPlacements = new List<SmartBuildPlacement>();
            var cuboidCells = new Dictionary<string, Vector3i>();
            var skipped = new List<Vector3i>();
            var patternWarnings = new List<string>();
            var targetKeys = new HashSet<string>();
            var fixedSignatures = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                foreach (SmartBuildPiece piece in _pieces)
                {
                    SmartBuildPatternResult result = PatternFor(piece).Build(piece, source);
                    if (!result.Success)
                        return Failed(result.FailureReason, preview);
                    patternWarnings.AddRange(result.Warnings);

                    foreach (Vector3i cell in result.CuboidCells)
                    {
                        Vector3i mirrored = variant.Mirror(cell);
                        string key = DecoLimitLifter.EsuSymmetry.CellKey(mirrored);
                        if (targetKeys.Add(key))
                            cuboidCells[key] = mirrored;
                    }

                    foreach (SmartBuildPlacement placement in result.FixedPlacements)
                    {
                        SmartBuildPlacement mirrored = MirrorPlacement(placement, variant, source);
                        if (mirrored == null)
                        {
                            return Failed(
                                "A mirrored Smart Builder shape is unavailable for this material.",
                                preview,
                                skipped);
                        }

                        string signature = PlacementSignature(mirrored);
                        if (!fixedSignatures.Add(signature))
                            continue;

                        Vector3i[] occupied = mirrored.CoveredCells()
                            .Where(cell => isOccupied != null && isOccupied(cell))
                            .ToArray();
                        if (occupied.Length > 0)
                        {
                            skipped.AddRange(occupied);
                            if (!options.SkipOccupiedCells)
                                return Failed("The preview intersects existing blocks.", preview, skipped);
                            continue;
                        }

                        bool duplicate = false;
                        foreach (Vector3i cell in mirrored.CoveredCells())
                        {
                            string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                            if (!targetKeys.Add(key))
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (duplicate)
                            return Failed("Smart Builder preview pieces overlap each other.", preview, skipped);

                        fixedPlacements.Add(mirrored);
                    }
                }
            }

            List<SmartBuildPlacement> placements = new List<SmartBuildPlacement>(fixedPlacements);
            IReadOnlyList<Vector3i> supportCells = cuboidCells.Values
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ToArray();
            SmartBuildVolume referenceVolume = VolumeFromCells(
                Construct,
                preview.Cells.Count > 0
                    ? preview.Cells
                    : supportCells.Concat(fixedPlacements.SelectMany(placement => placement.CoveredCells())).ToArray());

            if (supportCells.Count > 0)
            {
                SmartBuildVolume supportVolume = VolumeFromCells(Construct, supportCells);
                if (supportVolume != null)
                {
                    SmartBuildPlan supportPlan = SmartBuildPlanner.BuildPlanFromCells(
                        supportVolume,
                        supportCells,
                        supportVolume.GrainAxis,
                        source.Family,
                        isOccupied,
                        options);
                    skipped.AddRange(supportPlan.SkippedCells);
                    if (!supportPlan.CanCommit)
                    {
                        return new SmartBuildPlan(
                            Construct,
                            referenceVolume,
                            Array.Empty<SmartBuildPlacement>(),
                            skipped,
                            supportPlan.Warnings,
                            canCommit: false,
                            failureReason: supportPlan.FailureReason);
                    }

                    placements.AddRange(supportPlan.Placements);
                }
            }

            if (placements.Count == 0)
                return Failed("No empty cells are available in the preview.", preview, skipped);

            var warnings = patternWarnings
                .GroupBy(warning => warning, StringComparer.Ordinal)
                .Select(group => group.Key)
                .ToList();
            if (placements.Count > options.HardPlacementCap)
            {
                return new SmartBuildPlan(
                    Construct,
                    referenceVolume,
                    placements,
                    skipped,
                    warnings,
                    canCommit: false,
                    failureReason:
                    $"The plan needs {placements.Count:N0} placements, above the {options.HardPlacementCap:N0} hard cap.");
            }

            if (placements.Count > options.WarningPlacementCap)
                warnings.Add($"Large plan: {placements.Count:N0} placements. Commit may hitch.");

            return new SmartBuildPlan(
                Construct,
                referenceVolume,
                placements,
                skipped
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray(),
                warnings,
                canCommit: true,
                failureReason: null);
        }

        private string FirstBaseCollision(SmartBuildSource source)
        {
            var seen = new Dictionary<string, int>();
            foreach (SmartBuildPiece piece in _pieces)
            {
                foreach (Vector3i cell in piece.EnumeratePreviewCells(source))
                {
                    string key = DecoLimitLifter.EsuSymmetry.CellKey(cell);
                    if (seen.TryGetValue(key, out int otherId) && otherId != piece.Id)
                        return "Smart Builder preview pieces overlap each other.";
                    seen[key] = piece.Id;
                }
            }

            return null;
        }

        private SmartBuildPlan Failed(
            string reason,
            SmartBuildPreviewSnapshot preview,
            IEnumerable<Vector3i> skippedCells = null) =>
            new SmartBuildPlan(
                Construct,
                VolumeFromCells(Construct, preview?.Cells),
                Array.Empty<SmartBuildPlacement>(),
                (skippedCells ?? Array.Empty<Vector3i>()).ToArray(),
                Array.Empty<string>(),
                canCommit: false,
                failureReason: reason);

        private static ISmartBuildPattern PatternFor(SmartBuildPiece piece)
        {
            switch (piece.ShapeKind)
            {
                case SmartBuildShapeKind.DownSlope:
                    return DownSlopePattern.Instance;
                case SmartBuildShapeKind.GeneratedCircle:
                case SmartBuildShapeKind.GeneratedPolygon:
                case SmartBuildShapeKind.GeneratedSphere:
                    return GeneratedPattern.Instance;
                default:
                    if (piece.IsFixedGeometry)
                        return FixedGeometryPattern.Instance;
                    return CuboidPattern.Instance;
            }
        }

        private static SmartBuildPlacement MirrorPlacement(
            SmartBuildPlacement placement,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            return MirrorPlacement(placement, variant, null);
        }

        internal static SmartBuildPlacement MirrorPlacement(
            SmartBuildPlacement placement,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            SmartBuildSource source)
        {
            if (variant.Axes.Count == 0)
                return placement;

            int sign = placement.AxisSign;
            if (variant.Axes.Any(axis => ToSmartAxis(axis) == placement.Axis))
                sign *= -1;

            SmartBlockCandidate candidate = MirrorCandidate(placement.Candidate, variant, source);
            if (candidate == null)
                return null;

            return new SmartBuildPlacement(
                variant.Mirror(placement.Position),
                candidate,
                placement.Axis,
                sign,
                MirrorRotation(placement.Rotation, variant),
                placement.CoveredCells().Select(variant.Mirror),
                candidate.DisplayName);
        }

        private static SmartBlockCandidate MirrorCandidate(
            SmartBlockCandidate candidate,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            SmartBuildSource source)
        {
            if (candidate == null)
                return null;

            SmartBuildShapeDescriptor descriptor = candidate.Descriptor;
            if (!SmartBuildShapeDescriptors.IsOddMirror(descriptor, variant.Axes))
                return candidate;

            SmartBuildShapeDescriptor mirror = descriptor.MirrorDescriptor();
            if (mirror == null || mirror.Key == descriptor.Key)
                return candidate;

            SmartBlockCandidate mirrored = source?
                .FamilyForShape(mirror)?
                .CandidateForLength(candidate.Length);
            return mirrored;
        }

        private static Quaternion MirrorRotation(
            Quaternion rotation,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            Vector3 forward = MirrorDirection(rotation * Vector3.forward, variant);
            Vector3 up = MirrorDirection(rotation * Vector3.up, variant);
            forward = Normalize(forward);
            up = Normalize(up);
            Vector3 right = Normalize(Cross(up, forward));
            if (forward.sqrMagnitude <= 0.0001f ||
                up.sqrMagnitude <= 0.0001f ||
                right.sqrMagnitude <= 0.0001f)
            {
                return rotation;
            }

            up = Normalize(Cross(forward, right));
            return QuaternionFromBasis(right, up, forward);
        }

        private static Vector3 MirrorDirection(
            Vector3 direction,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            foreach (DecorationEditAxis axis in variant.Axes)
            {
                switch (axis)
                {
                    case DecorationEditAxis.X:
                        direction.x = -direction.x;
                        break;
                    case DecorationEditAxis.Y:
                        direction.y = -direction.y;
                        break;
                    case DecorationEditAxis.Z:
                        direction.z = -direction.z;
                        break;
                }
            }

            return direction;
        }

        private static Vector3 Normalize(Vector3 value)
        {
            float squared = value.x * value.x + value.y * value.y + value.z * value.z;
            if (squared <= 0.0001f)
                return Vector3.zero;

            float scale = 1f / (float)Math.Sqrt(squared);
            return new Vector3(value.x * scale, value.y * scale, value.z * scale);
        }

        private static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);

        private static Quaternion QuaternionFromBasis(
            Vector3 right,
            Vector3 up,
            Vector3 forward)
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

        private static SmartBuildAxis ToSmartAxis(DecorationEditAxis axis)
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

        private static string PlacementSignature(SmartBuildPlacement placement) =>
            string.Join(
                "|",
                placement.CoveredCells()
                    .Select(DecoLimitLifter.EsuSymmetry.CellKey)
                    .OrderBy(key => key)
                    .ToArray()) +
            "|" +
            placement.Candidate?.DisplayName;

        private static SmartBuildVolume VolumeFromCells(
            AllConstruct construct,
            IEnumerable<Vector3i> rawCells)
        {
            Vector3i[] cells = rawCells?.ToArray() ?? Array.Empty<Vector3i>();
            if (cells.Length == 0)
                return null;

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

            return SmartBuildVolume.FromBounds(construct, min, max);
        }

        private sealed class CuboidPattern : ISmartBuildPattern
        {
            internal static readonly CuboidPattern Instance = new CuboidPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source) =>
                new SmartBuildPatternResult(piece.EnumeratePreviewCells(), Array.Empty<SmartBuildPlacement>());
        }

        private sealed class DownSlopePattern : ISmartBuildPattern
        {
            internal static readonly DownSlopePattern Instance = new DownSlopePattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(
                    source,
                    out string reason);
                if (!string.IsNullOrWhiteSpace(reason))
                    return new SmartBuildPatternResult(
                        Array.Empty<Vector3i>(),
                        Array.Empty<SmartBuildPlacement>(),
                        reason);

                return new SmartBuildPatternResult(
                    piece.EnumerateSupportCells(),
                    placements);
            }
        }

        private sealed class FixedGeometryPattern : ISmartBuildPattern
        {
            internal static readonly FixedGeometryPattern Instance = new FixedGeometryPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(
                    source,
                    out string reason);
                if (!string.IsNullOrWhiteSpace(reason))
                    return new SmartBuildPatternResult(
                        Array.Empty<Vector3i>(),
                        Array.Empty<SmartBuildPlacement>(),
                        reason);

                return new SmartBuildPatternResult(
                    Array.Empty<Vector3i>(),
                    placements);
            }
        }

        private sealed class GeneratedPattern : ISmartBuildPattern
        {
            internal static readonly GeneratedPattern Instance = new GeneratedPattern();

            public SmartBuildPatternResult Build(SmartBuildPiece piece, SmartBuildSource source)
            {
                Vector3i[] generatedCells = piece.EnumeratePreviewCells(source).ToArray();
                IReadOnlyList<SmartBuildPlacement> smoothingPlacements =
                    piece.BuildGeneratedSmoothingPlacements(
                        source,
                        out IReadOnlyList<Vector3i> replacedCells,
                        out IReadOnlyList<string> warnings);
                if (smoothingPlacements.Count == 0 || replacedCells.Count == 0)
                {
                    return new SmartBuildPatternResult(
                        generatedCells,
                        Array.Empty<SmartBuildPlacement>(),
                        warnings: warnings);
                }

                var replaced = new HashSet<string>(replacedCells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
                Vector3i[] cuboidCells = generatedCells
                    .Where(cell => !replaced.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell)))
                    .ToArray();
                return new SmartBuildPatternResult(
                    cuboidCells,
                    smoothingPlacements,
                    warnings: warnings);
            }
        }
    }
}
