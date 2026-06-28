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
}
