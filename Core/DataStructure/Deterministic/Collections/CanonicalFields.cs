namespace LegendaryTools.Deterministic
{
    public static class CanonicalFields
    {
        public static DeterministicStatus WriteInt(ref DeterministicWriter writer, int value)
            => value < 0 ? DeterministicStatus.InvalidConfiguration : writer.TryVarUInt((uint)value);
        public static DeterministicStatus ReadInt(ref DeterministicReader reader, out int value)
        {
            value = 0; var trial = reader; var status = trial.TryVarUInt(out ulong raw); if (!status.IsSuccess()) return status;
            if (raw > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            value = (int)raw; reader = trial; return DeterministicStatus.None;
        }
        public static uint CombineSchema(uint kind, uint first, ushort firstVersion, uint second = 0, ushort secondVersion = 0)
        {
            if (first == 0 || firstVersion == 0 || second != 0 && secondVersion == 0) return 0;
            ulong a = ((ulong)kind << 32) | first;
            ulong hash = DeterministicHash.Mix(a ^ ((ulong)firstVersion << 17));
            uint result = (uint)DeterministicHash.Mix(hash ^ ((ulong)second << 16) ^ secondVersion);
            return result == 0 ? uint.MaxValue : result;
        }
    }
    public struct LinkedListWireState { public SlotMapState Slots; public LinkedListState Links; }
    public struct SparseSetWireState { public int Count, EntitySlotCapacity; }
    public struct BitSetWireState { public int Length; }
}
