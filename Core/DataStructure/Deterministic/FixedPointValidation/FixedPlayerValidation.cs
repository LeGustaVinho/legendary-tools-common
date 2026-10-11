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
            public int Cases, NativeCases;
            public string Platform, Error;
            public ulong Checksum;
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
                FixedRuntime.Initialize();
                ValidatePublicApi(report);
                string[] lines = FixedPlayerReference.Vectors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                using (var samples = new NativeArray<Sample>(lines.Length, Allocator.Persistent))
                using (var managed = new NativeArray<Result>(lines.Length, Allocator.Persistent))
                using (var native = new NativeArray<Result>(lines.Length, Allocator.Persistent))
                {
                    NativeArray<Sample> writable = samples;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] fields = lines[i].Split(','); int p = int.Parse(fields[1]);
                        writable[i] = new Sample { Kind = (FixedKind)int.Parse(fields[0]), Settings = new FixedSettings(Math.Min(p, 9), p),
                            Operation = (FixedUnary)Enum.Parse(typeof(FixedUnary), fields[2]), Raw = ulong.Parse(fields[3]) };
                    }
                    new ValidationJob { Samples = samples, Results = managed }.Execute();
                    new ValidationJob { Samples = samples, Results = native }.Schedule().Complete();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        Result m = managed[i], n = native[i];
                        Require(m.Managed == 1 && n.Managed == 0, "Burst did not execute native code."); m.Managed = 0;
                        Require(m.Equals(n), "Managed/Burst mismatch: " + lines[i]);
                        Sample sample = samples[i]; var state = new FixedRuntimeState(sample.Settings); var f = new FixedFormat(sample.Kind, state);
                        string[] fields = lines[i].Split(','); ulong expected = ulong.Parse(fields[4]);
                        FixedDecoded e = f.Decode(expected), a = f.Decode(n.Raw);
                        if (e.IsFinite && a.IsFinite)
                        {
                            ulong difference = e.Negative == a.Negative ? e.Magnitude > a.Magnitude ? e.Magnitude - a.Magnitude : a.Magnitude - e.Magnitude :
                                FixedUInt128.Add(new FixedUInt128(e.Magnitude), new FixedUInt128(a.Magnitude), out _).Lo;
                            Require(difference <= 1, "Accurate error bound: " + lines[i]);
                        }
                        else Require(expected == n.Raw, "Special value mismatch: " + lines[i]);
                        Require(n.RoundTrip == sample.Raw && n.WriteStatus.IsSuccess() && n.ReadStatus.IsSuccess(), "Wire mismatch: " + lines[i]);
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
                report.Passed = true;
            }
            catch (Exception exception) { report.Error = exception.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log(JsonUtility.ToJson(report));
            Application.Quit(report.Passed ? 0 : 1);
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
