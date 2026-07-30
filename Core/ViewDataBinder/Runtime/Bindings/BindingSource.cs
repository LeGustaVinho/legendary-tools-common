using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingSource
    {
        [SerializeField] private BindingEndpoint endpoint = new BindingEndpoint();

        public BindingSource()
        {
        }

        public BindingSource(BindingEndpoint endpoint)
        {
            this.endpoint = endpoint ?? new BindingEndpoint();
        }

        public BindingEndpoint Endpoint
        {
            get => endpoint;
            set => endpoint = value ?? new BindingEndpoint();
        }
    }
}
