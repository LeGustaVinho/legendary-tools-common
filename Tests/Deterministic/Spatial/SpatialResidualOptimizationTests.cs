using System;
using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialResidualOptimizationTests
    {
        [OneTimeSetUp]
        public void Initialize()
        {
            if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize();
            if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize();
        }

        private sealed class Probe<TBounds, TAccess, TCodec> : IDisposable
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            private readonly bool native;
            internal readonly SpatialIndexOptions Options;
            internal DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Managed;
            private ManagedSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> snapshot;
#if !FIXEDPOINT_STANDALONE
            internal NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Native;
            private NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> nativeSnapshot;
#endif
            internal Probe(SpatialIndexOptions options, bool native)
            {
                Options = options; this.native = native;
#if !FIXEDPOINT_STANDALONE
                if (native) { Native = new NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options, Allocator.Persistent); return; }
#endif
                Managed = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options);
            }
            internal static TBounds Box(long x, long width = 4)
            {
                TBounds b = default;
                if (default(TAccess).Dimensions == 2) *(FixedBounds2*)&b = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, 0), FixedVector2.FromRaw(x + width, 4));
                else *(FixedBounds3*)&b = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, 0, 0), FixedVector3.FromRaw(x + width, 4, 4));
                return b;
            }
            internal DeterministicStatus Apply(SpatialMutation<int, int, TBounds> m)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return Native.TryApply(m);
#endif
                return Managed.TryApply(m);
            }
            internal void Add(int key, long x, long width = 4) => Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Add, Key = key, Bounds = Box(x, width), Data = key }).ThrowIfFailure();
            internal void Move(int key, long x, long width = 4) => Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.UpdateBounds, Key = key, Bounds = Box(x, width) }).ThrowIfFailure();
            internal void Remove(int key) => Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Remove, Key = key }).ThrowIfFailure();
            internal DeterministicStatus Batch(params SpatialMutation<int, int, TBounds>[] commands)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { fixed (SpatialMutation<int, int, TBounds>* p = commands) return Native.TryApplyBatch(p, commands.Length); }
#endif
                return Managed.TryApplyBatch(commands);
            }
            internal void Rebuild()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Native.TryRebuild().ThrowIfFailure(); return; }
#endif
                Managed.TryRebuild().ThrowIfFailure();
            }
            internal void Capture()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Native.TryCapture(out nativeSnapshot).ThrowIfFailure(); return; }
#endif
                Managed.TryCapture(out snapshot).ThrowIfFailure();
            }
            internal void Restore()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Native.TryRestore(nativeSnapshot).ThrowIfFailure(); return; }
#endif
                Managed.TryRestore(snapshot).ThrowIfFailure();
            }
            internal void CheckGrid(int overflow)
            {
                Assert.AreEqual(24, sizeof(SpatialGridLink));
#if !FIXEDPOINT_STANDALONE
                if (native) { VerifyGrid<int, int, TBounds, NativeSpatialStorage<int, int, TBounds, TAccess, Int32Policy>>(ref Native.Storage, overflow); return; }
#endif
                VerifyGrid<int, int, TBounds, ManagedSpatialStorage<int, int, TBounds, TAccess, Int32Policy>>(ref Managed.Storage, overflow);
            }
            internal void CheckTree()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { VerifyTree<int, int, TBounds, NativeSpatialStorage<int, int, TBounds, TAccess, Int32Policy>>(ref Native.Storage, default(TAccess).Dimensions); return; }
