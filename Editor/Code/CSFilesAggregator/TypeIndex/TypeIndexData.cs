using System;
using System.Collections.Generic;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Serializable payload persisted to JSON under the Library folder.
    /// </summary>
    [Serializable]
    public sealed class TypeIndexData
    {
        /// <summary>
        /// Gets or sets the schema version for the persisted payload.
        /// </summary>
        public int Version = 1;

        /// <summary>
        /// Gets or sets the UTC timestamp (ISO 8601) when the index was generated.
        /// </summary>
        public string GeneratedAtUtcIso;

        /// <summary>
        /// Gets or sets the entries for all declared types found in scanned roots.
        /// </summary>
        public List<TypeIndexEntry> Entries = new();
    }
}