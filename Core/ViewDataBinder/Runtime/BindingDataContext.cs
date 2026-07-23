using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [AddComponentMenu("Legendary Tools/Binding Data Context")]
    [DisallowMultipleComponent]
    public sealed class BindingDataContext : MonoBehaviour
    {
        [SerializeField] private List<BindingDataContextEntry> contexts =
            new List<BindingDataContextEntry> { new BindingDataContextEntry() };

        private readonly Dictionary<string, BindingInstanceHandle> runtimeContexts =
            new Dictionary<string, BindingInstanceHandle>(StringComparer.Ordinal);

        private static int globalVersion;

        public IReadOnlyList<BindingDataContextEntry> Contexts => contexts;

        internal static int GlobalVersion => globalVersion;

        public void SetContext(string name, object instance, Type declaredType = null)
        {
            string normalizedName = NormalizeName(name);
            if (instance == null)
            {
                runtimeContexts.Remove(normalizedName);
                IncrementVersion();
                return;
            }

            Type resolvedType = instance.GetType() ?? declaredType;
            runtimeContexts[normalizedName] = new BindingInstanceHandle(instance, resolvedType, false);
            IncrementVersion();
        }

        public bool RemoveRuntimeContext(string name)
        {
            bool removed = runtimeContexts.Remove(NormalizeName(name));
            if (removed)
            {
                IncrementVersion();
            }

            return removed;
        }

        public void ClearRuntimeContexts()
        {
            if (runtimeContexts.Count == 0)
            {
                return;
            }

            runtimeContexts.Clear();
            IncrementVersion();
        }

        public bool TryResolveContext(
            string name,
            out BindingInstanceHandle handle,
            out string error)
        {
            string normalizedName = NormalizeName(name);
            if (runtimeContexts.TryGetValue(normalizedName, out handle))
            {
                if (handle.IsValid &&
                    (!(handle.Instance is UnityEngine.Object unityObject) || unityObject != null))
                {
                    error = string.Empty;
                    return true;
                }

                runtimeContexts.Remove(normalizedName);
            }

            for (int i = 0; i < contexts.Count; i++)
            {
                BindingDataContextEntry entry = contexts[i];
                if (entry == null ||
                    !string.Equals(NormalizeName(entry.Name), normalizedName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Value == null)
                {
                    handle = default;
                    error = $"Context '{normalizedName}' has no value configuration.";
                    return false;
                }

                return entry.Value.TryResolve(out handle, out error);
            }

            handle = default;
            error = $"Context '{normalizedName}' is not defined on '{name}'.";
            return false;
        }

        public bool TryGetDeclaredType(string name, out Type type)
        {
            string normalizedName = NormalizeName(name);
            if (runtimeContexts.TryGetValue(normalizedName, out BindingInstanceHandle handle) &&
                handle.Type != null)
            {
                type = handle.Type;
                return true;
            }

            for (int i = 0; i < contexts.Count; i++)
            {
                BindingDataContextEntry entry = contexts[i];
                if (entry == null ||
                    !string.Equals(NormalizeName(entry.Name), normalizedName, StringComparison.Ordinal))
                {
                    continue;
                }

                type = entry.Value?.GetDeclaredType();
                return type != null;
            }

            type = null;
            return false;
        }

        public static string NormalizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? BindingContextConstants.Default
                : name.Trim();
        }

        private void OnEnable()
        {
            IncrementVersion();
        }

        private void OnDisable()
        {
            IncrementVersion();
        }

        private void OnTransformParentChanged()
        {
            IncrementVersion();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            IncrementVersion();
        }
#endif

        private static void IncrementVersion()
        {
            unchecked
            {
                globalVersion++;
            }
        }
    }
}
