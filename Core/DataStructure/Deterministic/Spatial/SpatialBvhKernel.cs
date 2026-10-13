namespace LegendaryTools.Deterministic
{
    internal static class SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TPolicy : unmanaged, IDeterministicComparer<TKey>
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        private static int Allocate(ref TStorage store)
        {
            ref var state = ref store.State;
            if (state.FreeNode >= 0) { int index = state.FreeNode; state.FreeNode = store.ReadNode(index).NextFree; return index; }
            return state.NodeUsed++;
        }
        private static void Free(ref TStorage store, int index)
        {
            var node = store.ReadNode(index); node.NextFree = store.State.FreeNode;
            store.WriteNode(index, node); store.State.FreeNode = index;
        }
        private static SpatialBvhNode<TKey> Node(int slot, in SpatialRawBounds bounds, in TKey key) => new SpatialBvhNode<TKey>
        { RecordSlot = slot, Bounds = bounds, MinimumKey = key, Parent = -1, Left = -1, Right = -1, NextFree = -1 };
        private static TKey Minimum(in TKey a, in TKey b, TPolicy policy) => policy.Compare(a, b) <= 0 ? a : b;
        private static SpatialDistanceKey AddedCost(in SpatialRawBounds a, in SpatialRawBounds b, int dimensions)
        {
            var union = SpatialRawBounds.Union(a, b);
            var larger = SpatialGeometry.Cost(union, dimensions); var smaller = SpatialGeometry.Cost(a, dimensions);
            return SpatialDistanceKey.From(LegendaryTools.FixedPoint.FixedBigUInt.Subtract(larger.Wide, smaller.Wide));
        }
        private static void Refit(ref TStorage store, int index, TPolicy policy)
        {
            var node = store.ReadNode(index); if (node.IsLeaf) return;
            var left = store.ReadNode(node.Left); var right = store.ReadNode(node.Right);
            node.Bounds = SpatialRawBounds.Union(left.Bounds, right.Bounds);
            node.Height = 1 + System.Math.Max(left.Height, right.Height);
            node.MinimumKey = Minimum(left.MinimumKey, right.MinimumKey, policy); store.WriteNode(index, node);
        }
        private static void ReplaceChild(ref TStorage store, int parent, int oldChild, int newChild)
        {
            if (parent < 0) { store.State.Root = newChild; return; }
            var node = store.ReadNode(parent); if (node.Left == oldChild) node.Left = newChild; else node.Right = newChild;
            store.WriteNode(parent, node);
        }
        private static bool KeepLeft(in SpatialBvhNode<TKey> left, in SpatialBvhNode<TKey> right, in SpatialRawBounds other, int dimensions, TPolicy policy)
        {
            if (left.Height != right.Height) return left.Height > right.Height;
            // Equal-height rotations choose the smaller refitted child envelope, then a total key tie.
            int cost = SpatialGeometry.Cost(SpatialRawBounds.Union(other, left.Bounds), dimensions).CompareTo(
                SpatialGeometry.Cost(SpatialRawBounds.Union(other, right.Bounds), dimensions));
            return cost != 0 ? cost > 0 : policy.Compare(left.MinimumKey, right.MinimumKey) <= 0;
        }
        private static int Balance(ref TStorage store, int index, int dimensions, TPolicy policy)
        {
            var a = store.ReadNode(index); if (a.IsLeaf || a.Height < 2) return index;
            var b = store.ReadNode(a.Left); var c = store.ReadNode(a.Right);
            if (c.Height - b.Height > 1)
            {
                int ci = a.Right, cl = c.Left, cr = c.Right; var f = store.ReadNode(cl); var g = store.ReadNode(cr);
                ReplaceChild(ref store, a.Parent, index, ci); c.Parent = a.Parent; c.Left = index; a.Parent = ci;
                bool keepLeft = KeepLeft(f, g, b.Bounds, dimensions, policy);
                int keep = keepLeft ? cl : cr, move = keepLeft ? cr : cl;
                c.Right = keep; a.Right = move;
                var mn = store.ReadNode(move); mn.Parent = index; store.WriteNode(move, mn);
                var kn = store.ReadNode(keep); kn.Parent = ci; store.WriteNode(keep, kn);
                store.WriteNode(index, a); store.WriteNode(ci, c); Refit(ref store, index, policy); Refit(ref store, ci, policy); return ci;
            }
            if (b.Height - c.Height > 1)
            {
                int bi = a.Left; int dl = b.Left, dr = b.Right;
                var d = store.ReadNode(dl); var e = store.ReadNode(dr);
                ReplaceChild(ref store, a.Parent, index, bi); b.Parent = a.Parent; b.Right = index; a.Parent = bi;
                bool keepLeft = KeepLeft(d, e, c.Bounds, dimensions, policy);
                int keep = keepLeft ? dl : dr, move = keepLeft ? dr : dl;
                b.Left = keep; a.Left = move;
                var mn = store.ReadNode(move); mn.Parent = index; store.WriteNode(move, mn);
                var kn = store.ReadNode(keep); kn.Parent = bi; store.WriteNode(keep, kn);
                store.WriteNode(index, a); store.WriteNode(bi, b); Refit(ref store, index, policy); Refit(ref store, bi, policy); return bi;
            }
            return index;
        }
        internal static void Insert(ref TStorage store, int recordSlot, int dimensions, long margin, TPolicy policy)
        {
            var placement = store.ReadPlacement(recordSlot); var entry = store.ReadEntry(recordSlot);
            var envelope = SpatialGeometry.Expand(placement.Bounds, margin, dimensions);
            int leaf = Allocate(ref store); store.WriteNode(leaf, Node(recordSlot, envelope, entry.Key));
            placement.Leaf = leaf; store.WritePlacement(recordSlot, placement);
            if (store.State.Root < 0) { store.State.Root = leaf; return; }
            int sibling = store.State.Root;
            while (!store.ReadNode(sibling).IsLeaf)
            {
                var current = store.ReadNode(sibling); var left = store.ReadNode(current.Left); var right = store.ReadNode(current.Right);
                int c = AddedCost(left.Bounds, envelope, dimensions).CompareTo(AddedCost(right.Bounds, envelope, dimensions));
                if (c == 0) c = left.Height.CompareTo(right.Height);
                if (c == 0) c = policy.Compare(left.MinimumKey, right.MinimumKey);
                sibling = c <= 0 ? current.Left : current.Right;
            }
            var sn = store.ReadNode(sibling); int oldParent = sn.Parent, parent = Allocate(ref store);
            var pn = Node(-1, SpatialRawBounds.Union(sn.Bounds, envelope), Minimum(sn.MinimumKey, entry.Key, policy));
            pn.Parent = oldParent; pn.Left = sibling; pn.Right = leaf; pn.Height = sn.Height + 1;
            sn.Parent = parent; store.WriteNode(sibling, sn);
            var ln = store.ReadNode(leaf); ln.Parent = parent; store.WriteNode(leaf, ln); store.WriteNode(parent, pn);
            ReplaceChild(ref store, oldParent, sibling, parent);
            int cursor = parent;
            while (cursor >= 0) { Refit(ref store, cursor, policy); cursor = Balance(ref store, cursor, dimensions, policy); cursor = store.ReadNode(cursor).Parent; }
        }
        internal static void Remove(ref TStorage store, int recordSlot, int dimensions, TPolicy policy)
        {
            var placement = store.ReadPlacement(recordSlot); int leaf = placement.Leaf;
            if (leaf < 0) return;
            var ln = store.ReadNode(leaf);
            if (ln.Parent < 0) store.State.Root = -1;
            else
            {
                int parent = ln.Parent; var pn = store.ReadNode(parent);
                int sibling = pn.Left == leaf ? pn.Right : pn.Left;
                ReplaceChild(ref store, pn.Parent, parent, sibling);
                var sn = store.ReadNode(sibling); sn.Parent = pn.Parent; store.WriteNode(sibling, sn); Free(ref store, parent);
                int cursor = pn.Parent;
                while (cursor >= 0) { Refit(ref store, cursor, policy); cursor = Balance(ref store, cursor, dimensions, policy); cursor = store.ReadNode(cursor).Parent; }
            }
            Free(ref store, leaf); placement.Leaf = -1; store.WritePlacement(recordSlot, placement);
        }
        private static int CompareOrder(ref TStorage store, int a, int b, int axis, TPolicy policy)
        {
            var pa = store.ReadPlacement(a).Bounds; var pb = store.ReadPlacement(b).Bounds;
            int c = axis < 0 ? 0 : SpatialGeometry.Midpoint(pa.Min(axis), pa.Max(axis)).CompareTo(SpatialGeometry.Midpoint(pb.Min(axis), pb.Max(axis)));
            return c == 0 ? policy.Compare(store.ReadEntry(a).Key, store.ReadEntry(b).Key) : c;
        }
        private static void SiftOrder(ref TStorage store, int begin, int root, int count, int axis, TPolicy policy)
        {
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && CompareOrder(ref store, store.ReadOrder(begin + child), store.ReadOrder(begin + child + 1), axis, policy) < 0) child++;
                int value = store.ReadOrder(begin + root), other = store.ReadOrder(begin + child);
                if (CompareOrder(ref store, value, other, axis, policy) >= 0) return;
                store.WriteOrder(begin + root, other); store.WriteOrder(begin + child, value); root = child;
            }
        }
        internal static void SortOrder(ref TStorage store, int begin, int count, int axis, TPolicy policy)
        {
            for (int i = count / 2 - 1; i >= 0; i--) SiftOrder(ref store, begin, i, count, axis, policy);
            for (int end = count - 1; end > 0; end--)
            { int a = store.ReadOrder(begin); store.WriteOrder(begin, store.ReadOrder(begin + end)); store.WriteOrder(begin + end, a); SiftOrder(ref store, begin, 0, end, axis, policy); }
        }
        internal static void Rebuild(ref TStorage store, int dimensions, long margin, TPolicy policy, bool staticTree)
        {
            store.State.Root = -1; store.State.NodeUsed = 0; store.State.FreeNode = -1;
            int count = 0;
            for (int i = 0; i < store.State.Records.Used; i++) if (store.ReadEntry(i).Active != 0) store.WriteOrder(count++, i);
            if (count == 0) { store.State.Dirty = false; return; }
            if (!staticTree)
            {
                SortOrder(ref store, 0, count, -1, policy);
                for (int i = 0; i < count; i++) Insert(ref store, store.ReadOrder(i), dimensions, margin, policy);
                store.State.Dirty = false; return;
            }
            int tasks = 1; store.WriteTask(0, new SpatialBuildTask { Begin = 0, Count = count, Parent = -1 });
            while (tasks != 0)
            {
                var task = store.ReadTask(--tasks); int nodeId = Allocate(ref store);
                var bounds = store.ReadPlacement(store.ReadOrder(task.Begin)).Bounds;
                for (int i = 1; i < task.Count; i++) bounds = SpatialRawBounds.Union(bounds, store.ReadPlacement(store.ReadOrder(task.Begin + i)).Bounds);
                var node = Node(-1, bounds, default); node.Parent = task.Parent;
                if (task.Parent < 0) store.State.Root = nodeId;
                else { var parent = store.ReadNode(task.Parent); if (task.Side == 0) parent.Left = nodeId; else parent.Right = nodeId; store.WriteNode(task.Parent, parent); }
                if (task.Count == 1)
                {
                    int slot = store.ReadOrder(task.Begin); node.RecordSlot = slot; node.MinimumKey = store.ReadEntry(slot).Key;
                    var placement = store.ReadPlacement(slot); placement.Leaf = nodeId; store.WritePlacement(slot, placement);
                }
                else
                {
                    int axis = 0; ulong width = SpatialGeometry.Width(bounds.MinX, bounds.MaxX);
                    for (int i = 1; i < dimensions; i++) { ulong next = SpatialGeometry.Width(bounds.Min(i), bounds.Max(i)); if (next > width) { axis = i; width = next; } }
                    SortOrder(ref store, task.Begin, task.Count, axis, policy); int half = task.Count / 2;
                    store.WriteTask(tasks++, new SpatialBuildTask { Begin = task.Begin + half, Count = task.Count - half, Parent = nodeId, Side = 1 });
                    store.WriteTask(tasks++, new SpatialBuildTask { Begin = task.Begin, Count = half, Parent = nodeId, Side = 0 });
                }
                store.WriteNode(nodeId, node);
            }
            for (int i = store.State.NodeUsed - 1; i >= 0; i--) Refit(ref store, i, policy);
            store.State.Dirty = false;
        }
    }
}
