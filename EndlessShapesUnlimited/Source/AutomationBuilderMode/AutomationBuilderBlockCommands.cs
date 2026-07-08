using System;
using System.Collections.Generic;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Modding.Types;
using NetInfrastructure;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal static class AutomationBuilderBlockCommands
    {
        internal static bool TryPlaceBreadboard(
            cBuild build,
            AllConstruct construct,
            Vector3i position,
            ItemDefinition definition,
            out string message)
        {
            message = null;
            if (build == null || construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            if (definition == null)
            {
                message = "The breadboard item definition is unavailable.";
                return false;
            }

            try
            {
                if (construct.AllBasics.GetBlockViaLocalPosition(position) != null)
                {
                    message = "The target cell is already occupied.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                message = "Breadboard placement preflight failed: " + exception.Message;
                return false;
            }

            PlaceBlockCommand command = null;
            try
            {
                command = new PlaceBlockCommand(
                    construct,
                    position,
                    UnityEngine.Quaternion.identity,
                    definition,
                    0,
                    MirrorInfo.none);
                command.Apply();
                if (!command.Success)
                {
                    message = "The game rejected the breadboard placement.";
                    return false;
                }

                RegisterUndo(build, command);
                message = "Automation Builder placed a breadboard.";
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    command?.Undo();
                }
                catch
                {
                    // Best effort rollback; the caller reports the placement failure.
                }

                message = "Breadboard placement failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryDeleteBlock(
            cBuild build,
            AllConstruct construct,
            Vector3i position,
            out string message)
        {
            message = null;
            if (build == null || construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            try
            {
                if (construct.AllBasics.GetBlockViaLocalPosition(position) == null)
                {
                    message = "The selected block is no longer available.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                message = "Block delete preflight failed: " + exception.Message;
                return false;
            }

            RemoveBlockCommand command = null;
            try
            {
                command = new RemoveBlockCommand(construct, position, MirrorInfo.none);
                command.Apply();
                if (!command.Success)
                {
                    message = "The game rejected the block delete.";
                    return false;
                }

                RegisterUndo(
                    build,
                    new AutomationBuilderUndoCommand(
                        command.Apply,
                        command.Undo,
                        "Automation Builder block delete"));
                message = "Automation Builder deleted the selected block.";
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    command?.Undo();
                }
                catch
                {
                    // Best effort rollback; the caller reports the delete failure.
                }

                message = "Block delete failed: " + exception.Message;
                return false;
            }
        }

        private static void RegisterUndo(cBuild build, PlaceBlockCommand command)
        {
            if (command == null)
                return;

            RegisterUndo(
                build,
                new AutomationBuilderUndoCommand(
                    command.Apply,
                    command.Undo,
                    "Automation Builder breadboard placement"));
        }

        private static void RegisterUndo(cBuild build, AutomationBuilderUndoCommand command)
        {
            try
            {
                if (command == null)
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
                    new object[] { command });
            }
            catch
            {
                // The edit itself succeeded; missing undo registration is non-fatal.
            }
        }

        private sealed class AutomationBuilderUndoCommand : ICommand
        {
            private readonly Action _apply;
            private readonly Action _undo;
            private readonly string _description;

            internal AutomationBuilderUndoCommand(
                Action apply,
                Action undo,
                string description)
            {
                _apply = apply;
                _undo = undo;
                _description = string.IsNullOrWhiteSpace(description)
                    ? "Automation Builder edit"
                    : description;
            }

            public string Name => "Automation Builder";

            public IConnectionData Owner { get; set; }

            public GameTime StartTime { get; set; }

            public bool IsFirstExecute { get; set; }

            public ICommand Next { get; set; }

            public void Execute() => Apply();

            public void Apply() => _apply?.Invoke();

            public void Undo() => _undo?.Invoke();

            public string GetDescription() =>
                _description;

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }
    }
}
