using System;
using System.Globalization;
using NUnit.Framework;
using DeterministicFixedPoint;

namespace LegendaryTools.Tests.DeterministicFixedPoint
{
    public sealed class DetS64Tests
    {
        [SetUp]
        public void SetUp()
        {
            // Default to Wrap unless compile-time fixed.
            if (DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetLockOnly();
            else
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Wrap);
        }

        private static void RequireMode(DetOverflowMode desired)
        {
            if (DetConfigTestUtil.IsCompileTimeFixed && DetConfigTestUtil.CompileTimeMode != desired)
                Assert.Ignore(
                    $"DetConfig is compile-time fixed to {DetConfigTestUtil.CompileTimeMode}. Cannot test {desired}.");
        }

        [Test]
        public void Constants_DoNotLockConfigOnTypeLoad()
        {
            if (DetConfigTestUtil.IsCompileTimeFixed)
                Assert.Ignore("Compile-time fixed mode does not allow Initialize anyway.");

            // Access constants (should not lock)
            _ = DetS64.Zero;
            _ = DetS64.One;
            _ = DetS64.MinValue;
            _ = DetS64.MaxValue;

            // Initialize should still succeed (no "first use" yet)
            DetConfig.Initialize(DetOverflowMode.Saturate);
        }

        [Test]
        public void FirstUse_LocksConfig_InitializeAfterThrows()
        {
            if (DetConfigTestUtil.IsCompileTimeFixed)
                Assert.Ignore("Compile-time fixed mode does not allow Initialize anyway.");

            // First use: FromRaw touches config and locks it.
            _ = DetS64.FromRaw(0L);

            Assert.Throws<InvalidOperationException>(() => DetConfig.Initialize(DetOverflowMode.Saturate));
        }

        [Test]
        public void FromRaw_And_Raw_RoundTrip()
        {
            DetS64 v = DetS64.FromRaw(1234L);
            Assert.AreEqual(1234L, v.Raw);
        }

        [Test]
        public void FromLong_ScalesBy1000()
        {
            DetS64 v = DetS64.FromLong(12L);
            Assert.AreEqual(12000L, v.Raw);
            Assert.AreEqual("12.000", v.ToString());
        }

        [Test]
        public void FromFloat_Quantizes_TiesAwayFromZero()
        {
            // 1.2344 -> 1234.4 -> 1234
            Assert.AreEqual(1234L, DetS64.FromFloat(1.2344f).Raw);

            // 1.2345 -> 1234.5 tie -> away from zero -> 1235
            Assert.AreEqual(1235L, DetS64.FromFloat(1.2345f).Raw);

            // -1.2345 -> -1234.5 tie -> away from zero -> -1235
            Assert.AreEqual(-1235L, DetS64.FromFloat(-1.2345f).Raw);
        }

        [Test]
        public void ToIntFloor_WorksForNegative()
        {
            DetS64 v1 = DetS64.FromRaw(1999L); // 1.999
            DetS64 v2 = DetS64.FromRaw(-1999L); // -1.999

            Assert.AreEqual(1L, v1.ToIntFloor());
            Assert.AreEqual(-2L, v2.ToIntFloor());
        }

        [Test]
        public void ToIntRound_TiesAwayFromZero()
        {
            DetS64 v1 = DetS64.FromRaw(1500L); // 1.500 -> 2
            DetS64 v2 = DetS64.FromRaw(-1500L); // -1.500 -> -2
            DetS64 v3 = DetS64.FromRaw(1499L); // 1.499 -> 1

            Assert.AreEqual(2L, v1.ToIntRound());
            Assert.AreEqual(-2L, v2.ToIntRound());
            Assert.AreEqual(1L, v3.ToIntRound());
        }

        [Test]
        public void Addition_Subtraction_Basic()
        {
            DetS64 a = DetS64.FromRaw(1000L); // 1.000
            DetS64 b = DetS64.FromRaw(250L); // 0.250

            Assert.AreEqual(1250L, (a + b).Raw);
            Assert.AreEqual(750L, (a - b).Raw);
        }

        [Test]
        public void Multiplication_RoundsDeterministically_TiesAwayFromZero()
        {
            // 0.001 * 1.500 = 0.0015 -> rounds to 0.002 (raw 2)
            DetS64 a = DetS64.FromRaw(1L);
            DetS64 b = DetS64.FromRaw(1500L);
            Assert.AreEqual(2L, (a * b).Raw);

            // -0.001 * 1.500 = -0.0015 -> rounds to -0.002 (raw -2)
            DetS64 c = DetS64.FromRaw(-1L);
            Assert.AreEqual(-2L, (c * b).Raw);
        }

