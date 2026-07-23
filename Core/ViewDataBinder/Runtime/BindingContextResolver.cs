using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    internal sealed class BindingContextResolver
    {
        private readonly Dictionary<string, HierarchicalContextCacheEntry> hierarchyCache =
            new Dictionary<string, HierarchicalContextCacheEntry>(StringComparer.Ordinal);

        public bool TryResolve(
            Component owner,
            ViewDataBindingProfileReference profileReference,
            string contextName,
            out BindingInstanceHandle handle,
            out string error)
        {
            handle = default;
            error = null;

            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (profileReference != null &&
                profileReference.TryResolveContext(normalizedName, out handle, out error))
            {
                return true;
            }

            if (profileReference != null && profileReference.DefinesContext(normalizedName))
            {
                handle = default;
                return false;
            }

            if (owner == null)
            {
                handle = default;
                error = $"Context '{normalizedName}' cannot be resolved without a component owner.";
                return false;
            }

            int contextVersion = BindingDataContext.GetVersion(normalizedName);
            if (hierarchyCache.TryGetValue(normalizedName, out HierarchicalContextCacheEntry cached) &&
                cached.Version == contextVersion &&
                cached.Context != null &&
                cached.Context.isActiveAndEnabled &&
                cached.Context.TryResolveContext(normalizedName, out handle, out error))
            {
                return true;
            }

            Transform current = owner.transform;
            string lastError = null;
            while (current != null)
            {
                if (current.TryGetComponent(out BindingDataContext dataContext) &&
                    dataContext.isActiveAndEnabled)
                {
                    if (dataContext.TryResolveContext(normalizedName, out handle, out error))
                    {
                        hierarchyCache[normalizedName] =
                            new HierarchicalContextCacheEntry(dataContext, contextVersion);
                        return true;
                    }

                    lastError = error;
                }

                current = current.parent;
            }

            hierarchyCache.Remove(normalizedName);
            handle = default;
            error = lastError ?? $"No active hierarchical context named '{normalizedName}' was found.";
            return false;
        }

        public bool TryGetDeclaredType(
            Component owner,
            ViewDataBindingProfileReference profileReference,
            string contextName,
            out Type type)
        {
            string normalizedName = BindingDataContext.NormalizeName(contextName);
            if (profileReference != null &&
                profileReference.TryGetDeclaredContextType(normalizedName, out type))
            {
                return true;
            }

            if (profileReference != null && profileReference.DefinesContext(normalizedName))
            {
                type = null;
                return false;
            }

            Transform current = owner != null ? owner.transform : null;
            while (current != null)
            {
                if (current.TryGetComponent(out BindingDataContext dataContext) &&
                    dataContext.TryGetDeclaredType(normalizedName, out type))
                {
                    return true;
                }

                current = current.parent;
            }

            type = null;
            return false;
        }

        public void Invalidate()
        {
            hierarchyCache.Clear();
        }

        private readonly struct HierarchicalContextCacheEntry
        {
            public HierarchicalContextCacheEntry(
                BindingDataContext context,
                int version)
            {
                Context = context;
                Version = version;
            }

            public BindingDataContext Context { get; }

            public int Version { get; }
        }
    }
}
