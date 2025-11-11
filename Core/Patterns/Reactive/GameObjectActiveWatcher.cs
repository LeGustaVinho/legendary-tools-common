using System;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Emits events on GameObject activation changes (OnEnable/OnDisable).
    /// Useful to implement TwoWay binding for GameObject active state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameObjectActiveWatcher : MonoBehaviour
    {
        public event Action<bool> OnActiveChanged; // true on enable, false on disable

        private void OnEnable()
        {
            OnActiveChanged?.Invoke(true);
        }

        private void OnDisable()
        {
            OnActiveChanged?.Invoke(false);
        }
    }
}