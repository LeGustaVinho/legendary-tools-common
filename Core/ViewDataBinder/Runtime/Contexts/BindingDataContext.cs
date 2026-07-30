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
        private readonly Dictionary<string, BindingDataContextEntry> serializedContextLookup =
            new Dictionary<string, BindingDataContextEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> publishedNames =
            new HashSet<string>(StringComparer.Ordinal);
        private bool serializedContextLookupDirty = true;

        private static readonly Dictionary<string, int> ContextVersions =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly object ContextVersionsLock = new object();

        public IReadOnlyList<BindingDataContextEntry> Contexts => contexts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lock (ContextVersionsLock)
            {
                ContextVersions.Clear();
            }
        }

        internal static int GetVersion(string name)
        {
            string normalizedName = NormalizeName(name);
            lock (ContextVersionsLock)
            {
                return ContextVersions.TryGetValue(normalizedName, out int version)
                    ? version
                    : 0;
            }
        }

        public void SetContext(string name, object instance, Type declaredType = null)
        {
            string normalizedName = NormalizeName(name);
            if (instance == null)
            {
                if (runtimeContexts.Remove(normalizedName))
                {
                    PublishNameChange(normalizedName);
                }

                return;
            }

            Type resolvedType = instance.GetType() ?? declaredType;
            runtimeContexts[normalizedName] = new BindingInstanceHandle(instance, resolvedType, false);
            PublishNameChange(normalizedName);
        }

        public bool RemoveRuntimeContext(string name)
        {
            bool removed = runtimeContexts.Remove(NormalizeName(name));
            if (removed)
            {
                PublishNameChange(NormalizeName(name));
            }

            return removed;
        }

        public void ClearRuntimeContexts()
        {
            if (runtimeContexts.Count == 0)
            {
                return;
            }

            string[] changedNames = new string[runtimeContexts.Count];
            runtimeContexts.Keys.CopyTo(changedNames, 0);
            runtimeContexts.Clear();
            for (int i = 0; i < changedNames.Length; i++)
            {
                PublishNameChange(changedNames[i]);
            }
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
                PublishNameChange(normalizedName);
            }

            EnsureSerializedContextLookup();
            if (serializedContextLookup.TryGetValue(normalizedName, out BindingDataContextEntry entry))
            {
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

            EnsureSerializedContextLookup();
            if (serializedContextLookup.TryGetValue(normalizedName, out BindingDataContextEntry entry))
            {
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
            serializedContextLookupDirty = true;
            PublishAllNameChanges();
        }

        private void OnDisable()
        {
            PublishPublishedNameChanges();
        }

        private void OnTransformParentChanged()
        {
            PublishPublishedNameChanges();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            serializedContextLookupDirty = true;
            PublishAllNameChanges();
        }
#endif

        private void EnsureSerializedContextLookup()
        {
            if (!serializedContextLookupDirty)
            {
                return;
            }

            serializedContextLookup.Clear();
            for (int i = 0; i < contexts.Count; i++)
            {
                BindingDataContextEntry entry = contexts[i];
                if (entry == null)
                {
                    continue;
                }

                string normalizedName = NormalizeName(entry.Name);
                if (!serializedContextLookup.ContainsKey(normalizedName))
                {
                    serializedContextLookup.Add(normalizedName, entry);
                }
            }

            serializedContextLookupDirty = false;
        }

        private void PublishAllNameChanges()
        {
            EnsureSerializedContextLookup();
            var currentNames = new HashSet<string>(runtimeContexts.Keys, StringComparer.Ordinal);
            foreach (string serializedName in serializedContextLookup.Keys)
            {
                currentNames.Add(serializedName);
            }

            foreach (string previousName in publishedNames)
            {
                IncrementVersion(previousName);
            }

            foreach (string currentName in currentNames)
            {
                if (!publishedNames.Contains(currentName))
                {
                    IncrementVersion(currentName);
                }
            }

            publishedNames.Clear();
            foreach (string currentName in currentNames)
            {
                publishedNames.Add(currentName);
            }
        }

        private void PublishPublishedNameChanges()
        {
            foreach (string publishedName in publishedNames)
            {
                IncrementVersion(publishedName);
            }
        }

        private void PublishNameChange(string name)
        {
            string normalizedName = NormalizeName(name);
            IncrementVersion(normalizedName);

            EnsureSerializedContextLookup();
            if (runtimeContexts.ContainsKey(normalizedName) ||
                serializedContextLookup.ContainsKey(normalizedName))
            {
                publishedNames.Add(normalizedName);
            }
            else
            {
                publishedNames.Remove(normalizedName);
            }
        }

        private static void IncrementVersion(string name)
        {
            lock (ContextVersionsLock)
            {
                ContextVersions.TryGetValue(name, out int version);
                unchecked
                {
                    ContextVersions[name] = version + 1;
                }
            }
        }
    }
}
