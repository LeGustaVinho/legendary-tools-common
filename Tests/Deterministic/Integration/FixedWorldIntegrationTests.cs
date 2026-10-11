using NUnit.Framework;
using LegendaryTools.FixedPoint;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedWorldIntegrationTests
    {
        [SetUp] public void InitializeNumericRuntime() { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); }
        [Test]
        public void NumericKeysNormalizeZerosWhileCheckpointPreservesTheirRawRepresentation()
        {
            var policy = default(Fixed32Policy); Assert.IsTrue(policy.Equals(Fixed32.Zero, Fixed32.NegativeZero));
            Assert.AreEqual(policy.Hash(Fixed32.Zero), policy.Hash(Fixed32.NegativeZero));
            using (var map = new DeterministicDictionary<Fixed32, int, Fixed32Policy>(4))
            {
                map.Add(Fixed32.NegativeZero, 1); Assert.AreEqual(DeterministicStatus.DuplicateKey, map.TryAdd(Fixed32.Zero, 2));
                map[Fixed32.Zero] = 8; foreach (var pair in map) Assert.AreEqual(Fixed32.NegativeZero.RawValue, pair.Key.RawValue);
            }
            byte* bytes = stackalloc byte[8]; var writer = new DeterministicWriter(bytes, 8);
            policy.TryWrite(ref writer, Fixed32.NegativeZero).ThrowIfFailure(); var reader = new DeterministicReader(bytes, writer.Position);
            policy.TryRead(ref reader, out Fixed32 restored).ThrowIfFailure(); Assert.AreEqual(Fixed32.NegativeZero.RawValue, restored.RawValue);
            var unsigned = default(UFixed64Policy); writer = new DeterministicWriter(bytes, 8); unsigned.TryWrite(ref writer, UFixed64.FromRaw(ulong.MaxValue)).ThrowIfFailure();
            reader = new DeterministicReader(bytes, 8); unsigned.TryRead(ref reader, out UFixed64 wide).ThrowIfFailure(); Assert.AreEqual(ulong.MaxValue, wide.RawValue);
        }
        [Test]
        public void RandomCheckpointRestoresNextWordsAcrossEveryCachePosition()
        {
            byte* bytes = stackalloc byte[FixedRandomState.Bytes]; var codec = default(FixedRandomStateCodec); var random = FixedRandom.Create(123, 89);
            for (int position = 0; position < 19; position++)
            {
                var state = random.CaptureState(); var writer = new DeterministicWriter(bytes, FixedRandomState.Bytes); codec.TryWrite(ref writer, state).ThrowIfFailure();
                var reader = new DeterministicReader(bytes, FixedRandomState.Bytes); codec.TryRead(ref reader, out var decoded).ThrowIfFailure();
                var copy = default(FixedRandom); copy.RestoreState(decoded); var oracle = random;
                for (int word = 0; word < 11; word++) Assert.AreEqual(oracle.NextUInt32(), copy.NextUInt32()); random.NextUInt32();
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct WorldJob : IJob
        {
            internal NativeDeterministicList<Fixed32> Values;
            internal NativeDeterministicCell<FixedRandomState> Random;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Random.TryGet(0, out var state); var random = default(FixedRandom); random.RestoreState(state);
                uint value = random.NextUInt32(); Result[1] = unchecked((int)value);
                Values.TryAdd(Fixed32.FromRaw((int)(value % 10000))); Random.TrySet(0, random.CaptureState());
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void NativeWorldMatchesManagedCheckpointAndRollbackRestoresFutureRandom(SnapshotPolicy snapshots)
        {
            FixedDeterministicProfile.TryFingerprint(out ulong numeric).ThrowIfFailure();
            var profile = new DeterministicCompatibilityProfile(1, numeric, FixedRandom.AlgorithmId, 4, 5);
            var managedContext = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var nativeContext = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            var initial = FixedRandom.Create(123, 456).CaptureState();
            using (var managedValues = new DeterministicList<Fixed32>(8)) using (var managedRandom = new DeterministicCell<FixedRandomState>(initial))
            using (var nativeValues = new NativeDeterministicList<Fixed32>(8, Allocator.Persistent))
            using (var nativeRandom = new NativeDeterministicCell<FixedRandomState>(new BufferOptions(1), Allocator.Persistent))
            using (var result = new NativeArray<int>(2, Allocator.TempJob))
            using (var managed = new DeterministicStateRegistry(managedContext, profile, 2))
            using (var native = new DeterministicStateRegistry(nativeContext, profile, 2))
            {
                nativeRandom.TrySet(0, initial).ThrowIfFailure();
                managed.TryRegister(ManagedStateBindings.List(1, managedValues, default(Fixed32Policy), 8, 512, 1024)).ThrowIfFailure();
                managed.TryRegister(ManagedStateBindings.Cell(2, managedRandom, default(FixedRandomStateCodec), 1, 128, 128)).ThrowIfFailure();
                native.TryRegister(NativeStateBindings.List(1, nativeValues, default(Fixed32Policy), 8, 512, 1024)).ThrowIfFailure();
                native.TryRegister(NativeStateBindings.Cell(2, nativeRandom, default(FixedRandomStateCodec), 1, 128, 128)).ThrowIfFailure();
                managed.TryFreeze().ThrowIfFailure(); native.TryFreeze().ThrowIfFailure();
                using (var history = new DeterministicWorldHistory(nativeContext, native.MaxCanonicalBytes, 8, snapshots, 4, 64))
                {
                    history.TryCapture(native, 0).ThrowIfFailure();
                    for (int tick = 1; tick <= 5; tick++)
                    {
                        new WorldJob { Values = nativeValues, Random = nativeRandom, Result = result }.Schedule().Complete(); Assert.AreEqual(0, result[0]);
                        var rng = default(FixedRandom); rng.RestoreState(managedRandom.Value); uint word = rng.NextUInt32();
                        managedValues.Add(Fixed32.FromRaw((int)(word % 10000))); managedRandom.Value = rng.CaptureState();
                        managed.TryEncode(tick, out byte* a, out int aLength).ThrowIfFailure(); native.TryEncode(tick, out byte* b, out int bLength).ThrowIfFailure();
                        Assert.AreEqual(aLength, bLength); for (int i = 0; i < aLength; i++) Assert.AreEqual(a[i], b[i]); history.TryCapture(native, tick).ThrowIfFailure();
                    }
                    int expectedWord = result[1]; history.TryRestore(native, 4).ThrowIfFailure();
                    new WorldJob { Values = nativeValues, Random = nativeRandom, Result = result }.Schedule().Complete();
                    Assert.AreEqual(0, result[0]); Assert.AreEqual(expectedWord, result[1]); Assert.AreEqual(5, nativeValues.Count);
                }
            }
        }
    }
}
