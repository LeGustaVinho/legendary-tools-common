#if !FIXEDPOINT_STANDALONE
using System;
using System.IO;
using System.Runtime.InteropServices;
using LegendaryTools.FixedPoint;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace LegendaryTools.Deterministic
{
    public unsafe struct SpatialNativeCorpusOwner<TBounds, TAccess, TCodec> : ISpatialCorpusOwner<TBounds>
        where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
    {
        public NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Index;
        private NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> snapshot;
        public DeterministicStatus Apply(in SpatialMutation<int, int, TBounds> mutation) => Index.TryApply(mutation);
        public DeterministicStatus Rebuild() => Index.TryRebuild();
        public DeterministicStatus Capture() => Index.TryCapture(out snapshot);
        public DeterministicStatus Restore() => Index.TryRestore(snapshot);
        public void Release() => snapshot.Dispose();
        public DeterministicStatus Query(in SpatialQuery query, SpatialQueryScratch scratch, SpatialHit<int>* hits, out int written) => Index.TryQuery(query, scratch, hits, SpatialCorpus.Records, out written);
        public DeterministicStatus Write(ref DeterministicWriter writer) => Index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec));
    }
    public static unsafe class SpatialPlayerValidation
    {
        [Serializable] private sealed class Report
        {
            public bool Passed;
            public int Version = SpatialCorpus.Version, Cases, NativeCases, Schedules, ReplayedSteps;
            public long ComparedWords;
            public ulong Checksum2, Checksum3, Checksum;
            public int PermutationVersion = SpatialPermutationCorpus.Version, PermutationCases, PermutationNativeCases, PermutationSchedules, PermutationReplayedSteps;
            public long PermutationComparedWords;
            public ulong PermutationChecksum2, PermutationChecksum3;
            public string Architecture, Backend, Representation, Error;
        }
        [BurstCompile(CompileSynchronously = true)] private struct Job2 : IJob
        {
            internal NativeDeterministicSpatialIndex<int, int, FixedBounds2, SpatialBounds2Access, Int32Policy> Index;
            internal NativeArray<ulong> Trace;
            internal NativeArray<int> Result;
            internal int Seed, Permutation;
            internal bool Permuted;
            public void Execute()
            {
                var owner = new SpatialNativeCorpusOwner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy> { Index = Index };
                int words, replayed;
                var status = Permuted ? SpatialPermutationCorpus.Evaluate<FixedBounds2, SpatialBounds2Access, SpatialNativeCorpusOwner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>>(
                    ref owner, Seed, Permutation, (ulong*)Trace.GetUnsafePtr(), Trace.Length, out words, out replayed) :
                    SpatialCorpus.Evaluate<FixedBounds2, SpatialBounds2Access, SpatialNativeCorpusOwner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>>(
                    ref owner, Seed, (ulong*)Trace.GetUnsafePtr(), Trace.Length, out words, out replayed);
                Result[0] = (int)status;
                Result[1] = words; Result[2] = replayed; int marker = 0; Managed(ref marker); Result[3] = marker;
            }
            [BurstDiscard] private static void Managed(ref int marker) => marker = 1;
        }
        [BurstCompile(CompileSynchronously = true)] private struct Job3 : IJob
        {
            internal NativeDeterministicSpatialIndex<int, int, FixedBounds3, SpatialBounds3Access, Int32Policy> Index;
            internal NativeArray<ulong> Trace;
            internal NativeArray<int> Result;
            internal int Seed, Permutation;
            internal bool Permuted;
            public void Execute()
            {
                var owner = new SpatialNativeCorpusOwner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy> { Index = Index };
                int words, replayed;
                var status = Permuted ? SpatialPermutationCorpus.Evaluate<FixedBounds3, SpatialBounds3Access, SpatialNativeCorpusOwner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>>(
                    ref owner, Seed, Permutation, (ulong*)Trace.GetUnsafePtr(), Trace.Length, out words, out replayed) :
                    SpatialCorpus.Evaluate<FixedBounds3, SpatialBounds3Access, SpatialNativeCorpusOwner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>>(
                    ref owner, Seed, (ulong*)Trace.GetUnsafePtr(), Trace.Length, out words, out replayed);
                Result[0] = (int)status;
                Result[1] = words; Result[2] = replayed; int marker = 0; Managed(ref marker); Result[3] = marker;
            }
            [BurstDiscard] private static void Managed(ref int marker) => marker = 1;
        }
        private static ulong Validate<TBounds, TAccess, TCodec>(Report report, bool permuted = false)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            int dimensions = default(TAccess).Dimensions;
            int capacity = permuted ? SpatialPermutationCorpus.Capacity : SpatialCorpus.Capacity;
            int expectedReplay = permuted ? SpatialPermutationCorpus.Steps - SpatialPermutationCorpus.Checkpoint - 1 : SpatialCorpus.Steps - SpatialCorpus.Checkpoint - 1;
            var reference = new ulong[SpatialCorpus.Cases / 2][];
            var lengths = new int[reference.Length]; ulong expected = 1;
            for (int scenario = 0; scenario < reference.Length; scenario++)
            {
                reference[scenario] = new ulong[capacity];
                using var index = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(SpatialCorpus.Options(scenario));
                var owner = new SpatialManagedCorpusOwner<TBounds, TAccess, TCodec> { Index = index };
                fixed (ulong* trace = reference[scenario])
                {
                    int replayed;
                    var status = permuted ? SpatialPermutationCorpus.Evaluate<TBounds, TAccess, SpatialManagedCorpusOwner<TBounds, TAccess, TCodec>>(ref owner, scenario / 6, 0, trace, capacity, out lengths[scenario], out replayed) :
                        SpatialCorpus.Evaluate<TBounds, TAccess, SpatialManagedCorpusOwner<TBounds, TAccess, TCodec>>(ref owner, scenario / 6, trace, capacity, out lengths[scenario], out replayed);
                    status.ThrowIfFailure();
                    if (replayed != expectedReplay) throw new InvalidOperationException("Managed replay count differs.");
                    expected = DeterministicHash.Mix(expected ^ SpatialCorpus.Hash(trace, lengths[scenario]));
                }
                if (permuted) report.PermutationCases++; else report.Cases++;
            }
            if (expected != (permuted ? SpatialPermutationCorpus.ExpectedChecksum(dimensions) : SpatialCorpus.ExpectedChecksum(dimensions))) throw new InvalidOperationException("Managed player differs from the versioned standalone corpus.");
            using var actual = new NativeArray<ulong>(capacity, Allocator.Persistent);
            using var result = new NativeArray<int>(4, Allocator.Persistent);
            for (int pass = 0; pass < 3; pass++)
            {
                var digests = new ulong[reference.Length];
                for (int ordinal = 0; ordinal < reference.Length; ordinal++)
                {
                    int scenario = pass == 0 ? ordinal : pass == 1 ? reference.Length - 1 - ordinal : (ordinal * 5 + 7) % reference.Length;
                    var native = new NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(SpatialCorpus.Options(scenario), Allocator.Persistent);
                    try
                    {
                        if (dimensions == 2)
                            new Job2 { Index = *(NativeDeterministicSpatialIndex<int, int, FixedBounds2, SpatialBounds2Access, Int32Policy>*)&native, Trace = actual, Result = result, Seed = scenario / 6, Permuted = permuted, Permutation = pass }.Schedule().Complete();
                        else new Job3 { Index = *(NativeDeterministicSpatialIndex<int, int, FixedBounds3, SpatialBounds3Access, Int32Policy>*)&native, Trace = actual, Result = result, Seed = scenario / 6, Permuted = permuted, Permutation = pass }.Schedule().Complete();
                        if (result[0] != 0 || result[3] != 0 || result[1] != lengths[scenario] || result[2] != expectedReplay)
                        {
                            int first = -1; ulong expectedWord = 0, actualWord = 0;
                            for (int i = 0; i < Math.Min(result[1], lengths[scenario]); i++) if (actual[i] != reference[scenario][i])
                            { first = i; expectedWord = reference[scenario][i]; actualWord = actual[i]; break; }
                            throw new InvalidOperationException($"Native corpus status/marker/length/replay differs: d={dimensions} scenario={scenario} status={result[0]} marker={result[3]} words={result[1]}/{lengths[scenario]} replay={result[2]} first={first} values={actualWord}/{expectedWord}");
                        }
                        fixed (ulong* trace = reference[scenario]) for (int i = 0; i < result[1]; i++)
                            if (actual[i] != trace[i]) throw new InvalidOperationException($"Native corpus word differs: d={dimensions} scenario={scenario} schedule={pass} word={i}");
                        digests[scenario] = SpatialCorpus.Hash((ulong*)actual.GetUnsafeReadOnlyPtr(), result[1]);
                        if (permuted) { report.PermutationComparedWords += result[1]; report.PermutationReplayedSteps += result[2]; if (pass == 0) report.PermutationNativeCases++; }
                        else { report.ComparedWords += result[1]; report.ReplayedSteps += result[2]; if (pass == 0) report.NativeCases++; }
                    }
                    finally { native.Dispose(); }
                }
                ulong checksum = 1; for (int i = 0; i < digests.Length; i++) checksum = DeterministicHash.Mix(checksum ^ digests[i]);
                if (checksum != expected) throw new InvalidOperationException("Native schedule checksum differs.");
                if (permuted) report.PermutationSchedules++; else report.Schedules++;
            }
            return expected;
        }
        public static string VerifyInEditor()
        {
            var report = new Report();
            report.Checksum2 = Validate<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(report);
            report.Checksum3 = Validate<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(report);
            report.PermutationChecksum2 = Validate<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(report, true);
            report.PermutationChecksum3 = Validate<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(report, true);
            report.Checksum = DeterministicHash.Mix(report.Checksum2 ^ report.Checksum3); report.Passed = true;
            return JsonUtility.ToJson(report);
        }
