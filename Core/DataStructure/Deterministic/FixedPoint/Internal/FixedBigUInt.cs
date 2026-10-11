namespace LegendaryTools.FixedPoint
{
    /// <summary>Fixed-capacity, allocation-free guard precision for scalar math. Little-endian 32-bit limbs.</summary>
    internal unsafe struct FixedBigUInt
    {
        private fixed uint words[16];
        internal uint Get(int index) { fixed (uint* p = words) return (uint)index < 16 ? p[index] : 0; }
        internal void Set(int index, uint value) { fixed (uint* p = words) if ((uint)index < 16) p[index] = value; }
        internal ulong Low => Get(0) | (ulong)Get(1) << 32;
        internal bool IsZero => BitLength == 0;
        internal int BitLength
        {
            get
            {
                for (int i = 15; i >= 0; i--)
                {
                    uint w = Get(i);
                    if (w == 0) continue;
                    int bits = 0;
                    while (w != 0) { bits++; w >>= 1; }
                    return i * 32 + bits;
                }
                return 0;
            }
        }
        internal bool Bit(int index) => index >= 0 && index < 512 && (Get(index >> 5) & (1U << (index & 31))) != 0;
        internal void SetBit(int index) { if ((uint)index < 512) Set(index >> 5, Get(index >> 5) | 1U << (index & 31)); }
        internal bool AnyBelow(int bits)
        {
            if (bits <= 0) return false;
            if (bits >= 512) return !IsZero;
            int whole = bits >> 5;
            for (int i = 0; i < whole; i++) if (Get(i) != 0) return true;
            return (bits & 31) != 0 && (Get(whole) & ((1U << (bits & 31)) - 1)) != 0;
        }
        internal static FixedBigUInt From(ulong value) { FixedBigUInt r = default; r.Set(0, (uint)value); r.Set(1, (uint)(value >> 32)); return r; }
        internal static FixedBigUInt From(FixedUInt128 value)
        { FixedBigUInt r = From(value.Lo); r.Set(2, (uint)value.Hi); r.Set(3, (uint)(value.Hi >> 32)); return r; }
        internal static FixedBigUInt PowerOfTwo(int bit) { FixedBigUInt r = default; r.SetBit(bit); return r; }
        internal static int Compare(FixedBigUInt a, FixedBigUInt b)
        { for (int i = 15; i >= 0; i--) { uint x = a.Get(i), y = b.Get(i); if (x != y) return x < y ? -1 : 1; } return 0; }
        internal static FixedBigUInt Add(FixedBigUInt a, FixedBigUInt b)
        {
            FixedBigUInt r = default; ulong carry = 0;
            for (int i = 0; i < 16; i++) { ulong n = (ulong)a.Get(i) + b.Get(i) + carry; r.Set(i, (uint)n); carry = n >> 32; }
            return r;
        }
        internal static FixedBigUInt Subtract(FixedBigUInt a, FixedBigUInt b)
        {
            FixedBigUInt r = default; ulong borrow = 0;
            for (int i = 0; i < 16; i++)
            { ulong x = a.Get(i), y = (ulong)b.Get(i) + borrow; r.Set(i, unchecked((uint)(x - y))); borrow = x < y ? 1UL : 0UL; }
            return r;
        }
        internal static FixedBigUInt ShiftLeft(FixedBigUInt a, int bits)
        {
            if (bits < 0) return ShiftRight(a, -bits);
            FixedBigUInt r = default; if (bits >= 512) return r;
            int shift = bits & 31, whole = bits >> 5;
            for (int i = whole; i < 16; i++)
            { uint n = a.Get(i - whole) << shift; if (shift != 0) n |= a.Get(i - whole - 1) >> (32 - shift); r.Set(i, n); }
            return r;
        }
        internal static FixedBigUInt ShiftRight(FixedBigUInt a, int bits)
        {
            if (bits < 0) return ShiftLeft(a, -bits);
            FixedBigUInt r = default; if (bits >= 512) return r;
            int shift = bits & 31, whole = bits >> 5;
            for (int i = 0; i + whole < 16; i++)
            { uint n = a.Get(i + whole) >> shift; if (shift != 0) n |= a.Get(i + whole + 1) << (32 - shift); r.Set(i, n); }
            return r;
        }
        internal static FixedBigUInt Multiply(FixedBigUInt a, FixedBigUInt b, int rightShift = 0)
        {
            uint* product = stackalloc uint[32];
            for (int i = 0; i < 32; i++) product[i] = 0;
            int an = (a.BitLength + 31) >> 5, bn = (b.BitLength + 31) >> 5;
            for (int i = 0; i < an; i++)
            {
                ulong carry = 0;
                for (int j = 0; j < bn; j++)
                {
                    ulong n = (ulong)a.Get(i) * b.Get(j) + product[i + j] + carry;
                    product[i + j] = (uint)n; carry = n >> 32;
                }
                product[i + bn] = (uint)carry;
            }
            FixedBigUInt r = default; int shift = rightShift & 31, whole = rightShift >> 5;
            for (int i = 0; i < 16; i++)
            {
                int p = i + whole;
                uint n = p < 32 ? product[p] >> shift : 0;
                if (shift != 0 && p + 1 < 32) n |= product[p + 1] << (32 - shift);
                r.Set(i, n);
            }
            return r;
        }
        internal static FixedBigUInt Divide(FixedBigUInt a, ulong divisor, out ulong remainder)
        {
            FixedBigUInt r = default; remainder = 0;
            for (int i = 7; i >= 0; i--)
            {
                ulong word = a.Get(i * 2) | (ulong)a.Get(i * 2 + 1) << 32;
                FixedUInt128 q = FixedUInt128.Divide(new FixedUInt128(word, remainder), divisor, out remainder);
                r.Set(i * 2, (uint)q.Lo); r.Set(i * 2 + 1, (uint)(q.Lo >> 32));
            }
            return r;
        }
        internal static FixedBigUInt Divide(FixedBigUInt numerator, FixedBigUInt denominator, int leftShift = 0)
        {
            FixedBigUInt q = default, rem = default;
            if (denominator.IsZero) return q;
            for (int i = numerator.BitLength + leftShift - 1; i >= 0; i--)
            {
                bool carry = rem.Bit(511);
                rem = ShiftLeft(rem, 1);
                if (numerator.Bit(i - leftShift)) rem.Set(0, rem.Get(0) | 1);
                if (carry || Compare(rem, denominator) >= 0)
                { rem = Subtract(rem, denominator); q.SetBit(i); }
            }
            return q;
        }
        internal static FixedBigUInt Mod(FixedBigUInt a, FixedBigUInt divisor)
        {
            FixedBigUInt rem = default;
            if (divisor.IsZero) return rem;
            for (int i = a.BitLength - 1; i >= 0; i--)
            {
                bool carry = rem.Bit(511); rem = ShiftLeft(rem, 1);
                if (a.Bit(i)) rem.Set(0, rem.Get(0) | 1);
                if (carry || Compare(rem, divisor) >= 0) rem = Subtract(rem, divisor);
            }
            return rem;
        }
        internal static FixedBigUInt Sqrt(FixedBigUInt a, int leftShift = 0)
        {
            FixedBigUInt root = default, rem = default;
            int pairs = (a.BitLength + leftShift + 1) / 2;
            for (int i = pairs - 1; i >= 0; i--)
            {
                rem = ShiftLeft(rem, 2);
                uint pair = (a.Bit(i * 2 - leftShift) ? 1U : 0U) | (a.Bit(i * 2 + 1 - leftShift) ? 2U : 0U);
                rem.Set(0, rem.Get(0) | pair);
                FixedBigUInt trial = Add(ShiftLeft(root, 2), From(1));
                root = ShiftLeft(root, 1);
                if (Compare(rem, trial) >= 0) { rem = Subtract(rem, trial); root = Add(root, From(1)); }
            }
            return root;
        }
        internal ulong Modulo(ulong modulus) { Divide(this, modulus, out ulong r); return r; }
    }

    internal struct FixedQ
    {
        internal FixedBigUInt Magnitude;
        internal bool Negative;
        internal FixedQ(FixedBigUInt magnitude, bool negative = false) { Magnitude = magnitude; Negative = negative; }
        internal bool IsZero => Magnitude.IsZero;
        internal FixedQ Negated => new FixedQ(Magnitude, !Negative);
        internal FixedQ Abs => new FixedQ(Magnitude);
        internal static FixedQ One(int fractionBits) => new FixedQ(FixedBigUInt.PowerOfTwo(fractionBits));
        internal static FixedQ From(ulong magnitude, bool negative, ulong scale, int fractionBits) => new FixedQ(
            FixedBigUInt.Divide(FixedBigUInt.ShiftLeft(FixedBigUInt.From(magnitude), fractionBits), scale, out _), negative);
        internal static FixedQ Integer(ulong magnitude, int fractionBits, bool negative = false) => new FixedQ(FixedBigUInt.ShiftLeft(FixedBigUInt.From(magnitude), fractionBits), negative);
        internal static FixedQ Add(FixedQ a, FixedQ b)
        {
            if (a.Negative == b.Negative) return new FixedQ(FixedBigUInt.Add(a.Magnitude, b.Magnitude), a.Negative);
            int comparison = FixedBigUInt.Compare(a.Magnitude, b.Magnitude);
            return comparison < 0 ? new FixedQ(FixedBigUInt.Subtract(b.Magnitude, a.Magnitude), b.Negative) :
                new FixedQ(FixedBigUInt.Subtract(a.Magnitude, b.Magnitude), a.Negative);
        }
        internal static FixedQ Subtract(FixedQ a, FixedQ b) => Add(a, b.Negated);
        internal static FixedQ Multiply(FixedQ a, FixedQ b, int fractionBits) => new FixedQ(FixedBigUInt.Multiply(a.Magnitude, b.Magnitude, fractionBits), a.Negative ^ b.Negative);
        internal static FixedQ Divide(FixedQ a, FixedQ b, int fractionBits) => new FixedQ(FixedBigUInt.Divide(a.Magnitude, b.Magnitude, fractionBits), a.Negative ^ b.Negative);
        internal FixedQ DividedBy(ulong divisor) => new FixedQ(FixedBigUInt.Divide(Magnitude, divisor, out _), Negative);
        internal FixedQ Shifted(int bits) => new FixedQ(bits >= 0 ? FixedBigUInt.ShiftLeft(Magnitude, bits) : FixedBigUInt.ShiftRight(Magnitude, -bits), Negative);
        internal static int Compare(FixedQ a, FixedQ b)
        { if (a.IsZero && b.IsZero) return 0; if (a.Negative != b.Negative) return a.Negative ? -1 : 1; int c = FixedBigUInt.Compare(a.Magnitude, b.Magnitude); return a.Negative ? -c : c; }
    }
}
