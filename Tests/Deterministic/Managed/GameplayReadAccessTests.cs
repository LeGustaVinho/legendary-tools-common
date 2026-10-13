using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public class GameplayReadAccessTests
    {
        [Test]
        public void LogicalSlotReadsRespectHolesGenerationsAndUsedBounds()
        {
            using var slots = new DeterministicSlotMap<int>(8);
            var first = slots.Add(10); var second = slots.Add(20);
            slots.TryRemove(first).ThrowIfFailure();
            Assert.That(slots.UsedSlotCount, Is.EqualTo(2));
            Assert.That(slots.TryGetSlot(0, out _), Is.EqualTo(DeterministicStatus.NotFound));
            slots.TryGetSlot(1, out var item).ThrowIfFailure(); Assert.That(item.Handle, Is.EqualTo(second));
            Assert.That(slots.TryGetSlot(2, out _), Is.EqualTo(DeterministicStatus.OutOfRange));
            Assert.That(slots.TryGetSlot(-1, out _), Is.EqualTo(DeterministicStatus.OutOfRange));
            var reused = slots.Add(30); slots.TryGetSlot(0, out item).ThrowIfFailure();
            Assert.That(item.Handle, Is.EqualTo(reused)); Assert.That(item.Value, Is.EqualTo(30));
        }
        [Test]
        public void DenseReadsReturnPhysicalEntriesAndRejectOutOfRangeOrDisposedAccess()
        {
            using var components = new DeterministicSparseSet<int>(4, 8);
            var a = new StableHandle(1, 1); var b = new StableHandle(7, 2);
            components.TryAdd(a, 10).ThrowIfFailure(); components.TryAdd(b, 20).ThrowIfFailure();
            components.TryRemove(a).ThrowIfFailure(); components.TryGetDenseAt(0, out var row).ThrowIfFailure();
            Assert.That(row.Handle, Is.EqualTo(b)); Assert.That(row.Value, Is.EqualTo(20));
            Assert.That(components.TryGetDenseAt(1, out _), Is.EqualTo(DeterministicStatus.OutOfRange));
            components.TryGetSlot(7, out row).ThrowIfFailure(); Assert.That(row.Handle, Is.EqualTo(b));
            Assert.That(components.TryGetSlot(1, out _), Is.EqualTo(DeterministicStatus.NotFound));
            Assert.That(components.TryGetSlot(-1, out _), Is.EqualTo(DeterministicStatus.OutOfRange));
            Assert.That(components.TryGetSlot(8, out _), Is.EqualTo(DeterministicStatus.OutOfRange));
            components.Dispose(); Assert.That(components.TryGetDenseAt(0, out _), Is.EqualTo(DeterministicStatus.Disposed));
            Assert.That(components.TryGetSlot(7, out _), Is.EqualTo(DeterministicStatus.Disposed));
        }
    }
}
