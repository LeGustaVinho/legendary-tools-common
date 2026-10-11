using System;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Caller-owned Philox4x32-10 stream. Copies fork the current position. default is the seed/stream-zero stream.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public partial struct FixedRandom
    {
        public const byte AlgorithmId = 1;
        public const ushort SamplerVersion = 1;
        private ulong key, sequenceId, wordPosition;
        private uint cache0, cache1, cache2, cache3;
        private ulong cacheTag; // block + 1; zero is invalid, also for default(FixedRandom).
        public ulong Position => wordPosition;
        public bool IsExhausted => wordPosition == ulong.MaxValue;

        public static FixedRandom Create(ulong seed, ulong streamId = 0) => new FixedRandom
        { key = FixedRandomKernel.Mix64(seed ^ FixedRandomKernel.Mix64(streamId)) };
        public static FixedRandom ForEvent(ulong seed, ulong streamId, ulong eventId) => new FixedRandom
        { key = FixedRandomKernel.Mix64(seed ^ FixedRandomKernel.Mix64(streamId) ^ 1UL), sequenceId = eventId };
        public FixedRandomState CaptureState() => new FixedRandomState(key, sequenceId, wordPosition);
        public void RestoreState(FixedRandomState state)
        { key = state.Key; sequenceId = state.SequenceId; wordPosition = state.WordPosition; cacheTag = 0; }
        public void Seek(ulong position) { wordPosition = position; cacheTag = 0; }
        public bool TryAdvance(ulong words, out FixedStatus status)
        {
            status = words > ulong.MaxValue - wordPosition ? FixedStatus.OutOfRange : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            Seek(wordPosition + words); return true;
        }
        public void Advance(ulong words, out FixedStatus status) => TryAdvance(words, out status);
        public bool Advance(ulong words) => TryAdvance(words, out _);
        public void AdvanceChecked(ulong words) { TryAdvance(words, out FixedStatus status); status.ThrowIfFailure(); }

        public bool TryNextUInt32(out uint value, out FixedStatus status)
        {
            value = 0; status = IsExhausted ? FixedStatus.OutOfRange : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            ulong block = wordPosition >> 2;
            if (cacheTag != block + 1)
            {
                FixedRandomKernel.Block(key, sequenceId, block, out cache0, out cache1, out cache2, out cache3);
                cacheTag = block + 1;
            }
            switch ((int)(wordPosition & 3))
            { case 0: value = cache0; break; case 1: value = cache1; break; case 2: value = cache2; break; default: value = cache3; break; }
            wordPosition++; return true;
        }
        public uint NextUInt32() => NextUInt32(out _);
        public uint NextUInt32(out FixedStatus status) { TryNextUInt32(out uint value, out status); return value; }
        public uint NextUInt32Checked() { uint value = NextUInt32(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public bool TryNextUInt64(out ulong value, out FixedStatus status)
        {
            value = 0; status = wordPosition > ulong.MaxValue - 2 ? FixedStatus.OutOfRange : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            TryNextUInt32(out uint lo, out _); TryNextUInt32(out uint hi, out _);
            value = lo | (ulong)hi << 32; return true;
        }
        public ulong NextUInt64() => NextUInt64(out _);
        public ulong NextUInt64(out FixedStatus status) { TryNextUInt64(out ulong value, out status); return value; }
        public ulong NextUInt64Checked() { ulong value = NextUInt64(out FixedStatus status); status.ThrowIfFailure(); return value; }
        public int NextInt32() => NextInt32(out _);
        public int NextInt32(out FixedStatus status) => unchecked((int)NextUInt32(out status));
        public bool TryNextInt32(out int value, out FixedStatus status) { bool ok = TryNextUInt32(out uint raw, out status); value = unchecked((int)raw); return ok; }
        public int NextInt32Checked() { int v = NextInt32(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public long NextInt64() => NextInt64(out _);
        public long NextInt64(out FixedStatus status) => unchecked((long)NextUInt64(out status));
        public bool TryNextInt64(out long value, out FixedStatus status) { bool ok = TryNextUInt64(out ulong raw, out status); value = unchecked((long)raw); return ok; }
        public long NextInt64Checked() { long v = NextInt64(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public bool NextBool() => NextBool(out _);
        public bool NextBool(out FixedStatus status) { TryNextBool(out bool v, out status); return v; }
        public bool TryNextBool(out bool value, out FixedStatus status) { bool ok = TryNextUInt32(out uint raw, out status); value = (raw & 1) != 0; return ok; }
        public bool NextBoolChecked() { bool v = NextBool(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public int NextSign() => NextSign(out _);
        public int NextSign(out FixedStatus status) { bool positive = NextBool(out status); return status.IsSuccess() ? positive ? 1 : -1 : 0; }
        public bool TryNextSign(out int value, out FixedStatus status) { value = NextSign(out status); return status.IsSuccess(); }
        public int NextSignChecked() { int v = NextSign(out FixedStatus s); s.ThrowIfFailure(); return v; }

        public bool TryChanceRatio(ulong numerator, ulong denominator, out bool value, out FixedStatus status)
        {
            value = false; status = denominator == 0 || numerator > denominator ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess() || numerator == 0) return status.IsSuccess();
            if (numerator == denominator) { value = true; return true; }
            FixedRandom trial = this;
            if (!FixedRandomKernel.Bounded(ref trial, denominator, out ulong sample, out status)) return false;
            value = sample < numerator; this = trial; return true;
        }
        public bool ChanceRatio(ulong numerator, ulong denominator) => ChanceRatio(numerator, denominator, out _);
        public bool ChanceRatio(ulong numerator, ulong denominator, out FixedStatus status)
        { TryChanceRatio(numerator, denominator, out bool value, out status); return value; }
        public bool ChanceRatioChecked(ulong numerator, ulong denominator)
        { bool v = ChanceRatio(numerator, denominator, out FixedStatus s); s.ThrowIfFailure(); return v; }

        private bool TryIntegerRange(ulong min, ulong max, bool wide, bool signed, bool inclusive, out ulong value, out FixedStatus status)
        {
            value = 0;
            bool reversed = signed ? wide ? unchecked((long)min) > unchecked((long)max) : unchecked((int)min) > unchecked((int)max) : min > max;
            status = reversed ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (reversed) return false;
            if (min == max) { value = min; return true; }
            ulong mask = wide ? ulong.MaxValue : uint.MaxValue;
            ulong width = unchecked((max - min) & mask);
            FixedRandom trial = this;
            ulong offset;
            if (inclusive && width == mask)
            {
                if (wide) { if (!trial.TryNextUInt64(out offset, out status)) return false; }
                else { if (!trial.TryNextUInt32(out uint raw, out status)) return false; offset = raw; }
            }
            else if (!FixedRandomKernel.Bounded(ref trial, inclusive ? width + 1 : width, out offset, out status)) return false;
            value = unchecked(min + offset) & mask; this = trial; return true;
        }

        public bool TryRollDie(int sides, out int value, out FixedStatus status)
        {
            value = 0; status = sides < 1 ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            return TryRangeInclusive(1, sides, out value, out status);
        }
        public int RollDie(int sides) => RollDie(sides, out _);
        public int RollDie(int sides, out FixedStatus status) { TryRollDie(sides, out int value, out status); return value; }
        public int RollDieChecked(int sides) { int v = RollDie(sides, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public bool TryRollDice(int count, int sides, int modifier, out int value, out FixedStatus status)
        {
            value = 0;
            status = count < 0 || sides < 1 ? FixedStatus.InvalidDomain :
                (long)count + modifier < int.MinValue || (long)count * sides + modifier > int.MaxValue ? FixedStatus.OutOfRange : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            if (sides == 1 || count == 0) { value = (int)((long)count * sides + modifier); return true; }
            FixedRandom trial = this; value = modifier;
            for (int i = 0; i < count; i++)
            { if (!trial.TryRollDie(sides, out int roll, out status)) { value = 0; return false; } value += roll; }
            this = trial; return true;
        }
        public int RollDice(int count, int sides, int modifier = 0) => RollDice(count, sides, modifier, out _);
        public int RollDice(int count, int sides, int modifier, out FixedStatus status)
        { TryRollDice(count, sides, modifier, out int value, out status); return value; }
        public int RollDiceChecked(int count, int sides, int modifier = 0)
        { int v = RollDice(count, sides, modifier, out FixedStatus s); s.ThrowIfFailure(); return v; }

        /// <summary>One word per byte, low eight bits; equal to repeated RangeInclusive(0,255).</summary>
        public unsafe bool TryFillBytes(byte* buffer, int length, out FixedStatus status)
        {
            status = length < 0 || buffer == null && length != 0 ? FixedStatus.InvalidDomain :
                (ulong)length > ulong.MaxValue - wordPosition ? FixedStatus.OutOfRange : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            for (int i = 0; i < length; i++) buffer[i] = unchecked((byte)NextUInt32());
            return true;
        }
        public unsafe void FillBytes(byte* buffer, int length, out FixedStatus status) => TryFillBytes(buffer, length, out status);
        public unsafe bool TryFillBytes(Span<byte> buffer, out FixedStatus status)
        { fixed (byte* pointer = buffer) return TryFillBytes(pointer, buffer.Length, out status); }
        public void FillBytes(Span<byte> buffer) { TryFillBytes(buffer, out FixedStatus s); s.ThrowIfFailure(); }
    }
}