#endif
                VerifyTree<int, int, TBounds, ManagedSpatialStorage<int, int, TBounds, TAccess, Int32Policy>>(ref Managed.Storage, default(TAccess).Dimensions);
            }
            internal void QueryEnvelope(long x, long width, params int[] expected)
            {
                TBounds b = Box(x, width); default(TAccess).TryRead(b, out var raw).ThrowIfFailure();
                var query = SpatialQuery.FromEnvelope(raw, default(TAccess).Dimensions);
                SpatialQueryScratch.TryMeasure<int>(Options.Records.MaxCapacity, Options.Records.MaxCapacity * 2, out int bytes).ThrowIfFailure();
                byte* work = stackalloc byte[bytes]; SpatialHit<int>* output = stackalloc SpatialHit<int>[Options.Records.MaxCapacity];
                int count;
#if !FIXEDPOINT_STANDALONE
                if (native) Native.TryQuery(query, new SpatialQueryScratch(work, bytes), output, Options.Records.MaxCapacity, out count).ThrowIfFailure(); else
#endif
                Managed.TryQuery(query, new SpatialQueryScratch(work, bytes), output, Options.Records.MaxCapacity, out count).ThrowIfFailure();
                Assert.AreEqual(expected.Length, count);
                for (int i = 0; i < count; ++i) Assert.AreEqual(expected[i], output[i].Key);
            }
            internal byte[] Canonical()
            {
                var packet = new byte[65536]; int count;
                fixed (byte* p = packet)
                {
                    var writer = new DeterministicWriter(p, packet.Length);
#if !FIXEDPOINT_STANDALONE
                    if (native) Native.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure(); else
#endif
                    Managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    count = writer.Position;
                }
                Array.Resize(ref packet, count); return packet;
            }
            internal void Import(byte[] packet)
            {
                byte* work = stackalloc byte[65536];
                fixed (byte* p = packet)
                {
                    var reader = new DeterministicReader(p, packet.Length); var arena = new CanonicalScratch(work, 65536);
#if !FIXEDPOINT_STANDALONE
                    if (native) Native.TryReadCanonical(ref reader, ref arena, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure(); else
#endif
                    Managed.TryReadCanonical(ref reader, ref arena, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    Assert.AreEqual(packet.Length, reader.Position);
                }
            }
            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { nativeSnapshot.Dispose(); Native.Dispose(); return; }
#endif
                snapshot.Dispose(); Managed.Dispose();
            }
        }

        private static void VerifyGrid<TKey, TData, TBounds, TStorage>(ref TStorage store, int expectedOverflow)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
        {
            var live = new HashSet<int>(); int previous = -1, overflow = 0;
            for (int link = store.State.OverflowHead; link >= 0;)
            {
                Assert.IsTrue(live.Add(link), "Overflow list has a cycle or shared link");
                var node = store.ReadLink(link); Assert.AreEqual(previous, node.PreviousCell); Assert.AreEqual(-1, node.CellSlot);
                Assert.AreNotEqual(0, store.ReadEntry(node.RecordSlot).Active);
                var placement = store.ReadPlacement(node.RecordSlot); Assert.IsTrue(placement.Overflow); Assert.AreEqual(link, placement.GridHead);
                previous = link; link = node.NextCell; ++overflow;
            }
            Assert.AreEqual(expectedOverflow, overflow); Assert.AreEqual(overflow, store.State.OverflowCount);
            for (int cell = 0; cell < store.State.Cells.Used; ++cell)
            {
                var entry = store.ReadCell(cell); if (entry.Active == 0) continue;
                previous = -1;
                for (int link = entry.Value; link >= 0;)
                {
                    Assert.IsTrue(live.Add(link), "Cell lists have a cycle or shared link");
                    var node = store.ReadLink(link); Assert.AreEqual(cell, node.CellSlot); Assert.AreEqual(previous, node.PreviousCell);
                    Assert.AreNotEqual(0, store.ReadEntry(node.RecordSlot).Active); Assert.IsFalse(store.ReadPlacement(node.RecordSlot).Overflow);
                    previous = link; link = node.NextCell;
                }
            }
            int freeCount = 0;
            for (int link = store.State.FreeLink; link >= 0; link = store.ReadLink(link).NextFree)
            {
                Assert.IsTrue(live.Add(link), "Live and free lists share a link");
                Assert.LessOrEqual(++freeCount, store.LinkCapacity);
            }
            Assert.AreEqual(store.State.LinkUsed, live.Count, "Every allocated link is reachable or free");
        }

        private static void VerifyTree<TKey, TData, TBounds, TStorage>(ref TStorage store, int dimensions)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
        {
            var visited = new HashSet<int>(); var todo = new Stack<int>(); int leaves = 0;
            if (store.State.Root >= 0) todo.Push(store.State.Root);
            while (todo.Count != 0)
            {
                int id = todo.Pop(); Assert.IsTrue(visited.Add(id)); var node = store.ReadNode(id);
                if (id == store.State.Root) Assert.AreEqual(-1, node.Parent);
                if (node.IsLeaf)
                {
                    ++leaves; Assert.AreNotEqual(0, store.ReadEntry(node.RecordSlot).Active);
                    var placement = store.ReadPlacement(node.RecordSlot); Assert.AreEqual(id, placement.Leaf);
                    Assert.IsTrue(node.Bounds.Contains(placement.Bounds, dimensions)); Assert.AreEqual(0, node.Height);
                }
                else
                {
                    var left = store.ReadNode(node.Left); var right = store.ReadNode(node.Right);
                    Assert.AreEqual(id, left.Parent); Assert.AreEqual(id, right.Parent);
                    Assert.IsTrue(node.Bounds.Contains(left.Bounds, dimensions)); Assert.IsTrue(node.Bounds.Contains(right.Bounds, dimensions));
                    Assert.AreEqual(1 + Math.Max(left.Height, right.Height), node.Height);
                    todo.Push(node.Right); todo.Push(node.Left);
                }
            }
            Assert.AreEqual(store.State.Records.Count, leaves);
        }

        private static SpatialIndexOptions Options(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback, int capacity = 64)
            => new SpatialIndexOptions(new BufferOptions(capacity, snapshotSlots: 2, pageBytes: 256), strategy, rollback: rollback,
                cellSize: Fixed64.FromRaw(16), fatMargin: Fixed64.FromRaw(8), maxCellsPerObject: 4, maxCellsPerQuery: 1);

        private static void Grid<TBounds, TAccess, TCodec>(SpatialRollbackPolicy rollback, bool native)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            using var p = new Probe<TBounds, TAccess, TCodec>(Options(SpatialIndexStrategy.SparseGrid, rollback), native);
            for (int i = 0; i < 48; ++i) p.Add(i, i * 32L); // Fill and reuse cell slots, not just record slots.
            for (int i = 0; i < 48; i += 2) p.Remove(i);
            for (int i = 48; i < 64; ++i) p.Add(i, -i * 32L);
            p.Move(1, 0, 160); p.Move(3, 256, 160); p.Move(5, 512, 160); p.CheckGrid(3);
            p.Remove(3); p.CheckGrid(2); // Middle of overflow list.
            p.Move(5, 2048); p.CheckGrid(1); // Head becomes a regular object.
            p.Remove(1); p.CheckGrid(0); // Tail and final overflow object.
            p.Add(100, 0, 160); p.Add(101, 256, 160); p.Add(102, 512, 160); p.CheckGrid(3);
            p.Rebuild(); p.CheckGrid(3); p.CheckTree(); p.Capture(); byte[] saved = p.Canonical();
            Assert.AreEqual(DeterministicStatus.NotFound, p.Batch(
                new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Remove, Key = 101 },
                new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.UpdateBounds, Key = 100, Bounds = Probe<TBounds, TAccess, TCodec>.Box(4096) },
                new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Remove, Key = int.MaxValue }));
            CollectionAssert.AreEqual(saved, p.Canonical()); p.CheckGrid(3); p.CheckTree();
            p.Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Clear }).ThrowIfFailure(); p.CheckGrid(0);
            p.Add(999, 0, 160); p.CheckGrid(1); p.Restore(); p.CheckGrid(3); p.CheckTree();
            CollectionAssert.AreEqual(saved, p.Canonical()); p.QueryEnvelope(0, 4, 100);
            p.Import(saved); p.CheckGrid(3); p.CheckTree(); CollectionAssert.AreEqual(saved, p.Canonical());
            p.Remove(100); p.Remove(101); p.Remove(102); p.CheckGrid(0);
        }

        private static void Trees<TBounds, TAccess, TCodec>(SpatialIndexStrategy strategy, bool native)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            using var p = new Probe<TBounds, TAccess, TCodec>(Options(strategy, SpatialRollbackPolicy.FastRestore), native);
            for (int i = 0; i < 48; ++i) p.Add(i, i * 32L);
            p.Rebuild(); p.CheckTree(); p.Capture(); byte[] saved = p.Canonical();
            for (int i = 0; i < 48; i += 3) p.Remove(i);
            for (int i = 1; i < 48; i += 3) p.Move(i, -i * 64L);
            for (int i = 48; i < 64; ++i) p.Add(i, i * 64L);
            p.CheckTree(); p.QueryEnvelope(-64, 4, 1); p.Rebuild(); p.CheckTree();
            p.Restore(); p.CheckTree(); CollectionAssert.AreEqual(saved, p.Canonical());
            p.Move(1, 34); p.CheckTree(); p.QueryEnvelope(34, 2, 1); // Contained in a bulk-built fat leaf.
            p.Move(1, -1024); p.CheckTree(); p.QueryEnvelope(-1024, 4, 1);
        }

        [TestCase(SpatialRollbackPolicy.Compact, false)] [TestCase(SpatialRollbackPolicy.FastRestore, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SpatialRollbackPolicy.Compact, true)] [TestCase(SpatialRollbackPolicy.FastRestore, true)]
