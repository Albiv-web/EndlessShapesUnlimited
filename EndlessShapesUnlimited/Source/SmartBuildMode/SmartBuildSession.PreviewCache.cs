using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildSession
    {
        private const int MaximumPiecePreviewCacheEntries =
            SmartBuildLimits.MaximumSceneNodes * 8;

        private readonly Dictionary<SmartBuildPreviewCellCacheKey, Vector3i[]>
            _piecePreviewCellCache =
                new Dictionary<SmartBuildPreviewCellCacheKey, Vector3i[]>();
        private SmartBuildPieceScene _renderExpansionScene;
        private long _renderExpansionGeometryRevision = -1L;
        private IReadOnlyList<SmartBuildPiece> _renderExpandedPieces =
            Array.Empty<SmartBuildPiece>();

        private IReadOnlyList<SmartBuildPiece> PreviewPiecesForRendering()
        {
            if (_scene == null || _scene.Count == 0)
                return Array.Empty<SmartBuildPiece>();
            if (ReferenceEquals(_renderExpansionScene, _scene) &&
                _renderExpansionGeometryRevision == _scene.GeometryRevision)
            {
                return _renderExpandedPieces;
            }

            _renderExpansionScene = _scene;
            _renderExpansionGeometryRevision = _scene.GeometryRevision;
            _renderExpandedPieces = _scene.TryExpandSceneNodes(
                    out IReadOnlyList<SmartBuildPiece> expanded,
                    out _,
                    out _)
                ? expanded
                : _scene.Pieces;
            return _renderExpandedPieces;
        }

        private Vector3i[] CachedPreviewCells(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            out SmartBuildCellSetFingerprint fingerprint)
        {
            fingerprint = default;
            if (piece == null)
                return Array.Empty<Vector3i>();

            SmartBuildSource source = SourceForPiece(piece);
            var key = new SmartBuildPreviewCellCacheKey(
                piece.Id,
                piece.StructuralHash,
                source == null ? 0 : RuntimeHelpers.GetHashCode(source),
                SymmetryFingerprint(variant));
            if (!_piecePreviewCellCache.TryGetValue(key, out Vector3i[] cells))
            {
                cells = piece.EnumeratePreviewCells(source)
                    .Select(variant.Mirror)
                    .Distinct()
                    .ToArray();
                if (_piecePreviewCellCache.Count >= MaximumPiecePreviewCacheEntries)
                    _piecePreviewCellCache.Clear();
                _piecePreviewCellCache[key] = cells;
            }

            fingerprint = SmartBuildCellSetFingerprint.From(cells);
            return cells;
        }

        private static long SymmetryFingerprint(
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                HashSymmetryPoint(ref hash, variant.Mirror(new Vector3i(0, 0, 0)));
                HashSymmetryPoint(ref hash, variant.Mirror(new Vector3i(1, 0, 0)));
                HashSymmetryPoint(ref hash, variant.Mirror(new Vector3i(0, 1, 0)));
                HashSymmetryPoint(ref hash, variant.Mirror(new Vector3i(0, 0, 1)));
                return hash;
            }
        }

        private static void HashSymmetryPoint(ref long hash, Vector3i point)
        {
            unchecked
            {
                hash = (hash ^ point.x) * 1099511628211L;
                hash = (hash ^ point.y) * 1099511628211L;
                hash = (hash ^ point.z) * 1099511628211L;
            }
        }

        private readonly struct SmartBuildPreviewCellCacheKey : IEquatable<SmartBuildPreviewCellCacheKey>
        {
            internal SmartBuildPreviewCellCacheKey(
                int pieceId,
                long structuralHash,
                int sourceIdentity,
                long symmetryHash)
            {
                PieceId = pieceId;
                StructuralHash = structuralHash;
                SourceIdentity = sourceIdentity;
                SymmetryHash = symmetryHash;
            }

            private int PieceId { get; }
            private long StructuralHash { get; }
            private int SourceIdentity { get; }
            private long SymmetryHash { get; }

            public bool Equals(SmartBuildPreviewCellCacheKey other) =>
                PieceId == other.PieceId &&
                StructuralHash == other.StructuralHash &&
                SourceIdentity == other.SourceIdentity &&
                SymmetryHash == other.SymmetryHash;

            public override bool Equals(object obj) =>
                obj is SmartBuildPreviewCellCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PieceId;
                    hash = (hash * 397) ^ StructuralHash.GetHashCode();
                    hash = (hash * 397) ^ SourceIdentity;
                    hash = (hash * 397) ^ SymmetryHash.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct SmartBuildPreviewDrawKey : IEquatable<SmartBuildPreviewDrawKey>
        {
            internal SmartBuildPreviewDrawKey(
                int pieceId,
                SmartBuildCellSetFingerprint cells)
            {
                PieceId = pieceId;
                Cells = cells;
            }

            private int PieceId { get; }
            private SmartBuildCellSetFingerprint Cells { get; }

            public bool Equals(SmartBuildPreviewDrawKey other) =>
                PieceId == other.PieceId && Cells.Equals(other.Cells);

            public override bool Equals(object obj) =>
                obj is SmartBuildPreviewDrawKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (PieceId * 397) ^ Cells.GetHashCode();
                }
            }
        }

        private readonly struct SmartBuildVolumeKey : IEquatable<SmartBuildVolumeKey>
        {
            internal SmartBuildVolumeKey(Vector3i minimum, Vector3i maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            private Vector3i Minimum { get; }
            private Vector3i Maximum { get; }

            public bool Equals(SmartBuildVolumeKey other) =>
                Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum);

            public override bool Equals(object obj) =>
                obj is SmartBuildVolumeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Minimum.GetHashCode() * 397) ^ Maximum.GetHashCode();
                }
            }
        }

        private readonly struct SmartBuildCellSetFingerprint : IEquatable<SmartBuildCellSetFingerprint>
        {
            private SmartBuildCellSetFingerprint(int count, ulong xor, ulong sum)
            {
                Count = count;
                Xor = xor;
                Sum = sum;
            }

            private int Count { get; }
            private ulong Xor { get; }
            private ulong Sum { get; }

            internal bool IsEmpty => Count == 0;

            internal static SmartBuildCellSetFingerprint From(IEnumerable<Vector3i> cells)
            {
                int count = 0;
                ulong xor = 0UL;
                ulong sum = 0UL;
                foreach (Vector3i cell in cells ?? Array.Empty<Vector3i>())
                {
                    ulong cellHash = CellHash(cell);
                    xor ^= cellHash;
                    sum += cellHash * 0x9E3779B185EBCA87UL;
                    count++;
                }
                return new SmartBuildCellSetFingerprint(count, xor, sum);
            }

            private static ulong CellHash(Vector3i cell)
            {
                unchecked
                {
                    ulong hash = 1469598103934665603UL;
                    hash = (hash ^ (uint)cell.x) * 1099511628211UL;
                    hash = (hash ^ (uint)cell.y) * 1099511628211UL;
                    hash = (hash ^ (uint)cell.z) * 1099511628211UL;
                    hash ^= hash >> 32;
                    hash *= 0xD6E8FEB86659FD93UL;
                    return hash ^ (hash >> 32);
                }
            }

            public bool Equals(SmartBuildCellSetFingerprint other) =>
                Count == other.Count && Xor == other.Xor && Sum == other.Sum;

            public override bool Equals(object obj) =>
                obj is SmartBuildCellSetFingerprint other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Count;
                    hash = (hash * 397) ^ Xor.GetHashCode();
                    hash = (hash * 397) ^ Sum.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
