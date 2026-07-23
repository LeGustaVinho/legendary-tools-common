using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    internal static class BindingResolutionScope
    {
        [ThreadStatic] private static Frame current;

        public static Scope Push(
            Component owner,
            BindingContextResolver resolver,
            ViewDataBindingProfileReference profileReference)
        {
            Frame previous = current;
            current = new Frame(owner, resolver, profileReference);
            return new Scope(previous);
        }

        public static bool TryResolveContext(
            string contextName,
            string declaredTypeName,
            out BindingInstanceHandle handle,
            out string error)
        {
            if (current == null || current.Resolver == null)
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
            if (current != null &&
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

        internal sealed class Frame
        {
            public Frame(
                Component owner,
                BindingContextResolver resolver,
                ViewDataBindingProfileReference profileReference)
            {
                Owner = owner;
                Resolver = resolver;
                ProfileReference = profileReference;
            }

            public Component Owner { get; }

            public BindingContextResolver Resolver { get; }

            public ViewDataBindingProfileReference ProfileReference { get; }
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly Frame previous;

            internal Scope(Frame previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                current = previous;
            }
        }
    }
}