#if LEGENDARY_SPATIAL_VALIDATION
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--spatial-validation") < 0) return;
            string path = Path.Combine(Application.persistentDataPath, "spatial-validation.json"), mode = "min-max";
            foreach (string arg in args)
            { if (arg.StartsWith("--spatial-report=")) path = arg.Substring("--spatial-report=".Length); if (arg.StartsWith("--spatial-bounds-mode=")) mode = arg.Substring("--spatial-bounds-mode=".Length); }
            var report = new Report { Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Backend = "Mono", Representation = mode };
#if ENABLE_IL2CPP
            report.Backend = "IL2CPP";
#endif
            try
            {
                FixedRuntime.Initialize(); FixedBoundsRuntime.Initialize(new FixedBoundsSettings(mode == "center-extents" ? FixedBoundsRepresentation.CenterExtents : FixedBoundsRepresentation.MinMax));
                report.Checksum2 = Validate<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(report);
                report.Checksum3 = Validate<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(report);
                report.PermutationChecksum2 = Validate<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(report, true);
                report.PermutationChecksum3 = Validate<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(report, true);
                report.Checksum = DeterministicHash.Mix(report.Checksum2 ^ report.Checksum3);
                report.Passed = true;
            }
            catch (Exception exception) { report.Error = exception.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Spatial player validation: " + path + " passed=" + report.Passed); Application.Quit(report.Passed ? 0 : 1);
        }
#endif
    }
}
#endif
