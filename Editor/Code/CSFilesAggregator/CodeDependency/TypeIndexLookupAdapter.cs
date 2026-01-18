// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/TypeIndexLookupAdapter.cs
using System.Collections.Generic;
using LegendaryTools.CSFilesAggregator.TypeIndex;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Adapter that exposes <see cref="TypeIndex.TypeIndex"/> through <see cref="ITypeIndexLookup"/>.
    /// </summary>
    internal sealed class TypeIndexLookupAdapter : ITypeIndexLookup
    {
        private readonly TypeIndex.TypeIndex _typeIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="TypeIndexLookupAdapter"/> class.
        /// </summary>
        public TypeIndexLookupAdapter(TypeIndex.TypeIndex typeIndex)
        {
            _typeIndex = typeIndex;
        }

        /// <inheritdoc />
        public bool TryGet(string fullName, out IReadOnlyList<TypeIndexEntry> entries)
        {
            entries = null;

            if (_typeIndex == null)
            {
                return false;
            }

            return _typeIndex.TryGet(fullName, out entries);
        }
    }
}
