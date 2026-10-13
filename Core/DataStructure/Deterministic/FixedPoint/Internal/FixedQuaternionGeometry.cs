namespace LegendaryTools.FixedPoint
{
    internal static partial class FixedQuaternionKernel
    {
        private static FixedQuaternionQ RawCross(FixedQuaternionQ a, FixedQuaternionQ b, FixedRuntimeState state)
            => new FixedQuaternionQ(
                Sum(Product(a.Y, b.Z, state), Product(a.Z, b.Y, state).Negated, state),
                Sum(Product(a.Z, b.X, state), Product(a.X, b.Z, state).Negated, state),
                Sum(Product(a.X, b.Y, state), Product(a.Y, b.X, state).Negated, state));
        private static FixedQuaternionQ QCross(FixedQuaternionQ a, FixedQuaternionQ b, int p)
            => new FixedQuaternionQ(
                FixedQ.Subtract(FixedQ.Multiply(a.Y, b.Z, p), FixedQ.Multiply(a.Z, b.Y, p)),
                FixedQ.Subtract(FixedQ.Multiply(a.Z, b.X, p), FixedQ.Multiply(a.X, b.Z, p)),
                FixedQ.Subtract(FixedQ.Multiply(a.X, b.Y, p), FixedQ.Multiply(a.Y, b.X, p)));
        private static FixedQ SqrtQ(FixedQ value, int p) => new FixedQ(FixedBigUInt.Sqrt(value.Negative ? default : value.Magnitude, p));
        private static FixedQuaternionQ LeastAxis(FixedQuaternionQ value)
        {
            var one = new FixedQ(FixedBigUInt.From(1));
            if (FixedBigUInt.Compare(value.X.Magnitude, value.Y.Magnitude) <= 0 && FixedBigUInt.Compare(value.X.Magnitude, value.Z.Magnitude) <= 0) return new FixedQuaternionQ(one, default, default);
            if (FixedBigUInt.Compare(value.Y.Magnitude, value.Z.Magnitude) <= 0) return new FixedQuaternionQ(default, one, default);
            return new FixedQuaternionQ(default, default, one);
        }
        internal static FixedQuaternionBits AngleAxis(ulong angle, FixedVectorBits axis, bool degrees, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireVector(axis, state, out status)) return NaN;
            if (!FiniteScalar(angle, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedQuaternionQ raw = Raw(axis, state);
            if (raw.IsZero) { status = FixedStatus.InvalidDomain; return Identity(state); }
            int p = Precision(state, angle); FixedQ radians = Radians(Q(angle, state, p), p, degrees, true);
            FixedMathKernel.SinCos(radians.Shifted(-1), p, out FixedQ sine, out FixedQ cosine);
            FixedQuaternionQ result = QScale(QNormalize(raw, p), sine, p); result.W = cosine;
            return PackQ(QNormalize(result, p), p, state, ref status);
        }
        internal static FixedQuaternionBits Euler(FixedVectorBits angles, bool degrees, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireVector(angles, state, out status)) return NaN;
            int p = Precision(state, angles.X);
            if (Precision(state, angles.Y) > p || Precision(state, angles.Z) > p) p = 256;
            FixedMathKernel.SinCos(Radians(Q(angles.X, state, p), p, degrees, true).Shifted(-1), p, out FixedQ sx, out FixedQ cx);
            FixedMathKernel.SinCos(Radians(Q(angles.Y, state, p), p, degrees, true).Shifted(-1), p, out FixedQ sy, out FixedQ cy);
            FixedMathKernel.SinCos(Radians(Q(angles.Z, state, p), p, degrees, true).Shifted(-1), p, out FixedQ sz, out FixedQ cz);
            // Hamilton product Ry * Rx * Rz, entirely at guard precision.
            var result = new FixedQuaternionQ(
                FixedQ.Add(FixedQ.Multiply(FixedQ.Multiply(sx, cy, p), cz, p), FixedQ.Multiply(FixedQ.Multiply(sy, sz, p), cx, p)),
                FixedQ.Subtract(FixedQ.Multiply(FixedQ.Multiply(sy, cx, p), cz, p), FixedQ.Multiply(FixedQ.Multiply(sx, sz, p), cy, p)),
                FixedQ.Subtract(FixedQ.Multiply(FixedQ.Multiply(sz, cx, p), cy, p), FixedQ.Multiply(FixedQ.Multiply(sx, sy, p), cz, p)),
                FixedQ.Add(FixedQ.Multiply(FixedQ.Multiply(cx, cy, p), cz, p), FixedQ.Multiply(FixedQ.Multiply(sx, sy, p), sz, p)));
            return PackQ(QNormalize(result, p), p, state, ref status);
        }
        private static FixedQuaternionQ FromToQ(FixedQuaternionQ a, FixedQuaternionQ b, FixedRuntimeState state, int p)
        {
            FixedQuaternionQ cross = RawCross(a, b, state);
            FixedQ dot = RawDot(a, b, state);
            if (cross.IsZero)
                return dot.Negative && !dot.IsZero ? QNormalize(RawCross(a, LeastAxis(a), state), p) : new FixedQuaternionQ(default, default, default, FixedQ.One(p));
            FixedBigUInt squared = FixedBigUInt.Multiply(RawDot(a, a, state).Magnitude, RawDot(b, b, state).Magnitude);
            FixedQ root = new FixedQ(FixedBigUInt.Sqrt(squared, p * 2));
            cross = QShift(cross, p); cross.W = FixedQ.Add(root, dot.Shifted(p));
            return QNormalize(cross, p);
        }
        internal static FixedQuaternionBits FromTo(FixedVectorBits from, FixedVectorBits to, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireVector(from, state, out status)) return NaN;
            if (!RequireVector(to, state, out status)) return NaN;
            FixedQuaternionQ a = Raw(from, state), b = Raw(to, state);
            if (a.IsZero || b.IsZero) { status = FixedStatus.InvalidDomain; return Identity(state); }
            int p = Precision(state);
            return PackQ(FromToQ(a, b, state, p), p, state, ref status);
        }
        private static FixedQ Matrix(FixedQuaternionQ x, FixedQuaternionQ y, FixedQuaternionQ z, int row, int column)
            => (column == 0 ? x : column == 1 ? y : z).Get(row);
        private static FixedQuaternionQ FromMatrix(FixedQuaternionQ x, FixedQuaternionQ y, FixedQuaternionQ z, int p)
        {
            FixedQ one = FixedQ.One(p), trace = FixedQ.Add(FixedQ.Add(x.X, y.Y), z.Z);
            if (!trace.Negative && !trace.IsZero)
            {
                FixedQ root = SqrtQ(FixedQ.Add(one, trace), p), divisor = root.Shifted(1);
                return new FixedQuaternionQ(FixedQ.Divide(FixedQ.Subtract(y.Z, z.Y), divisor, p), FixedQ.Divide(FixedQ.Subtract(z.X, x.Z), divisor, p), FixedQ.Divide(FixedQ.Subtract(x.Y, y.X), divisor, p), root.Shifted(-1));
            }
            int i = FixedQ.Compare(x.X, y.Y) >= 0 && FixedQ.Compare(x.X, z.Z) >= 0 ? 0 : FixedQ.Compare(y.Y, z.Z) >= 0 ? 1 : 2;
            int j = (i + 1) % 3, k = (i + 2) % 3;
            FixedQ r = SqrtQ(FixedQ.Subtract(FixedQ.Add(one, Matrix(x, y, z, i, i)), FixedQ.Add(Matrix(x, y, z, j, j), Matrix(x, y, z, k, k))), p);
            FixedQ d = r.Shifted(1); FixedQuaternionQ result = default;
            result.Set(i, r.Shifted(-1));
            result.Set(j, FixedQ.Divide(FixedQ.Add(Matrix(x, y, z, i, j), Matrix(x, y, z, j, i)), d, p));
            result.Set(k, FixedQ.Divide(FixedQ.Add(Matrix(x, y, z, i, k), Matrix(x, y, z, k, i)), d, p));
            result.W = FixedQ.Divide(FixedQ.Subtract(Matrix(x, y, z, k, j), Matrix(x, y, z, j, k)), d, p);
            return result;
        }
        internal static FixedQuaternionBits LookRotation(FixedVectorBits forward, FixedVectorBits upwards, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireVector(forward, state, out status)) return NaN;
            if (!RequireVector(upwards, state, out status)) return NaN;
            FixedQuaternionQ f = Raw(forward, state), u = Raw(upwards, state);
            if (f.IsZero) { status = FixedStatus.InvalidDomain; return Identity(state); }
            int p = Precision(state); FixedQuaternionQ cross = RawCross(u, f, state);
            if (cross.IsZero) return PackQ(FromToQ(new FixedQuaternionQ(default, default, new FixedQ(FixedBigUInt.From(1))), f, state, p), p, state, ref status);
            FixedQuaternionQ z = QNormalize(f, p), x = QNormalize(cross, p), y = QNormalize(QCross(z, x, p), p);
            return PackQ(QNormalize(FromMatrix(x, y, z, p), p), p, state, ref status);
        }
        private static FixedQ WrapPositive(FixedQ angle, FixedQ period)
        {
            FixedBigUInt remainder = FixedBigUInt.Mod(angle.Magnitude, period.Magnitude);
            return new FixedQ(angle.Negative && !remainder.IsZero ? FixedBigUInt.Subtract(period.Magnitude, remainder) : remainder);
        }
        internal static FixedVectorBits EulerAngles(FixedQuaternionBits value, bool degrees, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Require(value, state, out status)) return FixedVectorKernel.NaN;
            FixedQuaternionQ raw = Raw(value, state);
            if (raw.IsZero) { status = FixedStatus.InvalidDomain; return FixedVectorKernel.NaN; }
            int p = Precision(state); FixedQuaternionQ q = QNormalize(raw, p);
            FixedQ xx = FixedQ.Multiply(q.X, q.X, p), yy = FixedQ.Multiply(q.Y, q.Y, p), zz = FixedQ.Multiply(q.Z, q.Z, p), ww = FixedQ.Multiply(q.W, q.W, p);
            FixedQ xy = FixedQ.Multiply(q.X, q.Y, p).Shifted(1), yz = FixedQ.Multiply(q.Y, q.Z, p).Shifted(1), zx = FixedQ.Multiply(q.Z, q.X, p).Shifted(1);
            FixedQ xw = FixedQ.Multiply(q.X, q.W, p).Shifted(1), yw = FixedQ.Multiply(q.Y, q.W, p).Shifted(1), zw = FixedQ.Multiply(q.Z, q.W, p).Shifted(1);
            FixedQ sin = FixedQ.Subtract(yz, xw), one = FixedQ.One(p);
            if (FixedBigUInt.Compare(sin.Magnitude, one.Magnitude) > 0) sin.Magnitude = one.Magnitude;
            FixedQ complement = FixedQ.Subtract(one, FixedQ.Multiply(sin, sin, p));
            FixedQ ex = FixedMathKernel.Atan2(sin.Negated, SqrtQ(complement, p), p), ey, ez;
            if (complement.Magnitude.BitLength <= 4)
            {
                ey = default;
                ez = FixedMathKernel.Atan2(FixedQ.Subtract(zw, xy), FixedQ.Subtract(FixedQ.Add(ww, xx), FixedQ.Add(yy, zz)), p);
            }
            else
            {
                ey = FixedMathKernel.Atan2(FixedQ.Add(zx, yw), FixedQ.Subtract(FixedQ.Add(zz, ww), FixedQ.Add(xx, yy)), p);
                ez = FixedMathKernel.Atan2(FixedQ.Add(xy, zw), FixedQ.Subtract(FixedQ.Add(yy, ww), FixedQ.Add(xx, zz)), p);
            }
            FixedQ period = degrees ? FixedQ.Integer(360, p) : Pi(p).Shifted(1);
            var f = new FixedFormat(Kind, state); FixedVectorBits result = default;
            result.X = FixedMathKernel.FromQ(WrapPositive(degrees ? Degrees(ex, p) : ex, period), f, state.Settings, p, ref status);
            result.Y = FixedMathKernel.FromQ(WrapPositive(degrees ? Degrees(ey, p) : ey, period), f, state.Settings, p, ref status);
            result.Z = FixedMathKernel.FromQ(WrapPositive(degrees ? Degrees(ez, p) : ez, period), f, state.Settings, p, ref status);
            // Rounding just below one revolution must still return an angle in the documented interval.
            FixedQ rawPeriod = new FixedQ(FixedBigUInt.Multiply(degrees ? FixedBigUInt.From(360) : period.Magnitude, FixedBigUInt.From(state.Scale64), degrees ? 0 : p));
            for (int i = 0; i < 3; i++) if (f.Decode(result.Get(i)).IsFinite && Raw(result.Get(i), state).Magnitude.BitLength <= 64 && FixedBigUInt.Compare(Raw(result.Get(i), state).Magnitude, rawPeriod.Magnitude) >= 0) result.Set(i, 0);
            return result;
        }
        internal static ulong ToAngleAxis(FixedQuaternionBits value, bool degrees, FixedRuntimeState state, out FixedVectorBits axis, out FixedStatus status)
        {
            axis = FixedVectorKernel.NaN;
            if (!Require(value, state, out status)) return NaN.X;
            FixedQuaternionQ raw = Raw(value, state);
            if (raw.IsZero) { status = FixedStatus.InvalidDomain; return NaN.X; }
            int p = Precision(state); FixedQuaternionQ q = QNormalize(raw, p);
            FixedQ first = !q.W.IsZero ? q.W : !q.X.IsZero ? q.X : !q.Y.IsZero ? q.Y : q.Z;
            if (first.Negative) q = QScale(q, FixedQ.One(p).Negated, p);
            FixedQ w = q.W; q.W = default;
            FixedQ length = SqrtQ(QDot(q, q, p), p);
            FixedQ angle = FixedMathKernel.Atan2(length, w, p).Shifted(1);
            q = q.IsZero ? new FixedQuaternionQ(FixedQ.One(p), default, default) : QNormalize(q, p);
            var f = new FixedFormat(Kind, state);
            axis = new FixedVectorBits(FixedMathKernel.FromQ(q.X, f, state.Settings, p, ref status), FixedMathKernel.FromQ(q.Y, f, state.Settings, p, ref status), FixedMathKernel.FromQ(q.Z, f, state.Settings, p, ref status));
            return FixedMathKernel.FromQ(degrees ? Degrees(angle, p) : angle, f, state.Settings, p, ref status);
        }
    }
}
