using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using NetInfrastructure;

namespace DecoLimitLifter.SmartBuildMode
{
    internal static class SmartBuildCommitter
    {
        internal static bool TryCommit(
            cBuild build,
            SmartBuildPlan plan,
            out string message)
        {
            message = null;
            if (build == null || plan?.Volume?.Construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            if (!plan.CanCommit)
            {
                message = plan.FailureReason ?? "The plan cannot be committed.";
                return false;
            }

            if (plan.Placements.Count == 0)
            {
                message = "The plan contains no placements.";
                return false;
            }

            var applied = new List<PlaceBlockCommand>();
            try
            {
                if (!TryOrderPlacementsForCommit(plan, out List<SmartBuildPlacement> ordered, out message))
                    return false;

                var committedCells = new HashSet<Vector3i>();
                foreach (SmartBuildPlacement placement in ordered)
                {
                    if (placement.Candidate.Definition == null)
                        throw new InvalidOperationException("The selected block definition is unavailable.");

                    foreach (Vector3i cell in placement.CoveredCells())
                    {
                        if (plan.Volume.Construct.AllBasics.GetBlockViaLocalPosition(cell) != null)
                            throw new InvalidOperationException("A target cell became occupied before commit.");
                    }

                    if (!PlacementTouchesConstructOrPlacedCells(placement, plan.Volume.Construct, committedCells))
                        throw new InvalidOperationException("A planned block is disconnected from the construct.");

                    var command = new PlaceBlockCommand(
                        plan.Volume.Construct,
                        placement.Position,
                        placement.Rotation,
                        placement.Candidate.Definition,
                        0,
                        MirrorInfo.none);
                    command.Apply();
                    if (!command.Success)
                        throw new InvalidOperationException("The game rejected a planned block placement.");

                    applied.Add(command);
                    foreach (Vector3i cell in placement.CoveredCells())
                        committedCells.Add(cell);
                }

                RegisterUndo(build, applied);
                message = $"Smart Builder placed {applied.Count:N0} block item(s) covering {plan.CoveredCellCount:N0} cell(s).";
                return true;
            }
            catch (Exception exception)
            {
                RollBack(applied);
                message = "Smart Builder commit failed: " + exception.Message;
                return false;
            }
        }

        private static bool TryOrderPlacementsForCommit(
            SmartBuildPlan plan,
            out List<SmartBuildPlacement> ordered,
            out string message)
        {
            ordered = new List<SmartBuildPlacement>();
            message = null;
            var remaining = new List<SmartBuildPlacement>(plan.Placements);
            var placedCells = new HashSet<Vector3i>();
            while (remaining.Count > 0)
            {
                int index = remaining.FindIndex(
                    placement => PlacementTouchesConstructOrPlacedCells(
                        placement,
                        plan.Volume.Construct,
                        placedCells));
                if (index < 0)
                {
                    message = ordered.Count == 0
                        ? "Smart Builder preview must touch an existing block before Apply."
                        : "Smart Builder commit failed: remaining planned blocks are disconnected from placed cells.";
                    return false;
                }

                SmartBuildPlacement next = remaining[index];
                remaining.RemoveAt(index);
                ordered.Add(next);
                foreach (Vector3i cell in next.CoveredCells())
                    placedCells.Add(cell);
            }

            return true;
        }

        private static bool PlacementTouchesConstructOrPlacedCells(
            SmartBuildPlacement placement,
            AllConstruct construct,
            HashSet<Vector3i> placedCells)
        {
            foreach (Vector3i cell in placement.CoveredCells())
            {
                foreach (Vector3i neighbor in NeighborCells(cell))
                {
                    if (placedCells.Contains(neighbor) ||
                        construct.AllBasics.GetBlockViaLocalPosition(neighbor) != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<Vector3i> NeighborCells(Vector3i cell)
        {
            yield return cell + new Vector3i(1, 0, 0);
            yield return cell + new Vector3i(-1, 0, 0);
            yield return cell + new Vector3i(0, 1, 0);
            yield return cell + new Vector3i(0, -1, 0);
            yield return cell + new Vector3i(0, 0, 1);
            yield return cell + new Vector3i(0, 0, -1);
        }

        private static void RollBack(IReadOnlyList<PlaceBlockCommand> commands)
        {
            for (int index = commands.Count - 1; index >= 0; index--)
            {
                try
                {
                    commands[index].Undo();
                }
                catch
                {
                    // Continue best-effort rollback; the caller reports the original failure.
                }
            }
        }

        private static void RegisterUndo(
            cBuild build,
            IReadOnlyList<PlaceBlockCommand> commands)
        {
            try
            {
                if (commands.Count == 0)
                    return;

                object undoRedo = typeof(cBuild)
                    .GetProperty(
                        "UndoRedo",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(build, null);
                object container = undoRedo?.GetType()
                    .GetProperty(
                        "Container",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(undoRedo, null);
                MethodInfo register = container?.GetType()
                    .GetMethod(
                        "Register",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ICommand) },
                        null);
                register?.Invoke(
                    container,
                    new object[] { new SmartBuildUndoCommand(commands.ToArray()) });
            }
            catch
            {
                // The build itself succeeded; missing undo registration should not roll it back.
            }
        }

        private sealed class SmartBuildUndoCommand : ICommand
        {
            private readonly PlaceBlockCommand[] _commands;

            internal SmartBuildUndoCommand(PlaceBlockCommand[] commands)
            {
                _commands = commands ?? Array.Empty<PlaceBlockCommand>();
            }

            public string Name => "Smart Block Builder";

            public IConnectionData Owner { get; set; }

            public GameTime StartTime { get; set; }

            public bool IsFirstExecute { get; set; }

            public ICommand Next { get; set; }

            public void Execute() => Apply();

            public void Apply()
            {
                for (int index = 0; index < _commands.Length; index++)
                    _commands[index].Apply();
            }

            public void Undo()
            {
                for (int index = _commands.Length - 1; index >= 0; index--)
                    _commands[index].Undo();
            }

            public string GetDescription() =>
                $"Smart Block Builder placement: {_commands.Length:N0} block item(s)";

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }
    }
}
