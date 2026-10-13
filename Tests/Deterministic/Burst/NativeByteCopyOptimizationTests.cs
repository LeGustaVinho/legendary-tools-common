using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicRuntimeOptimization")]
    public sealed unsafe class NativeByteCopyOptimizationTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct CopyJob : IJob
        {
            internal NativeArray<byte> Buffer;
            internal NativeArray<int> Result;
            internal int Source, Destination, Length, Capacity;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Buffer);
                var writer = new DeterministicWriter(p, Capacity, Destination);
                Result[1] = (int)writer.TryBytes(p + Source, Length); Result[2] = writer.Position;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(0)] [TestCase(1)] [TestCase(31)] [TestCase(32)] [TestCase(33)] [TestCase(256)] [TestCase(65536)]
        public void ByteCopiesAndFailureAtomicityExecuteInsideBurst(int length)
        {
            using var bytes = new NativeArray<byte>(length * 3 + 256, Allocator.Persistent);
            using var result = new NativeArray<int>(3, Allocator.Persistent);
            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(bytes);
            foreach (int delta in new[] { -17, -1, 0, 1, 17, length + 13 })
            foreach (int offset in new[] { 0, 3 })
            {
                int source = 64 + length + offset, destination = source + delta;
                var original = new byte[bytes.Length]; for (int i = 0; i < original.Length; i++) original[i] = p[i] = (byte)(i * 17 + 3);
                var expected = (byte[])original.Clone(); for (int i = 0; i < length; i++) expected[destination + i] = original[source + i];
                var job = new CopyJob { Buffer = bytes, Result = result, Source = source, Destination = destination, Length = length, Capacity = bytes.Length };
                job.Schedule().Complete(); Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(destination + length, result[2]);
                for (int i = 0; i < expected.Length; i++) if (p[i] != expected[i]) Assert.Fail("Burst byte/guard differs at " + i);
                for (int i = 0; i < original.Length; i++) p[i] = original[i];
                job.Capacity = destination + length - 1; job.Schedule().Complete(); Assert.AreEqual(0, result[0]);
                Assert.AreEqual((int)DeterministicStatus.BufferTooSmall, result[1]); Assert.AreEqual(destination, result[2]);
                for (int i = 0; i < original.Length; i++) if (p[i] != original[i]) Assert.Fail("Failed Burst copy wrote destination.");
            }
        }
    }
}
