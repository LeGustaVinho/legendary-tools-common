// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/TypeReferenceCandidate.cs
using System;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Represents a normalized type reference candidate discovered via syntax.
    /// Normalization removes type arguments and uses CLR-style arity markers for generics (e.g. List`1).
    /// </summary>
    [Serializable]
    public sealed class TypeReferenceCandidate
    {
        /// <summary>
        /// Gets or sets the normalized name (e.g. "Foo", "My.Namespace.Bar", "List`1", "Outer.Inner`2").
        /// </summary>
        public string NormalizedName;

        /// <summary>
        /// Gets or sets a value indicating whether the candidate appears to be namespace/type qualified.
        /// </summary>
        public bool IsQualified;

        /// <summary>
        /// Gets or sets the 1-based line number where the candidate appears (best-effort).
        /// </summary>
        public int Line;

        /// <summary>
        /// Gets or sets the 1-based column number where the candidate appears (best-effort).
        /// </summary>
        public int Column;
    }
}
