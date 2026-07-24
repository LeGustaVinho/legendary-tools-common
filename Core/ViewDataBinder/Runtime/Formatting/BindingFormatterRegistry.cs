using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public static class BindingFormatterRegistry
    {
        private static readonly Dictionary<string, IBindingFormatter> FormattersById =
            new Dictionary<string, IBindingFormatter>(StringComparer.Ordinal);
        private static IBindingFormatter[] sortedFormatters = Array.Empty<IBindingFormatter>();
        private static bool sortedFormattersDirty = true;

        static BindingFormatterRegistry()
        {
            Register(new CompositeStringBindingFormatter());
        }

        public static IReadOnlyList<IBindingFormatter> Formatters
        {
            get
            {
                if (sortedFormattersDirty)
                {
                    sortedFormatters = new IBindingFormatter[FormattersById.Count];
                    FormattersById.Values.CopyTo(sortedFormatters, 0);
                    Array.Sort(
                        sortedFormatters,
                        (left, right) => string.Compare(
                            left?.DisplayName,
                            right?.DisplayName,
                            StringComparison.Ordinal));
                    sortedFormattersDirty = false;
                }

                return sortedFormatters;
            }
        }

        public static void Register(IBindingFormatter formatter)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            if (string.IsNullOrWhiteSpace(formatter.Id))
            {
                throw new ArgumentException("Formatter Id cannot be null or whitespace.", nameof(formatter));
            }

            FormattersById[formatter.Id] = formatter;
            sortedFormattersDirty = true;
        }

        public static bool TryGet(string id, out IBindingFormatter formatter)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                formatter = null;
                return false;
            }

            return FormattersById.TryGetValue(id, out formatter);
        }

        public static void ResetDefaults()
        {
            FormattersById.Clear();
            sortedFormatters = Array.Empty<IBindingFormatter>();
            sortedFormattersDirty = true;
            Register(new CompositeStringBindingFormatter());
        }
    }
}
