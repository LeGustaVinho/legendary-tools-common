// Generated using python-xxhash 3.6.0, independent of the C# implementation.
// Specification: https://github.com/Cyan4973/xxHash/blob/dev/doc/xxhash_spec.md
using NUnit.Framework;
namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class Xxh64OracleTests
    {
        private readonly struct Vector
        {
            internal readonly int Length;
            internal readonly ulong Seed, Expected;
            internal Vector(int length, ulong seed, ulong expected) { Length = length; Seed = seed; Expected = expected; }
        }
        private static readonly Vector[] Vectors = {
            new Vector(0, 0x0000000000000000UL, 0xef46db3751d8e999UL),
            new Vector(0, 0x0123456789abcdefUL, 0x51e24c0e9077a48cUL),
            new Vector(0, 0xffffffffffffffffUL, 0x298f4c84b24f5380UL),
            new Vector(1, 0x0000000000000000UL, 0xad10cd9780ac4ff7UL),
            new Vector(1, 0x0123456789abcdefUL, 0x7d63f86c79b8f4b3UL),
            new Vector(1, 0xffffffffffffffffUL, 0xdc2e21ee587419c3UL),
            new Vector(2, 0x0000000000000000UL, 0xb22635899e8f2235UL),
            new Vector(2, 0x0123456789abcdefUL, 0x8c5232f990153994UL),
            new Vector(2, 0xffffffffffffffffUL, 0xee7ff1e2e48e000fUL),
            new Vector(3, 0x0000000000000000UL, 0x40626d96276e4594UL),
            new Vector(3, 0x0123456789abcdefUL, 0xc14a8494c16cdef5UL),
            new Vector(3, 0xffffffffffffffffUL, 0x208c3f6f49d52785UL),
            new Vector(4, 0x0000000000000000UL, 0x882207c122c76e23UL),
            new Vector(4, 0x0123456789abcdefUL, 0x415d3b0a1c06ead8UL),
            new Vector(4, 0xffffffffffffffffUL, 0xa3ca1715ebc4e113UL),
            new Vector(7, 0x0000000000000000UL, 0x43726945ca9c2a6cUL),
            new Vector(7, 0x0123456789abcdefUL, 0x0ebb21bf3f6b83dfUL),
            new Vector(7, 0xffffffffffffffffUL, 0x4f4fc108d80cff13UL),
            new Vector(8, 0x0000000000000000UL, 0xccbe188cb8bc88ddUL),
            new Vector(8, 0x0123456789abcdefUL, 0x60aeed147301dc13UL),
            new Vector(8, 0xffffffffffffffffUL, 0x79a847f56b7a8f9fUL),
            new Vector(9, 0x0000000000000000UL, 0xed3fb54e2069fd2fUL),
            new Vector(9, 0x0123456789abcdefUL, 0x4cd22709879d73f6UL),
            new Vector(9, 0xffffffffffffffffUL, 0x6031ec9cee9fe02aUL),
            new Vector(15, 0x0000000000000000UL, 0x671bd1c3c893eb7aUL),
            new Vector(15, 0x0123456789abcdefUL, 0x06318a7475488705UL),
            new Vector(15, 0xffffffffffffffffUL, 0x4b14a45c987ebd72UL),
            new Vector(16, 0x0000000000000000UL, 0xf9482751d02e18f3UL),
            new Vector(16, 0x0123456789abcdefUL, 0xa12a35a15b5949f8UL),
            new Vector(16, 0xffffffffffffffffUL, 0xc12d287d0583a659UL),
            new Vector(17, 0x0000000000000000UL, 0xd437b3d2c45a0779UL),
            new Vector(17, 0x0123456789abcdefUL, 0x327143811a5a28d8UL),
            new Vector(17, 0xffffffffffffffffUL, 0xf6310ee9c72becebUL),
            new Vector(31, 0x0000000000000000UL, 0x30c9075dea0ee27aUL),
            new Vector(31, 0x0123456789abcdefUL, 0x8c9bfa5370e1c963UL),
            new Vector(31, 0xffffffffffffffffUL, 0x262b77cb3520a8dcUL),
            new Vector(32, 0x0000000000000000UL, 0xac43fc451c2dad7dUL),
            new Vector(32, 0x0123456789abcdefUL, 0x150775d336a31230UL),
            new Vector(32, 0xffffffffffffffffUL, 0xa97ca35d5d02be9eUL),
            new Vector(33, 0x0000000000000000UL, 0x4d64bbc6f79f66a2UL),
            new Vector(33, 0x0123456789abcdefUL, 0x4c6486b5815a509fUL),
            new Vector(33, 0xffffffffffffffffUL, 0x1a86343f70c7abbfUL),
            new Vector(63, 0x0000000000000000UL, 0x1ba9cf48fda4100bUL),
            new Vector(63, 0x0123456789abcdefUL, 0x523c157b1f25cef8UL),
            new Vector(63, 0xffffffffffffffffUL, 0x47cfb8e49cdde78eUL),
            new Vector(64, 0x0000000000000000UL, 0x0dc0245663779210UL),
            new Vector(64, 0x0123456789abcdefUL, 0xfb66feb40a9f2e57UL),
            new Vector(64, 0xffffffffffffffffUL, 0x27e3951ae639146eUL),
            new Vector(65, 0x0000000000000000UL, 0x9431eb081e8558b5UL),
            new Vector(65, 0x0123456789abcdefUL, 0xd9e186e06f14401bUL),
            new Vector(65, 0xffffffffffffffffUL, 0xd30e0231c594d75cUL),
            new Vector(127, 0x0000000000000000UL, 0xd5a9ac9c2ca2bc6dUL),
            new Vector(127, 0x0123456789abcdefUL, 0xec0caff37d39d53eUL),
            new Vector(127, 0xffffffffffffffffUL, 0xb158dd6cb5189ea7UL),
            new Vector(128, 0x0000000000000000UL, 0x7ce9016bfcb512b6UL),
            new Vector(128, 0x0123456789abcdefUL, 0x0543e6537f70f315UL),
            new Vector(128, 0xffffffffffffffffUL, 0xaa9285b2fe3e3355UL),
            new Vector(129, 0x0000000000000000UL, 0x8f05d9054fb331ddUL),
            new Vector(129, 0x0123456789abcdefUL, 0xd98cc8a9c07fe0c2UL),
            new Vector(129, 0xffffffffffffffffUL, 0xbffadd6bed38e24bUL),
            new Vector(255, 0x0000000000000000UL, 0xb25dd5af1ba21214UL),
            new Vector(255, 0x0123456789abcdefUL, 0x8f3d4405bd144d3aUL),
            new Vector(255, 0xffffffffffffffffUL, 0xfe9257c2b60f126eUL),
            new Vector(256, 0x0000000000000000UL, 0x21437e6b62ba512bUL),
            new Vector(256, 0x0123456789abcdefUL, 0x2e78ed4dde020868UL),
            new Vector(256, 0xffffffffffffffffUL, 0x3a59401190d09489UL),
            new Vector(257, 0x0000000000000000UL, 0x3e1097a6622e0304UL),
            new Vector(257, 0x0123456789abcdefUL, 0x670f9cda57122ccbUL),
            new Vector(257, 0xffffffffffffffffUL, 0xb1b56fafd0fc897dUL),
            new Vector(512, 0x0000000000000000UL, 0x3ffdc8f632a61e1eUL),
            new Vector(512, 0x0123456789abcdefUL, 0xbbb28f7a14fca8bcUL),
            new Vector(512, 0xffffffffffffffffUL, 0xeb6962d0229aa348UL),
            new Vector(1024, 0x0000000000000000UL, 0x41551ebad464a322UL),
            new Vector(1024, 0x0123456789abcdefUL, 0x808873af34cf18aeUL),
            new Vector(1024, 0xffffffffffffffffUL, 0xba1556df2c9747bbUL),
            new Vector(4096, 0x0000000000000000UL, 0x419f8fe02229c43fUL),
            new Vector(4096, 0x0123456789abcdefUL, 0x55c4648d07ca4d4aUL),
            new Vector(4096, 0xffffffffffffffffUL, 0x37d100205e94fb7aUL),
            new Vector(65536, 0x0000000000000000UL, 0x3f04fe8a8079afdcUL),
            new Vector(65536, 0x0123456789abcdefUL, 0x9d9164d51cd573d9UL),
            new Vector(65536, 0xffffffffffffffffUL, 0x098c67515780f472UL),
        };
        [Test]
        public void IndependentVectorsCoverLongInputsSeedsAndUnalignedBuffers()
        {
            byte* buffer = stackalloc byte[65540];
            for (int offset = 0; offset < 4; offset++)
            {
                for (int i = 0; i < 65536; i++) buffer[offset + i] = (byte)(i * 131 + (i >> 2) + 17);
                foreach (var vector in Vectors)
                    Assert.AreEqual(vector.Expected, DeterministicHash.Xxh64(buffer + offset, vector.Length, vector.Seed),
                        "length=" + vector.Length + ", offset=" + offset + ", seed=" + vector.Seed);
            }
        }
    }
}
