using System;
using System.Globalization;
using System.Threading.Tasks;
using LegendaryTools.Persistence;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Orchestrator: wiring + Unity events + exposing API.
    /// Heavy responsibilities are delegated to small classes.
    /// </summary>
    public sealed class Chronos : IChronos
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool Verbose { get; set; }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool IsInitialized => isInitialized;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool WasFirstStart { get; private set; }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool IsTimeTrusted => isTimeTrusted;

        public ChronosStatus Status => new(
            isInitialized,
            isTimeTrusted,
            stateRepo.State.TamperDetected,
            lastSyncUtc,
            lastSyncResult,
            stateRepo.State.FailureCount,
            stateRepo.State.LastProviderName ?? string.Empty);

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedTimeSpan LastElapsedTimeWhileAppIsClosed { get; private set; }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedDateTime LastRecordedDateTimeUtc => lastRecordedUtc;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedDateTime NowUtc => lastRecordedUtc.DateTime.AddSeconds(GetUnscaledDelta());

        public DateTime UtcNow => NowUtc.DateTime;
        public DateTime LocalNow => UtcNow.ToLocalTime();
        public long UtcNowUnixSeconds => new DateTimeOffset(UtcNow).ToUnixTimeSeconds();
        public long UtcNowUnixMilliseconds => new DateTimeOffset(UtcNow).ToUnixTimeMilliseconds();

        public event Action<TimeSpan> ElapsedTimeWhileAppWasPause;
        public event Action<TimeSpan> ElapsedTimeWhileAppLostFocus;
        public event Action<ChronosSyncResult> Synced;

        private readonly ChronosConfig config;
        private readonly IUnityHub unityHub;

        private readonly ChronosTimeSource timeSource;
        private readonly ChronosStateRepository stateRepo;
        private readonly ChronosSyncEngine syncEngine;
        private readonly ChronosCooldownLimiter cooldown;

        private bool isInitialized;
        private bool isTimeTrusted;
        private bool isSyncInProgress;

        private SerializedDateTime lastRecordedUtc;
        private double lastUnscaledAnchor;

        private DateTime lastSyncUtc;
        private ChronosSyncResult lastSyncResult;

        public Chronos(ChronosConfig config, IUnityHub unityHub, IPersistence persistence)
        {
            this.config = config;
            this.unityHub = unityHub;

            timeSource = new ChronosTimeSource(config);
            stateRepo = new ChronosStateRepository(persistence);
            syncEngine = new ChronosSyncEngine(timeSource, stateRepo);
            cooldown = new ChronosCooldownLimiter();

            unityHub.OnApplicationFocused += OnApplicationFocus;
            unityHub.OnApplicationPaused += OnApplicationPause;
        }

        public async Task Initialize()
        {
            stateRepo.LoadOrCreate();

            ChronosSecurityPolicy policy = GetPolicy();

            WasFirstStart = stateRepo.State.IsFirstStart;

            if (stateRepo.State.TamperDetected && policy.FailClosedOnTamper)
            {
                isTimeTrusted = false;
                LastElapsedTimeWhileAppIsClosed = TimeSpan.Zero;

                LoadAnchorsFromState();
                isInitialized = true;

                lastSyncResult = ChronosSyncResult.Failed(ChronosSyncReason.Initialize, isTimeTrusted,
                    stateRepo.State.LastProviderName);
                RaiseSynced(lastSyncResult);

                if (Verbose)
                    Debug.LogWarning("Chronos.Initialize: Tamper detected. Fail-closed (offline elapsed = 0).");

                return;
            }

            // Initialize does a sync attempt (may fall back depending on policy).
            _ = await SyncInternalAsync(ChronosSyncReason.Initialize, true);

            // Even if strict-failed, we consider the service initialized.
            isInitialized = true;
        }

        public void Sync()
        {
            _ = SyncInternalAsync(ChronosSyncReason.Manual, false);
        }

        public Task<ChronosSyncResult> SyncAsync()
        {
            return SyncInternalAsync(ChronosSyncReason.Manual, false);
        }

        public async Task<(bool, DateTime)> RequestDateTime()
        {
            (bool ok, DateTime utc) = await RequestDateTimeUtc();
            if (!ok) return (false, default);
            return (true, utc.ToLocalTime());
        }

        public async Task<(bool, DateTime)> RequestDateTimeUtc()
        {
            ChronosTimeSample sample = await timeSource.TryGetRemoteUtcNowAsync();
            return (sample.Success, sample.UtcNow);
        }

        public bool TryGetTrustedUtcNow(out DateTime utcNow)
        {
            if (!IsTimeTrusted)
            {
                utcNow = default;
                return false;
            }

            utcNow = UtcNow;
            return true;
        }

        public TimeSpan GetElapsedSinceUtc(DateTime utcSince)
        {
            DateTime now = UtcNow;

            if (utcSince.Kind != DateTimeKind.Utc)
                utcSince = DateTime.SpecifyKind(utcSince.ToUniversalTime(), DateTimeKind.Utc);

            if (now <= utcSince)
                return TimeSpan.Zero;

            return now - utcSince;
        }

        public TimeSpan GetElapsedUntilUtc(DateTime utcUntil)
        {
            DateTime now = UtcNow;

            if (utcUntil.Kind != DateTimeKind.Utc)
                utcUntil = DateTime.SpecifyKind(utcUntil.ToUniversalTime(), DateTimeKind.Utc);

            if (utcUntil <= now)
                return TimeSpan.Zero;

            return utcUntil - now;
        }

        public void ClearPersistentData()
        {
            stateRepo.Clear();
        }

        public void Dispose()
        {
            unityHub.OnApplicationFocused -= OnApplicationFocus;
            unityHub.OnApplicationPaused -= OnApplicationPause;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                PersistRuntimeAnchor();
                return;
            }

            _ = SyncInternalAsync(ChronosSyncReason.FocusGained, false);
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                PersistRuntimeAnchor();
                return;
            }

            _ = SyncInternalAsync(ChronosSyncReason.ResumedFromPause, false);
        }

        private async Task<ChronosSyncResult> SyncInternalAsync(ChronosSyncReason reason, bool bypassCooldown)
        {
            if (!bypassCooldown)
            {
                ChronosSecurityPolicy policy = GetPolicy();
                if (cooldown.ShouldSkip(Time.unscaledTimeAsDouble, policy.MinSyncIntervalSeconds))
                {
                    ChronosSyncResult skipped =
                        ChronosSyncResult.SkippedCooldown(reason, isTimeTrusted, stateRepo.State.LastProviderName);
                    lastSyncResult = skipped;
                    RaiseSynced(skipped);

                    if (Verbose)
                        Debug.Log($"Chronos.Sync({reason}) skipped due to cooldown.");

                    return skipped;
                }
            }

            if (isSyncInProgress)
            {
                ChronosSyncResult busy =
                    ChronosSyncResult.Failed(reason, isTimeTrusted, stateRepo.State.LastProviderName);
                lastSyncResult = busy;
                RaiseSynced(busy);
                return busy;
            }

            if (!IsInitialized && reason != ChronosSyncReason.Initialize && reason != ChronosSyncReason.Manual)
            {
                ChronosSyncResult notReady =
                    ChronosSyncResult.Failed(reason, isTimeTrusted, stateRepo.State.LastProviderName);
                lastSyncResult = notReady;
                RaiseSynced(notReady);
                return notReady;
            }

            isSyncInProgress = true;

            try
            {
                ChronosSecurityPolicy policy = GetPolicy();
                DateTime storedBefore = stateRepo.GetStoredUtcOrNow();

                (ChronosSyncResult result, bool trusted, TimeSpan elapsed, DateTime committedUtc) =
                    await syncEngine.SyncAsync(policy, reason, true, storedBefore);

                isTimeTrusted = trusted;

                // Update runtime anchors
                lastRecordedUtc = committedUtc;
                lastUnscaledAnchor = Time.unscaledTimeAsDouble;

                // Only treat app-closed elapsed as meaningful on Initialize.
                if (reason == ChronosSyncReason.Initialize)
                    LastElapsedTimeWhileAppIsClosed = elapsed;

                // Track last sync
                lastSyncUtc = committedUtc;
                lastSyncResult = result;

                RaiseSynced(result);

                // Fire gameplay pause/focus events (caller can check result.IsTimeTrusted)
                if (reason == ChronosSyncReason.ResumedFromPause)
                    ElapsedTimeWhileAppWasPause?.Invoke(elapsed);
                else if (reason == ChronosSyncReason.FocusGained)
                    ElapsedTimeWhileAppLostFocus?.Invoke(elapsed);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isTimeTrusted = false;

                ChronosSyncResult failed =
                    ChronosSyncResult.Failed(reason, isTimeTrusted, stateRepo.State.LastProviderName);
                lastSyncResult = failed;
                RaiseSynced(failed);

                return failed;
            }
            finally
            {
                isSyncInProgress = false;
            }
        }

        private void PersistRuntimeAnchor()
        {
            if (!IsInitialized)
                return;

            // Save the monotonic estimate while app is running.
            SerializedDateTime now = NowUtc;
            lastRecordedUtc = now;
            lastUnscaledAnchor = Time.unscaledTimeAsDouble;

            ChronosSignedState s = stateRepo.State;
            s.LastRecordedUtcIso = now.DateTime.ToString("o", CultureInfo.InvariantCulture);
            s.LastUnscaledTimeAsDouble = lastUnscaledAnchor;
            stateRepo.State = s;
            stateRepo.Save();

            if (Verbose)
                Debug.Log($"Chronos.PersistRuntimeAnchor -> {now.DateTime:o}");
        }

        private void LoadAnchorsFromState()
        {
            DateTime stored = stateRepo.GetStoredUtcOrNow();
            lastRecordedUtc = stored;
            lastUnscaledAnchor = Time.unscaledTimeAsDouble;

            lastSyncUtc = stateRepo.GetLastSyncUtcOrDefault();
        }

        private double GetUnscaledDelta()
        {
            double delta = Time.unscaledTimeAsDouble - lastUnscaledAnchor;
            if (delta < 0)
                delta = 0;
            return delta;
        }

        private ChronosSecurityPolicy GetPolicy()
        {
            if (config != null && config.SecurityPolicy != null)
                return config.SecurityPolicy;

            // Fallback policy if not assigned
            return ScriptableObject.CreateInstance<ChronosSecurityPolicy>();
        }

        private void RaiseSynced(in ChronosSyncResult result)
        {
            Synced?.Invoke(result);
        }
    }
}