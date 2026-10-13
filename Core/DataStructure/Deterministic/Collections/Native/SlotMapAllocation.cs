namespace LegendaryTools.Deterministic
{
    public unsafe partial struct NativeDeterministicSlotMap<T> where T : unmanaged
    {
        public bool CanAdd { get { CheckRead(); return IsCreated && (Storage.Data->SlotState.FreeHead != 0 || Storage.Data->SlotState.Used < Storage.Data->Options.MaxCapacity); } }
    }
}
