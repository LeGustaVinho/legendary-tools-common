using System;
using System.Threading.Tasks;

namespace LegendaryTools.Chronos
{
    public interface IChronos : IDisposable
    {
        bool Verbose { get; set; }
        bool IsInitialized { get; }
        bool WasFirstStart { get; }
        bool IsTimeTrusted { get; }

        ChronosStatus Status { get; }

        SerializedTimeSpan LastElapsedTimeWhileAppIsClosed { get; }
        SerializedDateTime LastRecordedDateTimeUtc { get; }
        SerializedDateTime NowUtc { get; }

        DateTime UtcNow { get; }
        DateTime LocalNow { get; }
        long UtcNowUnixSeconds { get; }
        long UtcNowUnixMilliseconds { get; }

        event Action<TimeSpan> ElapsedTimeWhileAppWasPause;
        event Action<TimeSpan> ElapsedTimeWhileAppLostFocus;
        event Action<ChronosSyncResult> Synced;

        Task Initialize();
        void Sync();
        Task<ChronosSyncResult> SyncAsync();

        Task<(bool, DateTime)> RequestDateTime();
        Task<(bool, DateTime)> RequestDateTimeUtc();

        bool TryGetTrustedUtcNow(out DateTime utcNow);
        TimeSpan GetElapsedSinceUtc(DateTime utcSince);
        TimeSpan GetElapsedUntilUtc(DateTime utcUntil);

        void ClearPersistentData();
    }
}