using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public interface IBindingFormatter
    {
        string Id { get; }

        string DisplayName { get; }

        bool TryGetOutputType(
            IReadOnlyList<BindingMemberMetadata> sourceMetadata,
            out Type outputType,
            out string error);

        bool TryFormat(
            IReadOnlyList<object> sourceValues,
            BindingFormatterSettings settings,
            out object formattedValue,
            out string error);
    }
}
