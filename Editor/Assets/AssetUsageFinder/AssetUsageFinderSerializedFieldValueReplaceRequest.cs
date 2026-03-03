using System;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderSerializedFieldValueReplaceRequest
    {
        public IReadOnlyList<SerializedFieldFilterRow> Filters { get; }
        public SerializedFieldValueBox ReplaceWithValue { get; }
        public Type ReplaceValueType { get; }
        public AssetUsageFinderSearchScope SearchScope { get; }

        public AssetUsageFinderSerializedFieldValueReplaceRequest(
            IReadOnlyList<SerializedFieldFilterRow> filters,
            SerializedFieldValueBox replaceWithValue,
            Type replaceValueType,
            AssetUsageFinderSearchScope searchScope)
        {
            Filters = filters;
            ReplaceWithValue = replaceWithValue;
            ReplaceValueType = replaceValueType;
            SearchScope = searchScope;
        }
    }
}