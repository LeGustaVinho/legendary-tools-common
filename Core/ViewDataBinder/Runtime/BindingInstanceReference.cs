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

        public BindingInstanceKind Kind => kind;

        public UnityEngine.Object ObjectReference => objectReference;

        public string StaticTypeName => staticTypeName;

        public UnityEngine.Object ProviderReference => providerReference;

        public string ContextName => contextName;

        public string ContextTypeName => contextTypeName;

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

                default:
                    type = null;
                    return false;
            }
        }
    }
}
