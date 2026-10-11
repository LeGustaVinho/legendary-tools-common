using System;

namespace LegendaryTools.FixedPoint
{
    public enum FixedKind : byte { Signed32, Unsigned32, Signed64, Unsigned64 }
    public enum FixedRounding : byte { ToEven, AwayFromZero, TowardZero, TowardPositiveInfinity, TowardNegativeInfinity }
    public enum FixedOverflow : byte { Infinity, Saturate, NaN, WrapFinite }
    public enum FixedZeroDivision : byte { Infinity, Saturate, NaN }
    public enum FixedUnsignedNegative : byte { NaN, SaturateZero, WrapFinite }
    public enum FixedMathProfile : byte { Accurate, Fast }
    public enum FixedArithmeticMode : byte { Auto, Portable, Intrinsic }
    public enum FixedExecutionMode : byte { Auto, Managed, Burst }

    [Flags]
    public enum FixedStatus : uint
    {
        None = 0,
        Overflow = 1 << 0,
        DivisionByZero = 1 << 1,
        InvalidDomain = 1 << 2,
        NegativeResult = 1 << 3,
        Uninitialized = 1 << 4,
        InvalidConfiguration = 1 << 5,
        ConfigurationFrozen = 1 << 6,
        BufferTooSmall = 1 << 7,
        InvalidEncoding = 1 << 8,
        ProfileMismatch = 1 << 9,
        OutOfRange = 1 << 10,
        BackendUnavailable = 1 << 11,
        Inexact = 1 << 30
    }

    public static class FixedStatusExtensions
    {
        /// <summary>Quantization alone is successful; all other reported conditions are failures.</summary>
        public static bool IsSuccess(this FixedStatus status) => (status & ~FixedStatus.Inexact) == 0;

        public static void ThrowIfFailure(this FixedStatus status)
        {
            if (status.IsSuccess()) return;
            if ((status & FixedStatus.DivisionByZero) != 0) throw new DivideByZeroException();
            if ((status & (FixedStatus.Overflow | FixedStatus.NegativeResult | FixedStatus.OutOfRange)) != 0)
                throw new OverflowException("The fixed-point result is outside the destination's finite interval: " + status);
            if ((status & (FixedStatus.Uninitialized | FixedStatus.ConfigurationFrozen | FixedStatus.InvalidConfiguration)) != 0)
                throw new InvalidOperationException("The fixed-point runtime is not configured for this operation: " + status);
            throw new ArithmeticException("The fixed-point operation failed: " + status);
        }
    }

    /// <summary>One numeric profile for an application domain. Signedness does not change a family's scale.</summary>
    [Serializable]
    public readonly struct FixedSettings : IEquatable<FixedSettings>
    {
        public const uint NumericVersion = 1;
        public readonly int DecimalPlaces32;
        public readonly int DecimalPlaces64;
        public readonly FixedRounding Rounding;
        public readonly FixedOverflow Overflow;
        public readonly FixedZeroDivision ZeroDivision;
        public readonly FixedUnsignedNegative UnsignedNegative;
        public readonly FixedMathProfile MathProfile;
        public readonly FixedArithmeticMode ArithmeticMode;

        public static FixedSettings Default => new FixedSettings(3, 3);

        public FixedSettings(int decimalPlaces32 = 3, int decimalPlaces64 = 3,
            FixedRounding rounding = FixedRounding.ToEven,
            FixedOverflow overflow = FixedOverflow.Infinity,
            FixedZeroDivision zeroDivision = FixedZeroDivision.Infinity,
            FixedUnsignedNegative unsignedNegative = FixedUnsignedNegative.NaN,
            FixedMathProfile mathProfile = FixedMathProfile.Accurate,
            FixedArithmeticMode arithmeticMode = FixedArithmeticMode.Auto)
        {
            DecimalPlaces32 = decimalPlaces32;
            DecimalPlaces64 = decimalPlaces64;
            Rounding = rounding;
            Overflow = overflow;
            ZeroDivision = zeroDivision;
            UnsignedNegative = unsignedNegative;
            MathProfile = mathProfile;
            ArithmeticMode = arithmeticMode;
        }

        public bool IsValid => DecimalPlaces32 >= 0 && DecimalPlaces32 <= 9 &&
            DecimalPlaces64 >= 0 && DecimalPlaces64 <= 18 &&
            Rounding <= FixedRounding.TowardNegativeInfinity && Overflow <= FixedOverflow.WrapFinite &&
            ZeroDivision <= FixedZeroDivision.NaN && UnsignedNegative <= FixedUnsignedNegative.WrapFinite &&
            MathProfile <= FixedMathProfile.Fast && ArithmeticMode <= FixedArithmeticMode.Intrinsic;

        public ulong NumericFingerprint
        {
            get
            {
                // Execution/arithmetic backends are deliberately excluded: their numeric results are identical.
                ulong hash = 14695981039346656037UL;
                hash = Mix(hash, NumericVersion);
                hash = Mix(hash, (uint)DecimalPlaces32);
                hash = Mix(hash, (uint)DecimalPlaces64);
                hash = Mix(hash, (uint)Rounding);
                hash = Mix(hash, (uint)Overflow);
                hash = Mix(hash, (uint)ZeroDivision);
                hash = Mix(hash, (uint)UnsignedNegative);
                return Mix(hash, (uint)MathProfile);
            }
        }

        private static ulong Mix(ulong hash, uint value)
        {
            for (int i = 0; i < 4; i++) { hash = unchecked((hash ^ (byte)value) * 1099511628211UL); value >>= 8; }
            return hash;
        }

        public bool NumericEquals(FixedSettings other) => DecimalPlaces32 == other.DecimalPlaces32 &&
            DecimalPlaces64 == other.DecimalPlaces64 && Rounding == other.Rounding && Overflow == other.Overflow &&
            ZeroDivision == other.ZeroDivision && UnsignedNegative == other.UnsignedNegative && MathProfile == other.MathProfile;
        public bool Equals(FixedSettings other) => NumericEquals(other) && ArithmeticMode == other.ArithmeticMode;
        public override bool Equals(object obj) => obj is FixedSettings other && Equals(other);
        public override int GetHashCode() => unchecked((int)(NumericFingerprint ^ (NumericFingerprint >> 32))) ^ (int)ArithmeticMode;
        public static bool operator ==(FixedSettings a, FixedSettings b) => a.Equals(b);
        public static bool operator !=(FixedSettings a, FixedSettings b) => !a.Equals(b);
    }
}
