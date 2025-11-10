using System;
using UnityEngine;
using UnityEngine.Events;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Binds a variable to UnityEvents in the scene.
    /// </summary>
    public abstract class VariableListener<T> : MonoBehaviour
    {
        [SerializeField] private SOVariable<T> _variable;

        [Serializable]
        public class TEvent : UnityEvent<T, T>
        {
        }

        [SerializeField] private TEvent _onChanged = new();

        /// <summary>Invoked on Start with (Value, Value) if <see cref="invokeOnStart"/> is true.</summary>
        [SerializeField] private bool invokeOnStart = true;

        protected virtual void OnEnable()
        {
            if (_variable != null)
                _variable.OnValueChanged.AddListener(HandleChanged);
        }

        protected virtual void OnDisable()
        {
            if (_variable != null)
                _variable.OnValueChanged.RemoveListener(HandleChanged);
        }

        protected virtual void Start()
        {
            if (invokeOnStart && _variable != null)
                _onChanged?.Invoke(_variable.Value, _variable.Value);
        }

        private void HandleChanged(T oldValue, T newValue)
        {
            _onChanged?.Invoke(oldValue, newValue);
        }
    }
}