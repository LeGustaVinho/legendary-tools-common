using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Included only by validation builds; Android validation starts automatically.</summary>
    internal static class FixedPlayerValidation
    {
        [Serializable] private class Report
        {
            public bool Passed, PublicApiPassed;
            public bool VectorPublicApiPassed;
            public bool QuaternionPublicApiPassed;
            public int QuaternionCases, NativeQuaternionCases, QuaternionReplayVersion;
            public ulong QuaternionChecksum;
            public int QuaternionSequenceCases, NativeQuaternionSequenceCases, QuaternionSequenceVersion;
            public ulong QuaternionSequenceChecksum;
            public string QuaternionNormalization;
            public int Cases, NativeCases, ReplayCases, NativeReplayCases, ReplayVersion;
            public int VectorCases, NativeVectorCases, VectorReplayVersion;
            public int NativeVectorSequenceTicks;
            public string Platform, Error;
            public ulong Checksum, ReplayChecksum, ReplayInputChecksum;
            public ulong VectorChecksum;
            public ulong VectorInputChecksum;
        }
        private struct Sample
        { internal FixedKind Kind; internal FixedSettings Settings; internal FixedUnary Operation; internal ulong Raw; }
        private struct Result
        { internal ulong Raw, FloatBits, DoubleBits, RoundTrip; internal FixedStatus Status, WriteStatus, ReadStatus; internal int Managed; }

        [BurstCompile(CompileSynchronously = true)]
        private struct PublicApiJob : IJob
        {
            internal NativeArray<ulong> Results;
            public void Execute()
            {
                Fixed32 a = Fixed32.FromInt(2); UFixed32 b = UFixed32.FromUInt(2);
                Fixed64 c = Fixed64.FromInt(2); UFixed64 d = UFixed64.FromUInt(2);
                Results[0] = unchecked((uint)FixedMath.Sqrt(a * a).RawValue);
                Results[1] = FixedMath.Sqrt(b * b).RawValue;
                Results[2] = unchecked((ulong)FixedMath.Sqrt(c * c).RawValue);
                Results[3] = FixedMath.Sqrt(d * d).RawValue;
                int managed = 0; MarkManaged(ref managed); Results[4] = (ulong)managed;
                bool success = Fixed32.NegativeZero.TryToUInt64(out ulong zero, out FixedStatus status);
                Results[5] = zero; Results[6] = (uint)status; Results[7] = success ? 1UL : 0UL;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct ValidationJob : IJob
        {
            [ReadOnly] internal NativeArray<Sample> Samples;
            internal NativeArray<Result> Results;
            public void Execute()
            {
                byte* buffer = stackalloc byte[16];
                for (int i = 0; i < Samples.Length; i++)
                {
                    Sample s = Samples[i]; var state = new FixedRuntimeState(s.Settings); Result r = default;
                    MarkManaged(ref r.Managed);
                    r.Raw = FixedMathKernel.Unary(s.Raw, s.Kind, state, s.Operation, out r.Status);
                    r.FloatBits = FixedIeee.Bits(FixedIeee.ToSingle(s.Raw, s.Kind, state));
                    r.DoubleBits = FixedIeee.Bits(FixedIeee.ToDouble(s.Raw, s.Kind, state));
                    var profile = new FixedNetworkProfile(s.Settings, s.Kind);
                    var writer = new FixedBufferWriter(buffer, 16);
                    FixedNetworkKernel.Write(ref writer, s.Raw, profile, state, out r.WriteStatus);
                    var reader = new FixedBufferReader(buffer, writer.BytesWritten);
                    FixedNetworkKernel.Read(ref reader, profile, state, out r.RoundTrip, out r.ReadStatus);
                    Results[i] = r;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ReplayJob : IJob
        {
            [ReadOnly] internal NativeArray<FixedReplayCorpus.Sample> Samples;
            internal NativeArray<FixedReplayCorpus.Result> Results;
            public void Execute()
            {
                for (int i = 0; i < Samples.Length; i++)
                {
                    var result = FixedReplayCorpus.Evaluate(Samples[i]);
                    MarkManaged(ref result.Managed); Results[i] = result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] arguments = Environment.GetCommandLineArgs();
#if !LEGENDARY_FIXEDPOINT_VALIDATION_ANDROID
            if (Array.IndexOf(arguments, "--fixed-point-validation") < 0) return;
#endif
            string path = Path.Combine(Application.persistentDataPath, "fixed-point-validation.json");
            foreach (string argument in arguments) if (argument.StartsWith("--fixed-point-report=", StringComparison.Ordinal)) path = argument.Substring(21);
            var report = new Report { Platform = Application.platform + " " + SystemInfo.processorType };
            try
            {
                var quaternionNormalization = Array.IndexOf(arguments,"--fixed-quaternion-normalize-composition") >= 0 ? FixedQuaternionNormalizationMode.NormalizeResult : FixedQuaternionNormalizationMode.None;
                FixedRuntime.Initialize(new FixedSettings(quaternionNormalization:quaternionNormalization));
                report.QuaternionNormalization=quaternionNormalization.ToString();
                ValidatePublicApi(report);
                string[] lines = FixedPlayerReference.Vectors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int referenceCases = lines.Length * 2;
                using (var samples = new NativeArray<Sample>(referenceCases, Allocator.Persistent))
                using (var managed = new NativeArray<Result>(referenceCases, Allocator.Persistent))
                using (var native = new NativeArray<Result>(referenceCases, Allocator.Persistent))
                {
                    NativeArray<Sample> writable = samples;
                    for (int i = 0; i < referenceCases; i++)
                    {
                        string[] fields = lines[i % lines.Length].Split(','); int p = int.Parse(fields[1]);
                        writable[i] = new Sample { Kind = (FixedKind)int.Parse(fields[0]), Settings = new FixedSettings(Math.Min(p, 9), p,
                            mathProfile: (FixedMathProfile)(i / lines.Length)),
                            Operation = (FixedUnary)Enum.Parse(typeof(FixedUnary), fields[2]), Raw = ulong.Parse(fields[3]) };
                    }
                    new ValidationJob { Samples = samples, Results = managed }.Execute();
                    new ValidationJob { Samples = samples, Results = native }.Schedule().Complete();
                    for (int i = 0; i < referenceCases; i++)
                    {
                        string line = lines[i % lines.Length];
                        Result m = managed[i], n = native[i];
                        Require(m.Managed == 1 && n.Managed == 0, "Burst did not execute native code."); m.Managed = 0;
                        Require(m.Equals(n), "Managed/Burst mismatch: " + line);
                        Sample sample = samples[i]; var state = new FixedRuntimeState(sample.Settings); var f = new FixedFormat(sample.Kind, state);
                        string[] fields = line.Split(','); ulong expected = ulong.Parse(fields[4]);
                        Require((n.Status & ~FixedStatus.Inexact) == (FixedStatus)uint.Parse(fields[5]), "Reference status: " + line);
                        FixedDecoded e = f.Decode(expected), a = f.Decode(n.Raw);
                        if (e.IsFinite && a.IsFinite)
                        {
                            ulong difference = e.Negative == a.Negative ? e.Magnitude > a.Magnitude ? e.Magnitude - a.Magnitude : a.Magnitude - e.Magnitude :
                                FixedUInt128.Add(new FixedUInt128(e.Magnitude), new FixedUInt128(a.Magnitude), out _).Lo;
                            Require(difference <= (sample.Settings.MathProfile == FixedMathProfile.Accurate ? 1UL : 4UL), "Math error bound: " + line);
                        }
                        else Require(expected == n.Raw, "Special value mismatch: " + line);
                        Require(n.RoundTrip == sample.Raw && n.WriteStatus.IsSuccess() && n.ReadStatus.IsSuccess(), "Wire mismatch: " + line);
                        report.Cases++; report.NativeCases++;
                        report.Checksum = Mix(report.Checksum, n.Raw);
                        report.Checksum = Mix(report.Checksum, (uint)n.Status);
                        report.Checksum = Mix(report.Checksum, n.FloatBits);
                        report.Checksum = Mix(report.Checksum, n.DoubleBits);
                        report.Checksum = Mix(report.Checksum, n.RoundTrip);
                        report.Checksum = Mix(report.Checksum, (uint)n.WriteStatus);
                        report.Checksum = Mix(report.Checksum, (uint)n.ReadStatus);
                    }
                }
                ValidateReplay(report, arguments);
                string vectorTracePath = null;
                foreach (string argument in arguments)
                    if (argument.StartsWith("--fixed-vector-replay-trace=", StringComparison.Ordinal)) vectorTracePath = argument.Substring(28);
                FixedVectorPlayerValidation.Run(out report.VectorCases, out report.NativeVectorCases, out report.VectorChecksum,
                    out report.VectorInputChecksum, out report.NativeVectorSequenceTicks, vectorTracePath);
                report.VectorPublicApiPassed = true;
                report.VectorReplayVersion = FixedVectorReplayCorpus.Version;
                string quaternionTracePath=null;
                foreach (string argument in arguments) if (argument.StartsWith("--fixed-quaternion-replay-trace=",StringComparison.Ordinal)) quaternionTracePath=argument.Substring("--fixed-quaternion-replay-trace=".Length);
                FixedQuaternionPlayerValidation.Run(out report.QuaternionCases,out report.NativeQuaternionCases,out report.QuaternionChecksum,quaternionTracePath);
                report.QuaternionReplayVersion=FixedQuaternionReplayCorpus.Version; report.QuaternionPublicApiPassed=true;
                string quaternionSequenceTracePath=null;
                foreach (string argument in arguments) if (argument.StartsWith("--fixed-quaternion-sequence-trace=",StringComparison.Ordinal)) quaternionSequenceTracePath=argument.Substring("--fixed-quaternion-sequence-trace=".Length);
                FixedQuaternionPlayerValidation.RunSequences(out report.QuaternionSequenceCases,out report.NativeQuaternionSequenceCases,out report.QuaternionSequenceChecksum,quaternionSequenceTracePath);
                report.QuaternionSequenceVersion=FixedQuaternionSequenceCorpus.Version;
                report.Passed = true;
            }
            catch (Exception exception) { report.Error = exception.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log(JsonUtility.ToJson(report));
            Application.Quit(report.Passed ? 0 : 1);
        }
        private static void ValidateReplay(Report report, string[] arguments)
        {
            using (var samples = new NativeArray<FixedReplayCorpus.Sample>(FixedReplayCorpus.Count, Allocator.Persistent))
            using (var managed = new NativeArray<FixedReplayCorpus.Result>(FixedReplayCorpus.Count, Allocator.Persistent))
            using (var native = new NativeArray<FixedReplayCorpus.Result>(FixedReplayCorpus.Count, Allocator.Persistent))
            {
                var writable = samples;
                report.ReplayInputChecksum = 14695981039346656037UL;
                for (int i = 0; i < samples.Length; i++)
                {
                    var sample = FixedReplayCorpus.Create(i, (FixedArithmeticMode)(i % 3)); writable[i] = sample;
                    report.ReplayInputChecksum = FixedReplayCorpus.DigestInput(sample, report.ReplayInputChecksum);
                }
                new ReplayJob { Samples = samples, Results = managed }.Execute();
                new ReplayJob { Samples = samples, Results = native }.Schedule().Complete();
                const string tracePrefix = "--fixed-point-replay-trace=";
                foreach (string argument in arguments)
                    if (argument.StartsWith(tracePrefix, StringComparison.Ordinal)) WriteReplayTrace(argument.Substring(tracePrefix.Length), samples, native);
                Require(report.ReplayInputChecksum == FixedReplayCorpus.ExpectedInputChecksum, "Replay input generation differs across runtimes.");
                report.ReplayVersion = FixedReplayCorpus.Version; report.ReplayChecksum = 14695981039346656037UL;
                for (int i = 0; i < samples.Length; i++)
                {
                    var a = managed[i]; var b = native[i];
                    Require(a.Managed == 1 && b.Managed == 0, "Replay job did not execute native code: " + i);
                    Require(a.Same(ref b), "Replay Managed/Burst field mismatch: " + i);
                    report.ReplayChecksum = b.Digest(report.ReplayChecksum);
                    report.Checksum = b.Digest(report.Checksum);
                    report.Cases++; report.NativeCases++; report.ReplayCases++; report.NativeReplayCases++;
                }
                Require(report.ReplayChecksum == FixedReplayCorpus.ExpectedChecksum, "Versioned replay checksum differs across runtimes.");
            }
        }

        private static void WriteReplayTrace(string path, NativeArray<FixedReplayCorpus.Sample> samples,
            NativeArray<FixedReplayCorpus.Result> results)
        {
            using (var writer = new StreamWriter(path))
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    var sample = samples[i]; var result = results[i];
                    writer.Write(i + "," + sample.A + "," + sample.B + "," + sample.C + "," + sample.InputDouble + "," + sample.InputSingle);
                    for (int field = 0; field < result.Count; field++) writer.Write("," + result.ValueAt(field));
                    writer.WriteLine();
                }
            }
        }

        private static void ValidatePublicApi(Report report)
        {
            using (var managed = new NativeArray<ulong>(8, Allocator.TempJob))
            using (var native = new NativeArray<ulong>(8, Allocator.TempJob))
            {
                new PublicApiJob { Results = managed }.Execute();
                new PublicApiJob { Results = native }.Schedule().Complete();
                Require(managed[4] == 1 && native[4] == 0, "Public API job did not execute native code.");
                for (int i = 0; i < native.Length; i++)
                {
                    if (i == 4) continue;
                    Require(managed[i] == native[i], "Public API Managed/Burst mismatch: " + i);
                    Require(native[i] == (i < 4 ? 2000UL : i == 7 ? 1UL : 0UL), "Public API or SharedRuntime result: " + i);
                    report.Checksum = Mix(report.Checksum, native[i]);
                }
                report.PublicApiPassed = true;
            }
        }
        private static ulong Mix(ulong checksum, ulong value) => unchecked((checksum ^ value) * 1099511628211UL);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
