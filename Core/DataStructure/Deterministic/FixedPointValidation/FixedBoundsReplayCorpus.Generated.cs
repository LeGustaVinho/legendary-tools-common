namespace LegendaryTools.FixedPoint
{
    // Shared with players by Tools/FixedPoint/sync_bounds_replay_corpus.py.
    internal static unsafe class FixedBoundsReplayCorpus
    {
        internal const int Version = 1;
        internal const int Count = 5760; // 2 dimensions * 2 representations * 4 precisions * 5 roundings * 4 overflow modes * 3 arithmetic modes * 6 cases.
        internal const ulong ExpectedChecksum = 14973702704214226896UL;
        internal struct Result { internal ulong Hash; internal int Managed; }
        internal static ulong Mix(ulong hash, ulong value)
        { for (int i = 0; i < 8; i++) { hash = unchecked((hash ^ (byte)value) * 1099511628211UL); value >>= 8; } return hash; }
        private static ulong Vector(ulong hash, FixedVectorBits v, int dimensions)
        { for (int i = 0; i < dimensions; i++) hash = Mix(hash, v.Get(i)); return hash; }
        private static ulong Box(ulong hash, FixedBoundsBits box, int dimensions) => Vector(Vector(hash, box.First, dimensions), box.Second, dimensions);
        private static FixedVectorBits V(long x, long y, long z) => new FixedVectorBits(unchecked((ulong)x), unchecked((ulong)y), unchecked((ulong)z));
        internal static Result Evaluate(int index)
        {
            int originalIndex = index; int variant = index % 6; index /= 6;
            var arithmetic = (FixedArithmeticMode)(index % 3); index /= 3;
            var overflow = (FixedOverflow)(index % 4); index /= 4;
            var rounding = (FixedRounding)(index % 5); index /= 5;
            int precisionIndex = index % 4; index /= 4; int precision = precisionIndex == 0 ? 0 : precisionIndex == 1 ? 3 : precisionIndex == 2 ? 9 : 18;
            var rep = (FixedBoundsRepresentation)(index % 2); index /= 2; int dimensions = 2 + index % 2;
            var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding, overflow, arithmeticMode: arithmetic));
            var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(rep));
            long hi = long.MaxValue - 2;
            var min = variant == 2 ? V(hi - 64, -21, -7) : variant == 3 ? V(long.MinValue + 2, 0, 0) : V(-13, -21, -7);
            var max = variant == 2 ? V(hi, 63, 19) : variant == 3 ? V(hi, 0, 0) : variant == 1 ? V(-12, -20, -6) : V(37, 63, 19);
            if (variant == 4) min.X = 0x8000000000000000UL;
            if (variant == 5) { min = new FixedVectorBits(0x7FFFFFFFFFFFFFFEUL, 0, 0x7FFFFFFFFFFFFFFEUL); max = default; }
            ulong hash = Mix(14695981039346656037UL, (ulong)originalIndex);
            hash = Mix(hash, numeric.Settings.NumericFingerprint); hash = Mix(hash, geometry.Settings.Fingerprint);
            bool ok = FixedBoundsKernel.FromMinMax(min, max, dimensions, numeric, geometry, out var box, out var status);
            hash = Mix(Box(hash, box, dimensions), (uint)status); hash = Mix(hash, ok ? 1UL : 0UL);
            for (int part = 0; part < 5; part++)
            { var v = FixedBoundsKernel.Get(box, (FixedBoundsPart)part, dimensions, numeric, geometry, out status); hash = Mix(Vector(hash, v, dimensions), (uint)status); }
            var point = V(47, -49, 3);
            ok = FixedBoundsKernel.Contains(box, point, dimensions, numeric, geometry, out bool contains, out status); hash = Mix(Mix(hash, ok && contains ? 1UL : 0UL), (uint)status);
            var closest = FixedBoundsKernel.ClosestPoint(box, point, dimensions, numeric, geometry, out status); hash = Mix(Vector(hash, closest, dimensions), (uint)status);
            var distance = FixedBoundsKernel.SqrDistance(box, point, dimensions, numeric, geometry, out status); hash = Mix(Mix(hash, distance), (uint)status);
            ok = FixedBoundsKernel.IntersectRay(box, V(-123, 2, 3), V(3, 0, 0), dimensions, numeric, geometry, true, out bool hit, out var parameter, out status);
            hash = Mix(Mix(Mix(hash, ok && hit ? 1UL : 0UL), parameter), (uint)status);
            ok = FixedBoundsKernel.Intersects(box, box, dimensions, numeric, geometry, out hit, out status); hash = Mix(Mix(hash, ok && hit ? 1UL : 0UL), (uint)status);
            // Atomic mutations include odd half-raw sizes and operations that overflow at the finite boundary.
            ok = FixedBoundsKernel.Expand(box, V(3, -1, 2), dimensions, numeric, geometry, out var changed, out status); hash = Mix(Mix(Box(hash, changed, dimensions), ok ? 1UL : 0UL), (uint)status);
            ok = FixedBoundsKernel.Encapsulate(box, point, point, dimensions, numeric, geometry, out changed, out status); hash = Mix(Mix(Box(hash, changed, dimensions), ok ? 1UL : 0UL), (uint)status);
            for (int part = 0; part < 5; part++)
            { ok = FixedBoundsKernel.Set(box, (FixedBoundsPart)part, V(2, 3, 4), dimensions, numeric, geometry, out changed, out status); hash = Mix(Mix(Box(hash, changed, dimensions), ok ? 1UL : 0UL), (uint)status); }
            byte* bytes = stackalloc byte[60];
            for (int codec = 0; codec < 3; codec++)
            {
                for (int b = 0; b < 60; b++) bytes[b] = 0xA5;
                var axis = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, (FixedCodec)codec,
                    lowerRaw: codec == 2 ? unchecked((ulong)-1000L) : 0, upperRaw: codec == 2 ? 1000UL : 0);
                var fields = new FixedVectorNetworkProfile(axis, dimensions); var profile = new FixedBoundsNetworkProfile(rep, fields, fields);
                var writer = new FixedBufferWriter(bytes, 60, codec == 2 ? 3 : 0);
                ok = FixedBoundsNetworkKernel.Write(ref writer, box, dimensions, profile, numeric, geometry, out status);
                hash = Mix(Mix(Mix(hash, ok ? 1UL : 0UL), (uint)status), (ulong)writer.BitPosition);
                for (int b = 0; b < writer.BytesWritten; b++) hash = Mix(hash, bytes[b]);
                if (ok)
                {
                    var reader = new FixedBufferReader(bytes, writer.BytesWritten, codec == 2 ? 3 : 0);
                    bool read = FixedBoundsNetworkKernel.Read(ref reader, dimensions, profile, numeric, geometry, out var restored, out status);
                    hash = Mix(Mix(Mix(Box(hash, restored, dimensions), read ? 1UL : 0UL), (uint)status), (ulong)reader.BitPosition);
                }
            }
            return new Result { Hash = hash, Managed = 0 };
        }
        internal static ulong Checksum()
        { ulong hash = 14695981039346656037UL; for (int i = 0; i < Count; i++) hash = Mix(hash, Evaluate(i).Hash); return hash; }
    }
}
