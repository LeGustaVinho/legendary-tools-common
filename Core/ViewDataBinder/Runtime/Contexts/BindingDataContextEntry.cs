using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingDataContextEntry
    {
        [SerializeField] private string name = BindingContextConstants.Default;
        [SerializeField] private BindingContextValue value = new BindingContextValue();

        public string Name
        {
            get => name;
            set => name = BindingDataContext.NormalizeName(value);
        }

        public BindingContextValue Value
        {
            get => value;
            set => this.value = value ?? new BindingContextValue();
        }
    }
}
