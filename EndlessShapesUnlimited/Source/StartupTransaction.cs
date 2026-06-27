using System;
using System.Collections.Generic;

namespace DecoLimitLifter
{
    internal sealed class StartupTransaction
    {
        private readonly Action _unpatch;
        private Action _restoreDecorationLimit;
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
            if (_restoreDecorationLimit != null)
                throw new InvalidOperationException("The decoration limit is already tracked.");
            _restoreDecorationLimit = () => restore(previousValue);
        }

        internal void Commit()
        {
            _committed = true;
            _restoreDecorationLimit = null;
        }

        internal IReadOnlyList<Exception> Rollback()
        {
            var errors = new List<Exception>();
            if (_committed || _rolledBack)
                return errors;
            _rolledBack = true;

            if (_restoreDecorationLimit != null)
            {
                try { _restoreDecorationLimit(); }
                catch (Exception exception) { errors.Add(exception); }
                _restoreDecorationLimit = null;
            }

            try { _unpatch(); }
            catch (Exception exception) { errors.Add(exception); }
            return errors;
        }
    }
}
