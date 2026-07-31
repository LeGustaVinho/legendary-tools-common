using System;
using System.Globalization;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "FloatStringBindingConverter",
        menuName = "Legendary Tools/View Data Binder/Converters/Float to String")]
    public sealed class FloatStringBindingConverter : BindingConverter<float, string>
    {
        [SerializeField] private string format = "0.##";
        [SerializeField] private string cultureName;

        public string Format
        {
            get => format;
            set => format = value ?? string.Empty;
        }

        public string CultureName
        {
            get => cultureName;
            set => cultureName = value ?? string.Empty;
        }

        public override bool SupportsReverseConversion => true;

        protected override bool TryConvertValue(float sourceValue, out string targetValue, out string error)
        {
            try
            {
                CultureInfo culture = ResolveCulture();
                targetValue = sourceValue.ToString(format, culture);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                targetValue = null;
                error = exception.Message;
                return false;
            }
        }

        protected override bool TryConvertBackValue(string targetValue, out float sourceValue, out string error)
        {
            try
            {
                CultureInfo culture = ResolveCulture();
                if (float.TryParse(
                        targetValue,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        culture,
                        out sourceValue))
                {
                    error = string.Empty;
                    return true;
                }

                error = $"'{targetValue}' is not a valid float for culture '{culture.Name}'.";
                return false;
            }
            catch (Exception exception)
            {
                sourceValue = default;
                error = exception.Message;
                return false;
            }
        }

        private CultureInfo ResolveCulture()
        {
            return string.IsNullOrWhiteSpace(cultureName)
                ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(cultureName);
        }
    }
}
