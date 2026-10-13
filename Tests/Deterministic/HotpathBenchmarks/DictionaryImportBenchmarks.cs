using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class ExtendedHotpathBenchmarks
    {
        private struct ImportHashPolicy : IDeterministicEquality<int>
        {
            internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) => Collisions ? 1UL : default(Int32Policy).Hash(value);
        }
        private static IEnumerable<TestCaseData> DictionaryImportCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (bool collisions in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, collisions, policy).SetName($"H12_DictionaryImport_{backend}_{count}_{(collisions ? "ConstantHash" : "NormalHash")}_{policy}");
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct DictionaryImportJob : IJob
        {
            internal NativeDeterministicDictionary<int, int, ImportHashPolicy> Map;
            [ReadOnly] internal NativeArray<byte> Packet;
            internal NativeArray<byte> Scratch;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var reader = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Packet), Packet.Length);
                var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                Result[1] = (int)Map.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy));
                Result[2] = reader.Position; Result[3] = scratch.Position;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCaseSource(nameof(DictionaryImportCases)), Performance]
        public void DictionaryCanonicalValidationAndBucketRebuild(Backend backend, int count, bool collisions, SnapshotPolicy policy)
        {
            var equality = new ImportHashPolicy { Collisions = collisions };
            // Canonical source and reference order are shared by both backends.
            using var source = new DeterministicDictionary<int, int, ImportHashPolicy>(Options(count), equality);
            for (int i = 0; i < count; i++) source.Add(i, SlotValue(i));
            var measure = DeterministicWriter.CreateMeasure(); source.TryWriteCanonical(ref measure, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            using var packet = new NativeArray<byte>(measure.Position, Allocator.Persistent);
            using var scratch = new NativeArray<byte>(count * (sizeof(OrderedHashEntry<int, int>) + 16) + 256, Allocator.Persistent);
            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet), w = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(scratch);
            var writer = new DeterministicWriter(p, packet.Length); source.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            Assert.AreEqual(packet.Length, writer.Position);
            string distribution = (collisions ? "ConstantHash" : "NormalHash") + "_" + policy + "_1DifferentRoot";
            if (backend == Backend.Managed)
            {
                using var map = new DeterministicDictionary<int, int, ImportHashPolicy>(Options(count, policy, 1), equality);
                for (int i = 0; i < count; i++) map.Add(i, ~SlotValue(i));
                map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    int status = 0, position = 0, usedScratch = 0; long baseline = (map.Entries.Owner.MemoryStats + map.Buckets.Owner.MemoryStats).LivePayloadBytes;
                    Benchmark("H12", "Dictionary.ValidateAndImport", backend, distribution, count, count, packet.Length,
                        () =>
                        {
                            var reader = new DeterministicReader(p, packet.Length); var work = new CanonicalScratch(w, scratch.Length);
                            status = (int)map.TryReadCanonical(ref reader, ref work, default(Int32Policy), default(Int32Policy)); position = reader.Position; usedScratch = work.Position;
                        },
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(packet.Length, position); Assert.AreEqual(count, map.Count); Assert.AreEqual(count, root.Count);
                            int index = 0; foreach (var pair in map)
                            { if (pair.Key != index || pair.Value != SlotValue(index)) Assert.Fail("Imported dictionary order " + index); index++; }
                            Assert.AreEqual(count, index);
                            for (int i = 0; i < count; i++)
                            {
                                // Test derived bucket lookup as well as insertion order and the retained values.
                                var a = map.TryGet(i, out int value); var b = root.Entries.TryGet(i, out var retained);
                                if (!a.IsSuccess() || !b.IsSuccess() || value != SlotValue(i) || retained.Active != 1 || retained.Key != i || retained.Value != ~SlotValue(i)) Assert.Fail("Dictionary bucket/snapshot " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(map.Entries.Owner.MemoryStats + map.Buckets.Owner.MemoryStats); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.scratchBytes = usedScratch; d.packetBytes = packet.Length; d.active = count; d.retainedRoots = 1; d.payloadElementBytes = sizeof(OrderedHashEntry<int, int>); return d; });
                }
                finally { root.Dispose(); }
            }
            else
            {
                using var map = new NativeDeterministicDictionary<int, int, ImportHashPolicy>(Options(count, policy, 1), Allocator.Persistent, equality);
                using var result = new NativeArray<int>(4, Allocator.Persistent);
                for (int i = 0; i < count; i++) map.Add(i, ~SlotValue(i));
                map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = (map.Entries.MemoryStats + map.Buckets.MemoryStats).LivePayloadBytes;
                    var job = new DictionaryImportJob { Map = map, Packet = packet, Scratch = scratch, Result = result };
                    Benchmark("H12", "Dictionary.ValidateAndImport", backend, distribution, count, count, packet.Length, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(packet.Length, result[2]); Assert.AreEqual(count, map.Count); Assert.AreEqual(count, root.Count);
                            int index = 0; var iterator = map.GetEnumerator(); while (iterator.MoveNext())
                            { var pair = iterator.Current; if (pair.Key != index || pair.Value != SlotValue(index)) Assert.Fail("Imported native dictionary order " + index); index++; }
                            Assert.AreEqual(count, index);
                            for (int i = 0; i < count; i++)
                            {
                                var a = map.TryGet(i, out int value); var retained = root.Entries.ReadSnapshot(root.EntrySlot, i);
                                if (!a.IsSuccess() || retained.Active != 1 || retained.Key != i || value != SlotValue(i) || retained.Value != ~SlotValue(i)) Assert.Fail("Native dictionary bucket/snapshot " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(map.Entries.MemoryStats + map.Buckets.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(map.Entries) + NativeMetadata(map.Buckets); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.scratchBytes = result[3]; d.packetBytes = packet.Length; d.active = count; d.retainedRoots = 1; d.payloadElementBytes = sizeof(OrderedHashEntry<int, int>); return d; });
                }
                finally { root.Dispose(); }
            }
        }
    }
}
