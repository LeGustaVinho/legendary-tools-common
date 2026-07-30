using System;
using System.Diagnostics;
using System.Reflection;

namespace LegendaryTools.ViewBinding
{
    public sealed class BindingRuntimeStatistics
    {
        private static readonly Func<long> AllocatedBytesReader =
            CreateAllocatedBytesReader();

        public bool DetailedMeasurementsEnabled { get; set; }

        public bool AllocationMeasurementsAvailable => AllocatedBytesReader != null;

        public long EvaluatedBindings { get; internal set; }

        public long SuccessfulSynchronizations { get; internal set; }

        public long SkippedSynchronizations { get; internal set; }

        public long FailedSynchronizations { get; internal set; }

        public long MissingEndpointResults { get; internal set; }

        public long AvoidedEndpointRetries { get; internal set; }

        public long ObservedTasks { get; internal set; }

        public long WritesPerformed { get; internal set; }

        public long AvoidedWrites { get; internal set; }

        public long ExecutionPlanBuilds { get; internal set; }

        public long ExecutionPlanCacheHits { get; internal set; }

        public long GeneratedDirectSynchronizations { get; internal set; }

        public long MeasuredSynchronizations { get; private set; }

        public long TotalSynchronizationTicks { get; private set; }

        public long LongestSynchronizationTicks { get; private set; }

        public string SlowestBindingName { get; private set; }

        public long MeasuredAllocatedBytes { get; private set; }

        public long HighestAllocatedBytes { get; private set; }

        public string HighestAllocationBindingName { get; private set; }

        public double TotalSynchronizationMilliseconds =>
            TotalSynchronizationTicks * 1000d / Stopwatch.Frequency;

        public double AverageSynchronizationMilliseconds =>
            MeasuredSynchronizations == 0
                ? 0d
                : TotalSynchronizationMilliseconds / MeasuredSynchronizations;

        public double LongestSynchronizationMilliseconds =>
            LongestSynchronizationTicks * 1000d / Stopwatch.Frequency;

        public void Reset()
        {
            EvaluatedBindings = 0;
            SuccessfulSynchronizations = 0;
            SkippedSynchronizations = 0;
            FailedSynchronizations = 0;
            MissingEndpointResults = 0;
            AvoidedEndpointRetries = 0;
            ObservedTasks = 0;
            WritesPerformed = 0;
            AvoidedWrites = 0;
            ExecutionPlanBuilds = 0;
            ExecutionPlanCacheHits = 0;
            GeneratedDirectSynchronizations = 0;
            MeasuredSynchronizations = 0;
            TotalSynchronizationTicks = 0;
            LongestSynchronizationTicks = 0;
            SlowestBindingName = null;
            MeasuredAllocatedBytes = 0;
            HighestAllocatedBytes = 0;
            HighestAllocationBindingName = null;
        }

        internal BindingPerformanceSample BeginSample()
        {
            if (!DetailedMeasurementsEnabled)
            {
                return default;
            }

            long allocatedBytes = AllocatedBytesReader != null
                ? AllocatedBytesReader()
                : 0;
            return new BindingPerformanceSample(
                true,
                Stopwatch.GetTimestamp(),
                allocatedBytes);
        }

        internal void EndSample(string bindingName, BindingPerformanceSample sample)
        {
            if (!sample.Enabled)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - sample.StartTimestamp;
            MeasuredSynchronizations++;
            TotalSynchronizationTicks += elapsedTicks;
            if (elapsedTicks > LongestSynchronizationTicks)
            {
                LongestSynchronizationTicks = elapsedTicks;
                SlowestBindingName = bindingName;
            }

            if (AllocatedBytesReader == null)
            {
                return;
            }

            long allocatedBytes = Math.Max(
                0L,
                AllocatedBytesReader() - sample.StartAllocatedBytes);
            MeasuredAllocatedBytes += allocatedBytes;
            if (allocatedBytes > HighestAllocatedBytes)
            {
                HighestAllocatedBytes = allocatedBytes;
                HighestAllocationBindingName = bindingName;
            }
        }

        internal void RecordDataBinding(BindingSyncResult result)
        {
            Record(result);
            if (result.Status == BindingSyncStatus.Success)
            {
                WritesPerformed++;
            }
            else if (result.Status == BindingSyncStatus.NoChange)
            {
                AvoidedWrites++;
            }
        }

        internal void Record(BindingSyncResult result)
        {
            EvaluatedBindings++;
            switch (result.Status)
            {
                case BindingSyncStatus.Success:
                    SuccessfulSynchronizations++;
                    break;
                case BindingSyncStatus.NoChange:
                    SkippedSynchronizations++;
                    break;
                case BindingSyncStatus.UnresolvedInstance:
                    MissingEndpointResults++;
                    FailedSynchronizations++;
                    break;
                case BindingSyncStatus.Disabled:
                    break;
                default:
                    FailedSynchronizations++;
                    break;
            }
        }

        private static Func<long> CreateAllocatedBytesReader()
        {
            try
            {
                MethodInfo method = typeof(GC).GetMethod(
                    "GetAllocatedBytesForCurrentThread",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null || method.ReturnType != typeof(long))
                {
                    return null;
                }

                return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            }
            catch
            {
                return null;
            }
        }
    }

    internal readonly struct BindingPerformanceSample
    {
        public BindingPerformanceSample(
            bool enabled,
            long startTimestamp,
            long startAllocatedBytes)
        {
            Enabled = enabled;
            StartTimestamp = startTimestamp;
            StartAllocatedBytes = startAllocatedBytes;
        }

        public bool Enabled { get; }

        public long StartTimestamp { get; }

        public long StartAllocatedBytes { get; }
    }
}
