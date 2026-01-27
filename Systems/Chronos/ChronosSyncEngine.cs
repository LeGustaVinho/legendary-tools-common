using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Encapsulates sync rules: remote success, offline fallback policies, caps and failure counts.
    /// </summary>
    public sealed class ChronosSyncEngine
    {
        private readonly ChronosTimeSource timeSource;
        private readonly ChronosStateRepository stateRepo;

        public ChronosSyncEngine(ChronosTimeSource timeSource, ChronosStateRepository stateRepo)
        {
            this.timeSource = timeSource;
            this.stateRepo = stateRepo;
        }

        public async Task<(ChronosSyncResult Result, bool IsTimeTrusted, TimeSpan Elapsed, DateTime CommittedUtc)>
            SyncAsync(
                ChronosSecurityPolicy policy,
                ChronosSyncReason reason,
                bool allowFirstStart,
                DateTime storedBeforeUtc)
        {
            bool isFirstStart = allowFirstStart && stateRepo.State.IsFirstStart;

            ChronosTimeSample remote = await timeSource.TryGetRemoteUtcNowAsync();
            if (remote.Success)
            {
                ChronosSignedState s = stateRepo.State;
                s.FailureCount = 0;
                s.LastProviderName = remote.ProviderName;
                stateRepo.State = s;

                if (remote.UtcNow <= storedBeforeUtc)
                {
                    // No forward progress, but still consume FirstStart if this is first initialization.
                    CommitUtc(storedBeforeUtc, true, remote.ProviderName, isFirstStart);

                    ChronosSyncResult noProgress = new(
                        true,
                        true,
                        false,
                        reason,
                        storedBeforeUtc,
                        isFirstStart ? default : storedBeforeUtc,
                        TimeSpan.Zero,
                        remote.ProviderName);

                    return (noProgress, true, TimeSpan.Zero, storedBeforeUtc);
                }

                TimeSpan elapsed = remote.UtcNow - storedBeforeUtc;
                DateTime commitUtc = remote.UtcNow;

                elapsed = ChronosOfflinePolicy.ApplyForwardJumpCap(policy, storedBeforeUtc, ref commitUtc, elapsed);

                if (isFirstStart)
                    elapsed = TimeSpan.Zero;

                CommitUtc(commitUtc, true, remote.ProviderName, isFirstStart);

                ChronosSyncResult ok = new(
                    true,
                    true,
                    false,
                    reason,
                    commitUtc,
                    isFirstStart ? default : storedBeforeUtc,
                    elapsed,
                    remote.ProviderName);

                return (ok, true, elapsed, commitUtc);
            }

            // Remote failure
            {
                ChronosSignedState s = stateRepo.State;
                s.FailureCount++;
                stateRepo.State = s;

                bool allowFallback = ChronosOfflinePolicy.ShouldAllowFallback(policy, stateRepo.State.FailureCount);
                if (!allowFallback)
                {
                    stateRepo.Save();

                    ChronosSyncResult failed =
                        ChronosSyncResult.Failed(reason, false, stateRepo.State.LastProviderName);
                    return (failed, false, TimeSpan.Zero, storedBeforeUtc);
                }

                ChronosTimeSample device = ChronosOfflinePolicy.GetDeviceUtcNowSample();

                if (device.UtcNow <= storedBeforeUtc)
                {
                    // No progress, but still consume FirstStart if this is first initialization (baseline established).
                    CommitUtc(storedBeforeUtc, false, device.ProviderName, isFirstStart);

                    ChronosSyncResult noProgress = new(
                        true,
                        false,
                        false,
                        reason,
                        storedBeforeUtc,
                        isFirstStart ? default : storedBeforeUtc,
                        TimeSpan.Zero,
                        device.ProviderName);

                    return (noProgress, false, TimeSpan.Zero, storedBeforeUtc);
                }

                TimeSpan elapsed = device.UtcNow - storedBeforeUtc;
                DateTime commitUtc = device.UtcNow;

                elapsed = ChronosOfflinePolicy.ApplyOfflineSoftCap(policy, storedBeforeUtc, ref commitUtc, elapsed);

                if (isFirstStart)
                    elapsed = TimeSpan.Zero;

                CommitUtc(commitUtc, false, device.ProviderName, isFirstStart);

                ChronosSyncResult fallbackOk = new(
                    true,
                    false,
                    false,
                    reason,
                    commitUtc,
                    isFirstStart ? default : storedBeforeUtc,
                    elapsed,
                    device.ProviderName);

                return (fallbackOk, false, elapsed, commitUtc);
            }
        }

        private void CommitUtc(DateTime utc, bool isTrusted, string providerName, bool markFirstStartFalse)
        {
            ChronosSignedState s = stateRepo.State;

            if (markFirstStartFalse)
                s.IsFirstStart = false;

            s.LastRecordedUtcIso = utc.ToString("o", CultureInfo.InvariantCulture);
            s.NonceBase64 = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            s.LastProviderName = providerName ?? string.Empty;

            stateRepo.State = s;

            UpdateLastSync(utc, providerName);
            stateRepo.Save();
        }

        private void UpdateLastSync(DateTime utc, string providerName)
        {
            stateRepo.SetLastSync(utc, providerName);
        }
    }
}