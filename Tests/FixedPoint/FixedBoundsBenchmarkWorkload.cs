using System;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
#endif

namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
    internal enum BoundsOperation
    {
        LoopControl, Validate, ContainsInside, ContainsMissFirst, ContainsMissLast, IntersectsHit, IntersectsMissLast,
        DirectMin, AllViews, ClosestPoint, Distance, DistanceWide, RayBoolean, RayParameter, RayMissLast, RayTinyOverflow,
        FromMinMax, SetCenter, SelfSetCenter, ExpandEven, ExpandOdd, ExpandRejected, Encapsulate,
        ManyContains, CachedContains, CachedBuildAndContains, NetworkWrite, NetworkWriteSingleStage, NetworkWriteSingleStageByteCopy,
        NetworkRead, NetworkCanonicalize, NetworkShortWrite, NetworkInvalidLastRead, DescriptorWrite, DescriptorRead
    }
    internal struct BoundsBenchmarkSample
    {
        internal FixedBoundsBits Box, Other;
        internal FixedVectorBits Min, Max, Center, Point, Origin, Direction;
    }
    internal unsafe struct BoundsBenchmarkSpec
    {
        internal BoundsOperation Operation;
        internal int Dimension, Queries, Offset;
        internal FixedRuntimeState Numeric;
        internal FixedBoundsRuntimeState Geometry;
        internal FixedBoundsNetworkProfile Profile;
        internal byte* Packet;
        internal int PacketLength;
    }
    internal struct BoundsBenchmarkResult { internal ulong Digest; internal int Status, Managed; internal long Bytes; }
    internal static unsafe class BoundsBenchmarkWorkload
    {
        internal static BoundsBenchmarkSample[] Create(int count, int dimension, FixedRuntimeState numeric, FixedBoundsRuntimeState geometry)
        {
            var samples = new BoundsBenchmarkSample[count];
            for (int i = 0; i < count; i++)
            {
                long c = i % 97 - 48, extent = 4 + i % 13;
                var center = V(c, c + 2, c - 3); var e = V(extent, extent + 2, extent + 4);
                FixedBoundsKernel.FromCenterExtents(center, e, dimension, numeric, geometry, out var box, out _);
                var min = FixedBoundsKernel.Get(box, FixedBoundsPart.Min, dimension, numeric, geometry, out _);
                var max = FixedBoundsKernel.Get(box, FixedBoundsPart.Max, dimension, numeric, geometry, out _);
                var otherCenter = center; otherCenter.Set(dimension - 1, unchecked((ulong)(c + extent * 4 + 100)));
                FixedBoundsKernel.FromCenterExtents(otherCenter, e, dimension, numeric, geometry, out var other, out _);
                samples[i] = new BoundsBenchmarkSample { Box = box, Other = other, Min = min, Max = max, Center = center,
                    Point = V(c + extent + 1, c + extent + 7, c + extent + 11), Origin = V(c - extent - 37, c + 2, c - 3), Direction = V(17) };
            }
            return samples;
        }
        internal static FixedVectorBits V(long x, long y = 0, long z = 0) => new FixedVectorBits(unchecked((ulong)x), unchecked((ulong)y), unchecked((ulong)z));
        internal static long Integer(ulong bits) => bits == 0x7FFFFFFFFFFFFFFEUL ? 0 : unchecked((long)bits);
        private static ulong Box(FixedBoundsBits box) => box.First.X ^ box.First.Y ^ box.First.Z ^ box.Second.X ^ box.Second.Y ^ box.Second.Z;
        internal static bool SingleStage(ref FixedBufferWriter writer, FixedBoundsBits value, in BoundsBenchmarkSpec spec, bool byteCopy, out FixedStatus status)
        {
            // Benchmark-only orchestration prototype. It keeps canonicalization/geometry checks and stages the entire box before publishing.
            var candidate = FixedBoundsNetworkKernel.Canonicalize(value, spec.Dimension, spec.Profile, spec.Numeric, spec.Geometry, out status);
            if (!status.IsSuccess()) return false;
            if (spec.Profile.First.X.Codec != FixedCodec.BitPacked && (writer.BitPosition & 7) != 0) { status |= FixedStatus.InvalidEncoding; return false; }
            byte* bytes = stackalloc byte[60]; for (int i = 0; i < 60; i++) bytes[i] = 0;
            var trial = new FixedBufferWriter(bytes, 60);
            for (int field = 0; field < spec.Dimension * 2; field++)
            {
                bool first = field < spec.Dimension; int axis = field % spec.Dimension;
                bool ok = FixedNetworkKernel.Write(ref trial, first ? candidate.First.Get(axis) : candidate.Second.Get(axis),
                    first ? spec.Profile.First.Get(axis) : spec.Profile.Second.Get(axis), spec.Numeric, out var s); status |= s; if (!ok) return false;
            }
            if (!writer.Has(trial.BitPosition)) { status |= FixedStatus.BufferTooSmall; return false; }
            if (byteCopy && (writer.BitPosition & 7) == 0 && (trial.BitPosition & 7) == 0)
            {
                int start = (int)(writer.BitPosition / 8); for (int i = 0; i < trial.BytesWritten; i++) writer.Buffer[start + i] = bytes[i]; writer.BitPosition += trial.BitPosition;
            }
            else
            {
                var reader = new FixedBufferReader(bytes, trial.BytesWritten); long remaining = trial.BitPosition;
                while (remaining > 0) { int bits = remaining > 64 ? 64 : (int)remaining; writer.WriteBits(reader.ReadBits(bits), bits); remaining -= bits; }
            }
            return true;
        }
        private static ulong Evaluate(in BoundsBenchmarkSample sample, in BoundsBenchmarkSpec spec, byte* packet, int packetLength, ref long bytes, out FixedStatus status)
        {
            int d = spec.Dimension; var n = spec.Numeric; var g = spec.Geometry; status = FixedStatus.None;
            FixedVectorBits vector; FixedBoundsBits changed; bool hit, ok; ulong raw;
            switch (spec.Operation)
            {
                case BoundsOperation.LoopControl: return sample.Box.First.X;
                case BoundsOperation.Validate: return FixedBoundsKernel.Validate(sample.Box, d, n, g, out status) ? 1UL : 0UL;
                case BoundsOperation.ContainsInside: FixedBoundsKernel.Contains(sample.Box, sample.Center, d, n, g, out hit, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.ContainsMissFirst: vector = sample.Center; vector.X = sample.Point.X; FixedBoundsKernel.Contains(sample.Box, vector, d, n, g, out hit, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.ContainsMissLast: vector = sample.Center; vector.Set(d - 1, sample.Point.Get(d - 1)); FixedBoundsKernel.Contains(sample.Box, vector, d, n, g, out hit, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.IntersectsHit: FixedBoundsKernel.Intersects(sample.Box, sample.Box, d, n, g, out hit, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.IntersectsMissLast: FixedBoundsKernel.Intersects(sample.Box, sample.Other, d, n, g, out hit, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.DirectMin: return FixedBoundsKernel.Get(sample.Box, FixedBoundsPart.Min, d, n, g, out status).X;
                case BoundsOperation.AllViews:
                    raw = 0; for (int part = 0; part < 5; part++) { var v = FixedBoundsKernel.Get(sample.Box, (FixedBoundsPart)part, d, n, g, out var s); status |= s; raw += v.X; } return raw;
                case BoundsOperation.ClosestPoint: return FixedBoundsKernel.ClosestPoint(sample.Box, sample.Point, d, n, g, out status).X;
                case BoundsOperation.Distance: return FixedBoundsKernel.SqrDistance(sample.Box, sample.Point, d, n, g, out status);
                case BoundsOperation.DistanceWide: return FixedBoundsKernel.SqrDistance(sample.Box, V(long.MinValue + 2, long.MaxValue - 2, long.MinValue + 2), d, n, g, out status);
                case BoundsOperation.RayBoolean: FixedBoundsKernel.IntersectRay(sample.Box, sample.Origin, sample.Direction, d, n, g, false, out hit, out _, out status); return hit ? 1UL : 0UL;
                case BoundsOperation.RayParameter: FixedBoundsKernel.IntersectRay(sample.Box, sample.Origin, sample.Direction, d, n, g, true, out hit, out raw, out status); return raw ^ (hit ? 1UL : 0UL);
                case BoundsOperation.RayMissLast:
                    vector = sample.Origin; vector.Set(d - 1, sample.Point.Get(d - 1)); FixedBoundsKernel.IntersectRay(sample.Box, vector, sample.Direction, d, n, g, true, out hit, out raw, out status); return raw ^ (hit ? 1UL : 0UL);
                case BoundsOperation.RayTinyOverflow: FixedBoundsKernel.IntersectRay(sample.Box, V(long.MinValue + 2, Integer(sample.Center.Y), Integer(sample.Center.Z)), V(1), d, n, g, true, out hit, out raw, out status); return raw ^ (hit ? 1UL : 0UL);
                case BoundsOperation.FromMinMax: FixedBoundsKernel.FromMinMax(sample.Min, sample.Max, d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.SetCenter: FixedBoundsKernel.Set(sample.Box, FixedBoundsPart.Center, sample.Point, d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.SelfSetCenter: FixedBoundsKernel.Set(sample.Box, FixedBoundsPart.Center, sample.Center, d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.ExpandEven: FixedBoundsKernel.Expand(sample.Box, V(2, 2, 2), d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.ExpandOdd: FixedBoundsKernel.Expand(sample.Box, V(3, 3, 3), d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.ExpandRejected: vector = V(3, 3, 3); vector.Set(d - 1, unchecked((ulong)-1000L)); FixedBoundsKernel.Expand(sample.Box, vector, d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.Encapsulate: FixedBoundsKernel.Encapsulate(sample.Box, sample.Point, sample.Point, d, n, g, out changed, out status); return Box(changed);
                case BoundsOperation.ManyContains:
                    raw = 0; for (int query = 0; query < spec.Queries; query++)
                    { FixedBoundsKernel.Contains(sample.Box, (query & 1) == 0 ? sample.Center : sample.Point, d, n, g, out hit, out var s); status |= s; raw += hit ? 1UL : 0UL; }
                    return raw;
                case BoundsOperation.CachedContains:
                case BoundsOperation.CachedBuildAndContains:
                    var lo = spec.Operation == BoundsOperation.CachedContains ? sample.Min : FixedBoundsKernel.Get(sample.Box, FixedBoundsPart.Min, d, n, g, out status);
                    var hi = spec.Operation == BoundsOperation.CachedContains ? sample.Max : FixedBoundsKernel.Get(sample.Box, FixedBoundsPart.Max, d, n, g, out status);
                    raw = 0; for (int query = 0; query < spec.Queries; query++)
                    {
                        vector = (query & 1) == 0 ? sample.Center : sample.Point; hit = true;
                        for (int axis = 0; axis < d; axis++) { long p = Integer(vector.Get(axis)); if (p < Integer(lo.Get(axis)) || p > Integer(hi.Get(axis))) { hit = false; break; } }
                        raw += hit ? 1UL : 0UL;
                    }
                    return raw;
                default:
                    var writer = new FixedBufferWriter(packet, spec.Operation == BoundsOperation.NetworkShortWrite ? 0 : 300, spec.Offset);
                    if (spec.Operation == BoundsOperation.NetworkWrite || spec.Operation == BoundsOperation.NetworkShortWrite) ok = FixedBoundsNetworkKernel.Write(ref writer, sample.Box, d, spec.Profile, n, g, out status);
                    else if (spec.Operation == BoundsOperation.NetworkWriteSingleStage || spec.Operation == BoundsOperation.NetworkWriteSingleStageByteCopy) ok = SingleStage(ref writer, sample.Box, spec, spec.Operation == BoundsOperation.NetworkWriteSingleStageByteCopy, out status);
                    else if (spec.Operation == BoundsOperation.NetworkCanonicalize) { changed = FixedBoundsNetworkKernel.Canonicalize(sample.Box, d, spec.Profile, n, g, out status); return Box(changed); }
                    else if (spec.Operation == BoundsOperation.DescriptorWrite) ok = FixedBoundsNetworkKernel.WriteDescriptor(ref writer, spec.Profile, out status);
                    else
                    {
                        var reader = new FixedBufferReader(packet, packetLength, spec.Offset);
                        if (spec.Operation == BoundsOperation.DescriptorRead) { ok = FixedBoundsNetworkKernel.ReadDescriptor(ref reader, out var profile, out status); bytes += reader.BitPosition - spec.Offset; return ok ? (ulong)profile.DescriptorBytes : 0; }
                        ok = FixedBoundsNetworkKernel.Read(ref reader, d, spec.Profile, n, g, out changed, out status); bytes += reader.BitPosition - spec.Offset; return Box(changed) ^ (ok ? 1UL : 0UL);
                    }
                    bytes += writer.BitPosition - spec.Offset;
                    return (ulong)writer.BitPosition ^ (ok ? (ulong)packet[0] | (ulong)packet[writer.BytesWritten - 1] << 8 : 0UL);
            }
        }
        internal static void Run(BoundsBenchmarkSample* samples, int count, int repeats, BoundsBenchmarkSpec* spec, BoundsBenchmarkResult* result)
        {
            *result = default; result->Digest = 0xCBF29CE484222325UL;
            byte* packet = stackalloc byte[300]; for (int i = 0; i < 300; i++) packet[i] = 0xA5;
            int packetLength = spec->PacketLength;
            if (spec->Operation == BoundsOperation.NetworkRead || spec->Operation == BoundsOperation.NetworkInvalidLastRead || spec->Operation == BoundsOperation.DescriptorRead)
            {
                packet = spec->Packet;
            }
            for (int repeat = 0; repeat < repeats; repeat++) for (int i = 0; i < count; i++)
            {
                ulong value = Evaluate(samples[i], *spec, packet, packetLength, ref result->Bytes, out var status);
                result->Status |= (int)status; result->Digest = unchecked((result->Digest ^ value ^ (uint)status) * 1099511628211UL);
            }
            MarkManaged(ref result->Managed);
        }
#if !FIXEDPOINT_STANDALONE
        [BurstDiscard]
#endif
        private static void MarkManaged(ref int managed) => managed = 1;
    }
}
