namespace LegendaryTools.Deterministic
{
    public sealed partial class DeterministicArray<T> where T : unmanaged
    {
        /// <summary>Host-local invalidation token, excluded from canonical state.</summary>
        public ulong Revision => Storage.Revision;
        public ulong HistoryRevision => Storage.HistoryRevision;
    }
    public sealed partial class DeterministicCell<T> where T : unmanaged
    {
        public ulong Revision => storage.Revision;
        public ulong HistoryRevision => storage.HistoryRevision;
    }
    public sealed partial class DeterministicDictionary<TKey, TValue, TPolicy>
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        public ulong Revision => Entries.Revision;
        public ulong HistoryRevision => Entries.HistoryRevision;
    }
    public sealed partial class DeterministicSlotMap<T> where T : unmanaged
    {
        public ulong HistoryRevision => Storage.HistoryRevision;
    }
}
