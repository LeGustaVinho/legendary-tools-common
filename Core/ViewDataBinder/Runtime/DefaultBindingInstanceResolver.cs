using System;

namespace LegendaryTools.ViewBinding
{
    public sealed class DefaultBindingInstanceResolver : IBindingInstanceResolver
    {
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

            Type type = Type.GetType(assemblyQualifiedName, false);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(assemblyQualifiedName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
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
    }
}
