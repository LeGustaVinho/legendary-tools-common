using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialLargeGridRestoreTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [TestCase(SpatialRollbackPolicy.Compact)] [TestCase(SpatialRollbackPolicy.FastRestore)]
        public void LargeSparseCellTableKeepsMetadataMatchedToItsBuffersDuringRestore(SpatialRollbackPolicy rollback)
        {
            const int count = 1000;
            using var index = new DeterministicSpatialIndex2<int, int, Int32Policy>(new SpatialIndexOptions(new BufferOptions(count, snapshotSlots: 2),
                SpatialIndexStrategy.SparseGrid, rollback: rollback, cellSize: Fixed64.FromRaw(64), maxCellsPerObject: 8));
            for (int i = 0; i < count; i++) index.TryAdd(i, FixedBounds2.FromMinMax(FixedVector2.FromRaw(i * 128, i * 64), FixedVector2.FromRaw(i * 128 + 16, i * 64 + 16)), i).ThrowIfFailure();
            index.TryCapture(out var snapshot).ThrowIfFailure();
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); var workspace = new byte[bytes]; var hits = new SpatialHit<int>[count];
            try
            {
                fixed (byte* work = workspace) fixed (SpatialHit<int>* output = hits)
                for (int round = 0; round < 3; round++)
                {
                    for (int i = 0; i < count / 10; i++) index.TryUpdateBounds(i, FixedBounds2.FromMinMax(FixedVector2.FromRaw(-i * 128 - 200, -i * 64 - 200), FixedVector2.FromRaw(-i * 128 - 184, -i * 64 - 184))).ThrowIfFailure();
                    index.TryRestore(snapshot).ThrowIfFailure();
                    var visited = new HashSet<int>(); int next = index.Storage.State.Cells.OrderHead;
                    while (next != 0) { Assert.IsTrue(visited.Add(next)); var cell = index.Storage.ReadCell(next - 1); Assert.AreEqual(1, cell.Active); Assert.AreNotEqual(next, cell.BucketNext); next = cell.OrderNext; }
                    Assert.AreEqual(index.Storage.State.Cells.Count, visited.Count);
                    for (int i = 0; i < count; i += 13)
                    {
                        index.TryQuery(SpatialQuery.ForPoint(FixedVector2.FromRaw(i * 128, i * 64)), new SpatialQueryScratch(work, bytes), output, hits.Length, out int n).ThrowIfFailure();
                        Assert.AreEqual(1, n); Assert.AreEqual(i, hits[0].Key);
                    }
                }
            }
            finally { snapshot.Dispose(); }
        }
    }
}
