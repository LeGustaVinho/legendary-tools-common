using System.Collections.Generic;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// In-memory view of a persisted type index.
    /// </summary>
    public sealed class TypeIndex
    {
        private readonly Dictionary<string, List<TypeIndexEntry>> _byFullName;

        /// <summary>
        /// Initializes a new instance of the <see cref="TypeIndex"/> class.
        /// </summary>
        /// <param name="data">The persisted data object.</param>
        public TypeIndex(TypeIndexData data)
        {
            Data = data;
            _byFullName = new Dictionary<string, List<TypeIndexEntry>>(1024);

            if (data?.Entries == null) return;

            for (int i = 0; i < data.Entries.Count; i++)
            {
                TypeIndexEntry entry = data.Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.FullName)) continue;

                if (!_byFullName.TryGetValue(entry.FullName, out List<TypeIndexEntry> list))
                {
                    list = new List<TypeIndexEntry>(1);
                    _byFullName.Add(entry.FullName, list);
                }

                list.Add(entry);
            }
        }

        /// <summary>
        /// Gets the raw data payload loaded from disk.
        /// </summary>
        public TypeIndexData Data { get; }

        /// <summary>
        /// Tries to get all declarations for the given fully qualified type name.
        /// Multiple results can exist for partial types.
        /// </summary>
        /// <param name="fullName">The fully qualified type name.</param>
        /// <param name="entries">The matching entries.</param>
        /// <returns><see langword="true"/> if at least one entry was found; otherwise <see langword="false"/>.</returns>
        public bool TryGet(string fullName, out IReadOnlyList<TypeIndexEntry> entries)
        {
            entries = null;

            if (string.IsNullOrEmpty(fullName)) return false;

            if (_byFullName.TryGetValue(fullName, out List<TypeIndexEntry> list) && list != null && list.Count > 0)
            {
                entries = list;
                return true;
            }

            return false;
        }
    }
}