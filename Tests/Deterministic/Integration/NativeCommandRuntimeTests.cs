using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeCommandRuntimeTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct ProducerJob : IJob
        {
            internal NativeDeterministicCommandWriter<int> Writer;
            internal NativeArray<int> Result;
            internal int Offset;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                for (int i = 0; i < 12; i++) Result[1] = (int)Writer.TryEnqueue((ushort)(i % 3), 1, new StableHandle((uint)(11 - i), 1), i + Offset);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void IndependentBurstProducersMergeAfterJobsComplete()
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            using (var buffer = new NativeDeterministicCommandBuffer<int>(context, new uint[] { 9, 2 }, 12))
            using (var a = new NativeArray<int>(2, Allocator.TempJob)) using (var b = new NativeArray<int>(2, Allocator.TempJob))
            {
                buffer.TryGetWriter(9, out var first).ThrowIfFailure(); buffer.TryGetWriter(2, out var second).ThrowIfFailure();
                var left = new ProducerJob { Writer = first, Result = a, Offset = 100 }.Schedule();
                var right = new ProducerJob { Writer = second, Result = b, Offset = 200 }.Schedule();
                JobHandle.CombineDependencies(left, right).Complete(); Assert.AreEqual(0, a[0]); Assert.AreEqual(0, b[0]); Assert.AreEqual(0, a[1]); Assert.AreEqual(0, b[1]);
                buffer.TryMerge(out var commands).ThrowIfFailure(); Assert.AreEqual(24, commands.Length);
                for (int i = 1; i < commands.Length; i++) Assert.Less(commands[i - 1].Header.CompareTo(commands[i].Header), 0);
                buffer.TryReset().ThrowIfFailure(); buffer.TryMerge(out commands).ThrowIfFailure(); Assert.AreEqual(0, commands.Length);
            }
        }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void OutstandingProducerPreventsResetAndDisposedOwnerInvalidatesWriter()
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            using (var buffer = new NativeDeterministicCommandBuffer<int>(context, new uint[] { 1 }, 12))
            using (var result = new NativeArray<int>(2, Allocator.TempJob))
            {
                buffer.TryGetWriter(1, out var writer).ThrowIfFailure(); var handle = new ProducerJob { Writer = writer, Result = result }.Schedule();
                try { Assert.That(() => buffer.TryReset(), Throws.InstanceOf<System.InvalidOperationException>()); }
                finally { handle.Complete(); }
                buffer.Dispose(); Assert.That(() => writer.TryEnqueue(0, 1, default, 3), Throws.InstanceOf<System.InvalidOperationException>());
            }
        }
#endif
    }
}
