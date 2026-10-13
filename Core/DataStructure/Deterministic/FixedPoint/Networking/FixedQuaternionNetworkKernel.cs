namespace LegendaryTools.FixedPoint
{
    internal static unsafe class FixedQuaternionNetworkKernel
    {
        private const uint Magic = 0x31515046; // FPQ1, little endian.
        private static bool Validate(FixedQuaternionNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            status = !state.IsInitialized ? FixedStatus.Uninitialized : !profile.IsValid ? FixedStatus.InvalidConfiguration :
                !profile.Settings.QuaternionNumericEquals(state.Settings) ? FixedStatus.ProfileMismatch : FixedStatus.None;
            return status.IsSuccess();
        }
        // Exact v1 integer expressions: floor(2^(2p) / floor(sqrt(2^(2p+1)))).
        // Literal words avoid mutable caches, initialization order and repeated sqrt/division in Burst/managed.
        internal static FixedQ Limit(int p)
        {
            if (p != 128 && p != 256)
                return FixedQ.Divide(FixedQ.One(p), new FixedQ(FixedBigUInt.Sqrt(FixedQ.Integer(2, p).Magnitude, p)), p);
            FixedBigUInt value = default;
            if (p == 128)
            {
                value.Set(0, 0x754abe9fU); value.Set(1, 0x597d89b3U);
                value.Set(2, 0xf9de6484U); value.Set(3, 0xb504f333U);
            }
            else
            {
                value.Set(0, 0x83339915U); value.Set(1, 0xed17ac85U);
                value.Set(2, 0x893ba84cU); value.Set(3, 0x1d6f60baU);
                value.Set(4, 0x754abe9fU); value.Set(5, 0x597d89b3U);
                value.Set(6, 0xf9de6484U); value.Set(7, 0xb504f333U);
            }
            return new FixedQ(value);
        }
        private static bool EncodeCompact(ref FixedBufferWriter writer, FixedQuaternionBits value, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, ref FixedStatus status)
        {
            if (!FixedQuaternionKernel.Finite(value, state)) { status |= FixedStatus.InvalidDomain; return false; }
            var f = new FixedFormat(FixedKind.Signed64, state); int largest = 0; ulong maximum = 0;
            for (int i = 0; i < 4; i++) { ulong magnitude = f.Decode(value.Get(i)).Magnitude; if (magnitude > maximum) { maximum = magnitude; largest = i; } }
            if (maximum == 0) { status |= FixedStatus.InvalidDomain; return false; }
            int p = FixedMathKernel.Precision(state), bits = profile.ComponentBits; long bound = (1L << (bits - 1)) - 1;
            var q = FixedQuaternionKernel.UnitQ(value, p, state); var limit = Limit(p);
            if (f.Decode(value.Get(largest)).Negative) q = FixedQuaternionKernel.QScale(q, FixedQ.One(p).Negated, p);
            writer.WriteBits((uint)largest, 2);
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                FixedQ c = q.Get(i);
                FixedQ numerator = new FixedQ(FixedBigUInt.Multiply(c.Magnitude, FixedBigUInt.From((ulong)bound)), c.Negative);
                ulong code = FixedVectorKernel.RoundRatio(numerator, limit.Magnitude, state, ref status);
                var decoded = f.Decode(code); long integer = (long)(decoded.Magnitude > (ulong)bound ? (ulong)bound : decoded.Magnitude);
                if (decoded.Negative) integer = -integer;
                writer.WriteBits(unchecked((ulong)integer), bits);
            }
            writer.WriteBits(0, profile.TotalBits - 2 - bits * 3); status |= FixedStatus.Inexact;
            return status.IsSuccess();
        }
        private static bool DecodeCompact(ref FixedBufferReader reader, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, out FixedQuaternionBits value, ref FixedStatus status)
        {
            value = FixedQuaternionKernel.NaN;
            if (!reader.Has(profile.TotalBits)) { status |= FixedStatus.BufferTooSmall; return false; }
            int largest = (int)reader.ReadBits(2), bits = profile.ComponentBits, p = FixedMathKernel.Precision(state);
            long bound = (1L << (bits - 1)) - 1; FixedQ limit = Limit(p); FixedQuaternionQ q = default;
            FixedQ sum = default;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                ulong raw = reader.ReadBits(bits);
                long code = (raw & (1UL << (bits - 1))) != 0 ? unchecked((long)(raw | (~0UL << bits))) : (long)raw;
                if (code == -bound - 1) { status |= FixedStatus.InvalidEncoding; return false; }
                ulong magnitude = (ulong)(code < 0 ? -code : code);
                FixedQ c = new FixedQ(FixedBigUInt.Divide(FixedBigUInt.Multiply(limit.Magnitude, FixedBigUInt.From(magnitude)), (ulong)bound, out _), code < 0);
                q.Set(i, c); sum = FixedQ.Add(sum, FixedQ.Multiply(c, c, p));
            }
            if (reader.ReadBits(profile.TotalBits - 2 - bits * 3) != 0 || FixedQ.Compare(sum, FixedQ.One(p)) > 0)
            { status |= FixedStatus.InvalidEncoding; return false; }
            FixedQ omitted = new FixedQ(FixedBigUInt.Sqrt(FixedQ.Subtract(FixedQ.One(p), sum).Magnitude, p));
            // Permit the maximum movement caused by three directed quantizations around a dominant-component tie.
            FixedQ tolerance = limit.Shifted(2).DividedBy((ulong)bound);
            for (int i = 0; i < 4; i++) if (i != largest && FixedQ.Compare(q.Get(i).Abs, FixedQ.Add(omitted, tolerance)) > 0)
            { status |= FixedStatus.InvalidEncoding; return false; }
            q.Set(largest, omitted); status |= FixedStatus.Inexact;
            value = FixedQuaternionKernel.PackQ(q, p, state, ref status);
            return status.IsSuccess();
        }
        internal static bool Write(ref FixedBufferWriter writer, FixedQuaternionBits value, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Validate(profile, state, out status)) return false;
            if (profile.Codec == FixedQuaternionCodec.Components && profile.X.Codec != FixedCodec.BitPacked && (writer.BitPosition & 7) != 0)
            { status = FixedStatus.InvalidEncoding; return false; }
            if (profile.Codec == FixedQuaternionCodec.SmallestThree && !writer.Has(profile.TotalBits) && FixedQuaternionKernel.Finite(value, state))
            {
                var format = new FixedFormat(FixedKind.Signed64, state);
                bool nonzero = false;
                for (int i = 0; i < 4; i++) nonzero |= format.Decode(value.Get(i)).Magnitude != 0;
                // Preserve domain-error precedence and the Inexact flag reported by every valid compact encode.
                if (nonzero) { status = FixedStatus.BufferTooSmall | FixedStatus.Inexact; return false; }
            }
            byte* scratch = stackalloc byte[40]; for (int i = 0; i < 40; i++) scratch[i] = 0;
            var staging = new FixedBufferWriter(scratch, 40);
            if (profile.Codec == FixedQuaternionCodec.SmallestThree)
            { if (!EncodeCompact(ref staging, value, profile, state, ref status)) return false; }
            else for (int i = 0; i < 4; i++)
            { bool ok = FixedNetworkKernel.Write(ref staging, value.Get(i), profile.Get(i), state, out FixedStatus s); status |= s; if (!ok) return false; }
            if (!writer.Has(staging.BitPosition)) { status |= FixedStatus.BufferTooSmall; return false; }
            if ((writer.BitPosition & 7) == 0 && (staging.BitPosition & 7) == 0)
            {
                long at = writer.BitPosition >> 3;
                for (int i = 0; i < staging.BytesWritten; i++) writer.Buffer[at + i] = scratch[i];
                writer.BitPosition += staging.BitPosition;
                return true;
            }
            var reader = new FixedBufferReader(scratch, staging.BytesWritten);
            long remaining = staging.BitPosition;
            while (remaining > 0) { int count = remaining > 64 ? 64 : (int)remaining; writer.WriteBits(reader.ReadBits(count), count); remaining -= count; }
            return true;
        }
        internal static bool Read(ref FixedBufferReader reader, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, out FixedQuaternionBits value, out FixedStatus status)
        {
            value = FixedQuaternionKernel.NaN;
            if (!Validate(profile, state, out status)) return false;
            if (profile.Codec == FixedQuaternionCodec.Components && profile.X.Codec != FixedCodec.BitPacked && (reader.BitPosition & 7) != 0)
            { status = FixedStatus.InvalidEncoding; return false; }
            var trial = reader; FixedQuaternionBits candidate = default;
            if (profile.Codec == FixedQuaternionCodec.SmallestThree)
            { if (!DecodeCompact(ref trial, profile, state, out candidate, ref status)) return false; }
            else for (int i = 0; i < 4; i++)
            { bool ok = FixedNetworkKernel.Read(ref trial, profile.Get(i), state, out ulong c, out FixedStatus s); status |= s; if (!ok) return false; candidate.Set(i, c); }
            reader = trial; value = candidate; return true;
        }
        internal static bool WriteDescriptor(ref FixedBufferWriter writer, FixedQuaternionNetworkProfile profile, out FixedStatus status)
        {
            status = !profile.IsValid ? FixedStatus.InvalidConfiguration : (writer.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding :
                !writer.Has(profile.DescriptorBytes * 8L) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            writer.WriteBits(Magic, 32); writer.WriteBits(FixedQuaternionNetworkProfile.Version, 16);
            writer.WriteBits((byte)profile.Codec, 8); writer.WriteBits((byte)profile.Settings.QuaternionNormalization, 8);
            writer.WriteBits(FixedQuaternion.AlgorithmVersion, 16); writer.WriteBits(profile.TotalBits, 8); writer.WriteBits(profile.AllowLossy ? 1UL : 0UL, 8);
            for (int i = 0; i < (profile.Codec == FixedQuaternionCodec.Components ? 4 : 1); i++) FixedNetworkKernel.WriteDescriptor(ref writer, profile.Get(i), out _);
            return true;
        }
        internal static bool ReadDescriptor(ref FixedBufferReader reader, out FixedQuaternionNetworkProfile profile, out FixedStatus status)
        {
            profile = default; status = FixedStatus.None;
            if ((reader.BitPosition & 7) != 0) { status = FixedStatus.InvalidEncoding; return false; }
            if (!reader.Has(96)) { status = FixedStatus.BufferTooSmall; return false; }
            var trial = reader;
            if (trial.ReadBits(32) != Magic || trial.ReadBits(16) != FixedQuaternionNetworkProfile.Version) { status = FixedStatus.InvalidEncoding; return false; }
            var codec = (FixedQuaternionCodec)trial.ReadBits(8); var normalization = (FixedQuaternionNormalizationMode)trial.ReadBits(8);
            ulong algorithm = trial.ReadBits(16); int bits = (int)trial.ReadBits(8); ulong lossy = trial.ReadBits(8);
            if (codec > FixedQuaternionCodec.SmallestThree || normalization > FixedQuaternionNormalizationMode.NormalizeResult || lossy > 1)
            { status = FixedStatus.InvalidEncoding; return false; }
            if (algorithm != FixedQuaternion.AlgorithmVersion) { status = FixedStatus.ProfileMismatch; return false; }
            if (!FixedNetworkKernel.ReadDescriptor(ref trial, out FixedNetworkProfile x, out status)) return false;
            FixedSettings s = x.Settings;
            var settings = new FixedSettings(s.DecimalPlaces32, s.DecimalPlaces64, s.Rounding, s.Overflow, s.ZeroDivision, s.UnsignedNegative, s.MathProfile, s.ArithmeticMode, normalization);
            FixedQuaternionNetworkProfile candidate;
            if (codec == FixedQuaternionCodec.Components)
            {
                if (!FixedNetworkKernel.ReadDescriptor(ref trial, out var y, out status) || !FixedNetworkKernel.ReadDescriptor(ref trial, out var z, out status) || !FixedNetworkKernel.ReadDescriptor(ref trial, out var w, out status)) return false;
                candidate = new FixedQuaternionNetworkProfile(x, y, z, w, settings);
                if (bits != 0 || candidate.AllowLossy != (lossy == 1)) { status = FixedStatus.InvalidEncoding; return false; }
            }
            else
            {
                candidate = FixedQuaternionNetworkProfile.SmallestThree(settings, bits, lossy == 1);
                if (!candidate.X.IsCompatible(x)) { status = FixedStatus.InvalidEncoding; return false; }
            }
            if (!candidate.IsValid) { status = FixedStatus.InvalidEncoding; return false; }
            reader = trial; profile = candidate; return true;
        }
    }
}