        [Test]
        public void Division_RoundsDeterministically_TiesAwayFromZero()
        {
            // 1 / 2 = 0.5 -> raw 500 (ties away from zero not relevant here)
            DetS64 one = DetS64.FromRaw(1000L);
            DetS64 two = DetS64.FromRaw(2000L);
            Assert.AreEqual(500L, (one / two).Raw);

            // 1 / 3 = 0.333... -> raw 333
            DetS64 three = DetS64.FromRaw(3000L);
            Assert.AreEqual(333L, (one / three).Raw);
        }

        [Test]
        public void Division_ByZero_Throws()
        {
            DetS64 a = DetS64.FromRaw(1000L);
            DetS64 z = DetS64.FromRaw(0L);

            Assert.Throws<DivideByZeroException>(() => { _ = a / z; });
            Assert.Throws<DivideByZeroException>(() => { _ = a % z; });
        }

        [Test]
        public void Comparisons_AreRawBased()
        {
            DetS64 a = DetS64.FromRaw(100L);
            DetS64 b = DetS64.FromRaw(101L);

            Assert.IsTrue(a < b);
            Assert.IsTrue(a <= b);
            Assert.IsTrue(b > a);
            Assert.IsTrue(b >= a);
            Assert.IsTrue(a != b);
            Assert.IsTrue(a == DetS64.FromRaw(100L));
        }

        [Test]
        public void Bitwise_Operations_WorkOnRaw()
        {
            DetS64 a = DetS64.FromRaw(unchecked((long)0x0F0F0F0F0F0F0F0F));
            DetS64 b = DetS64.FromRaw(unchecked((long)0x00FF00FF00FF00FF));

            Assert.AreEqual(unchecked((long)0x000F000F000F000F), (a & b).Raw);
            Assert.AreEqual(unchecked((long)0x0FFF0FFF0FFF0FFF), (a | b).Raw);
            Assert.AreEqual(unchecked((long)0x0FF00FF00FF00FF0), (a ^ b).Raw);
            Assert.AreEqual(unchecked((long)~0x0F0F0F0F0F0F0F0F), (~a).Raw);
        }

        [Test]
        public void Shifts_MaskTo63Bits()
        {
            DetS64 a = DetS64.FromRaw(1L);

            // shift uses (shift & 63)
            Assert.AreEqual(1L, (a << 64).Raw); // 64 -> 0
            Assert.AreEqual(1L, (a >> 64).Raw); // 64 -> 0
        }

        [Test]
        public void ShiftRight_IsArithmetic()
        {
            DetS64 a = DetS64.FromRaw(unchecked((long)0x8000000000000000)); // negative
            // Arithmetic shift keeps sign bit
            Assert.AreEqual(unchecked((long)0xC000000000000000), (a >> 1).Raw);
        }

        [Test]
        public void ToString_IsInvariantWithThreeDecimals()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                DetS64 v = DetS64.FromRaw(1234L);
                Assert.AreEqual("1.234", v.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Test]
        public void WrapOverflow_Addition_Wraps()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetS64 a = DetS64.FromRaw(long.MaxValue);
            DetS64 b = DetS64.FromRaw(1L);

            Assert.AreEqual(long.MinValue, (a + b).Raw);
        }

        [Test]
        public void SaturateOverflow_Addition_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS64 a = DetS64.FromRaw(long.MaxValue);
            DetS64 b = DetS64.FromRaw(1L);

            Assert.AreEqual(long.MaxValue, (a + b).Raw);
        }

        [Test]
        public void WrapOverflow_ShiftLeft_Wraps()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetS64 a = DetS64.FromRaw(1L);
            // 1 << 63 => sign bit set => long.MinValue
            Assert.AreEqual(long.MinValue, (a << 63).Raw);
        }

        [Test]
        public void SaturateOverflow_ShiftLeft_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS64 a = DetS64.FromRaw(1L);
            // (BigInteger)1<<63 = 9223372036854775808 => clamp to long.MaxValue
            Assert.AreEqual(long.MaxValue, (a << 63).Raw);
        }

        [Test]
        public void SaturateUnaryMinus_LongMin_ClampsToMax()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS64 v = DetS64.FromRaw(long.MinValue);
            Assert.AreEqual(long.MaxValue, (-v).Raw);
        }
    }
}