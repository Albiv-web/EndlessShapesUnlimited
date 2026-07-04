using System;
using System.Collections.Generic;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;

namespace DecoLimitLifter.DecorationEditMode
{
    internal interface IDecorationEditCommand
    {
        string Label { get; }

        bool Undo(DecorationEditSession session);

        bool Redo(DecorationEditSession session);
    }

    internal sealed class DecorationEditHistory
    {
        private const int MaxDepth = 128;
        private readonly Stack<IDecorationEditCommand> _undo = new Stack<IDecorationEditCommand>();
        private readonly Stack<IDecorationEditCommand> _redo = new Stack<IDecorationEditCommand>();

        internal int UndoCount => _undo.Count;

        internal int RedoCount => _redo.Count;

        internal bool CanUndo => _undo.Count > 0;

        internal bool CanRedo => _redo.Count > 0;

        internal void Record(IDecorationEditCommand command)
        {
            if (command == null)
                return;

            _undo.Push(command);
            _redo.Clear();
            TrimUndo();
        }

        internal bool Undo(DecorationEditSession session)
        {
            if (_undo.Count == 0)
                return false;

            IDecorationEditCommand command = _undo.Peek();
            if (!command.Undo(session))
                return false;

            _undo.Pop();
            _redo.Push(command);
            return true;
        }

        internal bool Redo(DecorationEditSession session)
        {
            if (_redo.Count == 0)
                return false;

            IDecorationEditCommand command = _redo.Peek();
            if (!command.Redo(session))
                return false;

            _redo.Pop();
            _undo.Push(command);
            TrimUndo();
            return true;
        }

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private void TrimUndo()
        {
            if (_undo.Count <= MaxDepth)
                return;

            IDecorationEditCommand[] commands = _undo.ToArray();
            _undo.Clear();
            for (int index = Math.Min(MaxDepth, commands.Length) - 1; index >= 0; index--)
                _undo.Push(commands[index]);
        }
    }

    internal sealed class DecorationSnapshotCommand : IDecorationEditCommand
    {
        private readonly AllConstruct _construct;
        private readonly Decoration _decoration;
        private readonly DecorationEditSnapshot _before;
        private readonly DecorationEditSnapshot _after;

