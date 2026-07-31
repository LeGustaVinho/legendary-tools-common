using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "StringBooleanBindingConverter",
        menuName = "Legendary Tools/View Data Binder/Converters/String to Boolean")]
    public sealed class StringBooleanBindingConverter : BindingConverter<string, bool>
    {
        [SerializeField] private string trueValue = "true";
        [SerializeField] private string falseValue = "false";
        [SerializeField] private bool ignoreCase = true;
        [SerializeField] private bool trimInput = true;

        public string TrueValue
        {
            get => trueValue;
            set => trueValue = value ?? string.Empty;
        }

        public string FalseValue
        {
            get => falseValue;
            set => falseValue = value ?? string.Empty;
        }

        public bool IgnoreCase
        {
            get => ignoreCase;
            set => ignoreCase = value;
        }

        public bool TrimInput
        {
            get => trimInput;
            set => trimInput = value;
        }

        public override bool SupportsReverseConversion => true;

        protected override bool TryConvertValue(string sourceValue, out bool targetValue, out string error)
        {
            string candidate = trimInput ? sourceValue?.Trim() : sourceValue;
            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(trueValue, falseValue, comparison))
            {
                targetValue = false;
                error = "The configured true and false values must be different.";
                return false;
            }

            if (string.Equals(candidate, trueValue, comparison))
            {
                targetValue = true;
                error = string.Empty;
                return true;
            }

            if (string.Equals(candidate, falseValue, comparison))
            {
                targetValue = false;
                error = string.Empty;
                return true;
            }

            targetValue = false;
            error = $"'{sourceValue}' does not match the configured true or false value.";
            return false;
        }

        protected override bool TryConvertBackValue(bool targetValue, out string sourceValue, out string error)
        {
            sourceValue = targetValue ? trueValue : falseValue;
            error = string.Empty;
            return true;
        }
    }
}
