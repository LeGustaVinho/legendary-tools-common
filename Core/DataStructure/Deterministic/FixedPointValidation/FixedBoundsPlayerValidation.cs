using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedBoundsPlayerValidation
    {
        [Serializable] private sealed class Report
        {
            public bool Passed;
            public int Version = FixedBoundsReplayCorpus.Version, Cases, NativeCases;
            public ulong Checksum;
            public int StressVersion = FixedBoundsStressCorpus.Version, StressCases, StressNativeCases, StressSteps, StressReplayedSteps, StressSchedules;
            public long StressComparedWords;
            public ulong StressChecksum;
            public string Platform, Architecture, Backend, Representation, Error;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct CorpusJob : IJobParallelFor
        {
            internal NativeArray<FixedBoundsReplayCorpus.Result> Results;
            public void Execute(int index)
            { var result = FixedBoundsReplayCorpus.Evaluate(index); MarkManaged(ref result.Managed); Results[index] = result; }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct StressJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] internal NativeArray<FixedBoundsStressCorpus.Result> Results;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedBoundsStressCorpus.Record> Trace;
            internal int Permutation;
            public void Execute(int index)
            {
                if (Permutation == 1) index = Results.Length - 1 - index;
                else if (Permutation == 2) index = (index * 17 + 23) % Results.Length;
                var records = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafePtr(Trace) + index * FixedBoundsStressCorpus.Steps;
                var result = FixedBoundsStressCorpus.Evaluate(index, records); MarkManaged(ref result.Managed); Results[index] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        private static unsafe void ValidateStress(Report report)
        {
            using var managed = new NativeArray<FixedBoundsStressCorpus.Result>(FixedBoundsStressCorpus.Count, Allocator.Persistent);
            using var native = new NativeArray<FixedBoundsStressCorpus.Result>(managed.Length, Allocator.Persistent);
            using var managedTrace = new NativeArray<FixedBoundsStressCorpus.Record>(managed.Length * FixedBoundsStressCorpus.Steps, Allocator.Persistent);
            using var nativeTrace = new NativeArray<FixedBoundsStressCorpus.Record>(managedTrace.Length, Allocator.Persistent);
            var reference = new StressJob { Results = managed, Trace = managedTrace }; for (int i = 0; i < managed.Length; i++) reference.Execute(i);
            var a = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(managedTrace);
            var b = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(nativeTrace);
            for (int pass = 0; pass < 3; pass++)
            {
                new StressJob { Results = native, Trace = nativeTrace, Permutation = pass }.Schedule(native.Length, pass == 0 ? 1 : pass == 1 ? 17 : 64).Complete();
                ulong checksum = 14695981039346656037UL;
                for (int i = 0; i < native.Length; i++)
                {
                    if (managed[i].Managed != 1 || native[i].Managed != 0 || managed[i].Errors != 0 || native[i].Errors != 0 || managed[i].Hash != native[i].Hash || managed[i].Replayed != 32 || native[i].Replayed != 32)
                        throw new InvalidOperationException("Bounds sequence/restore mismatch at " + i + ", schedule " + pass);
                    for (int tick = 0; tick < FixedBoundsStressCorpus.Steps; tick++)
                    {
                        int at = i * FixedBoundsStressCorpus.Steps + tick;
                        if (!FixedBoundsStressCorpus.Equal(a + at, b + at)) throw new InvalidOperationException("Bounds direct trace mismatch at " + i + ", step " + tick + ", schedule " + pass);
                        report.StressComparedWords += FixedBoundsStressCorpus.Record.Capacity;
                    }
                    checksum = FixedBoundsReplayCorpus.Mix(checksum, native[i].Hash);
                }
                if (checksum != FixedBoundsStressCorpus.ExpectedChecksum) throw new InvalidOperationException("Bounds sequence golden checksum differs.");
                report.StressChecksum = checksum; report.StressSchedules++;
            }
            report.StressCases = managed.Length; report.StressNativeCases = native.Length;
            report.StressSteps = managedTrace.Length; report.StressReplayedSteps = managed.Length * (FixedBoundsStressCorpus.Steps - FixedBoundsStressCorpus.CheckpointStep);
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct PublicJob : IJob
        {
            internal NativeArray<ulong> Result;
            public void Execute()
            {
                var box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(-3, -5, -7), FixedVector3.FromRaw(11, 13, 17));
                box.TryExpand(Fixed64.FromRaw(1), out var status); box.TryEncapsulate(FixedVector3.FromRaw(50, 60, 70), out status);
                box.center = box.center; box.extents = box.extents;
                var point = box.ClosestPoint(FixedVector3.zero); var d = box.SqrDistance(FixedVector3.zero, out status);
                var ray = new FixedRay3(FixedVector3.FromRaw(-50, 0, 0), FixedVector3.right);
                bool contact = box.IntersectRay(ray, out var t); var rayPoint = ray.GetPoint(t, out status);
                var flat = FixedBounds2.FromCenterExtents(FixedVector2.zero, FixedVector2.one);
                flat.Encapsulate(new FixedVector2(2, 3)); flat.TrySetSize(new FixedVector2(8, 10), out status);
                byte* bytes = stackalloc byte[60]; var profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3));
                var writer = new FixedBufferWriter(bytes, 60); bool written = FixedBoundsNetwork.TryWrite(ref writer, box, profile, out status);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten); bool read = FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 restored, out status);
                ulong hash = 14695981039346656037UL;
                hash = FixedBoundsReplayCorpus.Mix(hash, unchecked((uint)box.GetHashCode())); hash = FixedBoundsReplayCorpus.Mix(hash, unchecked((uint)flat.GetHashCode()));
                hash = FixedBoundsReplayCorpus.Mix(hash, (ulong)d.RawValue); hash = FixedBoundsReplayCorpus.Mix(hash, (ulong)t.RawValue);
                hash = FixedBoundsReplayCorpus.Mix(hash, (ulong)point.x.RawValue); hash = FixedBoundsReplayCorpus.Mix(hash, (ulong)rayPoint.x.RawValue);
                hash = FixedBoundsReplayCorpus.Mix(hash, contact ? 1UL : 0UL);
                Result[0] = hash; Result[1] = written && read && restored == box && status.IsSuccess() ? 1UL : 0UL;
                int managed = 0; MarkManaged(ref managed); Result[2] = (ulong)managed;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
#if !LEGENDARY_FIXEDBOUNDS_VALIDATION_ANDROID
            if (Array.IndexOf(args, "--fixed-bounds-validation") < 0) return;
#endif
            string path = Path.Combine(Application.persistentDataPath, "fixed-bounds-validation.json");
            string mode = "min-max";
            foreach (string arg in args)
            {
                if (arg.StartsWith("--fixed-bounds-report=", StringComparison.Ordinal)) path = arg.Substring("--fixed-bounds-report=".Length);
                if (arg.StartsWith("--fixed-bounds-mode=", StringComparison.Ordinal)) mode = arg.Substring("--fixed-bounds-mode=".Length);
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
            { string requested = intent.Call<string>("getStringExtra", "bounds-mode"); if (!string.IsNullOrEmpty(requested)) mode = requested; }
#endif
            var report = new Report { Platform = Application.platform.ToString(), Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Representation = mode };
#if ENABLE_IL2CPP
            report.Backend = "IL2CPP";
#else
            report.Backend = "Mono";
#endif
            try
            {
                if (mode != "min-max" && mode != "center-extents") throw new ArgumentException("Unknown bounds mode: " + mode);
                FixedRuntime.Initialize(); FixedBoundsRuntime.Initialize(new FixedBoundsSettings(mode == "min-max" ? FixedBoundsRepresentation.MinMax : FixedBoundsRepresentation.CenterExtents));
                if (UnsafeUtility.SizeOf<FixedBounds2>() != 32 || UnsafeUtility.SizeOf<FixedBounds3>() != 48) throw new InvalidOperationException("Bounds layout differs in player.");
                using (var managed = new NativeArray<ulong>(3, Allocator.Persistent)) using (var native = new NativeArray<ulong>(3, Allocator.Persistent))
                {
                    new PublicJob { Result = managed }.Execute(); new PublicJob { Result = native }.Schedule().Complete();
                    if (managed[2] != 1 || native[2] != 0 || native[1] != 1 || managed[0] != native[0]) throw new InvalidOperationException("Public bounds API/shared runtime failed native parity.");
                }
                using (var managed = new NativeArray<FixedBoundsReplayCorpus.Result>(FixedBoundsReplayCorpus.Count, Allocator.Persistent))
                using (var native = new NativeArray<FixedBoundsReplayCorpus.Result>(managed.Length, Allocator.Persistent))
                {
                    var reference = new CorpusJob { Results = managed }; for (int i = 0; i < managed.Length; i++) reference.Execute(i);
                    new CorpusJob { Results = native }.Schedule(native.Length, 17).Complete(); report.Checksum = 14695981039346656037UL;
                    for (int i = 0; i < native.Length; i++)
                    {
                        if (managed[i].Managed != 1 || native[i].Managed != 0 || managed[i].Hash != native[i].Hash) throw new InvalidOperationException("Bounds native corpus mismatch at " + i);
                        report.Checksum = FixedBoundsReplayCorpus.Mix(report.Checksum, native[i].Hash); report.Cases++; report.NativeCases++;
                    }
                }
                ValidateStress(report);
                report.Passed = report.Cases == FixedBoundsReplayCorpus.Count && report.Checksum == FixedBoundsReplayCorpus.ExpectedChecksum && report.StressCases == FixedBoundsStressCorpus.Count && report.StressChecksum == FixedBoundsStressCorpus.ExpectedChecksum;
                if (!report.Passed) report.Error = "Bounds corpus differs from the versioned golden checksum.";
            }
            catch (Exception exception) { report.Error = exception.ToString(); report.Passed = false; }
            string parent = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(parent);
            File.WriteAllText(path, JsonUtility.ToJson(report, true)); Debug.Log(JsonUtility.ToJson(report)); Application.Quit(report.Passed ? 0 : 1);
        }
    }
}
