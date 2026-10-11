using System;
using System.Globalization;
using System.Numerics;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Managed-only authoring and formatting. No method in this class is called by a numeric Burst kernel.</summary>
    internal static class FixedManaged
    {
        internal static ulong FromDecimal(decimal value, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            int[] bits = decimal.GetBits(value);
            BigInteger magnitude = (uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            bool negative = (bits[3] & int.MinValue) != 0;
            int decimals = (bits[3] >> 16) & 255;
            return FromDecimalDigits(magnitude, negative, -decimals, kind, state, out status);
        }

        internal static ulong FromDecimalDigits(BigInteger digits, bool negative, BigInteger exponent,
            FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            if (digits.IsZero) return f.Finite(0, negative);
            BigInteger power = exponent + f.DecimalPlaces;
            if (power > 40)
            {
                ulong residue = (ulong)(digits % f.FiniteCount);
                BigInteger factor = BigInteger.ModPow(10, power, f.FiniteCount);
                residue = (ulong)((BigInteger)residue * factor % f.FiniteCount);
                return FixedKernel.Oversized(residue, negative, f, state.Settings, ref status);
            }
            BigInteger magnitude;
            if (power >= 0) magnitude = digits * BigInteger.Pow(10, (int)power);
            else
            {
                // Far below one quantum: avoid constructing a denominator with millions of digits.
                int decimalLength = digits.ToString(CultureInfo.InvariantCulture).Length;
                if (-power > decimalLength + 2L)
                {
                    status |= FixedStatus.Inexact;
                    bool away = state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                        state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && negative;
                    return FixedKernel.Pack(new FixedUInt128(away ? 1UL : 0UL), negative, f, state.Settings, ref status);
                }
                BigInteger divisor = BigInteger.Pow(10, (int)-power);
                magnitude = BigInteger.DivRem(digits, divisor, out BigInteger remainder);
                if (!remainder.IsZero)
                {
                    status |= FixedStatus.Inexact;
                    bool increment = state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                        state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && negative;
                    if (state.Settings.Rounding == FixedRounding.ToEven || state.Settings.Rounding == FixedRounding.AwayFromZero)
                        increment = remainder * 2 > divisor || remainder * 2 == divisor &&
                            (state.Settings.Rounding == FixedRounding.AwayFromZero || !magnitude.IsEven);
                    if (increment) magnitude++;
                }
            }
            if (magnitude >> 128 != 0)
                return FixedKernel.Oversized((ulong)(magnitude % f.FiniteCount), negative, f, state.Settings, ref status);
            return FixedKernel.Pack(new FixedUInt128((ulong)(magnitude & ulong.MaxValue), (ulong)(magnitude >> 64)),
                negative, f, state.Settings, ref status);
        }

        internal static decimal ToDecimal(ulong raw, FixedKind kind, FixedRuntimeState state)
        {
            if (!state.IsInitialized) throw new InvalidOperationException("Call FixedRuntime.Initialize before using fixed-point values.");
            var f = new FixedFormat(kind, state);
            FixedDecoded value = f.Decode(raw);
            if (!value.IsFinite) throw new OverflowException("NaN and infinity cannot be represented by decimal.");
            decimal number = (decimal)value.Magnitude / f.Scale;
            return value.Negative ? -number : number;
        }

        internal static string Format(ulong raw, FixedKind kind, FixedRuntimeState state, string format, IFormatProvider provider)
        {
            var f = new FixedFormat(kind, state);
            FixedDecoded value = f.Decode(raw);
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider ?? CultureInfo.InvariantCulture);
            if (!state.IsInitialized || value.IsNaN) return nfi.NaNSymbol;
            if (!value.IsFinite) return value.Negative ? nfi.NegativeInfinitySymbol : nfi.PositiveInfinitySymbol;
            bool roundTrip = string.IsNullOrEmpty(format) || string.Equals(format, "R", StringComparison.OrdinalIgnoreCase);
            string actualFormat = roundTrip ? "F" + f.DecimalPlaces : format;
            if (value.IsZero && value.Negative && !roundTrip)
                return FixedIeee.Double(1UL << 63).ToString(actualFormat, nfi);
            decimal number = (decimal)value.Magnitude / f.Scale;
            if (value.Negative && !value.IsZero) number = -number;
            string text = number.ToString(actualFormat, nfi);
            return value.IsZero && value.Negative ? nfi.NegativeSign + text : text;
        }

        internal static bool TryParse(string text, NumberStyles styles, IFormatProvider provider, FixedKind kind,
            FixedRuntimeState state, out ulong raw, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state);
            raw = f.NaN;
            status = state.IsInitialized ? FixedStatus.InvalidEncoding : FixedStatus.Uninitialized;
            if ((styles & ~NumberStyles.Any) != 0) throw new ArgumentException("Hexadecimal and binary styles are not supported.", nameof(styles));
            if (!state.IsInitialized || string.IsNullOrEmpty(text)) return false;
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider ?? CultureInfo.InvariantCulture);
            if (TryDecimalText(text, styles, nfi, out BigInteger digits, out bool negative, out BigInteger exponent))
            {
                raw = FromDecimalDigits(digits, negative, exponent, kind, state, out status);
                return status.IsSuccess();
            }
            // Floating special names have their own grammar, including Unicode padding,
            // signed NaN and leading signs even when finite-number signs are disabled.
            if (!TrySpecial(text.Trim(), nfi, f, out raw)) return false;
            status = FixedStatus.None; return true;
        }

        private static bool TryDecimalText(string text, NumberStyles styles, NumberFormatInfo nfi,
            out BigInteger digits, out bool negative, out BigInteger exponent)
        {
            digits = exponent = BigInteger.Zero; negative = false;
            int position = 0; bool signSeen = false, parentheses = false, currencySeen = false;
            bool currency = (styles & NumberStyles.AllowCurrencySymbol) != 0;
            // Consume prefixes in their original order: removing a currency symbol first
            // would incorrectly admit "- CUR12" and reject "CUR(12)".
            while (position < text.Length)
            {
                if (IsNumberWhite(text[position]) && (styles & NumberStyles.AllowLeadingWhite) != 0 &&
                    (!signSeen || currencySeen || nfi.NumberNegativePattern == 2)) { position++; continue; }
                if (!signSeen && (styles & NumberStyles.AllowLeadingSign) != 0 && TrySign(text, ref position, nfi, out bool sign))
                { signSeen = true; negative = sign; continue; }
                if (!signSeen && text[position] == '(' && (styles & NumberStyles.AllowParentheses) != 0)
                { signSeen = parentheses = negative = true; position++; continue; }
                if (currency && !currencySeen && MatchToken(text, ref position, nfi.CurrencySymbol))
                { currencySeen = true; continue; }
                break;
            }

            bool decimalSeen = false, digitSeen = false; int fractionDigits = 0;
            var mantissa = new System.Text.StringBuilder();
            string decimalSeparator = currency ? nfi.CurrencyDecimalSeparator : nfi.NumberDecimalSeparator;
            string groupSeparator = currency ? nfi.CurrencyGroupSeparator : nfi.NumberGroupSeparator;
            while (position < text.Length)
            {
                char ch = text[position];
                if (ch >= '0' && ch <= '9')
                { mantissa.Append(ch); digitSeen = true; position++; if (decimalSeen) fractionDigits++; continue; }
                if (!decimalSeen && (styles & NumberStyles.AllowDecimalPoint) != 0 &&
                    (MatchToken(text, ref position, decimalSeparator) ||
                    currency && !currencySeen && MatchToken(text, ref position, nfi.NumberDecimalSeparator)))
                { decimalSeen = true; continue; }
                if (digitSeen && !decimalSeen && (styles & NumberStyles.AllowThousands) != 0 &&
                    (MatchToken(text, ref position, groupSeparator) ||
                    currency && !currencySeen && MatchToken(text, ref position, nfi.NumberGroupSeparator))) continue;
                break;
            }
            if (!digitSeen) return false;
            if (position < text.Length && (text[position] == 'e' || text[position] == 'E') && (styles & NumberStyles.AllowExponent) != 0)
            {
                int exponentStart = position++;
                TrySign(text, ref position, nfi, out bool exponentNegative);
                int firstDigit = position;
                while (position < text.Length && text[position] >= '0' && text[position] <= '9')
                { exponent = exponent * 10 + text[position] - '0'; position++; }
                if (position == firstDigit) position = exponentStart;
                else if (exponentNegative) exponent = -exponent;
            }
            while (position < text.Length)
            {
                if (IsNumberWhite(text[position]) && (styles & NumberStyles.AllowTrailingWhite) != 0) { position++; continue; }
                if (!signSeen && (styles & NumberStyles.AllowTrailingSign) != 0 && TrySign(text, ref position, nfi, out bool sign))
                { signSeen = true; negative = sign; continue; }
                if (parentheses && text[position] == ')') { parentheses = false; position++; continue; }
                if (currency && !currencySeen && MatchToken(text, ref position, nfi.CurrencySymbol))
                { currencySeen = true; continue; }
                break;
            }
            // Number parsing accepts terminal NULs, but embedded NULs are never ignored.
            while (position < text.Length && text[position] == '\0') position++;
            if (parentheses || position != text.Length) return false;
            exponent -= fractionDigits;
            return BigInteger.TryParse(mantissa.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out digits);
        }

        private static bool MatchToken(string text, ref int position, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > text.Length - position) return false;
            for (int i = 0; i < token.Length; i++)
            {
                char expected = token[i], actual = text[position + i];
                // .NET permits ordinary space for a locale's nonbreaking grouping/currency space.
                if (actual != expected && !(actual == ' ' && (expected == '\u00a0' || expected == '\u202f'))) return false;
            }
            position += token.Length; return true;
        }
        private static bool TrySign(string text, ref int position, NumberFormatInfo nfi, out bool negative)
        {
            negative = false;
            if (MatchToken(text, ref position, nfi.PositiveSign)) return true;
            if (MatchToken(text, ref position, nfi.NegativeSign)) { negative = true; return true; }
            // Cultures with one of these minus signs also accept the ASCII hyphen.
            if (position < text.Length && text[position] == '-' && nfi.NegativeSign.Length == 1 &&
                "\u2012\u207b\u208b\u2212\u2796\ufe63\uff0d".IndexOf(nfi.NegativeSign[0]) >= 0)
            { position++; negative = true; return true; }
            return false;
        }
        private static bool IsNumberWhite(char value) => value == ' ' || value >= '\t' && value <= '\r';

        private static bool TrySpecial(string value, NumberFormatInfo nfi, FixedFormat f, out ulong raw)
        {
            raw = f.NaN;
            if (string.Equals(value, nfi.PositiveInfinitySymbol, StringComparison.OrdinalIgnoreCase))
            { raw = f.PositiveInfinity; return true; }
            if (string.Equals(value, nfi.NegativeInfinitySymbol, StringComparison.OrdinalIgnoreCase))
            { raw = f.NegativeInfinity; return true; }
            if (string.Equals(value, nfi.NaNSymbol, StringComparison.OrdinalIgnoreCase)) return true;
            // Special-token signs are case-insensitive and have a different grammar
            // from finite numbers. Negative infinity must match its complete symbol.
            if (value.StartsWith(nfi.PositiveSign, StringComparison.OrdinalIgnoreCase))
            {
                string unsigned = value.Substring(nfi.PositiveSign.Length);
                if (string.Equals(unsigned, nfi.PositiveInfinitySymbol, StringComparison.OrdinalIgnoreCase))
                { raw = f.PositiveInfinity; return true; }
                return string.Equals(unsigned, nfi.NaNSymbol, StringComparison.OrdinalIgnoreCase);
            }
            return value.StartsWith(nfi.NegativeSign, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value.Substring(nfi.NegativeSign.Length), nfi.NaNSymbol, StringComparison.OrdinalIgnoreCase);
        }


        internal static ulong Parse(string text, NumberStyles styles, IFormatProvider provider, FixedKind kind, FixedRuntimeState state)
        {
            TryParse(text, styles, provider, kind, state, out ulong raw, out FixedStatus status);
            if ((status & FixedStatus.Uninitialized) != 0) status.ThrowIfFailure();
            if ((status & FixedStatus.InvalidEncoding) != 0) throw new FormatException("Invalid fixed-point number.");
            return raw;
        }

        internal static object ConvertTo(ulong bits, FixedKind kind, Type destination, FixedRuntimeState state, IFormatProvider provider)
        {
            if (destination == typeof(string)) return Format(bits, kind, state, null, provider);
            if (destination == typeof(float)) return FixedIeee.ToSingle(bits, kind, state);
            if (destination == typeof(double)) return FixedIeee.ToDouble(bits, kind, state);
            if (destination == typeof(decimal)) return ToDecimal(bits, kind, state);
            var f = new FixedFormat(kind, state);
            if (destination == typeof(bool)) return !f.Decode(bits).IsZero;
            if (destination == typeof(char) || destination == typeof(DateTime)) throw new InvalidCastException();
            if (!state.IsInitialized) FixedStatus.Uninitialized.ThrowIfFailure();
            bool unsigned = destination == typeof(byte) || destination == typeof(ushort) || destination == typeof(uint) || destination == typeof(ulong);
            int width = destination == typeof(byte) || destination == typeof(sbyte) ? 8 :
                destination == typeof(short) || destination == typeof(ushort) ? 16 :
                destination == typeof(int) || destination == typeof(uint) ? 32 : 64;
            FixedDecoded decoded = f.Decode(bits);
            if (!decoded.IsFinite) throw new OverflowException("Non-finite numbers cannot be converted to integers.");
            bool negative = decoded.Negative;
            ulong magnitude = decoded.Magnitude / f.Scale, remainder = decoded.Magnitude % f.Scale;
            // IConvertible follows Convert's nearest-even integer conversion; explicit casts truncate.
            if (FixedUInt128.ShouldIncrement(magnitude, remainder, f.Scale, negative, FixedRounding.ToEven)) magnitude++;
            ulong limit = unsigned ? width == 64 ? ulong.MaxValue : (1UL << width) - 1 :
                negative ? 1UL << (width - 1) : (1UL << (width - 1)) - 1;
            if (magnitude > limit || unsigned && negative && magnitude != 0) throw new OverflowException();
            long signed = unchecked((long)(negative ? 0UL - magnitude : magnitude));
            if (destination == typeof(byte)) return (byte)magnitude;
            if (destination == typeof(sbyte)) return (sbyte)signed;
            if (destination == typeof(short)) return (short)signed;
            if (destination == typeof(ushort)) return (ushort)magnitude;
            if (destination == typeof(int)) return (int)signed;
            if (destination == typeof(uint)) return (uint)magnitude;
            if (destination == typeof(long)) return signed;
            if (destination == typeof(ulong)) return magnitude;
            throw new InvalidCastException("Unsupported destination type: " + destination);
        }
    }
}
