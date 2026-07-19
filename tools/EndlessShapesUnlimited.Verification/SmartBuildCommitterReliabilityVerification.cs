using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using DecoLimitLifter.SmartBuildMode;
using NetInfrastructure;

internal static partial class Program
{
    private static void VerifySmartBuildCommitterReliability()
    {
        VerifySmartBuildCommitContextPreflight();
        VerifySmartBuildPlacementOrderingIsDeterministic();
        VerifySmartBuildPlacementOrderingScalesLinearly();
        VerifySmartBuildInitialApplyCompensation();
        VerifySmartBuildEveryFaultPositionCompensates();
        VerifySmartBuildUndoRegistrationFailureCompensates();
        VerifySmartBuildCompositeUndoRedoCompensation();
        VerifySmartBuildJournalAggregatesCompensationFailures();
        VerifySmartBuildPlanCoordinator();
    }

    private static void VerifySmartBuildCommitContextPreflight()
    {
        var planned = new object();
        var other = new object();
        Action<ICommand> registrar = _ => { };

        bool accepted = SmartBuildCommitter.TryValidateCommitContextSnapshot(
            buildAvailable: true,
            activeBuildMode: true,
            focusedConstruct: planned,
            plannedConstruct: planned,
            undoEnabled: true,
            undoRegistrar: registrar,
            out Action<ICommand> resolvedRegistrar,
            out string acceptedReason);
        bool inactiveRejected = !SmartBuildCommitter.TryValidateCommitContextSnapshot(
            buildAvailable: true,
            activeBuildMode: false,
            focusedConstruct: planned,
            plannedConstruct: planned,
            undoEnabled: true,
            undoRegistrar: registrar,
            out _,
            out string inactiveReason);
        bool focusRejected = !SmartBuildCommitter.TryValidateCommitContextSnapshot(
            buildAvailable: true,
            activeBuildMode: true,
            focusedConstruct: other,
            plannedConstruct: planned,
            undoEnabled: true,
            undoRegistrar: registrar,
            out _,
            out string focusReason);
        bool undoRejected = !SmartBuildCommitter.TryValidateCommitContextSnapshot(
            buildAvailable: true,
            activeBuildMode: true,
            focusedConstruct: planned,
            plannedConstruct: planned,
            undoEnabled: false,
            undoRegistrar: registrar,
            out _,
            out string undoReason);
        bool missingContainerRejected = !SmartBuildCommitter.TryValidateCommitContextSnapshot(
            buildAvailable: true,
            activeBuildMode: true,
            focusedConstruct: planned,
            plannedConstruct: planned,
            undoEnabled: true,
            undoRegistrar: null,
            out _,
            out string containerReason);

        Assert(
            accepted && ReferenceEquals(resolvedRegistrar, registrar) && acceptedReason == null &&
            inactiveRejected && inactiveReason.Contains("build mode") &&
            focusRejected && focusReason.Contains("focused construct") &&
            undoRejected && undoReason.Contains("undo/redo") &&
            missingContainerRejected && containerReason.Contains("undo container"),
            "Smart Builder commit preflight requires active build mode, the exact focused construct, enabled native undo, and a live native undo registrar before mutation.");
    }

    private static void VerifySmartBuildPlacementOrderingIsDeterministic()
    {
        SmartBuildPlacement chainEnd = ReliabilityPlacement(2);
        SmartBuildPlacement separateSeed = ReliabilityPlacement(10);
        SmartBuildPlacement chainMiddle = ReliabilityPlacement(1);
        SmartBuildPlacement chainSeed = ReliabilityPlacement(0);
        SmartBuildPlacement[] placements =
        {
            chainEnd,
            separateSeed,
            chainMiddle,
            chainSeed
        };

        bool orderedSuccessfully = SmartBuildCommitter.TryOrderPlacementsForCommit(
            placements,
            cell => SameReliabilityCell(cell, -1) || SameReliabilityCell(cell, 9),
            out List<SmartBuildPlacement> ordered,
            out string orderingReason);
        bool disconnectedRejected = !SmartBuildCommitter.TryOrderPlacementsForCommit(
            new[] { ReliabilityPlacement(0), ReliabilityPlacement(20) },
            cell => SameReliabilityCell(cell, -1),
            out List<SmartBuildPlacement> disconnectedOrder,
            out string disconnectedReason);

        Assert(
            orderedSuccessfully && orderingReason == null && ordered.Count == 4 &&
            ReferenceEquals(ordered[0], separateSeed) &&
            ReferenceEquals(ordered[1], chainSeed) &&
            ReferenceEquals(ordered[2], chainMiddle) &&
            ReferenceEquals(ordered[3], chainEnd) &&
            disconnectedRejected && disconnectedOrder.Count == 0 &&
            disconnectedReason.Contains("disconnected"),
            "Smart Builder placement ordering uses stable input-order seeds followed by deterministic cell-indexed breadth-first connectivity and rejects disconnected components atomically.");
    }

