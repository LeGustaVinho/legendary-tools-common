using System;
using System.Globalization;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedPublicApiTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize(FixedSettings.Default);

        [Test]
        public void FourVariantsHaveExactlyOneNativeSizedFieldAndBehaveLikeScalars()
        {
            Assert.AreEqual(4, Marshal.SizeOf<Fixed32>());
            Assert.AreEqual(4, Marshal.SizeOf<UFixed32>());
            Assert.AreEqual(8, Marshal.SizeOf<Fixed64>());
            Assert.AreEqual(8, Marshal.SizeOf<UFixed64>());
            Fixed32 a = 2;
            UFixed32 b = 2U;
            Fixed64 c = 2L;
            UFixed64 d = 2UL;
            Assert.AreEqual(3000, (++a).RawValue);
            Assert.AreEqual(1000U, (--b).RawValue);
            Assert.AreEqual(4000L, (c * c).RawValue);
            Assert.AreEqual(1000UL, (d / d).RawValue);
            Assert.AreEqual(1234L, ((Fixed64)Fixed32.FromRaw(1234)).RawValue);
            Assert.IsTrue(UFixed64.IsNaN((UFixed64)Fixed64.FromRaw(-1)));
            Assert.AreEqual(0L, (long)Fixed64.NaN);
            Assert.AreEqual(ulong.MaxValue, (ulong)UFixed64.PositiveInfinity);
            Assert.AreEqual(2, Convert.ToInt32(Fixed32.FromDecimal(1.5m)));
            Assert.AreEqual(1, (int)Fixed32.FromDecimal(1.5m));
            Assert.AreEqual(0, Convert.ToByte(Fixed32.FromDecimal(-.1m)));
            Assert.Throws<OverflowException>(() => Convert.ToInt32(Fixed32.NaN));
        }

        [Test]
        public void NegativeZeroConvertsToUnsignedIntegersWithoutFailure()
        {
            Assert.IsTrue(Fixed32.NegativeZero.TryToUInt64(out ulong signed32, out FixedStatus signed32Status));
            Assert.IsTrue(UFixed32.NegativeZero.TryToUInt64(out ulong unsigned32, out FixedStatus unsigned32Status));
            Assert.IsTrue(Fixed64.NegativeZero.TryToUInt64(out ulong signed64, out FixedStatus signed64Status));
            Assert.IsTrue(UFixed64.NegativeZero.TryToUInt64(out ulong unsigned64, out FixedStatus unsigned64Status));
            Assert.AreEqual(0UL, signed32 | unsigned32 | signed64 | unsigned64);
            Assert.AreEqual(FixedStatus.None, signed32Status | unsigned32Status | signed64Status | unsigned64Status);
            Assert.AreEqual(0U, (uint)Fixed32.NegativeZero);
            Assert.AreEqual(0UL, (ulong)UFixed64.NegativeZero);
            Assert.IsFalse(Fixed32.FromRaw(-1).TryToUInt64(out _, out FixedStatus negativeStatus));
            Assert.AreEqual(FixedStatus.NegativeResult, negativeStatus);
        }

        [Test]
        public void ManagedAuthoringPreservesDecimalDigitsCultureSpecialsAndSignedZero()
        {
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.AreEqual(1235, Fixed32.FromDecimal(1.2345m + 0.0001m).RawValue);
            Assert.AreEqual(1234, Fixed32.Parse("1,234", culture).RawValue);
            Assert.AreEqual("1,234", Fixed32.FromRaw(1234).ToString("R", culture));
            Assert.AreEqual("-0.000", Fixed32.NegativeZero.ToString("R", CultureInfo.InvariantCulture));
            Assert.AreEqual(Fixed32.NegativeZero.RawValue, Fixed32.Parse("-0.000").RawValue);
            float ieeeNegativeZero = FixedIeee.Single(0x80000000U);
            foreach (string format in new[] { "C2", "P2", "N3", "0.###;-0.###;zero" })
                Assert.AreEqual(ieeeNegativeZero.ToString(format, CultureInfo.InvariantCulture),
                    Fixed32.NegativeZero.ToString(format, CultureInfo.InvariantCulture), format);
            var currencyCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.AreEqual(1234, Fixed32.Parse("$1.234", NumberStyles.Currency, currencyCulture).RawValue);
            Assert.AreEqual(-1234, Fixed32.Parse("($1.234)", NumberStyles.Currency, currencyCulture).RawValue);
            Assert.IsFalse(Fixed32.TryParse("1$2", NumberStyles.Currency, currencyCulture, out _));
            Assert.IsTrue(Fixed32.IsNaN(Fixed32.Parse("NaN")));
            Assert.AreEqual(1234567890123456UL, UFixed64.Parse("1234567890123.456").RawValue);
            Assert.IsTrue(Fixed32.IsPositiveInfinity(Fixed32.Parse("1e1000000000")));
            Assert.IsFalse(Fixed32.TryParse("abc", out _));
            Assert.Throws<FormatException>(() => Fixed32.Parse("abc"));
            Assert.Throws<DivideByZeroException>(() => Fixed32.DivideChecked(Fixed32.One, Fixed32.Zero));
        }

        [Test]
        public void ComparisonEqualityHashAndFrozenSettingsAreExplicit()
        {
            Fixed32 nan = Fixed32.NaN;
            Assert.IsFalse(nan == Fixed32.NaN);
            Assert.IsTrue(nan.Equals(Fixed32.NaN));
            Assert.IsFalse(nan <= Fixed32.One);
            Assert.Less(nan.CompareTo(Fixed32.NegativeInfinity), 0);
            Assert.AreEqual(Fixed32.Zero.GetHashCode(), Fixed32.NegativeZero.GetHashCode());
            Assert.IsTrue(FixedRuntime.TryInitialize(FixedSettings.Default, out _));
            Assert.IsFalse(FixedRuntime.TryInitialize(new FixedSettings(4, 4), out FixedStatus status));
            Assert.AreEqual(FixedStatus.ConfigurationFrozen, status);
        }
    }
}
