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

        internal static ulong FromDecimalDigits(BigInteger digits, bool negative, long exponent,
            FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            if (digits.IsZero) return f.Finite(0, negative);
            long power = exponent + f.DecimalPlaces;
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
            if (!state.IsInitialized || text == null || text.Length == 0) return false;
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider ?? CultureInfo.InvariantCulture);
            string value = text;
            if ((styles & NumberStyles.AllowLeadingWhite) != 0) value = value.TrimStart();
            if ((styles & NumberStyles.AllowTrailingWhite) != 0) value = value.TrimEnd();
            if (value.Length == 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])) return false;
            if (string.Equals(value, nfi.NaNSymbol, StringComparison.OrdinalIgnoreCase)) { raw = f.NaN; status = FixedStatus.None; return true; }
            if (string.Equals(value, nfi.PositiveInfinitySymbol, StringComparison.OrdinalIgnoreCase)) { raw = f.PositiveInfinity; status = FixedStatus.None; return true; }
            if (string.Equals(value, nfi.NegativeInfinitySymbol, StringComparison.OrdinalIgnoreCase)) { raw = f.NegativeInfinity; status = FixedStatus.None; return true; }

            bool negative = false, hasSign = false;
            if (value[0] == '(' && value[value.Length - 1] == ')' && (styles & NumberStyles.AllowParentheses) != 0)
            { negative = true; hasSign = true; value = value.Substring(1, value.Length - 2).Trim(); }
            bool currency = false;
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && !string.IsNullOrEmpty(nfi.CurrencySymbol))
            {
                int first = value.IndexOf(nfi.CurrencySymbol, StringComparison.Ordinal);
                if (first >= 0)
                {
                    if (value.IndexOf(nfi.CurrencySymbol, first + nfi.CurrencySymbol.Length, StringComparison.Ordinal) >= 0) return false;
                    if (!IsCurrencyMargin(value.Substring(0, first), nfi) &&
                        !IsCurrencyMargin(value.Substring(first + nfi.CurrencySymbol.Length), nfi)) return false;
                    value = value.Remove(first, nfi.CurrencySymbol.Length).Trim();
                    currency = true;
                }
            }
            if ((styles & NumberStyles.AllowLeadingSign) != 0 && !hasSign)
            {
                if (TakePrefix(ref value, nfi.NegativeSign)) { negative = true; hasSign = true; }
                else if (TakePrefix(ref value, nfi.PositiveSign)) hasSign = true;
            }
            if ((styles & NumberStyles.AllowTrailingSign) != 0 && !hasSign)
            {
                if (TakeSuffix(ref value, nfi.NegativeSign)) { negative = true; hasSign = true; }
                else if (TakeSuffix(ref value, nfi.PositiveSign)) hasSign = true;
            }
            if (value.Length == 0) return false;
            if (string.Equals(value, nfi.PositiveInfinitySymbol, StringComparison.OrdinalIgnoreCase))
            { raw = f.Infinity(negative); status = FixedStatus.None; return true; }
            long exponent = 0;
            int e = value.IndexOfAny(new[] { 'e', 'E' });
            if (e >= 0)
            {
                if ((styles & NumberStyles.AllowExponent) == 0 || e == 0 || e == value.Length - 1) return false;
                string exp = value.Substring(e + 1);
                bool expNegative = TakePrefix(ref exp, nfi.NegativeSign);
                if (!expNegative) TakePrefix(ref exp, nfi.PositiveSign);
                if (exp.Length == 0) return false;
                foreach (char c in exp)
                {
                    if (c < '0' || c > '9') return false;
                    exponent = Math.Min(1000000000L, exponent * 10 + c - '0');
                }
                if (expNegative) exponent = -exponent;
                value = value.Substring(0, e);
            }
            string decimalSeparator = currency ? nfi.CurrencyDecimalSeparator : nfi.NumberDecimalSeparator;
            string groupSeparator = currency ? nfi.CurrencyGroupSeparator : nfi.NumberGroupSeparator;
            int dot = string.IsNullOrEmpty(decimalSeparator) ? -1 : value.IndexOf(decimalSeparator, StringComparison.Ordinal);
            int fractionLength = 0;
            string integerPart = value, fractionPart = "";
            if (dot >= 0)
            {
                if ((styles & NumberStyles.AllowDecimalPoint) == 0 ||
                    value.IndexOf(decimalSeparator, dot + decimalSeparator.Length, StringComparison.Ordinal) >= 0) return false;
                integerPart = value.Substring(0, dot);
                fractionPart = value.Substring(dot + decimalSeparator.Length);
                fractionLength = fractionPart.Length;
            }
            if ((styles & NumberStyles.AllowThousands) != 0 && !string.IsNullOrEmpty(groupSeparator))
            {
                if (integerPart.StartsWith(groupSeparator, StringComparison.Ordinal)) return false;
                integerPart = integerPart.Replace(groupSeparator, "");
            }
            string digits = integerPart + fractionPart;
            if (digits.Length == 0) return false;
            foreach (char c in digits) if (c < '0' || c > '9') return false;
            if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger magnitude)) return false;
            raw = FromDecimalDigits(magnitude, negative, exponent - fractionLength, kind, state, out status);
            return status.IsSuccess();
        }

        private static bool IsCurrencyMargin(string value, NumberFormatInfo nfi)
        {
            value = value.Trim();
            return value.Length == 0 || value == nfi.NegativeSign || value == nfi.PositiveSign;
        }

        private static bool TakePrefix(ref string value, string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
            value = value.Substring(prefix.Length); return true;
        }
        private static bool TakeSuffix(ref string value, string suffix)
        {
            if (string.IsNullOrEmpty(suffix) || !value.EndsWith(suffix, StringComparison.Ordinal)) return false;
            value = value.Substring(0, value.Length - suffix.Length); return true;
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
