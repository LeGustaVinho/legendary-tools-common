using System;
using System.Collections.Generic;

namespace FlatData
{
    public sealed class FlatRow
    {
        public FlatRow(int index, IDictionary<string, object> values)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Index = index;
            Values = new Dictionary<string, object>(
                values ?? throw new ArgumentNullException(nameof(values)));
        }

        public int Index { get; }

        public IReadOnlyDictionary<string, object> Values { get; }
    }
}
