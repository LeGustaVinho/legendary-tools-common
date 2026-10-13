namespace LegendaryTools.Deterministic
{
    public readonly struct GraphNodeHandleComparer : IDeterministicComparer<GraphNodeHandle>
    {
        public bool IsValid => true;
        public int Compare(in GraphNodeHandle a, in GraphNodeHandle b) => a.CompareTo(b);
    }
    public readonly struct GraphComponent
    {
        public readonly GraphNodeHandle Node;
        public readonly int Component;
        public GraphComponent(GraphNodeHandle node, int component) { Node = node; Component = component; }
    }
    internal struct GraphDfsFrame
    {
        internal GraphNodeHandle Node;
        internal GraphEdgeHandle Parent;
        internal int Ordinal, Cursor;
    }
    /// <summary>Iterative, statically specialized algorithms. Scratch is caller-owned and never retained.</summary>
    public static unsafe class GraphAlgorithms<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        private static DeterministicStatus Visited(ref CanonicalScratch scratch, int extent, out GraphWorkBuffer<ulong> visited)
        {
            visited = default; int words = (int)(((long)extent + 63) / 64);
            var s = scratch.TryReserve<ulong>(words, out var p); if (!s.IsSuccess()) return s;
            for (int i = 0; i < words; i++) p[i] = 0;
            visited = new GraphWorkBuffer<ulong>(p, words); return DeterministicStatus.None;
        }
        private static bool Seen(ref GraphWorkBuffer<ulong> visited, int extent, int ordinal)
        { BitSetKernel<GraphWorkBuffer<ulong>>.TryGet(ref visited, extent, ordinal, out bool seen); return seen; }
        private static void Mark(ref GraphWorkBuffer<ulong> visited, int extent, int ordinal)
            => BitSetKernel<GraphWorkBuffer<ulong>>.TrySet(ref visited, extent, ordinal, true);
        public static DeterministicStatus TryReachable<TReader>(TReader reader, GraphNodeHandle source, GraphNodeHandle target,
            ref CanonicalScratch scratch, out bool reachable) where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            reachable = false; var s = reader.Check(); if (!s.IsSuccess()) return s;
            s = reader.TryNodeOrdinal(source, out _); if (!s.IsSuccess()) return s;
            s = reader.TryNodeOrdinal(target, out _); if (!s.IsSuccess()) return s;
            if (source == target) { reachable = true; return DeterministicStatus.None; }
            s = Breadth(reader, source, target, true, GraphTraversal.Outgoing, ref scratch, out _, out _, out bool found);
            if (s.IsSuccess()) reachable = found; return s;
        }
        private static DeterministicStatus Breadth<TReader>(TReader reader, GraphNodeHandle start, GraphNodeHandle target, bool stopAtTarget,
            GraphTraversal direction, ref CanonicalScratch scratch, out GraphNodeHandle* result, out int count, out bool found)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            result = null; count = 0; found = false;
            if (direction > GraphTraversal.Incoming) return DeterministicStatus.InvalidConfiguration;
            var s = reader.Check(); if (!s.IsSuccess()) return s;
            s = reader.TryNodeOrdinal(start, out int ordinal); if (!s.IsSuccess()) return s;
            s = Visited(ref scratch, reader.NodeExtent, out var visited); if (!s.IsSuccess()) return s;
            s = scratch.TryReserve<GraphNodeHandle>(reader.NodeCount, out var queueMemory); if (!s.IsSuccess()) return s;
            if (!stopAtTarget) { s = scratch.TryReserve<GraphNodeHandle>(reader.NodeCount, out result); if (!s.IsSuccess()) return s; }
            var queue = new GraphWorkBuffer<GraphNodeHandle>(queueMemory, reader.NodeCount); var state = default(SequenceState);
            Mark(ref visited, reader.NodeExtent, ordinal);
            RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPush(ref queue, ref state, start, false);
            while (state.Count != 0)
            {
                RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPop(ref queue, ref state, true, out var node);
                if (!stopAtTarget) result[count] = node;
                count++;
                if (stopAtTarget && node == target) { found = true; return DeterministicStatus.None; }
                s = reader.TryFirstArc(node, direction, out int cursor); if (!s.IsSuccess()) return s;
                while (cursor != 0)
                {
                    s = reader.TryReadArc(cursor, out var arc, out cursor); if (!s.IsSuccess()) return s;
                    s = reader.TryNodeOrdinal(arc.Neighbor, out int index); if (!s.IsSuccess()) return s;
                    if (Seen(ref visited, reader.NodeExtent, index)) continue;
                    Mark(ref visited, reader.NodeExtent, index);
                    RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPush(ref queue, ref state, arc.Neighbor, false);
                }
            }
            return reader.Check();
        }
        private static DeterministicStatus Publish<T, TOutput>(T* staged, int count, ref TOutput output)
            where T : unmanaged where TOutput : struct, IDeterministicBuffer<T>
        {
            if (!output.IsCreated) return DeterministicStatus.Disposed;
            var s = output.CanPrepareWrite(count, 0, count); if (!s.IsSuccess()) return s;
            s = output.PrepareWrite(count, 0, count); if (!s.IsSuccess()) return s;
            for (int i = 0; i < count; i++) output.Write(i, staged[i]); output.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryBreadthFirst<TReader, TOutput>(TReader reader, GraphNodeHandle start,
            ref CanonicalScratch scratch, ref TOutput output, out int count, GraphTraversal direction = GraphTraversal.Outgoing)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge> where TOutput : struct, IDeterministicBuffer<GraphNodeHandle>
        {
            count = 0; var work = scratch;
            var s = Breadth(reader, start, default, false, direction, ref work, out var staged, out int n, out _); if (!s.IsSuccess()) return s;
            s = Publish(staged, n, ref output); if (s.IsSuccess()) { count = n; scratch = work; } return s;
        }
        public static DeterministicStatus TryDepthFirst<TReader, TOutput>(TReader reader, GraphNodeHandle start,
            ref CanonicalScratch scratch, ref TOutput output, out int count, GraphTraversal direction = GraphTraversal.Outgoing)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge> where TOutput : struct, IDeterministicBuffer<GraphNodeHandle>
        {
            count = 0; if (direction > GraphTraversal.Incoming) return DeterministicStatus.InvalidConfiguration;
            var s = reader.Check(); if (!s.IsSuccess()) return s;
            s = reader.TryNodeOrdinal(start, out int ordinal); if (!s.IsSuccess()) return s;
            var work = scratch;
            s = Visited(ref work, reader.NodeExtent, out var visited); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphDfsFrame>(reader.NodeCount, out var frames); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphNodeHandle>(reader.NodeCount, out var result); if (!s.IsSuccess()) return s;
            s = reader.TryFirstArc(start, direction, out int first); if (!s.IsSuccess()) return s;
            Mark(ref visited, reader.NodeExtent, ordinal); int n = 1; result[0] = start;
            int depth = 1;
            frames[0] = new GraphDfsFrame { Node = start, Ordinal = ordinal, Cursor = first };
            while (depth != 0)
            {
                var frame = frames[depth - 1];
                if (frame.Cursor == 0) { depth--; continue; }
                s = reader.TryReadArc(frame.Cursor, out var arc, out frame.Cursor); if (!s.IsSuccess()) return s;
                frames[depth - 1] = frame;
                s = reader.TryNodeOrdinal(arc.Neighbor, out int next); if (!s.IsSuccess()) return s;
                if (Seen(ref visited, reader.NodeExtent, next)) continue;
                Mark(ref visited, reader.NodeExtent, next); result[n++] = arc.Neighbor;
                s = reader.TryFirstArc(arc.Neighbor, direction, out first); if (!s.IsSuccess()) return s;
                frames[depth++] = new GraphDfsFrame { Node = arc.Neighbor, Ordinal = next, Cursor = first };
            }
            s = reader.Check(); if (!s.IsSuccess()) return s;
            s = Publish(result, n, ref output); if (s.IsSuccess()) { count = n; scratch = work; } return s;
        }
        public static DeterministicStatus TryHasCycle<TReader>(TReader reader, ref CanonicalScratch scratch, out bool cyclic)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            cyclic = false; var s = reader.Check(); if (!s.IsSuccess()) return s; var work = scratch;
            s = work.TryReserve<byte>(reader.NodeExtent, out var colors); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphDfsFrame>(reader.NodeCount, out var stack); if (!s.IsSuccess()) return s;
            for (int i = 0; i < reader.NodeExtent; i++) colors[i] = 0;
            for (int root = 0; root < reader.NodeExtent; root++)
            {
                s = reader.TryNodeAt(root, out var node, out _); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                if (colors[root] != 0) continue;
                s = reader.TryFirstArc(node, GraphTraversal.Outgoing, out int first); if (!s.IsSuccess()) return s;
                int depth = 1; stack[0] = new GraphDfsFrame { Node = node, Ordinal = root, Cursor = first }; colors[root] = 1;
                while (depth != 0)
                {
                    var frame = stack[depth - 1];
                    if (frame.Cursor == 0) { colors[frame.Ordinal] = 2; depth--; continue; }
                    s = reader.TryReadArc(frame.Cursor, out var arc, out frame.Cursor); if (!s.IsSuccess()) return s;
                    stack[depth - 1] = frame;
                    if (reader.Options.Direction == GraphDirection.Undirected && arc.Edge == frame.Parent) continue;
                    s = reader.TryNodeOrdinal(arc.Neighbor, out int index); if (!s.IsSuccess()) return s;
                    if (colors[index] == 1 || colors[index] == 2 && reader.Options.Direction == GraphDirection.Undirected)
                    { cyclic = true; scratch = work; return DeterministicStatus.None; }
                    if (colors[index] != 0) continue;
                    s = reader.TryFirstArc(arc.Neighbor, GraphTraversal.Outgoing, out first); if (!s.IsSuccess()) return s;
                    colors[index] = 1; stack[depth++] = new GraphDfsFrame { Node = arc.Neighbor, Ordinal = index, Cursor = first, Parent = arc.Edge };
                }
            }
            scratch = work; return reader.Check();
        }
        public static DeterministicStatus TryTopologicalOrder<TReader, TOutput>(TReader reader, ref CanonicalScratch scratch,
            ref TOutput output, out int count) where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
            where TOutput : struct, IDeterministicBuffer<GraphNodeHandle>
        {
            count = 0; var s = reader.Check(); if (!s.IsSuccess()) return s;
            if (reader.Options.Direction != GraphDirection.Directed) return DeterministicStatus.InvalidConfiguration;
            var work = scratch;
            s = work.TryReserve<int>(reader.NodeExtent, out var degrees); if (!s.IsSuccess()) return s;
            s = work.TryReserve<PriorityQueueNode<GraphNodeHandle, GraphNodeHandle>>(reader.NodeCount, out var heap); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphNodeHandle>(reader.NodeCount, out var result); if (!s.IsSuccess()) return s;
            var queue = new GraphWorkBuffer<PriorityQueueNode<GraphNodeHandle, GraphNodeHandle>>(heap, reader.NodeCount);
            var state = default(PriorityQueueState); var comparer = default(GraphNodeHandleComparer);
            for (int i = 0; i < reader.NodeExtent; i++)
            {
                degrees[i] = 0; s = reader.TryNodeAt(i, out var h, out _); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                s = reader.TryFirstArc(h, GraphTraversal.Incoming, out int cursor); if (!s.IsSuccess()) return s;
                while (cursor != 0) { s = reader.TryReadArc(cursor, out _, out cursor); if (!s.IsSuccess()) return s; degrees[i]++; }
                if (degrees[i] == 0)
                    PriorityQueueKernel<GraphNodeHandle, GraphNodeHandle, GraphNodeHandleComparer, GraphWorkBuffer<PriorityQueueNode<GraphNodeHandle, GraphNodeHandle>>>.TryEnqueue(ref queue, ref state, h, h, comparer);
            }
            int n = 0;
            while (state.Count != 0)
            {
                PriorityQueueKernel<GraphNodeHandle, GraphNodeHandle, GraphNodeHandleComparer, GraphWorkBuffer<PriorityQueueNode<GraphNodeHandle, GraphNodeHandle>>>.TryDequeue(ref queue, ref state, comparer, out var node, out _);
                result[n++] = node;
                s = reader.TryFirstArc(node, GraphTraversal.Outgoing, out int cursor); if (!s.IsSuccess()) return s;
                while (cursor != 0)
                {
                    s = reader.TryReadArc(cursor, out var arc, out cursor); if (!s.IsSuccess()) return s;
                    s = reader.TryNodeOrdinal(arc.Neighbor, out int index); if (!s.IsSuccess()) return s;
                    if (--degrees[index] == 0)
                        PriorityQueueKernel<GraphNodeHandle, GraphNodeHandle, GraphNodeHandleComparer, GraphWorkBuffer<PriorityQueueNode<GraphNodeHandle, GraphNodeHandle>>>.TryEnqueue(ref queue, ref state, arc.Neighbor, arc.Neighbor, comparer);
                }
            }
            if (n != reader.NodeCount) return DeterministicStatus.CycleDetected;
            s = reader.Check(); if (!s.IsSuccess()) return s;
            s = Publish(result, n, ref output); if (s.IsSuccess()) { count = n; scratch = work; } return s;
        }
        /// <summary>Directed graphs use weak connectivity; labels follow the smallest handle of each component.</summary>
        public static DeterministicStatus TryComponents<TReader, TOutput>(TReader reader, ref CanonicalScratch scratch,
            ref TOutput output, out int nodeCount, out int componentCount)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge> where TOutput : struct, IDeterministicBuffer<GraphComponent>
        {
            nodeCount = componentCount = 0; var s = reader.Check(); if (!s.IsSuccess()) return s; var work = scratch;
            s = work.TryReserve<int>(reader.NodeExtent, out var labels); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphNodeHandle>(reader.NodeCount, out var memory); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphComponent>(reader.NodeCount, out var result); if (!s.IsSuccess()) return s;
            for (int i = 0; i < reader.NodeExtent; i++) labels[i] = -1;
            var queue = new GraphWorkBuffer<GraphNodeHandle>(memory, reader.NodeCount); int components = 0;
            for (int root = 0; root < reader.NodeExtent; root++)
            {
                s = reader.TryNodeAt(root, out var node, out _); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                if (labels[root] != -1) continue;
                var state = default(SequenceState); labels[root] = components;
                RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPush(ref queue, ref state, node, false);
                while (state.Count != 0)
                {
                    RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPop(ref queue, ref state, true, out var current);
                    int directions = reader.Options.Direction == GraphDirection.Directed ? 2 : 1;
                    for (int d = 0; d < directions; d++)
                    {
                        s = reader.TryFirstArc(current, (GraphTraversal)d, out int cursor); if (!s.IsSuccess()) return s;
                        while (cursor != 0)
                        {
                            s = reader.TryReadArc(cursor, out var arc, out cursor); if (!s.IsSuccess()) return s;
                            s = reader.TryNodeOrdinal(arc.Neighbor, out int index); if (!s.IsSuccess()) return s;
                            if (labels[index] != -1) continue;
                            labels[index] = components;
                            RingKernel<GraphNodeHandle, GraphWorkBuffer<GraphNodeHandle>>.TryPush(ref queue, ref state, arc.Neighbor, false);
                        }
                    }
                }
                components++;
            }
            int n = 0;
            for (int i = 0; i < reader.NodeExtent; i++)
            {
                s = reader.TryNodeAt(i, out var h, out _); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                result[n++] = new GraphComponent(h, labels[i]);
            }
            s = Publish(result, n, ref output); if (s.IsSuccess()) { nodeCount = n; componentCount = components; scratch = work; } return s;
        }
    }
}
