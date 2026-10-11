using NUnit.Framework;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorBurstTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SequenceJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedVectorReplayCorpus.Sample> Samples;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedVectorReplayCorpus.Result> Results;
            internal int Reverse;
            public void Execute(int index)
            {
                if (Reverse != 0) index = Samples.Length - 1 - index;
                var sample = Samples[index]; FixedVectorBits position = sample.A, velocity = default;
                for (int tick = 0; tick < FixedVectorReplayCorpus.SequenceTicks; tick++)
                {
                    var result = FixedVectorReplayCorpus.SequenceStep(sample, tick, ref position, ref velocity);
                    MarkManaged(ref result.Managed); Results[index * FixedVectorReplayCorpus.SequenceTicks + tick] = result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(1)] [TestCase(17)]
        public void EverySimulationTickIsIdenticalInParallelRegardlessOfSubmissionOrder(int batchSize)
        {
            var selected = new List<FixedVectorReplayCorpus.Sample>();
            foreach (var sample in FixedVectorReplayCorpus.Create()) if (sample.Operation == 23) selected.Add(sample);
            Assert.AreEqual(FixedVectorReplayCorpus.SequenceCases, selected.Count); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var samples = new NativeArray<FixedVectorReplayCorpus.Sample>(selected.ToArray(), Allocator.Persistent))
            using (var managed = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length * FixedVectorReplayCorpus.SequenceTicks, Allocator.Persistent))
            using (var native = new NativeArray<FixedVectorReplayCorpus.Result>(managed.Length, Allocator.Persistent))
            {
                var reference = new SequenceJob { Samples = samples, Results = managed };
                for (int i = 0; i < samples.Length; i++) reference.Execute(i);
                new SequenceJob { Samples = samples, Results = native, Reverse = 1 }.Schedule(samples.Length, batchSize).Complete();
                for (int i = 0; i < managed.Length; i++)
                {
                    var m = managed[i]; var n = native[i]; Assert.AreEqual(1, m.Managed); Assert.AreEqual(0, n.Managed); m.Managed = 0;
                    Assert.AreEqual(m, n, "Simulation tick " + i + " batch " + batchSize);
                }
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ParityJob : IJob
        {
            [ReadOnly] internal NativeArray<FixedVectorReplayCorpus.Sample> Samples;
            internal NativeArray<FixedVectorReplayCorpus.Result> Results;
            public void Execute()
            {
                for (int i = 0; i < Samples.Length; i++)
                { var result = FixedVectorReplayCorpus.Evaluate(Samples[i]); MarkManaged(ref result.Managed); Results[i] = result; }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test] public void NativeExecutionMatchesEveryRawStatusAndNetworkByteAcrossTheCorpus()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled, "Enable Burst to validate native execution.");
            using (var samples = new NativeArray<FixedVectorReplayCorpus.Sample>(FixedVectorReplayCorpus.Create(), Allocator.TempJob))
            using (var managed = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length, Allocator.TempJob))
            using (var native = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length, Allocator.TempJob))
            {
                new ParityJob { Samples = samples, Results = managed }.Execute();
                new ParityJob { Samples = samples, Results = native }.Schedule().Complete();
                ulong checksum = 14695981039346656037UL, inputChecksum = checksum;
                for (int i = 0; i < samples.Length; i++)
                {
                    var m = managed[i]; var n = native[i];
                    Assert.AreEqual(1, m.Managed); Assert.AreEqual(0, n.Managed); m.Managed = 0;
                    Assert.AreEqual(m, n, "Sample " + i + " operation " + samples[i].Operation);
                    checksum = FixedVectorReplayCorpus.Mix(checksum, FixedVectorReplayCorpus.Hash(n));
                    inputChecksum = FixedVectorReplayCorpus.Mix(inputChecksum, FixedVectorReplayCorpus.HashInput(samples[i]));
                }
                Assert.AreEqual(FixedVectorReplayCorpus.ExpectedCases, samples.Length);
                Assert.AreEqual(FixedVectorReplayCorpus.ExpectedChecksum, checksum);
                Assert.AreEqual(FixedVectorReplayCorpus.ExpectedInputChecksum, inputChecksum);
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct PublicApiJob : IJob
        {
            internal NativeArray<ulong> Output;
            public void Execute()
            {
                FixedVector2 a = new FixedVector2(3, 4), b = FixedVector2.up;
                FixedVector3 c = new FixedVector3(1, 2, 3), d = FixedVector3.forward;
                ulong hash = 0;
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)(a + b).x.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)(a * b).y.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)(a / (Fixed64)2).x.RawValue);
                a.Normalize(out FixedStatus status); hash = FixedVectorReplayCorpus.Mix(hash, (uint)status);
                a.TryGetComponent(1, out Fixed64 component); a.TrySetComponent(0, component);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)FixedVector2.SignedAngle(a, b).RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)FixedVector2.Perpendicular(a).x.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)FixedVector3.Dot(c, d).RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)FixedVector3.Distance(c, d).RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)c.magnitude.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)c.sqrMagnitude.RawValue);
                c = FixedVector3.Cross(c, d); c = FixedVector3.Lerp(c, d, Fixed64.FromRatio(1, 2));
                c = FixedVector3.ProjectOnPlane(c, FixedVector3.up); c = FixedVector3.Project(c, FixedVector3.right);
                c = FixedVector3.Reflect(c, FixedVector3.right); c = FixedVector3.ClampMagnitude(c, 2);
                c = FixedVector3.MoveTowards(c, d, 1); c = FixedVector3.Slerp(c, d, Fixed64.FromRatio(1, 2));
                c = FixedVector3.RotateTowards(c, d, Fixed64.FromRatio(1, 2), 1);
                FixedVector3 velocity = default;
                c = FixedVector3.SmoothDamp(c, d, ref velocity, 1, Fixed64.PositiveInfinity, Fixed64.FromRatio(1, 60), out status);
                hash = FixedVectorReplayCorpus.Mix(hash, (uint)status);
                FixedVector3 n = c, t = d, binormal = FixedVector3.right;
                FixedVector3.TryOrthoNormalize(ref n, ref t, ref binormal, out status);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)n.x.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)t.y.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)binormal.z.RawValue);
                hash = FixedVectorReplayCorpus.Mix(hash, (uint)c.GetHashCode());
                hash = FixedVectorReplayCorpus.Mix(hash, FixedVector3.Approximately(c, c) ? 1UL : 0UL);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)((FixedVector2)c).x.RawValue);
                // Exercise the public codec facade with the same shared runtime used by geometry.
                byte* bytes = stackalloc byte[30];
                var profile = new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3);
                var writer = new FixedBufferWriter(bytes, 30);
                bool written = FixedVectorNetwork.TryWrite(ref writer, c, profile, out status);
                hash = FixedVectorReplayCorpus.Mix(hash, written ? 1UL : 0UL);
                hash = FixedVectorReplayCorpus.Mix(hash, (uint)status);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                bool read = FixedVectorNetwork.TryRead(ref reader, profile, out FixedVector3 restored, out status);
                hash = FixedVectorReplayCorpus.Mix(hash, read && restored == c ? 1UL : 0UL);
                hash = FixedVectorReplayCorpus.Mix(hash, (uint)status);
                hash = FixedVectorReplayCorpus.Mix(hash, (ulong)reader.BitPosition);
                int managed = 0; MarkManaged(ref managed); Output[0] = hash; Output[1] = (ulong)managed;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test] public void PublicApiUsesTheSharedRuntimeInANativeJob()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var managed = new NativeArray<ulong>(2, Allocator.TempJob))
            using (var native = new NativeArray<ulong>(2, Allocator.TempJob))
            {
                new PublicApiJob { Output = managed }.Execute(); new PublicApiJob { Output = native }.Schedule().Complete();
                Assert.AreEqual(managed[0], native[0]); Assert.AreEqual(1, managed[1]); Assert.AreEqual(0, native[1]);
            }
        }
    }
}
