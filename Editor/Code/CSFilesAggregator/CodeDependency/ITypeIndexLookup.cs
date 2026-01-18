// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/ITypeIndexLookup.cs
using System.Collections.Generic;
using LegendaryTools.CSFilesAggregator.TypeIndex;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Abstraction for resolving fully qualified type names to one or more type index entries.
    /// </summary>
    internal interface ITypeIndexLookup
    {
        /// <summary>
        /// Tries to resolve a fully qualified type name to one or more entries.
        /// </summary>
        /// <param name="fullName">Fully qualified type name (namespace + containing types + name + arity).</param>
        /// <param name="entries">Resolved entries.</param>
        /// <returns>True if at least one entry was found; otherwise false.</returns>
        bool TryGet(string fullName, out IReadOnlyList<TypeIndexEntry> entries);
    }
}
