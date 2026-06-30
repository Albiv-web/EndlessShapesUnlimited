using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ui.Special.InfoStore;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditTransactionSet
    {
        private readonly Dictionary<Decoration, DecorationEditSnapshot> _originals =
            new Dictionary<Decoration, DecorationEditSnapshot>();
        private readonly HashSet<Decoration> _created = new HashSet<Decoration>();

        internal bool HasChanges
        {
            get
            {
                PruneDeleted();
                if (_created.Count > 0)
                    return true;

                foreach (KeyValuePair<Decoration, DecorationEditSnapshot> pair in _originals)
                {
                    if (pair.Key != null &&
                        !pair.Key.IsDeleted &&
                        pair.Value != null &&
                        !pair.Value.Matches(pair.Key))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void TrackEdit(Decoration decoration, DecorationEditSnapshot before)
        {
            if (decoration == null || decoration.IsDeleted || before == null)
                return;
            if (_created.Contains(decoration) || _originals.ContainsKey(decoration))
                return;
            _originals.Add(decoration, before);
        }

        internal void MarkCreated(Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted)
                return;
            _originals.Remove(decoration);
            _created.Add(decoration);
        }

        internal void UnmarkCreated(Decoration decoration)
        {
            if (decoration == null)
                return;
            _created.Remove(decoration);
        }

        internal bool IsCreated(Decoration decoration) =>
            decoration != null && _created.Contains(decoration);

        internal DecorationEditSnapshot GetOriginal(Decoration decoration)
        {
            if (decoration == null)
                return null;
            return _originals.TryGetValue(decoration, out DecorationEditSnapshot snapshot)
                ? snapshot
                : null;
        }

        internal void Apply()
        {
            _originals.Clear();
            _created.Clear();
        }

        internal void Cancel()
        {
            Exception failure = null;
            foreach (Decoration decoration in _created)
            {
                if (decoration == null || decoration.IsDeleted)
                    continue;
                try
                {
                    decoration.Delete();
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(failure, exception);
                }
            }

            foreach (KeyValuePair<Decoration, DecorationEditSnapshot> pair in _originals)
            {
                Decoration decoration = pair.Key;
                if (decoration == null || decoration.IsDeleted)
                    continue;
                try
                {
                    if (!pair.Value.TryRestore(decoration))
                        InfoStore.Add("Decoration Edit cancel could not restore one tether/index.");
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(failure, exception);
                }
            }

            _originals.Clear();
            _created.Clear();

            if (failure != null)
            {
                EsuRuntimeLog.Exception("Decoration Edit", failure, "Decoration Edit transaction rollback had failures");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit transaction rollback had failures",
                    failure,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Decoration Edit rollback had failures; see the log.");
            }
        }

        private void PruneDeleted()
        {
            if (_created.RemoveWhere(decoration => decoration == null || decoration.IsDeleted) > 0)
            {
                // State was only pruned; the caller computes dirty state after this.
            }

            List<Decoration> remove = null;
            foreach (Decoration decoration in _originals.Keys)
            {
                if (decoration != null && !decoration.IsDeleted)
                    continue;
                if (remove == null)
                    remove = new List<Decoration>();
                remove.Add(decoration);
            }

            if (remove == null)
                return;
            for (int index = 0; index < remove.Count; index++)
                _originals.Remove(remove[index]);
        }
    }
}
