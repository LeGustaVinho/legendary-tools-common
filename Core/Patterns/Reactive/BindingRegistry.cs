using System;
using System.Collections.Generic;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Global registry of live bindings for diagnostics (Editor/runtime).
    /// Uses WeakReference to avoid memory retention.
    /// </summary>
    public static class BindingRegistry
    {
        private static readonly object _gate = new();
        private static readonly List<WeakReference<BindingHandle>> _items = new();

        internal static void Register(BindingHandle handle)
        {
            if (handle == null) return;
            lock (_gate)
            {
                _items.Add(new WeakReference<BindingHandle>(handle));
            }
        }

        internal static void Unregister(BindingHandle handle)
        {
            if (handle == null) return;
            lock (_gate)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i].TryGetTarget(out BindingHandle h) && ReferenceEquals(h, handle))
                    {
                        _items.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a stable snapshot of currently alive handles. Dead references are pruned.
        /// </summary>
        public static BindingHandle[] Snapshot()
        {
            lock (_gate)
            {
                List<BindingHandle> result = new(_items.Count);
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i].TryGetTarget(out BindingHandle h))
                        result.Add(h);
                    else
                        _items.RemoveAt(i);
                }

                return result.ToArray();
            }
        }
    }
}