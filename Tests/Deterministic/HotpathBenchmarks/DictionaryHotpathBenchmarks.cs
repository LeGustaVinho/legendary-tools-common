using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class DictionarySparseHotpathBenchmarks
    {
        public enum WriteLocality { Clustered32, Spread32 }
        public enum DictionaryProbe { RemoveOldest, RemoveMiddle, RemoveNewest, RemoveMissing, DuplicateOldest }
        internal enum MapOperation { Set, Add, Remove, Clear, Enumerate }
        [BurstDiscard] private static void MarkManaged(ref int marker) { marker = 1; }

        [BurstCompile(CompileSynchronously = true)]
        internal struct DictionaryOperationJob<T> : IJob where T : unmanaged, IPayload
        {
            internal NativeDeterministicDictionary<int, T, MapHashPolicy> Map;
            [ReadOnly] internal NativeArray<int> Inputs;
            [ReadOnly] internal NativeArray<T> Values;
            internal NativeArray<int> Statuses, Marker, OutputKeys;
            internal NativeArray<T> OutputValues;
            internal MapOperation Operation;
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Marker[0] = marker;
                if (Operation == MapOperation.Enumerate)
                {
                    int count = 0; var e = Map.GetEnumerator();
                    while (e.MoveNext()) { var pair = e.Current; OutputKeys[count] = pair.Key; OutputValues[count++] = pair.Value; }
                    Statuses[0] = count; return;
                }
                for (int i = 0; i < Inputs.Length; i++)
                {
                    int key = Inputs[i];
                    if (Operation == MapOperation.Set || Operation == MapOperation.Add)
                    {
                        var value = Values[i];
                        Statuses[i] = (int)(Operation == MapOperation.Set ? Map.TrySet(key, value) : Map.TryAdd(key, value));
                    }
                    else Statuses[i] = (int)(Operation == MapOperation.Remove ? Map.TryRemove(key) : Map.TryClear());
                }
            }
        }
        private static void RunMap<T>(DictionaryOwner<T> owner, MapOperation operation) where T : unmanaged, IPayload
        {
            if (owner.Backend == Backend.BurstJob)
            {
                new DictionaryOperationJob<T> { Map = owner.Native, Inputs = owner.Inputs, Values = owner.Values,
                    Statuses = owner.Statuses, Marker = owner.Marker, OutputKeys = owner.OutputKeys,
                    OutputValues = owner.OutputValues, Operation = operation }.Schedule().Complete();
                return;
            }
            if (operation == MapOperation.Enumerate)
            {
                int count = 0; var e = owner.Managed.GetEnumerator();
                while (e.MoveNext()) { var pair = e.Current; owner.OutputKeys[count] = pair.Key; owner.OutputValues[count++] = pair.Value; }
                owner.Statuses[0] = count; return;
            }
            for (int i = 0; i < owner.Inputs.Length; i++)
            {
                int key = owner.Inputs[i];
                if (operation == MapOperation.Set || operation == MapOperation.Add)
                {
                    var value = owner.Values[i];
                    owner.Statuses[i] = (int)(operation == MapOperation.Set ? owner.Managed.TrySet(key, value) : owner.Managed.TryAdd(key, value));
                }
                else owner.Statuses[i] = (int)(operation == MapOperation.Remove ? owner.Managed.TryRemove(key) : owner.Managed.TryClear());
            }
        }
        private static IEnumerable<TestCaseData> DictionaryUpdateCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (int bytes in new[] { 64, 256 })
            foreach (WriteLocality locality in Enum.GetValues(typeof(WriteLocality)))
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, bytes, locality, policy).SetName($"DS1_DictionaryUpdate_{backend}_Count{count}_Payload{bytes}_{locality}_{policy}");
        }
        [TestCaseSource(nameof(DictionaryUpdateCases)), Performance]
        public void DictionaryExistingValueWrites(Backend backend, int count, int bytes, WriteLocality locality, SnapshotPolicy policy)
        {
            if (bytes == 64) DictionaryUpdates<Payload64>(backend, count, locality, policy);
            else DictionaryUpdates<Payload256>(backend, count, locality, policy);
        }
        private void DictionaryUpdates<T>(Backend backend, int count, WriteLocality locality, SnapshotPolicy policy) where T : unmanaged, IPayload
        {
            int[] keys = Range(count); var updated = new bool[count];
            using var owner = new DictionaryOwner<T>(backend, Options(count, policy, 1), false, 32);
            foreach (int key in keys) owner.AddInitial(key);
            owner.CaptureBaseline(keys);
            for (int i = 0; i < 32; i++)
            {
                int key = locality == WriteLocality.Clustered32 ? i : i * (count / 32);
                owner.Inputs[i] = key; owner.Values[i] = Payload<T>(key, 1); updated[key] = true;
            }
            ulong digest = 0;
            Benchmark("DS1", "Dictionary.UpdateExisting32", backend, locality + "_Payload" + sizeof(T) + "_" + policy + "_1Root", count, 32, 32 * sizeof(T),
                () => RunMap(owner, MapOperation.Set),
                () => { owner.CheckStatus(0); Assert.AreEqual(count, owner.Capacity); digest = owner.Validate(keys, key => updated[key] ? 1 : 0); },
                owner.Reset, owner.Diagnostics, () => digest);
        }

        private static IEnumerable<TestCaseData> DictionaryRemovalCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (DictionaryProbe probe in Enum.GetValues(typeof(DictionaryProbe)))
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, probe, policy).SetName($"DS2_DictionaryChain_{backend}_Count{count}_{probe}_{policy}");
        }
        [TestCaseSource(nameof(DictionaryRemovalCases)), Performance]
        public void DictionaryRemovalAndDuplicateUnderCollisions(Backend backend, int count, DictionaryProbe probe, SnapshotPolicy policy)
        {
            int[] original = Range(count);
            using var owner = new DictionaryOwner<Payload256>(backend, Options(count, policy, 1), true, 1);
            foreach (int key in original) owner.AddInitial(key);
            owner.CaptureBaseline(original);
            int target = probe == DictionaryProbe.RemoveMiddle ? count / 2 : probe == DictionaryProbe.RemoveNewest ? count - 1 : probe == DictionaryProbe.RemoveMissing ? count + 7 : 0;
            bool duplicate = probe == DictionaryProbe.DuplicateOldest, missing = probe == DictionaryProbe.RemoveMissing;
            int[] expected = duplicate || missing ? original : Without(original, target);
            owner.Inputs[0] = target; owner.Values[0] = Payload<Payload256>(target, 1);
            ulong digest = 0;
            Benchmark("DS2", "Dictionary." + probe, backend, "ConstantHash_Payload256_" + policy + "_1Root", count, 1, 0,
                () => RunMap(owner, duplicate ? MapOperation.Add : MapOperation.Remove),
                () =>
                {
                    owner.CheckStatus((int)(duplicate ? DeterministicStatus.DuplicateKey : missing ? DeterministicStatus.NotFound : DeterministicStatus.None));
                    digest = owner.Validate(expected);
                    if (duplicate || missing) Assert.AreEqual(0, owner.Diagnostics().payloadGrowthBytes, "Rejected operation changed live storage.");
                }, owner.Reset, owner.Diagnostics, () => digest);
        }

        private static IEnumerable<TestCaseData> DictionaryClearCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int capacity in new[] { 1024, 65536 })
            foreach (bool highWater in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, capacity, highWater, policy).SetName($"DS3_DictionaryClear_{backend}_Capacity{capacity}_{(highWater ? "HighWater" : "LowWater")}_{policy}");
        }
        [TestCaseSource(nameof(DictionaryClearCases)), Performance]
        public void DictionaryClearWithFewLiveAndManyUsedSlots(Backend backend, int capacity, bool highWater, SnapshotPolicy policy)
        {
            using var owner = new DictionaryOwner<Payload64>(backend, Options(capacity, policy, 1), false, 1);
            int used = highWater ? capacity : 64;
            for (int i = 0; i < used; i++) owner.AddInitial(i);
            for (int i = used - 1; i >= 64; i--) owner.RemoveInitial(i);
            owner.CaptureBaseline(Range(64)); ulong digest = 0;
            Benchmark("DS3", "Dictionary.Clear", backend, (highWater ? "HighWater" : "LowWater") + "_64Live_" + policy + "_1Root", capacity, 1, 0,
                () => RunMap(owner, MapOperation.Clear),
                () => { owner.CheckStatus(0); Assert.AreEqual(0, owner.State.Used); digest = owner.Validate(Array.Empty<int>()); for (int i = 0; i < used; i++) Assert.AreEqual(0, owner.Entry(i).Active); },
                owner.Reset, owner.Diagnostics, () => digest);
        }

        private static IEnumerable<TestCaseData> DictionaryGrowthCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (bool reuse in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, reuse, policy).SetName($"DS4_DictionaryCapacity_{backend}_Initial{count}_{(reuse ? "ReuseSlot" : "GrowRehash")}_{policy}");
        }
        [TestCaseSource(nameof(DictionaryGrowthCases)), Performance]
        public void DictionaryGrowthRehashVersusFreeSlotReuse(Backend backend, int count, bool reuse, SnapshotPolicy policy)
        {
            DictionaryOwner<Payload64> owner = null; int[] original = Range(reuse ? count - 1 : count), expected = Appended(original, count + 1234); ulong digest = 0;
            Action setup = () =>
            {
                owner?.Dispose(); owner = new DictionaryOwner<Payload64>(backend, Growing(count, count * 2, policy), false, 1);
                for (int i = 0; i < count; i++) owner.AddInitial(i);
                if (reuse) owner.RemoveInitial(count - 1);
                owner.CaptureBaseline(original); owner.Inputs[0] = count + 1234; owner.Values[0] = Payload<Payload64>(count + 1234);
            };
            try
            {
                Benchmark("DS4", reuse ? "Dictionary.ReuseSlot" : "Dictionary.GrowRehash", backend, "Payload64_" + policy + "_1Root_RecreatedOwner", count, 1, 64,
                    () => RunMap(owner, MapOperation.Add),
                    () =>
                    {
                        owner.CheckStatus(0); Assert.AreEqual(reuse ? count : count * 2, owner.Capacity);
                        Assert.AreEqual(OrderedHashRules.BucketCapacity(owner.Capacity), owner.BucketCapacity);
                        Assert.AreEqual(reuse ? count : count + 1, owner.State.Used); digest = owner.Validate(expected);
                    }, setup, () => owner.Diagnostics(), () => digest, true);
            }
            finally { owner?.Dispose(); }
        }

        private static IEnumerable<TestCaseData> DictionaryIterationCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (int bytes in new[] { 64, 256 })
            foreach (bool fragmented in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, bytes, fragmented, policy).SetName($"DS5_DictionaryIteration_{backend}_Active{count}_Payload{bytes}_{(fragmented ? "Stride8" : "Dense")}_{policy}");
        }
        [TestCaseSource(nameof(DictionaryIterationCases)), Performance]
        public void DictionaryInsertionOrderOverFragmentedStorage(Backend backend, int count, int bytes, bool fragmented, SnapshotPolicy policy)
        {
            if (bytes == 64) DictionaryIteration<Payload64>(backend, count, fragmented, policy);
            else DictionaryIteration<Payload256>(backend, count, fragmented, policy);
        }
        private void DictionaryIteration<T>(Backend backend, int count, bool fragmented, SnapshotPolicy policy) where T : unmanaged, IPayload
        {
            int stride = fragmented ? 8 : 1, used = count * stride; int[] expected = Range(count, stride);
            using var owner = new DictionaryOwner<T>(backend, Options(used, policy, 0), false, 1, count);
            for (int i = 0; i < used; i++) owner.AddInitial(i);
            if (fragmented) for (int i = 0; i < used; i++) if (i % 8 != 0) owner.RemoveInitial(i);
            owner.CaptureBaseline(expected, false); ulong digest = 0;
            Benchmark("DS5", "Dictionary.InsertionOrderScan", backend, (fragmented ? "Stride8" : "Dense") + "_Payload" + sizeof(T) + "_" + policy + "_0Roots", count, count, count * sizeof(T),
                () => RunMap(owner, MapOperation.Enumerate),
                () =>
                {
                    owner.CheckStatus(count); digest = owner.Validate(expected);
                    for (int i = 0; i < count; i++) { Assert.AreEqual(expected[i], owner.OutputKeys[i]); digest = VerifyPayload(owner.OutputValues[i], expected[i], 0, digest); }
                }, diagnostics: owner.Diagnostics, digest: () => digest);
        }
    }
}

