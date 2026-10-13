namespace LegendaryTools.Deterministic
{
    public unsafe partial struct NativeDeterministicArray<T> where T : unmanaged
    {
        public ulong Revision { get { CheckRead(); return Storage.Revision; } }
        public ulong HistoryRevision { get { CheckRead(); return Storage.HistoryRevision; } }
    }
    public unsafe partial struct NativeDeterministicCell<T> where T : unmanaged
    {
        public ulong Revision { get { CheckRead(); return Storage.Revision; } }
        public ulong HistoryRevision { get { CheckRead(); return Storage.HistoryRevision; } }
    }
    public unsafe partial struct NativeDeterministicSlotMap<T> where T : unmanaged
    {
        public ulong Revision { get { CheckRead(); return Storage.Revision; } }
        public ulong HistoryRevision { get { CheckRead(); return Storage.HistoryRevision; } }
    }
    public unsafe partial struct NativeDeterministicDictionary<TKey, TValue, TPolicy>
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        public ulong Revision { get { CheckRead(); return Entries.Revision; } }
        public ulong HistoryRevision { get { CheckRead(); return Entries.HistoryRevision; } }
    }
}
