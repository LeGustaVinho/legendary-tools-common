using System;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Represents a live binding subscription with suspend/resume and resync controls.
    /// Automatically registers in BindingRegistry for diagnostics.
    /// </summary>
    public sealed class BindingHandle : IDisposable
    {
        private readonly Action _subscribe;
        private readonly Action _unsubscribe;
        private readonly Action _resync;
        private bool _isDisposed;
        private bool _isSubscribed;
        private bool _isSuspended;

        internal BindingAnchor Anchor { get; }
        internal BindingOptions Options { get; }

        /// <summary>
        /// Diagnostic metadata for this binding.
        /// </summary>
        public BindingInfo Info { get; }

        public BindingHandle(Action subscribe,
            Action unsubscribe,
            Action resync,
            BindingAnchor anchor,
            BindingOptions options,
            BindingInfo info = null)
        {
            _subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
            _resync = resync ?? (() => { });
            Anchor = anchor;
            Options = options ?? new BindingOptions();
            Info = info ?? new BindingInfo { Anchor = anchor, Options = Options };

            SubscribeInternal();
            BindingRegistry.Register(this);

            if (Anchor != null)
            {
                Anchor.OwnerDisabled += OnOwnerDisabled;
                Anchor.OwnerEnabled += OnOwnerEnabled;
                Anchor.OwnerDestroyed += OnOwnerDestroyed;
            }
        }

        /// <summary> Suspends the binding (no UI updates, and UI events are ignored). </summary>
        public void Suspend()
        {
            _isSuspended = true;
        }

        /// <summary> Resumes the binding. If resync is true, performs a resynchronization immediately. </summary>
        public void Resume(bool resync = false)
        {
            _isSuspended = false;
            if (resync) Resync();
        }

        /// <summary> Re-synchronizes UI from source (or vice versa by implementation). </summary>
        public void Resync()
        {
            if (_isDisposed || !_isSubscribed) return;
            _resync?.Invoke();
        }

        /// <summary> Unsubscribes everything and releases the binding. </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_isSubscribed)
            {
                _isSubscribed = false;
                _unsubscribe();
            }

            if (Anchor != null)
            {
                Anchor.OwnerDisabled -= OnOwnerDisabled;
                Anchor.OwnerEnabled -= OnOwnerEnabled;
                Anchor.OwnerDestroyed -= OnOwnerDestroyed;
            }

            BindingRegistry.Unregister(this);
        }

        /// <summary> Indicates whether this binding is currently suspended. </summary>
        public bool IsSuspended => _isSuspended;

        /// <summary> Indicates whether this binding is currently subscribed. </summary>
        public bool IsSubscribed => _isSubscribed && !_isDisposed;

        internal bool CanProcessNow()
        {
            if (_isSuspended) return false;
            if (Anchor == null) return true;
            return Anchor.ShouldProcessNow();
        }

        private void OnOwnerDisabled()
        {
            if (!Options.UnbindOnDisable) return;
            if (_isSubscribed)
            {
                _isSubscribed = false;
                _unsubscribe();
            }
        }

        private void OnOwnerEnabled()
        {
            if (!Options.UnbindOnDisable)
            {
                if (Options.ResyncOnEnable) Resync();
                return;
            }

            if (!_isSubscribed)
            {
                SubscribeInternal();
                if (Options.ResyncOnEnable) Resync();
            }
        }

        private void OnOwnerDestroyed()
        {
            Dispose();
        }

        private void SubscribeInternal()
        {
            if (_isSubscribed) return;
            _isSubscribed = true;
            _subscribe();
        }
    }
}