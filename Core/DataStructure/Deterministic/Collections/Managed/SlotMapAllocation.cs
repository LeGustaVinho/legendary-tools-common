namespace LegendaryTools.Deterministic
{
    public sealed partial class DeterministicSlotMap<T> where T : unmanaged
    {
        /// <summary>Logical allocation preflight includes retired slots. Does not reserve local allocator/COW capacity.</summary>
        public bool CanAdd => IsCreated && (State.FreeHead != 0 || State.Used < Storage.Owner.Options.MaxCapacity);
    }
}
