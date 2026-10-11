namespace LegendaryTools.FixedPoint
{
    internal static class FixedRandomGeometry
    {
        private static bool Finite(ulong raw, FixedRuntimeState state) => new FixedFormat(FixedKind.Signed64, state).Decode(raw).IsFinite;
        private static bool Bounds(ulong center, ulong radius, FixedRuntimeState state, out long lo, out long hi)
        {
            var f = new FixedFormat(FixedKind.Signed64, state); var c = f.Decode(center); var r = f.Decode(radius);
            lo = hi = 0;
            if (!c.IsFinite || !r.IsFinite || r.Negative && !r.IsZero) return false;
            long n = c.IsZero ? 0 : unchecked((long)center), size = (long)r.Magnitude;
            if (n > (long)f.MaxPositive - size || n < -(long)f.MaxNegative + size) return false;
            lo = n - size; hi = n + size; return true;
        }
        private static ulong Magnitude(long n) => n < 0 ? unchecked(0UL - (ulong)n) : (ulong)n;
        private static FixedUInt128 Square(long n, FixedRuntimeState state) => FixedUInt128.Multiply(Magnitude(n), Magnitude(n), state.Settings.ArithmeticMode);
        private static bool DrawQ(ref FixedRandom random, int precision, out FixedQ value, out FixedStatus status)
        {
            value = default;
            if (!random.TryNextUInt64(out ulong bits, out status)) return false;
            value = new FixedQ(FixedBigUInt.ShiftLeft(FixedBigUInt.From(bits), precision - 64)); return true;
        }
        private static FixedQ SqrtQ(FixedQ value, int precision) => new FixedQ(FixedBigUInt.Sqrt(value.Magnitude, precision));
        private static FixedQ RawQ(ulong raw, FixedRuntimeState state, int precision)
        { var f = new FixedFormat(FixedKind.Signed64, state); var d = f.Decode(raw); return FixedQ.From(d.Magnitude, d.Negative && !d.IsZero, f.Scale, precision); }
        private static Fixed64 Pack(FixedQ value, FixedRuntimeState state, int precision, ref FixedStatus status)
        {
            if (value.IsZero) value.Negative = false;
            var f = new FixedFormat(FixedKind.Signed64, state);
            ulong raw = FixedMathKernel.FromQ(value, f, state.Settings, precision, ref status);
            return Fixed64.FromBits(f.Decode(raw).IsZero ? 0 : raw);
        }
        internal static bool Inside(ref FixedRandom random, ulong cx, ulong cy, ulong cz, ulong radius, bool sphere, FixedRuntimeState state,
            out FixedPointVector3 point, out FixedStatus status)
        {
            point = default;
            status = !state.IsInitialized ? FixedStatus.Uninitialized : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            var f = new FixedFormat(FixedKind.Signed64, state); var r = f.Decode(radius);
            if (!r.IsFinite || r.Negative && !r.IsZero || !Finite(cx, state) || !Finite(cy, state) || sphere && !Finite(cz, state))
            { status = FixedStatus.InvalidDomain; return false; }
            if (!Bounds(cx, radius, state, out _, out _) || !Bounds(cy, radius, state, out _, out _) || sphere && !Bounds(cz, radius, state, out _, out _))
            { status = FixedStatus.OutOfRange; return false; }
            if (r.IsZero) { point = new FixedPointVector3(PackZero(cx, f), PackZero(cy, f), sphere ? PackZero(cz, f) : Fixed64.Zero); return true; }
            long size = (long)r.Magnitude;
            var limit = FixedUInt128.Multiply(r.Magnitude, r.Magnitude, state.Settings.ArithmeticMode);
            FixedRandom trial = random;
            while (true)
            {
                if (!trial.TryRangeInclusive(-size, size, out long x, out status) || !trial.TryRangeInclusive(-size, size, out long y, out status)) return false;
                long z = 0;
                if (sphere && !trial.TryRangeInclusive(-size, size, out z, out status)) return false;
                var sum = FixedUInt128.Add(Square(x, state), Square(y, state), out _);
                if (sphere) sum = FixedUInt128.Add(sum, Square(z, state), out _);
                if (FixedUInt128.Compare(sum, limit) > 0) continue;
                long centerX = f.Decode(cx).IsZero ? 0 : unchecked((long)cx), centerY = f.Decode(cy).IsZero ? 0 : unchecked((long)cy);
                long centerZ = f.Decode(cz).IsZero ? 0 : unchecked((long)cz);
                point = new FixedPointVector3(Fixed64.FromRaw(centerX + x), Fixed64.FromRaw(centerY + y), sphere ? Fixed64.FromRaw(centerZ + z) : Fixed64.Zero);
                random = trial; return true;
            }
        }
        private static Fixed64 PackZero(ulong raw, FixedFormat f) => Fixed64.FromBits(f.Decode(raw).IsZero ? 0 : raw);

        internal static bool On(ref FixedRandom random, ulong cx, ulong cy, ulong cz, ulong radius, bool sphere, FixedRuntimeState state,
            out FixedPointVector3 point, out FixedStatus status)
        {
            point = default; status = !state.IsInitialized ? FixedStatus.Uninitialized : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            var f = new FixedFormat(FixedKind.Signed64, state); var r = f.Decode(radius);
            if (!r.IsFinite || r.Negative && !r.IsZero || !Finite(cx, state) || !Finite(cy, state) || sphere && !Finite(cz, state))
            { status = FixedStatus.InvalidDomain; return false; }
            if (!Bounds(cx, radius, state, out _, out _) || !Bounds(cy, radius, state, out _, out _) || sphere && !Bounds(cz, radius, state, out _, out _))
            { status = FixedStatus.OutOfRange; return false; }
            if (r.IsZero) { point = new FixedPointVector3(PackZero(cx, f), PackZero(cy, f), sphere ? PackZero(cz, f) : Fixed64.Zero); return true; }
            int p = FixedMathKernel.Precision(state); FixedRandom trial = random;
            if (!DrawQ(ref trial, p, out FixedQ u, out status)) return false;
            FixedQ tau = FixedMathConstants.AtPrecision(FixedMathConstants.Pi, p).Shifted(1);
            FixedMathKernel.SinCos(FixedQ.Multiply(tau, u, p), p, out FixedQ sin, out FixedQ cos);
            FixedQ z = default, radial = FixedQ.One(p);
            if (sphere)
            {
                if (!DrawQ(ref trial, p, out FixedQ v, out status)) return false;
                z = FixedQ.Subtract(FixedQ.One(p), v.Shifted(1));
                radial = SqrtQ(FixedQ.Subtract(FixedQ.One(p), FixedQ.Multiply(z, z, p)), p);
            }
            FixedQ radiusQ = RawQ(radius, state, p);
            FixedQ radialRadius = FixedQ.Multiply(radial, radiusQ, p);
            var x = FixedQ.Add(RawQ(cx, state, p), FixedQ.Multiply(cos, radialRadius, p));
            var y = FixedQ.Add(RawQ(cy, state, p), FixedQ.Multiply(sin, radialRadius, p));
            var height = sphere ? FixedQ.Add(RawQ(cz, state, p), FixedQ.Multiply(z, radiusQ, p)) : default;
            point = new FixedPointVector3(Pack(x, state, p, ref status), Pack(y, state, p, ref status), sphere ? Pack(height, state, p, ref status) : Fixed64.Zero);
            if (!status.IsSuccess()) { point = default; return false; }
            random = trial; return true;
        }

        internal static bool Box(ref FixedRandom random, FixedPointVector3 min, FixedPointVector3 max, bool threeDimensions, FixedRuntimeState state,
            out FixedPointVector3 point, out FixedStatus status)
        {
            point = default; FixedRandom trial = random;
            if (!FixedRandomKernel.Range(ref trial, min.X.Bits, max.X.Bits, FixedKind.Signed64, state, false, out ulong x, out status) ||
                !FixedRandomKernel.Range(ref trial, min.Y.Bits, max.Y.Bits, FixedKind.Signed64, state, false, out ulong y, out status)) return false;
            ulong z = 0;
            if (threeDimensions && !FixedRandomKernel.Range(ref trial, min.Z.Bits, max.Z.Bits, FixedKind.Signed64, state, false, out z, out status)) return false;
            point = new FixedPointVector3(Fixed64.FromBits(x), Fixed64.FromBits(y), Fixed64.FromBits(z)); random = trial; return true;
        }

        private static FixedQ IntegerRaw(ulong bits, FixedRuntimeState state)
        { var d = new FixedFormat(FixedKind.Signed64, state).Decode(bits); return new FixedQ(FixedBigUInt.From(d.Magnitude), d.Negative && !d.IsZero); }
        private static FixedQ Cross(FixedQ a, FixedQ b, FixedQ c, FixedQ d) => FixedQ.Subtract(FixedQ.Multiply(a, b, 0), FixedQ.Multiply(c, d, 0));
        private static FixedQ Barycentric(FixedQ a, FixedQ b, FixedQ c, FixedQ wb, FixedQ wc, int p) =>
            FixedQ.Add(a, FixedQ.Add(FixedQ.Multiply(FixedQ.Subtract(b, a), wb, p), FixedQ.Multiply(FixedQ.Subtract(c, a), wc, p)));
        internal static bool Triangle(ref FixedRandom random, FixedPointVector3 a, FixedPointVector3 b, FixedPointVector3 c, FixedRuntimeState state,
            out FixedPointVector3 point, out FixedStatus status)
        {
            point = default; status = !state.IsInitialized ? FixedStatus.Uninitialized : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            if (!Finite(a.X.Bits, state) || !Finite(a.Y.Bits, state) || !Finite(a.Z.Bits, state) ||
                !Finite(b.X.Bits, state) || !Finite(b.Y.Bits, state) || !Finite(b.Z.Bits, state) ||
                !Finite(c.X.Bits, state) || !Finite(c.Y.Bits, state) || !Finite(c.Z.Bits, state))
            { status = FixedStatus.InvalidDomain; return false; }
            FixedQ dx = FixedQ.Subtract(IntegerRaw(b.X.Bits, state), IntegerRaw(a.X.Bits, state)), dy = FixedQ.Subtract(IntegerRaw(b.Y.Bits, state), IntegerRaw(a.Y.Bits, state)), dz = FixedQ.Subtract(IntegerRaw(b.Z.Bits, state), IntegerRaw(a.Z.Bits, state));
            FixedQ ex = FixedQ.Subtract(IntegerRaw(c.X.Bits, state), IntegerRaw(a.X.Bits, state)), ey = FixedQ.Subtract(IntegerRaw(c.Y.Bits, state), IntegerRaw(a.Y.Bits, state)), ez = FixedQ.Subtract(IntegerRaw(c.Z.Bits, state), IntegerRaw(a.Z.Bits, state));
            if (Cross(dy, ez, dz, ey).IsZero && Cross(dz, ex, dx, ez).IsZero && Cross(dx, ey, dy, ex).IsZero)
            { status = FixedStatus.InvalidDomain; return false; }
            int p = FixedMathKernel.Precision(state); FixedRandom trial = random;
            if (!DrawQ(ref trial, p, out FixedQ u, out status) || !DrawQ(ref trial, p, out FixedQ v, out status)) return false;
            FixedQ t = SqrtQ(u, p), wb = FixedQ.Multiply(t, FixedQ.Subtract(FixedQ.One(p), v), p), wc = FixedQ.Multiply(t, v, p);
            point = new FixedPointVector3(
                Pack(Barycentric(RawQ(a.X.Bits, state, p), RawQ(b.X.Bits, state, p), RawQ(c.X.Bits, state, p), wb, wc, p), state, p, ref status),
                Pack(Barycentric(RawQ(a.Y.Bits, state, p), RawQ(b.Y.Bits, state, p), RawQ(c.Y.Bits, state, p), wb, wc, p), state, p, ref status),
                Pack(Barycentric(RawQ(a.Z.Bits, state, p), RawQ(b.Z.Bits, state, p), RawQ(c.Z.Bits, state, p), wb, wc, p), state, p, ref status));
            if (!status.IsSuccess()) { point = default; return false; }
            random = trial; return true;
        }
    }
}
