namespace LegendaryTools.FixedPoint
{
    // Versioned, integer-only sequence corpus shared byte-for-byte with validation players.
    // Record words are compared directly across backends; the checksum is only a compact regression fingerprint.
    internal static unsafe class FixedBoundsStressCorpus
    {
        internal const int Version = 1, Count = 960, Steps = 64, CheckpointStep = 32;
        internal const ulong ExpectedChecksum = 11524040837634330506UL;
        internal struct Result { internal ulong Hash; internal int Errors, Replayed, Managed; }
        internal struct Record
        {
            internal const int Capacity = 64;
            internal int Count;
            internal fixed ulong Words[Capacity];
            internal void Add(ulong value) { Words[Count++] = value; }
            internal void Vector(FixedVectorBits value, int dimensions) { Add(value.X); Add(value.Y); Add(dimensions == 3 ? value.Z : 0); }
            internal void Buffer(byte* bytes)
            {
                for (int i = 0; i < 8; i++)
                { ulong word = 0; for (int b = 0; b < 8; b++) word |= (ulong)bytes[i * 8 + b] << (b * 8); Add(word); }
            }
        }
        internal static bool Equal(Record* a, Record* b)
        {
            if (a->Count != b->Count) return false;
            for (int i = 0; i < Record.Capacity; i++) if (a->Words[i] != b->Words[i]) return false;
            return true;
        }
        internal static ulong Next(ref ulong seed)
        {
            seed = unchecked(seed + 0x9E3779B97F4A7C15UL); ulong v = seed;
            v = unchecked((v ^ (v >> 30)) * 0xBF58476D1CE4E5B9UL);
            v = unchecked((v ^ (v >> 27)) * 0x94D049BB133111EBUL); return v ^ (v >> 31);
        }
        private static ulong Wide(ref ulong seed)
        {
            ulong v = Next(ref seed);
            if ((v & 15) == 0) return 0x8000000000000002UL;
            if ((v & 15) == 1) return 0x7FFFFFFFFFFFFFFDUL;
            if ((v & 15) == 2) return 0x7FFFFFFFFFFFFFFEUL; // -0
            ulong magnitude = v % 0x7FFFFFFFFFFFFFFDUL;
            return (v & 1) == 0 ? magnitude : unchecked(0UL - magnitude);
        }
        private static FixedVectorBits Small(ref ulong seed)
            => new FixedVectorBits(unchecked((ulong)((long)(Next(ref seed) % 513) - 256)),
                unchecked((ulong)((long)(Next(ref seed) % 513) - 256)), unchecked((ulong)((long)(Next(ref seed) % 513) - 256)));
        private static bool Same(FixedBoundsBits a, FixedBoundsBits b, int dimensions)
        { for (int i = 0; i < dimensions; i++) if (a.First.Get(i) != b.First.Get(i) || a.Second.Get(i) != b.Second.Get(i)) return false; return true; }
        private static bool Step(ref FixedBoundsBits box, int tick, int dimensions, FixedRuntimeState numeric,
            FixedBoundsRuntimeState geometry, ref ulong seed, out FixedStatus status)
        {
            var input = Small(ref seed); int operation = tick % 12; FixedBoundsBits changed;
            bool ok;
            if (operation < 5)
            {
                var part = operation == 0 ? FixedBoundsPart.Center : operation == 1 ? FixedBoundsPart.Size : operation == 2 ? FixedBoundsPart.Extents : operation == 3 ? FixedBoundsPart.Min : FixedBoundsPart.Max;
                if (operation == 1 || operation == 2) for (int i = 0; i < dimensions; i++) input.Set(i, (ulong)System.Math.Abs(unchecked((long)input.Get(i))));
                ok = FixedBoundsKernel.Set(box, part, input, dimensions, numeric, geometry, out changed, out status);
            }
            else if (operation == 5 || operation == 10)
            {
                for (int i = 0; i < dimensions; i++) input.Set(i, unchecked((ulong)((long)(Next(ref seed) % 11) - 5)));
                if (operation == 10) input.Set(dimensions - 1, unchecked((ulong)-(long.MaxValue - 2)));
                ok = FixedBoundsKernel.Expand(box, input, dimensions, numeric, geometry, out changed, out status);
            }
            else if (operation == 6 || operation == 11)
            {
                if (operation == 11) input.Set(dimensions - 1, Wide(ref seed));
                ok = FixedBoundsKernel.Encapsulate(box, input, input, dimensions, numeric, geometry, out changed, out status);
            }
            else
            {
                var part = (FixedBoundsPart)(tick % 5);
                if (operation == 7) input.Set(dimensions - 1, 0x8000000000000000UL);
                if (operation == 8) { input.Set(dimensions - 1, 0x7FFFFFFFFFFFFFFDUL); part = FixedBoundsPart.Center; }
                if (operation == 9) input = FixedBoundsKernel.Get(box, part, dimensions, numeric, geometry, out _);
                ok = FixedBoundsKernel.Set(box, part, input, dimensions, numeric, geometry, out changed, out status);
            }
            box = changed; return ok;
        }
        private static Record Observe(FixedBoundsBits box, bool changed, FixedStatus mutation, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, ref ulong seed, ref int errors)
        {
            Record record = default; record.Vector(box.First, dimensions); record.Vector(box.Second, dimensions); record.Add(changed ? 1UL : 0UL); record.Add((uint)mutation);
            if (!FixedBoundsKernel.Validate(box, dimensions, numeric, geometry, out _)) errors |= 2;
            var point = Small(ref seed); var direction = Small(ref seed);
            for (int i = 0; i < dimensions; i++)
            { if ((Next(ref seed) & 3) == 0) point.Set(i, Wide(ref seed)); direction.Set(i, (Next(ref seed) & 1) == 0 ? Wide(ref seed) : unchecked((ulong)((long)(Next(ref seed) % 5) - 2))); }
            bool ok = FixedBoundsKernel.Contains(box, point, dimensions, numeric, geometry, out bool contained, out var status);
            record.Add(ok && contained ? 1UL : 0UL); record.Add((uint)status);
            var closest = FixedBoundsKernel.ClosestPoint(box, point, dimensions, numeric, geometry, out status); record.Vector(closest, dimensions); record.Add((uint)status);
            if (!status.IsSuccess() || !FixedBoundsKernel.Contains(box, closest, dimensions, numeric, geometry, out bool closestInside, out _) || !closestInside) errors |= 4;
            ulong distance = FixedBoundsKernel.SqrDistance(box, point, dimensions, numeric, geometry, out status); record.Add(distance); record.Add((uint)status);
            bool rayOk = FixedBoundsKernel.IntersectRay(box, point, direction, dimensions, numeric, geometry, true, out bool hit, out ulong t, out status);
            record.Add(rayOk ? 1UL : 0UL); record.Add(hit ? 1UL : 0UL); record.Add(t); record.Add((uint)status);
            if (!FixedBoundsKernel.IntersectRay(box, point, direction, dimensions, numeric, geometry, false, out bool booleanHit, out _, out var booleanStatus) || booleanStatus != FixedStatus.None || rayOk && booleanHit != hit) errors |= 8;
            byte* bytes = stackalloc byte[64];
            for (int codec = 0; codec < 3; codec++)
            {
                for (int i = 0; i < 64; i++) bytes[i] = 0xA5;
                var axis = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, (FixedCodec)codec,
                    lowerRaw: codec == 2 ? 0x8000000000000002UL : 0, upperRaw: codec == 2 ? 0x7FFFFFFFFFFFFFFDUL : 0);
                var fields = new FixedVectorNetworkProfile(axis, dimensions); var profile = new FixedBoundsNetworkProfile(geometry.Settings.Representation, fields, fields);
                long start = codec == 2 ? 5 : 16; var writer = new FixedBufferWriter(bytes, 64, start);
                bool written = FixedBoundsNetworkKernel.Write(ref writer, box, dimensions, profile, numeric, geometry, out status);
                record.Add(written ? 1UL : 0UL); record.Add((uint)status); record.Add((ulong)writer.BitPosition); record.Buffer(bytes);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten, start);
                bool read = FixedBoundsNetworkKernel.Read(ref reader, dimensions, profile, numeric, geometry, out var restored, out status);
                if (!written || !read || status != FixedStatus.None || reader.BitPosition != writer.BitPosition || !Same(box, restored, dimensions)) errors |= 16;
                record.Add(read ? 1UL : 0UL); record.Add((uint)status); record.Add((ulong)reader.BitPosition);
            }
            return record;
        }
        internal static Result Evaluate(int index, Record* trace = null)
        {
            int original = index; var arithmetic = (FixedArithmeticMode)(index % 3); index /= 3; var overflow = (FixedOverflow)(index % 4); index /= 4;
            var rounding = (FixedRounding)(index % 5); index /= 5; int precisionIndex = index % 4; index /= 4;
            int precision = precisionIndex == 0 ? 0 : precisionIndex == 1 ? 3 : precisionIndex == 2 ? 9 : 18;
            var representation = (FixedBoundsRepresentation)(index % 2); index /= 2; int dimensions = 2 + index % 2;
            var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding, overflow, arithmeticMode: arithmetic));
            var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation));
            FixedBoundsKernel.FromMinMax(new FixedVectorBits(unchecked((ulong)-17L), unchecked((ulong)-23L), unchecked((ulong)-31L)), new FixedVectorBits(19, 41, 47), dimensions, numeric, geometry, out var box, out _);
            var fields = new FixedVectorNetworkProfile(new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, FixedCodec.FixedWidth), dimensions);
            var checkpointProfile = new FixedBoundsNetworkProfile(representation, fields, fields); byte* checkpoint = stackalloc byte[48]; int checkpointBytes = 0;
            Record* records = stackalloc Record[Steps]; ulong savedSeed = 0, seed = unchecked(0xCD902817FB62A035UL + (ulong)original);
            ulong hash = FixedBoundsReplayCorpus.Mix(14695981039346656037UL, (ulong)original); int errors = 0;
            for (int tick = 0; tick < Steps; tick++)
            {
                var previous = box; bool changed = Step(ref box, tick, dimensions, numeric, geometry, ref seed, out var status);
                if (!changed && !Same(previous, box, dimensions)) errors |= 32;
                Record record = Observe(box, changed, status, dimensions, numeric, geometry, ref seed, ref errors); records[tick] = record; if (trace != null) trace[tick] = record;
                hash = FixedBoundsReplayCorpus.Mix(hash, (ulong)record.Count);
                for (int i = 0; i < record.Count; i++) hash = FixedBoundsReplayCorpus.Mix(hash, record.Words[i]);
                if (tick == CheckpointStep - 1)
                {
                    savedSeed = seed; var writer = new FixedBufferWriter(checkpoint, 48);
                    if (!FixedBoundsNetworkKernel.Write(ref writer, box, dimensions, checkpointProfile, numeric, geometry, out status) || status != FixedStatus.None) errors |= 64;
                    checkpointBytes = writer.BytesWritten;
                }
            }
            var reader = new FixedBufferReader(checkpoint, checkpointBytes);
            if (!FixedBoundsNetworkKernel.Read(ref reader, dimensions, checkpointProfile, numeric, geometry, out box, out _)) errors |= 128;
            seed = savedSeed; int replayed = 0;
            for (int tick = CheckpointStep; tick < Steps; tick++)
            {
                bool changed = Step(ref box, tick, dimensions, numeric, geometry, ref seed, out var status);
                Record replay = Observe(box, changed, status, dimensions, numeric, geometry, ref seed, ref errors);
                if (!Equal(&replay, records + tick)) errors |= 1; replayed++;
            }
            return new Result { Hash = hash, Errors = errors, Replayed = replayed, Managed = 0 };
        }
        internal static ulong Checksum()
        { ulong hash = 14695981039346656037UL; for (int i = 0; i < Count; i++) hash = FixedBoundsReplayCorpus.Mix(hash, Evaluate(i).Hash); return hash; }
    }
}
