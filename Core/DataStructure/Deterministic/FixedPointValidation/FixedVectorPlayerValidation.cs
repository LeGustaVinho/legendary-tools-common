using System;
using System.IO;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedVectorPlayerValidation
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SequenceJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedVectorReplayCorpus.Sample> Samples;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedVectorReplayCorpus.Result> Results;
            public void Execute(int index)
            {
                var sample = Samples[index]; FixedVectorBits position = sample.A, velocity = default;
                for (int tick = 0; tick < FixedVectorReplayCorpus.SequenceTicks; tick++)
                {
                    var result = FixedVectorReplayCorpus.SequenceStep(sample, tick, ref position, ref velocity);
                    MarkManaged(ref result.Managed); Results[index * FixedVectorReplayCorpus.SequenceTicks + tick] = result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static int ValidateSequences(NativeArray<FixedVectorReplayCorpus.Sample> allSamples, TextWriter writer)
        {
            var indices = new List<int>(); var selected = new List<FixedVectorReplayCorpus.Sample>();
            for (int i = 0; i < allSamples.Length; i++) if (allSamples[i].Operation == 23) { indices.Add(i); selected.Add(allSamples[i]); }
            if (selected.Count != FixedVectorReplayCorpus.SequenceCases) throw new InvalidOperationException("Unexpected vector sequence count.");
            using (var samples = new NativeArray<FixedVectorReplayCorpus.Sample>(selected.ToArray(), Allocator.Persistent))
            using (var managed = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length * FixedVectorReplayCorpus.SequenceTicks, Allocator.Persistent))
            using (var native = new NativeArray<FixedVectorReplayCorpus.Result>(managed.Length, Allocator.Persistent))
            {
                var reference = new SequenceJob { Samples = samples, Results = managed };
                for (int i = 0; i < samples.Length; i++) reference.Execute(i);
                new SequenceJob { Samples = samples, Results = native }.Schedule(samples.Length, 17).Complete();
                for (int i = 0; i < managed.Length; i++)
                {
                    var m = managed[i]; var n = native[i];
                    if (m.Managed != 1 || n.Managed != 0) throw new InvalidOperationException("Vector tick did not execute native Burst.");
                    m.Managed = 0;
                    if (!m.Equals(n)) throw new InvalidOperationException("Vector simulation tick mismatch: " + i);
                    if (writer != null) FixedVectorReplayCorpus.WriteTraceRow(writer, indices[i / FixedVectorReplayCorpus.SequenceTicks], i % FixedVectorReplayCorpus.SequenceTicks,
                        samples[i / FixedVectorReplayCorpus.SequenceTicks], n);
                }
                return native.Length;
            }
        }
        private struct PublicResult
        {
            internal FixedVector2 Direction;
            internal FixedVector3 Cross, Motion, Wire;
            internal FixedStatus Status;
            internal int CodecSucceeded, Managed;
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct PublicApiJob : IJob
        {
            internal NativeArray<PublicResult> Results;
            public void Execute()
            {
                var result = new PublicResult {
                    Direction = new FixedVector2(3, 4).normalized,
                    Cross = FixedVector3.Cross(FixedVector3.right, FixedVector3.up)
                };
                FixedVector3 velocity = default;
                result.Motion = FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity,
                    1, Fixed64.PositiveInfinity, Fixed64.FromRatio(1, 60), out result.Status);
                byte* bytes = stackalloc byte[30];
                var profile = new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3);
                var writer = new FixedBufferWriter(bytes, 30);
                bool written = FixedVectorNetwork.TryWrite(ref writer, result.Motion, profile, out FixedStatus writeStatus);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                bool read = FixedVectorNetwork.TryRead(ref reader, profile, out result.Wire, out FixedStatus readStatus);
                result.Status |= writeStatus | readStatus;
                result.CodecSucceeded = written && read && result.Motion == result.Wire ? 1 : 0;
                MarkManaged(ref result.Managed); Results[0] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static void ValidatePublicApi()
        {
            if (UnsafeUtility.SizeOf<FixedVector2>() != 16 || UnsafeUtility.SizeOf<FixedVector3>() != 24)
                throw new InvalidOperationException("Unexpected vector layout in player.");
            using (var managed = new NativeArray<PublicResult>(1, Allocator.Persistent))
            using (var native = new NativeArray<PublicResult>(1, Allocator.Persistent))
            {
                new PublicApiJob { Results = managed }.Execute(); new PublicApiJob { Results = native }.Schedule().Complete();
                var m = managed[0]; var n = native[0];
                if (m.Managed != 1 || n.Managed != 0) throw new InvalidOperationException("Public vector API did not execute native Burst.");
                m.Managed = 0;
                if (!m.Equals(n) || !n.Status.IsSuccess() || n.CodecSucceeded != 1 ||
                    n.Direction != new FixedVector2(Fixed64.FromRatio(3, 5), Fixed64.FromRatio(4, 5)) || n.Cross != FixedVector3.forward)
                    throw new InvalidOperationException("Public vector API/layout/codec mismatch in player.");
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct VectorJob : IJob
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
        internal static void Run(out int cases, out int nativeCases, out ulong checksum, out ulong inputChecksum, out int sequenceTicks, string tracePath)
        {
            cases = nativeCases = 0; checksum = 14695981039346656037UL;
            inputChecksum = checksum; sequenceTicks = 0;
            ValidatePublicApi();
            using (var trace = tracePath == null ? null : new StreamWriter(tracePath))
            using (var samples = new NativeArray<FixedVectorReplayCorpus.Sample>(FixedVectorReplayCorpus.Create(), Allocator.Persistent))
            using (var managed = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length, Allocator.Persistent))
            using (var native = new NativeArray<FixedVectorReplayCorpus.Result>(samples.Length, Allocator.Persistent))
            {
                new VectorJob { Samples = samples, Results = managed }.Execute();
                new VectorJob { Samples = samples, Results = native }.Schedule().Complete();
                for (int i = 0; i < samples.Length; i++)
                {
                    var m = managed[i]; var n = native[i];
                    if (m.Managed != 1 || n.Managed != 0) throw new InvalidOperationException("Vector replay did not execute native Burst.");
                    m.Managed = 0;
                    if (!m.Equals(n)) throw new InvalidOperationException("Vector managed/native mismatch at sample " + i);
                    checksum = FixedVectorReplayCorpus.Mix(checksum, FixedVectorReplayCorpus.Hash(n)); cases++; nativeCases++;
                    inputChecksum = FixedVectorReplayCorpus.Mix(inputChecksum, FixedVectorReplayCorpus.HashInput(samples[i]));
                    if (trace != null) FixedVectorReplayCorpus.WriteTraceRow(trace, i, -1, samples[i], n);
                }
                if (cases != FixedVectorReplayCorpus.ExpectedCases || checksum != FixedVectorReplayCorpus.ExpectedChecksum || inputChecksum != FixedVectorReplayCorpus.ExpectedInputChecksum)
                    throw new InvalidOperationException("Vector replay differs from the versioned golden checksum.");
                sequenceTicks = ValidateSequences(samples, trace);
            }
        }
    }
}
