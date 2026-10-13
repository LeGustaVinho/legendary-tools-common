using System.Collections.Generic;
using System.IO;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Dependent mixed-operation chains with checkpointed PRNG/state and wire feedback. All values are integers.</summary>
    internal static class FixedQuaternionSequenceCorpus
    {
        internal const int Version = 1;
        internal const int Steps = 32;
        internal const int ExpectedSequences = 1140;
        internal const int ExpectedCases = ExpectedSequences * Steps;
        internal const ulong ExpectedChecksum = 8229647155990362304UL;
        internal struct Sample { internal FixedSettings Settings; internal ulong Seed; }
        internal struct State { internal FixedQuaternionBits Quaternion; internal ulong Random, Hash; internal int Tick; }
        internal struct Result
        {
            internal FixedQuaternionReplayCorpus.Result Value;
            internal State Continuation;
            internal int Operation, Managed;
        }
        internal static Sample[] Create()
        {
            var samples = new List<Sample>();
            for (int precision = 0; precision <= 18; precision++)
            for (int rounding = 0; rounding < 5; rounding++)
            for (int math = 0; math < 2; math++)
            for (int normalization = 0; normalization < 2; normalization++)
            for (int arithmetic = 0; arithmetic < 3; arithmetic++)
            {
                int key = (((precision * 5 + rounding) * 2 + math) * 2 + normalization);
                samples.Add(new Sample {
                    Settings = new FixedSettings(decimalPlaces64: precision, rounding: (FixedRounding)rounding,
                        mathProfile: (FixedMathProfile)math, quaternionNormalization: (FixedQuaternionNormalizationMode)normalization,
                        arithmeticMode: (FixedArithmeticMode)arithmetic, overflow: (FixedOverflow)(key % 4)),
                    Seed = unchecked(0x9e3779b97f4a7c15UL * (ulong)(key + 1)) });
            }
            return samples.ToArray();
        }
        internal static State Initialize(Sample sample) => new State {
            Quaternion = FixedQuaternionKernel.Identity(new FixedRuntimeState(sample.Settings)), Random = sample.Seed, Hash = 14695981039346656037UL };
        private static ulong Next(ref ulong value) { value ^= value << 13; value ^= value >> 7; value ^= value << 17; return value; }
        private static ulong Signed(ref ulong value, ulong scale) => unchecked((ulong)((long)(Next(ref value) % (scale * 4 + 1)) - (long)(scale * 2)));
        internal static Result Advance(Sample sample, ref State continuation)
        {
            var state = new FixedRuntimeState(sample.Settings); ulong scale = state.Scale64;
            var b = new FixedQuaternionBits(Signed(ref continuation.Random, scale), Signed(ref continuation.Random, scale), Signed(ref continuation.Random, scale), Signed(ref continuation.Random, scale));
            b = FixedQuaternionKernel.Normalize(b, state, out var preparation);
            if (!preparation.IsSuccess()) b = FixedQuaternionKernel.Identity(state);
            var vector = new FixedVectorBits(Signed(ref continuation.Random, scale), Signed(ref continuation.Random, scale), Signed(ref continuation.Random, scale));
            // Include zero directions, sub-quantum vectors, dominant ties and a near-collinear direction in each chain.
            if (continuation.Tick % 11 == 0) vector = default;
            if (continuation.Tick % 11 == 1) vector = new FixedVectorBits(1, 1, 1);
            if (continuation.Tick % 11 == 2) vector = new FixedVectorBits(1, scale, 0);
            int operation = (continuation.Tick * 7 + sample.Settings.DecimalPlaces64) % 15;
            var input = new FixedQuaternionReplayCorpus.Sample {
                Settings = sample.Settings, A = continuation.Quaternion, B = b, Vector = vector,
                Scalar = Signed(ref continuation.Random, scale), Operation = operation, Encoding = continuation.Tick % 6 };
            var value = FixedQuaternionReplayCorpus.Evaluate(input);
            bool returnsQuaternion = operation == 0 || operation == 1 || operation == 2 || operation == 6 || operation == 7 || operation == 8 || operation == 9 || operation == 10 || operation == 11 || operation == 14;
            if (returnsQuaternion && value.Status.IsSuccess() && FixedQuaternionKernel.Finite(value.Quaternion, state)) continuation.Quaternion = value.Quaternion;
            // Restore the actual received representation, including loss, instead of re-encoding a decoded compact baseline.
            if (continuation.Tick % 5 == 4 && value.WriteStatus.IsSuccess() && value.ReadStatus.IsSuccess()) continuation.Quaternion = value.Decoded;
            continuation.Hash = FixedQuaternionReplayCorpus.Mix(continuation.Hash, FixedQuaternionReplayCorpus.Hash(value));
            for (int i = 0; i < 4; i++) continuation.Hash = FixedQuaternionReplayCorpus.Mix(continuation.Hash, continuation.Quaternion.Get(i));
            continuation.Tick++;
            return new Result { Value = value, Continuation = continuation, Operation = operation, Managed = 0 };
        }
        internal static ulong Hash(Result result)
        {
            ulong hash = FixedQuaternionReplayCorpus.Hash(result.Value);
            for (int i = 0; i < 4; i++) hash = FixedQuaternionReplayCorpus.Mix(hash, result.Continuation.Quaternion.Get(i));
            hash = FixedQuaternionReplayCorpus.Mix(hash, result.Continuation.Random);
            hash = FixedQuaternionReplayCorpus.Mix(hash, result.Continuation.Hash);
            hash = FixedQuaternionReplayCorpus.Mix(hash, (ulong)result.Continuation.Tick);
            return FixedQuaternionReplayCorpus.Mix(hash, (ulong)result.Operation);
        }
        internal static void WriteTraceRow(TextWriter writer, int index, Result result)
        {
            // Keep the established trace layout first; append the complete dependent state and PRNG checkpoint.
            using (var row = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                FixedQuaternionReplayCorpus.WriteTraceRow(row, index, result.Value);
                writer.Write(row.ToString().TrimEnd('\r', '\n'));
            }
            for (int i = 0; i < 4; i++) { writer.Write(','); writer.Write(result.Continuation.Quaternion.Get(i)); }
            writer.Write(','); writer.Write(result.Continuation.Random); writer.Write(','); writer.Write(result.Continuation.Hash);
            writer.Write(','); writer.Write(result.Continuation.Tick); writer.Write(','); writer.WriteLine(result.Operation);
        }
    }
}
