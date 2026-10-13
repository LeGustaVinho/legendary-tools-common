using System;
using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    public struct SpatialBounds2Access : ISpatialBoundsAccess<FixedBounds2>
    {
        public int Dimensions => 2;
        public DeterministicStatus TryRead(in FixedBounds2 bounds, out SpatialRawBounds envelope)
            => SpatialGeometry.Read(bounds.Bits, 2, out envelope);
    }
    public struct SpatialBounds3Access : ISpatialBoundsAccess<FixedBounds3>
    {
        public int Dimensions => 3;
        public DeterministicStatus TryRead(in FixedBounds3 bounds, out SpatialRawBounds envelope)
            => SpatialGeometry.Read(bounds.Bits, 3, out envelope);
    }
    /// <summary>Exact squared raw distance (three limbs). Conversion to Fixed64 happens only after selection.</summary>
    public readonly struct SpatialDistanceKey : IComparable<SpatialDistanceKey>
    {
        public readonly ulong Low, Middle, High;
        internal SpatialDistanceKey(ulong low, ulong middle, ulong high) { Low = low; Middle = middle; High = high; }
        internal static SpatialDistanceKey From(FixedBigUInt value) => new SpatialDistanceKey(value.Low,
            value.Get(2) | (ulong)value.Get(3) << 32, value.Get(4) | (ulong)value.Get(5) << 32);
        internal FixedBigUInt Wide
        {
            get
            {
                var value = FixedBigUInt.From(Low); value.Set(2, (uint)Middle); value.Set(3, (uint)(Middle >> 32));
                value.Set(4, (uint)High); value.Set(5, (uint)(High >> 32)); return value;
            }
        }
        public int CompareTo(SpatialDistanceKey other) => High != other.High ? High.CompareTo(other.High) :
            Middle != other.Middle ? Middle.CompareTo(other.Middle) : Low.CompareTo(other.Low);
        public bool TryGetSquaredDistance(out Fixed64 distance, out FixedStatus status)
        {
            distance = Fixed64.NaN; status = FixedStatus.Uninitialized;
            if (!FixedRuntime.IsInitialized) return false;
            var state = FixedRuntime.State; status = FixedStatus.None;
            distance = Fixed64.FromBits(FixedVectorKernel.RoundRatio(new FixedQ(Wide), FixedBigUInt.From(state.Scale64), state, ref status));
            return status.IsSuccess();
        }
    }
    /// <summary>Nonnegative exact ray parameter; default is invalid, zero is 0/1.</summary>
    public readonly struct SpatialRayParameter : IComparable<SpatialRayParameter>
    {
        public readonly ulong Numerator, Denominator;
        internal SpatialRayParameter(ulong numerator, ulong denominator) { Numerator = numerator; Denominator = denominator; }
        public bool IsValid => Denominator != 0;
        public int CompareTo(SpatialRayParameter other)
        {
            var a = FixedUInt128.Multiply(Numerator, other.Denominator, FixedArithmeticMode.Portable);
            var b = FixedUInt128.Multiply(other.Numerator, Denominator, FixedArithmeticMode.Portable);
            return FixedUInt128.Compare(a, b);
        }
        public bool TryGetParameter(out Fixed64 parameter, out FixedStatus status)
        {
            parameter = Fixed64.NaN; status = !FixedRuntime.IsInitialized ? FixedStatus.Uninitialized :
                !IsValid ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            var state = FixedRuntime.State;
            var n = FixedBigUInt.From(FixedUInt128.Multiply(Numerator, state.Scale64, state.Settings.ArithmeticMode));
            parameter = Fixed64.FromBits(FixedVectorKernel.RoundRatio(new FixedQ(n), FixedBigUInt.From(Denominator), state, ref status));
            return status.IsSuccess();
        }
    }
    /// <summary>Typed factories avoid introducing another public Bounds or Ray representation.</summary>
    public readonly struct SpatialQuery
    {
        public readonly SpatialQueryKind Kind;
        public readonly int Dimensions, K;
        internal readonly SpatialRawBounds Bounds;
        internal readonly FixedVector3 Point, DirectionOrEnd;
        internal readonly Fixed64 Radius;
        private SpatialQuery(SpatialQueryKind kind, int dimensions, SpatialRawBounds bounds,
            FixedVector3 point, FixedVector3 directionOrEnd, Fixed64 radius, int k)
        { Kind = kind; Dimensions = dimensions; Bounds = bounds; Point = point; DirectionOrEnd = directionOrEnd; Radius = radius; K = k; }
        public static SpatialQuery ForPoint(in FixedVector2 point) => new SpatialQuery(SpatialQueryKind.Point, 2, default, point, default, default, 0);
        public static SpatialQuery ForPoint(in FixedVector3 point) => new SpatialQuery(SpatialQueryKind.Point, 3, default, point, default, default, 0);
        internal static SpatialQuery FromEnvelope(in SpatialRawBounds bounds, int dimensions) => new SpatialQuery(SpatialQueryKind.Aabb, dimensions, bounds, default, default, default, 0);
        public static DeterministicStatus TryAabb(in FixedBounds2 bounds, out SpatialQuery query)
        { var s = default(SpatialBounds2Access).TryRead(bounds, out var raw); query = s.IsSuccess() ? new SpatialQuery(SpatialQueryKind.Aabb, 2, raw, default, default, default, 0) : default; return s; }
        public static DeterministicStatus TryAabb(in FixedBounds3 bounds, out SpatialQuery query)
        { var s = default(SpatialBounds3Access).TryRead(bounds, out var raw); query = s.IsSuccess() ? new SpatialQuery(SpatialQueryKind.Aabb, 3, raw, default, default, default, 0) : default; return s; }
        public static SpatialQuery ForCircle(in FixedVector2 center, Fixed64 radius) => new SpatialQuery(SpatialQueryKind.Sphere, 2, default, center, default, radius, 0);
        public static SpatialQuery ForSphere(in FixedVector3 center, Fixed64 radius) => new SpatialQuery(SpatialQueryKind.Sphere, 3, default, center, default, radius, 0);
        public static SpatialQuery ForRay(in FixedRay2 ray) => new SpatialQuery(SpatialQueryKind.Ray, 2, default, ray.origin, ray.direction, default, 0);
        public static SpatialQuery ForRay(in FixedRay3 ray) => new SpatialQuery(SpatialQueryKind.Ray, 3, default, ray.origin, ray.direction, default, 0);
        public static SpatialQuery ForSegment(in FixedVector2 start, in FixedVector2 end) => new SpatialQuery(SpatialQueryKind.Segment, 2, default, start, end, default, 0);
        public static SpatialQuery ForSegment(in FixedVector3 start, in FixedVector3 end) => new SpatialQuery(SpatialQueryKind.Segment, 3, default, start, end, default, 0);
        public static SpatialQuery ForNearest(in FixedVector2 point, int k = 1) => new SpatialQuery(SpatialQueryKind.Nearest, 2, default, point, default, default, k);
        public static SpatialQuery ForNearest(in FixedVector3 point, int k = 1) => new SpatialQuery(SpatialQueryKind.Nearest, 3, default, point, default, default, k);
    }
    internal struct SpatialPreparedQuery
    {
        internal SpatialQueryKind Kind;
        internal int Dimensions, K;
        internal SpatialRawBounds Envelope;
        internal FixedVectorBits Origin;
        internal FixedQ Dx, Dy, Dz;
        internal SpatialDistanceKey RadiusSquared;
        internal bool HasEnvelope;
    }
    internal static class SpatialGeometry
    {
        internal static long Normalize(ulong bits, FixedRuntimeState numeric)
        { var v = new FixedFormat(FixedKind.Signed64, numeric).Decode(bits); return v.Negative && v.Magnitude != 0 ? unchecked(-(long)v.Magnitude) : (long)v.Magnitude; }
        internal static FixedVectorBits MinBits(in SpatialRawBounds box) => new FixedVectorBits(unchecked((ulong)box.MinX), unchecked((ulong)box.MinY), unchecked((ulong)box.MinZ));
        internal static FixedVectorBits MaxBits(in SpatialRawBounds box) => new FixedVectorBits(unchecked((ulong)box.MaxX), unchecked((ulong)box.MaxY), unchecked((ulong)box.MaxZ));
        internal static DeterministicStatus Read(FixedBoundsBits bounds, int dimensions, out SpatialRawBounds envelope)
        {
            envelope = default; var numeric = FixedRuntime.State; var geometry = FixedBoundsRuntime.State;
            if (!numeric.IsInitialized || !geometry.IsInitialized) return DeterministicStatus.InvalidConfiguration;
            if (!FixedBoundsKernel.Validate(bounds, dimensions, numeric, geometry, out _)) return DeterministicStatus.OutOfRange;
            FixedBoundsKernel.Corners(bounds, dimensions, numeric, geometry, out var lo, out var hi);
            for (int i = 0; i < dimensions; i++) envelope.Set(i, Normalize(lo.Get(i), numeric), Normalize(hi.Get(i), numeric));
            return DeterministicStatus.None;
        }
        internal static ulong Width(long lo, long hi) => unchecked((ulong)hi - (ulong)lo);
        internal static long Midpoint(long lo, long hi) => unchecked(lo + (long)(Width(lo, hi) >> 1));
        internal static SpatialRawBounds Expand(in SpatialRawBounds box, long margin, int dimensions)
        {
            var result = box; var f = new FixedFormat(FixedKind.Signed64, FixedRuntime.State);
            long min = unchecked(-(long)f.MaxNegative), max = (long)f.MaxPositive;
            for (int i = 0; i < dimensions; i++)
            {
                long lo = box.Min(i), hi = box.Max(i);
                result.Set(i, Width(min, lo) < (ulong)margin ? min : lo - margin,
                    Width(hi, max) < (ulong)margin ? max : hi + margin);
            }
            return result;
        }
        internal static SpatialDistanceKey Cost(in SpatialRawBounds box, int dimensions)
        {
            ulong x = Width(box.MinX, box.MaxX), y = Width(box.MinY, box.MaxY), z = Width(box.MinZ, box.MaxZ);
            FixedBigUInt n;
            if (dimensions == 2) n = FixedBigUInt.Add(FixedBigUInt.From(x), FixedBigUInt.From(y));
            else
            {
                n = FixedBigUInt.From(FixedUInt128.Multiply(x, y, FixedArithmeticMode.Portable));
                n = FixedBigUInt.Add(n, FixedBigUInt.From(FixedUInt128.Multiply(x, z, FixedArithmeticMode.Portable)));
                n = FixedBigUInt.Add(n, FixedBigUInt.From(FixedUInt128.Multiply(y, z, FixedArithmeticMode.Portable)));
            }
            return SpatialDistanceKey.From(FixedBigUInt.ShiftLeft(n, 1));
        }
        internal static SpatialDistanceKey Distance(in SpatialRawBounds box, FixedVectorBits point, int dimensions)
        {
            var numeric = FixedRuntime.State; FixedBigUInt sum = default;
            for (int i = 0; i < dimensions; i++)
            {
                long p = Normalize(point.Get(i), numeric), lo = box.Min(i), hi = box.Max(i);
                ulong delta = p < lo ? Width(p, lo) : p > hi ? Width(hi, p) : 0;
                sum = FixedBigUInt.Add(sum, FixedBigUInt.From(FixedUInt128.Multiply(delta, delta, numeric.Settings.ArithmeticMode)));
            }
            return SpatialDistanceKey.From(sum);
        }
        internal static DeterministicStatus Prepare(in SpatialQuery query, int dimensions, out SpatialPreparedQuery prepared)
        {
            prepared = default;
            if (!FixedRuntime.IsInitialized || !FixedBoundsRuntime.IsInitialized) return DeterministicStatus.InvalidConfiguration;
            if (query.Dimensions != dimensions || query.Kind > SpatialQueryKind.Nearest || query.K < 0) return DeterministicStatus.OutOfRange;
            prepared.Kind = query.Kind; prepared.Dimensions = dimensions; prepared.K = query.K;
            if (query.Kind == SpatialQueryKind.Aabb) { prepared.Envelope = query.Bounds; prepared.HasEnvelope = true; return DeterministicStatus.None; }
            if (!FixedVector3.IsFinite(query.Point)) return DeterministicStatus.OutOfRange;
            var numeric = FixedRuntime.State; prepared.Origin = query.Point.Bits;
            for (int i = 0; i < dimensions; i++) { long p = Normalize(prepared.Origin.Get(i), numeric); prepared.Envelope.Set(i, p, p); }
            if (query.Kind == SpatialQueryKind.Point) { prepared.HasEnvelope = true; return DeterministicStatus.None; }
            if (query.Kind == SpatialQueryKind.Nearest) return DeterministicStatus.None;
            if (query.Kind == SpatialQueryKind.Sphere)
            {
                if (!Fixed64.IsFinite(query.Radius) || query.Radius < Fixed64.Zero) return DeterministicStatus.OutOfRange;
                long r = Normalize(unchecked((ulong)query.Radius.RawValue), numeric);
                prepared.Envelope = Expand(prepared.Envelope, r, dimensions); prepared.HasEnvelope = true;
                prepared.RadiusSquared = SpatialDistanceKey.From(FixedBigUInt.From(FixedUInt128.Multiply((ulong)r, (ulong)r, numeric.Settings.ArithmeticMode)));
                return DeterministicStatus.None;
            }
            if (!FixedVector3.IsFinite(query.DirectionOrEnd)) return DeterministicStatus.OutOfRange;
            var other = query.DirectionOrEnd.Bits;
            var dx = FixedBoundsKernel.Raw(other.X, numeric); var dy = FixedBoundsKernel.Raw(other.Y, numeric); var dz = FixedBoundsKernel.Raw(other.Z, numeric);
            if (query.Kind == SpatialQueryKind.Segment)
            {
                dx = FixedQ.Subtract(dx, FixedBoundsKernel.Raw(prepared.Origin.X, numeric));
                dy = FixedQ.Subtract(dy, FixedBoundsKernel.Raw(prepared.Origin.Y, numeric));
                dz = FixedQ.Subtract(dz, FixedBoundsKernel.Raw(prepared.Origin.Z, numeric));
                for (int i = 0; i < dimensions; i++)
                { long a = prepared.Envelope.Min(i), b = Normalize(other.Get(i), numeric); prepared.Envelope.Set(i, Math.Min(a, b), Math.Max(a, b)); }
                prepared.HasEnvelope = true;
            }
            prepared.Dx = dx; prepared.Dy = dy; prepared.Dz = dz; return DeterministicStatus.None;
        }
        internal static bool Match(in SpatialRawBounds bounds, in SpatialPreparedQuery query,
            out SpatialDistanceKey distance, out SpatialRayParameter parameter)
        {
            distance = default; parameter = default;
            if (query.HasEnvelope && !bounds.Intersects(query.Envelope, query.Dimensions)) return false;
            if (query.Kind == SpatialQueryKind.Point || query.Kind == SpatialQueryKind.Aabb) return true;
            if (query.Kind == SpatialQueryKind.Sphere || query.Kind == SpatialQueryKind.Nearest)
            { distance = Distance(bounds, query.Origin, query.Dimensions); return query.Kind == SpatialQueryKind.Nearest || distance.CompareTo(query.RadiusSquared) <= 0; }
            if (!FixedBoundsKernel.IntersectCorners(MinBits(bounds), MaxBits(bounds), query.Origin, query.Dx, query.Dy, query.Dz,
                query.Dimensions, FixedRuntime.State, out var enter, out _, out _)) return false;
            if (query.Kind == SpatialQueryKind.Segment && FixedQ.Compare(enter.Numerator, new FixedQ(FixedBigUInt.From(enter.Denominator))) > 0) return false;
            parameter = new SpatialRayParameter(enter.Numerator.Magnitude.Low, enter.Denominator); return true;
        }
        internal static long Cell(long coordinate, long size)
        { long q = coordinate / size, r = coordinate % size; return r < 0 ? q - 1 : q; }
    }
}
