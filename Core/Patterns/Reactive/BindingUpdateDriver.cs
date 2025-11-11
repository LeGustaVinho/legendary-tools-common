using System;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Per-GameObject driver that provides Update/LateUpdate/FixedUpdate ticks to bindings.
    /// Created on demand and reused by multiple bindings on the same GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BindingUpdateDriver : MonoBehaviour
    {
        public event Action OnUpdateTick;
        public event Action OnLateUpdateTick;
        public event Action OnFixedUpdateTick;

        private void Update()
        {
            OnUpdateTick?.Invoke();
        }

        private void LateUpdate()
        {
            OnLateUpdateTick?.Invoke();
        }

        private void FixedUpdate()
        {
            OnFixedUpdateTick?.Invoke();
        }
    }
}