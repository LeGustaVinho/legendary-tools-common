using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ViewBinding
{
    public static class BindingFormatterRegistry
    {
        private static readonly Dictionary<string, IBindingFormatter> FormattersById =
            new Dictionary<string, IBindingFormatter>(StringComparer.Ordinal);

        static BindingFormatterRegistry()
        {
            Register(new CompositeStringBindingFormatter());
        }

        public static IReadOnlyList<IBindingFormatter> Formatters =>
            FormattersById.Values.OrderBy(formatter => formatter.DisplayName, StringComparer.Ordinal).ToArray();

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
            Register(new CompositeStringBindingFormatter());
        }
    }
}
