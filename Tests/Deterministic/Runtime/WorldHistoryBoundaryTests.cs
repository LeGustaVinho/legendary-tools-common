using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class WorldHistoryBoundaryTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CanonicalCheckpointAtTheWrongBoundaryDoesNotPublishWorldState(SnapshotPolicy policy)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            using (var value = new DeterministicCell<int>())
            using (var state = new DeterministicStateRegistry(context, new DeterministicCompatibilityProfile(1, 0, 0, 2, 3), 1))
            using (var history = new DeterministicWorldHistory(context, 512, 8, policy, 4, 64))
            {
                state.TryRegister(ManagedStateBindings.Cell(1, value, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); state.TryFreeze().ThrowIfFailure();
                value.Value = 41; state.TryEncode(7, out byte* bytes, out int length).ThrowIfFailure(); history.TryCapture(0, bytes, length).ThrowIfFailure();
                value.Value = 99; Assert.AreEqual(DeterministicStatus.ProtocolConflict, history.TryRestore(state, 0)); Assert.AreEqual(99, value.Value);
            }
        }
    }
}
