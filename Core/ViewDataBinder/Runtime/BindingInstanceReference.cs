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

        public bool TryResolve(out BindingInstanceHandle handle, out string error)
        {
            return BindingBackendRegistry.InstanceResolver.TryResolve(this, out handle, out error);
        }
    }
}
