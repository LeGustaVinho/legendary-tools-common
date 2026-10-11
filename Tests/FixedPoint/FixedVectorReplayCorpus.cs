using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LegendaryTools.FixedPoint
{
    // Shared with validation players by Tools/FixedPoint/sync_vector_replay_corpus.py.
    internal static class FixedVectorReplayCorpus
    {
        internal const int Version = 2;
        internal const int ExpectedCases = 115220;
        internal const ulong ExpectedChecksum = 9703503698145310419UL;
        internal const ulong ExpectedInputChecksum = 5598736731664205017UL;
        internal const int SequenceTicks = 32;
        internal const int SequenceCases = 1140;
        internal struct Sample
        { internal FixedSettings Settings; internal FixedVectorBits A, B; internal ulong Amount; internal int Operation, Dimensions; }
        internal struct Result
        {
            internal FixedVectorBits Vector, Velocity;
            internal ulong Scalar, WireChecksum;
            internal FixedVectorBits WireVector;
            internal ulong Wire0, Wire1, Wire2, Wire3;
            internal FixedStatus Status, WriteStatus, ReadStatus;
            internal long BitPosition, ReadBitPosition;
            internal int Managed;
        }
        internal static Sample[] Create()
        {
            var samples = new List<Sample>(); int index = 0;
            foreach (int precision in new[] { 0, 3, 6, 9, 18 })
            for (int rounding = 0; rounding < 5; rounding++)
            for (int profile = 0; profile < 2; profile++)
            for (int backend = 0; backend < 2; backend++)
            for (int scenario = 0; scenario < 4; scenario++)
            {
                var settings = new FixedSettings(3, precision, (FixedRounding)rounding, (FixedOverflow)scenario,
                    mathProfile: (FixedMathProfile)profile, arithmeticMode: backend == 0 ? FixedArithmeticMode.Portable : FixedArithmeticMode.Intrinsic);
                ulong scale = FixedRuntimeState.PowerOfTen(precision);
                FixedVectorBits a = new FixedVectorBits(scale, scale * 2, scale * 3), b = new FixedVectorBits(unchecked(0UL - scale * 2), scale, scale);
                if (scenario == 1) { a = new FixedVectorBits(0x7FFFFFFFFFFFFFFD, 0x7FFFFFFFFFFFFFFD, 1); b = new FixedVectorBits(0x7FFFFFFFFFFFFFFD, 0x8000000000000003, 1); }
                if (scenario == 2) { a = new FixedVectorBits(1, 0, 0); b = new FixedVectorBits(unchecked(0UL - 1), 0, 0); }
                if (scenario == 3) { a = new FixedVectorBits(0x8000000000000000, 0x7FFFFFFFFFFFFFFE, 0x7FFFFFFFFFFFFFFF); b = default; }
                for (int operation = 0; operation < 23; operation++)
                    samples.Add(new Sample { Settings = settings, A = a, B = b, Amount = scale / 2, Operation = operation, Dimensions = (++index & 1) == 0 ? 2 : 3 });
            }
            ulong seed = 0xD1B54A32D192ED03UL;
            for (int precision = 0; precision <= 18; precision++)
            for (int rounding = 0; rounding < 5; rounding++)
            for (int overflow = 0; overflow < 4; overflow++)
            for (int profile = 0; profile < 2; profile++)
            for (int mode = 0; mode < 3; mode++)
            for (int dimensions = 2; dimensions <= 3; dimensions++)
            {
                var settings = new FixedSettings(3, precision, (FixedRounding)rounding, (FixedOverflow)overflow,
                    mathProfile: (FixedMathProfile)profile, arithmeticMode: (FixedArithmeticMode)mode);
                ulong scale = FixedRuntimeState.PowerOfTen(precision);
                FixedVectorBits a = NextVector(ref seed), b = NextVector(ref seed);
                for (int operation = 0; operation < 23; operation++)
                {
                    ulong amount = operation % 4 == 0 ? unchecked(0UL - scale) : operation % 4 == 1 ? 0 : operation % 4 == 2 ? scale / 2 : scale * 2;
                    samples.Add(new Sample { Settings = settings, A = a, B = b, Amount = amount, Operation = operation, Dimensions = dimensions });
                }
                if (overflow == 0)
                    samples.Add(new Sample { Settings = settings, A = new FixedVectorBits(scale, 0),
                        B = new FixedVectorBits(unchecked(0UL - scale), scale, dimensions == 3 ? scale : 0), Operation = 23, Dimensions = dimensions });
            }
            return samples.ToArray();
        }
        private static ulong Next(ref ulong seed)
        {
            seed ^= seed >> 12; seed ^= seed << 25; seed ^= seed >> 27;
            ulong value = unchecked(seed * 2685821657736338717UL);
            return value == 0x8000000000000000UL || value == 0x8000000000000001UL || value >= 0x7FFFFFFFFFFFFFFEUL && value <= 0x7FFFFFFFFFFFFFFFUL ? 1UL : value;
        }
        private static FixedVectorBits NextVector(ref ulong seed) => new FixedVectorBits(Next(ref seed), Next(ref seed), Next(ref seed));
        internal static unsafe Result Evaluate(Sample s)
        {
            if (s.Operation == 23)
            {
                FixedVectorBits position = s.A, velocity = default; Result sequence = default;
                ulong trace = 14695981039346656037UL;
                for (int tick = 0; tick < SequenceTicks; tick++)
                { sequence = SequenceStep(s, tick, ref position, ref velocity); trace = Mix(trace, Hash(sequence)); }
                sequence.Scalar = trace; return sequence;
            }
            var state = new FixedRuntimeState(s.Settings); Result r = new Result { Managed = 0 };
            // Two-dimensional inputs must not leak a stored third coordinate into geometry.
            if (s.Dimensions == 2) { s.A.Z = 0; s.B.Z = 0; }
            int d = s.Dimensions; var axis = new FixedVectorBits(0, 0, state.Scale64);
            switch (s.Operation)
            {
                case 0: r.Vector = FixedVectorKernel.Binary(s.A, s.B, d, FixedBinary.Add, state, out r.Status); break;
                case 1: r.Vector = FixedVectorKernel.Binary(s.A, s.B, d, FixedBinary.Subtract, state, out r.Status); break;
                case 2: r.Vector = FixedVectorKernel.Binary(s.A, s.B, d, FixedBinary.Multiply, state, out r.Status); break;
                case 3: r.Vector = FixedVectorKernel.Binary(s.A, s.B, d, FixedBinary.Divide, state, out r.Status); break;
                case 4: r.Scalar = FixedVectorKernel.Dot(s.A, s.B, d, state, out r.Status); break;
                case 5: r.Scalar = FixedVectorKernel.Magnitude(s.A, d, false, state, out r.Status); break;
                case 6: r.Scalar = FixedVectorKernel.Magnitude(s.A, d, true, state, out r.Status); break;
                case 7: r.Vector = FixedVectorKernel.Normalize(s.A, d, state, out r.Status); break;
                case 8: r.Scalar = FixedVectorKernel.Distance(s.A, s.B, d, state, out r.Status); break;
                case 9: r.Vector = FixedVectorKernel.Lerp(s.A, s.B, s.Amount, d, true, state, out r.Status); break;
                case 10: r.Vector = FixedVectorKernel.Lerp(s.A, s.B, s.Amount, d, false, state, out r.Status); break;
                case 11: r.Vector = FixedVectorKernel.MoveTowards(s.A, s.B, s.Amount, d, state, out r.Status); break;
                case 12: r.Vector = FixedVectorKernel.ClampMagnitude(s.A, state.Scale64, d, state, out r.Status); break;
                case 13: r.Vector = FixedVectorKernel.Reflect(s.A, s.B, d, state, out r.Status); break;
                case 14: r.Scalar = FixedVectorKernel.Angle(s.A, s.B, axis, d, false, state, out r.Status); break;
                case 15: r.Scalar = FixedVectorKernel.Angle(s.A, s.B, axis, d, true, state, out r.Status); break;
                case 16: r.Vector = FixedVectorKernel.Cross(s.A, s.B, state, out r.Status); break;
                case 17: r.Vector = FixedVectorKernel.Project(s.A, s.B, false, state, out r.Status); break;
                case 18: r.Vector = FixedVectorKernel.Project(s.A, s.B, true, state, out r.Status); break;
                case 19: r.Vector = FixedVectorKernel.Slerp(s.A, s.B, s.Amount, (s.Dimensions == 2), state, out r.Status); break;
                case 20: r.Vector = FixedVectorKernel.RotateTowards(s.A, s.B, s.Amount, state.Scale64, state, out r.Status); break;
                case 21: FixedVectorKernel.OrthoNormalize(s.A, s.B, axis, true, state, out r.Vector, out r.Velocity, out FixedVectorBits third, out r.Status); r.Scalar = third.Z; break;
                case 22: r.Vector = FixedVectorKernel.SmoothDamp(s.A, s.B, default, state.Scale64, 0x7FFFFFFFFFFFFFFF, state.Scale64 / 60, d, state, out r.Velocity, out r.Status); break;
            }
            Network(ref r, s.A, d, s.Settings, s.Operation % 3, s.Settings.DecimalPlaces64);
            return r;
        }
        private static unsafe void Network(ref Result r, FixedVectorBits value, int dimensions, FixedSettings settings, int codecValue, int wirePrecision)
        {
            var state = new FixedRuntimeState(settings);
            byte* bytes = stackalloc byte[30];
            for (int i = 0; i < 30; i++) bytes[i] = 0;
            var codec = (FixedCodec)codecValue;
            var component = new FixedNetworkProfile(settings, FixedKind.Signed64, codec, wirePrecision, wirePrecision < settings.DecimalPlaces64,
                lowerRaw: codec == FixedCodec.BitPacked ? 0x8000000000000002UL : 0,
                upperRaw: codec == FixedCodec.BitPacked ? 0x7FFFFFFFFFFFFFFDUL : 0);
            var network = new FixedVectorNetworkProfile(component, dimensions); var writer = new FixedBufferWriter(bytes, 30);
            FixedVectorNetworkKernel.Write(ref writer, value, dimensions, network, state, out r.WriteStatus);
            r.BitPosition = writer.BitPosition;
            var reader = new FixedBufferReader(bytes, writer.BytesWritten);
            FixedVectorNetworkKernel.Read(ref reader, dimensions, network, state, out FixedVectorBits read, out r.ReadStatus);
            r.ReadBitPosition = reader.BitPosition;
            r.WireVector = read;
            for (int i = 0; i < writer.BytesWritten; i++)
            {
                ulong word = (ulong)bytes[i] << ((i & 7) * 8);
                if (i < 8) r.Wire0 |= word; else if (i < 16) r.Wire1 |= word; else if (i < 24) r.Wire2 |= word; else r.Wire3 |= word;
            }
            r.WireChecksum = Mix(Mix(Mix(0, read.X), read.Y), read.Z);
            for (int i = 0; i < writer.BytesWritten; i++) r.WireChecksum = Mix(r.WireChecksum, bytes[i]);
        }
        internal static Result SequenceStep(Sample s, int tick, ref FixedVectorBits position, ref FixedVectorBits velocity)
        {
            var state = new FixedRuntimeState(s.Settings); ulong scale = state.Scale64;
            Result result = default; FixedVectorBits target = (tick / 8 & 1) == 0 ? s.B : s.A;
            ulong delta = scale / 60; if (delta == 0) delta = 1;
            FixedVectorBits next = FixedVectorKernel.SmoothDamp(position, target, velocity, scale / 2, scale * 3, delta, s.Dimensions, state,
                out FixedVectorBits nextVelocity, out result.Status);
            if (result.Status.IsSuccess()) { position = next; velocity = nextVelocity; }
            if ((tick & 3) == 0)
            {
                position = FixedVectorKernel.MoveTowards(position, target, delta, s.Dimensions, state, out FixedStatus move);
                result.Status |= move;
            }
            position = FixedVectorKernel.ClampMagnitude(position, scale * 3, s.Dimensions, state, out FixedStatus clamp); result.Status |= clamp;
            Network(ref result, position, s.Dimensions, s.Settings, tick % 3, s.Settings.DecimalPlaces64 > 1 ? s.Settings.DecimalPlaces64 - 2 : s.Settings.DecimalPlaces64);
            if (result.ReadStatus.IsSuccess()) position = result.WireVector;
            result.Vector = position; result.Velocity = velocity;
            return result;
        }
        internal static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
        internal static ulong Hash(Result r)
        {
            ulong h = Mix(Mix(Mix(0, r.Vector.X), r.Vector.Y), r.Vector.Z);
            h = Mix(Mix(Mix(h, r.Velocity.X), r.Velocity.Y), r.Velocity.Z);
            h = Mix(Mix(Mix(h, r.Scalar), (uint)r.Status), r.WireChecksum);
            h = Mix(Mix(Mix(h, r.WireVector.X), r.WireVector.Y), r.WireVector.Z);
            h = Mix(Mix(Mix(Mix(h, r.Wire0), r.Wire1), r.Wire2), r.Wire3);
            return Mix(Mix(Mix(Mix(h, (uint)r.WriteStatus), (uint)r.ReadStatus), (ulong)r.BitPosition), (ulong)r.ReadBitPosition);
        }
        internal static ulong InputField(Sample s, int field)
        {
            if (field < 3) return s.A.Get(field);
            if (field < 6) return s.B.Get(field - 3);
            switch (field)
            {
                case 6: return s.Amount; case 7: return (ulong)s.Operation; case 8: return (ulong)s.Dimensions;
                case 9: return (ulong)s.Settings.DecimalPlaces32; case 10: return (ulong)s.Settings.DecimalPlaces64;
                case 11: return (ulong)s.Settings.Rounding; case 12: return (ulong)s.Settings.Overflow;
                case 13: return (ulong)s.Settings.ZeroDivision; case 14: return (ulong)s.Settings.UnsignedNegative;
                case 15: return (ulong)s.Settings.MathProfile; default: return (ulong)s.Settings.ArithmeticMode;
            }
        }
        internal static ulong HashInput(Sample s)
        { ulong hash = 0; for (int field = 0; field < 17; field++) hash = Mix(hash, InputField(s, field)); return hash; }
        internal static ulong OutputField(Result r, int field)
        {
            if (field < 3) return r.Vector.Get(field);
            if (field < 6) return r.Velocity.Get(field - 3);
            if (field >= 8 && field < 11) return r.WireVector.Get(field - 8);
            switch (field)
            {
                case 6: return r.Scalar; case 7: return r.WireChecksum;
                case 11: return r.Wire0; case 12: return r.Wire1; case 13: return r.Wire2; case 14: return r.Wire3;
                case 15: return (uint)r.Status; case 16: return (uint)r.WriteStatus; case 17: return (uint)r.ReadStatus;
                case 18: return (ulong)r.BitPosition; default: return (ulong)r.ReadBitPosition;
            }
        }
        // Managed trace export compares every field across processes, beyond the versioned signatures.
        internal static void WriteTraceRow(TextWriter writer, int index, int tick, Sample sample, Result result)
        {
            writer.Write(index.ToString(CultureInfo.InvariantCulture)); writer.Write(','); writer.Write(tick.ToString(CultureInfo.InvariantCulture));
            for (int field = 0; field < 17; field++) { writer.Write(','); writer.Write(InputField(sample, field).ToString(CultureInfo.InvariantCulture)); }
            for (int field = 0; field < 20; field++) { writer.Write(','); writer.Write(OutputField(result, field).ToString(CultureInfo.InvariantCulture)); }
            writer.Write('\n');
        }
        internal static void WriteSequenceTrace(TextWriter writer, int index, Sample sample)
        {
            FixedVectorBits position = sample.A, velocity = default;
            for (int tick = 0; tick < SequenceTicks; tick++) WriteTraceRow(writer, index, tick, sample, SequenceStep(sample, tick, ref position, ref velocity));
        }
    }
}
