#if !FIXEDPOINT_STANDALONE
using System;
using System.Globalization;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBinaryMathBurstTests
    {
        private struct Sample
        {
            internal FixedKind Kind;
            internal int Precision;
            internal FixedRounding Rounding;
            internal int Operation;
            internal ulong A, B;
        }
        private struct Result { internal ulong Raw; internal FixedStatus Status; internal int Managed; }
        [BurstCompile(CompileSynchronously = true)]
        private struct BinaryJob : IJob
        {
            [ReadOnly] internal NativeArray<Sample> Samples;
            internal NativeArray<Result> Results;
            internal FixedMathProfile Profile;
            public void Execute()
            {
                for (int i = 0; i < Samples.Length; i++)
                {
                    Sample sample = Samples[i];
                    var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, sample.Precision), sample.Precision,
                        sample.Rounding, mathProfile: Profile, arithmeticMode: (FixedArithmeticMode)(i % 3)));
                    Result r = default;
                    r.Raw = sample.Operation == 0 ? FixedMathKernel.Pow(sample.A, sample.B, sample.Kind, state, out r.Status) :
                        sample.Operation == 1 ? FixedMathKernel.Log(sample.A, sample.B, sample.Kind, state, out r.Status) :
                        FixedMathKernel.Atan2(sample.A, sample.B, sample.Kind, state, out r.Status);
                    MarkManaged(ref r.Managed); Results[i] = r;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [Test]
        public void EveryBinaryReferenceHasIdenticalRawAndStatusInManagedAndNativeBurst()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            string[] lines = FixedBinaryMathReference.Vectors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            using (var samples = new NativeArray<Sample>(lines.Length, Allocator.Persistent))
            using (var managed = new NativeArray<Result>(lines.Length, Allocator.Persistent))
            using (var native = new NativeArray<Result>(lines.Length, Allocator.Persistent))
            {
                var writable = samples;
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] f = lines[i].Split(',');
                    writable[i] = new Sample { Kind = (FixedKind)int.Parse(f[0], CultureInfo.InvariantCulture),
                        Precision = int.Parse(f[1], CultureInfo.InvariantCulture), Rounding = (FixedRounding)int.Parse(f[2], CultureInfo.InvariantCulture),
                        Operation = f[3] == "Pow" ? 0 : f[3] == "Log" ? 1 : 2,
                        A = ulong.Parse(f[4], CultureInfo.InvariantCulture), B = ulong.Parse(f[5], CultureInfo.InvariantCulture) };
                }
                foreach (FixedMathProfile profile in Enum.GetValues(typeof(FixedMathProfile)))
                {
                    new BinaryJob { Samples = samples, Results = managed, Profile = profile }.Execute();
                    new BinaryJob { Samples = samples, Results = native, Profile = profile }.Schedule().Complete();
                    ulong digest = 14695981039346656037UL;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        Result a = managed[i], b = native[i];
                        Assert.AreEqual(1, a.Managed); Assert.AreEqual(0, b.Managed, "Managed fallback " + i);
                        Assert.AreEqual(a.Raw, b.Raw, profile + " sample " + i);
                        Assert.AreEqual(a.Status, b.Status, profile + " status " + i);
                        digest = unchecked((digest ^ b.Raw) * 1099511628211UL);
                        digest = unchecked((digest ^ (uint)b.Status) * 1099511628211UL);
                    }
                    TestContext.Progress.WriteLine("FIXED_BINARY_BURST profile=" + profile + " nativeCases=" + lines.Length + " checksum=" + digest);
                }
            }
        }
    }
}
#endif
