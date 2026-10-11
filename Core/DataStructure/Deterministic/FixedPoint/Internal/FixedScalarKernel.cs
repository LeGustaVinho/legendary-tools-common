namespace LegendaryTools.FixedPoint
{
    internal enum FixedScalar : byte { Clamp, Lerp, LerpUnclamped, InverseLerp, MoveTowards, Repeat, PingPong, DeltaAngle, LerpAngle, MoveTowardsAngle, SmoothStep, DegreesToRadians, RadiansToDegrees }

    internal static class FixedScalarKernel
    {
        internal static ulong MinMax(ulong a, ulong b, bool maximum, bool number, bool magnitude, FixedKind kind, FixedRuntimeState state)
        {
            var f = new FixedFormat(kind, state); FixedDecoded x = f.Decode(a), y = f.Decode(b);
            if (x.IsNaN) return number ? b : a;
            if (y.IsNaN) return number ? a : b;
            if (magnitude)
            {
                int c = x.IsFinite != y.IsFinite ? x.IsFinite ? -1 : 1 : x.Magnitude < y.Magnitude ? -1 : x.Magnitude > y.Magnitude ? 1 : 0;
                if (c != 0) return (c > 0) == maximum ? a : b;
            }
            int comparison = FixedKernel.Compare(a, b, kind, state);
            if (comparison == 0 && x.IsZero && y.IsZero) return maximum ? x.Negative ? b : a : x.Negative ? a : b;
            return (comparison > 0) == maximum ? a : b;
        }

        internal static int Sign(ulong raw, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); FixedDecoded x = f.Decode(raw);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (x.IsNaN) { status |= FixedStatus.InvalidDomain; return 0; }
            return x.IsZero ? 0 : x.Negative ? -1 : 1;
        }

        private static FixedQ Sum(FixedQ a, FixedQ b, FixedRounding rounding)
        {
            FixedQ sum = FixedQ.Add(a, b);
            if (sum.IsZero && a.Negative != b.Negative) sum.Negative = rounding == FixedRounding.TowardNegativeInfinity;
            return sum;
        }
        private static FixedQ MoveTowards(FixedQ from, FixedQ to, FixedQ delta, FixedRounding rounding)
        {
            FixedQ diff = FixedQ.Subtract(to, from);
            if (!delta.Negative && FixedQ.Compare(diff.Abs, delta) <= 0) return to;
            return Sum(from, diff.Negative ? delta.Negated : delta, rounding);
        }

        private static FixedQ Raw(FixedDecoded value) => new FixedQ(FixedBigUInt.From(value.Magnitude), value.Negative);
        private static FixedQ Times(FixedQ a, FixedQ b) => FixedQ.Multiply(a, b, 0);
        private static FixedQ ClampAmount(FixedQ amount, ulong scale) => amount.Negative ? default :
            FixedBigUInt.Compare(amount.Magnitude, FixedBigUInt.From(scale)) > 0 ? new FixedQ(FixedBigUInt.From(scale)) : amount;
        private static FixedQ RepeatRaw(FixedQ value, FixedBigUInt length)
        {
            FixedBigUInt rem = FixedBigUInt.Mod(value.Magnitude, length);
            return new FixedQ(value.Negative && !rem.IsZero ? FixedBigUInt.Subtract(length, rem) : rem);
        }
        private static FixedQ DeltaRaw(FixedQ from, FixedQ to, ulong scale)
        {
            FixedBigUInt period = FixedBigUInt.Multiply(FixedBigUInt.From(scale), FixedBigUInt.From(360));
            FixedQ rem = RepeatRaw(FixedQ.Subtract(to, from), period);
            FixedBigUInt half = FixedBigUInt.ShiftRight(period, 1);
            return FixedBigUInt.Compare(rem.Magnitude, half) > 0 ? FixedQ.Subtract(rem, new FixedQ(period)) : rem;
        }
        private static ulong RoundRatio(FixedQ numerator, FixedBigUInt denominator, FixedFormat f, FixedSettings settings, ref FixedStatus status)
        {
            FixedBigUInt q = FixedBigUInt.Divide(numerator.Magnitude, denominator);
            FixedBigUInt rem = FixedBigUInt.Mod(numerator.Magnitude, denominator);
            if (!rem.IsZero)
            {
                status |= FixedStatus.Inexact;
                bool increment = settings.Rounding == FixedRounding.TowardPositiveInfinity && !numerator.Negative ||
                    settings.Rounding == FixedRounding.TowardNegativeInfinity && numerator.Negative;
                if (settings.Rounding == FixedRounding.ToEven || settings.Rounding == FixedRounding.AwayFromZero)
                {
                    int c = FixedBigUInt.Compare(FixedBigUInt.ShiftLeft(rem, 1), denominator);
                    increment = c > 0 || c == 0 && (settings.Rounding == FixedRounding.AwayFromZero || q.Bit(0));
                }
                if (increment) q = FixedBigUInt.Add(q, FixedBigUInt.From(1));
            }
            return FixedMathKernel.PackBig(q, numerator.Negative, f, settings, ref status);
        }

        internal static ulong Evaluate(ulong aBits, ulong bBits, ulong cBits, FixedScalar operation, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            FixedDecoded a = f.Decode(aBits), b = f.Decode(bBits), c = f.Decode(cBits);
            if (a.IsNaN || b.IsNaN || c.IsNaN) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            if (operation == FixedScalar.Clamp)
            {
                if (FixedKernel.Compare(bBits, cBits, kind, state) > 0) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                return FixedKernel.Compare(aBits, bBits, kind, state) < 0 ? bBits : FixedKernel.Compare(aBits, cBits, kind, state) > 0 ? cBits : aBits;
            }
            if ((operation == FixedScalar.Lerp || operation == FixedScalar.LerpUnclamped) && c.IsZero) return aBits;
            if ((operation == FixedScalar.Lerp || operation == FixedScalar.LerpUnclamped) && c.IsFinite && !c.Negative && c.Magnitude == f.Scale) return bBits;
            if (!a.IsFinite || !b.IsFinite || !c.IsFinite) { status |= FixedStatus.InvalidDomain; return f.NaN; }
            // Rational helpers operate directly in raw integer units. Converting each operand
            // to binary guard precision first would bias exact decimal midpoint ties.
            FixedQ rx = Raw(a), ry = Raw(b), rz = Raw(c), denominator = new FixedQ(FixedBigUInt.From(f.Scale));
            FixedQ numerator;
            switch (operation)
            {
                case FixedScalar.Lerp:
                case FixedScalar.LerpUnclamped:
                    if (operation == FixedScalar.Lerp) rz = ClampAmount(rz, f.Scale);
                    numerator = Sum(Times(rx, denominator), Times(FixedQ.Subtract(ry, rx), rz), state.Settings.Rounding);
                    return RoundRatio(numerator, denominator.Magnitude, f, state.Settings, ref status);
                case FixedScalar.InverseLerp:
                    FixedQ range = FixedQ.Subtract(ry, rx), offset = FixedQ.Subtract(rz, rx);
                    if (range.IsZero) return 0;
                    if (offset.Negative != range.Negative && !offset.IsZero) return 0;
                    if (FixedBigUInt.Compare(offset.Magnitude, range.Magnitude) >= 0)
                        return FixedKernel.FromInteger(1, false, kind, state, out status);
                    return RoundRatio(new FixedQ(FixedBigUInt.Multiply(offset.Magnitude, denominator.Magnitude)), range.Magnitude, f, state.Settings, ref status);
                case FixedScalar.MoveTowards:
                    numerator = MoveTowards(rx, ry, rz, state.Settings.Rounding);
                    return FixedMathKernel.PackBig(numerator.Magnitude, numerator.Negative, f, state.Settings, ref status);
                case FixedScalar.Repeat:
                case FixedScalar.PingPong:
                    if (ry.Negative || ry.IsZero) { status |= FixedStatus.InvalidDomain; return f.NaN; }
                    if (operation == FixedScalar.Repeat) numerator = RepeatRaw(rx, ry.Magnitude);
                    else
                    {
                        FixedBigUInt period = FixedBigUInt.ShiftLeft(ry.Magnitude, 1);
                        FixedBigUInt rem = FixedBigUInt.Mod(rx.Magnitude, period);
                        numerator = new FixedQ(FixedBigUInt.Compare(rem, ry.Magnitude) > 0 ? FixedBigUInt.Subtract(period, rem) : rem);
                    }
                    return FixedMathKernel.PackBig(numerator.Magnitude, false, f, state.Settings, ref status);
                case FixedScalar.DeltaAngle:
                    numerator = DeltaRaw(rx, ry, f.Scale);
                    return FixedMathKernel.PackBig(numerator.Magnitude, numerator.Negative, f, state.Settings, ref status);
                case FixedScalar.LerpAngle:
                    numerator = Sum(Times(rx, denominator), Times(DeltaRaw(rx, ry, f.Scale), ClampAmount(rz, f.Scale)), state.Settings.Rounding);
                    return RoundRatio(numerator, denominator.Magnitude, f, state.Settings, ref status);
                case FixedScalar.MoveTowardsAngle:
                    numerator = MoveTowards(rx, Sum(rx, DeltaRaw(rx, ry, f.Scale), state.Settings.Rounding), rz, state.Settings.Rounding);
                    return FixedMathKernel.PackBig(numerator.Magnitude, numerator.Negative, f, state.Settings, ref status);
                case FixedScalar.SmoothStep:
                    rz = ClampAmount(rz, f.Scale);
                    FixedQ blend = Times(Times(rz, rz), FixedQ.Subtract(Times(denominator, new FixedQ(FixedBigUInt.From(3))), rz.Shifted(1)));
                    FixedQ cube = Times(Times(denominator, denominator), denominator);
                    numerator = Sum(Times(rx, cube), Times(FixedQ.Subtract(ry, rx), blend), state.Settings.Rounding);
                    return RoundRatio(numerator, cube.Magnitude, f, state.Settings, ref status);
            }
            int p = FixedMathKernel.Precision(state);
            FixedQ x = FixedQ.From(a.Magnitude, a.Negative, f.Scale, p);
            FixedQ result;
            switch (operation)
            {
                case FixedScalar.DegreesToRadians:
                    result = FixedQ.Multiply(x, FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p), p).DividedBy(180); break;
                case FixedScalar.RadiansToDegrees:
                    result = FixedQ.Divide(FixedQ.Multiply(x, FixedQ.Integer(180, p), p), FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p), p); break;
                default: status |= FixedStatus.InvalidDomain; return f.NaN;
            }
            return FixedMathKernel.FromQ(result, f, state.Settings, p, ref status);
        }

        internal static bool Approximately(ulong aBits, ulong bBits, ulong toleranceBits, FixedKind kind, FixedRuntimeState state)
        {
            var f = new FixedFormat(kind, state); FixedDecoded a = f.Decode(aBits), b = f.Decode(bBits), tolerance = f.Decode(toleranceBits);
            if (!state.IsInitialized || a.IsNaN || b.IsNaN || !tolerance.IsFinite || tolerance.Negative) return false;
            if (FixedKernel.Equal(aBits, bBits, kind, state, false)) return true;
            if (!a.IsFinite || !b.IsFinite) return false;
            FixedUInt128 difference = a.Negative == b.Negative ? new FixedUInt128(a.Magnitude > b.Magnitude ? a.Magnitude - b.Magnitude : b.Magnitude - a.Magnitude) :
                FixedUInt128.Add(new FixedUInt128(a.Magnitude), new FixedUInt128(b.Magnitude), out _);
            return difference.Hi == 0 && difference.Lo <= tolerance.Magnitude;
        }
    }
}
