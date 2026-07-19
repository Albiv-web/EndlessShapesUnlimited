using System;
using BrilliantSkies.Ftd.Avatar.Build;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// Immutable identity captured with a displayed plan. The exact focused
    /// construct and its main/root craft must still match at commit time.
    /// </summary>
    internal sealed class SmartBuildConstructToken
    {
        private SmartBuildConstructToken(
            AllConstruct construct,
            object rootConstruct,
            long occupancyRevision)
        {
            Construct = construct;
            RootConstruct = rootConstruct;
            OccupancyRevision = Math.Max(0L, occupancyRevision);
        }

        internal AllConstruct Construct { get; }

        internal object RootConstruct { get; }

        internal long OccupancyRevision { get; }

        internal static SmartBuildConstructToken Capture(
            AllConstruct construct,
            long occupancyRevision = 0L) =>
            new SmartBuildConstructToken(
                construct,
                RootFor(construct),
                occupancyRevision);

        internal bool Matches(
            AllConstruct plannedConstruct,
            AllConstruct focusedConstruct,
            out string reason)
        {
            if (Construct == null || plannedConstruct == null ||
                !ReferenceEquals(Construct, plannedConstruct))
            {
                reason = "The Smart Builder plan construct token no longer matches its plan.";
                return false;
            }
            if (!ReferenceEquals(Construct, focusedConstruct))
            {
                reason = "The Smart Builder plan is no longer focused on its captured construct.";
                return false;
            }

            object focusedRoot = RootFor(focusedConstruct);
            if (RootConstruct != null && focusedRoot != null &&
                !ReferenceEquals(RootConstruct, focusedRoot))
            {
                reason = "The Smart Builder plan root craft changed after preview.";
                return false;
            }

            reason = null;
            return true;
        }

        private static object RootFor(AllConstruct construct)
        {
            if (construct == null)
                return null;
            try
            {
                return construct is MainConstruct
                    ? (object)construct
                    : construct.Main ?? (object)construct;
            }
            catch
            {
                return construct;
            }
        }
    }

    /// <summary>
    /// Atomic scene mutation boundary shared by typed tools and future viewport
    /// gestures. A failed mutation restores the exact prior node graph.
    /// </summary>
    internal sealed class SmartBuildSceneController
    {
        internal SmartBuildSceneController(SmartBuildPieceScene scene)
        {
            Scene = scene;
        }

        internal SmartBuildPieceScene Scene { get; private set; }

        internal long Revision { get; private set; }

        internal void Replace(SmartBuildPieceScene scene)
        {
            Scene = scene;
            AdvanceRevision();
        }

        internal bool TryMutate(
            Func<SmartBuildPieceScene, bool> mutation,
            out SmartBuildSceneStateDelta delta,
            out string reason)
        {
            delta = null;
            if (Scene == null || mutation == null)
            {
                reason = "No Smart Builder scene mutation is available.";
                return false;
            }

            SmartBuildSceneState before = Scene.CaptureState();
            try
            {
                if (!mutation(Scene))
                {
                    Scene = SmartBuildPieceScene.RestoreState(before);
                    reason = "The Smart Builder scene mutation was rejected.";
                    return false;
                }

                SmartBuildSceneState after = Scene.CaptureState();
                delta = SmartBuildSceneStateDelta.Create(before, after);
                if (!delta.HasSceneChanges)
                {
                    delta = null;
                    reason = null;
                    return true;
                }
                AdvanceRevision();
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                Scene = SmartBuildPieceScene.RestoreState(before);
                reason = "The Smart Builder scene mutation failed and was restored: " +
                         exception.Message;
                return false;
            }
        }

        internal bool TryApplyDelta(
            SmartBuildSceneStateDelta delta,
            bool forward,
            out string reason)
        {
            if (Scene == null || delta == null)
            {
                reason = "No Smart Builder scene delta is available.";
                return false;
            }
            SmartBuildSceneState target = Scene.CaptureState().Apply(delta, forward);
            SmartBuildPieceScene restored = SmartBuildPieceScene.RestoreState(target);
            if (restored == null && target.Nodes.Count > 0)
            {
                reason = "The Smart Builder scene delta could not be restored.";
                return false;
            }
            Scene = restored;
            AdvanceRevision();
            reason = null;
            return true;
        }

        private void AdvanceRevision()
        {
            unchecked
            {
                Revision++;
                if (Revision < 0L)
                    Revision = 0L;
            }
        }
    }
}
