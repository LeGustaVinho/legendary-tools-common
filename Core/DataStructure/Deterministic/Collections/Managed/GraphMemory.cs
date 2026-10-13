namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public GraphMemoryStats MemoryStats => !IsCreated ? default : NodesStorage.Owner.MemoryStats + EdgesStorage.Owner.MemoryStats + ArcsStorage.Owner.MemoryStats + new GraphMemoryStats(workspace.Length, workspace.Length, 0, 0, 0);
    }
    public sealed unsafe partial class PersistentDeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    { public GraphMemoryStats MemoryStats => working.MemoryStats; }
    public sealed unsafe partial class FrozenDeterministicGraph<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public GraphMemoryStats MemoryStats => !IsCreated ? default : NodesStorage.Owner.MemoryStats + EdgesStorage.Owner.MemoryStats + ArcsStorage.Owner.MemoryStats + OutgoingStorage.Owner.MemoryStats + IncomingStorage.Owner.MemoryStats + new GraphMemoryStats(workspace?.Length ?? 0, workspace?.Length ?? 0, 0, 0, 0);
    }
}
