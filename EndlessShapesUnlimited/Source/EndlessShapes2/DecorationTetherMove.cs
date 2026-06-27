using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Ftd.Avatar.Items;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using UnityEngine;

namespace EndlessShapes2
{
    public class DecorationTetherMove : CharacterItem
    {
        private readonly Action<ITimeStep> _updateCallback;
        private Block _pointedBlock;
        private bool _registered;

        public DecorationTetherMove()
        {
            _updateCallback = UpdatePointedBlock;
        }

        public void OnEnable() => RegisterUpdater();

        public void OnDisable()
        {
            TryUnregisterUpdater();
            _pointedBlock = null;
        }

        public override void StartingWithItem()
        {
            base.StartingWithItem();
            RegisterUpdater();
        }

        public override void FinishedWithItem()
        {
            try
            {
                TryUnregisterUpdater();
                _pointedBlock = null;
            }
            finally
            {
                base.FinishedWithItem();
            }
        }

        public void OnGUI()
        {
            if (Get.UserInput.AllGameControlsEnabled &&
                cBuild.GetSingleton().IsInactive() &&
                _pointedBlock != null &&
                !_pointedBlock.IsDeleted)
            {
                DisplayText(_pointedBlock.Name);
            }
        }

        public override bool AreYouTwoHanded() => true;

        public override void LeftClick() => MoveTether(forward: true);

        public override void RightClick() => MoveTether(forward: false);

        private void MoveTether(bool forward)
        {
            Block pointedBlock = _pointedBlock;
            if (pointedBlock == null || pointedBlock.IsDeleted)
                return;
            if (!TetherMoveRules.IsExpectedSource(
                    pointedBlock.item?.ComponentId.Guid ?? Guid.Empty))
            {
                InfoStore.Add("Point at an EndlessShapes tether block before moving it.");
                return;
            }

            ItemDefinition itemDefinition = Configured.i
                .Get<ModificationComponentContainerItem>()
                .Find(TetherMoveRules.TetherBlockGuid, out bool found);
            if (!found || itemDefinition == null)
            {
                InfoStore.Add("The configured tether block is unavailable.");
                return;
            }

            AllConstruct construct = pointedBlock.GetC();
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
            {
                InfoStore.Add("The construct decoration manager is unavailable.");
                return;
            }

            CameraManager camera = CameraManager.GetSingleton();
            if (camera == null)
            {
                InfoStore.Add("The player camera is unavailable.");
                return;
            }

            Vector3 localDirection = Quaternion.Inverse(construct.myTransform.rotation) *
                                     camera.transform.rotation *
                                     (forward ? Vector3.forward : Vector3.back);
            Vector3i shift = DominantAxis(localDirection);
            if (shift == Vector3i.zero)
                return;

            Vector3i oldPosition = pointedBlock.LocalPosition;
            Vector3i newPosition = oldPosition + shift;
            List<TetherMoveEntry> entries;
            try
            {
                entries = BuildMoveEntries(decorations, oldPosition, shift);
                if (entries == null)
                    return;
            }
            catch (Exception exception)
            {
                InfoStore.Add($"Tether preflight failed: {exception.Message}");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Tether preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                return;
            }

            PlaceBlockCommand placeCommand = null;
            RemoveBlockCommand removeCommand = null;
            try
            {
                placeCommand = new PlaceBlockCommand(
                    construct,
                    newPosition,
                    Quaternion.identity,
                    itemDefinition,
                    0,
                    MirrorInfo.none);
                removeCommand = new RemoveBlockCommand(construct, oldPosition, MirrorInfo.none);
                var transaction = new TetherMoveTransaction<TetherMoveEntry>(entries);
                TetherMoveResult result = transaction.Execute(
                    () =>
                    {
                        placeCommand.Apply();
                        return placeCommand.Success;
                    },
                    placeCommand.Undo,
                    () =>
                    {
                        removeCommand.Apply();
                        return removeCommand.Success;
                    },
                    removeCommand.Undo,
                    ApplyEntry,
                    RestoreEntry);

                if (!result.Succeeded)
                {
                    Exception logged = result.RollbackErrors.Count == 0
                        ? result.Failure
                        : new AggregateException(
                            "Tether movement failed and rollback encountered additional errors.",
                            Combine(result.Failure, result.RollbackErrors));
                    InfoStore.Add($"Tether movement failed: {result.Failure.Message}");
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Tether movement failed",
                        logged,
                        LogOptions._AlertDevAndCustomerInGame);
                    return;
                }

                InfoStore.Add(
                    $"Moved tether one block and retethered {entries.Count:N0} decoration(s).");
            }
            catch (Exception exception)
            {
                InfoStore.Add($"Tether movement failed: {exception.Message}");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Tether movement failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }
        }

