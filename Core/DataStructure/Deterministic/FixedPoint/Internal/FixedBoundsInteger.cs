namespace LegendaryTools.FixedPoint
{
    /// <summary>Signed intermediates for bounds construction and mutation, which need at most 66 bits.</summary>
    internal readonly struct FixedBoundsInteger
    {
        internal readonly FixedUInt128 Magnitude;
        internal readonly bool Negative;
        internal FixedBoundsInteger(FixedUInt128 magnitude, bool negative = false)
        { Magnitude = magnitude; Negative = negative; }
        internal bool IsZero => Magnitude.IsZero;
        internal FixedBoundsInteger Negated => new FixedBoundsInteger(Magnitude, !Negative);
        internal static FixedBoundsInteger Add(FixedBoundsInteger a, FixedBoundsInteger b)
        {
            if (a.Negative == b.Negative)
                return new FixedBoundsInteger(FixedUInt128.Add(a.Magnitude, b.Magnitude, out _), a.Negative);
            return FixedUInt128.Compare(a.Magnitude, b.Magnitude) < 0
                ? new FixedBoundsInteger(FixedUInt128.Subtract(b.Magnitude, a.Magnitude), b.Negative)
                : new FixedBoundsInteger(FixedUInt128.Subtract(a.Magnitude, b.Magnitude), a.Negative);
        }
        internal static FixedBoundsInteger Subtract(FixedBoundsInteger a, FixedBoundsInteger b) => Add(a, b.Negated);
        internal static int Compare(FixedBoundsInteger a, FixedBoundsInteger b)
        {
            if (a.IsZero && b.IsZero) return 0;
            if (a.Negative != b.Negative) return a.Negative ? -1 : 1;
            int comparison = FixedUInt128.Compare(a.Magnitude, b.Magnitude);
            return a.Negative ? -comparison : comparison;
        }
        internal static ulong Round(FixedBoundsInteger numerator, ulong denominator, FixedRuntimeState state, ref FixedStatus status)
        {
            var quotient = FixedUInt128.RoundedDivide(numerator.Magnitude, denominator, numerator.Negative,
                state.Settings.Rounding, out bool inexact);
            if (inexact) status |= FixedStatus.Inexact;
            // Keep the sign of an exact or rounded zero, just as the wide ratio kernel does.
            return FixedKernel.Pack(quotient, numerator.Negative, new FixedFormat(FixedKind.Signed64, state), state.Settings, ref status);
        }
    }
}
