using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Generic ScriptableObject-backed variable.
    /// Stores design-time InitialValue in the asset; at runtime, read/write Value.
    /// Optional persistence via IPersistence provider (opt-in per variable).
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public abstract class SOVariable<T> : ScriptableObject, IVariable<T>
    {
        [Header("Config")] [SerializeField] private T _initialValue = default;

        /// <summary>Invoked whenever Value changes. Payload: (oldValue, newValue).</summary>
        [SerializeField] public ValueChangedEvent<T> OnValueChanged = new();

        // ---- Persistence (opt-in) ----
        [Header("Persistence (opt-in)")]
        [Tooltip("Enable persistence for this variable using the selected provider.")]
        [SerializeField]
        private bool _usePersistence = false;

        [Tooltip("Provider used to save/load the runtime value. Leave null to disable.")] [SerializeField]
        private PersistenceProviderSO _persistenceProvider = null;

        [Tooltip("Key used by the provider. If empty, uses <AssetName>.")] [SerializeField]
        private string _persistenceKey = "";

        [Tooltip("Automatically call provider.Save() after Set/Delete operations.")] [SerializeField]
        private bool _autoSave = true;

        [Tooltip("When loading for the first time and there is no stored value, write InitialValue to storage.")]
        [SerializeField]
        private bool _initializeStorageWithInitialValue = true;

        // -------------------------------

        [NonSerialized] private T _runtimeValue;
        [NonSerialized] private bool _initialized;

        /// <summary>Current runtime value. Setting triggers OnValueChanged if different.</summary>
        public T Value
        {
            get
            {
                EnsureInitialized();
                return _runtimeValue;
            }
            set => SetValue(value);
        }

        /// <summary>Initial value configured on the asset.</summary>
        public T InitialValue
        {
            get => _initialValue;
            set => _initialValue = value;
        }

        /// <summary>Equality comparer used to detect changes. Override for custom types if needed.</summary>
        protected virtual IEqualityComparer<T> Comparer => EqualityComparer<T>.Default;

        /// <summary>Assign without firing events. Still persists when enabled.</summary>
        public virtual void SetValueSilent(T value)
        {
            EnsureInitialized();
            _runtimeValue = value;

            if (CanPersist()) _persistenceProvider.Set(value, GetPersistenceKey(), 1, _autoSave);
        }

        /// <summary>Assign and fire events if changed. Persists when enabled.</summary>
        public virtual void SetValue(T value)
        {
            EnsureInitialized();
            if (Comparer.Equals(_runtimeValue, value))
                return;

            T old = _runtimeValue;
            _runtimeValue = value;
            OnValueChanged?.Invoke(old, _runtimeValue);

            if (CanPersist()) _persistenceProvider.Set(value, GetPersistenceKey(), 1, _autoSave);
        }

        /// <summary>Reset runtime to initial value and persist when enabled.</summary>
        public void ResetToInitial()
        {
            SetValue(_initialValue);
        }

        /// <summary>Apply a functional modifier to the value (fires event if changed).</summary>
        public void Modify(Func<T, T> transformer)
        {
            if (transformer == null) return;
            SetValue(transformer(Value));
        }

        /// <summary>Apply a functional modifier without firing event.</summary>
        public void ModifySilent(Func<T, T> transformer)
        {
            if (transformer == null) return;
            SetValueSilent(transformer(Value));
        }

        /// <summary>Ensures the runtime value is initialized from the initial value (or persistence when enabled).</summary>
        protected void EnsureInitialized()
        {
            if (_initialized) return;

            _runtimeValue = _initialValue;

            if (CanPersist())
                try
                {
                    _persistenceProvider.Load();

                    string key = GetPersistenceKey();
                    bool has = _persistenceProvider.Contains<T>(key);

                    if (has)
                        _runtimeValue = _persistenceProvider.Get<T>(key, _initialValue);
                    else if (_initializeStorageWithInitialValue)
                        _persistenceProvider.Set(_initialValue, key, 1, _autoSave);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[SOVariable<{typeof(T).Name}>] Persistence load failed for '{name}': {e.Message}");
                }

            _initialized = true;
        }

        protected virtual void OnEnable()
        {
            _initialized = false;
            EnsureInitialized();
        }

        private bool CanPersist()
        {
            return _usePersistence && _persistenceProvider != null;
        }

        private string GetPersistenceKey()
        {
            string token = string.IsNullOrWhiteSpace(_persistenceKey) ? name : _persistenceKey;
            return token;
        }
    }
}