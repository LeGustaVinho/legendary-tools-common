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
        public enum SnapshotReadPattern { Last32, OrdinalSweep }
        private static IEnumerable<TestCaseData> DictionarySnapshotReadCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 128, 512 })
            foreach (int bytes in new[] { 64, 256 })
            foreach (SnapshotReadPattern pattern in Enum.GetValues(typeof(SnapshotReadPattern)))
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, bytes, pattern, policy).SetName($"DS6_DictionarySnapshotRead_{backend}_Count{count}_Payload{bytes}_{pattern}_{policy}");
        }
        [BurstCompile(CompileSynchronously = true)]
        internal struct DictionarySnapshotReadJob<T> : IJob where T : unmanaged, IPayload
        {
            [ReadOnly] internal NativeDictionarySnapshot<int, T> Snapshot;
            [ReadOnly] internal NativeArray<int> Ordinals;
            internal NativeArray<int> Statuses, Marker, OutputKeys;
            internal NativeArray<T> OutputValues;
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Marker[0] = marker;
                for (int i = 0; i < Ordinals.Length; i++)
                {
                    Statuses[i] = (int)Snapshot.TryGetAt(Ordinals[i], out var pair);
                    OutputKeys[i] = pair.Key; OutputValues[i] = pair.Value;
                }
            }
        }
        [TestCaseSource(nameof(DictionarySnapshotReadCases)), Performance]
        public void DictionarySnapshotOrdinalReads(Backend backend, int count, int bytes, SnapshotReadPattern pattern, SnapshotPolicy policy)
        {
            if (bytes == 64) DictionarySnapshotReads<Payload64>(backend, count, pattern, policy);
            else DictionarySnapshotReads<Payload256>(backend, count, pattern, policy);
        }
        private void DictionarySnapshotReads<T>(Backend backend, int count, SnapshotReadPattern pattern, SnapshotPolicy policy) where T : unmanaged, IPayload
        {
            int calls = pattern == SnapshotReadPattern.Last32 ? 32 : count; int[] keys = Range(count);
            using var owner = new DictionaryOwner<T>(backend, Options(count, policy, 1), false, calls, calls);
            foreach (int key in keys) owner.AddInitial(key);
            owner.CaptureBaseline(keys);
            for (int i = 0; i < calls; i++) owner.Inputs[i] = pattern == SnapshotReadPattern.Last32 ? count - 32 + i : i;
            Action action = () =>
            {
                if (backend == Backend.BurstJob)
                    new DictionarySnapshotReadJob<T> { Snapshot = owner.NativeRoot, Ordinals = owner.Inputs, Statuses = owner.Statuses,
                        Marker = owner.Marker, OutputKeys = owner.OutputKeys, OutputValues = owner.OutputValues }.Schedule().Complete();
                else for (int i = 0; i < calls; i++)
                {
                    owner.Statuses[i] = (int)owner.ManagedRoot.TryGetAt(owner.Inputs[i], out var pair);
                    owner.OutputKeys[i] = pair.Key; owner.OutputValues[i] = pair.Value;
                }
            };
            ulong digest = 0;
            Benchmark("DS6", "Dictionary.Snapshot" + pattern, backend, "Payload" + sizeof(T) + "_" + policy + "_1Root", count, calls, calls * sizeof(T),
                action,
                () =>
                {
                    owner.CheckStatus(0); digest = owner.Validate(keys);
                    for (int i = 0; i < calls; i++)
                    {
                        int expected = pattern == SnapshotReadPattern.Last32 ? count - 32 + i : i;
                        Assert.AreEqual(expected, owner.OutputKeys[i]); digest = Mix(digest, (uint)expected);
                        digest = VerifyPayload(owner.OutputValues[i], expected, 0, digest);
                    }
                    Assert.AreEqual(0, owner.Diagnostics().payloadGrowthBytes);
                }, diagnostics: owner.Diagnostics, digest: () => digest);
        }
    }
}

