using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatData
{
    public sealed class FlatTable
    {
        public FlatTable(
            Type itemType,
            IEnumerable<string> columns,
            IEnumerable<FlatRow> rows)
        {
            ItemType = itemType ?? throw new ArgumentNullException(nameof(itemType));
            Columns = (columns ?? throw new ArgumentNullException(nameof(columns))).ToArray();
            Rows = (rows ?? throw new ArgumentNullException(nameof(rows))).ToArray();
        }

        public Type ItemType { get; }

        public IReadOnlyList<string> Columns { get; }

        public IReadOnlyList<FlatRow> Rows { get; }
    }
}