        private static List<TetherMoveEntry> BuildMoveEntries(
            AllConstructDecorations decorations,
            Vector3i oldPosition,
            Vector3i shift)
        {
            var entries = new List<TetherMoveEntry>();
            if (!decorations.TryGetDecorationsList(oldPosition, out List<Decoration> atPosition) ||
                atPosition == null)
            {
                return entries;
            }

            foreach (Decoration decoration in new List<Decoration>(atPosition))
            {
                if (decoration == null || decoration.IsDeleted)
                    continue;
                if (!TetherMoveRules.TryMovePosition(
                        decoration.Positioning.Us,
                        shift,
                        out Vector3 newPositioning))
                {
                    InfoStore.Add(
                        "Tether move aborted because at least one linked decoration would exceed the +/-10 positioning limit.");
                    return null;
                }

                entries.Add(new TetherMoveEntry(
                    decoration,
                    decoration.TetherPoint.Us,
                    decoration.Positioning.Us,
                    decoration.TetherPoint.Us + shift,
                    newPositioning));
            }
            return entries;
        }

        internal static bool IsOffsetWithinBounds(Vector3 value)
        {
            return FlexibleFloatParser.IsFinite(value.x) &&
                   FlexibleFloatParser.IsFinite(value.y) &&
                   FlexibleFloatParser.IsFinite(value.z) &&
                   Mathf.Abs(value.x) <= 10f &&
                   Mathf.Abs(value.y) <= 10f &&
                   Mathf.Abs(value.z) <= 10f;
        }

        internal static Vector3i DominantAxis(Vector3 direction)
        {
            if (!FlexibleFloatParser.IsFinite(direction.x) ||
                !FlexibleFloatParser.IsFinite(direction.y) ||
                !FlexibleFloatParser.IsFinite(direction.z))
            {
                return Vector3i.zero;
            }

            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            float z = Mathf.Abs(direction.z);
            if (x >= y && x >= z && x > 0f)
                return new Vector3i(direction.x >= 0f ? 1 : -1, 0, 0);
            if (y >= x && y >= z && y > 0f)
                return new Vector3i(0, direction.y >= 0f ? 1 : -1, 0);
            if (z > 0f)
                return new Vector3i(0, 0, direction.z >= 0f ? 1 : -1);
            return Vector3i.zero;
        }

        private static void ApplyEntry(TetherMoveEntry entry)
        {
            if (entry.Decoration == null || entry.Decoration.IsDeleted)
                throw new InvalidOperationException("A linked decoration disappeared during tether movement.");
            entry.Decoration.TetherPoint.Us = entry.NewTether;
            entry.Decoration.Positioning.Us = entry.NewPositioning;
        }

        private static void RestoreEntry(TetherMoveEntry entry)
        {
            if (entry.Decoration == null || entry.Decoration.IsDeleted)
                return;
            entry.Decoration.Positioning.Us = entry.OldPositioning;
            entry.Decoration.TetherPoint.Us = entry.OldTether;
        }

        private static IEnumerable<Exception> Combine(
            Exception original,
            IEnumerable<Exception> rollbackErrors)
        {
            yield return original;
            foreach (Exception error in rollbackErrors)
                yield return error;
        }

        private void RegisterUpdater()
        {
            if (_registered)
                return;
            GameEvents.Four_Second_PlayerTime.RegWithEvent(_updateCallback);
            _registered = true;
        }

        private void UnregisterUpdater()
        {
            if (!_registered)
                return;
            GameEvents.Four_Second_PlayerTime.UnregWithEvent(_updateCallback);
            _registered = false;
        }

        private void TryUnregisterUpdater()
        {
            try
            {
                UnregisterUpdater();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not unregister the tether pointer callback",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private void UpdatePointedBlock(ITimeStep timeStep)
        {
            _pointedBlock = GetPointedBlock();
        }

        private readonly struct TetherMoveEntry
        {
            internal TetherMoveEntry(
                Decoration decoration,
                Vector3i oldTether,
                Vector3 oldPositioning,
                Vector3i newTether,
                Vector3 newPositioning)
            {
                Decoration = decoration;
                OldTether = oldTether;
                OldPositioning = oldPositioning;
                NewTether = newTether;
                NewPositioning = newPositioning;
            }

            internal Decoration Decoration { get; }

            internal Vector3i OldTether { get; }

            internal Vector3 OldPositioning { get; }

            internal Vector3i NewTether { get; }

            internal Vector3 NewPositioning { get; }
        }
    }
}
