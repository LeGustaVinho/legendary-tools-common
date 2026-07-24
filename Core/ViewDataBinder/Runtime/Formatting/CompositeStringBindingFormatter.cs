using System;
using System.Collections.Generic;
using System.Globalization;

namespace LegendaryTools.ViewBinding
{
    public sealed class CompositeStringBindingFormatter : IBindingFormatter
    {
        public const string FormatterId = "composite-string";

        public string Id => FormatterId;

        public string DisplayName => "Composite String";

        public bool TryGetOutputType(
            IReadOnlyList<BindingMemberMetadata> sourceMetadata,
            out Type outputType,
            out string error)
        {
            outputType = typeof(string);

            if (sourceMetadata == null || sourceMetadata.Count == 0)
            {
                error = "Composite String requires at least one Source.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryFormat(
            IReadOnlyList<object> sourceValues,
            BindingFormatterSettings settings,
            out object formattedValue,
            out string error)
        {
            formattedValue = null;

            if (sourceValues == null || sourceValues.Count == 0)
            {
                error = "Composite String requires at least one Source value.";
                return false;
            }

            if (settings == null)
            {
                error = "Formatter settings are missing.";
                return false;
            }

            try
            {
                IFormatProvider formatProvider = ResolveCulture(settings.CultureName);
                string format = settings.FormatString ?? string.Empty;

                switch (sourceValues.Count)
                {
                    case 1:
                        formattedValue = string.Format(formatProvider, format, sourceValues[0]);
                        break;

                    case 2:
                        formattedValue = string.Format(
                            formatProvider,
                            format,
                            sourceValues[0],
                            sourceValues[1]);
                        break;

                    case 3:
                        formattedValue = string.Format(
                            formatProvider,
                            format,
                            sourceValues[0],
                            sourceValues[1],
                            sourceValues[2]);
                        break;

                    default:
                        if (sourceValues is object[] values)
                        {
                            formattedValue = string.Format(formatProvider, format, values);
                            break;
                        }

                        object[] copiedValues = new object[sourceValues.Count];
                        for (int i = 0; i < sourceValues.Count; i++)
                        {
                            copiedValues[i] = sourceValues[i];
                        }

                        formattedValue = string.Format(formatProvider, format, copiedValues);
                        break;
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Formatter failed: {exception.Message}";
                return false;
            }
        }

        private static IFormatProvider ResolveCulture(string cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return CultureInfo.CurrentCulture;
            }

            return CultureInfo.GetCultureInfo(cultureName);
        }
    }
}
