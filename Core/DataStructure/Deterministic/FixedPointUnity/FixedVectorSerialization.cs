using System.Globalization;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedVectorSerialization
    {
        // Zero is the unassigned/default representation. Precision 0..18 is encoded as 1..19.
        internal static int Precision(byte code) => code == 0 ? FixedSettings.Default.DecimalPlaces64 : code - 1;
        internal static FixedRuntimeState State(int precision)
        {
            FixedSettings s = FixedRuntime.IsInitialized ? FixedRuntime.Settings : FixedSettings.Default;
            return new FixedRuntimeState(new FixedSettings(s.DecimalPlaces32, precision, s.Rounding, s.Overflow, s.ZeroDivision, s.UnsignedNegative, s.MathProfile, s.ArithmeticMode));
        }
        internal static bool TryRescale(long raw, int from, int to, out long result, out FixedStatus status)
        {
            result = raw;
            if (from < 0 || from > 18 || to < 0 || to > 18) { status = FixedStatus.InvalidConfiguration; return false; }
            result = unchecked((long)FixedNetworkKernel.Rescale(unchecked((ulong)raw), FixedKind.Signed64, State(from), State(to), out status));
            return status.IsSuccess();
        }
        internal static string Format(long raw, int precision) => precision < 0 || precision > 18 ? "<precisão inválida>" :
            FixedManaged.Format(unchecked((ulong)raw), FixedKind.Signed64, State(precision), "R", CultureInfo.InvariantCulture);
        internal static bool TryParse(string text, int precision, out long result, out FixedStatus status)
        {
            result = 0;
            if (precision < 0 || precision > 18) { status = FixedStatus.InvalidConfiguration; return false; }
            bool success = FixedManaged.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, FixedKind.Signed64, State(precision), out ulong bits, out status);
            result = unchecked((long)bits); return success;
        }
    }
}