    private static void VerifySmartBuildPlacementOrderingScalesLinearly()
    {
        const int placementCount = 10_000;
        SmartBuildPlacement[] reversedChain = Enumerable
            .Range(0, placementCount)
            .Reverse()
            .Select(ReliabilityPlacement)
            .ToArray();
        int occupancyProbes = 0;
        bool orderedSuccessfully = SmartBuildCommitter.TryOrderPlacementsForCommit(
            reversedChain,
            cell =>
            {
                occupancyProbes++;
                return SameReliabilityCell(cell, -1);
            },
            out List<SmartBuildPlacement> ordered,
            out string reason);

        bool completeAscendingChain =
            orderedSuccessfully &&
            ordered.Count == placementCount &&
            ordered.Select((placement, index) => placement.Position.x == index).All(value => value);
        Assert(
            completeAscendingChain && reason == null && occupancyProbes <= placementCount * 6,
            "Smart Builder orders the 10,000-placement hard-cap chain with at most six cached construct probes per placement instead of repeatedly scanning the remaining plan.");
    }

    private static void VerifySmartBuildInitialApplyCompensation()
    {
        var applied = new HashSet<int>();
        var trace = new List<string>();
        ICommand[] commands =
        {
            new ReliabilityJournalCommand(1, applied, trace),
            new ReliabilityJournalCommand(2, applied, trace, throwOnApply: true),
            new ReliabilityJournalCommand(3, applied, trace)
        };

        bool completed = SmartBuildCommitter.TryExecuteCommandJournal(
            commands,
            SmartBuildCommitter.SmartBuildJournalDirection.Apply,
            out string failureReason);
        Assert(
            !completed && applied.Count == 0 &&
            trace.SequenceEqual(new[] { "A1", "A2", "U2", "U1" }) &&
            failureReason.Contains("Apply failed at command 2") &&
            failureReason.Contains("fully compensated by Undo"),
            "Smart Builder initial command application compensates both the faulting command and every earlier command in reverse order.");
    }

