using System;
using LegendaryTools.FixedPoint;
using LegendaryTools.FixedPoint.Tests.Benchmarks;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Tests.Benchmarks
{
    [Category("BoundsBenchmark"), Explicit("Select this fixture explicitly for Bounds canonical, history and job-granularity measurements.")]
    public sealed unsafe class BoundsRuntimeBenchmarks : BoundsBenchmarkFixture
    {
        private static FixedBounds3[] Initial(int count)
        {
            var result = new FixedBounds3[count];
            for (int i = 0; i < count; i++) result[i] = new FixedBounds3(new FixedVector3(i % 17, i % 11, i % 7), new FixedVector3(2, 4, 6));
            return result;
        }
        private static void Canonical(FixedBounds3* boxes, byte* packet, int count, int operation, out long size, out ulong digest, out int status)
        {
            var codec = default(FixedBounds3Policy); size = 0; digest = 14695981039346656037UL; status = 0;
            var writer = operation == 1 ? DeterministicWriter.CreateMeasure() : new DeterministicWriter(packet, count * 48);
            var reader = new DeterministicReader(packet, count * 48);
            for (int i = 0; i < count; i++)
            {
                if (operation == 0) size += codec.Measure(boxes[i]);
                else if (operation <= 2) status |= (int)codec.TryWrite(ref writer, boxes[i]);
                else
                {
                    status |= (int)codec.TryRead(ref reader, out var box); box.GetStorage(out var a, out var b);
                    digest = unchecked((digest ^ (ulong)(a.x.RawValue ^ b.z.RawValue)) * 1099511628211UL);
                }
            }
            if (operation != 0) size = operation <= 2 ? writer.Position : reader.Position;
        }
#if !FIXEDPOINT_STANDALONE
        [BurstCompile(CompileSynchronously = true)]
        private struct CanonicalJob : IJob
        {
            [ReadOnly] internal NativeArray<FixedBounds3> Boxes;
            internal NativeArray<byte> Packet;
            internal NativeArray<long> Result;
            internal int Operation;
            public void Execute()
            {
                Canonical((FixedBounds3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Boxes), (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet), Boxes.Length, Operation, out var size, out var digest, out var status);
                int managed = 0; MarkManaged(ref managed); Result[0] = managed; Result[1] = size; Result[2] = unchecked((long)digest); Result[3] = status;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct TransitionJob : IJob
        {
            internal NativeArray<FixedBounds3> Boxes;
            internal NativeArray<int> Result;
            internal int Start, Count;
            public void Execute()
            {
                int managed = 0, status = 0; MarkManaged(ref managed);
                for (int i = Start; i < Start + Count; i++) { var value = Boxes[i]; status |= (int)BoundsSimulationTransition.TryApply(ref value, 1); Boxes[i] = value; }
                Result[0] = managed; Result[1] = status;
            }
        }
        [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
#endif
        [TestCase(128)] [TestCase(4096)]
        public void SeparateConstantSizingValidatedMeasureCanonicalWriteAndRead(int count)
        {
            var initial = Initial(count); var packet = new byte[count * 48]; var names = new[] { "ConstantSize", "ValidatedMeasureWriter", "CanonicalWrite", "CanonicalRead" };
            fixed (FixedBounds3* boxesPin = initial) fixed (byte* packetPin = packet)
            {
                FixedBounds3* boxes = boxesPin; byte* bytes = packetPin;
                Canonical(boxes, bytes, count, 2, out long expectedSize, out _, out int expectedStatus); Assert.AreEqual(0, expectedStatus); Assert.AreEqual(count * 48, expectedSize);
                long measuredSize = 0; ulong measuredDigest = 0; int measuredStatus = 0;
                for (int operation = 0; operation < 4; operation++)
                {
                    int op = operation; Canonical(boxes, bytes, count, op, out expectedSize, out ulong expectedDigest, out expectedStatus);
                    Action verify = () => { Assert.AreEqual(expectedSize, measuredSize); Assert.AreEqual(expectedDigest, measuredDigest); Assert.AreEqual(0, measuredStatus); };
                    Measure("B8", names[op], "Managed", "canonical policy batch; no registry/envelope", 3, FixedRuntime.Settings.DecimalPlaces64,
                        FixedBoundsRuntime.Settings.Representation.ToString(), "count=" + count, count, count * 48L, 0, count * 48L,
                        () => Canonical(boxes, bytes, count, op, out measuredSize, out measuredDigest, out measuredStatus), verify);
#if !FIXEDPOINT_STANDALONE
                    using var nativeBoxes = new NativeArray<FixedBounds3>(initial, Allocator.Persistent);
                    using var nativePacket = new NativeArray<byte>(packet, Allocator.Persistent); using var nativeResult = new NativeArray<long>(4, Allocator.Persistent);
                    var job = new CanonicalJob { Boxes = nativeBoxes, Packet = nativePacket, Result = nativeResult, Operation = op };
                    job.Schedule().Complete(); Assert.AreEqual(0, nativeResult[0]);
                    Measure("B8", names[op], "Burst", "one Schedule+Complete including canonical policy batch", 3, FixedRuntime.Settings.DecimalPlaces64,
                        FixedBoundsRuntime.Settings.Representation.ToString(), "count=" + count, count, count * 48L, 0, count * 48L,
                        () => job.Schedule().Complete(), () => { Assert.AreEqual(0, nativeResult[0]); Assert.AreEqual(expectedSize, nativeResult[1]); Assert.AreEqual(unchecked((long)expectedDigest), nativeResult[2]); Assert.AreEqual(0, nativeResult[3]); });
#endif
                }
            }
        }
        [TestCase(1)] [TestCase(256)] [TestCase(2048)]
        public void ComparePerEntitySynchronizationWithOneBatchForTheSameBoundsTransitions(int count)
        {
            var initial = Initial(count); var expected = (FixedBounds3[])initial.Clone();
            for (int i = 0; i < count; i++) BoundsSimulationTransition.TryApply(ref expected[i], 1).ThrowIfFailure();
            var managed = (FixedBounds3[])initial.Clone();
            Measure("B10", "BoundsTransition", "Managed", "direct transitions in one host loop", 3, FixedRuntime.Settings.DecimalPlaces64,
                FixedBoundsRuntime.Settings.Representation.ToString(), "entities=" + count, count, count * 48L, 0, 0,
                () => { for (int i = 0; i < count; i++) BoundsSimulationTransition.TryApply(ref managed[i], 1).ThrowIfFailure(); },
                () => { for (int i = 0; i < count; i++) Assert.AreEqual(expected[i], managed[i]); }, () => Array.Copy(initial, managed, count));
#if !FIXEDPOINT_STANDALONE
            using var native = new NativeArray<FixedBounds3>(initial, Allocator.Persistent); using var result = new NativeArray<int>(2, Allocator.Persistent);
            foreach (int variant in new[] { 0, 1, 2 })
            {
                Action action = variant == 0 ? (Action)(() => new TransitionJob { Boxes = native, Result = result, Count = 0 }.Schedule().Complete()) : variant == 1 ?
                    () => { for (int i = 0; i < count; i++) new TransitionJob { Boxes = native, Result = result, Start = i, Count = 1 }.Schedule().Complete(); } :
                    (Action)(() => new TransitionJob { Boxes = native, Result = result, Count = count }.Schedule().Complete());
                Measure("B10", variant == 0 ? "EmptyJobControl" : "BoundsTransition", "Burst", "Schedule+Complete; jobs=" + (variant == 1 ? count : 1),
                    3, FixedRuntime.Settings.DecimalPlaces64, FixedBoundsRuntime.Settings.Representation.ToString(), "entities=" + count + ";variant=" + variant,
                    variant == 0 ? 1 : count, count * 48L, 8, 0, action,
                    () => { Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); for (int i = 0; i < count; i++) Assert.AreEqual(variant == 0 ? initial[i] : expected[i], native[i]); },
                    () => native.CopyFrom(initial));
            }
#endif
        }
        [TestCase(128)] [TestCase(4096)]
        public void MeasureRetainedHistoryCpuReservedArenaAndLivePayloadByChangeDensity(int count)
        {
            const int frames = 32; int bytes = count * 48; var codec = default(FixedBounds3Policy);
            foreach (int changedPercent in new[] { 0, 1, 100 })
            {
                // Pre-encode the complete frame stream. History timings exclude Bounds mutation and serialization (measured separately in B3/B8).
                var boxes = Initial(count); var packets = new byte[frames * bytes];
                fixed (byte* pointer = packets)
                {
                    for (int tick = 0; tick < frames; tick++)
                    {
                        int changed = (count * changedPercent + 99) / 100;
                        for (int i = 0; i < changed; i++) { var center = boxes[i].center; center.x += Fixed64.FromRaw(1); boxes[i].center = center; }
                        var writer = new DeterministicWriter(pointer + tick * bytes, bytes);
                        for (int i = 0; i < count; i++) codec.TryWrite(ref writer, boxes[i]).ThrowIfFailure(); Assert.AreEqual(bytes, writer.Position);
                    }
                    byte* stream = pointer;
                    foreach (SnapshotPolicy policy in Enum.GetValues(typeof(SnapshotPolicy))) foreach (int pageSize in policy == SnapshotPolicy.CopyOnWrite ? new[] { 256, 4096 } : new[] { 256 })
                    {
                        History(new DeterministicRuntimeContext(DeterministicBackend.Managed), stream, count, frames, policy, pageSize, changedPercent);
#if !FIXEDPOINT_STANDALONE
                        History(new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory()), stream, count, frames, policy, pageSize, changedPercent);
#endif
                    }
                }
            }
        }
        private void History(DeterministicRuntimeContext context, byte* stream, int count, int frames, SnapshotPolicy policy, int pageSize, int density)
        {
            int bytes = count * 48; using var history = new DeterministicWorldHistory(context, bytes, frames, policy, 8, pageSize);
            string backend = context.Backend == DeterministicBackend.Managed ? "Managed" : "NativeHost";
            string scenario = "count=" + count + ";frames=" + frames + ";policy=" + policy + ";page=" + pageSize + ";changed-percent=" + density;
            Action capture = () => { for (int tick = 0; tick < frames; tick++) history.TryCapture(tick, stream + tick * bytes, bytes).ThrowIfFailure(); };
            Action verify = () =>
            {
                for (int tick = 0; tick < frames; tick++)
                {
                    history.TryRestore(tick, out byte* restored, out int length).ThrowIfFailure(); Assert.AreEqual(bytes, length);
                    byte* expected = stream + tick * bytes;
                    for (int i = 0; i < bytes; i++) if (restored[i] != expected[i]) Assert.Fail("Retained Bounds payload differs at frame " + tick + ", byte " + i);
                }
            };
            Func<long> live = () =>
            {
                if (policy == SnapshotPolicy.CopyOnWrite) return (long)history.LivePages * pageSize;
                long total = 0; for (int tick = 0; tick < frames; tick++) { history.TryGetStoredBytes(tick, out int stored).ThrowIfFailure(); total += stored; } return total;
            };
            Measure("B9", "History.Capture32", backend, "host capture/hash/delta/page management of preencoded Bounds frames", 3, FixedRuntime.Settings.DecimalPlaces64,
                FixedBoundsRuntime.Settings.Representation.ToString(), scenario, frames, (long)frames * bytes, history.ReservedBytes, (long)frames * bytes,
                capture, verify, () => history.TryReset().ThrowIfFailure(), liveBytes: live);
            // Capture has left a complete history. Replay-depth comparisons include decoded payload and hash verification, without mutating registry state.
            byte* restoredPacket = null; int restoredLength = 0;
            foreach (int age in new[] { 0, 7, 15 })
            {
                int target = frames - 1 - age;
                Measure("B9", "History.Restore", backend, "host reconstruct plus checkpoint hash verification", 3, FixedRuntime.Settings.DecimalPlaces64,
                    FixedBoundsRuntime.Settings.Representation.ToString(), scenario + ";age=" + age, 1, (long)frames * bytes, history.ReservedBytes, bytes,
                    () => history.TryRestore(target, out restoredPacket, out restoredLength).ThrowIfFailure(),
                    () => { Assert.AreEqual(bytes, restoredLength); byte* expected = stream + target * bytes; for (int i = 0; i < bytes; i++) if (expected[i] != restoredPacket[i]) Assert.Fail("History restore mismatch"); }, liveBytes: live);
            }
        }
    }
}
