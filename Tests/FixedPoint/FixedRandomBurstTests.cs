using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomBurstTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct ReplayStressJob : IJobParallelFor
        {
            internal NativeArray<FixedRandomReplayStress.Result> Results;
            internal bool Reverse;
            public void Execute(int index)
            {
                var result = FixedRandomReplayStress.Evaluate(Reverse ? Results.Length - 1 - index : index);
                MarkManaged(ref result.Managed); Results[index] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void LongReplayMatchesEveryManagedWordAcrossReverseOrderAndDifferentNativeJobPartitions()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var forward = new NativeArray<FixedRandomReplayStress.Result>(FixedRandomReplayStress.Cases, Allocator.TempJob))
            using (var reverse = new NativeArray<FixedRandomReplayStress.Result>(FixedRandomReplayStress.Cases, Allocator.TempJob))
            {
                new ReplayStressJob { Results = forward }.Schedule(forward.Length, 1).Complete();
                new ReplayStressJob { Results = reverse, Reverse = true }.Schedule(reverse.Length, 13).Complete();
                ulong checksum = 14695981039346656037UL;
                for (int i = 0; i < forward.Length; i++)
                {
                    var managed = FixedRandomReplayStress.Evaluate(i); var a = forward[i]; var b = reverse[reverse.Length - 1 - i];
                    Assert.AreEqual(0, a.Managed, "Managed fallback at " + i); Assert.AreEqual(0, b.Managed);
                    Assert.AreEqual(0, a.Failures); Assert.IsTrue(managed.Same(ref a), "Managed/native word mismatch at " + i);
                    Assert.IsTrue(a.Same(ref b), "Order/partition mismatch at " + i); Assert.AreEqual(FixedRandomReplayStress.ExpectedCases[i], a.Digest());
                    checksum = a.Digest(checksum);
                }
                Assert.AreEqual(FixedRandomReplayStress.ExpectedChecksum, checksum);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct CorpusJob : IJobParallelFor
        {
            internal NativeArray<FixedRandomCorpus.Result> Results;
            public void Execute(int index)
            {
                var result = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(index)); MarkManaged(ref result.Managed); Results[index] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void NativeBurstAndDifferentJobPartitionsMatchEveryManagedCorpusWord()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var first = new NativeArray<FixedRandomCorpus.Result>(FixedRandomCorpus.Count, Allocator.TempJob))
            using (var second = new NativeArray<FixedRandomCorpus.Result>(FixedRandomCorpus.Count, Allocator.TempJob))
            {
                new CorpusJob { Results = first }.Schedule(first.Length, 1).Complete();
                new CorpusJob { Results = second }.Schedule(second.Length, 17).Complete();
                for (int i = 0; i < first.Length; i++)
                {
                    var managed = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(i)); var a = first[i]; var b = second[i];
                    Assert.AreEqual(0, a.Managed, "Managed fallback at sample " + i); Assert.AreEqual(0, b.Managed);
                    Assert.IsTrue(managed.Same(ref a), "Raw/status/state/wire sample " + i); Assert.IsTrue(a.Same(ref b));
                    Assert.AreEqual(FixedRandomCorpus.ExpectedCases[i], a.Digest());
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdaptersJob : IJob
        {
            internal NativeArray<Fixed32> A;
            internal NativeArray<UFixed32> B;
            internal NativeArray<Fixed64> C;
            internal NativeArray<UFixed64> D;
            internal NativeArray<byte> Bytes;
            internal NativeArray<int> Values, Selected, Scratch;
            internal NativeArray<ulong> Prefix, Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = (ulong)managed;
                var random = FixedRandom.Create(778); ulong errors = 0;
                if (!FixedRandomNative.TryFill(ref random, A, out var status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, B, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, C, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, D, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFillBytes(ref random, Bytes, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryShuffle(ref random, Values, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryChoose(ref random, Values, out int chosen, out status)) errors++; errors |= (uint)status; Result[1] = (ulong)chosen;
                if (!FixedRandomNative.TrySampleWithoutReplacement(ref random, Values.Length, Selected, Scratch, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryBuildWeights(C, Prefix, out var table, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, table, out chosen, out status)) errors++; errors |= (uint)status; Result[2] = (ulong)chosen;
                if (!FixedRandomNative.TryWeightedIndex(ref random, A, out chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, B, out chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, D, out chosen, out status)) errors++; errors |= (uint)status;
                Result[3] = errors; Result[4] = random.Position;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void NativeArrayAdaptersExecuteInNativeBurst()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var a = new NativeArray<Fixed32>(16, Allocator.TempJob))
            using (var b = new NativeArray<UFixed32>(16, Allocator.TempJob))
            using (var c = new NativeArray<Fixed64>(16, Allocator.TempJob))
            using (var d = new NativeArray<UFixed64>(16, Allocator.TempJob))
            using (var bytes = new NativeArray<byte>(19, Allocator.TempJob))
            using (var values = new NativeArray<int>(12, Allocator.TempJob))
            using (var selected = new NativeArray<int>(4, Allocator.TempJob))
            using (var scratch = new NativeArray<int>(12, Allocator.TempJob))
            using (var prefix = new NativeArray<ulong>(16, Allocator.TempJob))
            using (var result = new NativeArray<ulong>(5, Allocator.TempJob))
            {
                var writable = values; for (int i = 0; i < values.Length; i++) writable[i] = i;
                new AdaptersJob { A = a, B = b, C = c, D = d, Bytes = bytes, Values = values, Selected = selected, Scratch = scratch, Prefix = prefix, Result = result }.Schedule().Complete();
                Assert.AreEqual(0UL, result[0]); Assert.AreEqual(0UL, result[3]); Assert.Greater(result[4], 0UL);
                Assert.That(result[1], Is.InRange(0UL, 11UL)); Assert.That(result[2], Is.InRange(0UL, 15UL));
                int mask = 0; for (int i = 0; i < selected.Length; i++) { int bit = 1 << selected[i]; Assert.AreEqual(0, mask & bit); mask |= bit; }
            }
        }
    }
}
