using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingInstanceReference
    {
        [SerializeField] private BindingInstanceKind kind;
        [SerializeField] private UnityEngine.Object objectReference;
        [SerializeField] private string staticTypeName;
        [SerializeField] private UnityEngine.Object providerReference;
        [SerializeField] private string contextName = BindingContextConstants.Default;
        [SerializeField] private string contextTypeName;
        [SerializeField] private string runtimeTypeName;

        [NonSerialized] private object runtimeInstance;

        public BindingInstanceKind Kind => kind;

        public UnityEngine.Object ObjectReference => objectReference;

        public string StaticTypeName => staticTypeName;

        public UnityEngine.Object ProviderReference => providerReference;

        public string ContextName => contextName;

        public string ContextTypeName => contextTypeName;

        public string RuntimeTypeName => runtimeTypeName;

        public object RuntimeInstance => runtimeInstance;

        public BindingInstanceReference ConfigureUnityObject(UnityEngine.Object instance)
        {
            ResetConfiguration();
            kind = BindingInstanceKind.UnityObject;
            objectReference = instance;
            return this;
        }

        public BindingInstanceReference ConfigureStaticType(Type type)
        {
            ResetConfiguration();
            kind = BindingInstanceKind.StaticType;
            staticTypeName = type?.AssemblyQualifiedName ?? string.Empty;
            return this;
        }

        public BindingInstanceReference ConfigureProvider(UnityEngine.Object provider)
        {
            if (provider != null && !(provider is IBindingInstanceProvider))
            {
                throw new ArgumentException(
                    "The Provider must implement IBindingInstanceProvider.",
                    nameof(provider));
            }

            ResetConfiguration();
            kind = BindingInstanceKind.Provider;
            providerReference = provider;
            return this;
        }

        public BindingInstanceReference ConfigureContext(string name, Type declaredType = null)
        {
            ResetConfiguration();
            kind = BindingInstanceKind.Context;
            contextName = BindingDataContext.NormalizeName(name);
            contextTypeName = declaredType?.AssemblyQualifiedName ?? string.Empty;
            return this;
        }

        public BindingInstanceReference ConfigureRuntime(Type declaredType = null, object instance = null)
        {
            ResetConfiguration();
            kind = BindingInstanceKind.Runtime;
            runtimeTypeName = declaredType?.AssemblyQualifiedName ?? string.Empty;
            if (!SetRuntimeInstance(instance))
            {
                throw new ArgumentException(
                    $"The Runtime instance is not assignable to '{declaredType?.FullName}'.",
                    nameof(instance));
            }

            return this;
        }

        public bool SetRuntimeInstance(object instance)
        {
            if (kind != BindingInstanceKind.Runtime)
            {
                return false;
            }

            Type declaredType = DefaultBindingInstanceResolver.FindType(runtimeTypeName);
            if (instance != null &&
                declaredType != null &&
                !declaredType.IsInstanceOfType(instance))
            {
                return false;
            }

            runtimeInstance = instance;
            return true;
        }

        public void ClearRuntimeInstance()
        {
            runtimeInstance = null;
        }

        private void ResetConfiguration()
        {
            objectReference = null;
            staticTypeName = string.Empty;
            providerReference = null;
            contextName = BindingContextConstants.Default;
            contextTypeName = string.Empty;
            runtimeTypeName = string.Empty;
            runtimeInstance = null;
        }

        internal bool ReferencesObject(UnityEngine.Object candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (kind == BindingInstanceKind.Provider)
            {
                return providerReference == candidate;
            }

            if (kind == BindingInstanceKind.Context &&
                TryResolve(out BindingInstanceHandle contextHandle, out _))
            {
                if (ReferenceEquals(contextHandle.Instance, candidate))
                {
                    return true;
                }

                if (contextHandle.Instance is Component contextComponent &&
                    candidate is GameObject contextGameObject)
                {
                    return contextComponent.gameObject == contextGameObject;
                }

                if (contextHandle.Instance is GameObject resolvedGameObject &&
                    candidate is Component candidateComponent)
                {
                    return resolvedGameObject == candidateComponent.gameObject;
                }
            }

            if (kind == BindingInstanceKind.Runtime)
            {
                if (ReferenceEquals(runtimeInstance, candidate))
                {
                    return true;
                }

                if (runtimeInstance is Component runtimeComponent &&
                    candidate is GameObject runtimeGameObject)
                {
                    return runtimeComponent.gameObject == runtimeGameObject;
                }

                if (runtimeInstance is GameObject resolvedRuntimeGameObject &&
                    candidate is Component runtimeCandidateComponent)
                {
                    return resolvedRuntimeGameObject == runtimeCandidateComponent.gameObject;
                }

                return false;
            }

            if (kind != BindingInstanceKind.UnityObject || objectReference == null)
            {
                return false;
            }

            if (objectReference == candidate)
            {
                return true;
            }

            if (objectReference is Component referencedComponent &&
                candidate is GameObject candidateGameObject)
            {
                return referencedComponent.gameObject == candidateGameObject;
            }

            if (objectReference is GameObject referencedGameObject &&
                candidate is Component referencedCandidateComponent)
            {
                return referencedGameObject == referencedCandidateComponent.gameObject;
            }

            return false;
        }

        public bool TryResolve(out BindingInstanceHandle handle, out string error)
        {
            return BindingResolutionScope.TryResolveInstance(this, out handle, out error);
        }

        public bool TryGetDeclaredType(out Type type)
        {
            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    type = objectReference != null ? objectReference.GetType() : null;
                    return type != null;

                case BindingInstanceKind.StaticType:
                    type = DefaultBindingInstanceResolver.FindType(staticTypeName);
                    return type != null;

                case BindingInstanceKind.Provider:
                    if (providerReference is IBindingInstanceProvider provider)
                    {
                        try
                        {
                            type = provider.GetBindingInstanceType() ?? provider.GetBindingInstance()?.GetType();
                            return type != null;
                        }
                        catch
                        {
                            type = null;
                            return false;
                        }
                    }

                    type = null;
                    return false;

                case BindingInstanceKind.Context:
                    return BindingResolutionScope.TryGetDeclaredContextType(
                        contextName,
                        contextTypeName,
                        out type);

                case BindingInstanceKind.Runtime:
                    type = DefaultBindingInstanceResolver.FindType(runtimeTypeName) ??
                           runtimeInstance?.GetType();
                    return type != null;

                default:
                    type = null;
                    return false;
            }
        }
    }
}
