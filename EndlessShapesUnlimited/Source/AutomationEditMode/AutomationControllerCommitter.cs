using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Modding.Types;
using DecoLimitLifter.DecorationEditMode;
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

                Quaternion[] candidateRotations = CandidatePlacementRotations(rotation);
                using (EsuHudNotifications.BeginSilentInfoStoreCapture())
                {
                    for (int index = 0; index < candidateRotations.Length; index++)
                    {
                        var command = new PlaceBlockCommand(
                            construct,
                            position,
                            candidateRotations[index],
                            item,
                            0,
                            MirrorInfo.none);
                        command.Apply();
                        if (!command.Success)
                            continue;

                        RegisterUndo(build, command);
                        message = index == 0
                            ? "Placed " + descriptor.Label + "."
                            : "Placed " + descriptor.Label + " after rotating to a valid attach face.";
                        return true;
                    }
                }

                message =
                    "FtD rejected the automation controller placement after ESU tried valid attach-face rotations.";
                return false;
            }
            catch (Exception exception)
            {
                message = "Automation controller placement failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryRemoveController(
            cBuild build,
            AutomationTarget target,
            out string message)
        {
            message = null;
            if (build == null || target?.Construct == null)
            {
                message = "No valid construct is available.";
                return false;
            }

            if (!target.IsController)
            {
                message = "Select an Automation controller before deleting.";
                return false;
            }

            try
            {
                if (target.Construct.AllBasics.GetBlockViaLocalPosition(target.LocalPosition) == null)
                {
                    message = "Automation controller block is already gone.";
                    return false;
                }

                var command = new RemoveBlockCommand(
                    target.Construct,
                    target.LocalPosition,
                    MirrorInfo.none);
                command.Apply();
                if (!command.Success)
                {
                    message = "FtD rejected the Automation controller delete.";
                    return false;
                }

                RegisterUndo(build, command);
                message = "Deleted " + target.Label + ".";
                return true;
            }
            catch (Exception exception)
            {
                message = "Automation controller delete failed: " + exception.Message;
                return false;
            }
        }

        private static Quaternion[] CandidatePlacementRotations(Quaternion preferred)
        {
            var rotations = new List<Quaternion>();
            AddUniqueRotation(rotations, preferred);
            foreach (Quaternion rotation in CubeRotations())
                AddUniqueRotation(rotations, rotation);
            return rotations.ToArray();
        }

        private static IEnumerable<Quaternion> CubeRotations()
        {
            Vector3[] directions =
            {
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left,
                Vector3.up,
                Vector3.down
            };

            foreach (Vector3 forward in directions)
            {
                foreach (Vector3 up in directions)
                {
                    if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.001f)
                        continue;

                    yield return Quaternion.LookRotation(forward, up);
                }
            }
        }

        private static void AddUniqueRotation(ICollection<Quaternion> rotations, Quaternion candidate)
        {
            if (rotations.Any(existing => Mathf.Abs(Quaternion.Dot(existing, candidate)) > 0.9999f))
                return;

            rotations.Add(candidate);
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

        private static void RegisterUndo(cBuild build, RemoveBlockCommand command)
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
                    new object[] { new AutomationControllerRemoveUndoCommand(command) });
            }
            catch
            {
                // The delete succeeded; missing undo registration should not put the block back.
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

        private sealed class AutomationControllerRemoveUndoCommand : ICommand
        {
            private readonly RemoveBlockCommand _command;

            internal AutomationControllerRemoveUndoCommand(RemoveBlockCommand command)
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
                "Automation Editor controller delete";

            public ICommand GetLast() => Next == null ? this : Next.GetLast();
        }
    }
}