    private static void VerifySmartBuildEveryFaultPositionCompensates()
    {
        for (int failureIndex = 0; failureIndex < 3; failureIndex++)
        {
            var applyState = new HashSet<int>();
            var applyTrace = new List<string>();
            ICommand[] applyCommands = Enumerable
                .Range(0, 3)
                .Select(index => (ICommand)new ReliabilityJournalCommand(
                    index + 1,
                    applyState,
                    applyTrace,
                    throwOnApply: index == failureIndex))
                .ToArray();
            bool applyCompleted = SmartBuildCommitter.TryExecuteCommandJournal(
                applyCommands,
                SmartBuildCommitter.SmartBuildJournalDirection.Apply,
                out string applyFailure);
            Assert(
                !applyCompleted && applyState.Count == 0 &&
                applyFailure.Contains("command " + (failureIndex + 1).ToString()) &&
                applyFailure.Contains("fully compensated by Undo"),
                "Smart Builder compensates first, middle, and last Apply faults without leaving craft state behind (fault position " +
                (failureIndex + 1).ToString() + ").");

            var undoState = new HashSet<int> { 1, 2, 3 };
            var undoTrace = new List<string>();
            ICommand[] undoCommands = Enumerable
                .Range(0, 3)
                .Select(index => (ICommand)new ReliabilityJournalCommand(
                    index + 1,
                    undoState,
                    undoTrace,
                    throwOnUndo: index == failureIndex))
                .ToArray();
            bool undoCompleted = SmartBuildCommitter.TryExecuteCommandJournal(
                undoCommands,
                SmartBuildCommitter.SmartBuildJournalDirection.Undo,
                out string undoFailure);
            Assert(
                !undoCompleted && undoState.SetEquals(new[] { 1, 2, 3 }) &&
                undoFailure.Contains("command " + (failureIndex + 1).ToString()) &&
                undoFailure.Contains("fully compensated by Apply"),
                "Smart Builder compensates first, middle, and last Undo faults without losing craft state (fault position " +
                (failureIndex + 1).ToString() + ").");

            var redoState = new HashSet<int>();
            var redoTrace = new List<string>();
            ICommand redo = SmartBuildCommitter.CreateUndoCommandForVerification(
                Enumerable.Range(0, 3)
                    .Select(index => (ICommand)new ReliabilityJournalCommand(
                        index + 1,
                        redoState,
                        redoTrace,
                        throwOnApply: index == failureIndex))
                    .ToArray());
            string redoFailure = CaptureInvalidOperation(redo.Apply);
            Assert(
                redoState.Count == 0 &&
                redoFailure.Contains("redo failed transactionally") &&
                redoFailure.Contains("command " + (failureIndex + 1).ToString()),
                "Smart Builder composite Redo compensates first, middle, and last faults (fault position " +
                (failureIndex + 1).ToString() + ").");

            var compositeUndoState = new HashSet<int> { 1, 2, 3 };
            var compositeUndoTrace = new List<string>();
            ICommand compositeUndo = SmartBuildCommitter.CreateUndoCommandForVerification(
                Enumerable.Range(0, 3)
                    .Select(index => (ICommand)new ReliabilityJournalCommand(
                        index + 1,
                        compositeUndoState,
                        compositeUndoTrace,
                        throwOnUndo: index == failureIndex))
                    .ToArray());
            string compositeUndoFailure = CaptureInvalidOperation(compositeUndo.Undo);
            Assert(
                compositeUndoState.SetEquals(new[] { 1, 2, 3 }) &&
                compositeUndoFailure.Contains("undo failed transactionally") &&
                compositeUndoFailure.Contains("command " + (failureIndex + 1).ToString()),
                "Smart Builder composite Undo compensates first, middle, and last faults (fault position " +
                (failureIndex + 1).ToString() + ").");
        }
    }

    private static void VerifySmartBuildUndoRegistrationFailureCompensates()
    {
        var applied = new HashSet<int>();
        var trace = new List<string>();
        ICommand[] commands = Enumerable
            .Range(1, 3)
            .Select(id => (ICommand)new ReliabilityJournalCommand(id, applied, trace))
            .ToArray();
        bool appliedSuccessfully = SmartBuildCommitter.TryExecuteCommandJournal(
            commands,
            SmartBuildCommitter.SmartBuildJournalDirection.Apply,
            out string applyFailure);
        bool finalized = SmartBuildCommitter.TryFinalizeCommandJournal(
            commands,
            undoRequired: true,
            _ => throw new InvalidOperationException("injected undo registration fault"),
            out string registrationFailure);

        Assert(
            appliedSuccessfully && applyFailure == null && !finalized &&
            applied.Count == 0 &&
            trace.SequenceEqual(new[] { "A1", "A2", "A3", "U3", "U2", "U1" }) &&
            registrationFailure.Contains("undo registration failed") &&
            registrationFailure.Contains("complete command journal was reversed") &&
            registrationFailure.Contains("injected undo registration fault"),
            "Smart Builder reverses an ordinary placement journal when native undo registration fails, so no successful Apply can become untracked craft state.");
    }

