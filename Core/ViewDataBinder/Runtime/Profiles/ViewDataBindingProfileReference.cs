using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataBindingProfileReference : ISerializationCallbackReceiver
    {
        [NonSerialized] private Dictionary<string, string> stateKeys;
        [NonSerialized] private Dictionary<string, BindingInstanceHandle> runtimeContexts;
        [NonSerialized] private string stateKeyPrefix;
        [NonSerialized] private Dictionary<string, BindingContextValue> serializedContextLookup;
        [SerializeField, HideInInspector] private string id;
        [SerializeField] private bool enabled = true;
        [SerializeField] private ViewDataBindingProfile profile;
        [SerializeField] private BindingContextValue sourceRoot = new BindingContextValue();
        [SerializeField] private BindingContextValue targetRoot = new BindingContextValue();
        [SerializeField] private List<BindingNamedContextOverride> namedContexts =
            new List<BindingNamedContextOverride>();

        public string Id => id;

        public bool Enabled => enabled;

        public ViewDataBindingProfile Profile => profile;

        public BindingContextValue SourceRoot => sourceRoot;

        public BindingContextValue TargetRoot => targetRoot;

        public IReadOnlyList<BindingNamedContextOverride> NamedContexts => namedContexts;

        internal void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }

        internal bool DefinesContext(string contextName)
        {
            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (runtimeContexts != null && runtimeContexts.ContainsKey(normalizedName))
            {
                return true;
            }

            if (string.Equals(normalizedName, BindingContextConstants.ProfileSource, StringComparison.Ordinal) ||
                string.Equals(normalizedName, BindingContextConstants.ProfileTarget, StringComparison.Ordinal))
            {
                return true;
            }

            EnsureSerializedContextLookup();
            return serializedContextLookup.ContainsKey(normalizedName);
        }

        internal bool TryResolveContext(
            string contextName,
            out BindingInstanceHandle handle,
            out string error)
        {
            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (runtimeContexts != null &&
                runtimeContexts.TryGetValue(normalizedName, out handle))
            {
                if (handle.IsValid &&
                    (!(handle.Instance is UnityEngine.Object unityObject) || unityObject != null))
                {
                    error = string.Empty;
                    return true;
                }

                runtimeContexts.Remove(normalizedName);
            }

            BindingContextValue value = FindContextValue(normalizedName);
            if (value == null)
            {
                handle = default;
                error = $"Profile override '{contextName}' is not defined.";
                return false;
            }

            return value.TryResolve(out handle, out error);
        }

        internal bool TryGetDeclaredContextType(string contextName, out Type type)
        {
            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (runtimeContexts != null &&
                runtimeContexts.TryGetValue(normalizedName, out BindingInstanceHandle handle) &&
                handle.Type != null)
            {
                type = handle.Type;
                return true;
            }

            BindingContextValue value = FindContextValue(normalizedName);
            type = value?.GetDeclaredType();
            return type != null;
        }

        internal bool SetRuntimeContext(
            string contextName,
            object instance,
            Type declaredType)
        {
            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (instance == null ||
                (instance is UnityEngine.Object unityObject && unityObject == null))
            {
                return RemoveRuntimeContext(normalizedName);
            }

            Type resolvedType = instance.GetType() ?? declaredType;
            if (resolvedType == null)
            {
                return false;
            }

            if (runtimeContexts == null)
            {
                runtimeContexts = new Dictionary<string, BindingInstanceHandle>(StringComparer.Ordinal);
            }

            runtimeContexts[normalizedName] = new BindingInstanceHandle(instance, resolvedType, false);
            return true;
        }

        internal bool RemoveRuntimeContext(string contextName)
        {
            return runtimeContexts != null &&
                   runtimeContexts.Remove(BindingDataContext.NormalizeName(contextName));
        }

        internal string GetStateKey(string bindingId)
        {
            EnsureId();
            if (stateKeys == null)
            {
                stateKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            if (string.IsNullOrEmpty(stateKeyPrefix))
            {
                stateKeyPrefix = "profile:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this) + ":";
            }

            if (!stateKeys.TryGetValue(bindingId, out string stateKey))
            {
                stateKey = stateKeyPrefix + bindingId;
                stateKeys.Add(bindingId, stateKey);
            }

            return stateKey;
        }

        private void EnsureSerializedContextLookup()
        {
            if (serializedContextLookup != null)
            {
                return;
            }

            serializedContextLookup =
                new Dictionary<string, BindingContextValue>(StringComparer.Ordinal);
            for (int i = 0; i < namedContexts.Count; i++)
            {
                BindingNamedContextOverride entry = namedContexts[i];
                if (entry == null)
                {
                    continue;
                }

                string normalizedName = BindingDataContext.NormalizeName(entry.Name);
                if (!serializedContextLookup.ContainsKey(normalizedName))
                {
                    serializedContextLookup.Add(normalizedName, entry.Value);
                }
            }
        }

        internal void InvalidateSerializedCaches()
        {
            serializedContextLookup = null;
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            InvalidateSerializedCaches();
            stateKeys = null;
            stateKeyPrefix = null;
            runtimeContexts = null;
        }

        private BindingContextValue FindContextValue(string contextName)
        {
            if (string.Equals(contextName, BindingContextConstants.ProfileSource, StringComparison.Ordinal))
            {
                return sourceRoot;
            }

            if (string.Equals(contextName, BindingContextConstants.ProfileTarget, StringComparison.Ordinal))
            {
                return targetRoot;
            }

            EnsureSerializedContextLookup();
            return serializedContextLookup.TryGetValue(contextName, out BindingContextValue value)
                ? value
                : null;
        }
    }
}
