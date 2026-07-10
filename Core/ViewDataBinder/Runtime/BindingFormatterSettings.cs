using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingFormatterSettings
    {
        [SerializeField] private bool enabled;
        [SerializeField] private string formatterId = CompositeStringBindingFormatter.FormatterId;
        [SerializeField] private string formatString = "{0}";
        [SerializeField] private string cultureName;

        public bool Enabled => enabled;

        public string FormatterId => formatterId;

        public string FormatString => formatString;

        public string CultureName => cultureName;
    }
}