    private static void VerifySmartBuildCompositeUndoRedoCompensation()
    {
        var undoApplied = new HashSet<int> { 1, 2, 3 };
        var undoTrace = new List<string>();
        ICommand undoComposite = SmartBuildCommitter.CreateUndoCommandForVerification(
            new ICommand[]
            {
                new ReliabilityJournalCommand(1, undoApplied, undoTrace),
                new ReliabilityJournalCommand(2, undoApplied, undoTrace, throwOnUndo: true),
                new ReliabilityJournalCommand(3, undoApplied, undoTrace)
            });
        string undoFailure = CaptureInvalidOperation(undoComposite.Undo);

        var redoApplied = new HashSet<int>();
        var redoTrace = new List<string>();
        ICommand redoComposite = SmartBuildCommitter.CreateUndoCommandForVerification(
            new ICommand[]
            {
                new ReliabilityJournalCommand(1, redoApplied, redoTrace),
                new ReliabilityJournalCommand(2, redoApplied, redoTrace, throwOnApply: true),
                new ReliabilityJournalCommand(3, redoApplied, redoTrace)
            });
        string redoFailure = CaptureInvalidOperation(redoComposite.Apply);

        Assert(
            undoApplied.SetEquals(new[] { 1, 2, 3 }) &&
            undoTrace.SequenceEqual(new[] { "U3", "U2", "A2", "A3" }) &&
            undoFailure.Contains("undo failed transactionally") &&
            redoApplied.Count == 0 &&
            redoTrace.SequenceEqual(new[] { "A1", "A2", "U2", "U1" }) &&
            redoFailure.Contains("redo failed transactionally"),
            "Smart Builder's registered composite command restores the prior craft state when either native Undo or Redo faults midway.");
    }

    private static void VerifySmartBuildJournalAggregatesCompensationFailures()
    {
        var applied = new HashSet<int>();
        var trace = new List<string>();
        ICommand[] commands =
        {
            new ReliabilityJournalCommand(1, applied, trace, throwOnUndo: true),
            new ReliabilityJournalCommand(2, applied, trace, throwOnUndo: true),
            new ReliabilityJournalCommand(3, applied, trace, throwOnApply: true)
        };

        bool completed = SmartBuildCommitter.TryExecuteCommandJournal(
            commands,
            SmartBuildCommitter.SmartBuildJournalDirection.Apply,
            out string failureReason);
        Assert(
            !completed && applied.Count == 0 &&
            trace.SequenceEqual(new[] { "A1", "A2", "A3", "U3", "U2", "U1" }) &&
            failureReason.Contains("Apply failed at command 3") &&
            failureReason.Contains("ROLLBACK INCOMPLETE: 2 of 3") &&
            failureReason.Contains("command 2: reliability undo fault 2") &&
            failureReason.Contains("command 1: reliability undo fault 1"),
            "Smart Builder continues every compensation attempt and aggregates all rollback failures with their exact command positions.");
    }

    private static SmartBuildPlacement ReliabilityPlacement(int x) =>
        new SmartBuildPlacement(
            new Vector3i(x, 0, 0),
            SmartBlockCandidate.ForTests(1),
            SmartBuildAxis.X);

    private static bool SameReliabilityCell(Vector3i cell, int x) =>
        cell.x == x && cell.y == 0 && cell.z == 0;

    private static string CaptureInvalidOperation(Action action)
    {
        try
        {
            action();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private sealed class ReliabilityJournalCommand : ICommand
    {
        private readonly int _id;
        private readonly HashSet<int> _applied;
        private readonly List<string> _trace;
        private readonly bool _throwOnApply;
        private readonly bool _throwOnUndo;

        internal ReliabilityJournalCommand(
            int id,
            HashSet<int> applied,
            List<string> trace,
            bool throwOnApply = false,
            bool throwOnUndo = false)
        {
            _id = id;
            _applied = applied;
            _trace = trace;
            _throwOnApply = throwOnApply;
            _throwOnUndo = throwOnUndo;
        }

        public string Name => "Smart Builder reliability command " + _id.ToString();

        public IConnectionData Owner { get; set; }

        public GameTime StartTime { get; set; }

        public bool IsFirstExecute { get; set; }

        public ICommand Next { get; set; }

        public void Execute() => Apply();

        public void Apply()
        {
            _trace.Add("A" + _id.ToString());
            _applied.Add(_id);
            if (_throwOnApply)
                throw new InvalidOperationException("reliability apply fault " + _id.ToString());
        }

        public void Undo()
        {
            _trace.Add("U" + _id.ToString());
            _applied.Remove(_id);
            if (_throwOnUndo)
                throw new InvalidOperationException("reliability undo fault " + _id.ToString());
        }

        public string GetDescription() => Name;

        public ICommand GetLast() => Next == null ? this : Next.GetLast();
    }
}
