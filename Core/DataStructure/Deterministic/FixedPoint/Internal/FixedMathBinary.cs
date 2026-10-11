namespace LegendaryTools.FixedPoint
{
    internal static partial class FixedMathKernel
    {
        internal static void SinCos(ulong raw, FixedKind kind, FixedRuntimeState state, out ulong sine, out ulong cosine, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); FixedDecoded x = f.Decode(raw);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            sine = cosine = f.NaN;
            if (!state.IsInitialized) return;
            if (!x.IsFinite) { status |= FixedStatus.InvalidDomain; return; }
            if (x.IsZero) { sine = raw; cosine = FixedKernel.FromInteger(1, false, kind, state, out status); return; }
            int p = Precision(state);
            if (p == 128 && x.Magnitude / f.Scale >= (1UL << 20)) p = 256;
            SinCos(FixedQ.From(x.Magnitude, x.Negative, f.Scale, p), p, out FixedQ sin, out FixedQ cos);
            sine = FromQ(sin, f, state.Settings, p, ref status); cosine = FromQ(cos, f, state.Settings, p, ref status);
        }

        internal static ulong Log(ulong raw, ulong baseBits, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded x = f.Decode(raw), b = f.Decode(baseBits);
            if (x.IsNaN || b.IsNaN || x.Negative && !x.IsZero || b.Negative && !b.IsZero || b.IsFinite && b.Magnitude == f.Scale ||
                (b.IsZero || !b.IsFinite) && (!x.IsFinite || x.Magnitude != f.Scale))
            { status |= FixedStatus.InvalidDomain; return f.NaN; }
            if (x.IsFinite && x.Magnitude == f.Scale) return f.Finite(0, b.IsZero || b.IsFinite && b.Magnitude < f.Scale);
            if (x.IsZero || !x.IsFinite) return f.Infinity(x.IsZero == (b.Magnitude > f.Scale));
            int p = Precision(state);
            return FromQ(FixedQ.Divide(Log(FixedQ.From(x.Magnitude, false, f.Scale, p), p), Log(FixedQ.From(b.Magnitude, false, f.Scale, p), p), p), f, state.Settings, p, ref status);
        }

        internal static ulong Atan2(ulong yBits, ulong xBits, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded y = f.Decode(yBits), x = f.Decode(xBits);
            if (y.IsNaN || x.IsNaN) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            int p = Precision(state);
            if (!y.IsFinite && !x.IsFinite)
            {
                FixedQ quarter = Constant(FixedMathConstants.Pi, p).Shifted(-2);
                if (x.Negative) quarter = FixedQ.Add(quarter, quarter.Shifted(1));
                quarter.Negative = y.Negative;
                return FromQ(quarter, f, state.Settings, p, ref status);
            }
            if (!y.IsFinite) return FromQ(new FixedQ(Constant(FixedMathConstants.HalfPi, p).Magnitude, y.Negative), f, state.Settings, p, ref status);
            if (!x.IsFinite) return x.Negative ? FromQ(new FixedQ(Constant(FixedMathConstants.Pi, p).Magnitude, y.Negative), f, state.Settings, p, ref status) : f.Finite(0, y.Negative);
            return FromQ(Atan2(FixedQ.From(y.Magnitude, y.Negative, f.Scale, p), FixedQ.From(x.Magnitude, x.Negative, f.Scale, p), p), f, state.Settings, p, ref status);
        }

        internal static ulong Pow(ulong xBits, ulong yBits, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded x = f.Decode(xBits), y = f.Decode(yBits);
            if (y.IsZero || x.IsFinite && !x.Negative && x.Magnitude == f.Scale) return FixedKernel.FromInteger(1, false, kind, state, out status);
            if (x.IsNaN || y.IsNaN) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            bool integer = y.IsFinite && y.Magnitude % f.Scale == 0;
            bool negative = x.Negative && integer && (y.Magnitude / f.Scale & 1) != 0;
            if (!y.IsFinite)
            {
                if (x.IsFinite && x.Magnitude == f.Scale) return FixedKernel.FromInteger(1, false, kind, state, out status);
                bool greater = !x.IsFinite || x.Magnitude > f.Scale;
                return greater != y.Negative ? f.PositiveInfinity : 0;
            }
            if (x.IsZero) return y.Negative ? FixedKernel.ZeroDivision(false, negative, f, state.Settings, ref status) : f.Finite(0, negative);
            if (!x.IsFinite) return y.Negative ? f.Finite(0, negative) : f.Infinity(negative);
            if (x.Negative && !integer) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            int p = Precision(state);
            FixedQ exponent = FixedQ.Multiply(Log(FixedQ.From(x.Magnitude, false, f.Scale, p), p), FixedQ.From(y.Magnitude, y.Negative, f.Scale, p), p);
            return ExpToRaw(exponent, f, state.Settings, p, ref status, negative);
        }

        internal static ulong FusedMultiplyAdd(ulong aBits, ulong bBits, ulong cBits, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded a = f.Decode(aBits), b = f.Decode(bBits), c = f.Decode(cBits);
            if (!a.IsFinite || !b.IsFinite || !c.IsFinite)
            {
                ulong product = FixedKernel.Binary(aBits, bBits, kind, state, FixedBinary.Multiply, out FixedStatus productStatus);
                ulong result = FixedKernel.Binary(product, cBits, kind, state, FixedBinary.Add, out FixedStatus sumStatus);
                status |= productStatus | sumStatus; return result;
            }
            // This sum can carry into bit 129 for unsigned 64-bit operands. Never truncate to UInt128 first.
            FixedQ productQ = new FixedQ(FixedBigUInt.From(FixedUInt128.Multiply(a.Magnitude, b.Magnitude, state.Settings.ArithmeticMode)), a.Negative ^ b.Negative);
            FixedQ cQ = new FixedQ(FixedBigUInt.From(FixedUInt128.Multiply(c.Magnitude, f.Scale, state.Settings.ArithmeticMode)), c.Negative);
            FixedQ sum = FixedQ.Add(productQ, cQ);
            if (sum.IsZero && productQ.Negative != cQ.Negative) sum.Negative = state.Settings.Rounding == FixedRounding.TowardNegativeInfinity;
            FixedBigUInt magnitude = FixedBigUInt.Divide(sum.Magnitude, f.Scale, out ulong remainder);
            if (remainder != 0) status |= FixedStatus.Inexact;
            if (FixedUInt128.ShouldIncrement(magnitude.Low, remainder, f.Scale, sum.Negative, state.Settings.Rounding)) magnitude = FixedBigUInt.Add(magnitude, FixedBigUInt.From(1));
            return PackBig(magnitude, sum.Negative, f, state.Settings, ref status);
        }

        internal static ulong CopySign(ulong value, ulong sign, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded x = f.Decode(value), y = f.Decode(sign);
            if (x.IsNaN) return f.NaN;
            return x.IsFinite ? FixedKernel.Pack(new FixedUInt128(x.Magnitude), y.Negative, f, state.Settings, ref status) : f.Infinity(y.Negative);
        }

        internal static ulong Adjacent(ulong raw, bool increase, FixedKind kind, FixedRuntimeState state)
        {
            var f = new FixedFormat(kind, state); FixedDecoded x = f.Decode(raw);
            if (x.IsNaN) return f.NaN;
            if (!x.IsFinite) return x.Negative ? increase ? f.MinValue : raw : increase ? raw : f.MaxPositive;
            if (x.IsZero) return increase ? 1 : f.Unsigned ? f.NegativeInfinity : f.Finite(1, true);
            if (increase == x.Negative) return f.Finite(x.Magnitude - 1, x.Negative);
            ulong max = x.Negative ? f.MaxNegative : f.MaxPositive;
            return x.Magnitude == max ? f.Infinity(x.Negative) : f.Finite(x.Magnitude + 1, x.Negative);
        }

        internal static int ILogB(ulong raw, FixedKind kind, FixedRuntimeState state)
        {
            var f = new FixedFormat(kind, state); FixedDecoded x = f.Decode(raw);
            if (!state.IsInitialized || x.IsNaN || x.IsZero) return int.MinValue;
            if (!x.IsFinite) return int.MaxValue;
            int exponent = FixedBigUInt.From(x.Magnitude).BitLength - FixedBigUInt.From(f.Scale).BitLength;
            FixedBigUInt a = FixedBigUInt.From(x.Magnitude), b = FixedBigUInt.From(f.Scale);
            if (FixedBigUInt.Compare(exponent >= 0 ? a : FixedBigUInt.ShiftLeft(a, -exponent), exponent >= 0 ? FixedBigUInt.ShiftLeft(b, exponent) : b) < 0) exponent--;
            return exponent;
        }

        internal static ulong ScaleB(ulong raw, int exponent, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded x = f.Decode(raw);
            if (!x.IsFinite || x.IsZero) return raw;
            if (exponent >= 0)
            {
                if (exponent < 128 && FixedBigUInt.From(x.Magnitude).BitLength + exponent <= 128)
                    return PackBig(FixedBigUInt.ShiftLeft(FixedBigUInt.From(x.Magnitude), exponent), x.Negative, f, state.Settings, ref status);
                ulong power = FixedUInt128.PowerOfTwoModulo((uint)exponent, f.FiniteCount, state.Settings.ArithmeticMode);
                FixedUInt128.Divide(FixedUInt128.Multiply(x.Magnitude % f.FiniteCount, power, state.Settings.ArithmeticMode), f.FiniteCount, out ulong residue);
                return FixedKernel.Oversized(residue, x.Negative, f, state.Settings, ref status);
            }
            long shift = -(long)exponent;
            ulong q = shift < 64 ? x.Magnitude >> (int)shift : 0;
            bool remainder = shift >= 64 || (x.Magnitude & ((1UL << (int)shift) - 1)) != 0;
            if (remainder) status |= FixedStatus.Inexact;
            bool increment = remainder && (state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && !x.Negative || state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && x.Negative);
            if (shift <= 64 && (state.Settings.Rounding == FixedRounding.ToEven || state.Settings.Rounding == FixedRounding.AwayFromZero))
            {
                ulong half = 1UL << ((int)shift - 1), rem = shift == 64 ? x.Magnitude : x.Magnitude & ((1UL << (int)shift) - 1);
                increment = rem > half || rem == half && (state.Settings.Rounding == FixedRounding.AwayFromZero || (q & 1) != 0);
            }
            return FixedKernel.Pack(new FixedUInt128(q + (increment ? 1UL : 0)), x.Negative, f, state.Settings, ref status);
        }
    }
}
