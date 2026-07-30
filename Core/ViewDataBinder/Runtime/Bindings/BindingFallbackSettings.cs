using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingFallbackSettings
    {
        [SerializeField] private bool enabled;
        [SerializeField] private bool useOnReadFailure;
        [SerializeField] private bool useOnFormatterFailure;
        [SerializeField] private bool useOnConverterFailure;
        [SerializeField] private BindingFallbackValue value = new BindingFallbackValue();

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public bool UseOnReadFailure
        {
            get => useOnReadFailure;
            set => useOnReadFailure = value;
        }

        public bool UseOnFormatterFailure
        {
            get => useOnFormatterFailure;
            set => useOnFormatterFailure = value;
        }

        public bool UseOnConverterFailure
        {
            get => useOnConverterFailure;
            set => useOnConverterFailure = value;
        }

        public BindingFallbackValue Value => value;
    }
}
