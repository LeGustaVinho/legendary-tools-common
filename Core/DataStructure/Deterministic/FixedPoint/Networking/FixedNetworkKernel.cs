namespace LegendaryTools.FixedPoint
{
    internal static unsafe class FixedNetworkKernel
    {
        private static bool Validate(FixedNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            status = !state.IsInitialized ? FixedStatus.Uninitialized : !profile.IsValid ? FixedStatus.InvalidConfiguration :
                !profile.Settings.NumericEquals(state.Settings) ? FixedStatus.ProfileMismatch : FixedStatus.None;
            return status.IsSuccess();
        }
        internal static ulong Rescale(ulong raw, FixedKind kind, FixedRuntimeState fromState, FixedRuntimeState toState, out FixedStatus status)
        {
            var from = new FixedFormat(kind, fromState); var to = new FixedFormat(kind, toState);
            status = FixedStatus.None; FixedDecoded value = from.Decode(raw);
            if (!value.IsFinite) return value.IsNaN ? to.NaN : to.Infinity(value.Negative);
            FixedUInt128 magnitude = FixedUInt128.RoundedDivide(FixedUInt128.Multiply(value.Magnitude, to.Scale, fromState.Settings.ArithmeticMode),
                from.Scale, value.Negative, fromState.Settings.Rounding, out bool inexact);
            if (inexact) status |= FixedStatus.Inexact;
            return FixedKernel.Pack(magnitude, value.Negative, to, toState.Settings, ref status);
        }
        internal static ulong Canonicalize(ulong raw, FixedNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            var f = new FixedFormat(profile.Kind, state);
            if (!Validate(profile, state, out status)) return f.NaN;
            ulong wire = Rescale(raw, profile.Kind, state, profile.WireState, out FixedStatus quantization);
            ulong result = Rescale(wire, profile.Kind, profile.WireState, state, out FixedStatus expansion);
            status |= quantization | expansion; return result;
        }
        private static ulong Encode(ulong raw, FixedFormat f)
        {
            FixedDecoded value = f.Decode(raw);
            if (value.IsNaN) return 0;
            if (!value.IsFinite) return value.Negative ? 1UL : 2UL;
            if (value.IsZero && value.Negative) return 3;
            if (f.Unsigned) return value.Magnitude + 4;
            ulong zigzag = value.Negative ? unchecked(value.Magnitude * 2 - 1) : value.Magnitude * 2;
            return zigzag + 4;
        }
        private static ulong Special(ulong code, FixedFormat f) => code == 0 ? f.NaN : code == 1 ? f.NegativeInfinity : code == 2 ? f.PositiveInfinity : f.NegativeZero;
        private static ulong Decode(ulong code, FixedFormat f)
        {
            if (code < 4) return Special(code, f);
            ulong payload = code - 4;
            return f.Unsigned ? payload : f.Finite((payload >> 1) + (payload & 1), (payload & 1) != 0);
        }
        internal static bool Write(ref FixedBufferWriter writer, ulong raw, FixedNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Validate(profile, state, out status)) return false;
            FixedRuntimeState wireState = profile.WireState;
            var f = new FixedFormat(profile.Kind, wireState);
            raw = Rescale(raw, profile.Kind, state, wireState, out FixedStatus quantization);
            status |= quantization;
            if (!status.IsSuccess()) return false;
            ulong code = Encode(raw, f);
            if (profile.Codec == FixedCodec.BitPacked)
            {
                if (code >= 4)
                {
                    if (FixedKernel.Compare(raw, profile.LowerRaw, profile.Kind, wireState) < 0 || FixedKernel.Compare(raw, profile.UpperRaw, profile.Kind, wireState) > 0)
                    { status |= FixedStatus.OutOfRange; return false; }
                    code = unchecked((raw - profile.LowerRaw) & f.Mask) + 4;
                }
                int bits = profile.PackedBits;
                if (!writer.Has(bits)) { status |= FixedStatus.BufferTooSmall; return false; }
                writer.WriteBits(code, bits); return true;
            }
            if ((writer.BitPosition & 7) != 0) { status |= FixedStatus.InvalidEncoding; return false; }
            if (profile.Codec == FixedCodec.FixedWidth)
            {
                if (!writer.Has(f.Bits)) { status |= FixedStatus.BufferTooSmall; return false; }
                writer.WriteBits(raw, f.Bits); return true;
            }
            ulong remaining = code; int bytes = 1;
            while ((remaining >>= 7) != 0) bytes++;
            if (!writer.Has(bytes * 8)) { status |= FixedStatus.BufferTooSmall; return false; }
            for (int i = 0; i < bytes; i++)
            { byte value = (byte)(code & 127); code >>= 7; writer.WriteBits((ulong)(value | (code != 0 ? 128 : 0)), 8); }
            return true;
        }
        internal static bool Read(ref FixedBufferReader reader, FixedNetworkProfile profile, FixedRuntimeState state, out ulong raw, out FixedStatus status)
        {
            var domainFormat = new FixedFormat(profile.Kind, state); raw = domainFormat.NaN;
            if (!Validate(profile, state, out status)) return false;
            var f = new FixedFormat(profile.Kind, profile.WireState); FixedBufferReader trial = reader;
            ulong code;
            if (profile.Codec == FixedCodec.BitPacked)
            {
                int bits = profile.PackedBits;
                if (!trial.Has(bits)) { status |= FixedStatus.BufferTooSmall; return false; }
                code = trial.ReadBits(bits);
                if (code < 4) raw = Special(code, f);
                else
                {
                    ulong offset = code - 4, limit = unchecked((profile.UpperRaw - profile.LowerRaw) & f.Mask);
                    if (offset > limit) { status |= FixedStatus.InvalidEncoding; return false; }
                    raw = unchecked(profile.LowerRaw + offset) & f.Mask;
                }
            }
            else
            {
                if ((trial.BitPosition & 7) != 0) { status |= FixedStatus.InvalidEncoding; return false; }
                if (profile.Codec == FixedCodec.FixedWidth)
                {
                    if (!trial.Has(f.Bits)) { status |= FixedStatus.BufferTooSmall; return false; }
                    raw = trial.ReadBits(f.Bits);
                }
                else
                {
                    int max = f.Bits == 32 ? 5 : 10; code = 0; bool terminated = false;
                    for (int i = 0; i < max; i++)
                    {
                        if (!trial.Has(8)) { status |= FixedStatus.BufferTooSmall; return false; }
                        byte value = (byte)trial.ReadBits(8); ulong payload = (ulong)(value & 127);
                        if (i == max - 1 && (payload > (f.Bits == 32 ? 15UL : 1UL) || (value & 128) != 0))
                        { status |= FixedStatus.InvalidEncoding; return false; }
                        code |= payload << (i * 7);
                        if ((value & 128) == 0)
                        {
                            if (i > 0 && payload == 0) { status |= FixedStatus.InvalidEncoding; return false; }
                            terminated = true; break;
                        }
                    }
                    if (!terminated) { status |= FixedStatus.InvalidEncoding; return false; }
                    raw = Decode(code, f);
                }
            }
            raw = Rescale(raw, profile.Kind, profile.WireState, state, out FixedStatus expansion); status |= expansion;
            if (!status.IsSuccess()) { raw = domainFormat.NaN; return false; }
            reader = trial; return true;
        }

        internal static bool WriteDescriptor(ref FixedBufferWriter writer, FixedNetworkProfile profile, out FixedStatus status)
        {
            status = !profile.IsValid ? FixedStatus.InvalidConfiguration : (writer.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding :
                !writer.Has(FixedNetworkProfile.DescriptorBytes * 8) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            writer.WriteBits(0x31504646, 32); writer.WriteBits(FixedNetworkProfile.Version, 8);
            writer.WriteBits((byte)profile.Kind, 8); writer.WriteBits((byte)profile.Codec, 8); writer.WriteBits((byte)profile.WireDecimalPlaces, 8);
            writer.WriteBits((byte)profile.Settings.DecimalPlaces32, 8); writer.WriteBits((byte)profile.Settings.DecimalPlaces64, 8);
            writer.WriteBits((byte)profile.Settings.Rounding, 8); writer.WriteBits((byte)profile.Settings.Overflow, 8);
            writer.WriteBits((byte)profile.Settings.ZeroDivision, 8); writer.WriteBits((byte)profile.Settings.UnsignedNegative, 8);
            writer.WriteBits((byte)profile.Settings.MathProfile, 8); writer.WriteBits(profile.AllowLossy ? 1UL : 0, 8);
            writer.WriteBits(profile.LowerRaw, 64); writer.WriteBits(profile.UpperRaw, 64); writer.WriteBits(profile.Settings.NumericFingerprint, 64);
            return true;
        }
        internal static bool ReadDescriptor(ref FixedBufferReader reader, out FixedNetworkProfile profile, out FixedStatus status)
        {
            profile = default;
            status = (reader.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding : !reader.Has(FixedNetworkProfile.DescriptorBytes * 8) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            FixedBufferReader trial = reader;
            if (trial.ReadBits(32) != 0x31504646 || trial.ReadBits(8) != FixedNetworkProfile.Version) { status |= FixedStatus.InvalidEncoding; return false; }
            FixedKind kind = (FixedKind)trial.ReadBits(8); FixedCodec codec = (FixedCodec)trial.ReadBits(8); int wire = (int)trial.ReadBits(8);
            int p32 = (int)trial.ReadBits(8), p64 = (int)trial.ReadBits(8);
            FixedRounding rounding = (FixedRounding)trial.ReadBits(8); FixedOverflow overflow = (FixedOverflow)trial.ReadBits(8);
            FixedZeroDivision division = (FixedZeroDivision)trial.ReadBits(8); FixedUnsignedNegative negative = (FixedUnsignedNegative)trial.ReadBits(8);
            FixedMathProfile math = (FixedMathProfile)trial.ReadBits(8); ulong lossy = trial.ReadBits(8);
            ulong lower = trial.ReadBits(64), upper = trial.ReadBits(64), fingerprint = trial.ReadBits(64);
            var settings = new FixedSettings(p32, p64, rounding, overflow, division, negative, math);
            var candidate = new FixedNetworkProfile(settings, kind, codec, wire, lossy != 0, lower, upper);
            if (lossy > 1 || !candidate.IsValid || fingerprint != settings.NumericFingerprint) { status |= FixedStatus.InvalidEncoding; return false; }
            profile = candidate; reader = trial; return true;
        }
    }
}
