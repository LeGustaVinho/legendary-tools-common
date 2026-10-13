namespace LegendaryTools.Deterministic
{
    public struct AvlNode<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
    {
        public TKey Key;
        public TValue Value;
        public int Left, Right, Parent, Height, Size, NextFree;
        public byte Active;
    }
    public struct AvlState { public int Root, Count, Used, FreeHead; }

    /// <summary>Index-based AVL tree. No recursion or pointer-based tree identity; rank lookup uses subtree sizes.</summary>
    public static class AvlKernel<TKey, TValue, TComparer, TBuffer>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
        where TBuffer : struct, IDeterministicBuffer<AvlNode<TKey, TValue>>
    {
        private static int Height(ref TBuffer buffer, int node) => node == 0 ? 0 : buffer.Read(node - 1).Height;
        private static int Size(ref TBuffer buffer, int node) => node == 0 ? 0 : buffer.Read(node - 1).Size;
        public static int First(ref TBuffer buffer, int root)
        { if (root == 0) return 0; int next; while ((next = buffer.Read(root - 1).Left) != 0) root = next; return root; }
        public static int Next(ref TBuffer buffer, int node)
        {
            var current = buffer.Read(node - 1); if (current.Right != 0) return First(ref buffer, current.Right);
            int parent = current.Parent;
            while (parent != 0) { var p = buffer.Read(parent - 1); if (p.Left == node) return parent; node = parent; parent = p.Parent; }
            return 0;
        }
        private static int Find(ref TBuffer buffer, int root, in TKey key, in TComparer comparer, out int parent)
        {
            parent = 0;
            while (root != 0)
            {
                var node = buffer.Read(root - 1); int c = comparer.Compare(key, node.Key);
                if (c == 0) return root;
                parent = root; root = c < 0 ? node.Left : node.Right;
            }
            return 0;
        }
        public static DeterministicStatus TryGet(ref TBuffer buffer, in AvlState state, in TKey key, in TComparer comparer, out TValue value)
        {
            value = default; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int node = Find(ref buffer, state.Root, key, comparer, out _); if (node == 0) return DeterministicStatus.NotFound;
            value = buffer.Read(node - 1).Value; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryGetAt(ref TBuffer buffer, in AvlState state, int index, out SortedEntry<TKey, TValue> item)
        {
            item = default; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)state.Count) return DeterministicStatus.OutOfRange;
            int encoded = state.Root;
            while (encoded != 0)
            {
                var node = buffer.Read(encoded - 1); int leftSize = Size(ref buffer, node.Left);
                if (index == leftSize) { item = new SortedEntry<TKey, TValue>(node.Key, node.Value); return DeterministicStatus.None; }
                if (index < leftSize) encoded = node.Left; else { index -= leftSize + 1; encoded = node.Right; }
            }
            return DeterministicStatus.InvalidEncoding;
        }
        private static DeterministicStatus ReserveNode(ref TBuffer buffer, int required, int encoded, bool prepare)
        { if (encoded == 0) return DeterministicStatus.None; return prepare ? buffer.PrepareWrite(required, encoded - 1, 1) : buffer.CanPrepareWrite(required, encoded - 1, 1); }
        private static DeterministicStatus ReservePair(ref TBuffer buffer, int required, in AvlNode<TKey, TValue> node, bool prepare)
        { var status = ReserveNode(ref buffer, required, node.Left, prepare); return status.IsSuccess() ? ReserveNode(ref buffer, required, node.Right, prepare) : status; }
        private static DeterministicStatus ReservePath(ref TBuffer buffer, int required, int start, bool prepare)
        {
            while (start != 0)
            {
                var node = buffer.Read(start - 1); var status = ReserveNode(ref buffer, required, start, prepare); if (!status.IsSuccess()) return status;
                status = ReservePair(ref buffer, required, node, prepare); if (!status.IsSuccess()) return status;
                for (int side = 0; side < 2; side++)
                {
                    int child = side == 0 ? node.Left : node.Right; if (child == 0) continue;
                    var c = buffer.Read(child - 1); status = ReservePair(ref buffer, required, c, prepare); if (!status.IsSuccess()) return status;
                    for (int inner = 0; inner < 2; inner++)
                    {
                        int grandchild = inner == 0 ? c.Left : c.Right; if (grandchild == 0) continue;
                        status = ReservePair(ref buffer, required, buffer.Read(grandchild - 1), prepare); if (!status.IsSuccess()) return status;
                    }
                }
                start = node.Parent;
            }
            return DeterministicStatus.None;
        }
        private static void Update(ref TBuffer buffer, int encoded)
        {
            var node = buffer.Read(encoded - 1);
            var leftNode = node.Left == 0 ? default : buffer.Read(node.Left - 1);
            var rightNode = node.Right == 0 ? default : buffer.Read(node.Right - 1);
            int left = leftNode.Height, right = rightNode.Height;
            node.Height = 1 + (left > right ? left : right); node.Size = 1 + leftNode.Size + rightNode.Size; buffer.Write(encoded - 1, node);
        }
        private static void Relink(ref TBuffer buffer, ref AvlState state, int parent, int oldChild, int newChild)
        {
            if (parent == 0) state.Root = newChild;
            else
            { var p = buffer.Read(parent - 1); if (p.Left == oldChild) p.Left = newChild; else p.Right = newChild; buffer.Write(parent - 1, p); }
            if (newChild != 0) { var child = buffer.Read(newChild - 1); child.Parent = parent; buffer.Write(newChild - 1, child); }
        }
        private static int Rotate(ref TBuffer buffer, ref AvlState state, int encoded, bool left)
        {
            var node = buffer.Read(encoded - 1); int parent = node.Parent, pivotId = left ? node.Right : node.Left;
            var pivot = buffer.Read(pivotId - 1); int beta = left ? pivot.Left : pivot.Right;
            if (left) { node.Right = beta; pivot.Left = encoded; } else { node.Left = beta; pivot.Right = encoded; }
            node.Parent = pivotId; pivot.Parent = parent;
            if (beta != 0) { var b = buffer.Read(beta - 1); b.Parent = encoded; buffer.Write(beta - 1, b); }
            buffer.Write(encoded - 1, node); buffer.Write(pivotId - 1, pivot); Relink(ref buffer, ref state, parent, encoded, pivotId);
            Update(ref buffer, encoded); Update(ref buffer, pivotId); return pivotId;
        }
        private static void Rebalance(ref TBuffer buffer, ref AvlState state, int encoded)
        {
            while (encoded != 0)
            {
                Update(ref buffer, encoded); var node = buffer.Read(encoded - 1);
                int balance = Height(ref buffer, node.Left) - Height(ref buffer, node.Right);
                if (balance > 1)
                {
                    var child = buffer.Read(node.Left - 1);
                    if (Height(ref buffer, child.Left) < Height(ref buffer, child.Right)) Rotate(ref buffer, ref state, node.Left, true);
                    encoded = Rotate(ref buffer, ref state, encoded, false);
                }
                else if (balance < -1)
                {
                    var child = buffer.Read(node.Right - 1);
                    if (Height(ref buffer, child.Right) < Height(ref buffer, child.Left)) Rotate(ref buffer, ref state, node.Right, false);
                    encoded = Rotate(ref buffer, ref state, encoded, true);
                }
                encoded = buffer.Read(encoded - 1).Parent;
            }
        }
        public static DeterministicStatus TryPut(ref TBuffer buffer, ref AvlState state, in TKey key, in TValue value, in TComparer comparer, bool addOnly)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int found = Find(ref buffer, state.Root, key, comparer, out int parent);
            if (found != 0)
            {
                if (addOnly) return DeterministicStatus.DuplicateKey;
                var status = buffer.PrepareWrite(buffer.Capacity, found - 1, 1); if (!status.IsSuccess()) return status;
                var current = buffer.Read(found - 1); current.Value = value; buffer.Write(found - 1, current); buffer.CommitWrite(); return DeterministicStatus.None;
            }
            if (state.FreeHead == 0 && state.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int slot = state.FreeHead != 0 ? state.FreeHead : state.Used + 1;
            int required = state.FreeHead != 0 ? buffer.Capacity : state.Used + 1;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = ReserveNode(ref buffer, required, slot, phase != 0); if (!status.IsSuccess()) return status;
                status = ReservePath(ref buffer, required, parent, phase != 0); if (!status.IsSuccess()) return status;
            }
            if (state.FreeHead != 0) state.FreeHead = buffer.Read(slot - 1).NextFree; else state.Used++;
            var node = new AvlNode<TKey, TValue> { Key = key, Value = value, Height = 1, Size = 1, Parent = parent, Active = 1 };
            buffer.Write(slot - 1, node);
            if (parent == 0) state.Root = slot;
            else { var p = buffer.Read(parent - 1); if (comparer.Compare(key, p.Key) < 0) p.Left = slot; else p.Right = slot; buffer.Write(parent - 1, p); }
            state.Count++; Rebalance(ref buffer, ref state, parent); buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemove(ref TBuffer buffer, ref AvlState state, in TKey key, in TComparer comparer)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int target = Find(ref buffer, state.Root, key, comparer, out _); if (target == 0) return DeterministicStatus.NotFound;
            var original = buffer.Read(target - 1); int removed = original.Left != 0 && original.Right != 0 ? First(ref buffer, original.Right) : target;
            var node = buffer.Read(removed - 1); int child = node.Left != 0 ? node.Left : node.Right;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = ReserveNode(ref buffer, buffer.Capacity, target, phase != 0); if (!status.IsSuccess()) return status;
                status = ReservePath(ref buffer, buffer.Capacity, removed, phase != 0); if (!status.IsSuccess()) return status;
            }
            if (removed != target) { original.Key = node.Key; original.Value = node.Value; buffer.Write(target - 1, original); }
            Relink(ref buffer, ref state, node.Parent, removed, child);
            buffer.Write(removed - 1, new AvlNode<TKey, TValue> { NextFree = state.FreeHead }); state.FreeHead = removed; state.Count--;
            Rebalance(ref buffer, ref state, node.Parent); buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TBuffer buffer, ref AvlState state)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (state.Used == 0) return DeterministicStatus.None;
            var status = buffer.PrepareWrite(buffer.Capacity, 0, state.Used); if (!status.IsSuccess()) return status;
            for (int i = 0; i < state.Used; i++) buffer.Write(i, default);
            state = default; buffer.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
