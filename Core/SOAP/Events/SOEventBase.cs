using System;
using UnityEngine;

namespace LegendaryTools.SOAP.Events
{
    /// <summary>
    /// Base ScriptableObject for all SO events.
    /// Provides basic bookkeeping and common guards.
    /// </summary>
    public abstract class SOEventBase : ScriptableObject
    {
        [SerializeField] [Tooltip("Optional description for documentation purposes.")]
        private string _notes;

        [NonSerialized] private bool _isRaising;
        [NonSerialized] private int _raiseCount;

        /// <summary>True while the event is being raised.</summary>
        public bool IsRaising => _isRaising;

        /// <summary>How many times this event has been raised since domain load.</summary>
        public int RaiseCount => _raiseCount;

        /// <summary>Returns total number of listeners currently registered (implementation-defined).</summary>
        public abstract int ListenerCount { get; }

        /// <summary>Remove all listeners (implementation-defined).</summary>
        public abstract void RemoveAllListeners();

        /// <summary>Utility guard to set raising state.</summary>
        protected IDisposable BeginRaiseScope()
        {
            _isRaising = true;
            _raiseCount++;
            return new EndScope(this);
        }

        private sealed class EndScope : IDisposable
        {
            private readonly SOEventBase _owner;

            public EndScope(SOEventBase owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner != null) _owner._isRaising = false;
            }
        }
    }
}