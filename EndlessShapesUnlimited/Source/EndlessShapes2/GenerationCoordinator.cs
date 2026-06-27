using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BrilliantSkies.Ftd.Constructs.Connections;

namespace EndlessShapes2
{
    internal sealed class GenerationLease
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<MainConstruct> Active =
            new HashSet<MainConstruct>(ReferenceComparer<MainConstruct>.Instance);

        private readonly MainConstruct _mainConstruct;
        private readonly ConnectionRules _connectionRules;
        private readonly bool _previousMasterSwitch;
        private readonly bool _previousRequestSwitch;
        private bool _released;

        private GenerationLease(MainConstruct mainConstruct, ConnectionRules connectionRules)
        {
            _mainConstruct = mainConstruct;
            _connectionRules = connectionRules;
            _previousMasterSwitch = connectionRules.Data.MasterSwitch.Us;
            _previousRequestSwitch = connectionRules.Data.RequestSwitch.Us;

            try
            {
                connectionRules.Data.MasterSwitch.Us = false;
                connectionRules.Data.RequestSwitch.Us = false;
            }
            catch
            {
                TryRestore(connectionRules, _previousMasterSwitch, _previousRequestSwitch, null);
                lock (Gate)
                    Active.Remove(mainConstruct);
                throw;
            }
        }

        internal static bool TryAcquire(
            MainConstruct mainConstruct,
            ConnectionRules connectionRules,
            out GenerationLease lease)
        {
            if (mainConstruct == null)
                throw new ArgumentNullException(nameof(mainConstruct));
            if (connectionRules == null)
                throw new ArgumentNullException(nameof(connectionRules));

            lock (Gate)
            {
                if (!Active.Add(mainConstruct))
                {
                    lease = null;
                    return false;
                }
            }

            lease = new GenerationLease(mainConstruct, connectionRules);
            return true;
        }

        internal void Release(ICollection<Exception> errors)
        {
            if (_released)
                return;
            _released = true;

            try
            {
                TryRestore(
                    _connectionRules,
                    _previousMasterSwitch,
                    _previousRequestSwitch,
                    errors);
            }
            finally
            {
                lock (Gate)
                    Active.Remove(_mainConstruct);
            }
        }

        private static void TryRestore(
            ConnectionRules rules,
            bool master,
            bool request,
            ICollection<Exception> errors)
        {
            try
            {
                rules.Data.MasterSwitch.Us = master;
            }
            catch (Exception exception)
            {
                errors?.Add(exception);
            }

            try
            {
                rules.Data.RequestSwitch.Us = request;
            }
            catch (Exception exception)
            {
                errors?.Add(exception);
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T>
            where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T left, T right) => ReferenceEquals(left, right);

            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
