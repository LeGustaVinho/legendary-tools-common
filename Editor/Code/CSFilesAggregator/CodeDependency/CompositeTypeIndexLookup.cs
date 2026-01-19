using System.Collections.Generic;
using LegendaryTools.CSFilesAggregator.TypeIndex;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Resolves type names by consulting an in-memory index first, then a persisted type index.
    /// </summary>
    internal sealed class CompositeTypeIndexLookup : ITypeIndexLookup
    {
        private readonly ITypeIndexLookup _primary;
        private readonly ITypeIndexLookup _secondary;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeTypeIndexLookup"/> class.
        /// </summary>
        public CompositeTypeIndexLookup(ITypeIndexLookup primary, ITypeIndexLookup secondary)
        {
            _primary = primary;
            _secondary = secondary;
        }

        /// <inheritdoc />
        public bool TryGet(string fullName, out IReadOnlyList<TypeIndexEntry> entries)
        {
            entries = null;

            if (_primary != null && _primary.TryGet(fullName, out entries)) return true;

            if (_secondary != null && _secondary.TryGet(fullName, out entries)) return true;

            return false;
        }
    }
}