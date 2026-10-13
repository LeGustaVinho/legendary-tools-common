using System.Collections.Generic;
using System.IO;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Versioned portable inputs and complete raw results for managed/Burst/player comparison.</summary>
    internal static unsafe class FixedQuaternionReplayCorpus
    {
        internal const int Version = 1;
        internal const int ExpectedCases = 17100;
        internal const ulong ExpectedChecksum = 3988754440199593305UL;
        internal struct Sample
        {
            internal FixedSettings Settings;
            internal int Operation, Encoding;
            internal FixedQuaternionBits A, B;
            internal FixedVectorBits Vector;
            internal ulong Scalar;
        }
        internal struct Result
        {
            internal FixedQuaternionBits Quaternion, Decoded;
            internal FixedVectorBits Vector;
            internal ulong Scalar, Payload0, Payload1, Payload2, Payload3, Payload4, DescriptorHash;
            internal FixedStatus Status, WriteStatus, ReadStatus, DescriptorStatus;
            internal long WriteBits, ReadBits;
            internal int Managed;
            internal ulong Payload(int i) => i == 0 ? Payload0 : i == 1 ? Payload1 : i == 2 ? Payload2 : i == 3 ? Payload3 : Payload4;
        }
        internal static Sample[] Create()
        {
            var samples = new List<Sample>();
            for (int precision = 0; precision <= 18; precision++)
            for (int arithmetic = 0; arithmetic < 3; arithmetic++)
            for (int math = 0; math < 2; math++)
            for (int normalization = 0; normalization < 2; normalization++)
            for (int rounding = 0; rounding < 5; rounding++)
            for (int operation = 0; operation < 15; operation++)
            {
                var settings = new FixedSettings(decimalPlaces64: precision, arithmeticMode: (FixedArithmeticMode)arithmetic, mathProfile: (FixedMathProfile)math,
                    rounding: (FixedRounding)rounding, quaternionNormalization: (FixedQuaternionNormalizationMode)normalization);
                ulong scale = FixedRuntimeState.PowerOfTen(precision);
                samples.Add(new Sample {
                    Settings = settings, Operation = operation, Encoding = (operation + rounding + normalization) % 6,
                    A = new FixedQuaternionBits(scale / 2, scale / 3, unchecked(0UL - scale / 4), scale),
                    B = new FixedQuaternionBits(unchecked(0UL - scale / 3), scale / 2, scale / 4, scale),
                    Vector = new FixedVectorBits(scale, scale / 3, scale / 2), Scalar = scale / 3 });
            }
            return samples.ToArray();
        }
        internal static Result Evaluate(Sample sample)
        {
            var state = new FixedRuntimeState(sample.Settings); Result result = new Result { Managed = 0 };
            switch (sample.Operation)
            {
                case 0: result.Quaternion = FixedQuaternionKernel.Normalize(sample.A, state, out result.Status); break;
                case 1: result.Quaternion = FixedQuaternionKernel.Multiply(sample.A, sample.B, state, out result.Status); break;
                case 2: result.Quaternion = FixedQuaternionKernel.Inverse(sample.A, state, out result.Status); break;
                case 3: result.Scalar = FixedQuaternionKernel.Dot(sample.A, sample.B, state, out result.Status); break;
                case 4: result.Vector = FixedQuaternionKernel.Rotate(sample.A, sample.Vector, state, out result.Status); break;
                case 5: result.Scalar = FixedQuaternionKernel.Angle(sample.A, sample.B, false, state, out result.Status); break;
                case 6: result.Quaternion = FixedQuaternionKernel.Interpolate(sample.A, sample.B, sample.Scalar, false, true, state, out result.Status); break;
                case 7: result.Quaternion = FixedQuaternionKernel.Interpolate(sample.A, sample.B, sample.Scalar, false, false, state, out result.Status); break;
                case 8: result.Quaternion = FixedQuaternionKernel.Euler(sample.Vector, false, state, out result.Status); break;
                case 9: result.Quaternion = FixedQuaternionKernel.AngleAxis(sample.Scalar, sample.Vector, false, state, out result.Status); break;
                case 10: result.Quaternion = FixedQuaternionKernel.FromTo(sample.Vector, new FixedVectorBits(0, state.Scale64, 0), state, out result.Status); break;
                case 11: result.Quaternion = FixedQuaternionKernel.LookRotation(sample.Vector, new FixedVectorBits(0, state.Scale64, 0), state, out result.Status); break;
                case 12: result.Scalar = FixedQuaternionKernel.ToAngleAxis(sample.A, false, state, out result.Vector, out result.Status); break;
                case 13: result.Vector = FixedQuaternionKernel.EulerAngles(sample.A, false, state, out result.Status); break;
                case 14: result.Quaternion = FixedQuaternionKernel.RotateTowards(sample.A, sample.B, sample.Scalar, false, state, out result.Status); break;
            }
            var profile = sample.Encoding < 3 ? new FixedQuaternionNetworkProfile(sample.Settings, (FixedCodec)(sample.Encoding == 0 ? 1 : sample.Encoding == 1 ? 0 : 2),
                lowerRaw: sample.Encoding == 2 ? unchecked(0UL - state.Scale64) : 0, upperRaw: sample.Encoding == 2 ? state.Scale64 : 0) :
                FixedQuaternionNetworkProfile.SmallestThree(sample.Settings, sample.Encoding == 3 ? 32 : sample.Encoding == 4 ? 48 : 64, true);
            byte* bytes = stackalloc byte[40]; for (int i = 0; i < 40; i++) bytes[i] = 0;
            var writer = new FixedBufferWriter(bytes, 40);
            FixedQuaternionNetworkKernel.Write(ref writer, sample.A, profile, state, out result.WriteStatus); result.WriteBits = writer.BitPosition;
            var reader = new FixedBufferReader(bytes, writer.BytesWritten);
            FixedQuaternionNetworkKernel.Read(ref reader, profile, state, out result.Decoded, out result.ReadStatus); result.ReadBits = reader.BitPosition;
            for (int i = 0; i < 8; i++) { result.Payload0 |= (ulong)bytes[i] << (i * 8); result.Payload1 |= (ulong)bytes[8+i] << (i*8); result.Payload2 |= (ulong)bytes[16+i] << (i*8); result.Payload3 |= (ulong)bytes[24+i] << (i*8); result.Payload4 |= (ulong)bytes[32+i] << (i*8); }
            byte* descriptor = stackalloc byte[FixedQuaternionNetworkProfile.MaxDescriptorBytes]; var dw = new FixedBufferWriter(descriptor, FixedQuaternionNetworkProfile.MaxDescriptorBytes);
            FixedQuaternionNetworkKernel.WriteDescriptor(ref dw, profile, out result.DescriptorStatus);
            result.DescriptorHash = 14695981039346656037UL; for (int i = 0; i < dw.BytesWritten; i++) result.DescriptorHash = unchecked((result.DescriptorHash ^ descriptor[i]) * 1099511628211UL);
            return result;
        }
        internal static ulong Mix(ulong hash, ulong value)
        { for (int i = 0; i < 8; i++) { hash = unchecked((hash ^ (byte)value) * 1099511628211UL); value >>= 8; } return hash; }
        internal static ulong Hash(Result value)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < 4; i++) { hash = Mix(hash, value.Quaternion.Get(i)); hash = Mix(hash, value.Decoded.Get(i)); }
            for (int i = 0; i < 3; i++) hash = Mix(hash, value.Vector.Get(i));
            hash = Mix(hash, value.Scalar); for (int i = 0; i < 5; i++) hash = Mix(hash, value.Payload(i));
            hash = Mix(hash, value.DescriptorHash); hash = Mix(hash, (uint)value.Status); hash = Mix(hash, (uint)value.WriteStatus); hash = Mix(hash, (uint)value.ReadStatus); hash = Mix(hash, (uint)value.DescriptorStatus);
            hash = Mix(hash, (ulong)value.WriteBits); return Mix(hash, (ulong)value.ReadBits);
        }
        internal static void WriteTraceRow(TextWriter writer, int index, Result value)
        {
            writer.Write(index); for (int i = 0; i < 4; i++) { writer.Write(','); writer.Write(value.Quaternion.Get(i)); writer.Write(','); writer.Write(value.Decoded.Get(i)); }
            for (int i = 0; i < 3; i++) { writer.Write(','); writer.Write(value.Vector.Get(i)); }
            writer.Write(','); writer.Write(value.Scalar); for (int i = 0; i < 5; i++) { writer.Write(','); writer.Write(value.Payload(i)); }
            writer.Write(','); writer.Write(value.DescriptorHash); writer.Write(','); writer.Write((uint)value.Status); writer.Write(','); writer.Write((uint)value.WriteStatus); writer.Write(','); writer.Write((uint)value.ReadStatus);
            writer.Write(','); writer.Write((uint)value.DescriptorStatus); writer.Write(','); writer.Write(value.WriteBits); writer.Write(','); writer.WriteLine(value.ReadBits);
        }
    }
}
