using System;
namespace LegendaryTools.FixedPoint
{
    public partial struct FixedRandom
    {
        public Fixed32 NextFixed32() => NextFixed32(out _);
        public Fixed32 NextFixed32(out FixedStatus status) { TryNextFixed32(out Fixed32 value, out status); return value; }
        public bool TryNextFixed32(out Fixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Unit(ref trial, FixedKind.Signed32, FixedRuntime.State, out ulong raw, out status);
            value = Fixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed32 NextFixed32Checked() { var value = NextFixed32(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool TryFill(Span<Fixed32> output, out FixedStatus status)
        {
            status = FixedRuntime.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!status.IsSuccess()) return false;
            FixedRandom end = this;
            for (int i = 0; i < output.Length; i++) if (!end.TryNextFixed32(out _, out status)) return false;
            FixedRandom replay = this;
            for (int i = 0; i < output.Length; i++) output[i] = replay.NextFixed32();
            this = end; return true;
        }
        public void Fill(Span<Fixed32> output) { TryFill(output, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TryFill(Fixed32* output, int count, out FixedStatus status)
        {
            status = count < 0 || output == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryFill(new Span<Fixed32>(output, count), out status);
        }
        public Fixed32 Range(Fixed32 min, Fixed32 max) => Range(min, max, out _);
        public Fixed32 Range(Fixed32 min, Fixed32 max, out FixedStatus status)
        { TryRange(min, max, out Fixed32 value, out status); return value; }
        public bool TryRange(Fixed32 min, Fixed32 max, out Fixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Signed32, FixedRuntime.State, false, out ulong raw, out status);
            value = Fixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed32 RangeChecked(Fixed32 min, Fixed32 max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public Fixed32 RangeInclusive(Fixed32 min, Fixed32 max) => RangeInclusive(min, max, out _);
        public Fixed32 RangeInclusive(Fixed32 min, Fixed32 max, out FixedStatus status)
        { TryRangeInclusive(min, max, out Fixed32 value, out status); return value; }
        public bool TryRangeInclusive(Fixed32 min, Fixed32 max, out Fixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Signed32, FixedRuntime.State, true, out ulong raw, out status);
            value = Fixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed32 RangeInclusiveChecked(Fixed32 min, Fixed32 max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool Chance(Fixed32 probability) => Chance(probability, out _);
        public bool Chance(Fixed32 probability, out FixedStatus status) { TryChance(probability, out bool value, out status); return value; }
        public bool TryChance(Fixed32 probability, out bool value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Chance(ref trial, probability.Bits, FixedKind.Signed32, FixedRuntime.State, out value, out status);
            if (ok) this = trial; return ok;
        }
        public bool ChanceChecked(Fixed32 probability)
        { bool value = Chance(probability, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public UFixed32 NextUFixed32() => NextUFixed32(out _);
        public UFixed32 NextUFixed32(out FixedStatus status) { TryNextUFixed32(out UFixed32 value, out status); return value; }
        public bool TryNextUFixed32(out UFixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Unit(ref trial, FixedKind.Unsigned32, FixedRuntime.State, out ulong raw, out status);
            value = UFixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed32 NextUFixed32Checked() { var value = NextUFixed32(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool TryFill(Span<UFixed32> output, out FixedStatus status)
        {
            status = FixedRuntime.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!status.IsSuccess()) return false;
            FixedRandom end = this;
            for (int i = 0; i < output.Length; i++) if (!end.TryNextUFixed32(out _, out status)) return false;
            FixedRandom replay = this;
            for (int i = 0; i < output.Length; i++) output[i] = replay.NextUFixed32();
            this = end; return true;
        }
        public void Fill(Span<UFixed32> output) { TryFill(output, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TryFill(UFixed32* output, int count, out FixedStatus status)
        {
            status = count < 0 || output == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryFill(new Span<UFixed32>(output, count), out status);
        }
        public UFixed32 Range(UFixed32 min, UFixed32 max) => Range(min, max, out _);
        public UFixed32 Range(UFixed32 min, UFixed32 max, out FixedStatus status)
        { TryRange(min, max, out UFixed32 value, out status); return value; }
        public bool TryRange(UFixed32 min, UFixed32 max, out UFixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Unsigned32, FixedRuntime.State, false, out ulong raw, out status);
            value = UFixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed32 RangeChecked(UFixed32 min, UFixed32 max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public UFixed32 RangeInclusive(UFixed32 min, UFixed32 max) => RangeInclusive(min, max, out _);
        public UFixed32 RangeInclusive(UFixed32 min, UFixed32 max, out FixedStatus status)
        { TryRangeInclusive(min, max, out UFixed32 value, out status); return value; }
        public bool TryRangeInclusive(UFixed32 min, UFixed32 max, out UFixed32 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Unsigned32, FixedRuntime.State, true, out ulong raw, out status);
            value = UFixed32.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed32 RangeInclusiveChecked(UFixed32 min, UFixed32 max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool Chance(UFixed32 probability) => Chance(probability, out _);
        public bool Chance(UFixed32 probability, out FixedStatus status) { TryChance(probability, out bool value, out status); return value; }
        public bool TryChance(UFixed32 probability, out bool value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Chance(ref trial, probability.Bits, FixedKind.Unsigned32, FixedRuntime.State, out value, out status);
            if (ok) this = trial; return ok;
        }
        public bool ChanceChecked(UFixed32 probability)
        { bool value = Chance(probability, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public Fixed64 NextFixed64() => NextFixed64(out _);
        public Fixed64 NextFixed64(out FixedStatus status) { TryNextFixed64(out Fixed64 value, out status); return value; }
        public bool TryNextFixed64(out Fixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Unit(ref trial, FixedKind.Signed64, FixedRuntime.State, out ulong raw, out status);
            value = Fixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed64 NextFixed64Checked() { var value = NextFixed64(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool TryFill(Span<Fixed64> output, out FixedStatus status)
        {
            status = FixedRuntime.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!status.IsSuccess()) return false;
            FixedRandom end = this;
            for (int i = 0; i < output.Length; i++) if (!end.TryNextFixed64(out _, out status)) return false;
            FixedRandom replay = this;
            for (int i = 0; i < output.Length; i++) output[i] = replay.NextFixed64();
            this = end; return true;
        }
        public void Fill(Span<Fixed64> output) { TryFill(output, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TryFill(Fixed64* output, int count, out FixedStatus status)
        {
            status = count < 0 || output == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryFill(new Span<Fixed64>(output, count), out status);
        }
        public Fixed64 Range(Fixed64 min, Fixed64 max) => Range(min, max, out _);
        public Fixed64 Range(Fixed64 min, Fixed64 max, out FixedStatus status)
        { TryRange(min, max, out Fixed64 value, out status); return value; }
        public bool TryRange(Fixed64 min, Fixed64 max, out Fixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Signed64, FixedRuntime.State, false, out ulong raw, out status);
            value = Fixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed64 RangeChecked(Fixed64 min, Fixed64 max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public Fixed64 RangeInclusive(Fixed64 min, Fixed64 max) => RangeInclusive(min, max, out _);
        public Fixed64 RangeInclusive(Fixed64 min, Fixed64 max, out FixedStatus status)
        { TryRangeInclusive(min, max, out Fixed64 value, out status); return value; }
        public bool TryRangeInclusive(Fixed64 min, Fixed64 max, out Fixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Signed64, FixedRuntime.State, true, out ulong raw, out status);
            value = Fixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public Fixed64 RangeInclusiveChecked(Fixed64 min, Fixed64 max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool Chance(Fixed64 probability) => Chance(probability, out _);
        public bool Chance(Fixed64 probability, out FixedStatus status) { TryChance(probability, out bool value, out status); return value; }
        public bool TryChance(Fixed64 probability, out bool value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Chance(ref trial, probability.Bits, FixedKind.Signed64, FixedRuntime.State, out value, out status);
            if (ok) this = trial; return ok;
        }
        public bool ChanceChecked(Fixed64 probability)
        { bool value = Chance(probability, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public UFixed64 NextUFixed64() => NextUFixed64(out _);
        public UFixed64 NextUFixed64(out FixedStatus status) { TryNextUFixed64(out UFixed64 value, out status); return value; }
        public bool TryNextUFixed64(out UFixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Unit(ref trial, FixedKind.Unsigned64, FixedRuntime.State, out ulong raw, out status);
            value = UFixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed64 NextUFixed64Checked() { var value = NextUFixed64(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool TryFill(Span<UFixed64> output, out FixedStatus status)
        {
            status = FixedRuntime.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!status.IsSuccess()) return false;
            FixedRandom end = this;
            for (int i = 0; i < output.Length; i++) if (!end.TryNextUFixed64(out _, out status)) return false;
            FixedRandom replay = this;
            for (int i = 0; i < output.Length; i++) output[i] = replay.NextUFixed64();
            this = end; return true;
        }
        public void Fill(Span<UFixed64> output) { TryFill(output, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TryFill(UFixed64* output, int count, out FixedStatus status)
        {
            status = count < 0 || output == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryFill(new Span<UFixed64>(output, count), out status);
        }
        public UFixed64 Range(UFixed64 min, UFixed64 max) => Range(min, max, out _);
        public UFixed64 Range(UFixed64 min, UFixed64 max, out FixedStatus status)
        { TryRange(min, max, out UFixed64 value, out status); return value; }
        public bool TryRange(UFixed64 min, UFixed64 max, out UFixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Unsigned64, FixedRuntime.State, false, out ulong raw, out status);
            value = UFixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed64 RangeChecked(UFixed64 min, UFixed64 max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public UFixed64 RangeInclusive(UFixed64 min, UFixed64 max) => RangeInclusive(min, max, out _);
        public UFixed64 RangeInclusive(UFixed64 min, UFixed64 max, out FixedStatus status)
        { TryRangeInclusive(min, max, out UFixed64 value, out status); return value; }
        public bool TryRangeInclusive(UFixed64 min, UFixed64 max, out UFixed64 value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Range(ref trial, min.Bits, max.Bits, FixedKind.Unsigned64, FixedRuntime.State, true, out ulong raw, out status);
            value = UFixed64.FromBits(raw); if (ok) this = trial; return ok;
        }
        public UFixed64 RangeInclusiveChecked(UFixed64 min, UFixed64 max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool Chance(UFixed64 probability) => Chance(probability, out _);
        public bool Chance(UFixed64 probability, out FixedStatus status) { TryChance(probability, out bool value, out status); return value; }
        public bool TryChance(UFixed64 probability, out bool value, out FixedStatus status)
        {
            FixedRandom trial = this;
            bool ok = FixedRandomKernel.Chance(ref trial, probability.Bits, FixedKind.Unsigned64, FixedRuntime.State, out value, out status);
            if (ok) this = trial; return ok;
        }
        public bool ChanceChecked(UFixed64 probability)
        { bool value = Chance(probability, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public int Range(int min, int max) => Range(min, max, out _);
        public int Range(int min, int max, out FixedStatus status)
        { TryRange(min, max, out int value, out status); return value; }
        public bool TryRange(int min, int max, out int value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)(long)min), unchecked((ulong)(long)max), false, true, false, out ulong raw, out status);
            value = unchecked((int)raw); return ok;
        }
        public int RangeChecked(int min, int max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public int RangeInclusive(int min, int max) => RangeInclusive(min, max, out _);
        public int RangeInclusive(int min, int max, out FixedStatus status)
        { TryRangeInclusive(min, max, out int value, out status); return value; }
        public bool TryRangeInclusive(int min, int max, out int value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)(long)min), unchecked((ulong)(long)max), false, true, true, out ulong raw, out status);
            value = unchecked((int)raw); return ok;
        }
        public int RangeInclusiveChecked(int min, int max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public uint Range(uint min, uint max) => Range(min, max, out _);
        public uint Range(uint min, uint max, out FixedStatus status)
        { TryRange(min, max, out uint value, out status); return value; }
        public bool TryRange(uint min, uint max, out uint value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), false, false, false, out ulong raw, out status);
            value = unchecked((uint)raw); return ok;
        }
        public uint RangeChecked(uint min, uint max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public uint RangeInclusive(uint min, uint max) => RangeInclusive(min, max, out _);
        public uint RangeInclusive(uint min, uint max, out FixedStatus status)
        { TryRangeInclusive(min, max, out uint value, out status); return value; }
        public bool TryRangeInclusive(uint min, uint max, out uint value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), false, false, true, out ulong raw, out status);
            value = unchecked((uint)raw); return ok;
        }
        public uint RangeInclusiveChecked(uint min, uint max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public long Range(long min, long max) => Range(min, max, out _);
        public long Range(long min, long max, out FixedStatus status)
        { TryRange(min, max, out long value, out status); return value; }
        public bool TryRange(long min, long max, out long value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), true, true, false, out ulong raw, out status);
            value = unchecked((long)raw); return ok;
        }
        public long RangeChecked(long min, long max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public long RangeInclusive(long min, long max) => RangeInclusive(min, max, out _);
        public long RangeInclusive(long min, long max, out FixedStatus status)
        { TryRangeInclusive(min, max, out long value, out status); return value; }
        public bool TryRangeInclusive(long min, long max, out long value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), true, true, true, out ulong raw, out status);
            value = unchecked((long)raw); return ok;
        }
        public long RangeInclusiveChecked(long min, long max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public ulong Range(ulong min, ulong max) => Range(min, max, out _);
        public ulong Range(ulong min, ulong max, out FixedStatus status)
        { TryRange(min, max, out ulong value, out status); return value; }
        public bool TryRange(ulong min, ulong max, out ulong value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), true, false, false, out ulong raw, out status);
            value = unchecked((ulong)raw); return ok;
        }
        public ulong RangeChecked(ulong min, ulong max)
        { var value = Range(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
        public ulong RangeInclusive(ulong min, ulong max) => RangeInclusive(min, max, out _);
        public ulong RangeInclusive(ulong min, ulong max, out FixedStatus status)
        { TryRangeInclusive(min, max, out ulong value, out status); return value; }
        public bool TryRangeInclusive(ulong min, ulong max, out ulong value, out FixedStatus status)
        {
            bool ok = TryIntegerRange(unchecked((ulong)min), unchecked((ulong)max), true, false, true, out ulong raw, out status);
            value = unchecked((ulong)raw); return ok;
        }
        public ulong RangeInclusiveChecked(ulong min, ulong max)
        { var value = RangeInclusive(min, max, out FixedStatus status); status.ThrowIfFailure(); return value; }
    }
}
