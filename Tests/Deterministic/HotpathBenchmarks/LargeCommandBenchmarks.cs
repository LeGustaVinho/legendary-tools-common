using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class NetworkLifecycleHotpathBenchmarks
    {
        private struct CommandPayload64 { internal fixed ulong Words[8]; }
        private struct CommandPayload256 { internal fixed ulong Words[32]; }
        private static IEnumerable<TestCaseData> LargeCommandCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int count in new[] { 256, 2048 })
            foreach (int producers in new[] { 4, 64 })
            foreach (int payloadBytes in new[] { 64, 256 })
            foreach (bool shuffled in new[] { false, true })
                yield return new TestCaseData(backend, count, producers, payloadBytes, shuffled).SetName($"H17_CommandMerge_{backend}_Count{count}_Producers{producers}_Payload{payloadBytes}_{(shuffled ? "Shuffled" : "OrderedPartitions")}");
        }
        [TestCaseSource(nameof(LargeCommandCases)), Performance]
        public void MergeCopiesAndSortsLargePayloadsAcrossPartitions(Backend backend, int count, int producers, int payloadBytes, bool shuffled)
        {
            if (payloadBytes == 64) MergeLargeCommands<CommandPayload64>(backend, count, producers, shuffled);
            else MergeLargeCommands<CommandPayload256>(backend, count, producers, shuffled);
        }
        private static byte PayloadByte(int key, int index) => (byte)(Value(key) + index * 17 + (index >> 3) * 29);
        private void MergeLargeCommands<T>(Backend backend, int count, int producerCount, bool shuffled) where T : unmanaged
        {
            int perProducer = count / producerCount;
            var ids = new uint[producerCount]; for (int i = 0; i < ids.Length; i++) ids[i] = (uint)(1009 - i * 7);
            int[] keys = Permutation(count);
            var oracle = new DeterministicCommand<T>[count]; var factory = new TrackingFactory(backend);
            using var commands = new DeterministicCommandBuffer<T>(Context(backend, factory), ids, perProducer);
            for (int p = 0; p < producerCount; p++)
            {
                commands.TryGetWriter(ids[p], out var writer).ThrowIfFailure();
                for (int i = 0; i < perProducer; i++)
                {
                    int rank = p * perProducer + i, key = shuffled ? keys[rank] : rank;
                    T value = default; byte* payload = (byte*)&value;
                    for (int b = 0; b < sizeof(T); b++) payload[b] = PayloadByte(key, b);
                    var entity = new StableHandle((uint)key, 1);
                    writer.TryEnqueue(1, 3, entity, (ulong)i, value).ThrowIfFailure();
                    // Indexed by independent final rank rather than sorting with the production comparator.
                    oracle[key] = new DeterministicCommand<T>(new DeterministicCommandHeader(1, 3, entity, (ulong)i, ids[p]), value);
                }
            }
            DeterministicStatus status = default; IntPtr address = IntPtr.Zero; int mergedCount = 0; ulong digest = 0;
            Benchmark("H17", "CommandBuffer.MergeLargePayload", backend,
                (shuffled ? "Shuffled" : "OrderedPartitions") + "_Producers" + producerCount + "_Payload" + sizeof(T),
                count, count, count * sizeof(DeterministicCommand<T>),
                () =>
                {
                    status = commands.TryMerge(out var merged); mergedCount = merged.Length;
                    fixed (DeterministicCommand<T>* pointer = merged) address = (IntPtr)pointer;
                },
                () =>
                {
                    status.ThrowIfFailure(); Assert.AreEqual(count, commands.Count); Assert.AreEqual(count, mergedCount);
                    var sorted = (DeterministicCommand<T>*)address; digest = DigestSeed;
                    for (int i = 0; i < count; i++)
                    {
                        var header = sorted[i].Header; var expected = oracle[i].Header;
                        if (header.Phase != expected.Phase || header.SystemId != expected.SystemId || header.Entity != expected.Entity ||
                            header.Sequence != expected.Sequence || header.ProducerId != expected.ProducerId) Assert.Fail("Command header/rank differs at " + i);
                        byte* payload = (byte*)&sorted[i].Payload;
                        digest = Mix(digest, header.Entity.Packed); digest = Mix(digest, header.Sequence); digest = Mix(digest, header.ProducerId);
                        for (int b = 0; b < sizeof(T); b++)
                        { if (payload[b] != PayloadByte(i, b)) Assert.Fail("Command payload moved to the wrong header."); digest = Mix(digest, payload[b]); }
                    }
                }, diagnostics: () => new Diagnostics { reservedArenaBytes = factory.Reserved, active = count, producerCount = producerCount,
                    payloadElementBytes = sizeof(T), externalBufferBytes = (long)count * sizeof(DeterministicCommand<T>) + keys.Length * sizeof(int) + ids.Length * sizeof(uint),
                    scratchBytes = (long)count * sizeof(DeterministicCommand<T>) }, digest: () => digest);
        }
    }
}
