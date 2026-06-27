using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Constants;
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
        private const string TetherBlockGuid = "8bd20877-417f-4094-ab24-1ebae4d73f85";
        private static readonly Action<ITimeStep> UpdateCallback = UpdatePointedBlock;
        private static Block _pointedBlock;
        private bool _registered;

        public void OnEnable()
        {
            if (_registered)
                return;
            GameEvents.Four_Second_PlayerTime.RegWithEvent(UpdateCallback);
            _registered = true;
        }

        public void OnDisable()
        {
            if (!_registered)
                return;
            GameEvents.Four_Second_PlayerTime.UnregWithEvent(UpdateCallback);
            _registered = false;
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

        private static void MoveTether(bool forward)
        {
            Block pointedBlock = _pointedBlock;
            if (pointedBlock == null || pointedBlock.IsDeleted)
                return;

            ItemDefinition itemDefinition = Configured.i
                .Get<ModificationComponentContainerItem>()
                .Find(new Guid(TetherBlockGuid), out bool found);
            if (!found)
            {
                InfoStore.Add("The configured tether block is unavailable.");
                return;
            }

            AllConstruct construct = pointedBlock.GetC();
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
                return;

            Vector3 localDirection = Quaternion.Inverse(construct.myTransform.rotation) *
                                     CameraManager.GetSingleton().transform.rotation *
                                     (forward ? Vector3.forward : Vector3.back);
            Vector3i shift = DominantAxis(localDirection);
            if (shift == Vector3i.zero)
                return;

            Vector3i oldPosition = pointedBlock.LocalPosition;
            Vector3i newPosition = oldPosition + shift;
            var placeCommand = new PlaceBlockCommand(
                construct,
                newPosition,
                Quaternion.identity,
                itemDefinition,
                0,
                MirrorInfo.none);
            placeCommand.Apply();
            if (!placeCommand.Success)
            {
                InfoStore.Add("The tether block cannot be placed in that direction.");
                return;
            }

            var removeCommand = new RemoveBlockCommand(construct, oldPosition, MirrorInfo.none);
            removeCommand.Apply();
            if (!removeCommand.Success)
            {
                placeCommand.Undo();
                InfoStore.Add("The original tether block could not be removed.");
                return;
            }

            if (!decorations.TryGetDecorationsList(oldPosition, out List<Decoration> atPosition))
                return;

            var snapshot = new List<Decoration>(atPosition);
            foreach (Decoration decoration in snapshot)
            {
                Vector3 localOffset = decoration.Positioning.Us - shift;
                if (Mathf.Abs(localOffset.x) <= 10f &&
                    Mathf.Abs(localOffset.y) <= 10f &&
                    Mathf.Abs(localOffset.z) <= 10f)
                {
                    decoration.TetherPoint.Us += shift;
                    decoration.Positioning.Us = localOffset;
                }
            }
        }

        private static Vector3i DominantAxis(Vector3 direction)
        {
            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            float z = Mathf.Abs(direction.z);

            if (x >= y && x >= z)
                return new Vector3i(direction.x >= 0f ? 1 : -1, 0, 0);
            if (y >= x && y >= z)
                return new Vector3i(0, direction.y >= 0f ? 1 : -1, 0);
            if (z > 0f)
                return new Vector3i(0, 0, direction.z >= 0f ? 1 : -1);
            return Vector3i.zero;
        }

        private static void UpdatePointedBlock(ITimeStep timeStep)
        {
            _pointedBlock = GetPointedBlock();
        }
    }
}
