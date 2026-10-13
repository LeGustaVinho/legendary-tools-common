using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class HotpathOpportunityBenchmarks
    {
        public enum WorldOperation { Encode, Restore }
        private static int WorldValue(int index) => unchecked(index * 104729 ^ 0x31415926);
        private sealed class World<TCodec> : IDisposable where TCodec : unmanaged, IDeterministicCodec<int>
        {
            internal readonly TrackingFactory Factory;
            internal readonly DeterministicRuntimeContext Context;
            internal readonly DeterministicStateRegistry Registry;
            internal readonly DeterministicArray<int>[] Managed;
            internal readonly NativeDeterministicArray<int>[] Native;
            internal readonly int PerState, Values;
            internal World(Backend backend, int count, int bindings)
            {
                Factory = new TrackingFactory(backend); Context = HotpathOpportunityBenchmarks.Context(backend, Factory);
                Values = count; PerState = count / bindings;
                Managed = backend == Backend.Managed ? new DeterministicArray<int>[bindings] : null;
                Native = backend == Backend.NativeHost ? new NativeDeterministicArray<int>[bindings] : null;
                Registry = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), bindings);
                try
                {
                    for (int b = bindings - 1; b >= 0; b--)
                    {
                        int limit = PerState * 5 + 256, scratch = PerState * 24 + 4096;
                        if (Managed != null)
                        {
                            Managed[b] = new DeterministicArray<int>(Options(PerState));
                            for (int i = 0; i < PerState; i++) Managed[b][i] = WorldValue(b * PerState + i);
                            Registry.TryRegister(ManagedStateBindings.Array((uint)b + 1, Managed[b], default(TCodec), 0x1001, limit, scratch)).ThrowIfFailure();
                        }
                        else
                        {
                            Native[b] = new NativeDeterministicArray<int>(Options(PerState), Allocator.Persistent);
                            for (int i = 0; i < PerState; i++) Native[b].TrySet(i, WorldValue(b * PerState + i)).ThrowIfFailure();
                            Registry.TryRegister(NativeStateBindings.Array((uint)b + 1, Native[b], default(TCodec), 0x1001, limit, scratch)).ThrowIfFailure();
                        }
                    }
                    Registry.TryFreeze().ThrowIfFailure();
                }
                catch { Dispose(); throw; }
            }
            internal void Disturb()
            {
                for (int b = 0; b < Values / PerState; b++)
                    if (Managed != null) Managed[b][0] = -999; else Native[b].TrySet(0, -999).ThrowIfFailure();
            }
            internal void ValidateValues()
            {
                for (int b = 0; b < Values / PerState; b++)
                for (int i = 0; i < PerState; i++)
                {
                    int actual = Managed != null ? Managed[b][i] : Native[b][i];
                    if (actual != WorldValue(b * PerState + i)) Assert.Fail("World payload at state " + b + " / index " + i);
                }
            }
            internal GraphMemoryStats MemoryStats
            {
                get
                {
                    GraphMemoryStats sum = default;
                    for (int b = 0; b < Values / PerState; b++) sum += Managed != null ? Managed[b].Storage.Owner.MemoryStats : Native[b].Storage.MemoryStats;
                    return sum;
                }
            }
            internal long NativeMetadataBytes
            {
                get { long sum = 0; if (Native != null) foreach (var state in Native) sum += NativeMetadata(state.Storage); return sum; }
            }
            public void Dispose()
            {
                Registry?.Dispose();
                if (Managed != null) foreach (var state in Managed) state?.Dispose();
                if (Native != null) for (int i = 0; i < Native.Length; i++) if (Native[i].IsCreated) Native[i].Dispose();
            }
        }
        private struct CountingCodec : IDeterministicCodec<int>
        {
            internal static long Measures, Writes, Reads;
            public uint SchemaId => default(Int32Policy).SchemaId;
            public ushort Version => default(Int32Policy).Version;
            public int Measure(in int value) => default(Int32Policy).Measure(value);
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value)
            { if (writer.IsMeasuring) Measures++; else Writes++; return default(Int32Policy).TryWrite(ref writer, value); }
            public DeterministicStatus TryRead(ref DeterministicReader reader, out int value)
            { Reads++; return default(Int32Policy).TryRead(ref reader, out value); }
            internal static void Reset() { Measures = Writes = Reads = 0; }
        }
        private static IEnumerable<TestCaseData> WorldCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int count in new[] { 512, 8192 })
            foreach (int bindings in new[] { 1, 16 })
            {
                yield return new TestCaseData(backend, count, bindings, WorldOperation.Encode, SnapshotPolicy.FullPerTick);
                foreach (SnapshotPolicy policy in Enum.GetValues(typeof(SnapshotPolicy)))
                    yield return new TestCaseData(backend, count, bindings, WorldOperation.Restore, policy);
            }
        }
        [TestCaseSource(nameof(WorldCases)), Performance]
        public void CompositeWorldEncodingAndRollback(Backend backend, int count, int bindings, WorldOperation operation, SnapshotPolicy policy)
        {
            byte[] reference;
            using (var oracle = new World<Int32Policy>(Backend.Managed, count, bindings))
            {
                oracle.Registry.TryEncode(17, out byte* data, out int length).ThrowIfFailure();
                reference = new byte[length]; for (int i = 0; i < length; i++) reference[i] = data[i];
            }
            using (var world = new World<Int32Policy>(backend, count, bindings))
            using (var history = operation == WorldOperation.Restore ? new DeterministicWorldHistory(world.Context, world.Registry.MaxCanonicalBytes, 8, policy, 4, 4096) : null)
            {
                // Frame 16 is the keyframe; frame 17 changes the boundary/header/checksum only.
                if (history != null) { history.TryCapture(world.Registry, 16).ThrowIfFailure(); history.TryCapture(world.Registry, 17).ThrowIfFailure(); }
                IntPtr encoded = IntPtr.Zero; int length = 0; DeterministicStatus status = default;
                long arena = world.Factory.Reserved;
                Benchmark(operation == WorldOperation.Encode ? "H1" : "H2", "World." + operation, backend,
                    bindings + "Bindings_" + (operation == WorldOperation.Encode ? "NoHistory" : policy.ToString()), count, count, reference.Length,
                    () =>
                    {
                        if (operation == WorldOperation.Encode)
                        { status = world.Registry.TryEncode(17, out byte* data, out length); encoded = (IntPtr)data; }
                        else status = history.TryRestore(world.Registry, 17);
                    },
                    () =>
                    {
                        status.ThrowIfFailure(); world.ValidateValues();
                        if (operation == WorldOperation.Encode)
                        {
                            Assert.AreEqual(reference.Length, length); byte* data = (byte*)encoded;
                            for (int i = 0; i < length; i++) if (data[i] != reference[i]) Assert.Fail("Canonical world byte " + i);
                        }
                    },
                    operation == WorldOperation.Restore ? (Action)world.Disturb : null,
                    () => WorldDiagnostics(backend, count, bindings, operation, policy, arena));
            }
        }
        private static Diagnostics WorldDiagnostics(Backend backend, int count, int bindings, WorldOperation operation, SnapshotPolicy policy, long arena)
        {
            // Counting codec is used only in this untimed second corpus. Timed adapters use Int32Policy directly.
            using (var world = new World<CountingCodec>(backend, count, bindings))
            using (var history = operation == WorldOperation.Restore ? new DeterministicWorldHistory(world.Context, world.Registry.MaxCanonicalBytes, 8, policy, 4, 4096) : null)
            {
                if (history != null) { history.TryCapture(world.Registry, 16).ThrowIfFailure(); history.TryCapture(world.Registry, 17).ThrowIfFailure(); }
                if (operation == WorldOperation.Restore) world.Disturb();
                CountingCodec.Reset();
                if (operation == WorldOperation.Encode) world.Registry.TryEncode(17, out _, out _).ThrowIfFailure();
                else history.TryRestore(world.Registry, 17).ThrowIfFailure();
                world.ValidateValues();
                var d = Stats(world.MemoryStats); d.reservedArenaBytes = arena; d.active = count;
                d.reservedNativeMetadataBytes = world.NativeMetadataBytes;
                d.measuringCodecCalls = CountingCodec.Measures; d.writingCodecCalls = CountingCodec.Writes; d.readingCodecCalls = CountingCodec.Reads; return d;
            }
        }

        private static IEnumerable<TestCaseData> InputCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int gap in new[] { 0, 128, 4096 })
            foreach (int players in new[] { 1, 64 })
            foreach (PredictionPolicy policy in Enum.GetValues(typeof(PredictionPolicy)))
                yield return new TestCaseData(backend, gap, players, policy);
        }
        [TestCaseSource(nameof(InputCases)), Performance]
        public void InputPredictionAcrossMissingTickWindow(Backend backend, int gap, int players, PredictionPolicy policy)
        {
            var factory = new TrackingFactory(backend);
            using (var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend, factory), players, Math.Max(2, gap + 2), policy, -7))
            {
                // Players become active at gap; preceding empty-roster frames are advanced untimed.
                // No actual cells exist in that window, so HoldLast scans it before falling back to carry.
                for (int p = players; p >= 1; p--)
                { inputs.TryRegisterPlayer(new PlayerId((ulong)p), gap).ThrowIfFailure(); inputs.TrySeedLastInput(new PlayerId((ulong)p), -1, p * 17).ThrowIfFailure(); }
                for (int tick = 0; tick < gap; tick++)
                { inputs.TryResolve(tick, true, out _).ThrowIfFailure(); inputs.TryAdvance(tick).ThrowIfFailure(); }
                DeterministicInputFrame<int> frame = default; DeterministicStatus status = default;
                Benchmark("H6", "Input.ResolvePredicted", backend, policy + "_" + players + "Players", gap, players, 0,
                    () => status = inputs.TryResolve(gap, true, out frame),
                    () =>
                    {
                        status.ThrowIfFailure(); Assert.AreEqual(gap, inputs.NextTick); Assert.AreEqual(players, frame.Count);
                        for (int p = 0; p < players; p++)
                        { Assert.AreEqual((ulong)p + 1, frame[p].Player.Value); Assert.IsTrue(frame[p].Predicted); Assert.AreEqual(policy == PredictionPolicy.HoldLast ? (p + 1) * 17 : -7, frame[p].Value); }
                    },
                    diagnostics: () => new Diagnostics { reservedArenaBytes = factory.Reserved, active = players, universe = gap });
            }
        }
    }
}

