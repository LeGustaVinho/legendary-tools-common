using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class CanonicalBoundaryTests
    {
        [Test]
        public void StagingWorkspaceCannotOverwriteInputPacket()
        {
            byte* bytes = stackalloc byte[256]; var codec = default(Int32Policy);
            using (var list = new DeterministicList<int>(4))
            {
                list.Add(7); var writer = new DeterministicWriter(bytes, 256); list.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(bytes + 8, 128);
                Assert.AreEqual(DeterministicStatus.InvalidConfiguration, list.TryReadCanonical(ref reader, ref scratch, codec));
                Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position); Assert.AreEqual(7, list[0]);
            }
        }
        [Test]
        public void UnsupportedEqualityIsRejectedEvenForEmptyLists()
        {
            using (var list = new DeterministicList<float>(4))
            {
                Assert.AreEqual(DeterministicStatus.UnsupportedType, list.TryIndexOf(1f, default(PrimitiveEquality<float>), out int index)); Assert.AreEqual(-1, index);
            }
        }
    }
}
