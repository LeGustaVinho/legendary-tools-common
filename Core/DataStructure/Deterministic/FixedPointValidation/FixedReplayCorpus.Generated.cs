namespace LegendaryTools.FixedPoint
{
    /// <summary>Shared deterministic validation inputs; no runtime/library oracle claims.</summary>
    internal static unsafe class FixedReplayCorpus
    {
        internal const int Version = 1;
        internal const int Count = 58 * 5 * 72;
        internal const ulong ExpectedChecksum = 6622938699589000362UL;
        internal const ulong ExpectedInputChecksum = 14675807239874021106UL;
        internal struct Sample
        {
            internal FixedKind Kind;
            internal FixedSettings Settings;
            internal ulong A, B, C, InputDouble;
            internal uint InputSingle;
            internal FixedUnary Unary;
        }
        internal struct Result
        {
            internal int Count, Managed;
            internal fixed ulong Values[96];
            internal void Add(ulong value)
            {
                if (Count >= 96) { Count = 97; return; }
                fixed (ulong* values = Values) values[Count++] = value;
            }
            internal void Add(ulong value, FixedStatus status) { Add(value); Add((uint)status); }
            internal ulong ValueAt(int index) { fixed (ulong* values = Values) return values[index]; }
            internal bool Same(ref Result other)
            {
                if (Count > 96 || Count != other.Count) return false;
                fixed (ulong* a = Values, b = other.Values)
                    for (int i = 0; i < Count; i++) if (a[i] != b[i]) return false;
                return true;
            }
            internal ulong Digest(ulong checksum)
            {
                checksum = Mix(checksum, (ulong)Count);
                if (Count > 96) return checksum;
                fixed (ulong* values = Values) for (int i = 0; i < Count; i++) checksum = Mix(checksum, values[i]);
                return checksum;
            }
        }
        internal static ulong Mix(ulong checksum, ulong value) => unchecked((checksum ^ value) * 1099511628211UL);
        private static ulong RandomBits(ulong value)
        {
            value = unchecked(value + 0x9e3779b97f4a7c15UL);
            value = unchecked((value ^ value >> 30) * 0xbf58476d1ce4e5b9UL);
            value = unchecked((value ^ value >> 27) * 0x94d049bb133111ebUL);
            return value ^ value >> 31;
        }
        private static ulong Edge(FixedFormat f, int index, ulong seed)
        {
            switch (index & 15)
            {
                case 0: return f.NaN;
                case 1: return f.NegativeInfinity;
                case 2: return f.PositiveInfinity;
                case 3: return f.NegativeZero;
                case 4: return 0;
                case 5: return 1;
                case 6: return f.Scale;
                case 7: return f.Finite(1, !f.Unsigned);
                case 8: return f.MinValue;
                case 9: return f.MaxPositive;
                case 10: return f.Finite(f.Scale, !f.Unsigned);
                case 11: return f.Finite(f.Scale <= f.MaxPositive / 100 ? f.Scale * 100 : f.MaxPositive, !f.Unsigned);
                case 12: return f.Finite(f.Scale <= f.MaxPositive / 100 ? f.Scale * 100 : f.MaxPositive, false);
                case 13: return f.Scale / 2;
                case 14: return f.MaxPositive - 1;
                default:
                    // Keep the ulong off the evaluation stack across the short-circuit branch.
                    // Unity 6000.3 IL2CPP can otherwise infer an int32 merge temporary for rem.un.
                    ulong magnitude = RandomBits(seed) % (f.MaxPositive + 1);
                    bool negative = !f.Unsigned && (seed & 1) != 0;
                    return f.Finite(magnitude, negative);
            }
        }
        internal static ulong DigestInput(Sample sample, ulong checksum)
        {
            checksum = Mix(checksum, (uint)sample.Kind);
            checksum = Mix(checksum, (uint)sample.Settings.DecimalPlaces32);
            checksum = Mix(checksum, (uint)sample.Settings.DecimalPlaces64);
            checksum = Mix(checksum, (uint)sample.Settings.Rounding);
            checksum = Mix(checksum, (uint)sample.Settings.Overflow);
            checksum = Mix(checksum, (uint)sample.Settings.ZeroDivision);
            checksum = Mix(checksum, (uint)sample.Settings.UnsignedNegative);
            checksum = Mix(checksum, (uint)sample.Settings.MathProfile);
            checksum = Mix(checksum, (uint)sample.Unary);
            checksum = Mix(checksum, sample.A); checksum = Mix(checksum, sample.B); checksum = Mix(checksum, sample.C);
            checksum = Mix(checksum, sample.InputDouble); return Mix(checksum, sample.InputSingle);
        }
        internal static Sample Create(int index, FixedArithmeticMode mode = FixedArithmeticMode.Portable)
        {
            int block = index / (5 * 72);
            FixedKind kind = block < 10 ? FixedKind.Signed32 : block < 20 ? FixedKind.Unsigned32 :
                block < 39 ? FixedKind.Signed64 : FixedKind.Unsigned64;
            int p = block < 10 ? block : block < 20 ? block - 10 : block < 39 ? block - 20 : block - 39;
            int phase = index % 72;
            var settings = new FixedSettings(p > 9 ? 9 : p, p, (FixedRounding)(index / 72 % 5),
                (FixedOverflow)(phase % 4), (FixedZeroDivision)(phase / 4 % 3),
                (FixedUnsignedNegative)(phase / 12 % 3), (FixedMathProfile)(phase / 36), mode);
            var f = new FixedFormat(kind, new FixedRuntimeState(settings));
            ulong seed = unchecked((ulong)index * 0x632be59bd9b4e019UL);
            ulong doubleBits = phase % 8 == 0 ? 0x8000000000000000UL : phase % 8 == 1 ? 1UL :
                phase % 8 == 2 ? 0x7ff0000000000000UL : phase % 8 == 3 ? 0xfff0000000000000UL :
                phase % 8 == 4 ? 0x7ff8000000000001UL : phase % 8 == 5 ? 0x400921fb54442d18UL : RandomBits(seed);
            return new Sample { Kind = kind, Settings = settings, A = Edge(f, index, seed), B = Edge(f, index + 5, seed + 1),
                C = Edge(f, index + 11, seed + 2), Unary = (FixedUnary)(index % ((int)FixedUnary.ReciprocalSqrt + 1)),
                InputDouble = doubleBits, InputSingle = phase % 6 == 0 ? 0x80000000U : phase % 6 == 1 ? 1U :
                    phase % 6 == 2 ? 0x7f800000U : phase % 6 == 3 ? 0xff800000U : (uint)RandomBits(seed + 3) };
        }
        internal static Result Evaluate(Sample sample)
        {
            var state = new FixedRuntimeState(sample.Settings); var f = new FixedFormat(sample.Kind, state);
            Result result = default; result.Managed = 0;
            for (int op = 0; op <= (int)FixedBinary.IEEERemainder; op++)
                result.Add(FixedKernel.Binary(sample.A, sample.B, sample.Kind, state, (FixedBinary)op, out FixedStatus status), status);
            result.Add(FixedMathKernel.FusedMultiplyAdd(sample.A, sample.B, sample.C, sample.Kind, state, out FixedStatus fused), fused);
            result.Add(FixedMathKernel.Unary(sample.A, sample.Kind, state, sample.Unary, out FixedStatus unary), unary);
            result.Add(FixedMathKernel.Pow(sample.A, sample.B, sample.Kind, state, out FixedStatus power), power);
            result.Add(FixedMathKernel.Log(sample.A, sample.B, sample.Kind, state, out FixedStatus logarithm), logarithm);
            result.Add(FixedMathKernel.Atan2(sample.A, sample.B, sample.Kind, state, out FixedStatus angle), angle);
            FixedMathKernel.SinCos(sample.A, sample.Kind, state, out ulong sine, out ulong cosine, out FixedStatus trigonometry);
            result.Add(sine, trigonometry); result.Add(cosine);
            for (int op = 0; op <= (int)FixedScalar.RadiansToDegrees; op++)
                result.Add(FixedScalarKernel.Evaluate(sample.A, sample.B, sample.C, (FixedScalar)op, sample.Kind, state, out FixedStatus status), status);
            result.Add(FixedIeee.Bits(FixedIeee.ToSingle(sample.A, sample.Kind, state)));
            result.Add(FixedIeee.Bits(FixedIeee.ToDouble(sample.A, sample.Kind, state)));
            result.Add(FixedIeee.From(FixedIeee.Single(sample.InputSingle), sample.Kind, state, out FixedStatus single), single);
            result.Add(FixedIeee.From(FixedIeee.Double(sample.InputDouble), sample.Kind, state, out FixedStatus dbl), dbl);
            for (int kind = 0; kind <= (int)FixedKind.Unsigned64; kind++)
                result.Add(FixedKernel.Convert(sample.A, sample.Kind, (FixedKind)kind, state, out FixedStatus status), status);
            byte* buffer = stackalloc byte[16];
            for (int codec = 0; codec <= (int)FixedCodec.BitPacked; codec++)
            {
                for (int i = 0; i < 16; i++) buffer[i] = 0xa5;
                var profile = codec == (int)FixedCodec.BitPacked ? new FixedNetworkProfile(sample.Settings, sample.Kind,
                    (FixedCodec)codec, lowerRaw: f.MinValue, upperRaw: f.MaxPositive) : new FixedNetworkProfile(sample.Settings, sample.Kind, (FixedCodec)codec);
                int start = codec == (int)FixedCodec.BitPacked ? (int)(sample.InputDouble & 7) : 0;
                var writer = new FixedBufferWriter(buffer, 16, start);
                bool written = FixedNetworkKernel.Write(ref writer, sample.A, profile, state, out FixedStatus write);
                var reader = new FixedBufferReader(buffer, writer.BytesWritten, start);
                bool read = FixedNetworkKernel.Read(ref reader, profile, state, out ulong raw, out FixedStatus readStatus);
                result.Add(written ? 1UL : 0UL, write); result.Add((ulong)writer.BitPosition);
                result.Add(read ? 1UL : 0UL, readStatus); result.Add((ulong)reader.BitPosition); result.Add(raw);
                // Store every byte, including guard bits, rather than relying on round-trip or hash alone.
                for (int i = 0; i < 16; i += 8)
                {
                    ulong bytes = 0;
                    for (int j = 0; j < 8; j++) bytes |= (ulong)buffer[i + j] << (j * 8);
                    result.Add(bytes);
                }
            }
            result.Add(FixedKernel.FromRatio(ulong.MaxValue, sample.C, (sample.InputDouble & 1) != 0,
                sample.Kind, state, out FixedStatus ratio), ratio);
            return result;
        }
    }
}
