using System;
using System.Globalization;
using NUnit.Framework;
using DeterministicFixedPoint;

namespace LegendaryTools.Tests.DeterministicFixedPoint
{
    public sealed class DetS32Tests
    {
        [SetUp]
        public void SetUp()
        {
            // Default to Wrap for tests unless compile-time fixed.
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
            // Accessing constants should not lock unless you touch public API.
            // NOTE: we can only assert lock indirectly by Initialize not throwing.
            if (DetConfigTestUtil.IsCompileTimeFixed)
                Assert.Ignore("Compile-time fixed mode does not allow Initialize anyway.");

            // Access constants (should not lock by design)
            _ = DetS32.Zero;
            _ = DetS32.One;
            _ = DetS32.MinValue;
            _ = DetS32.MaxValue;

            // Initialize should still succeed (since no "use" happened)
            DetConfig.Initialize(DetOverflowMode.Saturate);
        }

        [Test]
        public void FirstUse_LocksConfig_InitializeAfterThrows()
        {
            if (DetConfigTestUtil.IsCompileTimeFixed)
                Assert.Ignore("Compile-time fixed mode does not allow Initialize anyway.");

            // First use: FromRaw touches config and locks it.
            _ = DetS32.FromRaw(0);

            Assert.Throws<InvalidOperationException>(() => DetConfig.Initialize(DetOverflowMode.Saturate));
        }

        [Test]
        public void FromRaw_And_Raw_RoundTrip()
        {
            DetS32 v = DetS32.FromRaw(1234);
            Assert.AreEqual(1234, v.Raw);
        }

        [Test]
        public void FromInt_ScalesBy1000()
        {
            DetS32 v = DetS32.FromInt(12);
            Assert.AreEqual(12000, v.Raw);
            Assert.AreEqual("12.000", v.ToString());
        }

        [Test]
        public void FromFloat_Quantizes_TiesAwayFromZero()
        {
            // 1.2344 -> 1234.4 -> 1234
            Assert.AreEqual(1234, DetS32.FromFloat(1.2344f).Raw);

            // 1.2345 -> 1234.5 tie -> away from zero -> 1235
            Assert.AreEqual(1235, DetS32.FromFloat(1.2345f).Raw);

            // -1.2345 -> -1234.5 tie -> away from zero -> -1235
            Assert.AreEqual(-1235, DetS32.FromFloat(-1.2345f).Raw);
        }

        [Test]
        public void ToIntFloor_WorksForNegative()
        {
            DetS32 v1 = DetS32.FromRaw(1999); // 1.999
            DetS32 v2 = DetS32.FromRaw(-1999); // -1.999

            Assert.AreEqual(1, v1.ToIntFloor());
            Assert.AreEqual(-2, v2.ToIntFloor());
        }

        [Test]
        public void ToIntRound_TiesAwayFromZero()
        {
            // 1.500 -> 1.5 -> 2
            DetS32 v1 = DetS32.FromRaw(1500);
            Assert.AreEqual(2, v1.ToIntRound());

            // -1.500 -> -1.5 -> -2
            DetS32 v2 = DetS32.FromRaw(-1500);
            Assert.AreEqual(-2, v2.ToIntRound());

            // 1.499 -> 1
            DetS32 v3 = DetS32.FromRaw(1499);
            Assert.AreEqual(1, v3.ToIntRound());
        }

        [Test]
        public void Addition_Subtraction_Basic()
        {
            DetS32 a = DetS32.FromRaw(1000); // 1.000
            DetS32 b = DetS32.FromRaw(250); // 0.250

            Assert.AreEqual(1250, (a + b).Raw);
            Assert.AreEqual(750, (a - b).Raw);
        }

        [Test]
        public void Multiplication_RoundsDeterministically_TiesAwayFromZero()
        {
            // 0.001 * 1.500 = 0.0015 -> rounds to 0.002 (raw 2)
            DetS32 a = DetS32.FromRaw(1);
            DetS32 b = DetS32.FromRaw(1500);
            Assert.AreEqual(2, (a * b).Raw);

            // -0.001 * 1.500 = -0.0015 -> rounds to -0.002 (raw -2)
            DetS32 c = DetS32.FromRaw(-1);
            Assert.AreEqual(-2, (c * b).Raw);
        }

