using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    internal static class BindingResolutionScope
    {
        [ThreadStatic] private static Frame current;
        [ThreadStatic] private static bool hasCurrent;
        [ThreadStatic] private static int scopeToken;
        [ThreadStatic] private static ConditionalWeakTable<BindingInstanceReference, InstanceCacheBox> instanceCache;
        private static readonly ConditionalWeakTable<BindingInstanceReference, InstanceCacheBox>.CreateValueCallback
            InstanceCacheFactory = CreateInstanceCacheBox;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            current = default;
            hasCurrent = false;
            scopeToken = 0;
            instanceCache = null;
        }

        public static Scope Push(
            Component owner,
            BindingContextResolver resolver,
            ViewDataBindingProfileReference profileReference)
        {
            Frame previous = current;
            bool hadPrevious = hasCurrent;
            unchecked
            {
                scopeToken++;
            }

            current = new Frame(owner, resolver, profileReference, scopeToken);
            hasCurrent = true;
            return new Scope(previous, hadPrevious);
        }

        internal static bool TryResolveInstance(
            BindingInstanceReference reference,
            out BindingInstanceHandle handle,
            out string error)
        {
            if (!hasCurrent)
            {
                return BindingBackendRegistry.InstanceResolver.TryResolve(reference, out handle, out error);
            }

            if (instanceCache == null)
            {
                instanceCache = new ConditionalWeakTable<BindingInstanceReference, InstanceCacheBox>();
            }

            InstanceCacheBox cacheBox = instanceCache.GetValue(reference, InstanceCacheFactory);
            if (cacheBox.ScopeToken == current.ScopeToken)
            {
                handle = cacheBox.Handle;
                error = cacheBox.Error;
                return cacheBox.Success;
            }

            bool success = BindingBackendRegistry.InstanceResolver.TryResolve(reference, out handle, out error);
            cacheBox.ScopeToken = current.ScopeToken;
            cacheBox.Success = success;
            cacheBox.Handle = handle;
            cacheBox.Error = error;
            return success;
        }

        private static InstanceCacheBox CreateInstanceCacheBox(BindingInstanceReference _)
        {
            return new InstanceCacheBox();
        }

        public static bool TryResolveContext(
            string contextName,
            string declaredTypeName,
            out BindingInstanceHandle handle,
            out string error)
        {
            if (!hasCurrent || current.Resolver == null)
            {
                handle = default;
                error = $"Context '{BindingDataContext.NormalizeName(contextName)}' requires an active binder resolution scope.";
                return false;
            }

            if (current.Resolver.TryResolve(
                    current.Owner,
                    current.ProfileReference,
                    contextName,
                    out handle,
                    out error))
            {
                return true;
            }

            Type declaredType = DefaultBindingInstanceResolver.FindType(declaredTypeName);
            if (declaredType != null)
            {
                error += $" Declared context type: '{declaredType.FullName}'.";
            }

            return false;
        }

        public static bool TryGetDeclaredContextType(
            string contextName,
            string declaredTypeName,
            out Type type)
        {
            if (hasCurrent &&
                current.Resolver != null &&
                current.Resolver.TryGetDeclaredType(
                    current.Owner,
                    current.ProfileReference,
                    contextName,
                    out type))
            {
                return true;
            }

            type = DefaultBindingInstanceResolver.FindType(declaredTypeName);
            return type != null;
        }

        internal readonly struct Frame
        {
            public Frame(
                Component owner,
                BindingContextResolver resolver,
                ViewDataBindingProfileReference profileReference,
                int scopeToken)
            {
                Owner = owner;
                Resolver = resolver;
                ProfileReference = profileReference;
                ScopeToken = scopeToken;
            }

            public Component Owner { get; }

            public BindingContextResolver Resolver { get; }

            public ViewDataBindingProfileReference ProfileReference { get; }

            public int ScopeToken { get; }
        }

        private sealed class InstanceCacheBox
        {
            public int ScopeToken;

            public bool Success;

            public BindingInstanceHandle Handle;

            public string Error;
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly Frame previous;
            private readonly bool hadPrevious;

            internal Scope(Frame previous, bool hadPrevious)
            {
                this.previous = previous;
                this.hadPrevious = hadPrevious;
            }

            public void Dispose()
            {
                current = previous;
                hasCurrent = hadPrevious;
            }
        }
    }
}
