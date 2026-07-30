using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingEndpoint
    {
        [SerializeField] private BindingInstanceReference instance = new BindingInstanceReference();
        [SerializeField] private string memberPath;

        public BindingEndpoint()
        {
        }

        public BindingEndpoint(BindingInstanceReference instance, string memberPath)
        {
            this.instance = instance ?? new BindingInstanceReference();
            this.memberPath = memberPath;
        }

        public BindingInstanceReference Instance
        {
            get => instance;
            set => instance = value ?? new BindingInstanceReference();
        }

        public string MemberPath
        {
            get => memberPath;
            set => memberPath = value ?? string.Empty;
        }
    }
}
