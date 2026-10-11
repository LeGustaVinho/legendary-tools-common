using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Decimal fixed-point scalar. Configuration belongs to FixedRuntime, never to a value.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public readonly struct UFixed32 : IEquatable<UFixed32>, IComparable<UFixed32>, IComparable, IFormattable, IConvertible
    {
        public readonly uint RawValue;
        public const FixedKind Kind = FixedKind.Unsigned32;
        private UFixed32(uint raw) { RawValue = raw; }
        internal ulong Bits => unchecked((uint)RawValue);
        internal static UFixed32 FromBits(ulong raw) => new UFixed32(unchecked((uint)raw));
        private static FixedFormat Format => new FixedFormat(Kind, FixedRuntime.State);
        private FixedDecoded Decoded => Format.Decode(Bits);

        public static UFixed32 FromRaw(uint raw) => new UFixed32(raw);
        public static UFixed32 Zero => default;
        public static UFixed32 NegativeZero => FromBits(Format.NegativeZero);
        public static UFixed32 One => FromInt(1);
        public static UFixed32 Epsilon => FromRaw(1);
        public static UFixed32 MinValue => FromBits(Format.MinValue);
        public static UFixed32 MaxValue => FromBits(Format.MaxPositive);
        public static UFixed32 NaN => FromBits(Format.NaN);
        public static UFixed32 PositiveInfinity => FromBits(Format.PositiveInfinity);
        public static UFixed32 NegativeInfinity => FromBits(Format.NegativeInfinity);
        public static UFixed32 Pi => FromBits(FixedMathKernel.Constant(FixedMathConstants.Pi, Kind, FixedRuntime.State, out _));
        public static UFixed32 Tau => FromBits(FixedMathKernel.Constant(FixedMathConstants.Tau, Kind, FixedRuntime.State, out _));
        public static UFixed32 E => FromBits(FixedMathKernel.Constant(FixedMathConstants.E, Kind, FixedRuntime.State, out _));
        public static int DecimalPlaces => Format.DecimalPlaces;
        public static ulong Scale => Format.Scale;

        public static bool IsNaN(UFixed32 value) => value.Decoded.IsNaN;
        public static bool IsFinite(UFixed32 value) => value.Decoded.IsFinite;
        public static bool IsInfinity(UFixed32 value) => !value.Decoded.IsFinite && !value.Decoded.IsNaN;
        public static bool IsPositiveInfinity(UFixed32 value) => value.Decoded.Class == FixedClass.PositiveInfinity;
        public static bool IsNegativeInfinity(UFixed32 value) => value.Decoded.Class == FixedClass.NegativeInfinity;
        public static bool IsNegative(UFixed32 value) => value.Decoded.Negative;
        public static bool IsZero(UFixed32 value) => value.Decoded.IsZero;
        public static bool IsInteger(UFixed32 value) => FixedRuntime.IsInitialized && value.Decoded.IsFinite && value.Decoded.Magnitude % Scale == 0;
        // Fixed-point has no subnormals. Normal means finite and nonzero.
        public static bool IsNormal(UFixed32 value) => value.Decoded.IsFinite && !value.Decoded.IsZero;
        public static bool IsSubnormal(UFixed32 value) => false;

        public static UFixed32 FromInt(long value) => FromInt(value, out _);
        public static UFixed32 FromInt(long value, out FixedStatus status) => FromBits(FixedKernel.FromInteger(
            value < 0 ? unchecked(0UL - (ulong)value) : (ulong)value, value < 0, Kind, FixedRuntime.State, out status));
        public static UFixed32 FromUInt(ulong value) => FromUInt(value, out _);
        public static UFixed32 FromUInt(ulong value, out FixedStatus status) => FromBits(FixedKernel.FromInteger(value, false, Kind, FixedRuntime.State, out status));
        public static UFixed32 FromRatio(long numerator, long denominator) => FromRatio(numerator, denominator, out _);
        public static UFixed32 FromRatio(long numerator, long denominator, out FixedStatus status) => FromBits(FixedKernel.FromRatio(
            numerator < 0 ? unchecked(0UL - (ulong)numerator) : (ulong)numerator,
            denominator < 0 ? unchecked(0UL - (ulong)denominator) : (ulong)denominator,
            (numerator < 0) ^ (denominator < 0), Kind, FixedRuntime.State, out status));
        public static UFixed32 FromUnsignedRatio(ulong numerator, ulong denominator) => FromUnsignedRatio(numerator, denominator, out _);
        public static UFixed32 FromUnsignedRatio(ulong numerator, ulong denominator, out FixedStatus status) => FromBits(FixedKernel.FromRatio(numerator, denominator, false, Kind, FixedRuntime.State, out status));
        public static UFixed32 FromSingle(float value) => FromSingle(value, out _);
        public static UFixed32 FromSingle(float value, out FixedStatus status) => FromBits(FixedIeee.From(value, Kind, FixedRuntime.State, out status));
        public static UFixed32 FromDouble(double value) => FromDouble(value, out _);
        public static UFixed32 FromDouble(double value, out FixedStatus status) => FromBits(FixedIeee.From(value, Kind, FixedRuntime.State, out status));
        public static UFixed32 FromDecimal(decimal value) => FromDecimal(value, out _);
        public static UFixed32 FromDecimal(decimal value, out FixedStatus status) => FromBits(FixedManaged.FromDecimal(value, Kind, FixedRuntime.State, out status));

        public float ToSingle() => FixedIeee.ToSingle(Bits, Kind, FixedRuntime.State);
        public double ToDouble() => FixedIeee.ToDouble(Bits, Kind, FixedRuntime.State);
        public decimal ToDecimal() => FixedManaged.ToDecimal(Bits, Kind, FixedRuntime.State);
        public long ToInt64() => ToInt64(out _);
        public long ToInt64(out FixedStatus status)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(Bits, Kind, FixedRuntime.State, false, 64, out bool negative, out status);
            return unchecked((long)(negative ? 0UL - magnitude : magnitude));
        }
        public ulong ToUInt64() => ToUInt64(out _);
        public ulong ToUInt64(out FixedStatus status) => FixedKernel.IntegralMagnitude(Bits, Kind, FixedRuntime.State, true, 64, out _, out status);
        public bool TryToInt64(out long result, out FixedStatus status) { result = ToInt64(out status); return status.IsSuccess(); }
        public bool TryToUInt64(out ulong result, out FixedStatus status) { result = ToUInt64(out status); return status.IsSuccess(); }

        public static UFixed32 operator +(UFixed32 value) => value;
        public static UFixed32 operator -(UFixed32 value) => Negate(value, out _);
        public static UFixed32 Negate(UFixed32 value, out FixedStatus status) => FromBits(FixedKernel.Negate(value.Bits, Kind, FixedRuntime.State, out status));
        public static UFixed32 operator ++(UFixed32 value) => value + One;
        public static UFixed32 operator --(UFixed32 value) => value - One;

        public static UFixed32 operator +(UFixed32 a, UFixed32 b) => Add(a, b, out _);
        public static UFixed32 Add(UFixed32 a, UFixed32 b, out FixedStatus status) => FromBits(FixedKernel.Binary(a.Bits, b.Bits, Kind, FixedRuntime.State, FixedBinary.Add, out status));
        public static bool TryAdd(UFixed32 a, UFixed32 b, out UFixed32 result, out FixedStatus status)
        { result = Add(a, b, out status); return status.IsSuccess(); }
        public static UFixed32 AddChecked(UFixed32 a, UFixed32 b)
        { UFixed32 result = Add(a, b, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static UFixed32 operator -(UFixed32 a, UFixed32 b) => Subtract(a, b, out _);
        public static UFixed32 Subtract(UFixed32 a, UFixed32 b, out FixedStatus status) => FromBits(FixedKernel.Binary(a.Bits, b.Bits, Kind, FixedRuntime.State, FixedBinary.Subtract, out status));
        public static bool TrySubtract(UFixed32 a, UFixed32 b, out UFixed32 result, out FixedStatus status)
        { result = Subtract(a, b, out status); return status.IsSuccess(); }
        public static UFixed32 SubtractChecked(UFixed32 a, UFixed32 b)
        { UFixed32 result = Subtract(a, b, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static UFixed32 operator *(UFixed32 a, UFixed32 b) => Multiply(a, b, out _);
        public static UFixed32 Multiply(UFixed32 a, UFixed32 b, out FixedStatus status) => FromBits(FixedKernel.Binary(a.Bits, b.Bits, Kind, FixedRuntime.State, FixedBinary.Multiply, out status));
        public static bool TryMultiply(UFixed32 a, UFixed32 b, out UFixed32 result, out FixedStatus status)
        { result = Multiply(a, b, out status); return status.IsSuccess(); }
        public static UFixed32 MultiplyChecked(UFixed32 a, UFixed32 b)
        { UFixed32 result = Multiply(a, b, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static UFixed32 operator /(UFixed32 a, UFixed32 b) => Divide(a, b, out _);
        public static UFixed32 Divide(UFixed32 a, UFixed32 b, out FixedStatus status) => FromBits(FixedKernel.Binary(a.Bits, b.Bits, Kind, FixedRuntime.State, FixedBinary.Divide, out status));
        public static bool TryDivide(UFixed32 a, UFixed32 b, out UFixed32 result, out FixedStatus status)
        { result = Divide(a, b, out status); return status.IsSuccess(); }
        public static UFixed32 DivideChecked(UFixed32 a, UFixed32 b)
        { UFixed32 result = Divide(a, b, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static UFixed32 operator %(UFixed32 a, UFixed32 b) => Remainder(a, b, out _);
        public static UFixed32 Remainder(UFixed32 a, UFixed32 b, out FixedStatus status) => FromBits(FixedKernel.Binary(a.Bits, b.Bits, Kind, FixedRuntime.State, FixedBinary.Remainder, out status));
        public static bool TryRemainder(UFixed32 a, UFixed32 b, out UFixed32 result, out FixedStatus status)
        { result = Remainder(a, b, out status); return status.IsSuccess(); }
        public static UFixed32 RemainderChecked(UFixed32 a, UFixed32 b)
        { UFixed32 result = Remainder(a, b, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromInt(long value, out UFixed32 result, out FixedStatus status)
        { result = FromInt(value, out status); return status.IsSuccess(); }
        public static UFixed32 FromIntChecked(long value)
        { UFixed32 result = FromInt(value, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromUInt(ulong value, out UFixed32 result, out FixedStatus status)
        { result = FromUInt(value, out status); return status.IsSuccess(); }
        public static UFixed32 FromUIntChecked(ulong value)
        { UFixed32 result = FromUInt(value, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromSingle(float value, out UFixed32 result, out FixedStatus status)
        { result = FromSingle(value, out status); return status.IsSuccess(); }
        public static UFixed32 FromSingleChecked(float value)
        { UFixed32 result = FromSingle(value, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromDouble(double value, out UFixed32 result, out FixedStatus status)
        { result = FromDouble(value, out status); return status.IsSuccess(); }
        public static UFixed32 FromDoubleChecked(double value)
        { UFixed32 result = FromDouble(value, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromDecimal(decimal value, out UFixed32 result, out FixedStatus status)
        { result = FromDecimal(value, out status); return status.IsSuccess(); }
        public static UFixed32 FromDecimalChecked(decimal value)
        { UFixed32 result = FromDecimal(value, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromRatio(long numerator, long denominator, out UFixed32 result, out FixedStatus status)
        { result = FromRatio(numerator, denominator, out status); return status.IsSuccess(); }
        public static UFixed32 FromRatioChecked(long numerator, long denominator)
        { UFixed32 result = FromRatio(numerator, denominator, out FixedStatus status); status.ThrowIfFailure(); return result; }
        public static bool TryFromUnsignedRatio(ulong numerator, ulong denominator, out UFixed32 result, out FixedStatus status)
        { result = FromUnsignedRatio(numerator, denominator, out status); return status.IsSuccess(); }
        public static UFixed32 FromUnsignedRatioChecked(ulong numerator, ulong denominator)
        { UFixed32 result = FromUnsignedRatio(numerator, denominator, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static bool operator ==(UFixed32 a, UFixed32 b) => FixedKernel.Equal(a.Bits, b.Bits, Kind, FixedRuntime.State, false);
        public static bool operator !=(UFixed32 a, UFixed32 b) => !(a == b);
        public static bool operator <(UFixed32 a, UFixed32 b) => !IsNaN(a) && !IsNaN(b) && a.CompareTo(b) < 0;
        public static bool operator >(UFixed32 a, UFixed32 b) => !IsNaN(a) && !IsNaN(b) && a.CompareTo(b) > 0;
        public static bool operator <=(UFixed32 a, UFixed32 b) => !IsNaN(a) && !IsNaN(b) && a.CompareTo(b) <= 0;
        public static bool operator >=(UFixed32 a, UFixed32 b) => !IsNaN(a) && !IsNaN(b) && a.CompareTo(b) >= 0;
        public bool Equals(UFixed32 other) => FixedKernel.Equal(Bits, other.Bits, Kind, FixedRuntime.State, true);
        public override bool Equals(object obj) => obj is UFixed32 other && Equals(other);
        public override int GetHashCode() => FixedKernel.Hash(Bits, Kind, FixedRuntime.State);
        public int CompareTo(UFixed32 other) => FixedKernel.Compare(Bits, other.Bits, Kind, FixedRuntime.State);
        int IComparable.CompareTo(object obj) => obj == null ? 1 : obj is UFixed32 other ? CompareTo(other) : throw new ArgumentException("Incompatible numeric type.", nameof(obj));

        public override string ToString() => FixedManaged.Format(Bits, Kind, FixedRuntime.State, null, CultureInfo.InvariantCulture);
        public string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);
        public string ToString(IFormatProvider provider) => ToString(null, provider);
        public string ToString(string format, IFormatProvider provider) => FixedManaged.Format(Bits, Kind, FixedRuntime.State, format, provider);
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null)
        {
            string text = ToString(format.IsEmpty ? null : format.ToString(), provider);
            if (text.Length > destination.Length) { charsWritten = 0; return false; }
            text.AsSpan().CopyTo(destination); charsWritten = text.Length; return true;
        }
        public static UFixed32 Parse(string text) => Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        public static UFixed32 Parse(string text, IFormatProvider provider) => Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, provider);
        public static UFixed32 Parse(string text, NumberStyles styles, IFormatProvider provider = null) => FromBits(FixedManaged.Parse(text, styles, provider, Kind, FixedRuntime.State));
        public static UFixed32 Parse(ReadOnlySpan<char> text, IFormatProvider provider = null) => Parse(text.ToString(), provider);
        public static bool TryParse(string text, out UFixed32 result) => TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result, out _);
        public static bool TryParse(string text, IFormatProvider provider, out UFixed32 result) => TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result, out _);
        public static bool TryParse(string text, NumberStyles styles, IFormatProvider provider, out UFixed32 result) => TryParse(text, styles, provider, out result, out _);
        public static bool TryParse(string text, NumberStyles styles, IFormatProvider provider, out UFixed32 result, out FixedStatus status)
        {
            bool success = FixedManaged.TryParse(text, styles, provider, Kind, FixedRuntime.State, out ulong bits, out status);
            result = FromBits(bits); return success;
        }
        public static bool TryParse(ReadOnlySpan<char> text, IFormatProvider provider, out UFixed32 result) => TryParse(text.ToString(), provider, out result);
        public static UFixed32 ParseChecked(string text)
        { TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out UFixed32 result, out FixedStatus status); status.ThrowIfFailure(); return result; }

        public static explicit operator UFixed32(float value) => FromSingle(value);
        public static explicit operator UFixed32(double value) => FromDouble(value);
        public static explicit operator UFixed32(decimal value) => FromDecimal(value);
        public static explicit operator float(UFixed32 value) => value.ToSingle();
        public static explicit operator double(UFixed32 value) => value.ToDouble();
        public static explicit operator decimal(UFixed32 value) => value.ToDecimal();
        public static explicit operator UFixed32(sbyte value) => FromInt(value);
        public static explicit operator sbyte(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, false, 8, out bool negative, out _);
            return unchecked((sbyte)(negative ? 0UL - magnitude : magnitude));
        }
        public static explicit operator UFixed32(byte value) => FromUInt(value);
        public static explicit operator byte(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, true, 8, out bool negative, out _);
            return unchecked((byte)magnitude);
        }
        public static explicit operator UFixed32(short value) => FromInt(value);
        public static explicit operator short(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, false, 16, out bool negative, out _);
            return unchecked((short)(negative ? 0UL - magnitude : magnitude));
        }
        public static explicit operator UFixed32(ushort value) => FromUInt(value);
        public static explicit operator ushort(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, true, 16, out bool negative, out _);
            return unchecked((ushort)magnitude);
        }
        public static explicit operator UFixed32(int value) => FromInt(value);
        public static explicit operator int(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, false, 32, out bool negative, out _);
            return unchecked((int)(negative ? 0UL - magnitude : magnitude));
        }
        public static implicit operator UFixed32(uint value) => FromUInt(value);
        public static explicit operator uint(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, true, 32, out bool negative, out _);
            return unchecked((uint)magnitude);
        }
        public static explicit operator UFixed32(long value) => FromInt(value);
        public static explicit operator long(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, false, 64, out bool negative, out _);
            return unchecked((long)(negative ? 0UL - magnitude : magnitude));
        }
        public static explicit operator UFixed32(ulong value) => FromUInt(value);
        public static explicit operator ulong(UFixed32 value)
        {
            ulong magnitude = FixedKernel.IntegralMagnitude(value.Bits, Kind, FixedRuntime.State, true, 64, out bool negative, out _);
            return unchecked((ulong)magnitude);
        }
        public static explicit operator UFixed32(Fixed32 value) => FromBits(FixedKernel.Convert(value.Bits, FixedKind.Signed32, Kind, FixedRuntime.State, out _));
        public Fixed32 ToFixed32(out FixedStatus status) => Fixed32.FromBits(FixedKernel.Convert(Bits, Kind, FixedKind.Signed32, FixedRuntime.State, out status));
        public bool TryToFixed32(out Fixed32 result, out FixedStatus status) { result = ToFixed32(out status); return status.IsSuccess(); }
        public static explicit operator UFixed32(Fixed64 value) => FromBits(FixedKernel.Convert(value.Bits, FixedKind.Signed64, Kind, FixedRuntime.State, out _));
        public Fixed64 ToFixed64(out FixedStatus status) => Fixed64.FromBits(FixedKernel.Convert(Bits, Kind, FixedKind.Signed64, FixedRuntime.State, out status));
        public bool TryToFixed64(out Fixed64 result, out FixedStatus status) { result = ToFixed64(out status); return status.IsSuccess(); }
        public static explicit operator UFixed32(UFixed64 value) => FromBits(FixedKernel.Convert(value.Bits, FixedKind.Unsigned64, Kind, FixedRuntime.State, out _));
        public UFixed64 ToUFixed64(out FixedStatus status) => UFixed64.FromBits(FixedKernel.Convert(Bits, Kind, FixedKind.Unsigned64, FixedRuntime.State, out status));
        public bool TryToUFixed64(out UFixed64 result, out FixedStatus status) { result = ToUFixed64(out status); return status.IsSuccess(); }

        TypeCode IConvertible.GetTypeCode() => TypeCode.Object;
        bool IConvertible.ToBoolean(IFormatProvider provider) => (bool)FixedManaged.ConvertTo(Bits, Kind, typeof(bool), FixedRuntime.State, provider);
        char IConvertible.ToChar(IFormatProvider provider) => throw new InvalidCastException();
        DateTime IConvertible.ToDateTime(IFormatProvider provider) => throw new InvalidCastException();
        decimal IConvertible.ToDecimal(IFormatProvider provider) => ToDecimal();
        float IConvertible.ToSingle(IFormatProvider provider) => ToSingle();
        double IConvertible.ToDouble(IFormatProvider provider) => ToDouble();
        string IConvertible.ToString(IFormatProvider provider) => ToString(provider);
        object IConvertible.ToType(Type destination, IFormatProvider provider)
        {
            if (destination == typeof(UFixed32)) return this;
            if (destination == typeof(Fixed32)) return ToFixed32(out _);
            if (destination == typeof(Fixed64)) return ToFixed64(out _);
            if (destination == typeof(UFixed64)) return ToUFixed64(out _);
            return FixedManaged.ConvertTo(Bits, Kind, destination, FixedRuntime.State, provider);
        }
        sbyte IConvertible.ToSByte(IFormatProvider provider) => (sbyte)FixedManaged.ConvertTo(Bits, Kind, typeof(sbyte), FixedRuntime.State, provider);
        byte IConvertible.ToByte(IFormatProvider provider) => (byte)FixedManaged.ConvertTo(Bits, Kind, typeof(byte), FixedRuntime.State, provider);
        short IConvertible.ToInt16(IFormatProvider provider) => (short)FixedManaged.ConvertTo(Bits, Kind, typeof(short), FixedRuntime.State, provider);
        ushort IConvertible.ToUInt16(IFormatProvider provider) => (ushort)FixedManaged.ConvertTo(Bits, Kind, typeof(ushort), FixedRuntime.State, provider);
        int IConvertible.ToInt32(IFormatProvider provider) => (int)FixedManaged.ConvertTo(Bits, Kind, typeof(int), FixedRuntime.State, provider);
        uint IConvertible.ToUInt32(IFormatProvider provider) => (uint)FixedManaged.ConvertTo(Bits, Kind, typeof(uint), FixedRuntime.State, provider);
        long IConvertible.ToInt64(IFormatProvider provider) => (long)FixedManaged.ConvertTo(Bits, Kind, typeof(long), FixedRuntime.State, provider);
        ulong IConvertible.ToUInt64(IFormatProvider provider) => (ulong)FixedManaged.ConvertTo(Bits, Kind, typeof(ulong), FixedRuntime.State, provider);
    }
}
