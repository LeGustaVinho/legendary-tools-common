using System;
using System.Diagnostics;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedRandomPlayerValidation
    {
        [Serializable] private class Report
        {
            public bool Passed, PublicApiPassed;
            public int Version, Cases, NativeCases;
            public ulong Checksum;
            public bool StressPassed;
            public int StressCases, StressNativeCases, StressOperations;
            public ulong StressChecksum;
            public string Platform, Error;
            public double RawNsPerOp, FixedNsPerOp, InsideSphereNsPerOp, OnSphereNsPerOp;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct CorpusJob : IJobParallelFor
        {
            internal NativeArray<FixedRandomCorpus.Result> Results;
            public void Execute(int index)
            { var result = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(index)); MarkManaged(ref result.Managed); Results[index] = result; }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
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
        [BurstCompile(CompileSynchronously = true)]
        private struct AdaptersJob : IJob
        {
            internal NativeArray<Fixed32> A;
            internal NativeArray<UFixed32> B;
            internal NativeArray<Fixed64> C;
            internal NativeArray<UFixed64> D;
            internal NativeArray<int> Values, Selected, Scratch;
            internal NativeArray<byte> Bytes;
            internal NativeArray<ulong> Prefix, Output;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Output[0] = (ulong)managed;
                var random = FixedRandom.Create(778); ulong errors = 0;
                if (!FixedRandomNative.TryFill(ref random, A, out var status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, B, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, C, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFill(ref random, D, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryFillBytes(ref random, Bytes, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryShuffle(ref random, Values, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryChoose(ref random, Values, out int chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TrySampleWithoutReplacement(ref random, Values.Length, Selected, Scratch, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryBuildWeights(C, Prefix, out var table, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, table, out chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, A, out chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, B, out chosen, out status)) errors++; errors |= (uint)status;
                if (!FixedRandomNative.TryWeightedIndex(ref random, D, out chosen, out status)) errors++; errors |= (uint)status;
                Output[1] = errors;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct BenchmarkJob : IJob
        {
            internal int Operation, Count;
            internal NativeArray<ulong> Output;
            public void Execute()
            {
                var r = FixedRandom.Create(1); ulong sum = 0;
                for (int i = 0; i < Count; i++)
                    sum = unchecked(sum + (Operation == 0 ? r.NextUInt32() : Operation == 1 ? (ulong)r.NextFixed64().RawValue :
                        Operation == 2 ? (ulong)r.InsideUnitSphere().X.RawValue : (ulong)r.OnUnitSphere().X.RawValue));
                int managed = 0; MarkManaged(ref managed); Output[0] = sum; Output[1] = (ulong)managed;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
#if !LEGENDARY_FIXEDPOINT_VALIDATION_ANDROID
            if (Array.IndexOf(args, "--fixed-random-validation") < 0) return;
#endif
            string path = Path.Combine(Application.persistentDataPath, "fixed-random-validation.json");
            foreach (string arg in args) if (arg.StartsWith("--fixed-random-report=", StringComparison.Ordinal)) path = arg.Substring("--fixed-random-report=".Length);
            var report = new Report { Version = FixedRandomCorpus.Version, Platform = Application.platform + " " + SystemInfo.processorType };
            try
            {
                FixedRuntime.Initialize();
                using (var native = new NativeArray<FixedRandomCorpus.Result>(FixedRandomCorpus.Count, Allocator.Persistent))
                {
                    new CorpusJob { Results = native }.Schedule(native.Length, 7).Complete(); report.Checksum = 14695981039346656037UL;
                    for (int i = 0; i < native.Length; i++)
                    {
                        var managed = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(i)); var result = native[i];
                        Require(result.Managed == 0, "Managed fallback: " + i);
                        Require(managed.Same(ref result), "Managed/Burst raw/status/state/wire mismatch: " + i);
                        Require(result.Digest() == FixedRandomCorpus.ExpectedCases[i], "Frozen v1 sample changed: " + i);
                        report.Checksum = result.Digest(report.Checksum); report.Cases++; report.NativeCases++;
                    }
                    Require(report.Checksum == FixedRandomCorpus.ExpectedChecksum, "Frozen random v1 checksum changed.");
                }
                ValidateReplayStress(report);
                ValidateAdapters(); report.PublicApiPassed = true;
                report.RawNsPerOp = Benchmark(0, 200000); report.FixedNsPerOp = Benchmark(1, 200000);
                report.InsideSphereNsPerOp = Benchmark(2, 10000); report.OnSphereNsPerOp = Benchmark(3, 2000);
                report.Passed = true;
            }
            catch (Exception e) { report.Error = e.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log(JsonUtility.ToJson(report)); Application.Quit(report.Passed ? 0 : 1);
        }
        private static void ValidateReplayStress(Report report)
        {
            using (var forward = new NativeArray<FixedRandomReplayStress.Result>(FixedRandomReplayStress.Cases, Allocator.TempJob))
            using (var reverse = new NativeArray<FixedRandomReplayStress.Result>(FixedRandomReplayStress.Cases, Allocator.TempJob))
            {
                new ReplayStressJob { Results = forward }.Schedule(forward.Length, 1).Complete();
                new ReplayStressJob { Results = reverse, Reverse = true }.Schedule(reverse.Length, 13).Complete();
                report.StressChecksum = 14695981039346656037UL;
                for (int i = 0; i < forward.Length; i++)
                {
                    var managed = FixedRandomReplayStress.Evaluate(i); var a = forward[i]; var b = reverse[reverse.Length - 1 - i];
                    Require(a.Managed == 0 && b.Managed == 0, "Replay stress managed fallback: " + i);
                    Require(a.Failures == 0, "Replay stress invariant failed: " + i);
                    Require(managed.Same(ref a) && a.Same(ref b), "Replay stress raw/status/state/wire/order mismatch: " + i);
                    Require(a.Digest() == FixedRandomReplayStress.ExpectedCases[i], "Replay stress frozen fixture changed: " + i);
                    report.StressChecksum = a.Digest(report.StressChecksum); report.StressCases++; report.StressNativeCases += 2;
                }
                Require(report.StressChecksum == FixedRandomReplayStress.ExpectedChecksum, "Replay stress checksum changed.");
                report.StressOperations = report.StressCases * FixedRandomReplayStress.Steps;
                report.StressPassed = true;
            }
        }
        private static void ValidateAdapters()
        {
            using (var a = new NativeArray<Fixed32>(16, Allocator.TempJob))
            using (var b = new NativeArray<UFixed32>(16, Allocator.TempJob))
            using (var c = new NativeArray<Fixed64>(16, Allocator.TempJob))
            using (var d = new NativeArray<UFixed64>(16, Allocator.TempJob))
            using (var bytes = new NativeArray<byte>(19, Allocator.TempJob))
            using (var values = new NativeArray<int>(12, Allocator.TempJob))
            using (var selected = new NativeArray<int>(4, Allocator.TempJob))
            using (var scratch = new NativeArray<int>(12, Allocator.TempJob))
            using (var prefix = new NativeArray<ulong>(16, Allocator.TempJob))
            using (var output = new NativeArray<ulong>(2, Allocator.TempJob))
            {
                var writable = values; for (int i = 0; i < values.Length; i++) writable[i] = i;
                new AdaptersJob { A = a, B = b, C = c, D = d, Bytes = bytes, Values = values, Selected = selected, Scratch = scratch, Prefix = prefix, Output = output }.Schedule().Complete();
                Require(output[0] == 0 && output[1] == 0, "NativeArray adapters did not pass in native Burst.");
            }
        }
        private static double Benchmark(int operation, int count)
        {
            using (var output = new NativeArray<ulong>(2, Allocator.TempJob))
            {
                var job = new BenchmarkJob { Operation = operation, Count = count, Output = output }; job.Schedule().Complete();
                var timer = Stopwatch.StartNew(); job.Schedule().Complete(); timer.Stop(); Require(output[1] == 0, "Benchmark ran managed.");
                return timer.Elapsed.TotalMilliseconds * 1000000 / count;
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
