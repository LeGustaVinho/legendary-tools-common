using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedManagedEdgeTests
    {
        private static NumberFormatInfo SeparateCurrencySeparators()
        {
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.CurrencySymbol = "CUR";
            nfi.CurrencyDecimalSeparator = ":";
            nfi.CurrencyGroupSeparator = "_";
            nfi.NumberDecimalSeparator = ".";
            nfi.NumberGroupSeparator = ",";
            return nfi;
        }

        [Test]
        public void CurrencyAndNumberStylesMatchTheManagedDecimalGrammar()
        {
            int checkedCount = 0, failures = 0; var examples = new List<string>();
            NumberFormatInfo[] formats = {
                CultureInfo.InvariantCulture.NumberFormat,
                CultureInfo.GetCultureInfo("pt-BR").NumberFormat,
                CultureInfo.GetCultureInfo("fr-FR").NumberFormat,
                SeparateCurrencySeparators()
            };
            foreach (NumberFormatInfo nfi in formats)
            foreach (NumberStyles styles in new[] { NumberStyles.None, NumberStyles.Integer,
                NumberStyles.Float, NumberStyles.Number, NumberStyles.Currency, NumberStyles.Any,
                NumberStyles.AllowParentheses, NumberStyles.AllowCurrencySymbol | NumberStyles.AllowDecimalPoint,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign })
            {
                string d = nfi.NumberDecimalSeparator, g = nfi.NumberGroupSeparator;
                string cd = nfi.CurrencyDecimalSeparator, cg = nfi.CurrencyGroupSeparator, symbol = nfi.CurrencySymbol;
                foreach (string text in new[] { "12", "-12", "+12", "12-", "(12)", " 12 ",
                    "12" + d + "34", "1" + g + "234" + d + "5", "12" + cd + "34", "1" + cg + "234" + cd + "5",
                    symbol + "12" + cd + "34", "12" + cd + "34" + symbol, "-" + symbol + "12" + cd + "34",
                    "(" + symbol + "12" + cd + "34)", symbol + " 12" + cd + "34", "12" + cd + "34 " + symbol,
                    symbol + "12" + d + "34", "12" + d + "34" + symbol, "( 12 )", "(12 )", "( 12)",
                    "- 12", "12 -", "12\t", "\t12", "12\u2003", "\u200312", "12\u00a0", "\u00a012",
                    "1e2", "1e-2", "12" + g, "12" + g + g + "3", g + "12", "1" + d + g + "2",
                    "1" + symbol + "2", "++12", "12--", "(12)-", "12\0", "12\0\0", "12\0x" })
                {
                    bool expectedSuccess = decimal.TryParse(text, styles, nfi, out decimal expected);
                    bool actualSuccess = FixedManaged.TryParse(text, styles, nfi, FixedKind.Signed64,
                        new FixedRuntimeState(FixedSettings.Default), out ulong actual, out FixedStatus status);
                    string context = "text=[" + text.Replace("\0", "\\0") + "] styles=" + styles + " currency=" + symbol;
                    checkedCount++;
                    bool same = expectedSuccess == actualSuccess;
                    if (expectedSuccess && actualSuccess)
                    {
                        same &= unchecked((ulong)(long)(expected * 1000m)) == actual && status == FixedStatus.None;
                    }
                    else if (!expectedSuccess) same &= status == FixedStatus.InvalidEncoding;
                    if (!same)
                    {
                        failures++;
                        if (examples.Count < 80) examples.Add(context + " decimal=" + expectedSuccess + "/" + expected +
                            " fixed=" + actualSuccess + "/" + actual + "/" + status);
                    }
                }
            }
            TestContext.Progress.WriteLine("FIXED_PARSE checked=" + checkedCount + " failures=" + failures);
            Assert.AreEqual(0, failures, string.Join("\n", examples));
        }

        [Test]
        public void SpecialValueGrammarFollowsDotNet8RulesInEveryRuntime()
        {
            int failures = 0; var examples = new List<string>();
            foreach (NumberFormatInfo nfi in new[] { CultureInfo.InvariantCulture.NumberFormat,
                CultureInfo.GetCultureInfo("pt-BR").NumberFormat, SeparateCurrencySeparators() })
            foreach (NumberStyles styles in new[] { NumberStyles.None, NumberStyles.Float, NumberStyles.Any })
            for (int symbolIndex = 0; symbolIndex < 3; symbolIndex++)
            {
                string symbol = symbolIndex == 0 ? nfi.NaNSymbol : symbolIndex == 1 ? nfi.PositiveInfinitySymbol : nfi.NegativeInfinitySymbol;
                string[] variants = { symbol, symbol.ToLowerInvariant(), nfi.PositiveSign + symbol,
                    nfi.NegativeSign + symbol, " " + symbol + " ", "\u2003" + symbol + "\u2003", symbol + "\0", symbol + "\0\0" };
                for (int variant = 0; variant < variants.Length; variant++)
                {
                    string text = variants[variant];
                    // Unity Mono has an older special-token grammar. Fix the expected contract
                    // explicitly so the fixed-point parser stays identical in both runtimes.
                    bool expectedSuccess = variant < 6 && !(symbolIndex == 2 && (variant == 2 || variant == 3));
                    // Some Mono cultures include '+' in PositiveInfinitySymbol. A prefixed
                    // minus is valid only when the whole text is the configured negative symbol.
                    if (symbolIndex == 1 && variant == 3)
                        expectedSuccess = string.Equals(text, nfi.NegativeInfinitySymbol, StringComparison.OrdinalIgnoreCase);
                    bool actualSuccess = FixedManaged.TryParse(text, styles, nfi, FixedKind.Signed64,
                        new FixedRuntimeState(FixedSettings.Default), out ulong actual, out FixedStatus status);
                    ulong expectedRaw = symbolIndex == 0 ? 1UL << 63 : symbolIndex == 2 || variant == 3 ?
                        (1UL << 63) + 1 : (1UL << 63) - 1;
#if FIXEDPOINT_STANDALONE
                    Assert.AreEqual(expectedSuccess, double.TryParse(text, styles, nfi, out _), "Independent .NET 8 grammar oracle: " + text);
#endif
                    bool same = expectedSuccess == actualSuccess && (expectedSuccess ? actual == expectedRaw && status == FixedStatus.None :
                        status == FixedStatus.InvalidEncoding);
                    if (!same)
                    {
                        failures++;
                        if (examples.Count < 30) examples.Add("[" + text.Replace("\0", "\\0") + "] " + styles +
                            " expected=" + expectedSuccess + " fixed=" + actualSuccess);
                    }
                }
            }
            Assert.AreEqual(0, failures, string.Join("\n", examples));
        }

        private enum ParsedToken { Invalid, Finite12, NaN, PositiveInfinity, NegativeInfinity }

        [Test]
        public void SpecialTokensHonorCustomSignCaseSymbolPrecedenceAndNumericPrecedence()
        {
            var words = SeparateCurrencySeparators();
            words.PositiveSign = "pos"; words.NegativeSign = "neg";
            words.NaNSymbol = "undefined"; words.PositiveInfinitySymbol = "infinite"; words.NegativeInfinitySymbol = "below";
            int checkedCount = 0; var failures = new List<string>();
            CheckSpecialTokens(words, ref checkedCount, failures,
                ("POSundefined", ParsedToken.NaN), ("NEGundefined", ParsedToken.NaN),
                ("posinfinite", ParsedToken.PositiveInfinity), ("POSINFINITE", ParsedToken.PositiveInfinity),
                ("neginfinite", ParsedToken.Invalid), ("NEGinfinite", ParsedToken.Invalid),
                ("BELOW", ParsedToken.NegativeInfinity), ("undefined", ParsedToken.NaN),
                ("+undefined", ParsedToken.Invalid), ("posbelow", ParsedToken.Invalid), ("negbelow", ParsedToken.Invalid));
            var same = (NumberFormatInfo)words.Clone();
            same.NaNSymbol = same.PositiveInfinitySymbol = same.NegativeInfinitySymbol = "same";
            CheckSpecialTokens(same, ref checkedCount, failures,
                ("same", ParsedToken.PositiveInfinity), ("SAME", ParsedToken.PositiveInfinity),
                ("possame", ParsedToken.PositiveInfinity), ("negsame", ParsedToken.NaN));
            same.PositiveInfinitySymbol = "distinct";
            CheckSpecialTokens(same, ref checkedCount, failures,
                ("same", ParsedToken.NegativeInfinity), ("negsame", ParsedToken.NaN), ("possame", ParsedToken.NaN),
                ("distinct", ParsedToken.PositiveInfinity), ("posdistinct", ParsedToken.PositiveInfinity), ("negdistinct", ParsedToken.Invalid));
            var numeric = (NumberFormatInfo)words.Clone(); numeric.NaNSymbol = "12";
            CheckSpecialTokens(numeric, ref checkedCount, failures,
                ("12", ParsedToken.Finite12), ("infinite", ParsedToken.PositiveInfinity), ("below", ParsedToken.NegativeInfinity));
            var emptySign = (NumberFormatInfo)words.Clone(); emptySign.PositiveSign = "";
            CheckSpecialTokens(emptySign, ref checkedCount, failures,
                ("negundefined", ParsedToken.Invalid), ("undefined", ParsedToken.NaN),
                ("infinite", ParsedToken.PositiveInfinity), ("below", ParsedToken.NegativeInfinity));
            foreach (char sign in "\u2012\u207b\u208b\u2212\u2796\ufe63\uff0d")
            {
                var unicode = (NumberFormatInfo)words.Clone(); unicode.NegativeSign = sign.ToString();
                CheckSpecialTokens(unicode, ref checkedCount, failures,
                    (sign + "UNDEFINED", ParsedToken.NaN), ("-undefined", ParsedToken.Invalid),
                    (sign + "infinite", ParsedToken.Invalid), ("below", ParsedToken.NegativeInfinity));
            }
            TestContext.Progress.WriteLine("FIXED_SPECIAL_CUSTOM checked=" + checkedCount + " failures=" + failures.Count);
            Assert.IsEmpty(failures, string.Join("\n", failures.GetRange(0, Math.Min(40, failures.Count))));
        }

        private static void CheckSpecialTokens(NumberFormatInfo nfi, ref int checkedCount, List<string> failures,
            params (string Text, ParsedToken Token)[] cases)
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            foreach (NumberStyles styles in new[] { NumberStyles.None, NumberStyles.Float, NumberStyles.Any })
            foreach (var sample in cases)
            {
                bool expectedSuccess = sample.Token != ParsedToken.Invalid;
#if FIXEDPOINT_STANDALONE
                bool oracleSuccess = double.TryParse(sample.Text, styles, nfi, out double oracle);
                Assert.AreEqual(expectedSuccess, oracleSuccess, "Independent .NET 8 grammar oracle: " + sample.Text);
                if (oracleSuccess)
                {
                    double expected = sample.Token == ParsedToken.NaN ? double.NaN :
                        sample.Token == ParsedToken.PositiveInfinity ? double.PositiveInfinity :
                        sample.Token == ParsedToken.NegativeInfinity ? double.NegativeInfinity : 12;
                    Assert.AreEqual(expected, oracle, "Independent .NET 8 token precedence: " + sample.Text);
                }
#endif
                var state = new FixedRuntimeState(FixedSettings.Default); var f = new FixedFormat(kind, state);
                ulong expectedRaw = sample.Token == ParsedToken.Finite12 ? 12000UL :
                    sample.Token == ParsedToken.PositiveInfinity ? f.PositiveInfinity :
                    sample.Token == ParsedToken.NegativeInfinity ? f.NegativeInfinity : f.NaN;
                FixedStatus expectedStatus = expectedSuccess ? FixedStatus.None : FixedStatus.InvalidEncoding;
                bool success = FixedManaged.TryParse(sample.Text, styles, nfi, kind, state, out ulong raw, out FixedStatus status);
                checkedCount++;
                if (success != expectedSuccess || raw != expectedRaw || status != expectedStatus)
                    failures.Add(kind + " [" + sample.Text + "] " + styles + " expected=" + sample.Token +
                        " actual=" + success + "/" + raw + "/" + status);
            }
        }

        [Test]
        public void EveryNumberStyleCombinationHandlesCurrencyOrderAndMulticharacterSeparators()
        {
            var words = SeparateCurrencySeparators();
            words.NumberDecimalSeparator = "E"; words.NumberGroupSeparator = "grp";
            words.CurrencyDecimalSeparator = "dot"; words.CurrencyGroupSeparator = "group";
            var spacedSign = SeparateCurrencySeparators(); spacedSign.NumberNegativePattern = 2;
            int checkedCount = 0, failures = 0; var examples = new List<string>();
            foreach (NumberFormatInfo nfi in new[] { SeparateCurrencySeparators(), words, spacedSign })
            for (int flags = 0; flags <= (int)NumberStyles.Any; flags++)
            foreach (string text in new[] { "12", "12 ", "-12", "+12", "12-", "12 -", "- 12", "- 12-", "(12)", "( 12)",
                "(12 )", "(12)-", "CUR12", "12CUR", "CUR(12)", "(CUR12)", "(12)CUR", "(12CUR)", "CUR-12", "-CUR12",
                "12-CUR", "12CUR-", "CUR 12", "12 CUR", "CUR - 12", "- CUR12", "1E2", "1E-2", "1dot2", "1grp234",
                "1group234", "1E2CUR", "CUR1E2", "12\0", "12 \0", "12\0 ", "(12\0)", "CURCUR12", ".", "E", "1E+" })
            {
                var styles = (NumberStyles)flags;
                bool expectedSuccess = decimal.TryParse(text, styles, nfi, out decimal expected);
                bool actualSuccess = FixedManaged.TryParse(text, styles, nfi, FixedKind.Signed64,
                    new FixedRuntimeState(FixedSettings.Default), out ulong actual, out FixedStatus status);
                bool same = expectedSuccess == actualSuccess && (expectedSuccess ? actual == unchecked((ulong)(long)(expected * 1000m)) &&
                    status == FixedStatus.None : status == FixedStatus.InvalidEncoding);
                if (!same)
                {
                    failures++;
                    if (examples.Count < 30) examples.Add("[" + text.Replace("\0", "\\0") + "] " + styles +
                        " decimal=" + expectedSuccess + "/" + expected + " fixed=" + actualSuccess + "/" + actual + " separator=" + nfi.NumberDecimalSeparator);
                }
                checkedCount++;
            }
            TestContext.Progress.WriteLine("FIXED_PARSE_ALL_STYLES checked=" + checkedCount + " failures=" + failures);
            Assert.AreEqual(0, failures, string.Join("\n", examples));
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void RoundTripPreservesEverySpecialAndFullWidthFiniteValuesAcrossCultures(FixedKind kind)
        {
            var custom = SeparateCurrencySeparators();
            custom.NegativeSign = "neg"; custom.PositiveSign = "pos";
            custom.NaNSymbol = "nan!"; custom.PositiveInfinitySymbol = "inf!"; custom.NegativeInfinitySymbol = "neg-inf!";
            foreach (int p in new[] { 0, 1, 3, 9, kind < FixedKind.Signed64 ? 9 : 18 })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p));
                var f = new FixedFormat(kind, state);
                foreach (NumberFormatInfo nfi in new[] { CultureInfo.InvariantCulture.NumberFormat,
                    CultureInfo.GetCultureInfo("pt-BR").NumberFormat, CultureInfo.GetCultureInfo("fr-FR").NumberFormat, custom })
                foreach (ulong raw in new[] { 0UL, f.NegativeZero, f.NaN, f.PositiveInfinity, f.NegativeInfinity,
                    1UL, f.Finite(1, !f.Unsigned), f.Scale, f.Scale - 1, f.MaxPositive, f.MinValue, f.MaxPositive - 1,
                    f.MaxPositive / 3, f.MaxPositive / 7 })
                {
                    string text = FixedManaged.Format(raw, kind, state, "R", nfi);
                    Assert.IsTrue(FixedManaged.TryParse(text, NumberStyles.Float, nfi, kind, state, out ulong parsed, out FixedStatus status),
                        kind + " p=" + p + " raw=" + raw + " [" + text + "]");
                    Assert.AreEqual(raw, parsed, text); Assert.AreEqual(FixedStatus.None, status, text);
                }
            }
        }

        [Test]
        public void DecimalFactoriesHandleMidpointTiesSignsAndAllDecimalScales()
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (int p in new[] { 0, 3, 9, kind < FixedKind.Signed64 ? 9 : 18 })
            foreach (decimal value in new[] { 0m, .0000000000000000000000000001m, -.0000000000000000000000000001m,
                .0005m, -.0005m, .0015m, -.0015m, 1.2345m, -1.2345m, 2.5m, -2.5m, decimal.MaxValue, decimal.MinValue })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, rounding,
                    FixedOverflow.WrapFinite, unsignedNegative: FixedUnsignedNegative.WrapFinite));
                int[] parts = decimal.GetBits(value);
                BigInteger n = (uint)parts[0] | (BigInteger)(uint)parts[1] << 32 | (BigInteger)(uint)parts[2] << 64;
                bool negative = parts[3] < 0;
                BigInteger divisor = BigInteger.Pow(10, parts[3] >> 16 & 255), scale = BigInteger.Pow(10, p);
                BigInteger q = BigInteger.DivRem(n * scale, divisor, out BigInteger rem);
                bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                    rounding == FixedRounding.TowardNegativeInfinity && negative;
                if (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero)
                    increment = rem * 2 > divisor || rem * 2 == divisor && (rounding == FixedRounding.AwayFromZero || !q.IsEven);
                if (!rem.IsZero && increment) q++;
                BigInteger signed = negative ? -q : q;
                int bits = kind < FixedKind.Signed64 ? 32 : 64;
                bool unsigned = kind == FixedKind.Unsigned32 || kind == FixedKind.Unsigned64;
                BigInteger mask = (BigInteger.One << bits) - 1, min = unsigned ? 0 : -(BigInteger.One << (bits - 1)) + 2;
                BigInteger count = (BigInteger.One << bits) - 4, max = min + count - 1;
                FixedStatus expectedStatus = rem.IsZero ? FixedStatus.None : FixedStatus.Inexact;
                if (unsigned && signed.Sign < 0) expectedStatus |= FixedStatus.NegativeResult;
                else if (signed < min || signed > max) expectedStatus |= FixedStatus.Overflow;
                BigInteger encoded = signed;
                if ((expectedStatus & (FixedStatus.NegativeResult | FixedStatus.Overflow)) != 0)
                    encoded = ((signed - min) % count + count) % count + min;
                ulong expected = (ulong)(encoded & mask);
                if (q.IsZero && negative) expected = unsigned ? (ulong)mask - 3 : (1UL << (bits - 1)) - 2;
                Assert.AreEqual(expected, FixedManaged.FromDecimal(value, kind, state, out FixedStatus status),
                    kind + " " + rounding + " p=" + p + " value=" + value);
                Assert.AreEqual(expectedStatus, status);
            }
        }
    }
}
