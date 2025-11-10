using System;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Variant asset that references a base ScriptableObject and stores overridden values in a hidden payload
    /// of the same concrete type. At runtime you can resolve to a concrete instance (base + overrides).
    /// </summary>
    [CreateAssetMenu(menuName = "Variants/ScriptableObject Variant", fileName = "NewScriptableObjectVariant")]
    public sealed class ScriptableObjectVariant : ScriptableObject
    {
        [Tooltip("Reference ScriptableObject this variant is based on.")]
        public ScriptableObject BaseAsset;

        [SerializeField] [HideInInspector] private ScriptableObject _payload;

        [SerializeField] private VariantOverrideSet _overrides = new();

        /// <summary>Gets the hidden payload (same type as BaseAsset). Editor guarantees type.</summary>
        public ScriptableObject Payload => _payload;

        /// <summary>Gets the set of overridden property paths (Unity-style).</summary>
        public VariantOverrideSet Overrides => _overrides;

        /// <summary>
        /// Editor-only: ensure payload exists and matches the base type.
        /// If initializeIfNew == true, clones the base into payload ONLY when the payload is created/recreated.
        /// </summary>
        public void __Editor_EnsurePayload(bool initializeIfNew = false)
        {
#if UNITY_EDITOR
            if (BaseAsset == null)
                return;

            Type baseType = BaseAsset.GetType();

            bool recreated = false;
            if (_payload != null && _payload.GetType() != baseType)
            {
                DestroyImmediate(_payload, true);
                _payload = null;
            }

            if (_payload == null)
            {
                _payload = CreateInstance(baseType);
                _payload.name = $"{name}_Payload ({baseType.Name})";
                UnityEditor.AssetDatabase.AddObjectToAsset(_payload, this);
                recreated = true;
            }

            // Only initialize when payload has just been created/recreated
            if (initializeIfNew && recreated)
            {
                VariantResolver.__EDITOR_CopyAll(BaseAsset, _payload);
                UnityEditor.EditorUtility.SetDirty(_payload);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>Resolves to a new instance of T with base values plus overrides applied.</summary>
        public T Resolve<T>() where T : ScriptableObject
        {
            if (BaseAsset == null)
                throw new InvalidOperationException("Variant has no BaseAsset assigned.");

            if (!(BaseAsset is T))
                throw new InvalidOperationException(
                    $"BaseAsset type '{BaseAsset.GetType().Name}' is not assignable to requested '{typeof(T).Name}'.");

            return VariantResolver.Resolve<T>(this);
        }

        /// <summary>Resolves to a new instance (non-generic) with base values plus overrides applied.</summary>
        public ScriptableObject Resolve()
        {
            if (BaseAsset == null)
                throw new InvalidOperationException("Variant has no BaseAsset assigned.");

            return VariantResolver.Resolve(this, BaseAsset.GetType());
        }
    }
}