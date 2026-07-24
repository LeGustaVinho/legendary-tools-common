using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingSource
    {
        [SerializeField] private BindingEndpoint endpoint = new BindingEndpoint();

        public BindingEndpoint Endpoint => endpoint;
    }
}
