using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using NetInfrastructure;

namespace DecoLimitLifter.SmartBuildMode
{
    internal static class SmartBuildCommitter
    {
        private static readonly Vector3i[] NeighborOffsets =
        {
            new Vector3i(1, 0, 0),
            new Vector3i(-1, 0, 0),
            new Vector3i(0, 1, 0),
            new Vector3i(0, -1, 0),
            new Vector3i(0, 0, 1),
            new Vector3i(0, 0, -1)
        };

        internal enum SmartBuildJournalDirection
        {
            Apply,
            Undo
        }

        internal static bool TryCommit(
            cBuild build,
            SmartBuildPlan plan,
            out string message) =>
            TryCommit(
                build,
                plan,
                SmartBuildNativeCommandFactory.Instance,
                out message);

        internal static bool TryCommit(
            cBuild build,
            SmartBuildPlan plan,
            ISmartBuildCommandFactory commandFactory,
            out string message)
        {
            message = null;
            if (commandFactory == null)
            {
                message = "No Smart Builder native command factory is available.";
                return false;
            }
            if (plan?.Construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            if (!plan.CanCommit)
            {
                message = plan.FailureReason ?? "The plan cannot be committed.";
                return false;
            }

            if (plan.Placements.Count == 0 &&
                plan.CommitOperation != SmartBuildCommitOperation.Erase)
            {
                message = "The plan contains no placements.";
                return false;
            }

            if (!TryResolveCommitContext(
                    build,
                    plan.Construct,
                    plan.ConstructToken,
                    out Action<ICommand> registerUndo,
                    out string contextFailure))
            {
                message = contextFailure;
                return false;
            }

            bool destructive =
                plan.CommitOperation == SmartBuildCommitOperation.Replace ||
                plan.CommitOperation == SmartBuildCommitOperation.Erase;
            IReadOnlyList<SmartBuildRemovalItem> removalItems = destructive
                ? plan.RemovalItems
                : Array.Empty<SmartBuildRemovalItem>();
            if (plan.CommitOperation == SmartBuildCommitOperation.Erase &&
                removalItems.Count == 0)
            {
                message = "The destructive plan contains no complete craft items.";
                return false;
            }

            if (destructive)
            {
                foreach (SmartBuildRemovalItem removal in plan.RemovalItems)
                {
                    if (!SmartBuildRemovalPlanner.TryValidateRemovalItem(
                            plan.Construct,
                            removal,
                            out string validationReason))
                    {
                        message = "Smart Builder destructive preflight failed: " + validationReason;
                        return false;
                    }
                }
            }

            var removalFootprint = new HashSet<Vector3i>(
                removalItems.SelectMany(removal => removal.FootprintCells));
            var ordered = new List<SmartBuildPlacement>();
            if (plan.CommitOperation != SmartBuildCommitOperation.Erase)
            {
                if (!TryPreflightPlacements(
                        plan,
                        removalFootprint,
                        out string placementFailure))
                {
                    message = placementFailure;
                    return false;
                }

                if (!TryOrderPlacementsForCommit(
                        plan.Placements,
                        cell => IsConstructCellOccupiedAfterRemovals(
                            plan.Construct,
                            removalFootprint,
                            cell),
                        out ordered,
                        out string orderFailure))
                {
                    message = orderFailure;
                    return false;
                }
            }

            var commands = new List<ICommand>(removalItems.Count + ordered.Count);
            try
            {
                foreach (SmartBuildRemovalItem removal in removalItems)
                {
                    commands.Add(new SmartBuildDeferredCommand(
                        "Remove planned block item",
                        () => commandFactory.CreateRemoval(plan.Construct, removal)));
                }

                foreach (SmartBuildPlacement placement in ordered)
                {
                    commands.Add(new SmartBuildDeferredCommand(
                        "Place planned block item",
                        () => commandFactory.CreatePlacement(plan.Construct, placement)));
                }
            }
            catch (Exception exception)
            {
                message = CommitFailurePrefix(plan.CommitOperation) +
                          "command preparation failed before the craft was modified: " +
                          ExceptionMessage(exception);
                return false;
            }

            if (!TryExecuteCommandJournal(
                    commands,
                    SmartBuildJournalDirection.Apply,
                    out string executionFailure))
            {
                message = CommitFailurePrefix(plan.CommitOperation) + executionFailure;
                return false;
            }

            if (!TryFinalizeCommandJournal(
                    commands,
                    undoRequired: true,
                    new SmartBuildDelegateUndoRegistrar(registerUndo),
                    out string undoFailure))
            {
                message = CommitFailurePrefix(plan.CommitOperation) + undoFailure;
                return false;
            }

            int removedItems = removalItems.Count;
            int placedItems = ordered.Count;
            if (plan.CommitOperation == SmartBuildCommitOperation.Erase)
            {
                message = $"Smart Builder erased {removedItems:N0} block item(s) transactionally.";
                return true;
            }

            message = plan.CommitOperation == SmartBuildCommitOperation.Replace && removedItems > 0
                ? $"Smart Builder replaced {removedItems:N0} block item(s) with {placedItems:N0} planned item(s) in one transaction."
                : $"Smart Builder placed {placedItems:N0} block item(s) covering {plan.CoveredCellCount:N0} cell(s).";
            return true;
        }

        private static bool TryResolveCommitContext(
            cBuild build,
            AllConstruct plannedConstruct,
            SmartBuildConstructToken constructToken,
            out Action<ICommand> registerUndo,
            out string failureReason)
        {
            registerUndo = null;
            if (build == null || plannedConstruct == null)
            {
                return TryValidateCommitContextSnapshot(
                    build != null,
                    activeBuildMode: false,
                    focusedConstruct: null,
                    plannedConstruct: plannedConstruct,
                    undoEnabled: false,
                    undoRegistrar: null,
                    out registerUndo,
                    out failureReason);
            }

            try
            {
                bool activeBuildMode =
                    build.buildMode == enumBuildMode.active ||
                    build.buildMode == enumBuildMode.activeInventory;
                AllConstruct focusedConstruct = build.GetC();
                bool undoEnabled = NativeUndoEnabled(build);
                Action<ICommand> undoRegistrar = undoEnabled
                    ? ResolveNativeUndoRegistrar(build)
                    : null;
                bool valid = TryValidateCommitContextSnapshot(
                    buildAvailable: true,
                    activeBuildMode: activeBuildMode,
                    focusedConstruct: focusedConstruct,
                    plannedConstruct: plannedConstruct,
                    undoEnabled: undoEnabled,
                    undoRegistrar: undoRegistrar,
                    out registerUndo,
                    out failureReason);
                if (!valid)
                    return false;
                if (constructToken != null &&
                    !constructToken.Matches(
                        plannedConstruct,
                        focusedConstruct,
                        out failureReason))
                {
                    registerUndo = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                registerUndo = null;
                failureReason =
                    "Smart Builder commit rejected because its native build context could not be verified: " +
                    ExceptionMessage(exception);
                return false;
            }
        }

        private static bool NativeUndoEnabled(cBuild build) =>
            build.IsUndoRedoEnabled();

        private static Action<ICommand> ResolveNativeUndoRegistrar(cBuild build)
        {
            IUndoRedoModule undoRedo = build.GetUndoRedo();
            UndoRedoContainer container = undoRedo?.Container;
            return container == null
                ? null
                : new Action<ICommand>(command =>
                    container.Register(command ?? throw new ArgumentNullException(nameof(command))));
        }

        internal static bool TryValidateCommitContextSnapshot(
            bool buildAvailable,
            bool activeBuildMode,
            object focusedConstruct,
            object plannedConstruct,
            bool undoEnabled,
            Action<ICommand> undoRegistrar,
            out Action<ICommand> registerUndo,
            out string failureReason)
        {
            registerUndo = null;
            failureReason = null;
            if (!buildAvailable || plannedConstruct == null)
            {
                failureReason = "Smart Builder commit rejected because no valid build controller or construct is available.";
                return false;
            }

            if (!activeBuildMode)
            {
                failureReason = "Smart Builder commit rejected because FtD build mode is no longer active.";
                return false;
            }

            if (focusedConstruct == null || !ReferenceEquals(focusedConstruct, plannedConstruct))
            {
                failureReason = "Smart Builder commit rejected because the preview construct is no longer the currently focused construct.";
                return false;
            }

            if (!undoEnabled)
            {
                failureReason = "Smart Builder commit rejected because native FtD undo/redo is disabled.";
                return false;
            }

            registerUndo = undoRegistrar;
            if (registerUndo == null)
            {
                failureReason = "Smart Builder commit rejected because the native FtD undo container is unavailable.";
                return false;
            }

            return true;
        }

        private static bool TryPreflightPlacements(
            SmartBuildPlan plan,
            HashSet<Vector3i> removalFootprint,
            out string failureReason)
        {
            failureReason = null;
            var targetCells = new HashSet<Vector3i>();
            try
            {
                foreach (SmartBuildPlacement placement in plan.Placements)
                {
                    if (placement?.Candidate?.Definition == null)
                    {
                        failureReason = "Smart Builder commit rejected because a selected block definition is unavailable.";
                        return false;
                    }

                    foreach (Vector3i cell in placement.CoveredCells())
                    {
                        if (!targetCells.Add(cell))
                        {
                            failureReason = "Smart Builder commit rejected because planned block footprints overlap.";
                            return false;
                        }

                        if (!removalFootprint.Contains(cell) &&
                            plan.Construct.AllBasics.GetBlockViaLocalPosition(cell) != null)
                        {
                            failureReason = "Smart Builder commit rejected because a target cell became occupied before Apply.";
                            return false;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failureReason =
                    "Smart Builder commit rejected because target occupancy could not be verified: " +
                    ExceptionMessage(exception);
                return false;
            }

            return true;
        }

        private static bool IsConstructCellOccupiedAfterRemovals(
            AllConstruct construct,
            HashSet<Vector3i> removalFootprint,
            Vector3i cell) =>
            !removalFootprint.Contains(cell) &&
            construct.AllBasics.GetBlockViaLocalPosition(cell) != null;

        internal static bool TryOrderPlacementsForCommit(
            IReadOnlyList<SmartBuildPlacement> placements,
            Func<Vector3i, bool> isConstructOccupied,
            out List<SmartBuildPlacement> ordered,
            out string failureReason)
        {
            ordered = new List<SmartBuildPlacement>();
            failureReason = null;
            if (placements == null || placements.Count == 0)
                return true;
            if (isConstructOccupied == null)
            {
                failureReason = "Smart Builder commit failed because construct occupancy is unavailable.";
                return false;
            }

            var cellsByPlacement = new Vector3i[placements.Count][];
            var ownerByCell = new Dictionary<Vector3i, int>();
            for (int index = 0; index < placements.Count; index++)
            {
                SmartBuildPlacement placement = placements[index];
                if (placement == null)
                {
                    failureReason = "Smart Builder commit failed because the placement plan contains a missing entry.";
                    return false;
                }

                Vector3i[] cells = placement.CoveredCells().Distinct().ToArray();
                if (cells.Length == 0)
                {
                    failureReason = "Smart Builder commit failed because a planned block has no footprint cells.";
                    return false;
                }

                cellsByPlacement[index] = cells;
                foreach (Vector3i cell in cells)
                {
                    if (ownerByCell.TryGetValue(cell, out int existingOwner))
                    {
                        failureReason =
                            "Smart Builder commit failed because placement " +
                            (index + 1).ToString() + " overlaps placement " +
                            (existingOwner + 1).ToString() + ".";
                        return false;
                    }

                    ownerByCell.Add(cell, index);
                }
            }

            var occupancyCache = new Dictionary<Vector3i, bool>();
            var queued = new bool[placements.Count];
            var queue = new List<int>();
            int queueReadIndex = 0;
            try
            {
                for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                {
                    if (!PlacementTouchesConstructOrPlacedCells(
                            cellsByPlacement[placementIndex],
                            isConstructOccupied,
                            occupancyCache))
                    {
                        continue;
                    }

                    queued[placementIndex] = true;
                    queue.Add(placementIndex);
                }
            }
            catch (Exception exception)
            {
                failureReason =
                    "Smart Builder commit failed because construct connectivity could not be verified: " +
                    ExceptionMessage(exception);
                return false;
            }

            if (queue.Count == 0)
            {
                failureReason = "Smart Builder preview must touch an existing block before Apply.";
                return false;
            }

            while (queueReadIndex < queue.Count)
            {
                int placementIndex = queue[queueReadIndex++];
                ordered.Add(placements[placementIndex]);
                foreach (Vector3i cell in cellsByPlacement[placementIndex])
                {
                    for (int offsetIndex = 0; offsetIndex < NeighborOffsets.Length; offsetIndex++)
                    {
                        Vector3i neighbor = cell + NeighborOffsets[offsetIndex];
                        if (!ownerByCell.TryGetValue(neighbor, out int neighborOwner) ||
                            queued[neighborOwner])
                        {
                            continue;
                        }

                        queued[neighborOwner] = true;
                        queue.Add(neighborOwner);
                    }
                }
            }

            if (ordered.Count != placements.Count)
            {
                failureReason =
                    "Smart Builder commit failed: remaining planned blocks are disconnected; " +
                    (placements.Count - ordered.Count).ToString() +
                    " block(s) are outside the construct-connected placement graph.";
                ordered.Clear();
                return false;
            }

            return true;
        }

        private static bool PlacementTouchesConstructOrPlacedCells(
            IReadOnlyList<Vector3i> placementCells,
            Func<Vector3i, bool> isOccupied,
            IDictionary<Vector3i, bool> occupancyCache)
        {
            foreach (Vector3i cell in placementCells)
            {
                for (int offsetIndex = 0; offsetIndex < NeighborOffsets.Length; offsetIndex++)
                {
                    Vector3i neighbor = cell + NeighborOffsets[offsetIndex];
                    if (!occupancyCache.TryGetValue(neighbor, out bool occupied))
                    {
                        occupied = isOccupied(neighbor);
                        occupancyCache.Add(neighbor, occupied);
                    }

                    if (occupied)
                        return true;
                }
            }

            return false;
        }

        internal static bool TryExecuteCommandJournal(
            IReadOnlyList<ICommand> commands,
            SmartBuildJournalDirection direction,
            out string failureReason)
        {
            failureReason = null;
            if (commands == null || commands.Count == 0)
                return true;

            var attempted = new List<int>();
            int index = direction == SmartBuildJournalDirection.Apply
                ? 0
                : commands.Count - 1;
            int end = direction == SmartBuildJournalDirection.Apply
                ? commands.Count
                : -1;
            int increment = direction == SmartBuildJournalDirection.Apply ? 1 : -1;
            for (; index != end; index += increment)
            {
                ICommand command = commands[index];
                if (command == null)
                {
                    return FailJournalAndCompensate(
                        commands,
                        attempted,
                        direction,
                        index,
                        new InvalidOperationException("The journal contains a missing command."),
                        out failureReason);
                }

                attempted.Add(index);
                try
                {
                    InvokeJournalCommand(command, direction);
                }
                catch (Exception exception)
                {
                    return FailJournalAndCompensate(
                        commands,
                        attempted,
                        direction,
                        index,
                        exception,
                        out failureReason);
                }
            }

            return true;
        }

        private static bool FailJournalAndCompensate(
            IReadOnlyList<ICommand> commands,
            IReadOnlyList<int> attempted,
            SmartBuildJournalDirection failedDirection,
            int failedIndex,
            Exception primaryFailure,
            out string failureReason)
        {
            SmartBuildJournalDirection compensationDirection =
                failedDirection == SmartBuildJournalDirection.Apply
                    ? SmartBuildJournalDirection.Undo
                    : SmartBuildJournalDirection.Apply;
            var compensationFailures = new List<string>();
            for (int attemptedIndex = attempted.Count - 1; attemptedIndex >= 0; attemptedIndex--)
            {
                int commandIndex = attempted[attemptedIndex];
                try
                {
                    InvokeJournalCommand(commands[commandIndex], compensationDirection);
                }
                catch (Exception exception)
                {
                    compensationFailures.Add(
                        "command " + (commandIndex + 1).ToString() + ": " +
                        ExceptionMessage(exception));
                }
            }

            string operation = failedDirection == SmartBuildJournalDirection.Apply
                ? "Apply"
                : "Undo";
            string compensation = compensationDirection == SmartBuildJournalDirection.Apply
                ? "Apply"
                : "Undo";
            failureReason =
                operation + " failed at command " + (failedIndex + 1).ToString() +
                " of " + commands.Count.ToString() + ": " +
                ExceptionMessage(primaryFailure) + ".";
            if (attempted.Count == 0)
                return false;

            if (compensationFailures.Count == 0)
            {
                failureReason +=
                    " All " + attempted.Count.ToString() +
                    " attempted command(s) were fully compensated by " + compensation + ".";
                return false;
            }

            failureReason +=
                " ROLLBACK INCOMPLETE: " + compensationFailures.Count.ToString() +
                " of " + attempted.Count.ToString() +
                " compensation command(s) failed (" +
                string.Join("; ", compensationFailures.ToArray()) +
                "). The craft may be partially modified; inspect it before continuing.";
            return false;
        }

        private static void InvokeJournalCommand(
            ICommand command,
            SmartBuildJournalDirection direction)
        {
            if (command == null)
                throw new InvalidOperationException("The journal contains a missing command.");

            if (direction == SmartBuildJournalDirection.Undo)
            {
                command.Undo();
                return;
            }

            command.Apply();
            EnsureCommandApplySucceeded(command);
        }

        private static void EnsureCommandApplySucceeded(ICommand command)
        {
            if (command is RemoveBlockCommand removal && !removal.Success)
                throw new InvalidOperationException("The game rejected a planned block removal.");
            if (command is PlaceBlockCommand placement && !placement.Success)
                throw new InvalidOperationException("The game rejected a planned block placement.");
        }

        internal static bool TryRollBackCommandJournal(
            IReadOnlyList<ICommand> commands,
            out string failureReason)
        {
            failureReason = null;
            if (commands == null || commands.Count == 0)
                return true;

            var failures = new List<string>();
            for (int index = commands.Count - 1; index >= 0; index--)
            {
                try
                {
                    ICommand command = commands[index];
                    if (command == null)
                        throw new InvalidOperationException("The journal contains a missing command.");
                    command.Undo();
                }
                catch (Exception exception)
                {
                    failures.Add(
                        "command " + (index + 1).ToString() + ": " +
                        ExceptionMessage(exception));
                }
            }

            if (failures.Count == 0)
                return true;

            failureReason =
                failures.Count.ToString() + " of " + commands.Count.ToString() +
                " applied command(s) could not be reversed (" +
                string.Join("; ", failures.ToArray()) +
                "). The craft may be partially modified; inspect it before continuing.";
            return false;
        }

        internal static bool TryFinalizeCommandJournal(
            IReadOnlyList<ICommand> commands,
            bool undoRequired,
            Action<ICommand> registerUndo,
            out string failureReason) =>
            TryFinalizeCommandJournal(
                commands,
                undoRequired,
                registerUndo == null
                    ? null
                    : new SmartBuildDelegateUndoRegistrar(registerUndo),
                out failureReason);

        internal static bool TryFinalizeCommandJournal(
            IReadOnlyList<ICommand> commands,
            bool undoRequired,
            ISmartBuildUndoRegistrar registerUndo,
            out string failureReason)
        {
            failureReason = null;
            if (commands == null || commands.Count == 0)
            {
                if (!undoRequired)
                    return true;

                failureReason = "The Smart Builder command journal is empty.";
                return false;
            }

            try
            {
                if (registerUndo == null)
                    throw new InvalidOperationException("The native FtD undo registrar is unavailable.");

                registerUndo.Register(new SmartBuildUndoCommand(commands.ToArray()));
                return true;
            }
            catch (Exception exception)
            {
                if (!undoRequired)
                {
                    // Preserve the historical placement behavior if FtD disables
                    // undo in a context where ordinary block placement is allowed.
                    failureReason = "Native undo registration was unavailable: " +
                                    ExceptionMessage(exception);
                    return true;
                }

                bool rollbackComplete = TryRollBackCommandJournal(
                    commands,
                    out string rollbackFailure);
                failureReason = rollbackComplete
                    ? "native FtD undo registration failed, so the complete command journal was reversed (" +
                      ExceptionMessage(exception) + ")."
                    : "native FtD undo registration failed and rollback was incomplete (" +
                      ExceptionMessage(exception) + "). " + rollbackFailure;
                return false;
            }
        }

        internal static ICommand CreateUndoCommandForVerification(
            IReadOnlyList<ICommand> commands) =>
            new SmartBuildUndoCommand((commands ?? Array.Empty<ICommand>()).ToArray());

        private static string CommitFailurePrefix(SmartBuildCommitOperation operation)
        {
            switch (operation)
            {
                case SmartBuildCommitOperation.Replace:
                    return "Smart Builder replacement failed: ";
                case SmartBuildCommitOperation.Erase:
                    return "Smart Builder erase failed: ";
                default:
                    return "Smart Builder commit failed: ";
            }
        }

        private static string ExceptionMessage(Exception exception)
        {
            if (exception == null)
                return "unknown failure";
            if (!(exception is AggregateException aggregate))
                return string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.GetType().Name
                    : exception.Message;

            string[] messages = aggregate
                .Flatten()
                .InnerExceptions
                .Select(ExceptionMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            return messages.Length == 0
                ? aggregate.GetType().Name
                : string.Join("; ", messages);
        }

        private sealed class SmartBuildDeferredCommand : ICommand
        {
            private readonly string _name;
            private readonly Func<ICommand> _factory;
            private ICommand _command;

            internal SmartBuildDeferredCommand(string name, Func<ICommand> factory)
            {
                _name = string.IsNullOrWhiteSpace(name) ? "Smart Builder command" : name;
                _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            }

            public string Name => _name;

            public IConnectionData Owner { get; set; }

            public GameTime StartTime { get; set; }

            public bool IsFirstExecute { get; set; }

            public ICommand Next { get; set; }

            public void Execute() => Apply();

            public void Apply()
            {
                if (_command == null)
                {
                    _command = _factory() ??
                               throw new InvalidOperationException(
                                   "The deferred native command factory returned no command.");
                }

                _command.Apply();
                EnsureCommandApplySucceeded(_command);
            }

            public void Undo()
            {
                // A factory can fail before a native command exists. Compensating
                // that unmaterialized step is therefore a safe no-op.
                _command?.Undo();
            }

            public string GetDescription() => _command?.GetDescription() ?? _name;

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }

        private sealed class SmartBuildUndoCommand : ICommand
        {
            private readonly ICommand[] _commands;

            internal SmartBuildUndoCommand(ICommand[] commands)
            {
                _commands = commands ?? Array.Empty<ICommand>();
            }

            public string Name => "Smart Block Builder";

            public IConnectionData Owner { get; set; }

            public GameTime StartTime { get; set; }

            public bool IsFirstExecute { get; set; }

            public ICommand Next { get; set; }

            public void Execute() => Apply();

            public void Apply()
            {
                if (!TryExecuteCommandJournal(
                        _commands,
                        SmartBuildJournalDirection.Apply,
                        out string failureReason))
                {
                    throw new InvalidOperationException(
                        "Smart Builder redo failed transactionally: " + failureReason);
                }
            }

            public void Undo()
            {
                if (!TryExecuteCommandJournal(
                        _commands,
                        SmartBuildJournalDirection.Undo,
                        out string failureReason))
                {
                    throw new InvalidOperationException(
                        "Smart Builder undo failed transactionally: " + failureReason);
                }
            }

            public string GetDescription() =>
                $"Smart Block Builder placement: {_commands.Length:N0} block item(s)";

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }
    }
}
