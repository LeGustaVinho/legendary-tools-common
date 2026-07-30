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

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public string FormatterId
        {
            get => formatterId;
            set => formatterId = value ?? string.Empty;
        }

        public string FormatString
        {
            get => formatString;
            set => formatString = value ?? string.Empty;
        }

        public string CultureName
        {
            get => cultureName;
            set => cultureName = value ?? string.Empty;
        }
    }
}
