namespace LegendaryTools.FixedPoint
{
    /// <summary>Versioned raw/status/state/wire corpus shared by NUnit, standalone and actual players. No Unity dependency.</summary>
    internal static unsafe partial class FixedRandomCorpus
    {
        internal const int Version = 1, Count = 160;
        internal struct Sample { internal int Index; internal FixedSettings Settings; internal FixedKind Kind; }
        internal unsafe struct Result
        {
            internal const int Capacity = 192;
            internal fixed ulong Words[Capacity];
            internal int Count, Managed;
            internal void Add(ulong value) { if (Count < Capacity) { fixed (ulong* p = Words) p[Count] = value; } Count++; }
            internal void Add(ulong value, FixedStatus status) { Add(value); Add((uint)status); }
            internal void Add(FixedPointVector3 value, FixedStatus status) { Add(unchecked((ulong)value.X.RawValue)); Add(unchecked((ulong)value.Y.RawValue)); Add(unchecked((ulong)value.Z.RawValue)); Add((uint)status); }
            internal bool Same(ref Result other)
            {
                if (Count != other.Count || Count > Capacity) return false;
                fixed (ulong* a = Words, b = other.Words) for (int i = 0; i < Count; i++) if (a[i] != b[i]) return false;
                return true;
            }
            internal ulong Digest(ulong hash = 14695981039346656037UL)
            {
                if (Count > Capacity) return 0;
                fixed (ulong* p = Words) for (int i = 0; i < Count; i++) hash = unchecked((hash ^ p[i]) * 1099511628211UL);
                return unchecked((hash ^ (uint)Count) * 1099511628211UL);
            }
        }
        internal static Sample Create(int index)
        {
            int group = index / 4 % 4, precision = group == 0 ? 0 : group == 1 ? 3 : group == 2 ? 9 : 18;
            return new Sample { Index = index, Kind = (FixedKind)(index % 4), Settings = new FixedSettings(precision > 9 ? 9 : precision, precision,
                rounding: (FixedRounding)(index / 32 % 5), mathProfile: (FixedMathProfile)(index / 16 % 2), arithmeticMode: (FixedArithmeticMode)(index % 3)) };
        }
        internal static Result Evaluate(Sample sample)
        {
            Result result = new Result { Managed = 0 }; int i = sample.Index;
            ulong seed = unchecked((ulong)i * 0x9E3779B97F4A7C15UL), stream = unchecked((ulong)i * 97UL);
            var random = (i & 1) == 0 ? FixedRandom.Create(seed, stream) : FixedRandom.ForEvent(seed, stream, (ulong)(uint)i << 48 | (uint)i);
            var state = new FixedRuntimeState(sample.Settings); var f = new FixedFormat(sample.Kind, state);
            result.Add(random.CaptureState().Key); result.Add(random.CaptureState().SequenceId); result.Add(random.Position);
            result.Add(random.NextUInt32()); result.Add(random.NextUInt64()); result.Add(unchecked((uint)random.NextInt32())); result.Add(unchecked((ulong)random.NextInt64()));
            result.Add(random.NextBool() ? 1UL : 0); result.Add(unchecked((ulong)random.NextSign()));
            result.Add(unchecked((ulong)random.Range(-57, 99, out FixedStatus status)), status);
            result.Add(random.Range(0x80000000U, uint.MaxValue, out status), status);
            result.Add(unchecked((ulong)random.Range(long.MinValue, long.MaxValue, out status)), status);
            result.Add(random.RangeInclusive(0UL, ulong.MaxValue, out status), status);
            result.Add(random.ChanceRatio(3, 7, out status) ? 1UL : 0, status);
            result.Add((ulong)random.RollDie(6, out status), status); result.Add((ulong)random.RollDice(3, 6, 2, out status), status);
            FixedRandomKernel.Unit(ref random, sample.Kind, state, out ulong raw, out status); result.Add(raw, status);
            FixedRandomKernel.Range(ref random, f.MinValue, f.MaxPositive, sample.Kind, state, true, out raw, out status); result.Add(raw, status);
            FixedRandomKernel.Range(ref random, 0, f.Scale, sample.Kind, state, false, out raw, out status); result.Add(raw, status);
            FixedRandomKernel.Chance(ref random, f.Scale / 3, sample.Kind, state, out bool chance, out status); result.Add(chance ? 1UL : 0, status);
            result.Add(unchecked((uint)random.NextFixed32(out status).RawValue), status); result.Add(random.NextUFixed32(out status).RawValue, status);
            result.Add(unchecked((ulong)random.NextFixed64(out status).RawValue), status); result.Add(random.NextUFixed64(out status).RawValue, status);
            result.Add(unchecked((uint)random.RangeInclusive(Fixed32.MinValue, Fixed32.MaxValue, out status).RawValue), status);
            result.Add(random.Range(UFixed32.Zero, UFixed32.MaxValue, out status).RawValue, status);
            result.Add(unchecked((ulong)random.Range(Fixed64.MinValue, Fixed64.MaxValue, out status).RawValue), status);
            result.Add(random.RangeInclusive(UFixed64.Zero, UFixed64.MaxValue, out status).RawValue, status);
            result.Add(random.Chance(Fixed32.FromRatio(1, 3), out status) ? 1UL : 0, status);
            result.Add(random.Chance(UFixed32.FromRatio(1, 3), out status) ? 1UL : 0, status);
            result.Add(random.Chance(Fixed64.FromRatio(1, 3), out status) ? 1UL : 0, status);
            result.Add(random.Chance(UFixed64.FromRatio(1, 3), out status) ? 1UL : 0, status);
            ulong* weights = stackalloc ulong[4]; ulong* prefix = stackalloc ulong[4];
            if (sample.Kind < FixedKind.Signed64) { uint* narrow = (uint*)weights; narrow[0] = 0; narrow[1] = 1; narrow[2] = 3; narrow[3] = 0; }
            else { weights[0] = 0; weights[1] = 1; weights[2] = 3; weights[3] = 0; }
            FixedRandomWeightsKernel.Build(weights, 4, sample.Kind, state, prefix, 4, out var table, out status); result.Add(table.Total, status);
            for (int w = 0; w < 4; w++) result.Add(prefix[w]);
            var linear = random;
            FixedRandomWeightsKernel.Select(ref linear, weights, 4, sample.Kind, state, out int selected, out status); result.Add(unchecked((ulong)selected), status);
            random.TryWeightedIndex(table, out selected, out status); result.Add(unchecked((ulong)selected), status);
            int* shuffled = stackalloc int[6]; for (int w = 0; w < 6; w++) shuffled[w] = w;
            random.TryShuffle(shuffled, 6, out status); result.Add((uint)status); for (int w = 0; w < 6; w++) result.Add((ulong)shuffled[w]);
            int* output = stackalloc int[4]; int* scratch = stackalloc int[12];
            random.TrySampleWithoutReplacement(12, output, 4, scratch, 12, out status); result.Add((uint)status); for (int w = 0; w < 4; w++) result.Add((ulong)output[w]);
            random.TryChoose(shuffled, 6, out selected, out status); result.Add((ulong)selected, status);
            byte* bytes = stackalloc byte[64]; random.TryFillBytes(bytes, 7, out status); result.Add((uint)status); for (int w = 0; w < 7; w++) result.Add(bytes[w]);
            Fixed64* batch = stackalloc Fixed64[5]; random.TryFill(batch, 5, out status); result.Add((uint)status); for (int w = 0; w < 5; w++) result.Add(unchecked((ulong)batch[w].RawValue));
            var wide = new FixedFormat(FixedKind.Signed64, state);
            FixedRandomGeometry.Inside(ref random, 0, 0, 0, wide.Scale, false, state, out var point, out status); result.Add(point, status);
            FixedRandomGeometry.On(ref random, 0, 0, 0, wide.Scale, false, state, out point, out status); result.Add(point, status);
            FixedRandomGeometry.Inside(ref random, 0, 0, 0, wide.Scale, true, state, out point, out status); result.Add(point, status);
            FixedRandomGeometry.On(ref random, 0, 0, 0, wide.Scale, true, state, out point, out status); result.Add(point, status);
            var min = new FixedPointVector3(Fixed64.FromRaw(-(long)wide.Scale), Fixed64.Zero, Fixed64.Zero);
            var max = new FixedPointVector3(Fixed64.FromRaw((long)wide.Scale), Fixed64.FromRaw((long)wide.Scale), Fixed64.FromRaw((long)wide.Scale));
            FixedRandomGeometry.Box(ref random, min, max, true, state, out point, out status); result.Add(point, status);
            var a = default(FixedPointVector3); var b = new FixedPointVector3(Fixed64.FromRaw((long)wide.Scale), Fixed64.Zero, Fixed64.Zero);
            var c = new FixedPointVector3(Fixed64.Zero, Fixed64.FromRaw((long)wide.Scale), Fixed64.Zero);
            FixedRandomGeometry.Triangle(ref random, a, b, c, state, out point, out status); result.Add(point, status);
            var circle = random.OnUnitCircle(out status); result.Add(unchecked((ulong)circle.X.RawValue)); result.Add(unchecked((ulong)circle.Y.RawValue)); result.Add((uint)status);
            result.Add(random.InsideUnitSphere(out status), status); result.Add(random.OnUnitSphere(out status), status);
            result.Add(unchecked((ulong)random.NextAngleRadians(out status).RawValue), status);
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); var writer = new FixedBufferWriter(bytes, 64);
            FixedRandomNetwork.TryWriteDescriptor(ref writer, profile, out status); result.Add((uint)status);
            FixedRandomNetwork.TryWriteState(ref writer, random.CaptureState(), profile, out status); result.Add((uint)status);
            result.Add((ulong)writer.BytesWritten); var wire = new FixedBufferReader(bytes, writer.BytesWritten);
            for (int w = 0; w < writer.BytesWritten / 8; w++) result.Add(wire.ReadBits(64));
            var reader = new FixedBufferReader(bytes, writer.BytesWritten);
            FixedRandomNetwork.TryReadDescriptor(ref reader, out var receivedProfile, out status); result.Add((uint)status);
            FixedRandomNetwork.TryReadState(ref reader, receivedProfile, out var saved, out status); result.Add((uint)status);
            result.Add(saved.Key); result.Add(saved.SequenceId); result.Add(saved.WordPosition);
            var restored = default(FixedRandom); restored.RestoreState(saved);
            result.Add(restored.NextUInt64()); result.Add(random.NextUInt64());
            writer = new FixedBufferWriter(bytes, 64); FixedRandomNetwork.TryWritePosition(ref writer, random.Position, profile, out status); result.Add((uint)status);
            result.Add((ulong)writer.BytesWritten); reader = new FixedBufferReader(bytes, writer.BytesWritten);
            FixedRandomNetwork.TryReadPosition(ref reader, ref restored, profile, out status); result.Add((uint)status); result.Add(restored.Position);
            result.Add(random.Position); result.Add(random.CaptureState().Key); result.Add(random.CaptureState().SequenceId);
            return result;
        }
    }
}
