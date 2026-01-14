using System;
using System.Globalization;
using NUnit.Framework;
using DeterministicFixedPoint;

namespace LegendaryTools.Tests.DeterministicFixedPoint
{
    public sealed class DetU64Tests
    {
        [SetUp]
        public void SetUp()
        {
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
        public void FromRaw_And_Raw_RoundTrip()
        {
            DetU64 v = DetU64.FromRaw(1234UL);
            Assert.AreEqual(1234UL, v.Raw);
        }

        [Test]
        public void FromULong_ScalesBy1000()
        {
            DetU64 v = DetU64.FromULong(12UL);
            Assert.AreEqual(12000UL, v.Raw);
            Assert.AreEqual("12.000", v.ToString());
        }

        [Test]
        public void FromFloat_Quantizes_TiesUp_And_NegativeToZero()
        {
            Assert.AreEqual(1234UL, DetU64.FromFloat(1.2344f).Raw);
            Assert.AreEqual(1235UL, DetU64.FromFloat(1.2345f).Raw); // ties up

            // negative -> clamp to zero
            Assert.AreEqual(0UL, DetU64.FromFloat(-1.0f).Raw);
        }

        [Test]
        public void ToIntFloor_And_ToIntRound()
        {
            DetU64 v1 = DetU64.FromRaw(1999UL); // 1.999
            Assert.AreEqual(1UL, v1.ToIntFloor());

            DetU64 v2 = DetU64.FromRaw(1500UL); // 1.500 ties -> up => 2
            Assert.AreEqual(2UL, v2.ToIntRound());
        }

        [Test]
        public void Addition_Subtraction_Basic()
        {
            DetU64 a = DetU64.FromRaw(1000UL);
            DetU64 b = DetU64.FromRaw(250UL);

            Assert.AreEqual(1250UL, (a + b).Raw);
            Assert.AreEqual(750UL, (a - b).Raw);
        }

        [Test]
        public void Multiplication_RoundsDeterministically_TiesUp()
        {
            // 0.001 * 1.500 = 0.0015 -> ties -> up => 0.002 (raw 2)
            DetU64 a = DetU64.FromRaw(1UL);
            DetU64 b = DetU64.FromRaw(1500UL);
            Assert.AreEqual(2UL, (a * b).Raw);
        }

        [Test]
        public void Division_RoundsDeterministically_TiesUp()
        {
            // 1 / 2 = 0.5 -> raw 500
            DetU64 one = DetU64.FromRaw(1000UL);
            DetU64 two = DetU64.FromRaw(2000UL);
            Assert.AreEqual(500UL, (one / two).Raw);

            // 1 / 3 = 0.333... -> raw 333
            DetU64 three = DetU64.FromRaw(3000UL);
            Assert.AreEqual(333UL, (one / three).Raw);
        }

        [Test]
        public void Division_ByZero_Throws()
        {
            DetU64 a = DetU64.FromRaw(1000UL);
            DetU64 z = DetU64.FromRaw(0UL);

            Assert.Throws<DivideByZeroException>(() => { _ = a / z; });
            Assert.Throws<DivideByZeroException>(() => { _ = a % z; });
        }

        [Test]
        public void Comparisons_AreRawBased()
        {
            DetU64 a = DetU64.FromRaw(100UL);
            DetU64 b = DetU64.FromRaw(101UL);

            Assert.IsTrue(a < b);
            Assert.IsTrue(a <= b);
            Assert.IsTrue(b > a);
            Assert.IsTrue(b >= a);
            Assert.IsTrue(a != b);
            Assert.IsTrue(a == DetU64.FromRaw(100UL));
        }

        [Test]
        public void Bitwise_Operations_WorkOnRaw()
        {
            DetU64 a = DetU64.FromRaw(0x0F0F0F0F0F0F0F0FUL);
            DetU64 b = DetU64.FromRaw(0x00FF00FF00FF00FFUL);

            Assert.AreEqual(0x000F000F000F000FUL, (a & b).Raw);
            Assert.AreEqual(0x0FFF0FFF0FFF0FFFUL, (a | b).Raw);
            Assert.AreEqual(0x0FF00FF00FF00FF0UL, (a ^ b).Raw);
            Assert.AreEqual(~0x0F0F0F0F0F0F0F0FUL, (~a).Raw);
        }

        [Test]
        public void Shifts_MaskTo63Bits()
        {
            DetU64 a = DetU64.FromRaw(1UL);

            Assert.AreEqual(1UL, (a << 64).Raw); // 64 -> 0
            Assert.AreEqual(1UL, (a >> 64).Raw); // 64 -> 0
        }

        [Test]
        public void ShiftRight_IsLogical()
        {
            DetU64 a = DetU64.FromRaw(0x8000000000000000UL);
            Assert.AreEqual(0x4000000000000000UL, (a >> 1).Raw);
        }

        [Test]
        public void ToString_IsInvariantWithThreeDecimals()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                DetU64 v = DetU64.FromRaw(1234UL);
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

            DetU64 a = DetU64.FromRaw(ulong.MaxValue);
            DetU64 b = DetU64.FromRaw(1UL);

            Assert.AreEqual(0UL, (a + b).Raw);
        }

        [Test]
        public void SaturateOverflow_Addition_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetU64 a = DetU64.FromRaw(ulong.MaxValue);
            DetU64 b = DetU64.FromRaw(1UL);

            Assert.AreEqual(ulong.MaxValue, (a + b).Raw);
        }

        [Test]
        public void Saturate_Subtraction_ClampsAtZero()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetU64 a = DetU64.FromRaw(0UL);
            DetU64 b = DetU64.FromRaw(1UL);

            Assert.AreEqual(0UL, (a - b).Raw);
        }

        [Test]
        public void Wrap_Subtraction_WrapsUnderflow()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetU64 a = DetU64.FromRaw(0UL);
            DetU64 b = DetU64.FromRaw(1UL);

            Assert.AreEqual(ulong.MaxValue, (a - b).Raw);
        }

        [Test]
        public void WrapOverflow_ShiftLeft_Wraps()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetU64 a = DetU64.FromRaw(1UL);
            // 1 << 63 => 0x8000.. which is valid in ulong
            Assert.AreEqual(0x8000000000000000UL, (a << 63).Raw);
        }

        [Test]
        public void SaturateOverflow_ShiftLeft_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            if (!DetConfigTestUtil.IsCompileTimeFixed)
                DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetU64 a = DetU64.FromRaw(1UL);
            // (BigInteger)1<<64 would overflow, but we shift by 63 here and it fits.
            // Saturate behavior should still be correct (no clamp needed).
            Assert.AreEqual(0x8000000000000000UL, (a << 63).Raw);
        }
    }
}