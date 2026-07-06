using System;
using System.Collections.Generic;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Modding.Types;
using NetInfrastructure;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal static class AutomationControllerCommitter
    {
        internal static bool TryPlaceController(
            cBuild build,
            AllConstruct construct,
            Vector3i position,
            Quaternion rotation,
            AutomationControllerDescriptor descriptor,
            out string message)
        {
            message = null;
            if (build == null || construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            if (descriptor == null)
            {
                message = "Select an automation controller first.";
                return false;
            }

            ItemDefinition item = descriptor.ResolveItemDefinition();
            if (item == null)
            {
                message = descriptor.Label + " is unavailable in this FtD install.";
                return false;
            }

            try
            {
                if (construct.AllBasics.GetBlockViaLocalPosition(position) != null)
                {
                    message = "Target cell is occupied.";
                    return false;
                }

                var command = new PlaceBlockCommand(
                    construct,
                    position,
                    rotation,
                    item,
                    0,
                    MirrorInfo.none);
                command.Apply();
                if (!command.Success)
                {
                    message = "FtD rejected the automation controller placement.";
                    return false;
                }

                RegisterUndo(build, command);
                message = "Placed " + descriptor.Label + ".";
                return true;
            }
            catch (Exception exception)
            {
                message = "Automation controller placement failed: " + exception.Message;
                return false;
            }
        }

        private static void RegisterUndo(cBuild build, PlaceBlockCommand command)
        {
            try
            {
                if (build == null || command == null)
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
                    new object[] { new AutomationControllerUndoCommand(command) });
            }
            catch
            {
                // The placement succeeded; missing undo registration should not remove it.
            }
        }

        private sealed class AutomationControllerUndoCommand : ICommand
        {
            private readonly PlaceBlockCommand _command;

            internal AutomationControllerUndoCommand(PlaceBlockCommand command)
            {
                _command = command;
            }

            public string Name => "Automation Editor";

            public IConnectionData Owner { get; set; }

            public GameTime StartTime { get; set; }

            public bool IsFirstExecute { get; set; }

            public ICommand Next { get; set; }

            public void Execute() => Apply();

            public void Apply() => _command?.Apply();

            public void Undo() => _command?.Undo();

            public string GetDescription() =>
                "Automation Editor controller placement";

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }
    }
}
