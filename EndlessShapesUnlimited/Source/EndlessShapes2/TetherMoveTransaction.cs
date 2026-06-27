using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessShapes2
{
    internal static class TetherMoveRules
    {
        internal static readonly Guid TetherBlockGuid =
            new Guid("8bd20877-417f-4094-ab24-1ebae4d73f85");

        internal static bool IsExpectedSource(Guid componentGuid) =>
            componentGuid == TetherBlockGuid;

        internal static bool TryMovePosition(Vector3 current, Vector3 shift, out Vector3 moved)
        {
            moved = current - shift;
            return DecorationTetherMove.IsOffsetWithinBounds(moved);
        }
    }

    internal sealed class TetherMoveTransaction<TEntry>
    {
        private readonly IReadOnlyList<TEntry> _entries;

        internal TetherMoveTransaction(IReadOnlyList<TEntry> entries)
        {
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        internal TetherMoveResult Execute(
            Func<bool> placeDestination,
            Action undoDestination,
            Func<bool> removeSource,
            Action undoSource,
            Action<TEntry> applyEntry,
            Action<TEntry> restoreEntry)
        {
            if (placeDestination == null || undoDestination == null ||
                removeSource == null || undoSource == null ||
                applyEntry == null || restoreEntry == null)
            {
                throw new ArgumentNullException("Tether transaction callbacks cannot be null.");
            }

            bool placementAttempted = false;
            bool removalAttempted = false;
            int lastEntryAttempted = -1;
            try
            {
                placementAttempted = true;
                if (!placeDestination())
                    throw new InvalidOperationException("The tether block cannot be placed in that direction.");

                removalAttempted = true;
                if (!removeSource())
                    throw new InvalidOperationException("The original tether block could not be removed.");

                for (int index = 0; index < _entries.Count; index++)
                {
                    lastEntryAttempted = index;
                    applyEntry(_entries[index]);
                }

                return TetherMoveResult.Success;
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<Exception>();
                for (int index = lastEntryAttempted; index >= 0; index--)
                {
                    try { restoreEntry(_entries[index]); }
                    catch (Exception exception) { rollbackErrors.Add(exception); }
                }

                if (removalAttempted)
                {
                    try { undoSource(); }
                    catch (Exception exception) { rollbackErrors.Add(exception); }
                }
                if (placementAttempted)
                {
                    try { undoDestination(); }
                    catch (Exception exception) { rollbackErrors.Add(exception); }
                }
                return new TetherMoveResult(failure, rollbackErrors);
            }
        }
    }

    internal sealed class TetherMoveResult
    {
        private TetherMoveResult()
        {
            Succeeded = true;
            RollbackErrors = Array.Empty<Exception>();
        }

        internal TetherMoveResult(Exception failure, IReadOnlyList<Exception> rollbackErrors)
        {
            Failure = failure ?? throw new ArgumentNullException(nameof(failure));
            RollbackErrors = rollbackErrors ?? throw new ArgumentNullException(nameof(rollbackErrors));
        }

        internal static TetherMoveResult Success { get; } = new TetherMoveResult();

        internal bool Succeeded { get; }

        internal Exception Failure { get; }

        internal IReadOnlyList<Exception> RollbackErrors { get; }
    }
}
