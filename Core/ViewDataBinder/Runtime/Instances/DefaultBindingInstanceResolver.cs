using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public sealed class DefaultBindingInstanceResolver : IBindingInstanceResolver
    {
        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly object TypeCacheLock = new object();

        public bool TryResolve(BindingInstanceReference reference, out BindingInstanceHandle handle, out string error)
        {
            handle = default;
            error = string.Empty;

            if (reference == null)
            {
                error = "The instance reference is null.";
                return false;
            }

            switch (reference.Kind)
            {
                case BindingInstanceKind.UnityObject:
                    return TryResolveUnityObject(reference, out handle, out error);

                case BindingInstanceKind.StaticType:
                    return TryResolveStaticType(reference, out handle, out error);

                case BindingInstanceKind.Provider:
                    return TryResolveProvider(reference, out handle, out error);

                case BindingInstanceKind.Context:
                    return BindingResolutionScope.TryResolveContext(
                        reference.ContextName,
                        reference.ContextTypeName,
                        out handle,
                        out error);

                case BindingInstanceKind.Runtime:
                    return TryResolveRuntime(reference, out handle, out error);

                default:
                    error = $"Unsupported instance kind: {reference.Kind}.";
                    return false;
            }
        }

        public static Type FindType(string assemblyQualifiedName)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
            {
                return null;
            }

            lock (TypeCacheLock)
            {
                if (TypeCache.TryGetValue(assemblyQualifiedName, out Type cachedType))
                {
                    return cachedType;
                }
            }

            Type type = Type.GetType(assemblyQualifiedName, false);
            if (type == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    type = assemblies[i].GetType(assemblyQualifiedName, false);
                    if (type != null)
                    {
                        break;
                    }
                }
            }

            lock (TypeCacheLock)
            {
                TypeCache[assemblyQualifiedName] = type;
            }

            return type;
        }

        public static void ClearTypeCache()
        {
            lock (TypeCacheLock)
            {
                TypeCache.Clear();
            }
        }

        private static bool TryResolveUnityObject(
            BindingInstanceReference reference,
            out BindingInstanceHandle handle,
            out string error)
        {
            if (reference.ObjectReference == null)
            {
                handle = default;
                error = "No Unity Object is assigned.";
                return false;
            }

            handle = new BindingInstanceHandle(
                reference.ObjectReference,
                reference.ObjectReference.GetType(),
                false);
            error = string.Empty;
            return true;
        }

        private static bool TryResolveStaticType(
            BindingInstanceReference reference,
            out BindingInstanceHandle handle,
            out string error)
        {
            Type type = FindType(reference.StaticTypeName);
            if (type == null)
            {
                handle = default;
                error = "The static type could not be resolved.";
                return false;
            }

            handle = new BindingInstanceHandle(null, type, true);
            error = string.Empty;
            return true;
        }

        private static bool TryResolveProvider(
            BindingInstanceReference reference,
            out BindingInstanceHandle handle,
            out string error)
        {
            if (!(reference.ProviderReference is IBindingInstanceProvider provider))
            {
                handle = default;
                error = "The assigned Provider must implement IBindingInstanceProvider.";
                return false;
            }

            try
            {
                object instance = provider.GetBindingInstance();
                Type declaredType = provider.GetBindingInstanceType();
                Type resolvedType = instance?.GetType() ?? declaredType;

                if (resolvedType == null)
                {
                    handle = default;
                    error = "The Provider returned neither an instance nor a declared type.";
                    return false;
                }

                if (instance == null)
                {
                    handle = default;
                    error = $"The Provider returned a null instance for type {resolvedType.FullName}.";
                    return false;
                }

                handle = new BindingInstanceHandle(instance, resolvedType, false);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                handle = default;
                error = $"Provider resolution failed: {exception.Message}";
                return false;
            }
        }

        private static bool TryResolveRuntime(
            BindingInstanceReference reference,
            out BindingInstanceHandle handle,
            out string error)
        {
            object instance = reference.RuntimeInstance;
            Type declaredType = FindType(reference.RuntimeTypeName);
            if (instance == null ||
                (instance is UnityEngine.Object unityObject && unityObject == null))
            {
                handle = default;
                error = declaredType == null
                    ? "No Runtime instance is assigned and no declared type is configured."
                    : $"No Runtime instance is assigned for type {declaredType.FullName}.";
                return false;
            }

            if (declaredType != null && !declaredType.IsInstanceOfType(instance))
            {
                handle = default;
                error =
                    $"The Runtime instance type {instance.GetType().FullName} is not assignable to the declared type {declaredType.FullName}.";
                return false;
            }

            handle = new BindingInstanceHandle(instance, instance.GetType(), false);
            error = string.Empty;
            return true;
        }
    }
}