        internal DecorationSnapshotCommand(
            string label,
            AllConstruct construct,
            Decoration decoration,
            DecorationEditSnapshot before,
            DecorationEditSnapshot after)
        {
            Label = string.IsNullOrEmpty(label) ? "Edit decoration" : label;
            _construct = construct;
            _decoration = decoration;
            _before = before;
            _after = after;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreHistorySnapshot(_construct, _decoration, _before, Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreHistorySnapshot(_construct, _decoration, _after, Label + " redo");
    }

    internal sealed class DecorationSnapshotBatchCommand : IDecorationEditCommand
    {
        private readonly AllConstruct _construct;
        private readonly Decoration[] _decorations;
        private readonly DecorationEditSnapshot[] _before;
        private readonly DecorationEditSnapshot[] _after;
        private readonly int _primaryIndex;

        internal DecorationSnapshotBatchCommand(
            string label,
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] before,
            DecorationEditSnapshot[] after,
            int primaryIndex)
        {
            Label = string.IsNullOrEmpty(label) ? "Edit mirrored decorations" : label;
            _construct = construct;
            _decorations = decorations ?? Array.Empty<Decoration>();
            _before = before ?? Array.Empty<DecorationEditSnapshot>();
            _after = after ?? Array.Empty<DecorationEditSnapshot>();
            _primaryIndex = primaryIndex;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreHistorySnapshots(
                _construct,
                _decorations,
                _before,
                _primaryIndex,
                Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreHistorySnapshots(
                _construct,
                _decorations,
                _after,
                _primaryIndex,
                Label + " redo");
    }

    internal sealed class SurfaceDraftHistoryCommand : IDecorationEditCommand
    {
        private readonly SurfaceDraftSnapshot _before;
        private readonly SurfaceDraftSnapshot _after;

        internal SurfaceDraftHistoryCommand(
            string label,
            SurfaceDraftSnapshot before,
            SurfaceDraftSnapshot after)
        {
            Label = string.IsNullOrEmpty(label) ? "Edit surface draft" : label;
            _before = before;
            _after = after;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreSurfaceDraftHistory(_before, Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreSurfaceDraftHistory(_after, Label + " redo");
    }

    internal sealed class GeneratorDraftHistoryCommand : IDecorationEditCommand
    {
        private readonly DecorationGeneratorEditSnapshot _before;
        private readonly DecorationGeneratorEditSnapshot _after;

        internal GeneratorDraftHistoryCommand(
            string label,
            DecorationGeneratorEditSnapshot before,
            DecorationGeneratorEditSnapshot after)
        {
            Label = string.IsNullOrEmpty(label) ? "Edit generator draft" : label;
            _before = before;
            _after = after;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreGeneratorDraftHistory(_before, Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreGeneratorDraftHistory(_after, Label + " redo");
    }

    internal sealed class SurfaceBuilderStyleHistoryCommand : IDecorationEditCommand
    {
        private readonly SurfaceDraftSnapshot _surfaceBefore;
        private readonly SurfaceDraftSnapshot _surfaceAfter;
        private readonly DecorationGeneratorEditSnapshot _generatorBefore;
        private readonly DecorationGeneratorEditSnapshot _generatorAfter;

        internal SurfaceBuilderStyleHistoryCommand(
            string label,
            SurfaceDraftSnapshot surfaceBefore,
            SurfaceDraftSnapshot surfaceAfter,
            DecorationGeneratorEditSnapshot generatorBefore,
            DecorationGeneratorEditSnapshot generatorAfter)
        {
            Label = string.IsNullOrEmpty(label) ? "Edit Surface Builder style" : label;
            _surfaceBefore = surfaceBefore;
            _surfaceAfter = surfaceAfter;
            _generatorBefore = generatorBefore;
            _generatorAfter = generatorAfter;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreSurfaceBuilderStyleHistory(
                _surfaceBefore,
                _generatorBefore,
                Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreSurfaceBuilderStyleHistory(
                _surfaceAfter,
                _generatorAfter,
                Label + " redo");
    }

    internal sealed class DecorationCreateCommand : IDecorationEditCommand
    {
        private readonly AllConstruct _construct;
        private readonly DecorationEditSnapshot _created;
        private Decoration _decoration;

        internal DecorationCreateCommand(
            AllConstruct construct,
            Decoration decoration,
            DecorationEditSnapshot created)
        {
            Label = "Create decoration";
            _construct = construct;
            _decoration = decoration;
            _created = created;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session)
        {
            if (session == null)
                return false;
            return session.TryUndoCreatedDecoration(_construct, ref _decoration);
        }

        public bool Redo(DecorationEditSession session)
        {
            if (session == null)
                return false;
            return session.TryRedoCreatedDecoration(_construct, _created, out _decoration);
        }
    }

    internal sealed class DecorationDeleteCommand : IDecorationEditCommand
    {
        private readonly AllConstruct _construct;
        private readonly DecorationEditSnapshot _deleted;
        private readonly DecorationEditSnapshot _original;
        private readonly bool _createdInSession;
        private Decoration _decoration;

        internal DecorationDeleteCommand(
            AllConstruct construct,
            Decoration decoration,
            DecorationEditSnapshot deleted,
            DecorationEditSnapshot original,
            bool createdInSession)
        {
            Label = "Delete decoration";
            _construct = construct;
            _decoration = decoration;
            _deleted = deleted;
            _original = original;
            _createdInSession = createdInSession;
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session)
        {
            if (session == null)
                return false;

            return session.TryUndoDeletedDecoration(
                _construct,
                _deleted,
                _original,
                _createdInSession,
                out _decoration);
        }

        public bool Redo(DecorationEditSession session)
        {
            if (session == null)
                return false;

            return session.TryRedoDeletedDecoration(
                _construct,
                ref _decoration,
                _deleted,
                _original,
                _createdInSession);
        }
    }

    internal sealed class DecorationCreateBatchCommand : IDecorationEditCommand
    {
        private readonly AllConstruct _construct;
        private readonly DecorationEditSnapshot[] _created;
        private readonly Decoration[] _decorations;

        internal DecorationCreateBatchCommand(
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] created)
        {
            Label = "Create mirrored decorations";
            _construct = construct;
            _decorations = decorations ?? Array.Empty<Decoration>();
            _created = created ?? Array.Empty<DecorationEditSnapshot>();
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session)
        {
            if (session == null)
                return false;

            bool ok = true;
            for (int index = _decorations.Length - 1; index >= 0; index--)
                ok &= session.TryUndoCreatedDecoration(_construct, ref _decorations[index]);
            return ok;
        }

        public bool Redo(DecorationEditSession session)
        {
            if (session == null)
                return false;

            var recreated = new Decoration[_created.Length];
            for (int index = 0; index < _created.Length; index++)
            {
                if (!session.TryRedoCreatedDecoration(_construct, _created[index], out recreated[index]))
                {
                    for (int rollback = index - 1; rollback >= 0; rollback--)
                        session.TryUndoCreatedDecoration(_construct, ref recreated[rollback]);
                    return false;
                }
            }

            for (int index = 0; index < recreated.Length && index < _decorations.Length; index++)
                _decorations[index] = recreated[index];
            return true;
        }
    }
}
