using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Opt-in managed authoring validation in Mono/IL2CPP; no numeric job calls these APIs.</summary>
    internal static class FixedManagedPlayerValidation
    {
        [Serializable] private sealed class Report
        {
            public bool Passed;
            public int Cases;
            public ulong Checksum = 14695981039346656037UL;
            public string Platform, Error;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, "--fixed-point-managed-validation") < 0) return;
            string path = Path.Combine(Application.persistentDataPath, "fixed-point-managed-validation.json");
            const string prefix = "--fixed-point-managed-report=";
            foreach (string arg in arguments) if (arg.StartsWith(prefix, StringComparison.Ordinal)) path = arg.Substring(prefix.Length);
            var report = new Report { Platform = Application.platform.ToString() };
            try
            {
                foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
                {
                    var state = new FixedRuntimeState(FixedSettings.Default); var f = new FixedFormat(kind, state);
                    var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
                    nfi.CurrencySymbol = "CUR"; nfi.CurrencyDecimalSeparator = ":"; nfi.CurrencyGroupSeparator = "_";
                    Check(report, "12:34", NumberStyles.Currency, nfi, kind, state, 12340, FixedStatus.None);
                    Check(report, "CUR(12:34)", NumberStyles.Currency, nfi, kind, state,
                        f.Unsigned ? f.NaN : f.Finite(12340, true), f.Unsigned ? FixedStatus.NegativeResult : FixedStatus.None);
                    Check(report, "(12:34)CUR", NumberStyles.Currency, nfi, kind, state,
                        f.Unsigned ? f.NaN : f.Finite(12340, true), f.Unsigned ? FixedStatus.NegativeResult : FixedStatus.None);
                    Check(report, "- CUR12", NumberStyles.Currency, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "CUR12.34", NumberStyles.Currency, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "12.34CUR", NumberStyles.Currency, nfi, kind, state, 12340, FixedStatus.None);
                    Check(report, "12\0\0", NumberStyles.None, nfi, kind, state, 12000, FixedStatus.None);
                    Check(report, "12\0x", NumberStyles.Any, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "\u2003NaN\u2003", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "+nan", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "-NaN", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "NaN\0", NumberStyles.Any, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "+infinity", NumberStyles.None, nfi, kind, state, f.PositiveInfinity, FixedStatus.None);
                    Check(report, "-Infinity", NumberStyles.None, nfi, kind, state, f.NegativeInfinity, FixedStatus.None);
                    Check(report, "-0", NumberStyles.Float, nfi, kind, state, f.NegativeZero, FixedStatus.None);
                    nfi.NumberDecimalSeparator = "E"; nfi.NumberGroupSeparator = "grp";
                    nfi.CurrencyDecimalSeparator = "dot"; nfi.CurrencyGroupSeparator = "group";
                    Check(report, "1E2", NumberStyles.Any, nfi, kind, state, 1200, FixedStatus.None);
                    Check(report, "1E2CUR", NumberStyles.Any, nfi, kind, state, 1200, FixedStatus.None);
                    Check(report, "CUR1E2", NumberStyles.Any, nfi, kind, state, 100000, FixedStatus.None);
                    Check(report, "1grp234", NumberStyles.Number, nfi, kind, state, 1234000, FixedStatus.None);
                    nfi.PositiveSign = "pos"; nfi.NegativeSign = "neg";
                    nfi.NaNSymbol = "undefined"; nfi.PositiveInfinitySymbol = "infinite"; nfi.NegativeInfinitySymbol = "below";
                    Check(report, "POSundefined", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "NEGundefined", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "POSINFINITE", NumberStyles.None, nfi, kind, state, f.PositiveInfinity, FixedStatus.None);
                    Check(report, "neginfinite", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "BELOW", NumberStyles.None, nfi, kind, state, f.NegativeInfinity, FixedStatus.None);
                    nfi.NaNSymbol = nfi.PositiveInfinitySymbol = nfi.NegativeInfinitySymbol = "same";
                    Check(report, "same", NumberStyles.None, nfi, kind, state, f.PositiveInfinity, FixedStatus.None);
                    Check(report, "possame", NumberStyles.None, nfi, kind, state, f.PositiveInfinity, FixedStatus.None);
                    Check(report, "negsame", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    nfi.PositiveInfinitySymbol = "distinct";
                    Check(report, "same", NumberStyles.None, nfi, kind, state, f.NegativeInfinity, FixedStatus.None);
                    Check(report, "negdistinct", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    nfi.NaNSymbol = "12";
                    Check(report, "12", NumberStyles.None, nfi, kind, state, 12000, FixedStatus.None);
                    nfi.PositiveSign = "";
                    Check(report, "neg12", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    nfi.PositiveSign = "pos"; nfi.NegativeSign = "\u2212";
                    nfi.NaNSymbol = "undefined"; nfi.PositiveInfinitySymbol = "infinite"; nfi.NegativeInfinitySymbol = "below";
                    Check(report, "-undefined", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    Check(report, "\u2212UNDEFINED", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.None);
                    Check(report, "\u2212infinite", NumberStyles.None, nfi, kind, state, f.NaN, FixedStatus.InvalidEncoding);
                    for (int p = 0; p <= (kind < FixedKind.Signed64 ? 9 : 18); p++)
                    {
                        var roundTripState = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p));
                        var format = new FixedFormat(kind, roundTripState);
                        foreach (ulong raw in new[] { 0UL, format.NegativeZero, format.NaN, format.PositiveInfinity,
                            format.NegativeInfinity, 1UL, format.Scale, format.MaxPositive, format.MinValue, format.MaxPositive - 1 })
                        {
                            string text = FixedManaged.Format(raw, kind, roundTripState, "R", CultureInfo.InvariantCulture);
                            Check(report, text, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, kind, roundTripState, raw, FixedStatus.None);
                        }
                    }
                }
                report.Passed = true;
            }
            catch (Exception exception) { report.Error = exception.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log(JsonUtility.ToJson(report)); Application.Quit(report.Passed ? 0 : 1);
        }

        private static void Check(Report report, string text, NumberStyles styles, NumberFormatInfo nfi,
            FixedKind kind, FixedRuntimeState state, ulong expected, FixedStatus expectedStatus)
        {
            bool success = FixedManaged.TryParse(text, styles, nfi, kind, state, out ulong actual, out FixedStatus status);
            if (actual != expected || status != expectedStatus || success != expectedStatus.IsSuccess())
                throw new InvalidOperationException(kind + " [" + text + "]: raw=" + actual + " status=" + status);
            report.Cases++;
            report.Checksum = unchecked((report.Checksum ^ actual) * 1099511628211UL);
            report.Checksum = unchecked((report.Checksum ^ (uint)status) * 1099511628211UL);
            report.Checksum = unchecked((report.Checksum ^ (success ? 1UL : 0UL)) * 1099511628211UL);
        }
    }
}