#endif
        public void CompactLinksSurviveCellReuseOverflowTransitionsAndAtomicRestore(SpatialRollbackPolicy rollback, bool native)
        {
            Grid<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(rollback, native);
            Grid<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(rollback, native);
        }

        [TestCase(SpatialIndexStrategy.DynamicBvh, false)] [TestCase(SpatialIndexStrategy.SparseGrid, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SpatialIndexStrategy.DynamicBvh, true)] [TestCase(SpatialIndexStrategy.SparseGrid, true)]
#endif
        public void BulkBuiltTreesRemainMutableAndKeepConservativeFatLeaves(SpatialIndexStrategy strategy, bool native)
        {
            Trees<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(strategy, native);
            Trees<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(strategy, native);
        }

        private static void Layouts<TBounds, TAccess, TCodec>(bool native)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            const int count = 128; var keys = new int[count]; for (int i = 0; i < count; ++i) keys[i] = i;
            foreach (SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
                for (int pattern = 0; pattern < 4; ++pattern)
                {
                    using var p = new Probe<TBounds, TAccess, TCodec>(Options(strategy, SpatialRollbackPolicy.Compact, count), native);
                    for (int i = 0; i < count; ++i)
                    {
                        long x = pattern == 0 ? (count - 1 - i) * 32L : pattern == 1 ? 0 :
                            pattern == 2 ? Math.Min(i, count - 1 - i) * 32L : (i % 2 == 0 ? -1 : 1) * (i / 2) * 32L;
                        p.Add(i, x);
                    }
                    byte[] before = p.Canonical(); p.Rebuild(); p.CheckTree();
                    p.QueryEnvelope(-4096, 8192, keys); CollectionAssert.AreEqual(before, p.Canonical());
                    p.Import(before); p.CheckTree(); p.QueryEnvelope(-4096, 8192, keys);
                }
        }

        [TestCase(false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(true)]
#endif
        public void BulkPartitionHandlesReverseCoincidentOrganPipeAndAlternatingLayouts(bool native)
        {
            Layouts<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(native);
            Layouts<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(native);
        }
    }
}
