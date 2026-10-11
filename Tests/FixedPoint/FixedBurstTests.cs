using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBurstTests
    {
        private struct Sample
        {
            internal FixedKind Kind;
            internal FixedSettings Settings;
            internal FixedUnary Unary;
            internal ulong A, B, C;
        }
        private struct Result
        {
            internal ulong Sum, Product, Quotient, Fma, Unary, FloatBits, DoubleBits, Network, Lerp, SmoothStep;
            internal FixedStatus SumStatus, ProductStatus, QuotientStatus, FmaStatus, UnaryStatus, WriteStatus, ReadStatus, LerpStatus, SmoothStatus;
            internal int Managed;
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct ParityJob : IJob
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
                    r.Sum = FixedKernel.Binary(s.A, s.B, s.Kind, state, FixedBinary.Add, out r.SumStatus);
                    r.Product = FixedKernel.Binary(s.A, s.B, s.Kind, state, FixedBinary.Multiply, out r.ProductStatus);
                    r.Quotient = FixedKernel.Binary(s.A, s.B, s.Kind, state, FixedBinary.Divide, out r.QuotientStatus);
                    r.Fma = FixedMathKernel.FusedMultiplyAdd(s.A, s.B, s.C, s.Kind, state, out r.FmaStatus);
                    r.Unary = FixedMathKernel.Unary(s.A, s.Kind, state, s.Unary, out r.UnaryStatus);
                    r.Lerp = FixedScalarKernel.Evaluate(s.A, s.B, s.C, FixedScalar.Lerp, s.Kind, state, out r.LerpStatus);
                    r.SmoothStep = FixedScalarKernel.Evaluate(s.A, s.B, s.C, FixedScalar.SmoothStep, s.Kind, state, out r.SmoothStatus);
                    r.FloatBits = FixedIeee.Bits(FixedIeee.ToSingle(s.A, s.Kind, state));
                    r.DoubleBits = FixedIeee.Bits(FixedIeee.ToDouble(s.A, s.Kind, state));
                    var profile = new FixedNetworkProfile(s.Settings, s.Kind);
                    var writer = new FixedBufferWriter(buffer, 16);
                    FixedNetworkKernel.Write(ref writer, s.A, profile, state, out r.WriteStatus);
                    var reader = new FixedBufferReader(buffer, writer.BytesWritten);
                    FixedNetworkKernel.Read(ref reader, profile, state, out r.Network, out r.ReadStatus);
                    Results[i] = r;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int managed) => managed = 1;
        }

        [Test]
        public void NativeBurstCompilationMatchesManagedRawBitsAndStatuses()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled, "Enable Burst for this native parity validation.");
            const int operations = (int)FixedUnary.ReciprocalSqrt + 1;
            const int count = 4 * 4 * operations;
            using (var samples = new NativeArray<Sample>(count, Allocator.TempJob))
            using (var managed = new NativeArray<Result>(count, Allocator.TempJob))
            using (var native = new NativeArray<Result>(count, Allocator.TempJob))
            {
                NativeArray<Sample> writableSamples = samples;
                for (int i = 0; i < count; i++)
                {
                    FixedKind kind = (FixedKind)(i / (4 * operations));
                    int p = (i / operations % 4) * 3; if (p == 9 && kind >= FixedKind.Signed64) p = 18;
                    var settings = new FixedSettings(p > 9 ? 9 : p, p, rounding: (FixedRounding)(i % 5),
                        mathProfile: (i & 1) == 0 ? FixedMathProfile.Accurate : FixedMathProfile.Fast,
                        arithmeticMode: (i & 2) == 0 ? FixedArithmeticMode.Portable : FixedArithmeticMode.Intrinsic);
                    var state = new FixedRuntimeState(settings); var f = new FixedFormat(kind, state);
                    // Cover special values, full-range unsigned input, tiny values and both precision profiles.
                    ulong raw = i % 9 == 0 ? f.NaN : i % 9 == 1 ? f.NegativeZero : i % 9 == 2 ? f.PositiveInfinity :
                        i % 9 == 3 ? f.NegativeInfinity : i % 9 == 4 ? f.MaxPositive : i % 9 == 5 ? f.MinValue : i % 9 == 6 ? 1 :
                        f.Finite(f.Scale, !f.Unsigned && (i & 1) != 0);
                    writableSamples[i] = new Sample { Kind = kind, Settings = settings, Unary = (FixedUnary)(i % operations), A = raw, B = f.Scale, C = 1 };
                }
                new ParityJob { Samples = samples, Results = managed }.Execute();
                new ParityJob { Samples = samples, Results = native }.Schedule().Complete();
                for (int i = 0; i < count; i++)
                {
                    Result m = managed[i], n = native[i];
                    Assert.AreEqual(1, m.Managed); Assert.AreEqual(0, n.Managed, "Job fell back to managed code.");
                    m.Managed = 0;
                    Assert.AreEqual(m, n, "Native parity sample " + i);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PublicApiJob : IJob
        {
            internal NativeArray<ulong> Result;
            public void Execute()
            {
                Fixed32 a = Fixed32.FromInt(2); UFixed32 b = UFixed32.FromUInt(2);
                Fixed64 c = Fixed64.FromInt(2); UFixed64 d = UFixed64.FromUInt(2);
                Result[0] = unchecked((uint)FixedMath.Sqrt(a * a).RawValue);
                Result[1] = FixedMath.Sqrt(b * b).RawValue;
                Result[2] = unchecked((ulong)FixedMath.Sqrt(c * c).RawValue);
                Result[3] = FixedMath.Sqrt(d * d).RawValue;
                int managed = 0; MarkManaged(ref managed); Result[4] = (ulong)managed;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [Test]
        public void PublicFacadeAndSharedRuntimeCompileInBurst()
        {
            FixedRuntime.Initialize();
            using (var managed = new NativeArray<ulong>(5, Allocator.TempJob))
            using (var values = new NativeArray<ulong>(5, Allocator.TempJob))
            {
                new PublicApiJob { Result = managed }.Execute();
                new PublicApiJob { Result = values }.Schedule().Complete();
                Assert.AreEqual(1UL, managed[4]); Assert.AreEqual(0UL, values[4], "Public facade job fell back to managed.");
                for (int i = 0; i < 4; i++) Assert.AreEqual(2000UL, values[i]);
            }
        }
    }
}
