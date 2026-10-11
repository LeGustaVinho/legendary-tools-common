namespace LegendaryTools.FixedPoint
{
    internal static partial class FixedVectorKernel
    {
        private static FixedQ Q(ulong bits, FixedRuntimeState state, int p)
        { FixedDecoded v = new FixedFormat(Kind, state).Decode(bits); return FixedQ.From(v.Magnitude, v.Negative, state.Scale64, p); }
        private static FixedVectorQ Q(FixedVectorBits v, FixedRuntimeState state, int p) => new FixedVectorQ(Q(v.X, state, p), Q(v.Y, state, p), Q(v.Z, state, p));
        private static FixedVectorQ QAdd(FixedVectorQ a, FixedVectorQ b) => new FixedVectorQ(FixedQ.Add(a.X, b.X), FixedQ.Add(a.Y, b.Y), FixedQ.Add(a.Z, b.Z));
        private static FixedVectorQ QSubtract(FixedVectorQ a, FixedVectorQ b) => new FixedVectorQ(FixedQ.Subtract(a.X, b.X), FixedQ.Subtract(a.Y, b.Y), FixedQ.Subtract(a.Z, b.Z));
        private static FixedVectorQ QMultiply(FixedVectorQ a, FixedQ b, int p) => new FixedVectorQ(FixedQ.Multiply(a.X, b, p), FixedQ.Multiply(a.Y, b, p), FixedQ.Multiply(a.Z, b, p));
        private static FixedQ QDot(FixedVectorQ a, FixedVectorQ b, int p) => FixedQ.Add(FixedQ.Add(FixedQ.Multiply(a.X, b.X, p), FixedQ.Multiply(a.Y, b.Y, p)), FixedQ.Multiply(a.Z, b.Z, p));
        private static FixedVectorQ QCross(FixedVectorQ a, FixedVectorQ b, int p) => new FixedVectorQ(
            FixedQ.Subtract(FixedQ.Multiply(a.Y, b.Z, p), FixedQ.Multiply(a.Z, b.Y, p)),
            FixedQ.Subtract(FixedQ.Multiply(a.Z, b.X, p), FixedQ.Multiply(a.X, b.Z, p)),
            FixedQ.Subtract(FixedQ.Multiply(a.X, b.Y, p), FixedQ.Multiply(a.Y, b.X, p)));
        private static FixedQ QLength(FixedVectorQ a, int p) => new FixedQ(FixedBigUInt.Sqrt(QDot(a, a, p).Magnitude, p));
        private static FixedVectorQ QNormalize(FixedVectorQ a, int p)
        {
            FixedQ length = QLength(a, p);
            return length.IsZero ? default : new FixedVectorQ(FixedQ.Divide(a.X, length, p), FixedQ.Divide(a.Y, length, p), FixedQ.Divide(a.Z, length, p));
        }
        private static FixedQ RawLengthQ(FixedVectorQ raw, int dimensions, FixedRuntimeState state, int p) =>
            new FixedQ(FixedBigUInt.Sqrt(Squares(raw, dimensions, state), p * 2)).DividedBy(state.Scale64);
        private static FixedVectorQ RawDirectionQ(FixedVectorQ raw, int dimensions, FixedRuntimeState state, int p)
        {
            FixedBigUInt root = FixedBigUInt.Sqrt(Squares(raw, dimensions, state), p * 2);
            if (root.IsZero) return default;
            // Virtual shifts in Divide/Sqrt keep full raw products, even for almost collinear extremes.
            return new FixedVectorQ(
                new FixedQ(FixedBigUInt.Divide(raw.X.Magnitude, root, p * 2), raw.X.Negative),
                new FixedQ(FixedBigUInt.Divide(raw.Y.Magnitude, root, p * 2), raw.Y.Negative),
                new FixedQ(FixedBigUInt.Divide(raw.Z.Magnitude, root, p * 2), raw.Z.Negative));
        }
        private static FixedVectorBits PackQ(FixedVectorQ q, int dimensions, int p, FixedRuntimeState state, ref FixedStatus status)
        {
            FixedVectorBits result = default; var f = new FixedFormat(Kind, state);
            for (int i = 0; i < dimensions; i++) result.Set(i, FixedMathKernel.FromQ(q.Get(i), f, state.Settings, p, ref status));
            return result;
        }
        private static FixedVectorQ LeastAxis(FixedVectorQ direction)
        {
            FixedQ one = new FixedQ(FixedBigUInt.From(1));
            if (FixedBigUInt.Compare(direction.X.Magnitude, direction.Y.Magnitude) <= 0 &&
                FixedBigUInt.Compare(direction.X.Magnitude, direction.Z.Magnitude) <= 0) return new FixedVectorQ(one, default);
            if (FixedBigUInt.Compare(direction.Y.Magnitude, direction.Z.Magnitude) <= 0) return new FixedVectorQ(default, one);
            return new FixedVectorQ(default, default, one);
        }
        private static FixedVectorQ PerpendicularRaw(FixedVectorQ direction, FixedRuntimeState state) => RawCross(direction, LeastAxis(direction), state);
        private static FixedQ AngleQ(FixedVectorQ a, FixedVectorQ b, int dimensions, FixedRuntimeState state, int p)
        {
            if (a.IsZero || b.IsZero) return default;
            FixedVectorQ cross = RawCross(a, b, state);
            FixedQ y = new FixedQ(FixedBigUInt.Sqrt(Squares(cross, 3, state), p * 2));
            FixedQ x = RawDot(a, b, dimensions, state).Shifted(p);
            return FixedMathKernel.Atan2(y, x, p);
        }
        internal static ulong Angle(FixedVectorBits a, FixedVectorBits b, FixedVectorBits axis, int dimensions, bool signed,
            FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, dimensions, state, out status)) return NaN.X;
            if (!Finite(b, dimensions, state) || signed && dimensions == 3 && !Finite(axis, 3, state))
            { status = FixedStatus.InvalidDomain; return NaN.X; }
            int p = FixedMathKernel.Precision(state); FixedVectorQ ra = Raw(a, state), rb = Raw(b, state);
            FixedQ radians = AngleQ(ra, rb, dimensions, state, p);
            FixedQ degrees = FixedQ.Divide(FixedQ.Multiply(radians, FixedQ.Integer(180, p), p), FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p), p);
            if (signed)
            {
                FixedVectorQ cross = RawCross(ra, rb, state);
                FixedQ sign = dimensions == 2 ? cross.Z : RawDot(Raw(axis, state), cross, 3, state);
                degrees.Negative = sign.Negative && !sign.IsZero;
            }
            return FixedMathKernel.FromQ(degrees, new FixedFormat(Kind, state), state.Settings, p, ref status);
        }
        private static FixedVectorQ DirectionBlend(FixedVectorQ rawA, FixedVectorQ rawB, FixedQ radians, FixedRuntimeState state, int p)
        {
            FixedVectorQ direction = RawDirectionQ(rawA, 3, state, p);
            if (radians.IsZero) return direction;
            FixedVectorQ cross = RawCross(rawA, rawB, state);
            FixedVectorQ tangent = cross.IsZero ? PerpendicularRaw(rawA, state) : RawCross(cross, rawA, state);
            tangent = RawDirectionQ(tangent, 3, state, p);
            FixedMathKernel.SinCos(radians, p, out FixedQ sin, out FixedQ cos);
            return QAdd(QMultiply(direction, cos, p), QMultiply(tangent, sin, p));
        }
        internal static FixedVectorBits Slerp(FixedVectorBits a, FixedVectorBits b, ulong amount, bool clamp,
            FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, 3, state, out status)) return NaN;
            var f = new FixedFormat(Kind, state);
            if (!Finite(b, 3, state) || !f.Decode(amount).IsFinite) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedQ rawT = Raw(amount, state);
            if (rawT.IsZero || clamp && rawT.Negative) return a;
            if (!rawT.Negative && (rawT.Magnitude.Low == state.Scale64 || clamp && rawT.Magnitude.Low > state.Scale64)) return b;
            FixedVectorQ ra = Raw(a, state), rb = Raw(b, state);
            if (ra.IsZero || rb.IsZero) return Lerp(a, b, amount, 3, clamp, state, out status);
            int p = FixedMathKernel.Precision(state); FixedQ t = Q(amount, state, p);
            FixedQ radians = FixedQ.Multiply(AngleQ(ra, rb, 3, state, p), t, p);
            FixedVectorQ direction = DirectionBlend(ra, rb, radians, state, p);
            FixedQ ma = RawLengthQ(ra, 3, state, p), mb = RawLengthQ(rb, 3, state, p);
            FixedQ length = FixedQ.Add(ma, FixedQ.Multiply(FixedQ.Subtract(mb, ma), t, p));
            return PackQ(QMultiply(direction, length, p), 3, p, state, ref status);
        }
        internal static FixedVectorBits MoveTowards(FixedVectorBits a, FixedVectorBits b, ulong delta, int dimensions,
            FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, dimensions, state, out status)) return NaN;
            if (!Finite(b, dimensions, state) || !new FixedFormat(Kind, state).Decode(delta).IsFinite) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ difference = SubtractRaw(Raw(b, state), Raw(a, state), state.Settings.Rounding);
            FixedBigUInt sum = Squares(difference, dimensions, state); FixedQ rawDelta = Raw(delta, state);
            if (sum.IsZero || !rawDelta.Negative && FixedBigUInt.Compare(sum, Product(rawDelta, rawDelta, state).Magnitude) <= 0) return b;
            if (rawDelta.IsZero) return a;
            int p = FixedMathKernel.Precision(state);
            return PackQ(QAdd(Q(a, state, p), QMultiply(RawDirectionQ(difference, dimensions, state, p), Q(delta, state, p), p)), dimensions, p, state, ref status);
        }
        private static FixedQ MoveQ(FixedQ from, FixedQ to, FixedQ delta)
        {
            FixedQ difference = FixedQ.Subtract(to, from);
            if (difference.IsZero || !delta.Negative && FixedBigUInt.Compare(difference.Magnitude, delta.Magnitude) <= 0) return to;
            return FixedQ.Add(from, difference.Negative ? delta.Negated : delta);
        }
        internal static FixedVectorBits RotateTowards(FixedVectorBits a, FixedVectorBits b, ulong radiansDelta, ulong magnitudeDelta,
            FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, 3, state, out status)) return NaN;
            var f = new FixedFormat(Kind, state); FixedDecoded angleDelta = f.Decode(radiansDelta), lengthDelta = f.Decode(magnitudeDelta);
            if (!Finite(b, 3, state) || !angleDelta.IsFinite || !lengthDelta.IsFinite || lengthDelta.Negative && !lengthDelta.IsZero)
            { status = FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ ra = Raw(a, state), rb = Raw(b, state);
            if (ra.IsZero || rb.IsZero) return MoveTowards(a, b, magnitudeDelta, 3, state, out status);
            int p = FixedMathKernel.Precision(state); FixedQ angle = AngleQ(ra, rb, 3, state, p), step = Q(radiansDelta, state, p);
            if (!step.Negative && FixedQ.Compare(step, angle) > 0) step = angle;
            if (step.Negative)
            {
                FixedQ remaining = FixedQ.Subtract(FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p), angle);
                if (FixedBigUInt.Compare(step.Magnitude, remaining.Magnitude) > 0) step = remaining.Negated;
            }
            FixedQ length = MoveQ(RawLengthQ(ra, 3, state, p), RawLengthQ(rb, 3, state, p), Q(magnitudeDelta, state, p));
            if (step.IsZero && FixedQ.Compare(length, RawLengthQ(ra, 3, state, p)) == 0) return a;
            return PackQ(QMultiply(DirectionBlend(ra, rb, step, state, p), length, p), 3, p, state, ref status);
        }
        internal static bool OrthoNormalize(FixedVectorBits normal, FixedVectorBits tangent, FixedVectorBits binormal, bool third,
            FixedRuntimeState state, out FixedVectorBits n, out FixedVectorBits t, out FixedVectorBits b, out FixedStatus status)
        {
            n = t = b = NaN;
            if (!RequireFinite(normal, 3, state, out status)) return false;
            if (!Finite(tangent, 3, state) || third && !Finite(binormal, 3, state)) { status = FixedStatus.InvalidDomain; return false; }
            FixedVectorQ rn = Raw(normal, state), rt = Raw(tangent, state);
            if (rn.IsZero) rn = new FixedVectorQ(new FixedQ(FixedBigUInt.From(1)), default);
            FixedVectorQ cross = RawCross(rn, rt, state);
            rt = cross.IsZero ? PerpendicularRaw(rn, state) : RawCross(cross, rn, state);
            if (cross.IsZero) cross = RawCross(rn, rt, state);
            int p = FixedMathKernel.Precision(state); FixedVectorQ qn = RawDirectionQ(rn, 3, state, p), qt = RawDirectionQ(rt, 3, state, p);
            n = PackQ(qn, 3, p, state, ref status); t = PackQ(qt, 3, p, state, ref status);
            if (third)
            {
                FixedVectorQ qb = QNormalize(QCross(qn, qt, p), p);
                FixedQ orientation = RawDot(Raw(binormal, state), cross, 3, state);
                if (orientation.Negative && !orientation.IsZero) qb = QMultiply(qb, FixedQ.One(p).Negated, p);
                b = PackQ(qb, 3, p, state, ref status);
            }
            return status.IsSuccess();
        }
        internal static FixedVectorBits SmoothDamp(FixedVectorBits current, FixedVectorBits target, FixedVectorBits velocity,
            ulong smoothTime, ulong maxSpeed, ulong deltaTime, int dimensions, FixedRuntimeState state,
            out FixedVectorBits nextVelocity, out FixedStatus status)
        {
            nextVelocity = velocity;
            if (!RequireFinite(current, dimensions, state, out status)) return NaN;
            var f = new FixedFormat(Kind, state); FixedDecoded st = f.Decode(smoothTime), ms = f.Decode(maxSpeed), dt = f.Decode(deltaTime);
            if (!Finite(target, dimensions, state) || !Finite(velocity, dimensions, state) || !st.IsFinite || !dt.IsFinite ||
                dt.Negative && !dt.IsZero || ms.IsNaN || ms.Negative && !ms.IsZero)
            { status = FixedStatus.InvalidDomain; return NaN; }
            if (dt.IsZero) return current;
            int p = FixedMathKernel.Precision(state); FixedQ one = FixedQ.One(p), time = Q(smoothTime, state, p), delta = Q(deltaTime, state, p);
            FixedQ minimum = new FixedQ(FixedBigUInt.Divide(FixedBigUInt.PowerOfTwo(p), 10000, out _));
            FixedQ epsilon = FixedQ.From(1, false, state.Scale64, p);
            if (FixedQ.Compare(minimum, epsilon) < 0) minimum = epsilon;
            if (FixedQ.Compare(time, minimum) < 0) time = minimum;
            FixedQ omega = FixedQ.Divide(FixedQ.Integer(2, p), time, p), x = FixedQ.Multiply(omega, delta, p);
            FixedQ c48 = FixedQ.Integer(12, p).DividedBy(25), c235 = FixedQ.Integer(47, p).DividedBy(200);
            // Horner evaluation limits intermediate size and retains exact rational coefficients at guard precision.
            FixedQ denominator = FixedQ.Add(one, FixedQ.Multiply(x, FixedQ.Add(one,
                FixedQ.Multiply(x, FixedQ.Add(c48, FixedQ.Multiply(c235, x, p)), p)), p));
            FixedQ decay = FixedQ.Divide(one, denominator, p);
            FixedVectorQ qc = Q(current, state, p), qtarget = Q(target, state, p), qvelocity = Q(velocity, state, p);
            FixedVectorQ change = QSubtract(qc, qtarget);
            if (ms.IsFinite)
            {
                FixedQ maximum = FixedQ.Multiply(Q(maxSpeed, state, p), time, p);
                FixedQ length = QLength(change, p);
                if (FixedQ.Compare(length, maximum) > 0 && !length.IsZero)
                    change = QMultiply(change, FixedQ.Divide(maximum, length, p), p);
            }
            FixedVectorQ effectiveTarget = QSubtract(qc, change);
            FixedVectorQ temp = QMultiply(QAdd(qvelocity, QMultiply(change, omega, p)), delta, p);
            FixedVectorQ updatedVelocity = QMultiply(QSubtract(qvelocity, QMultiply(temp, omega, p)), decay, p);
            FixedVectorQ output = QAdd(effectiveTarget, QMultiply(QAdd(change, temp), decay, p));
            FixedQ overshoot = QDot(QSubtract(qtarget, qc), QSubtract(output, qtarget), p);
            if (!overshoot.Negative && !overshoot.IsZero) { output = qtarget; updatedVelocity = default; }
            FixedVectorBits result = PackQ(output, dimensions, p, state, ref status);
            FixedVectorBits candidateVelocity = PackQ(updatedVelocity, dimensions, p, state, ref status);
            if (status.IsSuccess()) nextVelocity = candidateVelocity;
            return result;
        }
    }
}
