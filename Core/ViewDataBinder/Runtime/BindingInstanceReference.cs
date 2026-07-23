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

        public BindingInstanceKind Kind => kind;

        public UnityEngine.Object ObjectReference => objectReference;

        public string StaticTypeName => staticTypeName;

        public UnityEngine.Object ProviderReference => providerReference;

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
                candidate is Component candidateComponent)
            {
                return referencedGameObject == candidateComponent.gameObject;
            }

            return false;
        }

        public bool TryResolve(out BindingInstanceHandle handle, out string error)
        {
            return BindingBackendRegistry.InstanceResolver.TryResolve(this, out handle, out error);
        }
    }
}
