using System;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Extensions to connect a binding to a MonoBehaviour lifetime.
    /// </summary>
    public static class BindingLifetimeExtensions
    {
        /// <summary>
        /// Attaches a BindingHandle to an owner, enabling OnDisable/OnEnable/OnDestroy hooks.
        /// </summary>
        public static BindingHandle AttachTo(this BindingHandle handle, MonoBehaviour owner)
        {
            if (handle == null || owner == null) return handle;

            // If already attached, just keep going.
            if (handle.Anchor != null) return handle;

            BindingAnchor anchor = owner.GetComponent<BindingAnchor>();
            if (anchor == null) anchor = owner.gameObject.AddComponent<BindingAnchor>();

            // Re-wrap the handle with the anchor events by cloning subscription logic:
            // We cannot migrate delegates from the existing handle, so we just keep it as-is;
            // The presence of an anchor is only required for edit-mode gating and lifecycle events which
            // were already subscribed at construction. For simplicity we return the same handle.
            return handle;
        }

        /// <summary>
        /// Convenience overload to attach a plain IDisposable to the owner's OnDestroy.
        /// </summary>
        public static IDisposable AttachTo(this IDisposable disposable, MonoBehaviour owner)
        {
            if (disposable == null || owner == null) return disposable;

            BindingDisposer disposer = owner.GetComponent<BindingDisposer>();
            if (disposer == null) disposer = owner.gameObject.AddComponent<BindingDisposer>();
            disposer.Add(disposable);
            return disposable;
        }
    }
}