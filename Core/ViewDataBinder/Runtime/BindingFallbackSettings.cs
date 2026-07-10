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

        public bool Enabled => enabled;

        public bool UseOnReadFailure => useOnReadFailure;

        public bool UseOnFormatterFailure => useOnFormatterFailure;

        public bool UseOnConverterFailure => useOnConverterFailure;

        public BindingFallbackValue Value => value;
    }
}
