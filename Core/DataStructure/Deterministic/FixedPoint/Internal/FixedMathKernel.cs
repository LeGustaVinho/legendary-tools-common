namespace LegendaryTools.FixedPoint
{
    internal enum FixedUnary : byte
    {
        Abs, Ceiling, Floor, Truncate, Round, Sqrt, Cbrt, Sin, Cos, Tan, Asin, Acos, Atan,
        Sinh, Cosh, Tanh, Asinh, Acosh, Atanh, Exp, Log, Log2, Log10, Reciprocal, ReciprocalSqrt
    }

    internal static partial class FixedMathKernel
    {
        internal static int Precision(FixedRuntimeState state) => state.Settings.MathProfile == FixedMathProfile.Fast ? 128 : 256;
        private static FixedQ Constant(FixedBigUInt magnitude, int precision) => FixedMathConstants.AtPrecision(magnitude, precision);

        internal static ulong FromQ(FixedQ value, FixedFormat f, FixedSettings settings, int fractionBits, ref FixedStatus status)
        {
            FixedBigUInt scaled = FixedBigUInt.Multiply(value.Magnitude, FixedBigUInt.From(f.Scale));
            FixedBigUInt q = value.Magnitude.BitLength + FixedUInt128.Bits(f.Scale) <= 512 ? FixedBigUInt.ShiftRight(scaled, fractionBits) :
                FixedBigUInt.Multiply(value.Magnitude, FixedBigUInt.From(f.Scale), fractionBits);
            if (scaled.AnyBelow(fractionBits))
            {
                status |= FixedStatus.Inexact;
                bool increment = settings.Rounding == FixedRounding.TowardPositiveInfinity && !value.Negative ||
                    settings.Rounding == FixedRounding.TowardNegativeInfinity && value.Negative;
                if (settings.Rounding == FixedRounding.ToEven || settings.Rounding == FixedRounding.AwayFromZero)
                    increment = scaled.Bit(fractionBits - 1) && (scaled.AnyBelow(fractionBits - 1) ||
                        settings.Rounding == FixedRounding.AwayFromZero || q.Bit(0));
                if (increment) q = FixedBigUInt.Add(q, FixedBigUInt.From(1));
            }
            return PackBig(q, value.Negative, f, settings, ref status);
        }

        internal static ulong PackBig(FixedBigUInt magnitude, bool negative, FixedFormat f, FixedSettings settings, ref FixedStatus status)
        {
            if (magnitude.BitLength > 128) return FixedKernel.Oversized(magnitude.Modulo(f.FiniteCount), negative, f, settings, ref status);
            return FixedKernel.Pack(new FixedUInt128(magnitude.Low, magnitude.Get(2) | (ulong)magnitude.Get(3) << 32), negative, f, settings, ref status);
        }

        internal static ulong Constant(FixedBigUInt value, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            return state.IsInitialized ? FromQ(new FixedQ(value), f, state.Settings, 256, ref status) : f.NaN;
        }

        internal static ulong Unary(ulong raw, FixedKind kind, FixedRuntimeState state, FixedUnary operation, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded x = f.Decode(raw);
            if (x.IsNaN) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            if (operation == FixedUnary.Abs)
                return x.IsFinite ? FixedKernel.Pack(new FixedUInt128(x.Magnitude), false, f, state.Settings, ref status) : f.PositiveInfinity;
            if (operation == FixedUnary.Ceiling || operation == FixedUnary.Floor || operation == FixedUnary.Truncate || operation == FixedUnary.Round)
                return x.IsFinite ? Round(raw, kind, state, 0, operation == FixedUnary.Ceiling ? FixedRounding.TowardPositiveInfinity :
                    operation == FixedUnary.Floor ? FixedRounding.TowardNegativeInfinity : operation == FixedUnary.Truncate ? FixedRounding.TowardZero : state.Settings.Rounding, out status) : raw;
            if (operation == FixedUnary.Sqrt || operation == FixedUnary.Cbrt)
            {
                if (operation == FixedUnary.Sqrt && x.Negative && !x.IsZero)
                { status |= FixedStatus.InvalidDomain; return f.NaN; }
                if (!x.IsFinite || x.IsZero) return raw;
                FixedBigUInt radicand = FixedBigUInt.Multiply(FixedBigUInt.From(x.Magnitude), FixedBigUInt.From(f.Scale));
                if (operation == FixedUnary.Cbrt) radicand = FixedBigUInt.Multiply(radicand, FixedBigUInt.From(f.Scale));
                FixedBigUInt root = operation == FixedUnary.Sqrt ? FixedBigUInt.Sqrt(radicand) : CubeRoot(radicand);
                FixedBigUInt square = FixedBigUInt.Multiply(root, root);
                FixedBigUInt powered = operation == FixedUnary.Sqrt ? square : FixedBigUInt.Multiply(square, root);
                if (FixedBigUInt.Compare(powered, radicand) != 0)
                {
                    status |= FixedStatus.Inexact;
                    bool inc = state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && !x.Negative ||
                        state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && x.Negative;
                    if (state.Settings.Rounding == FixedRounding.ToEven || state.Settings.Rounding == FixedRounding.AwayFromZero)
                    {
                        FixedBigUInt midpoint = FixedBigUInt.Add(FixedBigUInt.ShiftLeft(root, 1), FixedBigUInt.From(1));
                        FixedBigUInt midSquare = FixedBigUInt.Multiply(midpoint, midpoint);
                        int c = FixedBigUInt.Compare(FixedBigUInt.ShiftLeft(radicand, operation == FixedUnary.Sqrt ? 2 : 3),
                            operation == FixedUnary.Sqrt ? midSquare : FixedBigUInt.Multiply(midSquare, midpoint));
                        inc = c > 0 || c == 0 && (state.Settings.Rounding == FixedRounding.AwayFromZero || root.Bit(0));
                    }
                    if (inc) root = FixedBigUInt.Add(root, FixedBigUInt.From(1));
                }
                return PackBig(root, x.Negative, f, state.Settings, ref status);
            }
            if (operation == FixedUnary.Reciprocal || operation == FixedUnary.ReciprocalSqrt)
            {
                if (operation == FixedUnary.ReciprocalSqrt)
                {
                    if (x.Negative && !x.IsZero) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (!x.IsFinite) return 0;
                    if (x.IsZero) return FixedKernel.ZeroDivision(false, x.Negative, f, state.Settings, ref status);
                    int p = Precision(state);
                    FixedQ v = FixedQ.From(x.Magnitude, false, f.Scale, p);
                    FixedQ root = new FixedQ(FixedBigUInt.Sqrt(v.Magnitude, p));
                    return FromQ(FixedQ.Divide(FixedQ.One(p), root, p), f, state.Settings, p, ref status);
                }
                if (x.IsZero) return FixedKernel.ZeroDivision(false, x.Negative, f, state.Settings, ref status);
                if (!x.IsFinite) return f.Finite(0, x.Negative);
                return FixedKernel.FromRatio(f.Scale, x.Magnitude, x.Negative, kind, state, out status);
            }
            int bits = Precision(state);
            // Wide arguments and tangent's conditioning need extra guard bits even in the Fast profile.
            if (bits == 128 && x.IsFinite && x.Magnitude / f.Scale >= (1UL << 20)) bits = 256;
            FixedQ one = FixedQ.One(bits);
            FixedQ qx = FixedQ.From(x.Magnitude, x.Negative, f.Scale, bits);
            switch (operation)
            {
                case FixedUnary.Sin:
                case FixedUnary.Cos:
                case FixedUnary.Tan:
                    if (!x.IsFinite) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (x.IsZero) return operation == FixedUnary.Cos ? FixedKernel.FromInteger(1, false, kind, state, out status) : raw;
                    SinCos(qx, bits, out FixedQ sine, out FixedQ cosine);
                    if (operation == FixedUnary.Tan && bits == 128 && cosine.Magnitude.BitLength < bits - 8)
                    {
                        bits = 256; qx = FixedQ.From(x.Magnitude, x.Negative, f.Scale, bits);
                        SinCos(qx, bits, out sine, out cosine);
                    }
                    return FromQ(operation == FixedUnary.Sin ? sine : operation == FixedUnary.Cos ? cosine : FixedQ.Divide(sine, cosine, bits),
                        f, state.Settings, bits, ref status);
                case FixedUnary.Atan:
                    if (x.IsZero) return raw;
                    return FromQ(!x.IsFinite ? new FixedQ(Constant(FixedMathConstants.HalfPi, bits).Magnitude, x.Negative) : Atan2(qx, one, bits), f, state.Settings, bits, ref status);
                case FixedUnary.Asin:
                case FixedUnary.Acos:
                    if (!x.IsFinite || FixedBigUInt.Compare(qx.Magnitude, one.Magnitude) > 0)
                    { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (operation == FixedUnary.Asin && x.IsZero) return raw;
                    FixedQ complement = new FixedQ(FixedBigUInt.Sqrt(FixedQ.Subtract(one, FixedQ.Multiply(qx, qx, bits)).Magnitude, bits));
                    return FromQ(operation == FixedUnary.Asin ? Atan2(qx, complement, bits) : Atan2(complement, qx, bits), f, state.Settings, bits, ref status);
                case FixedUnary.Log:
                case FixedUnary.Log2:
                case FixedUnary.Log10:
                    if (x.IsZero) return f.NegativeInfinity;
                    if (x.Negative) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (!x.IsFinite) return f.PositiveInfinity;
                    FixedQ log = Log(qx, bits);
                    if (operation == FixedUnary.Log2) log = FixedQ.Divide(log, Constant(FixedMathConstants.Ln2, bits), bits);
                    if (operation == FixedUnary.Log10) log = FixedQ.Divide(log, Constant(FixedMathConstants.Ln10, bits), bits);
                    return FromQ(log, f, state.Settings, bits, ref status);
                case FixedUnary.Exp:
                    if (!x.IsFinite) return x.Negative ? 0 : f.PositiveInfinity;
                    return ExpToRaw(qx, f, state.Settings, bits, ref status);
                case FixedUnary.Sinh:
                case FixedUnary.Cosh:
                case FixedUnary.Tanh:
                    if (!x.IsFinite)
                    {
                        if (operation == FixedUnary.Cosh) return f.PositiveInfinity;
                        if (operation == FixedUnary.Sinh) return f.Infinity(x.Negative);
                        return FixedKernel.FromInteger(1, x.Negative, kind, state, out status);
                    }
                    if (operation != FixedUnary.Cosh && x.IsZero) return raw;
                    if (operation == FixedUnary.Tanh) status |= FixedStatus.Inexact;
                    if (operation == FixedUnary.Tanh && FixedQ.Compare(qx.Abs, FixedQ.Integer(96, bits)) > 0)
                    {
                        // Finite tanh approaches one from inside; retain the discarded fraction and its direction.
                        bool towardZero = state.Settings.Rounding == FixedRounding.TowardZero ||
                            state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && x.Negative ||
                            state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && !x.Negative;
                        return FixedKernel.Pack(new FixedUInt128(f.Scale - (towardZero ? 1UL : 0UL)),
                            x.Negative, f, state.Settings, ref status);
                    }
                    if (FixedQ.Compare(qx.Abs, FixedQ.Integer(176, bits)) > 0)
                        return TranscendentalOverflow(operation == FixedUnary.Sinh && x.Negative, f, state.Settings, ref status);
                    FixedQ ep = Exp(qx.Abs, bits), en = FixedQ.Divide(one, ep, bits);
                    FixedQ hyper = operation == FixedUnary.Cosh ? FixedQ.Add(ep, en).Shifted(-1) :
                        operation == FixedUnary.Sinh ? new FixedQ(FixedQ.Subtract(ep, en).Shifted(-1).Magnitude, x.Negative) :
                        new FixedQ(FixedQ.Divide(FixedQ.Subtract(ep, en), FixedQ.Add(ep, en), bits).Magnitude, x.Negative);
                    return FromQ(hyper, f, state.Settings, bits, ref status);
                case FixedUnary.Asinh:
                    if (!x.IsFinite || x.IsZero) return raw;
                    FixedQ asinh = Log(FixedQ.Add(qx.Abs, new FixedQ(FixedBigUInt.Sqrt(FixedQ.Add(FixedQ.Multiply(qx, qx, bits), one).Magnitude, bits))), bits);
                    asinh.Negative = x.Negative;
                    return FromQ(asinh, f, state.Settings, bits, ref status);
                case FixedUnary.Acosh:
                    if (x.Negative || x.IsFinite && FixedQ.Compare(qx, one) < 0)
                    { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (!x.IsFinite) return f.PositiveInfinity;
                    return FromQ(Log(FixedQ.Add(qx, new FixedQ(FixedBigUInt.Sqrt(FixedQ.Subtract(FixedQ.Multiply(qx, qx, bits), one).Magnitude, bits))), bits), f, state.Settings, bits, ref status);
                case FixedUnary.Atanh:
                    int compare = FixedBigUInt.Compare(qx.Magnitude, one.Magnitude);
                    if (!x.IsFinite || compare > 0) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (compare == 0) return f.Infinity(x.Negative);
                    if (x.IsZero) return raw;
                    return FromQ(Log(FixedQ.Divide(FixedQ.Add(one, qx), FixedQ.Subtract(one, qx), bits), bits).Shifted(-1), f, state.Settings, bits, ref status);
                default: status |= FixedStatus.InvalidDomain; return f.NaN;
            }
        }

        internal static ulong Round(ulong raw, FixedKind kind, FixedRuntimeState state, int digits, FixedRounding rounding, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            if (digits < 0 || digits > 18 || rounding > FixedRounding.TowardNegativeInfinity)
            { status |= FixedStatus.InvalidDomain; return f.NaN; }
            FixedDecoded x = f.Decode(raw);
            if (!x.IsFinite) return raw;
            if (digits >= f.DecimalPlaces) return raw;
            ulong factor = FixedRuntimeState.PowerOfTen(f.DecimalPlaces - digits), q = x.Magnitude / factor, rem = x.Magnitude % factor;
            if (rem != 0) status |= FixedStatus.Inexact;
            if (FixedUInt128.ShouldIncrement(q, rem, factor, x.Negative, rounding)) q++;
            return FixedKernel.Pack(FixedUInt128.Multiply(q, factor, state.Settings.ArithmeticMode), x.Negative, f, state.Settings, ref status);
        }

        private static FixedBigUInt CubeRoot(FixedBigUInt value)
        {
            FixedBigUInt r = default;
            for (int i = (value.BitLength + 2) / 3 - 1; i >= 0; i--)
            {
                FixedBigUInt candidate = r; candidate.SetBit(i);
                if (FixedBigUInt.Compare(FixedBigUInt.Multiply(FixedBigUInt.Multiply(candidate, candidate), candidate), value) <= 0) r = candidate;
            }
            return r;
        }

        internal static void SinCos(FixedQ angle, int bits, out FixedQ sine, out FixedQ cosine)
        {
            FixedQ pi = Constant(FixedMathConstants.Pi, bits), halfPi = Constant(FixedMathConstants.HalfPi, bits);
            FixedQ z = new FixedQ(FixedBigUInt.Mod(angle.Magnitude, Constant(FixedMathConstants.Tau, bits).Magnitude), angle.Negative);
            if (FixedQ.Compare(z, pi) > 0) z = FixedQ.Subtract(z, pi.Shifted(1));
            if (FixedQ.Compare(z, pi.Negated) < 0) z = FixedQ.Add(z, pi.Shifted(1));
            bool flip = false;
            if (FixedQ.Compare(z, halfPi) > 0) { z = FixedQ.Subtract(z, pi); flip = true; }
            else if (FixedQ.Compare(z, halfPi.Negated) < 0) { z = FixedQ.Add(z, pi); flip = true; }
            FixedQ x = Constant(FixedMathConstants.CordicGainInverse, bits), y = default;
            for (int i = 0; i < bits; i++)
            {
                FixedQ dx = y.Shifted(-i), dy = x.Shifted(-i), atan = Constant(FixedMathConstants.Atan(i), bits);
                bool negative = z.Negative && !z.IsZero;
                x = negative ? FixedQ.Add(x, dx) : FixedQ.Subtract(x, dx);
                y = negative ? FixedQ.Subtract(y, dy) : FixedQ.Add(y, dy);
                z = negative ? FixedQ.Add(z, atan) : FixedQ.Subtract(z, atan);
            }
            sine = flip ? y.Negated : y; cosine = flip ? x.Negated : x;
        }

        internal static FixedQ Atan2(FixedQ y, FixedQ x, int bits)
        {
            if (y.IsZero)
                return x.Negative ? new FixedQ(Constant(FixedMathConstants.Pi, bits).Magnitude, y.Negative) : new FixedQ(default, y.Negative);
            if (x.IsZero) return new FixedQ(Constant(FixedMathConstants.HalfPi, bits).Magnitude, y.Negative);
            FixedQ z = default;
            if (x.Negative)
            { z = new FixedQ(Constant(FixedMathConstants.Pi, bits).Magnitude, y.Negative); x = x.Negated; y = y.Negated; }
            for (int i = 0; i < bits; i++)
            {
                if (y.IsZero) break;
                FixedQ dx = y.Shifted(-i), dy = x.Shifted(-i), atan = Constant(FixedMathConstants.Atan(i), bits);
                bool negative = y.Negative;
                x = negative ? FixedQ.Subtract(x, dx) : FixedQ.Add(x, dx);
                y = negative ? FixedQ.Add(y, dy) : FixedQ.Subtract(y, dy);
                z = negative ? FixedQ.Subtract(z, atan) : FixedQ.Add(z, atan);
            }
            return z;
        }

        internal static FixedQ Log(FixedQ x, int bits)
        {
            int exponent = x.Magnitude.BitLength - 1 - bits;
            FixedQ m = x.Shifted(-exponent), one = FixedQ.One(bits);
            FixedQ z = FixedQ.Divide(FixedQ.Subtract(m, one), FixedQ.Add(m, one), bits);
            FixedQ square = FixedQ.Multiply(z, z, bits), term = z, sum = z;
            for (ulong n = 3; n < 1024; n += 2)
            {
                term = FixedQ.Multiply(term, square, bits);
                FixedQ add = term.DividedBy(n);
                if (add.IsZero) break;
                sum = FixedQ.Add(sum, add);
            }
            FixedQ k = FixedQ.Integer((ulong)(exponent < 0 ? -exponent : exponent), bits, exponent < 0);
            return FixedQ.Add(sum.Shifted(1), FixedQ.Multiply(k, Constant(FixedMathConstants.Ln2, bits), bits));
        }

        internal static FixedQ Exp(FixedQ x, int bits)
        {
            FixedQ ln2 = Constant(FixedMathConstants.Ln2, bits), one = FixedQ.One(bits);
            FixedQ ratio = FixedQ.Divide(x, ln2, bits);
            ulong kMagnitude = FixedBigUInt.ShiftRight(ratio.Magnitude, bits).Low;
            if (ratio.Magnitude.Bit(bits - 1)) kMagnitude++;
            int k = x.Negative ? -(int)kMagnitude : (int)kMagnitude;
            FixedQ r = FixedQ.Subtract(x, FixedQ.Multiply(FixedQ.Integer(kMagnitude, bits, x.Negative), ln2, bits));
            FixedQ term = one, sum = one;
            for (ulong n = 1; n < 256; n++)
            {
                term = FixedQ.Multiply(term, r, bits).DividedBy(n);
                if (term.IsZero) break;
                sum = FixedQ.Add(sum, term);
            }
            return sum.Shifted(k);
        }

        internal static ulong ExpToRaw(FixedQ x, FixedFormat f, FixedSettings settings, int bits, ref FixedStatus status,
            bool negativeResult = false)
        {
            bool outsideGuard = FixedQ.Compare(x.Abs, FixedQ.Integer(176, bits)) > 0;
            if (outsideGuard && !x.Negative)
                return TranscendentalOverflow(negativeResult, f, settings, ref status);
            FixedQ result = outsideGuard ? default : Exp(x, bits);
            if (x.Negative && result.IsZero)
            {
                // A nonzero exponential may underflow even the guard accumulator, especially in Fast.
                status |= FixedStatus.Inexact;
                bool away = settings.Rounding == FixedRounding.TowardPositiveInfinity && !negativeResult ||
                    settings.Rounding == FixedRounding.TowardNegativeInfinity && negativeResult;
                return FixedKernel.Pack(new FixedUInt128(away ? 1UL : 0UL), negativeResult, f, settings, ref status);
            }
            result.Negative = negativeResult;
            return FromQ(result, f, settings, bits, ref status);
        }

        internal static ulong TranscendentalOverflow(bool negative, FixedFormat f, FixedSettings settings, ref FixedStatus status)
        {
            // A bounded guard accumulator cannot recover the integer residue of exp(2^64).
            // Refuse an invented finite residue when wrapping an unbounded transcendental.
            if (negative && f.Unsigned)
            {
                status |= FixedStatus.NegativeResult;
                return settings.UnsignedNegative == FixedUnsignedNegative.SaturateZero ? 0UL : f.NaN;
            }
            status |= FixedStatus.Overflow;
            return settings.Overflow == FixedOverflow.Saturate ? f.Finite(negative ? f.MaxNegative : f.MaxPositive, negative) :
                settings.Overflow == FixedOverflow.NaN || settings.Overflow == FixedOverflow.WrapFinite ? f.NaN : f.Infinity(negative);
        }
    }
}
