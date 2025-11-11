using System;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Lifetime host that informs bindings about OnDisable/OnEnable/OnDestroy and supports Edit Mode updates.
    /// </summary>
    [ExecuteAlways]
    public sealed class BindingAnchor : MonoBehaviour
    {
        /// <summary>
        /// If true, bindings subscribed to this anchor will unsubscribe on OnDisable and resubscribe on OnEnable.
        /// </summary>
        [Tooltip("Unsubscribe bindings on OnDisable and resubscribe on OnEnable.")]
        public bool unbindOnDisable = true;

        /// <summary>
        /// If true, bindings subscribed to this anchor will resync when the object is enabled again.
        /// </summary>
        [Tooltip("Resync bindings when the object becomes enabled again.")]
        public bool resyncOnEnable = true;

        /// <summary>
        /// If true, allows pushing updates even in Edit Mode (outside Play).
        /// </summary>
        [Tooltip("Allow updates in Edit Mode (outside Play mode).")]
        public bool updateInEditMode = false;

        /// <summary>
        /// Raised when the owner is disabled.
        /// </summary>
        public event Action OwnerDisabled;

        /// <summary>
        /// Raised when the owner is enabled.
        /// </summary>
        public event Action OwnerEnabled;

        /// <summary>
        /// Raised when the owner is destroyed.
        /// </summary>
        public event Action OwnerDestroyed;

        /// <summary>
        /// Indicates whether UI updates should be processed in the current context.
        /// </summary>
        public bool ShouldProcessNow()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return updateInEditMode;
#endif
            return true;
        }

        private void OnDisable()
        {
            OwnerDisabled?.Invoke();
        }

        private void OnEnable()
        {
            OwnerEnabled?.Invoke();
        }

        private void OnDestroy()
        {
            OwnerDestroyed?.Invoke();
        }
    }
}