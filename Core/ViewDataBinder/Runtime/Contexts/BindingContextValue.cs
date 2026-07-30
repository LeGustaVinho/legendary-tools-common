using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingContextValue
    {
        [SerializeField] private BindingContextValueKind kind;
        [SerializeField] private UnityEngine.Object objectReference;
        [SerializeField] private UnityEngine.Object providerReference;
        [SerializeField] private string staticTypeName;
        [SerializeField] private string declaredTypeName;

        public BindingContextValueKind Kind => kind;

        public UnityEngine.Object ObjectReference => objectReference;

        public UnityEngine.Object ProviderReference => providerReference;

        public string StaticTypeName => staticTypeName;

        public string DeclaredTypeName => declaredTypeName;

        public BindingContextValue ConfigureUnityObject(
            UnityEngine.Object instance,
            Type declaredType = null)
        {
            Reset();
            kind = BindingContextValueKind.UnityObject;
            objectReference = instance;
            declaredTypeName = declaredType?.AssemblyQualifiedName ?? string.Empty;
            return this;
        }

        public BindingContextValue ConfigureProvider(
            UnityEngine.Object provider,
            Type declaredType = null)
        {
            if (provider != null && !(provider is IBindingInstanceProvider))
            {
                throw new ArgumentException(
                    "The Provider must implement IBindingInstanceProvider.",
                    nameof(provider));
            }

            Reset();
            kind = BindingContextValueKind.Provider;
            providerReference = provider;
            declaredTypeName = declaredType?.AssemblyQualifiedName ?? string.Empty;
            return this;
        }

        public BindingContextValue ConfigureStaticType(Type type)
        {
            Reset();
            kind = BindingContextValueKind.StaticType;
            staticTypeName = type?.AssemblyQualifiedName ?? string.Empty;
            return this;
        }

        public bool TryResolve(out BindingInstanceHandle handle, out string error)
        {
            switch (kind)
            {
                case BindingContextValueKind.UnityObject:
                    if (objectReference == null)
                    {
                        handle = default;
                        error = "No Unity Object is assigned to the context.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(objectReference, objectReference.GetType(), false);
                    error = string.Empty;
                    return true;

                case BindingContextValueKind.Provider:
                    return TryResolveProvider(out handle, out error);

                case BindingContextValueKind.StaticType:
                    Type staticType = DefaultBindingInstanceResolver.FindType(staticTypeName);
                    if (staticType == null)
                    {
                        handle = default;
                        error = "The context static type could not be resolved.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(null, staticType, true);
                    error = string.Empty;
                    return true;

                default:
                    handle = default;
                    error = $"Unsupported context value kind: {kind}.";
                    return false;
            }
        }

        public Type GetDeclaredType()
        {
            switch (kind)
            {
                case BindingContextValueKind.UnityObject:
                    return objectReference != null
                        ? objectReference.GetType()
                        : DefaultBindingInstanceResolver.FindType(declaredTypeName);

                case BindingContextValueKind.Provider:
                    if (providerReference is IBindingInstanceProvider provider)
                    {
                        try
                        {
                            return provider.GetBindingInstanceType() ??
                                   provider.GetBindingInstance()?.GetType() ??
                                   DefaultBindingInstanceResolver.FindType(declaredTypeName);
                        }
                        catch
                        {
                            return DefaultBindingInstanceResolver.FindType(declaredTypeName);
                        }
                    }

                    return DefaultBindingInstanceResolver.FindType(declaredTypeName);

                case BindingContextValueKind.StaticType:
                    return DefaultBindingInstanceResolver.FindType(staticTypeName);

                default:
                    return null;
            }
        }

        private bool TryResolveProvider(out BindingInstanceHandle handle, out string error)
        {
            if (!(providerReference is IBindingInstanceProvider provider))
            {
                handle = default;
                error = "The context Provider must implement IBindingInstanceProvider.";
                return false;
            }

            try
            {
                object instance = provider.GetBindingInstance();
                Type declaredType = provider.GetBindingInstanceType() ??
                                    DefaultBindingInstanceResolver.FindType(declaredTypeName);
                Type resolvedType = instance?.GetType() ?? declaredType;
                if (resolvedType == null)
                {
                    handle = default;
                    error = "The context Provider returned neither an instance nor a declared type.";
                    return false;
                }

                if (instance == null)
                {
                    handle = default;
                    error = $"The context Provider returned a null instance for '{resolvedType.FullName}'.";
                    return false;
                }

                handle = new BindingInstanceHandle(instance, resolvedType, false);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                handle = default;
                error = $"Context Provider resolution failed: {exception.Message}";
                return false;
            }
        }

        private void Reset()
        {
            objectReference = null;
            providerReference = null;
            staticTypeName = string.Empty;
            declaredTypeName = string.Empty;
        }
    }
}
