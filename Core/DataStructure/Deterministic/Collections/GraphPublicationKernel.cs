using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Publishes a validated root, reserving all changed records before writing any of them.</summary>
    public static unsafe class GraphPublicationKernel<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        private static bool PayloadEqual<T>(in T a, in T b) where T : unmanaged
        {
            // A stricter comparison than canonical equality also preserves codec normalization.
            // Padding can cause an extra copy, but can never hide an actual field change.
            fixed (T* left = &a) fixed (T* right = &b)
            {
                byte* x = (byte*)left, y = (byte*)right;
                for (int i = 0; i < sizeof(T); i++) if (x[i] != y[i]) return false;
            }
            return true;
        }
        private static bool NodeEqual(in SlotMapNode<GraphNodeRecord<TNode>> a, in SlotMapNode<GraphNodeRecord<TNode>> b)
            => a.Generation == b.Generation && a.NextFree == b.NextFree && a.Active == b.Active && a.Retired == b.Retired &&
                a.Value.OutHead == b.Value.OutHead && a.Value.OutTail == b.Value.OutTail && a.Value.InHead == b.Value.InHead &&
                a.Value.InTail == b.Value.InTail && a.Value.OutDegree == b.Value.OutDegree && a.Value.InDegree == b.Value.InDegree &&
                PayloadEqual(a.Value.Data, b.Value.Data);
        private static bool EdgeEqual(in SlotMapNode<GraphEdgeRecord<TEdge>> a, in SlotMapNode<GraphEdgeRecord<TEdge>> b)
            => a.Generation == b.Generation && a.NextFree == b.NextFree && a.Active == b.Active && a.Retired == b.Retired &&
                a.Value.Source == b.Value.Source && a.Value.Target == b.Value.Target && PayloadEqual(a.Value.Data, b.Value.Data);
        private static bool ArcEqual(in GraphIncidence a, in GraphIncidence b)
            => a.Edge == b.Edge && a.Neighbor == b.Neighbor && a.Previous == b.Previous && a.Next == b.Next && a.Side == b.Side;
        private static DeterministicStatus Flags(ref CanonicalScratch scratch, int count, out ulong* flags, out int words)
        {
            words = (int)(((long)count + 63) / 64);
            var s = scratch.TryReserve<ulong>(words, out flags); if (!s.IsSuccess()) return s;
            for (int i = 0; i < words; i++) flags[i] = 0;
            return DeterministicStatus.None;
        }
        private static void Mark(ulong* flags, int index) => flags[index >> 6] |= 1UL << (index & 63);
        private static DeterministicStatus Reserve<T, TBuffer>(ref TBuffer buffer, int capacity, ulong* flags, int words, bool prepare)
            where T : unmanaged where TBuffer : struct, IDeterministicImportBuffer<T>
        {
            for (int word = 0; word < words; word++)
            {
                ulong bits = flags[word];
                while (bits != 0)
                {
                    int index = word * 64 + DeterministicBits.TrailingZeroCount(bits); bits &= bits - 1;
                    var s = prepare ? buffer.PrepareWrite(capacity, index, 1) : buffer.CanPrepareWrite(capacity, index, 1);
                    if (!s.IsSuccess()) return s;
                }
            }
            return DeterministicStatus.None;
        }
        private static void Write<T, TBuffer>(ref TBuffer buffer, T* items, int used, ulong* flags, int words)
            where T : unmanaged where TBuffer : struct, IDeterministicImportBuffer<T>
        {
            for (int word = 0; word < words; word++)
            {
                ulong bits = flags[word];
                while (bits != 0)
                {
                    int index = word * 64 + DeterministicBits.TrailingZeroCount(bits); bits &= bits - 1;
                    buffer.Write(index, index < used ? items[index] : default);
                }
            }
        }
        public static DeterministicStatus TryPublish<TNodes, TEdges, TArcs>(in GraphCanonicalStage<TNode, TEdge> stage,
            ref TNodes nodes, ref TEdges edges, ref TArcs arcs, ref SlotMapState ns, ref SlotMapState es, ref CanonicalScratch scratch)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
            where TArcs : struct, IDeterministicImportBuffer<GraphIncidence>
        {
            // Growing a logical capacity can preserve existing COW pages. Shrink keeps the
            // replacement path, whose contract clears records outside the new capacity.
            int nodeCapacity = stage.Nodes.Capacity, edgeCapacity = stage.Edges.Capacity, arcCapacity = edgeCapacity * 2;
            if (nodeCapacity < nodes.Capacity || edgeCapacity < edges.Capacity || arcCapacity < arcs.Capacity)
                return TryPublishFull(stage, ref nodes, ref edges, ref arcs, ref ns, ref es);
            bool growNodes = nodeCapacity != nodes.Capacity, growEdges = edgeCapacity != edges.Capacity, growArcs = arcCapacity != arcs.Capacity;
            // Some import buffers (e.g. caller-owned GraphWorkBuffer) can replace their
            // capacity but cannot grow through PrepareWrite. Preserve that public contract.
            DeterministicStatus growth;
            if (growNodes)
            {
                growth = nodes.CanPrepareWrite(nodeCapacity, 0, 0);
                if (growth == DeterministicStatus.BufferTooSmall || growth == DeterministicStatus.InvalidConfiguration) return TryPublishFull(stage, ref nodes, ref edges, ref arcs, ref ns, ref es);
                if (!growth.IsSuccess()) return growth;
            }
            if (growEdges)
            {
                growth = edges.CanPrepareWrite(edgeCapacity, 0, 0);
                if (growth == DeterministicStatus.BufferTooSmall || growth == DeterministicStatus.InvalidConfiguration) return TryPublishFull(stage, ref nodes, ref edges, ref arcs, ref ns, ref es);
                if (!growth.IsSuccess()) return growth;
            }
            if (growArcs)
            {
                growth = arcs.CanPrepareWrite(arcCapacity, 0, 0);
                if (growth == DeterministicStatus.BufferTooSmall || growth == DeterministicStatus.InvalidConfiguration) return TryPublishFull(stage, ref nodes, ref edges, ref arcs, ref ns, ref es);
                if (!growth.IsSuccess()) return growth;
            }
            var work = scratch; int nc = Math.Max(ns.Used, stage.Nodes.State.Used), ec = Math.Max(es.Used, stage.Edges.State.Used), ac = ec * 2;
            var s = Flags(ref work, nc, out var nf, out int nw); if (!s.IsSuccess()) return s;
            s = Flags(ref work, ec, out var ef, out int ew); if (!s.IsSuccess()) return s;
            s = Flags(ref work, ac, out var af, out int aw); if (!s.IsSuccess()) return s;
            for (int i = 0; i < nc; i++)
            {
                var current = i < nodes.Capacity ? nodes.Read(i) : default; var wanted = i < stage.Nodes.State.Used ? stage.Nodes.Items[i] : default;
                if (!NodeEqual(current, wanted)) Mark(nf, i);
            }
            for (int i = 0; i < ec; i++)
            {
                var current = i < edges.Capacity ? edges.Read(i) : default; var wanted = i < stage.Edges.State.Used ? stage.Edges.Items[i] : default;
                if (!EdgeEqual(current, wanted)) Mark(ef, i);
            }
            for (int i = 0; i < ac; i++)
            {
                var current = i < arcs.Capacity ? arcs.Read(i) : default; var wanted = i < stage.Edges.State.Used * 2 ? stage.Arcs[i] : default;
                if (!ArcEqual(current, wanted)) Mark(af, i);
            }
            for (int phase = 0; phase < 2; phase++)
            {
                // Zero-length reservations also publish a capacity-only growth when no record differs.
                if (growNodes) { s = phase == 0 ? nodes.CanPrepareWrite(nodeCapacity, 0, 0) : nodes.PrepareWrite(nodeCapacity, 0, 0); if (!s.IsSuccess()) return s; }
                if (growEdges) { s = phase == 0 ? edges.CanPrepareWrite(edgeCapacity, 0, 0) : edges.PrepareWrite(edgeCapacity, 0, 0); if (!s.IsSuccess()) return s; }
                if (growArcs) { s = phase == 0 ? arcs.CanPrepareWrite(arcCapacity, 0, 0) : arcs.PrepareWrite(arcCapacity, 0, 0); if (!s.IsSuccess()) return s; }
                s = Reserve<SlotMapNode<GraphNodeRecord<TNode>>, TNodes>(ref nodes, nodeCapacity, nf, nw, phase != 0); if (!s.IsSuccess()) return s;
                s = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edgeCapacity, ef, ew, phase != 0); if (!s.IsSuccess()) return s;
                s = Reserve<GraphIncidence, TArcs>(ref arcs, arcCapacity, af, aw, phase != 0); if (!s.IsSuccess()) return s;
            }
            Write(ref nodes, stage.Nodes.Items, stage.Nodes.State.Used, nf, nw);
            Write(ref edges, stage.Edges.Items, stage.Edges.State.Used, ef, ew);
            Write(ref arcs, stage.Arcs, stage.Edges.State.Used * 2, af, aw);
            ns = stage.Nodes.State; es = stage.Edges.State;
            // Successful publication still invalidates borrowed mutable views, even for an empty delta.
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); scratch = work;
            return DeterministicStatus.None;
        }
        public static DeterministicStatus TryPublishFull<TNodes, TEdges, TArcs>(in GraphCanonicalStage<TNode, TEdge> stage,
            ref TNodes nodes, ref TEdges edges, ref TArcs arcs, ref SlotMapState ns, ref SlotMapState es)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
            where TArcs : struct, IDeterministicImportBuffer<GraphIncidence>
        {
            var s = nodes.CanReplaceCapacity(stage.Nodes.Capacity); if (!s.IsSuccess()) return s;
            s = edges.CanReplaceCapacity(stage.Edges.Capacity); if (!s.IsSuccess()) return s;
            s = arcs.CanReplaceCapacity(stage.Edges.Capacity * 2); if (!s.IsSuccess()) return s;
            s = nodes.PrepareReplace(stage.Nodes.Capacity); if (!s.IsSuccess()) return s;
            s = edges.PrepareReplace(stage.Edges.Capacity); if (!s.IsSuccess()) return s;
            s = arcs.PrepareReplace(stage.Edges.Capacity * 2); if (!s.IsSuccess()) return s;
            for (int i = 0; i < stage.Nodes.State.Used; i++) nodes.Write(i, stage.Nodes.Items[i]);
            for (int i = 0; i < stage.Edges.State.Used; i++) edges.Write(i, stage.Edges.Items[i]);
            for (int i = 0; i < stage.Edges.State.Used * 2; i++) arcs.Write(i, stage.Arcs[i]);
            ns = stage.Nodes.State; es = stage.Edges.State;
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
