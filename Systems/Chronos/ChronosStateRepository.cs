using System;
using System.Globalization;
using System.Security.Cryptography;
using LegendaryTools.Persistence;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Loads/saves Chronos state + secret from IPersistence and signs state to detect tampering.
    /// </summary>
    public sealed class ChronosStateRepository
    {
        private const int CurrentStateVersion = 2;

        private readonly IPersistence persistence;
        private byte[] secret;

        /// <summary>
        /// Current persisted state (mutable).
        /// Always call <see cref="Save"/> after mutating.
        /// </summary>
        public ChronosSignedState State { get; set; }

        public ChronosStateRepository(IPersistence persistence)
        {
            this.persistence = persistence;
        }

        public void LoadOrCreate()
        {
            LoadOrCreateSecret();
            LoadOrCreateState();

            // Accept v1 signature and migrate to v2.
            if (State.Version == 1 && VerifySignatureAllowV1V2())
            {
                ChronosSignedState migrated = State;
                migrated.Version = CurrentStateVersion;
                migrated.FailureCount = 0;
                migrated.LastSyncUtcIso = string.Empty;
                migrated.LastProviderName = string.Empty;
                State = migrated;
                Save();
            }

            // If signature invalid, mark as tampered.
            if (!VerifySignatureAllowV1V2()) MarkTamperDetected();
        }

        public void Save()
        {
            ChronosSignedState s = State;

            if (s.Version != 1 && s.Version != CurrentStateVersion)
                s.Version = CurrentStateVersion;

            if (s.Version == 1)
                s.Version = CurrentStateVersion;

            s.SignatureBase64 = ChronosStateSigner.ComputeSignatureBase64(s, secret);
            State = s;

            persistence.Set(State, ChronosPersistenceKeys.StateId, State.Version, false);
            persistence.Save();
        }

        public void Clear()
        {
            persistence.Delete<ChronosSignedState>(ChronosPersistenceKeys.StateId, false);
            persistence.Delete<ChronosSecretData>(ChronosPersistenceKeys.SecretId, false);
            persistence.Save();

            secret = null;
            State = default;
        }

        public void MarkTamperDetected()
        {
            ChronosSignedState s = State;
            s.TamperDetected = true;
            State = s;
            Save();
        }

        public bool VerifySignatureAllowV1V2()
        {
            if (secret == null || secret.Length < 16)
                return false;

            if (State.Version != 1 && State.Version != CurrentStateVersion)
                return false;

            return ChronosStateSigner.Verify(State, secret);
        }

        public DateTime GetStoredUtcOrNow()
        {
            return ParseUtcIsoOrNow(State.LastRecordedUtcIso);
        }

        public DateTime GetLastSyncUtcOrDefault()
        {
            if (!string.IsNullOrEmpty(State.LastSyncUtcIso)
                && DateTime.TryParseExact(State.LastSyncUtcIso, "o", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                if (parsed.Kind != DateTimeKind.Utc)
                    parsed = DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);

                return parsed;
            }

            return default;
        }

        public void SetLastSync(DateTime utc, string providerName)
        {
            ChronosSignedState s = State;
            s.LastSyncUtcIso = utc.ToString("o", CultureInfo.InvariantCulture);
            s.LastProviderName = providerName ?? string.Empty;
            State = s;
        }

        private void LoadOrCreateSecret()
        {
            ChronosSecretData secretData = persistence.Get<ChronosSecretData>(ChronosPersistenceKeys.SecretId, default);

            if (!string.IsNullOrEmpty(secretData.SecretBase64))
                try
                {
                    secret = Convert.FromBase64String(secretData.SecretBase64);
                    if (secret != null && secret.Length >= 16)
                        return;
                }
                catch
                {
                    // ignored
                }

            secret = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(secret);
            }

            secretData = new ChronosSecretData
            {
                SecretBase64 = Convert.ToBase64String(secret)
            };

            persistence.Set(secretData, ChronosPersistenceKeys.SecretId, 1, false);
            persistence.Save();
        }

        private void LoadOrCreateState()
        {
            ChronosSignedState loaded = persistence.Get<ChronosSignedState>(ChronosPersistenceKeys.StateId, default);

            bool looksEmpty = loaded.Version == 0
                              && string.IsNullOrEmpty(loaded.LastRecordedUtcIso)
                              && string.IsNullOrEmpty(loaded.SignatureBase64);

            if (looksEmpty)
            {
                State = CreateDefaultState();
                Save();
                return;
            }

            State = loaded;

            if (State.Version <= 0)
            {
                ChronosSignedState s = State;
                s.Version = 1;
                State = s;
            }
        }

        private static ChronosSignedState CreateDefaultState()
        {
            return new ChronosSignedState
            {
                Version = CurrentStateVersion,
                IsFirstStart = true,
                LastRecordedUtcIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                LastUnscaledTimeAsDouble = 0,
                NonceBase64 = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                SignatureBase64 = string.Empty,
                TamperDetected = false,

                FailureCount = 0,
                LastSyncUtcIso = string.Empty,
                LastProviderName = string.Empty
            };
        }

        private static DateTime ParseUtcIsoOrNow(string iso)
        {
            if (!string.IsNullOrEmpty(iso)
                && DateTime.TryParseExact(iso, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                if (parsed.Kind != DateTimeKind.Utc)
                    parsed = DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);

                return parsed;
            }

            DateTime utc = DateTime.UtcNow;
            if (utc.Kind != DateTimeKind.Utc)
                utc = DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);

            return utc;
        }
    }
}