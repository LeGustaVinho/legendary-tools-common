using System;
using System.Collections.Generic;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Groups multiple BindingHandle instances and controls them as a single unit.
    /// </summary>
    public sealed class CompositeBindingHandle : IDisposable
    {
        private readonly List<BindingHandle> _handles = new();
        private bool _disposed;

        public CompositeBindingHandle(IEnumerable<BindingHandle> handles)
        {
            if (handles == null) return;
            _handles.AddRange(handles);
        }

        /// <summary>
        /// Suspends all child bindings.
        /// </summary>
        public void Suspend()
        {
            foreach (BindingHandle h in _handles)
            {
                h?.Suspend();
            }
        }

        /// <summary>
        /// Resumes all child bindings. Optionally resynchronizes after resuming.
        /// </summary>
        public void Resume(bool resync = false)
        {
            foreach (BindingHandle h in _handles)
            {
                h?.Resume(resync);
            }
        }

        /// <summary>
        /// Re-synchronizes all child bindings.
        /// </summary>
        public void Resync()
        {
            foreach (BindingHandle h in _handles)
            {
                h?.Resync();
            }
        }

        /// <summary>
        /// Disposes all child bindings.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    _handles[i]?.Dispose();
                }
                catch
                {
                    /* swallow */
                }
            }

            _handles.Clear();
        }
    }
}