        [Test]
        public void Division_ByZero_Throws()
        {
            DetS32 a = DetS32.FromRaw(1000);
            DetS32 z = DetS32.FromRaw(0);

            Assert.Throws<DivideByZeroException>(() => { _ = a / z; });
            Assert.Throws<DivideByZeroException>(() => { _ = a % z; });
        }

        [Test]
        public void Comparisons_AreRawBased()
        {
            DetS32 a = DetS32.FromRaw(100);
            DetS32 b = DetS32.FromRaw(101);

            Assert.IsTrue(a < b);
            Assert.IsTrue(a <= b);
            Assert.IsTrue(b > a);
            Assert.IsTrue(b >= a);
            Assert.IsTrue(a != b);
            Assert.IsTrue(a == DetS32.FromRaw(100));
        }

        [Test]
        public void Bitwise_Operations_WorkOnRaw()
        {
            DetS32 a = DetS32.FromRaw(unchecked((int)0x0F0F0F0F));
            DetS32 b = DetS32.FromRaw(unchecked((int)0x00FF00FF));

            Assert.AreEqual(unchecked((int)0x000F000F), (a & b).Raw);
            Assert.AreEqual(unchecked((int)0x0FFF0FFF), (a | b).Raw);
            Assert.AreEqual(unchecked((int)0x0FF00FF0), (a ^ b).Raw);
            Assert.AreEqual(unchecked((int)~0x0F0F0F0F), (~a).Raw);
        }

        [Test]
        public void Shifts_MaskTo31Bits()
        {
            // shift uses (shift & 31)
            DetS32 a = DetS32.FromRaw(1);

            Assert.AreEqual(1, (a << 32).Raw); // 32 -> 0
            Assert.AreEqual(1, (a >> 32).Raw); // 32 -> 0
        }

        [Test]
        public void ShiftRight_IsArithmetic()
        {
            DetS32 a = DetS32.FromRaw(unchecked((int)0x80000000)); // negative
            // Arithmetic shift keeps sign bit
            Assert.AreEqual(unchecked((int)0xC0000000), (a >> 1).Raw);
        }

        [Test]
        public void ToString_IsInvariantWithThreeDecimals()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR"); // decimal comma
                DetS32 v = DetS32.FromRaw(1234);
                Assert.AreEqual("1.234", v.ToString()); // must be invariant with '.'
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

            DetS32 a = DetS32.FromRaw(int.MaxValue);
            DetS32 b = DetS32.FromRaw(1);

            Assert.AreEqual(int.MinValue, (a + b).Raw);
        }

        [Test]
        public void SaturateOverflow_Addition_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS32 a = DetS32.FromRaw(int.MaxValue);
            DetS32 b = DetS32.FromRaw(1);

            Assert.AreEqual(int.MaxValue, (a + b).Raw);
        }

        [Test]
        public void WrapOverflow_ShiftLeft_Wraps()
        {
            RequireMode(DetOverflowMode.Wrap);

            DetS32 a = DetS32.FromRaw(1);
            // 1 << 31 => 0x80000000 => int.MinValue
            Assert.AreEqual(int.MinValue, (a << 31).Raw);
        }

        [Test]
        public void SaturateOverflow_ShiftLeft_Clamps()
        {
            RequireMode(DetOverflowMode.Saturate);
            DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS32 a = DetS32.FromRaw(1);
            // (long)1<<31 = 2147483648 => clamp to int.MaxValue
            Assert.AreEqual(int.MaxValue, (a << 31).Raw);
        }

        [Test]
        public void SaturateUnaryMinus_LongMin_ClampsToMax()
        {
            RequireMode(DetOverflowMode.Saturate);
            DetConfigTestUtil.ResetAndSetMode(DetOverflowMode.Saturate);

            DetS32 v = DetS32.FromRaw(int.MinValue);
            Assert.AreEqual(int.MaxValue, (-v).Raw);
        }
    }
}