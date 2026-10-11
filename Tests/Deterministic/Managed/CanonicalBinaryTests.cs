using System;
using System.Text;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class CanonicalBinaryTests
    {
        [TestCase("", 0xef46db3751d8e999UL)]
        [TestCase("a", 0xd24ec4f1a98c6e5bUL)]
        [TestCase("abc", 0x44bc2cf5ad770999UL)]
        public void Xxh64MatchesPublishedVectors(string text, ulong expected)
        { var bytes = Encoding.ASCII.GetBytes(text); fixed (byte* p = bytes) Assert.AreEqual(expected, DeterministicHash.Xxh64(p, bytes.Length)); }

        [Test]
        public void IntegerCodecRoundTripsBoundaryValuesAndUsesLittleEndian()
        {
            var bytes = new byte[128]; var policy = new Int32Policy();
            fixed (byte* p = bytes)
            {
                var writer = new DeterministicWriter(p, bytes.Length);
                foreach (int value in new[] { int.MinValue, -64, -1, 0, 1, 63, 64, int.MaxValue }) policy.TryWrite(ref writer, value).ThrowIfFailure();
                var reader = new DeterministicReader(p, writer.Position);
                foreach (int value in new[] { int.MinValue, -64, -1, 0, 1, 63, 64, int.MaxValue })
                { policy.TryRead(ref reader, out int decoded).ThrowIfFailure(); Assert.AreEqual(value, decoded); }
                Assert.AreEqual(writer.Position, reader.Position);
                writer = new DeterministicWriter(p, 8); writer.TryUInt(0x0102030405060708UL, 8).ThrowIfFailure();
                CollectionAssert.AreEqual(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, new ArraySegment<byte>(bytes, 0, 8));
            }
        }

        [Test]
        public void MalformedAndInsufficientBuffersPreserveCursorsAndDestination()
        {
            var bytes = new byte[] { 0xaa, 0xbb, 0xcc };
            fixed (byte* p = bytes)
            {
                var writer = new DeterministicWriter(p, 3, 2);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, writer.TryVarUInt(128));
                Assert.AreEqual(2, writer.Position); CollectionAssert.AreEqual(new byte[] { 0xaa, 0xbb, 0xcc }, bytes);
                var reader = new DeterministicReader(p, 1);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, reader.TryVarUInt(out _)); Assert.AreEqual(0, reader.Position);
            }
            bytes = new byte[] { 0x80, 0x00 };
            fixed (byte* p = bytes)
            {
                var reader = new DeterministicReader(p, 2);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, reader.TryVarUInt(out _)); Assert.AreEqual(0, reader.Position);
            }
            bytes = new byte[10]; for (int i = 0; i < 9; i++) bytes[i] = 255; bytes[9] = 2;
            fixed (byte* p = bytes)
            {
                var reader = new DeterministicReader(p, 10);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, reader.TryVarUInt(out _)); Assert.AreEqual(0, reader.Position);
            }
        }

        [Test]
        public void NarrowIntegerDecoderRejectsOverflowWithoutConsumingInput()
        {
            byte* bytes = stackalloc byte[10]; var writer = new DeterministicWriter(bytes, 10);
            writer.TryVarInt((long)int.MaxValue + 1).ThrowIfFailure(); var reader = new DeterministicReader(bytes, writer.Position);
            Assert.AreEqual(DeterministicStatus.InvalidEncoding, new Int32Policy().TryRead(ref reader, out _)); Assert.AreEqual(0, reader.Position);
        }
    }
}
