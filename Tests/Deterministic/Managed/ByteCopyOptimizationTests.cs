using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicRuntimeOptimization")]
    public sealed unsafe class ByteCopyOptimizationTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(31)] [TestCase(32)] [TestCase(33)] [TestCase(256)] [TestCase(65536)]
        public void CopyPreservesEveryByteAcrossOverlapAlignmentAndBlockThreshold(int length)
        {
            foreach (int delta in new[] { -17, -1, 0, 1, 17, length + 13 })
            foreach (int offset in new[] { 0, 3 })
            {
                int source = 64 + length + offset, destination = source + delta; var bytes = new byte[length * 3 + 256];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 17 + 3);
                var expected = (byte[])bytes.Clone(); var original = (byte[])bytes.Clone();
                for (int i = 0; i < length; i++) expected[destination + i] = original[source + i];
                fixed (byte* p = bytes)
                {
                    var writer = new DeterministicWriter(p, bytes.Length, destination);
                    writer.TryBytes(p + source, length).ThrowIfFailure(); Assert.AreEqual(destination + length, writer.Position);
                    CollectionAssert.AreEqual(expected, bytes);
                    Array.Copy(original, bytes, bytes.Length);
                    var shortWriter = new DeterministicWriter(p, destination + length - 1, destination);
                    Assert.AreEqual(DeterministicStatus.BufferTooSmall, shortWriter.TryBytes(p + source, length));
                    Assert.AreEqual(destination, shortWriter.Position); CollectionAssert.AreEqual(original, bytes);
                    var measure = DeterministicWriter.CreateMeasure(); measure.TryBytes(p + source, length).ThrowIfFailure(); Assert.AreEqual(length, measure.Position);
                }
            }
        }
        [Test]
        public void InvalidInputAndMeasuringCursorDoNotWriteOrOverflow()
        {
            byte* bytes = stackalloc byte[64]; for (int i = 0; i < 64; i++) bytes[i] = 0x7d;
            var writer = new DeterministicWriter(bytes, 64, 3);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, writer.TryBytes(null, 1));
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, writer.TryBytes(bytes, -1)); Assert.AreEqual(3, writer.Position);
            writer.TryBytes(null, 0).ThrowIfFailure(); Assert.AreEqual(3, writer.Position);
            for (int i = 0; i < 64; i++) Assert.AreEqual(0x7d, bytes[i]);
            var measure = DeterministicWriter.CreateMeasure(); measure.Position = int.MaxValue - 31;
            Assert.AreEqual(DeterministicStatus.BufferTooSmall, measure.TryBytes(bytes, 32)); Assert.AreEqual(int.MaxValue - 31, measure.Position);
        }
    }
}
