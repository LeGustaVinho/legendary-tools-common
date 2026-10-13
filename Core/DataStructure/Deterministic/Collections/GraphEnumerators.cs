namespace LegendaryTools.Deterministic
{
    public struct GraphNodeEnumerator<TNode, TEdge, TReader> where TNode : unmanaged where TEdge : unmanaged
        where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
    {
        private TReader reader;
        private int index;
        public GraphNodeItem<TNode> Current { get; private set; }
        public GraphNodeEnumerator(TReader reader) { this.reader = reader; index = -1; Current = default; }
        public GraphNodeEnumerator<TNode, TEdge, TReader> GetEnumerator() => this;
        public DeterministicStatus TryMoveNext(out bool moved)
        {
            moved = false; var s = reader.Check(); if (!s.IsSuccess()) return s;
            while (++index < reader.NodeExtent)
            {
                s = reader.TryNodeAt(index, out var h, out var data); if (s == DeterministicStatus.NotFound) continue;
                if (!s.IsSuccess()) return s; Current = new GraphNodeItem<TNode>(h, data); moved = true; break;
            }
            index = moved ? index : reader.NodeExtent; return DeterministicStatus.None;
        }
        public bool MoveNext() => TryMoveNext(out bool moved).IsSuccess() && moved;
    }
    public struct GraphEdgeEnumerator<TNode, TEdge, TReader> where TNode : unmanaged where TEdge : unmanaged
        where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
    {
        private TReader reader;
        private int index;
        public GraphEdgeItem<TEdge> Current { get; private set; }
        public GraphEdgeEnumerator(TReader reader) { this.reader = reader; index = -1; Current = default; }
        public GraphEdgeEnumerator<TNode, TEdge, TReader> GetEnumerator() => this;
        public DeterministicStatus TryMoveNext(out bool moved)
        {
            moved = false; var s = reader.Check(); if (!s.IsSuccess()) return s;
            while (++index < reader.EdgeExtent)
            {
                s = reader.TryEdgeAt(index, out var h, out var edge); if (s == DeterministicStatus.NotFound) continue;
                if (!s.IsSuccess()) return s; Current = new GraphEdgeItem<TEdge>(h, edge); moved = true; break;
            }
            index = moved ? index : reader.EdgeExtent; return DeterministicStatus.None;
        }
        public bool MoveNext() => TryMoveNext(out bool moved).IsSuccess() && moved;
    }
    public struct GraphArcEnumerator<TNode, TEdge, TReader> where TNode : unmanaged where TEdge : unmanaged
        where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
    {
        private TReader reader;
        private int cursor;
        private DeterministicStatus initial;
        public GraphArc Current { get; private set; }
        public GraphArcEnumerator(TReader reader, GraphNodeHandle node, GraphTraversal traversal)
        { this.reader = reader; Current = default; initial = reader.TryFirstArc(node, traversal, out cursor); }
        public GraphArcEnumerator<TNode, TEdge, TReader> GetEnumerator() => this;
        public DeterministicStatus TryMoveNext(out bool moved)
        {
            moved = false; if (!initial.IsSuccess()) return initial;
            var s = reader.Check(); if (!s.IsSuccess()) return s; if (cursor == 0) return DeterministicStatus.None;
            s = reader.TryReadArc(cursor, out var arc, out cursor); if (s.IsSuccess()) { Current = arc; moved = true; } return s;
        }
        public bool MoveNext() => TryMoveNext(out bool moved).IsSuccess() && moved;
    }
}
