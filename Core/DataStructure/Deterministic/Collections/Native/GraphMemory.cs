namespace LegendaryTools.Deterministic
{
    public unsafe partial struct NativeDeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public GraphMemoryStats MemoryStats { get { CheckRead(); return !IsCreated ? default : NodesStorage.MemoryStats + EdgesStorage.MemoryStats + ArcsStorage.MemoryStats + workspace.MemoryStats; } }
    }
    public unsafe partial struct NativePersistentDeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    { public GraphMemoryStats MemoryStats { get { working.CheckRead(); return working.MemoryStats; } } }
    public unsafe partial struct NativeFrozenDeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public GraphMemoryStats MemoryStats { get { CheckRead(); return !IsCreated ? default : NodesStorage.MemoryStats + EdgesStorage.MemoryStats + ArcsStorage.MemoryStats + OutgoingStorage.MemoryStats + IncomingStorage.MemoryStats + workspace.MemoryStats; } }
    }
}
