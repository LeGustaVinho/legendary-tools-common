namespace LegendaryTools.FixedPoint
{
    internal enum FixedBinary : byte { Add, Subtract, Multiply, Divide, Remainder, IEEERemainder }

    internal static class FixedKernel
    {
        internal static ulong Pack(FixedUInt128 magnitude, bool negative, FixedFormat format,
            FixedSettings settings, ref FixedStatus status)
        {
            if (magnitude.IsZero) return format.Finite(0, negative);
            if (negative && format.Unsigned)
            {
                status |= FixedStatus.NegativeResult;
                if (settings.UnsignedNegative == FixedUnsignedNegative.NaN) return format.NaN;
                if (settings.UnsignedNegative == FixedUnsignedNegative.SaturateZero) return 0;
                return Wrap(magnitude, true, format);
            }
            ulong limit = negative ? format.MaxNegative : format.MaxPositive;
            if (magnitude.Hi == 0 && magnitude.Lo <= limit) return format.Finite(magnitude.Lo, negative);
            status |= FixedStatus.Overflow;
            switch (settings.Overflow)
            {
                case FixedOverflow.Saturate: return format.Finite(limit, negative);
                case FixedOverflow.NaN: return format.NaN;
                case FixedOverflow.WrapFinite: return Wrap(magnitude, negative, format);
                default: return format.Infinity(negative);
            }
        }

        internal static ulong Wrap(FixedUInt128 magnitude, bool negative, FixedFormat format)
        {
            FixedUInt128.Divide(magnitude, format.FiniteCount, out ulong rem);
            if (negative && rem != 0) rem = format.FiniteCount - rem;
            if (format.Unsigned) return rem;
            ulong offset = FixedUInt128.AddModulo(rem, format.MaxNegative, format.FiniteCount);
            return offset < format.MaxNegative ? format.Finite(format.MaxNegative - offset, true) :
                format.Finite(offset - format.MaxNegative, false);
        }

        internal static ulong Oversized(ulong magnitudeModuloFiniteCount, bool negative, FixedFormat f,
            FixedSettings settings, ref FixedStatus status)
        {
            if (negative && f.Unsigned)
            {
                status |= FixedStatus.NegativeResult;
                if (settings.UnsignedNegative == FixedUnsignedNegative.SaturateZero) return 0;
                if (settings.UnsignedNegative == FixedUnsignedNegative.WrapFinite)
                    return Wrap(new FixedUInt128(magnitudeModuloFiniteCount), true, f);
                return f.NaN;
            }
            status |= FixedStatus.Overflow;
            switch (settings.Overflow)
            {
                case FixedOverflow.Saturate: return f.Finite(negative ? f.MaxNegative : f.MaxPositive, negative);
                case FixedOverflow.NaN: return f.NaN;
                case FixedOverflow.WrapFinite: return Wrap(new FixedUInt128(magnitudeModuloFiniteCount), negative, f);
                default: return f.Infinity(negative);
            }
        }

        internal static ulong FromInteger(ulong magnitude, bool negative, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            return Pack(FixedUInt128.Multiply(magnitude, f.Scale, state.Settings.ArithmeticMode), negative, f, state.Settings, ref status);
        }

        internal static ulong FromRatio(ulong numerator, ulong denominator, bool negative, FixedKind kind,
            FixedRuntimeState state, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            if (denominator == 0) return ZeroDivision(numerator == 0, negative, f, state.Settings, ref status);
            FixedUInt128 q = FixedUInt128.RoundedDivide(FixedUInt128.Multiply(numerator, f.Scale, state.Settings.ArithmeticMode),
                denominator, negative, state.Settings.Rounding, out bool inexact);
            if (inexact) status |= FixedStatus.Inexact;
            return Pack(q, negative, f, state.Settings, ref status);
        }

        internal static ulong Convert(ulong bits, FixedKind source, FixedKind destination, FixedRuntimeState state, out FixedStatus status)
        {
            FixedFormat from = new FixedFormat(source, state), to = new FixedFormat(destination, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return to.NaN;
            FixedDecoded value = from.Decode(bits);
            if (value.IsNaN) return to.NaN;
            if (!value.IsFinite) return to.Infinity(value.Negative);
            FixedUInt128 q = FixedUInt128.RoundedDivide(FixedUInt128.Multiply(value.Magnitude, to.Scale, state.Settings.ArithmeticMode),
                from.Scale, value.Negative, state.Settings.Rounding, out bool inexact);
            if (inexact) status |= FixedStatus.Inexact;
            return Pack(q, value.Negative, to, state.Settings, ref status);
        }

        internal static ulong Negate(ulong bits, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded value = f.Decode(bits).Negated;
            if (value.IsNaN) return f.NaN;
            if (!value.IsFinite) return f.Infinity(value.Negative);
            return Pack(new FixedUInt128(value.Magnitude), value.Negative, f, state.Settings, ref status);
        }

        internal static ulong Binary(ulong aBits, ulong bBits, FixedKind kind, FixedRuntimeState state,
            FixedBinary operation, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded a = f.Decode(aBits), b = f.Decode(bBits);
            if (a.IsNaN || b.IsNaN) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            if (operation == FixedBinary.Subtract) b = b.Negated;
            if (operation == FixedBinary.Add || operation == FixedBinary.Subtract)
            {
                if (!a.IsFinite || !b.IsFinite)
                {
                    if (!a.IsFinite && !b.IsFinite && a.Negative != b.Negative)
                    { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    return f.Infinity(!a.IsFinite ? a.Negative : b.Negative);
                }
                FixedUInt128 magnitude;
                bool negative;
                if (a.Negative == b.Negative)
                { magnitude = FixedUInt128.Add(new FixedUInt128(a.Magnitude), new FixedUInt128(b.Magnitude), out _); negative = a.Negative; }
                else if (a.Magnitude > b.Magnitude) { magnitude = new FixedUInt128(a.Magnitude - b.Magnitude); negative = a.Negative; }
                else if (a.Magnitude < b.Magnitude) { magnitude = new FixedUInt128(b.Magnitude - a.Magnitude); negative = b.Negative; }
                else { magnitude = default; negative = state.Settings.Rounding == FixedRounding.TowardNegativeInfinity; }
                return Pack(magnitude, negative, f, state.Settings, ref status);
            }
            bool sign = a.Negative ^ b.Negative;
            if (operation == FixedBinary.Multiply)
            {
                if (!a.IsFinite || !b.IsFinite)
                {
                    if (a.IsZero || b.IsZero) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    return f.Infinity(sign);
                }
                FixedUInt128 q = FixedUInt128.RoundedDivide(FixedUInt128.Multiply(a.Magnitude, b.Magnitude, state.Settings.ArithmeticMode),
                    f.Scale, sign, state.Settings.Rounding, out bool inexact);
                if (inexact) status |= FixedStatus.Inexact;
                return Pack(q, sign, f, state.Settings, ref status);
            }
            if (operation == FixedBinary.Divide)
            {
                if (!a.IsFinite && !b.IsFinite) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                if (!a.IsFinite) return f.Infinity(sign);
                if (!b.IsFinite) return f.Finite(0, sign);
                if (b.Magnitude == 0) return ZeroDivision(a.Magnitude == 0, sign, f, state.Settings, ref status);
                FixedUInt128 q = FixedUInt128.RoundedDivide(FixedUInt128.Multiply(a.Magnitude, f.Scale, state.Settings.ArithmeticMode),
                    b.Magnitude, sign, state.Settings.Rounding, out bool inexact);
                if (inexact) status |= FixedStatus.Inexact;
                return Pack(q, sign, f, state.Settings, ref status);
            }
            if (!a.IsFinite || b.IsZero)
            {
                status |= b.IsZero ? FixedStatus.DivisionByZero | FixedStatus.InvalidDomain : FixedStatus.InvalidDomain;
                return f.NaN;
            }
            if (!b.IsFinite) return aBits & f.Mask;
            ulong rem = a.Magnitude % b.Magnitude;
            bool negativeRem = a.Negative;
            if (operation == FixedBinary.IEEERemainder && FixedUInt128.ShouldIncrement(a.Magnitude / b.Magnitude,
                rem, b.Magnitude, false, FixedRounding.ToEven))
            { rem = b.Magnitude - rem; negativeRem = !negativeRem; }
            return Pack(new FixedUInt128(rem), negativeRem, f, state.Settings, ref status);
        }

        internal static ulong ZeroDivision(bool numeratorIsZero, bool negative, FixedFormat f, FixedSettings settings, ref FixedStatus status)
        {
            status |= FixedStatus.DivisionByZero;
            if (numeratorIsZero) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            if (settings.ZeroDivision == FixedZeroDivision.NaN) return f.NaN;
            if (settings.ZeroDivision == FixedZeroDivision.Saturate) return negative ? f.MinValue : f.MaxPositive;
            return f.Infinity(negative);
        }

        internal static bool Equal(ulong a, ulong b, FixedKind kind, FixedRuntimeState state, bool equalsMethod)
        {
            FixedFormat f = new FixedFormat(kind, state);
            FixedDecoded x = f.Decode(a), y = f.Decode(b);
            if (x.IsNaN || y.IsNaN) return equalsMethod && x.IsNaN && y.IsNaN;
            if (x.IsZero && y.IsZero) return true;
            return (a & f.Mask) == (b & f.Mask);
        }

        internal static int Compare(ulong a, ulong b, FixedKind kind, FixedRuntimeState state)
        {
            FixedFormat f = new FixedFormat(kind, state);
            FixedDecoded x = f.Decode(a), y = f.Decode(b);
            if (x.IsNaN) return y.IsNaN ? 0 : -1;
            if (y.IsNaN) return 1;
            if (x.Class == y.Class && !x.IsFinite) return 0;
            if (x.Class == FixedClass.NegativeInfinity || y.Class == FixedClass.PositiveInfinity) return -1;
            if (x.Class == FixedClass.PositiveInfinity || y.Class == FixedClass.NegativeInfinity) return 1;
            if (x.IsZero && y.IsZero) return 0;
            if (x.Negative != y.Negative) return x.Negative ? -1 : 1;
            int result = x.Magnitude < y.Magnitude ? -1 : x.Magnitude > y.Magnitude ? 1 : 0;
            return x.Negative ? -result : result;
        }

        internal static int Hash(ulong bits, FixedKind kind, FixedRuntimeState state)
        {
            FixedFormat f = new FixedFormat(kind, state);
            FixedDecoded value = f.Decode(bits);
            if (value.IsZero) return 0;
            if (value.IsNaN) return 0x7fc00000;
            return unchecked((int)(bits ^ (bits >> 32)));
        }

        internal static ulong IntegralMagnitude(ulong bits, FixedKind kind, FixedRuntimeState state, bool destinationUnsigned,
            int destinationBits, out bool negative, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            FixedDecoded value = f.Decode(bits);
            negative = value.Negative;
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized || value.IsNaN) { if (value.IsNaN) status |= FixedStatus.InvalidDomain; negative = false; return 0; }
            if (negative && destinationUnsigned && !value.IsZero) { status |= FixedStatus.NegativeResult; return 0; }
            ulong limit = destinationUnsigned ? (destinationBits == 64 ? ulong.MaxValue : (1UL << destinationBits) - 1) :
                negative ? 1UL << (destinationBits - 1) : (1UL << (destinationBits - 1)) - 1;
            if (!value.IsFinite) { status |= FixedStatus.Overflow; return limit; }
            ulong q = value.Magnitude / f.Scale;
            if (value.Magnitude % f.Scale != 0) status |= FixedStatus.Inexact;
            if (q > limit) { status |= FixedStatus.Overflow; return limit; }
            return q;
        }
    }
}
