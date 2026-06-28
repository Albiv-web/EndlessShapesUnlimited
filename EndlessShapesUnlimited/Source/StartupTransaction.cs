using System;
using System.Collections.Generic;

namespace DecoLimitLifter
{
    internal sealed class StartupTransaction
    {
        private readonly Action _unpatch;
        private readonly List<Action> _rollbackActions = new List<Action>();
        private bool _committed;
        private bool _rolledBack;

        internal StartupTransaction(Action unpatch)
        {
            _unpatch = unpatch ?? throw new ArgumentNullException(nameof(unpatch));
        }

        internal void TrackDecorationLimit(int previousValue, Action<int> restore)
        {
            if (restore == null)
                throw new ArgumentNullException(nameof(restore));
            TrackRollback(() => restore(previousValue));
        }

        internal void TrackRollback(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (_committed || _rolledBack)
                throw new InvalidOperationException("The startup transaction is no longer active.");
            _rollbackActions.Add(action);
        }

        internal void Commit()
        {
            _committed = true;
            _rollbackActions.Clear();
        }

        internal IReadOnlyList<Exception> Rollback()
        {
            var errors = new List<Exception>();
            if (_committed || _rolledBack)
                return errors;
            _rolledBack = true;

            for (int i = _rollbackActions.Count - 1; i >= 0; i--)
            {
                try { _rollbackActions[i](); }
                catch (Exception exception) { errors.Add(exception); }
            }
            _rollbackActions.Clear();

            try { _unpatch(); }
            catch (Exception exception) { errors.Add(exception); }
            return errors;
        }
    }
}
