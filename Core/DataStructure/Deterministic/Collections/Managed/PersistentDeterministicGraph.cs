using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>A lease over an immutable root. IDs are local resources and are not simulation state.</summary>
    public readonly struct ManagedGraphVersion<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        internal readonly PersistentDeterministicGraph<TNode, TEdge> Owner;
        public readonly GraphVersionId Id;
        internal ManagedGraphVersion(PersistentDeterministicGraph<TNode, TEdge> owner, GraphVersionId id) { Owner = owner; Id = id; }
        public bool IsCreated => Owner != null && Owner.Valid(Id);
        public ManagedGraphReadOnly<TNode, TEdge> AsReadOnly() => Owner != null && Owner.TrySnapshot(Id, out var root).IsSuccess() ? root.AsReadOnly() : default;
        public void Dispose() { if (Owner != null) Owner.Release(Id); }
        public DeterministicStatus TryWriteCanonical<TNodeCodec, TEdgeCodec>(ref DeterministicWriter writer, TNodeCodec nc, TEdgeCodec ec)
            where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
        {
            if (Owner == null) return DeterministicStatus.Disposed;
            var s = Owner.TrySnapshot(Id, out var root); return s.IsSuccess() ? root.TryWriteCanonical(ref writer, nc, ec) : s;
        }
    }
    /// <summary>Exclusive COW workspace plus published immutable roots. Construction/expansion belong to the host.</summary>
    public sealed unsafe partial class PersistentDeterministicGraph<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        private struct Root { internal ManagedGraphSnapshot<TNode, TEdge> Snapshot; internal ulong Lease; }
        private readonly DeterministicGraph<TNode, TEdge> working;
        private Root[] roots;
        private ManagedGraphSnapshot<TNode, TEdge> current;
        private ulong nextLease;
        private int count;
        public GraphPersistenceOptions Persistence { get; }
        public GraphOptions Options => working.Options;
        public bool IsCreated => working.IsCreated;
        public int VersionCapacity => roots.Length;
        public int VersionCount => count;
        public ManagedGraphReadOnly<TNode, TEdge> Current => current.AsReadOnly();
        public PersistentDeterministicGraph(GraphOptions options, GraphPersistenceOptions persistence)
        {
            if (!options.IsValid || !persistence.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); Persistence = persistence;
            roots = new Root[persistence.InitialVersionCapacity];
            working = new DeterministicGraph<TNode, TEdge>(options.WithSnapshots(SnapshotPolicy.CopyOnWrite, persistence.InitialVersionCapacity + 3));
            working.TryCapture(out current).ThrowIfFailure();
        }
        internal bool Valid(GraphVersionId id) => IsCreated && id.IsValid && (uint)id.Slot < (uint)roots.Length && roots[id.Slot].Lease == id.Lease;
        internal DeterministicStatus TrySnapshot(GraphVersionId id, out ManagedGraphSnapshot<TNode, TEdge> snapshot)
        { snapshot = default; if (!Valid(id)) return DeterministicStatus.Disposed; snapshot = roots[id.Slot].Snapshot; return snapshot.IsCreated ? DeterministicStatus.None : DeterministicStatus.Disposed; }
        internal void Release(GraphVersionId id)
        {
            if (!Valid(id) || Persistence.Retention == GraphVersionRetention.UntilOwnerDisposed) return;
            roots[id.Slot].Snapshot.Dispose(); roots[id.Slot] = default; count--;
        }
        public DeterministicStatus TryGetVersion(GraphVersionId id, out ManagedGraphVersion<TNode, TEdge> version)
        { version = default; if (!Valid(id)) return DeterministicStatus.NotFound; version = new ManagedGraphVersion<TNode, TEdge>(this, id); return DeterministicStatus.None; }
        private DeterministicStatus FreeSlot(out int slot)
        {
            slot = -1; if (!IsCreated) return DeterministicStatus.Disposed;
            if (nextLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            for (int i = 0; i < roots.Length; i++) if (roots[i].Lease == 0) { slot = i; return DeterministicStatus.None; }
            return DeterministicStatus.SnapshotCapacityExceeded;
        }
        private ManagedGraphVersion<TNode, TEdge> Publish(int slot, in ManagedGraphSnapshot<TNode, TEdge> root)
        { var id = new GraphVersionId(slot, ++nextLease); roots[slot] = new Root { Snapshot = root, Lease = id.Lease }; count++; return new ManagedGraphVersion<TNode, TEdge>(this, id); }
        public DeterministicStatus TryCaptureCurrent(out ManagedGraphVersion<TNode, TEdge> version)
        {
            version = default; var s = FreeSlot(out int slot); if (!s.IsSuccess()) return s;
            s = working.TryRestore(current); if (!s.IsSuccess()) return s; s = working.TryCapture(out var root);
            if (s.IsSuccess()) version = Publish(slot, root); return s;
        }
        public DeterministicStatus TrySelectCurrent(in ManagedGraphVersion<TNode, TEdge> version)
        {
            if (!ReferenceEquals(version.Owner, this)) return DeterministicStatus.InvalidOwner;
            var s = TrySnapshot(version.Id, out var root); if (!s.IsSuccess()) return s;
            s = working.TryRestore(root); if (!s.IsSuccess()) return s; s = working.TryCapture(out var retained);
            if (!s.IsSuccess()) { working.TryRestore(current); return s; }
            var previous = current; current = retained; previous.Dispose(); return DeterministicStatus.None;
        }
        public DeterministicStatus TryWith(in ManagedGraphVersion<TNode, TEdge> basis, in GraphMutation<TNode, TEdge> mutation,
            out ManagedGraphVersion<TNode, TEdge> version, out GraphMutationResult result)
        {
            version = default; result = default;
            if (!ReferenceEquals(basis.Owner, this)) return DeterministicStatus.InvalidOwner;
            var s = TrySnapshot(basis.Id, out var source); if (!s.IsSuccess()) return s;
            s = FreeSlot(out int slot); if (!s.IsSuccess()) return s;
            s = working.TryRestore(source); if (!s.IsSuccess()) return s;
            s = working.TryApply(mutation, out var changed);
            if (!s.IsSuccess()) { working.TryRestore(current); return s; }
            s = working.TryCapture(out var root); working.TryRestore(current);
            if (s.IsSuccess()) { version = Publish(slot, root); result = changed; } return s;
        }
        /// <summary>Publishes one root for an ordered batch. References target handles already known to the caller.</summary>
        public DeterministicStatus TryWithBatch<TMutations>(in ManagedGraphVersion<TNode, TEdge> basis, ref TMutations mutations,
            int mutationCount, out ManagedGraphVersion<TNode, TEdge> version)
            where TMutations : struct, IDeterministicBuffer<GraphMutation<TNode, TEdge>>
        {
            version = default; if (!ReferenceEquals(basis.Owner, this)) return DeterministicStatus.InvalidOwner;
            if (!mutations.IsCreated || mutationCount < 0 || mutationCount > mutations.Capacity) return DeterministicStatus.InvalidConfiguration;
            var s = TrySnapshot(basis.Id, out var source); if (!s.IsSuccess()) return s; s = FreeSlot(out int slot); if (!s.IsSuccess()) return s;
            s = working.TryRestore(source); if (!s.IsSuccess()) return s;
            for (int i = 0; i < mutationCount; i++)
            { var m = mutations.Read(i); s = working.TryApply(m, out _); if (!s.IsSuccess()) { working.TryRestore(current); return s; } }
            s = working.TryCapture(out var root); working.TryRestore(current); if (s.IsSuccess()) version = Publish(slot, root); return s;
        }
        public DeterministicStatus TryReserveVersionCapacity(int required)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (required < 0) return DeterministicStatus.InvalidConfiguration;
            if (required <= roots.Length) return DeterministicStatus.None;
            if (Persistence.Allocation != AllocationPolicy.Grow || required > Persistence.MaxVersionCapacity) return DeterministicStatus.SnapshotCapacityExceeded;
            int capacity = roots.Length; while (capacity < required) capacity = (int)Math.Min((long)Persistence.MaxVersionCapacity, (long)capacity * 2);
            var next = new Root[capacity]; Array.Copy(roots, next, roots.Length);
            var s = working.TryReserveSnapshotCapacity(capacity + 3); if (s.IsSuccess()) roots = next; return s;
        }
        public DeterministicStatus TryWithAddNode(in ManagedGraphVersion<TNode, TEdge> basis, in TNode data, out ManagedGraphVersion<TNode, TEdge> version, out GraphNodeHandle node)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.AddNode, NodeData = data }; var s = TryWith(basis, m, out version, out var r); node = r.Node; return s; }
        public DeterministicStatus TryWithAddEdge(in ManagedGraphVersion<TNode, TEdge> basis, GraphNodeHandle source, GraphNodeHandle target, in TEdge data, out ManagedGraphVersion<TNode, TEdge> version, out GraphEdgeHandle edge)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.AddEdge, Source = source, Target = target, EdgeData = data }; var s = TryWith(basis, m, out version, out var r); edge = r.Edge; return s; }
        public DeterministicStatus TryWithSetNode(in ManagedGraphVersion<TNode, TEdge> basis, GraphNodeHandle node, in TNode data, out ManagedGraphVersion<TNode, TEdge> version)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.SetNode, Node = node, NodeData = data }; return TryWith(basis, m, out version, out _); }
        public DeterministicStatus TryWithSetEdge(in ManagedGraphVersion<TNode, TEdge> basis, GraphEdgeHandle edge, in TEdge data, out ManagedGraphVersion<TNode, TEdge> version)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.SetEdge, Edge = edge, EdgeData = data }; return TryWith(basis, m, out version, out _); }
        public DeterministicStatus TryWithRemoveNode(in ManagedGraphVersion<TNode, TEdge> basis, GraphNodeHandle node, out ManagedGraphVersion<TNode, TEdge> version)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.RemoveNode, Node = node }; return TryWith(basis, m, out version, out _); }
        public DeterministicStatus TryWithRemoveEdge(in ManagedGraphVersion<TNode, TEdge> basis, GraphEdgeHandle edge, out ManagedGraphVersion<TNode, TEdge> version)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.RemoveEdge, Edge = edge }; return TryWith(basis, m, out version, out _); }
        public DeterministicStatus TryWithClear(in ManagedGraphVersion<TNode, TEdge> basis, out ManagedGraphVersion<TNode, TEdge> version)
        { var m = new GraphMutation<TNode, TEdge> { Kind = GraphMutationKind.Clear }; return TryWith(basis, m, out version, out _); }
        public DeterministicStatus TryWriteCanonical<TNodeCodec, TEdgeCodec>(ref DeterministicWriter writer, TNodeCodec nc, TEdgeCodec ec)
            where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
            => current.TryWriteCanonical(ref writer, nc, ec);
        public DeterministicStatus TryValidateCanonical<TNodeCodec, TEdgeCodec>(ref DeterministicReader reader, ref CanonicalScratch scratch, TNodeCodec nc, TEdgeCodec ec)
            where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
            => working.TryValidateCanonical(ref reader, ref scratch, nc, ec);
        public DeterministicStatus TryReadCanonical<TNodeCodec, TEdgeCodec>(ref DeterministicReader reader, ref CanonicalScratch scratch, TNodeCodec nc, TEdgeCodec ec)
            where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
        {
            var r = reader; var w = scratch; var s = working.TryReadCanonical(ref r, ref w, nc, ec); if (!s.IsSuccess()) return s;
            s = working.TryCapture(out var retained); if (!s.IsSuccess()) { working.TryRestore(current); return s; }
            var previous = current; current = retained; previous.Dispose(); reader = r; scratch = w; return s;
        }
        public void Dispose()
        {
            if (!IsCreated) return;
            for (int i = 0; i < roots.Length; i++) if (roots[i].Lease != 0) roots[i].Snapshot.Dispose();
            current.Dispose(); working.Dispose(); count = 0;
        }
    }
}
