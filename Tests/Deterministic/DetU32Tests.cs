using System;
using System.Globalization;
using NUnit.Framework;
using DeterministicFixedPoint;

namespace LegendaryTools.Tests.DeterministicFixedPoint
{
    public sealed class DetU32Tests
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
            DetU32 v = DetU32.FromRaw(1234u);
            Assert.AreEqual(1234u, v.Raw);
        }

        [Test]
        public void FromUInt_ScalesBy1000()
        {
            DetU32 v = DetU32.FromUInt(12u);
            Assert.AreEqual(12000u, v.Raw);
            Assert.AreEqual("12.000", v.ToString());
        }

        [Test]
        public void FromFloat_Quantizes_TiesUp_And_NegativeToZero()
        {
            Assert.AreEqual(1234u, DetU32.FromFloat(1.2344f).Raw);
            Assert.AreEqual(1235u, DetU32.FromFloat(1.2345f).Raw); // ties up

            // negative -> clamp to zero
            Assert.AreEqual(0u, DetU32.FromFloat(-1.0f).Raw);
        }

        [Test]
        public void ToUIntFloor_And_ToUIntRound()
        {
            DetU32 v1 = DetU32.FromRaw(1999u); // 1.999
            Assert.AreEqual(1u, v1.ToUIntFloor());

            DetU32 v2 = DetU32.FromRaw(1500u); // 1.500 ties -> up => 2
            Assert.AreEqual(2u, v2.ToUIntRound());
        }

        [Test]
        public void Aliases_ToIntFloor_ToIntRound_Work()
        {
            DetU32 v = DetU32.FromRaw(1234u);
            Assert.AreEqual(v.ToUIntFloor(), v.ToIntFloor());
            Assert.AreEqual(v.ToUIntRound(), v.ToIntRound());
        }

        [Test]
        public void Addition_Subtraction_Basic()
        {
            DetU32 a = DetU32.FromRaw(1000u);
            DetU32 b = DetU32.FromRaw(250u);

            Assert.AreEqual(1250u, (a + b).Raw);
            Assert.AreEqual(750u, (a - b).Raw);
        }

        [Test]
        public void Multiplication_RoundsDeterministically_TiesUp()
        {
            // 0.001 * 1.500 = 0.0015 -> ties -> up => 0.002 (raw 2)
            DetU32 a = DetU32.FromRaw(1u);
            DetU32 b = DetU32.FromRaw(1500u);
            Assert.AreEqual(2u, (a * b).Raw);
        }

        [Test]
        public void Division_ByZero_Throws()
        {
            DetU32 a = DetU32.FromRaw(1000u);
            DetU32 z = DetU32.FromRaw(0u);

            Assert.Throws<DivideByZeroException>(() => { _ = a / z; });
            Assert.Throws<DivideByZeroException>(() => { _ = a % z; });
        }

        [Test]
        public void Comparisons_AreRawBased()
        {
            DetU32 a = DetU32.FromRaw(100u);
            DetU32 b = DetU32.FromRaw(101u);

            Assert.IsTrue(a < b);
            Assert.IsTrue(a <= b);
            Assert.IsTrue(b > a);
            Assert.IsTrue(b >= a);
            Assert.IsTrue(a != b);
            Assert.IsTrue(a == DetU32.FromRaw(100u));
        }

        [Test]
        public void Bitwise_Operations_WorkOnRaw()
        {
            DetU32 a = DetU32.FromRaw(0x0F0F0F0Fu);
            DetU32 b = DetU32.FromRaw(0x00FF00FFu);

            Assert.AreEqual(0x000F000Fu, (a & b).Raw);
            Assert.AreEqual(0x0FFF0FFFu, (a | b).Raw);
            Assert.AreEqual(0x0FF00FF0u, (a ^ b).Raw);
            Assert.AreEqual(~0x0F0F0F0Fu, (~a).Raw);
        }

        [Test]
        public void Shifts_MaskTo31Bits()
        {
            DetU32 a = DetU32.FromRaw(1u);

            Assert.AreEqual(1u, (a << 32).Raw); // 32 -> 0
            Assert.AreEqual(1u, (a >> 32).Raw); // 32 -> 0
        }

        [Test]
        public void ShiftRight_IsLogical()
        {
            DetU32 a = DetU32.FromRaw(0x80000000u);
            Assert.AreEqual(0x40000000u, (a >> 1).Raw);
        }

        [Test]
        public void ToString_IsInvariantWithThreeDecimals()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                DetU32 v = DetU32.FromRaw(1234u);
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

            DetU32 a = DetU32.FromRaw(uint.MaxValue);
            DetU32 b = DetU32.FromRaw(1u);

            Assert.AreEqual(0u, (a + b).Raw);
        }

        [Test]
        public void SaturateOverflow_Addition_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetU32 a = DetU32.FromRaw(uint.MaxValue);
            DetU32 b = DetU32.FromRaw(1u);

            Assert.AreEqual(uint.MaxValue, (a + b).Raw);
        }

        [Test]
        public void Saturate_Subtraction_ClampsAtZero()
        {
            RequireMode(DetOverflowMode.Saturate);
            DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetU32 a = DetU32.FromRaw(0u);
            DetU32 b = DetU32.FromRaw(1u);

            Assert.AreEqual(0u, (a - b).Raw);
        }

        [Test]
        public void Wrap_Subtraction_WrapsUnderflow()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetU32 a = DetU32.FromRaw(0u);
            DetU32 b = DetU32.FromRaw(1u);

            Assert.AreEqual(uint.MaxValue, (a - b).Raw);
        }
    }
}