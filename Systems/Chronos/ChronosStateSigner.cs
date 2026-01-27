using System;
using System.Security.Cryptography;
using System.Text;

namespace LegendaryTools.Chronos
{
    public static class ChronosStateSigner
    {
        public static string ComputeSignatureBase64(in ChronosSignedState state, byte[] secret)
        {
            byte[] payloadBytes = Encoding.UTF8.GetBytes(BuildCanonicalPayload(state));
            using (HMACSHA256 hmac = new(secret))
            {
                byte[] sig = hmac.ComputeHash(payloadBytes);
                return Convert.ToBase64String(sig);
            }
        }

        public static bool Verify(in ChronosSignedState state, byte[] secret)
        {
            if (string.IsNullOrEmpty(state.SignatureBase64))
                return false;

            string expected;
            try
            {
                expected = ComputeSignatureBase64(state, secret);
            }
            catch
            {
                return false;
            }

            byte[] a;
            byte[] b;

            try
            {
                a = Convert.FromBase64String(expected);
                b = Convert.FromBase64String(state.SignatureBase64);
            }
            catch
            {
                return false;
            }

            if (a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }

        public static string BuildCanonicalPayload(in ChronosSignedState state)
        {
            // Versioned payload:
            // v1: original fields
            // v2: adds failure count + last sync info + last provider
            if (state.Version <= 1)
                return string.Concat(
                    "v=", state.Version, "|",
                    "fs=", state.IsFirstStart ? "1" : "0", "|",
                    "utc=", state.LastRecordedUtcIso ?? string.Empty, "|",
                    "u=", state.LastUnscaledTimeAsDouble.ToString("R"), "|",
                    "n=", state.NonceBase64 ?? string.Empty, "|",
                    "td=", state.TamperDetected ? "1" : "0"
                );

            return string.Concat(
                "v=", state.Version, "|",
                "fs=", state.IsFirstStart ? "1" : "0", "|",
                "utc=", state.LastRecordedUtcIso ?? string.Empty, "|",
                "u=", state.LastUnscaledTimeAsDouble.ToString("R"), "|",
                "n=", state.NonceBase64 ?? string.Empty, "|",
                "td=", state.TamperDetected ? "1" : "0", "|",
                "fc=", state.FailureCount, "|",
                "ls=", state.LastSyncUtcIso ?? string.Empty, "|",
                "lp=", state.LastProviderName ?? string.Empty
            );
        }
    }
}