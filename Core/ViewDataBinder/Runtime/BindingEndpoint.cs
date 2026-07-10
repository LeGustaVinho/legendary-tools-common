using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingEndpoint
    {
        [SerializeField] private BindingInstanceReference instance = new BindingInstanceReference();
        [SerializeField] private string memberPath;

        public BindingInstanceReference Instance => instance;

        public string MemberPath => memberPath;
    }
}
