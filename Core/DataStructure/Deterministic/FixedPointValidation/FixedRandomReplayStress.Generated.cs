namespace LegendaryTools.FixedPoint
{
    // A separate long replay fixture. Never changes or regenerates the original v1 corpus.
    internal static unsafe partial class FixedRandomReplayStress
    {
        internal const int Cases = 64, Steps = 256;
        internal struct Result
        {
            internal const int Capacity = 4096;
            internal fixed ulong Words[Capacity];
            internal int Count, Failures, Managed;
            internal void Add(ulong word)
            { if (Count < Capacity) { fixed (ulong* p = Words) p[Count] = word; } else Failures++; Count++; }
            internal void Add(ulong word, FixedStatus status) { Add(word); Add((uint)status); if (!status.IsSuccess()) Failures++; }
            internal void Add(FixedPointVector3 point, FixedStatus status)
            { Add(unchecked((ulong)point.X.RawValue)); Add(unchecked((ulong)point.Y.RawValue)); Add(unchecked((ulong)point.Z.RawValue)); Add(0, status); }
            internal void Check(bool condition) { if (!condition) Failures++; }
            internal bool Same(ref Result other)
            {
                if (Count != other.Count || Failures != other.Failures || Count > Capacity) return false;
                fixed (ulong* a = Words, b = other.Words) for (int i = 0; i < Count; i++) if (a[i] != b[i]) return false;
                return true;
            }
            internal ulong Digest(ulong hash = 14695981039346656037UL)
            {
                if (Count > Capacity) return 0;
                fixed (ulong* p = Words) for (int i = 0; i < Count; i++) hash = unchecked((hash ^ p[i]) * 1099511628211UL);
                return unchecked(((hash ^ (uint)Count) * 1099511628211UL ^ (uint)Failures) * 1099511628211UL);
            }
        }
        internal static Result Evaluate(int index)
        {
            Result result = new Result { Managed = 0 };
            ulong seed = index == 0 ? 0 : index == 63 ? ulong.MaxValue : unchecked((ulong)index * 0xd1342543de82ef95UL);
            ulong stream = (1UL << (index % 64)) ^ 0x8000000000000000UL;
            var random = (index & 1) == 0 ? FixedRandom.Create(seed, stream) : FixedRandom.ForEvent(seed, stream, ulong.MaxValue - (ulong)index);
            ulong start = index % 8 < 4 ? (ulong)(index % 4) : index % 8 == 4 ? uint.MaxValue :
                index % 8 == 5 ? (1UL << 40) + 3 : index % 8 == 6 ? (1UL << 63) + 1 : ulong.MaxValue - 16384;
            random.Seek(start);
            int group = index / 4 % 4, precision = group == 0 ? 0 : group == 1 ? 3 : group == 2 ? 9 : 18;
            var state = new FixedRuntimeState(new FixedSettings(precision > 9 ? 9 : precision, precision,
                rounding: (FixedRounding)(index / 4 % 5), mathProfile: (FixedMathProfile)(index / 16 % 2), arithmeticMode: (FixedArithmeticMode)(index % 3)));
            var kind = (FixedKind)(index % 4); var f = new FixedFormat(kind, state); var wide = new FixedFormat(FixedKind.Signed64, state);
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings);
            ulong* weights = stackalloc ulong[4]; ulong* prefix = stackalloc ulong[4];
            if (kind < FixedKind.Signed64) { uint* w = (uint*)weights; w[0] = 0; w[1] = 1; w[2] = 3; w[3] = 0; }
            else { weights[0] = 0; weights[1] = 1; weights[2] = 3; weights[3] = 0; }
            result.Check(FixedRandomWeightsKernel.Build(weights, 4, kind, state, prefix, 4, out var table, out _));
            byte* bytes = stackalloc byte[64]; int* values = stackalloc int[6];
            Fixed64* batch = stackalloc Fixed64[3];
            var origin = default(FixedPointVector3);
            var b = new FixedPointVector3(Fixed64.FromRaw((long)wide.Scale), Fixed64.Zero, Fixed64.Zero);
            var c = new FixedPointVector3(Fixed64.Zero, Fixed64.FromRaw((long)wide.Scale), Fixed64.Zero);
            var identity = random.CaptureState(); result.Add(identity.Key); result.Add(identity.SequenceId); result.Add(identity.WordPosition);
            for (int step = 0; step < Steps; step++)
            {
                result.Add((ulong)step); FixedStatus status;
                switch (step & 15)
                {
                    case 0: result.Add(random.NextUInt32(out status), status); break;
                    case 1: result.Add(random.NextUInt64(out status), status); break;
                    case 2: result.Add(unchecked((ulong)random.Range(long.MinValue, long.MaxValue, out status)), status); break;
                    case 3:
                        result.Check(FixedRandomKernel.Unit(ref random, kind, state, out ulong unit, out status)); result.Add(unit, status); break;
                    case 4:
                        result.Check(FixedRandomKernel.Range(ref random, f.MinValue, f.MaxPositive, kind, state, true, out ulong ranged, out status)); result.Add(ranged, status); break;
                    case 5: result.Add(random.ChanceRatio(ulong.MaxValue / 3, ulong.MaxValue, out status) ? 1UL : 0, status); break;
                    case 6: result.Add(unchecked((ulong)random.RollDice(3, 7, -5, out status)), status); break;
                    case 7:
                        for (int i = 0; i < 6; i++) values[i] = i;
                        result.Check(random.TryShuffle(values, 6, out status)); result.Add(0, status);
                        for (int i = 0; i < 6; i++) result.Add((ulong)values[i]); break;
                    case 8:
                        var linear = random;
                        result.Check(FixedRandomWeightsKernel.Select(ref linear, weights, 4, kind, state, out int expected, out status));
                        result.Check(random.TryWeightedIndex(table, out int selected, out status)); result.Add((ulong)selected, status);
                        result.Check(selected == expected && random.CaptureState() == linear.CaptureState()); break;
                    case 9:
                        result.Check(random.TryFillBytes(bytes, 5, out status)); result.Add(0, status);
                        for (int i = 0; i < 5; i++) result.Add(bytes[i]); break;
                    case 10:
                        var scalar = random;
                        result.Check(random.TryFill(batch, 3, out status)); result.Add(0, status);
                        for (int i = 0; i < 3; i++) { result.Check(batch[i] == scalar.NextFixed64()); result.Add(unchecked((ulong)batch[i].RawValue)); }
                        result.Check(random.CaptureState() == scalar.CaptureState()); break;
                    case 11:
                        result.Check(FixedRandomGeometry.Inside(ref random, 0, 0, 0, wide.Scale, true, state, out var inside, out status)); result.Add(inside, status); break;
                    case 12:
                        result.Check(FixedRandomGeometry.On(ref random, 0, 0, 0, wide.Scale, true, state, out var on, out status)); result.Add(on, status); break;
                    case 13:
                        result.Check(FixedRandomGeometry.Triangle(ref random, origin, b, c, state, out var triangle, out status)); result.Add(triangle, status); break;
                    case 14:
                        var writer = new FixedBufferWriter(bytes, 64);
                        result.Check(FixedRandomNetwork.TryWriteDescriptor(ref writer, profile, out status)); result.Add(0, status);
                        result.Check(FixedRandomNetwork.TryWriteState(ref writer, random.CaptureState(), profile, out status)); result.Add(0, status);
                        result.Check(FixedRandomNetwork.TryWritePosition(ref writer, random.Position, profile, out status)); result.Add(0, status);
                        for (int i = 0; i < writer.BytesWritten; i++) result.Add(bytes[i]);
                        var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                        result.Check(FixedRandomNetwork.TryReadDescriptor(ref reader, out var received, out status) && profile.IsCompatible(received));
                        result.Check(FixedRandomNetwork.TryReadState(ref reader, received, out var snapshot, out status));
                        var replay = default(FixedRandom); replay.RestoreState(snapshot); replay.Seek(0);
                        result.Check(FixedRandomNetwork.TryReadPosition(ref reader, ref replay, received, out status));
                        result.Check(replay.CaptureState() == random.CaptureState());
                        ulong wireWord = random.NextUInt64(out status); result.Add(wireWord, status);
                        result.Check(replay.NextUInt64() == wireWord);
                        result.Check(replay.CaptureState() == random.CaptureState()); break;
                    default:
                        var exhausted = random; exhausted.Seek(ulong.MaxValue); var before = exhausted.CaptureState();
                        result.Check(!exhausted.TryNextUInt64(out ulong failed, out status)); result.Add(failed); result.Add((uint)status);
                        result.Check(status == FixedStatus.OutOfRange && before == exhausted.CaptureState());
                        result.Check(!exhausted.TryPickIndex(2, out int failedIndex, out status)); result.Add(unchecked((ulong)failedIndex)); result.Add((uint)status);
                        result.Check(failedIndex == -1 && before == exhausted.CaptureState()); break;
                }
                result.Add(random.Position);
            }
            return result;
        }
    }
}
