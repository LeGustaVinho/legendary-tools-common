using System;
using System.Collections.Generic;

namespace FlatData
{
    public sealed class FlatObject
    {
        public FlatObject(Type rootType, IDictionary<string, object> values)
        {
            RootType = rootType ?? throw new ArgumentNullException(nameof(rootType));
            Values = new Dictionary<string, object>(
                values ?? throw new ArgumentNullException(nameof(values)));
        }

        public Type RootType { get; }

        public IReadOnlyDictionary<string, object> Values { get; }
    }
}
