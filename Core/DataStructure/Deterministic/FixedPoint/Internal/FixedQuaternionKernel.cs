namespace LegendaryTools.FixedPoint
{
    internal struct FixedQuaternionBits
    {
        internal ulong X, Y, Z, W;
        internal FixedQuaternionBits(ulong x, ulong y, ulong z, ulong w) { X = x; Y = y; Z = z; W = w; }
        internal ulong Get(int index) => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : W;
        internal void Set(int index, ulong value) { if (index == 0) X = value; else if (index == 1) Y = value; else if (index == 2) Z = value; else W = value; }
    }

    internal struct FixedQuaternionQ
    {
        internal FixedQ X, Y, Z, W;
        internal FixedQuaternionQ(FixedQ x, FixedQ y, FixedQ z, FixedQ w = default) { X = x; Y = y; Z = z; W = w; }
        internal FixedQ Get(int index) => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : W;
        internal void Set(int index, FixedQ value) { if (index == 0) X = value; else if (index == 1) Y = value; else if (index == 2) Z = value; else W = value; }
        internal bool IsZero => X.IsZero && Y.IsZero && Z.IsZero && W.IsZero;
    }

    /// <summary>Shared integer rotation kernel. Raw products are accumulated before decimal rounding.</summary>
    internal static partial class FixedQuaternionKernel
    {
        private const FixedKind Kind = FixedKind.Signed64;
        internal static FixedQuaternionBits NaN => new FixedQuaternionBits(0x8000000000000000UL, 0x8000000000000000UL, 0x8000000000000000UL, 0x8000000000000000UL);
        internal static FixedQuaternionBits Identity(FixedRuntimeState state) => new FixedQuaternionBits(0, 0, 0, state.Scale64);
        internal static bool Finite(FixedQuaternionBits value, FixedRuntimeState state)
        {
            var f = new FixedFormat(Kind, state);
            for (int i = 0; i < 4; i++) if (!f.Decode(value.Get(i)).IsFinite) return false;
            return true;
        }
        private static bool Require(FixedQuaternionBits value, FixedRuntimeState state, out FixedStatus status)
        {
            status = !state.IsInitialized ? FixedStatus.Uninitialized : !Finite(value, state) ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess();
        }
        private static bool RequireVector(FixedVectorBits value, FixedRuntimeState state, out FixedStatus status)
            => Require(new FixedQuaternionBits(value.X, value.Y, value.Z, 0), state, out status);
        private static FixedQ Raw(ulong bits, FixedRuntimeState state)
        { var v = new FixedFormat(Kind, state).Decode(bits); return new FixedQ(FixedBigUInt.From(v.Magnitude), v.Negative); }
        private static FixedQuaternionQ Raw(FixedQuaternionBits value, FixedRuntimeState state)
            => new FixedQuaternionQ(Raw(value.X, state), Raw(value.Y, state), Raw(value.Z, state), Raw(value.W, state));
        internal static FixedQuaternionQ UnitQ(FixedQuaternionBits value, int precision, FixedRuntimeState state) => QNormalize(Raw(value, state), precision);
        private static FixedQuaternionQ Raw(FixedVectorBits value, FixedRuntimeState state)
            => new FixedQuaternionQ(Raw(value.X, state), Raw(value.Y, state), Raw(value.Z, state));
        private static FixedQ Product(FixedQ a, FixedQ b, FixedRuntimeState state)
            => new FixedQ(a.Magnitude.BitLength <= 64 && b.Magnitude.BitLength <= 64
                ? FixedBigUInt.From(FixedUInt128.Multiply(a.Magnitude.Low, b.Magnitude.Low, state.Settings.ArithmeticMode))
                : FixedBigUInt.Multiply(a.Magnitude, b.Magnitude), a.Negative ^ b.Negative);
        private static FixedQ Sum(FixedQ a, FixedQ b, FixedRuntimeState state)
        {
            FixedQ result = FixedQ.Add(a, b);
            if (result.IsZero && a.Negative != b.Negative) result.Negative = state.Settings.Rounding == FixedRounding.TowardNegativeInfinity;
            return result;
        }
        private static FixedQ RawDot(FixedQuaternionQ a, FixedQuaternionQ b, FixedRuntimeState state)
        {
            FixedQ result = Product(a.X, b.X, state);
            for (int i = 1; i < 4; i++) result = Sum(result, Product(a.Get(i), b.Get(i), state), state);
            return result;
        }
        private static FixedQuaternionQ RawHamilton(FixedQuaternionQ a, FixedQuaternionQ b, FixedRuntimeState state)
            => new FixedQuaternionQ(
                Sum(Sum(Product(a.W, b.X, state), Product(a.X, b.W, state), state), Sum(Product(a.Y, b.Z, state), Product(a.Z, b.Y, state).Negated, state), state),
                Sum(Sum(Product(a.W, b.Y, state), Product(a.Y, b.W, state), state), Sum(Product(a.Z, b.X, state), Product(a.X, b.Z, state).Negated, state), state),
                Sum(Sum(Product(a.W, b.Z, state), Product(a.Z, b.W, state), state), Sum(Product(a.X, b.Y, state), Product(a.Y, b.X, state).Negated, state), state),
                Sum(Sum(Product(a.W, b.W, state), Product(a.X, b.X, state).Negated, state), Sum(Product(a.Y, b.Y, state).Negated, Product(a.Z, b.Z, state).Negated, state), state));
        private static FixedQuaternionBits NormalizeRaw(FixedQuaternionQ value, FixedRuntimeState state, ref FixedStatus status)
        {
            if (value.IsZero) return Identity(state);
            FixedBigUInt sum = RawDot(value, value, state).Magnitude;
            FixedBigUInt scaleSquared = FixedBigUInt.From(FixedUInt128.Multiply(state.Scale64, state.Scale64, state.Settings.ArithmeticMode));
            FixedQuaternionBits result = default;
            for (int i = 0; i < 4; i++)
            {
                FixedQ c = value.Get(i);
                FixedBigUInt numerator = FixedBigUInt.Multiply(FixedBigUInt.Multiply(c.Magnitude, c.Magnitude), scaleSquared);
                result.Set(i, FixedVectorKernel.RoundRootRatio(numerator, sum, c.Negative, state, ref status));
            }
            return CheckRotation(result, state, ref status);
        }
        private static FixedQuaternionBits CheckRotation(FixedQuaternionBits value, FixedRuntimeState state, ref FixedStatus status)
        {
            if (!status.IsSuccess()) return value;
            var f = new FixedFormat(Kind, state);
            for (int i = 0; i < 4; i++) if (!f.Decode(value.Get(i)).IsZero) return value;
            status |= FixedStatus.InvalidDomain;
            return NaN;
        }
        internal static FixedQuaternionBits Normalize(FixedQuaternionBits value, FixedRuntimeState state, out FixedStatus status)
            => Require(value, state, out status) ? NormalizeRaw(Raw(value, state), state, ref status) : NaN;
        internal static FixedQuaternionBits Multiply(FixedQuaternionBits a, FixedQuaternionBits b, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(a, state, out status)) return NaN;
            if (!Finite(b, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedQuaternionQ raw = RawHamilton(Raw(a, state), Raw(b, state), state);
            if (state.Settings.QuaternionNormalization == FixedQuaternionNormalizationMode.NormalizeResult) return NormalizeRaw(raw, state, ref status);
            FixedQuaternionBits result = default;
            for (int i = 0; i < 4; i++) result.Set(i, FixedVectorKernel.RoundRatio(raw.Get(i), FixedBigUInt.From(state.Scale64), state, ref status));
            return result;
        }
        internal static FixedQuaternionBits Inverse(FixedQuaternionBits value, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(value, state, out status)) return NaN;
            FixedQuaternionQ raw = Raw(value, state);
            if (raw.IsZero) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedBigUInt denominator = RawDot(raw, raw, state).Magnitude;
            FixedQ scaleSquared = new FixedQ(FixedBigUInt.From(FixedUInt128.Multiply(state.Scale64, state.Scale64, state.Settings.ArithmeticMode)));
            FixedQuaternionBits result = default;
            for (int i = 0; i < 4; i++) result.Set(i, FixedVectorKernel.RoundRatio(Product(i == 3 ? raw.Get(i) : raw.Get(i).Negated, scaleSquared, state), denominator, state, ref status));
            return result;
        }
        internal static ulong Dot(FixedQuaternionBits a, FixedQuaternionBits b, FixedRuntimeState state, out FixedStatus status)
        {
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return NaN.X;
            if (Finite(a, state) && Finite(b, state)) return FixedVectorKernel.RoundRatio(RawDot(Raw(a, state), Raw(b, state), state), FixedBigUInt.From(state.Scale64), state, ref status);
            ulong sum = 0;
            for (int i = 0; i < 4; i++)
            {
                ulong product = FixedKernel.Binary(a.Get(i), b.Get(i), Kind, state, FixedBinary.Multiply, out FixedStatus p);
                sum = FixedKernel.Binary(sum, product, Kind, state, FixedBinary.Add, out FixedStatus s); status |= p | s;
            }
            return sum;
        }
        internal static FixedVectorBits Rotate(FixedQuaternionBits rotation, FixedVectorBits point, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(rotation, state, out status)) return FixedVectorKernel.NaN;
            if (!RequireVector(point, state, out status)) return FixedVectorKernel.NaN;
            FixedQuaternionQ q = Raw(rotation, state), v = Raw(point, state);
            if (q.IsZero) { status = FixedStatus.InvalidDomain; return FixedVectorKernel.NaN; }
            FixedQ scaleSquared = new FixedQ(FixedBigUInt.From(FixedUInt128.Multiply(state.Scale64, state.Scale64, state.Settings.ArithmeticMode)));
            FixedVectorBits result = default;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3, k = (i + 2) % 3;
                FixedQ diagonal = Sum(scaleSquared, Sum(Product(q.Get(j), q.Get(j), state), Product(q.Get(k), q.Get(k), state), state).Shifted(1).Negated, state);
                FixedQ first = Sum(Product(q.Get(i), q.Get(j), state), Product(q.Get(k), q.W, state).Negated, state).Shifted(1);
                FixedQ second = Sum(Product(q.Get(i), q.Get(k), state), Product(q.Get(j), q.W, state), state).Shifted(1);
                FixedQ numerator = Sum(Sum(Product(diagonal, v.Get(i), state), Product(first, v.Get(j), state), state), Product(second, v.Get(k), state), state);
                result.Set(i, FixedVectorKernel.RoundRatio(numerator, scaleSquared.Magnitude, state, ref status));
            }
            return result;
        }

        private static FixedQuaternionQ QAdd(FixedQuaternionQ a, FixedQuaternionQ b)
            => new FixedQuaternionQ(FixedQ.Add(a.X, b.X), FixedQ.Add(a.Y, b.Y), FixedQ.Add(a.Z, b.Z), FixedQ.Add(a.W, b.W));
        internal static FixedQuaternionQ QScale(FixedQuaternionQ a, FixedQ t, int p)
            => new FixedQuaternionQ(FixedQ.Multiply(a.X, t, p), FixedQ.Multiply(a.Y, t, p), FixedQ.Multiply(a.Z, t, p), FixedQ.Multiply(a.W, t, p));
        private static FixedQuaternionQ QShift(FixedQuaternionQ a, int shift)
            => new FixedQuaternionQ(a.X.Shifted(shift), a.Y.Shifted(shift), a.Z.Shifted(shift), a.W.Shifted(shift));
        private static FixedQ QDot(FixedQuaternionQ a, FixedQuaternionQ b, int p)
        {
            FixedQ sum = FixedQ.Multiply(a.X, b.X, p);
            for (int i = 1; i < 4; i++) sum = FixedQ.Add(sum, FixedQ.Multiply(a.Get(i), b.Get(i), p));
            return sum;
        }
        internal static FixedQuaternionQ QNormalize(FixedQuaternionQ value, int p)
        {
            if (value.IsZero) return new FixedQuaternionQ(default, default, default, FixedQ.One(p));
            int bits = 0;
            for (int i = 0; i < 4; i++) if (value.Get(i).Magnitude.BitLength > bits) bits = value.Get(i).Magnitude.BitLength;
            // Scale the largest component into [1,2). Products and tiny directions retain guard precision.
            value = QShift(value, p + 1 - bits);
            FixedQ length = new FixedQ(FixedBigUInt.Sqrt(QDot(value, value, p).Magnitude, p));
            return new FixedQuaternionQ(FixedQ.Divide(value.X, length, p), FixedQ.Divide(value.Y, length, p), FixedQ.Divide(value.Z, length, p), FixedQ.Divide(value.W, length, p));
        }
        private static int Precision(FixedRuntimeState state, ulong trigArgument = 0)
        {
            int p = FixedMathKernel.Precision(state);
            return p == 128 && new FixedFormat(Kind, state).Decode(trigArgument).Magnitude / state.Scale64 >= (1UL << 20) ? 256 : p;
        }
        private static FixedQ Q(ulong bits, FixedRuntimeState state, int p)
        { var v = new FixedFormat(Kind, state).Decode(bits); return FixedQ.From(v.Magnitude, v.Negative, state.Scale64, p); }
        internal static FixedQuaternionBits PackQ(FixedQuaternionQ value, int p, FixedRuntimeState state, ref FixedStatus status)
        {
            FixedQuaternionBits result = default; var f = new FixedFormat(Kind, state);
            for (int i = 0; i < 4; i++) result.Set(i, FixedMathKernel.FromQ(value.Get(i), f, state.Settings, p, ref status));
            return CheckRotation(result, state, ref status);
        }
        private static FixedQ Pi(int p) => FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p);
        private static FixedQ Radians(FixedQ angle, int p, bool degrees, bool periodic)
        {
            if (!degrees) return angle;
            if (periodic) angle.Magnitude = FixedBigUInt.Mod(angle.Magnitude, FixedQ.Integer(360, p).Magnitude);
            return FixedQ.Multiply(angle.DividedBy(180), Pi(p), p);
        }
        private static FixedQ Degrees(FixedQ angle, int p) => FixedQ.Divide(FixedQ.Multiply(angle, FixedQ.Integer(180, p), p), Pi(p), p);
        private static bool FiniteScalar(ulong bits, FixedRuntimeState state) => new FixedFormat(Kind, state).Decode(bits).IsFinite;
        private static FixedQ HalfAngle(FixedQuaternionQ a, FixedQuaternionQ b, FixedRuntimeState state, int p)
        {
            FixedQ dot = RawDot(a, b, state);
            FixedBigUInt product = FixedBigUInt.Multiply(RawDot(a, a, state).Magnitude, RawDot(b, b, state).Magnitude);
            FixedBigUInt complement = FixedBigUInt.Subtract(product, FixedBigUInt.Multiply(dot.Magnitude, dot.Magnitude));
            return FixedMathKernel.Atan2(new FixedQ(FixedBigUInt.Sqrt(complement, p * 2)), dot.Abs.Shifted(p), p);
        }
        internal static ulong Angle(FixedQuaternionBits a, FixedQuaternionBits b, bool degrees, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(a, state, out status)) return NaN.X;
            if (!Finite(b, state)) { status = FixedStatus.InvalidDomain; return NaN.X; }
            FixedQuaternionQ ra = Raw(a, state), rb = Raw(b, state);
            if (ra.IsZero || rb.IsZero) { status = FixedStatus.InvalidDomain; return NaN.X; }
            int p = Precision(state); FixedQ angle = HalfAngle(ra, rb, state, p).Shifted(1);
            return FixedMathKernel.FromQ(degrees ? Degrees(angle, p) : angle, new FixedFormat(Kind, state), state.Settings, p, ref status);
        }
        internal static FixedQuaternionBits Interpolate(FixedQuaternionBits a, FixedQuaternionBits b, ulong amount, bool clamp, bool spherical, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(a, state, out status)) return NaN;
            if (!Finite(b, state) || !FiniteScalar(amount, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedQuaternionQ ra = Raw(a, state), rb = Raw(b, state);
            if (ra.IsZero || rb.IsZero) { status = FixedStatus.InvalidDomain; return NaN; }
            int p = Precision(state, amount); FixedQ t = Q(amount, state, p);
            if (clamp && t.Negative) t = default;
            if (clamp && FixedQ.Compare(t, FixedQ.One(p)) > 0) t = FixedQ.One(p);
            FixedQuaternionQ qa = QNormalize(ra, p), qb = QNormalize(rb, p);
            if (RawDot(ra, rb, state).Negative) qb = QScale(qb, FixedQ.One(p).Negated, p);
            FixedQuaternionQ result;
            if (!spherical) result = QAdd(QScale(qa, FixedQ.Subtract(FixedQ.One(p), t), p), QScale(qb, t, p));
            else
            {
                FixedQ theta = HalfAngle(ra, rb, state, p);
                if (theta.IsZero) return PackQ(qa, p, state, ref status);
                FixedQuaternionQ tangent = QAdd(qb, QScale(qa, QDot(qa, qb, p).Negated, p));
                tangent = QNormalize(tangent, p);
                FixedMathKernel.SinCos(FixedQ.Multiply(theta, t, p), p, out FixedQ sin, out FixedQ cos);
                result = QAdd(QScale(qa, cos, p), QScale(tangent, sin, p));
            }
            return PackQ(QNormalize(result, p), p, state, ref status);
        }
        internal static FixedQuaternionBits RotateTowards(FixedQuaternionBits a, FixedQuaternionBits b, ulong delta, bool degrees, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(a, state, out status)) return NaN;
            if (!Finite(b, state) || !FiniteScalar(delta, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedQuaternionQ ra = Raw(a, state), rb = Raw(b, state);
            if (ra.IsZero || rb.IsZero) { status = FixedStatus.InvalidDomain; return NaN; }
            int p = Precision(state, delta); FixedQ angle = HalfAngle(ra, rb, state, p).Shifted(1);
            if (angle.IsZero) return Normalize(a, state, out status);
            FixedQ step = Radians(Q(delta, state, p), p, degrees, false);
            if (!step.Negative && FixedQ.Compare(step, angle) > 0) step = angle;
            if (step.Negative && FixedQ.Compare(step.Abs, FixedQ.Subtract(Pi(p), angle)) > 0) step = FixedQ.Subtract(Pi(p), angle).Negated;
            // Keep t at guard precision; a rounded Fixed64 ratio would alter the requested angular step.
            return SlerpQ(ra, rb, FixedQ.Divide(step, angle, p), p, state, ref status);
        }
        private static FixedQuaternionBits SlerpQ(FixedQuaternionQ ra, FixedQuaternionQ rb, FixedQ t, int p, FixedRuntimeState state, ref FixedStatus status)
        {
            FixedQuaternionQ a = QNormalize(ra, p), b = QNormalize(rb, p);
            if (RawDot(ra, rb, state).Negative) b = QScale(b, FixedQ.One(p).Negated, p);
            FixedQ theta = HalfAngle(ra, rb, state, p);
            if (theta.IsZero) return PackQ(a, p, state, ref status);
            FixedQuaternionQ tangent = QNormalize(QAdd(b, QScale(a, QDot(a, b, p).Negated, p)), p);
            FixedMathKernel.SinCos(FixedQ.Multiply(theta, t, p), p, out FixedQ sin, out FixedQ cos);
            return PackQ(QNormalize(QAdd(QScale(a, cos, p), QScale(tangent, sin, p)), p), p, state, ref status);
        }
    }
}
