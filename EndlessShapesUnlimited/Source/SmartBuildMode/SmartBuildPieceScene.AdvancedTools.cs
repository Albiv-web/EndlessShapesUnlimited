using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildPieceScene
    {
        /// <summary>
        /// Atomically duplicates the complete selected group once per transform.
        /// Rotations use one shared group pivot, followed by the transform offset.
        /// The successful selection contains the originals and every new copy.
        /// </summary>
        internal bool TryDuplicateSelectionTransforms(
            IEnumerable<SmartBuildGroupTransform> transforms,
            out IReadOnlyList<SmartBuildPiece> copies,
            out string reason)
        {
            copies = Array.Empty<SmartBuildPiece>();
            SmartBuildPiece[] sources = SelectedPieces.ToArray();
            if (sources.Length == 0)
            {
                reason = "Select at least one Smart Builder piece to duplicate.";
                return false;
            }

            SmartBuildGroupTransform[] requested;
            try
            {
                requested = (transforms ?? Array.Empty<SmartBuildGroupTransform>()).ToArray();
            }
            catch (Exception exception)
            {
                reason = "The copy transform list could not be read: " + exception.Message;
                return false;
            }

            if (requested.Length == 0)
            {
                reason = "The copy pattern does not contain any transforms.";
                return false;
            }

            var transformKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < requested.Length; index++)
            {
                if (!SmartBuildAdvancedToolPlanner.IsValidTransform(requested[index], out reason))
                    return false;

                string key = TransformKey(requested[index]);
                if (!transformKeys.Add(key))
                {
                    reason = "The copy pattern contains duplicate transforms.";
                    return false;
                }
            }

            long addedCount = (long)sources.Length * requested.Length;
            if (addedCount > int.MaxValue ||
                _pieces.Count > MaximumScenePieces ||
                (long)_pieces.Count + addedCount > MaximumScenePieces)
            {
                reason =
                    "The pattern would create " + addedCount +
                    " copies and exceed the Smart Builder scene cap of " +
                    MaximumScenePieces + " pieces.";
                return false;
            }

            Vector3i groupPivot;
            try
            {
                groupPivot = GroupPivot(sources);
            }
            catch (Exception exception)
            {
                reason = "The selected group bounds could not be evaluated: " + exception.Message;
                return false;
            }

            int primarySourceId = SelectedPiece?.Id ?? sources[sources.Length - 1].Id;
            var staged = new List<SmartBuildPiece>((int)addedCount);
            SmartBuildPiece primaryCopy = null;
            try
            {
                for (int transformIndex = 0; transformIndex < requested.Length; transformIndex++)
                {
                    SmartBuildGroupTransform transform = requested[transformIndex];
                    for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                    {
                        SmartBuildPiece source = sources[sourceIndex];
                        SmartBuildPiece duplicate = source.Duplicate(new Vector3i(0, 0, 0));
                        if (transform.QuarterTurns != 0)
                        {
                            duplicate.RotateAroundAxis(
                                transform.RotationAxis,
                                transform.QuarterTurns,
                                groupPivot);
                        }

                        duplicate.MoveBy(transform.Offset);
                        if (!HasSafeBounds(duplicate))
                        {
                            reason =
                                "A transformed copy exceeds the safe +/-" +
                                SmartBuildAdvancedToolPlanner.MaximumCoordinateMagnitude +
                                " grid-cell range.";
                            return false;
                        }

                        staged.Add(duplicate);
                        if (source.Id == primarySourceId)
                            primaryCopy = duplicate;
                    }
                }
            }
            catch (Exception exception)
            {
                reason = "The copy pattern could not be transformed: " + exception.Message;
                return false;
            }

            // No scene state is changed until every copy has been validated.
            _pieces.AddRange(staged);
            MarkGeometryChanged();
            SetSelection(
                sources.Select(piece => piece.Id).Concat(staged.Select(piece => piece.Id)),
                primaryCopy?.Id ?? primarySourceId);

            copies = staged;
            reason = null;
            return true;
        }

        private static Vector3i GroupPivot(IReadOnlyList<SmartBuildPiece> pieces)
        {
            SmartBuildBounds first = pieces[0].Bounds;
            int minX = first.Min.x;
            int minY = first.Min.y;
            int minZ = first.Min.z;
            int maxX = first.Max.x;
            int maxY = first.Max.y;
            int maxZ = first.Max.z;
            for (int index = 1; index < pieces.Count; index++)
            {
                SmartBuildBounds bounds = pieces[index].Bounds;
                minX = Math.Min(minX, bounds.Min.x);
                minY = Math.Min(minY, bounds.Min.y);
                minZ = Math.Min(minZ, bounds.Min.z);
                maxX = Math.Max(maxX, bounds.Max.x);
                maxY = Math.Max(maxY, bounds.Max.y);
                maxZ = Math.Max(maxZ, bounds.Max.z);
            }

            return new Vector3i(
                RoundedCellCenter(minX, maxX),
                RoundedCellCenter(minY, maxY),
                RoundedCellCenter(minZ, maxZ));
        }

        private static int RoundedCellCenter(int min, int max) =>
            (int)((long)min + ((long)max - min + 1L) / 2L);

        private static bool HasSafeBounds(SmartBuildPiece piece)
        {
            SmartBuildBounds bounds = piece.Bounds;
            int limit = SmartBuildAdvancedToolPlanner.MaximumCoordinateMagnitude;
            return Math.Abs((long)bounds.Min.x) <= limit &&
                   Math.Abs((long)bounds.Min.y) <= limit &&
                   Math.Abs((long)bounds.Min.z) <= limit &&
                   Math.Abs((long)bounds.Max.x) <= limit &&
                   Math.Abs((long)bounds.Max.y) <= limit &&
                   Math.Abs((long)bounds.Max.z) <= limit;
        }

        private static string TransformKey(SmartBuildGroupTransform transform)
        {
            string axis = transform.QuarterTurns == 0
                ? "none"
                : transform.RotationAxis.ToString();
            return transform.Offset.x + ":" +
                   transform.Offset.y + ":" +
                   transform.Offset.z + ":" +
                   axis + ":" +
                   transform.QuarterTurns;
        }
    }
}
