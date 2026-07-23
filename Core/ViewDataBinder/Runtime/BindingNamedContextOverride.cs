using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingNamedContextOverride
    {
        [SerializeField] private string name = BindingContextConstants.Default;
        [SerializeField] private BindingContextValue value = new BindingContextValue();

        public string Name => name;

        public BindingContextValue Value => value;
    }
}
