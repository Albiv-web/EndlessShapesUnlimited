using System;
using System.Collections.Generic;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;

namespace DecoLimitLifter.DecorationEditMode
{
    /// <summary>
    /// Process-local clipboard for exact, in-place decoration copies. The payload deliberately
    /// keeps only weak ownership references so an editor clipboard cannot retain an unloaded
    /// construct or decoration manager.
    /// </summary>
    internal static class DecorationSelectionClipboard
    {
        private static readonly object Sync = new object();
        private static DecorationSelectionClipboardPayload _payload;

        internal static bool HasValue
        {
            get
            {
                lock (Sync)
                    return _payload != null;
            }
        }

        internal static bool TryCopy(
            AllConstruct construct,
            Decoration primary,
            IEnumerable<Decoration> selection,
            out string message)
        {
            if (!DecorationSelectionClipboardPayload.TryCreate(
                    construct,
                    primary,
                    selection,
                    out DecorationSelectionClipboardPayload payload,
                    out message))
            {
                return false;
            }

            lock (Sync)
                _payload = payload;
            return true;
        }

        internal static bool TryGetFor(
            AllConstruct construct,
            out DecorationSelectionClipboardPayload payload,
            out string message)
        {
            lock (Sync)
                payload = _payload;

            if (payload == null)
            {
                message = "No decorations have been copied.";
                return false;
            }

            if (payload.MatchesTarget(construct, out message))
                return true;

            payload = null;
            return false;
        }

        internal static void Clear()
        {
            lock (Sync)
                _payload = null;
        }
    }

    internal sealed class DecorationSelectionClipboardPayload
    {
        private readonly WeakReference _construct;
        private readonly WeakReference _manager;
        private readonly DecorationEditSnapshot[] _snapshots;

        private DecorationSelectionClipboardPayload(
            AllConstruct construct,
            AllConstructDecorations manager,
            DecorationEditSnapshot[] snapshots)
        {
            _construct = new WeakReference(construct);
            _manager = new WeakReference(manager);
            _snapshots = snapshots ?? Array.Empty<DecorationEditSnapshot>();
        }

        internal int Count => _snapshots.Length;

        // TryCreate always canonicalizes the primary decoration into slot zero.
        internal int PrimaryIndex => 0;

        internal DecorationEditSnapshot GetSnapshot(int index)
        {
            if (index < 0 || index >= _snapshots.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _snapshots[index];
        }

        internal DecorationEditSnapshot[] CopySnapshots()
        {
            var copies = new DecorationEditSnapshot[_snapshots.Length];
            for (int index = 0; index < _snapshots.Length; index++)
                copies[index] = _snapshots[index].Copy();
            return copies;
        }

        internal bool MatchesTarget(AllConstruct construct, out string message)
        {
            if (construct == null)
            {
                message = "Paste in place needs a focused construct.";
                return false;
            }

            object sourceConstruct = _construct.Target;
            object sourceManager = _manager.Target;
            if (sourceConstruct == null || sourceManager == null)
            {
                message = "The copied construct is no longer available.";
                return false;
            }

            if (!ReferenceEquals(sourceConstruct, construct))
            {
                message = "Paste in place is limited to the construct the decorations were copied from.";
                return false;
            }

            AllConstructDecorations manager;
            try
            {
                manager = construct.Decorations as AllConstructDecorations;
            }
            catch
            {
                message = "The target decoration manager is no longer available.";
                return false;
            }
            if (manager == null || !ReferenceEquals(sourceManager, manager))
            {
                message = "The copied decoration manager is no longer available.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        internal static bool TryCreate(
            AllConstruct construct,
            Decoration primary,
            IEnumerable<Decoration> selection,
            out DecorationSelectionClipboardPayload payload,
            out string message)
        {
            payload = null;
            if (construct == null)
            {
                message = "Copy selection needs a focused construct.";
                return false;
            }

            var manager = construct.Decorations as AllConstructDecorations;
            if (manager == null)
            {
                message = "The focused construct has no decoration manager.";
                return false;
            }

            if (!IsLiveDecorationForManager(primary, manager))
            {
                message = "The primary decoration is no longer available on the focused construct.";
                return false;
            }

            var requested = new List<Decoration> { primary };
            if (selection != null)
            {
                foreach (Decoration decoration in selection)
                {
                    if (!IsLiveDecorationForManager(decoration, manager))
                    {
                        message = "The selection contains a stale decoration or one from another construct.";
                        return false;
                    }

                    if (!ContainsReference(requested, decoration))
                        requested.Add(decoration);
                }
            }

            var managerOrdered = new List<Decoration>(requested.Count);
            foreach (Decoration decoration in manager.DecorationList)
            {
                if (decoration != null && ContainsReference(requested, decoration))
                    managerOrdered.Add(decoration);
            }

            if (managerOrdered.Count != requested.Count ||
                !ContainsReference(managerOrdered, primary))
            {
                message = "The selection changed while it was being copied.";
                return false;
            }

            var ordered = new List<Decoration>(requested.Count) { primary };
            for (int index = 0; index < managerOrdered.Count; index++)
            {
                Decoration decoration = managerOrdered[index];
                if (!ReferenceEquals(decoration, primary))
                    ordered.Add(decoration);
            }

            var snapshots = new DecorationEditSnapshot[ordered.Count];
            for (int index = 0; index < ordered.Count; index++)
            {
                var snapshot = new DecorationEditSnapshot(ordered[index]);
                if (!snapshot.HasFiniteTransform)
                {
                    message = "The selection contains invalid transform values and was not copied.";
                    return false;
                }

                snapshots[index] = snapshot;
            }

            payload = new DecorationSelectionClipboardPayload(construct, manager, snapshots);
            message = ordered.Count == 1
                ? "Copied 1 decoration."
                : "Copied " + ordered.Count + " decorations.";
            return true;
        }

        private static bool IsLiveDecorationForManager(
            Decoration decoration,
            AllConstructDecorations manager)
        {
            if (decoration == null || manager == null)
                return false;

            try
            {
                if (decoration.IsDeleted || !ReferenceEquals(decoration.OurManager, manager))
                    return false;

                foreach (Decoration live in manager.DecorationList)
                {
                    if (ReferenceEquals(live, decoration))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool ContainsReference(
            IReadOnlyList<Decoration> decorations,
            Decoration target)
        {
            for (int index = 0; index < decorations.Count; index++)
            {
                if (ReferenceEquals(decorations[index], target))
                    return true;
            }

            return false;
        }
    }
}
