using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.PerformanceTesting;
using UnityEngine;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    [Category("DeterministicDictionarySparseBenchmark")]
    [Explicit("Select this fixture explicitly. Timing thresholds depend on hardware.")]
    public sealed unsafe partial class DictionarySparseHotpathBenchmarks
    {
        public enum Backend { Managed, BurstJob }
        [Serializable]
        private sealed class Diagnostics
        {
            public long reservedPayloadBytes, livePayloadBytes, nativeMetadataBytes = -1, fixtureNativeBytes;
            public long baselineLivePayloadBytes, payloadGrowthBytes;
            public int baselineLivePages, livePages, pageGrowth, sharedLivePages, retainedLeases;
            public int activeBefore, activeAfter, retainedRoots, payloadBytes;
            public int primaryCapacityBefore, primaryCapacityAfter, secondaryCapacityBefore, secondaryCapacityAfter;
            public int usedBefore, usedAfter;
        }
        [Serializable]
        private sealed class Row
        {
            public string test, opportunity, operation, backend, distribution, executionScope, contentDigest;
            public int size, itemsPerSample, bytesPerSample, warmups, measurements;
            public bool burstVerified, allocationInsideOperation;
            public double minUs, medianUs, p95Us, meanUs, nsPerItem;
            public long totalMainThreadGcBytes, maxMainThreadGcBytes, totalMainThreadGcEvents, maxMainThreadGcEvents;
            public double[] samplesUs;
            public long[] mainThreadGcBytes;
            public long[] mainThreadGcEvents;
            public Diagnostics diagnostics;
        }
        [Serializable]
        private sealed class Report
        {
            public string utc, unity, burstAssemblyVersion, performanceAssemblyVersion, cpu, os, architecture, scope;
            public int processorCount;
            public long stopwatchFrequency;
            public bool collectionChecks, burstEnabled, profilerEnabled, mainThreadGcByteCounterAvailable, mainThreadGcEventCounterAvailable;
            public bool gcRecorderOriginallyEnabled, gcRecorderEnabledDuringTimings;
            public long gcCalibrationByteDelta, gcCalibrationEvents;
            public long[] gcEmptyActionEvents;
            public Row[] results;
        }
        private readonly List<Row> rows = new List<Row>();
        private bool byteCounterAvailable, eventCounterAvailable;
        private long calibrationBytes, calibrationEvents;
        private long[] emptyGcEvents;
        private static readonly Action EmptyAction = () => { };
        private UnityEngine.Profiling.Recorder gcRecorder;
        private bool recorderOriginallyEnabled, recorderEnabledDuringTimings;
        [OneTimeSetUp]
        public void StartReport()
        {
            rows.Clear(); GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[8192]; long after = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(probe);
            calibrationBytes = after - before; byteCounterAvailable = calibrationBytes >= 8192;
            gcRecorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorderOriginallyEnabled = gcRecorder.enabled; gcRecorder.enabled = false;
            calibrationEvents = CountGcEvents(() => { var value = new byte[8192]; GC.KeepAlive(value); });
            eventCounterAvailable = calibrationEvents > 0;
            CountGcEvents(EmptyAction); emptyGcEvents = new long[9];
            for (int i = 0; i < emptyGcEvents.Length; i++) emptyGcEvents[i] = CountGcEvents(EmptyAction);
        }
        private long CountGcEvents(Action action)
        {
            bool previous = gcRecorder.enabled; gcRecorder.enabled = false; gcRecorder.FilterToCurrentThread();
            long result = 0;
            try { gcRecorder.enabled = true; action(); }
            finally { gcRecorder.enabled = false; result = gcRecorder.sampleBlockCount; gcRecorder.CollectFromAllThreads(); gcRecorder.enabled = previous; }
            return result;
        }
        private static string Number(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
        [OneTimeTearDown]
        public void SaveReport()
        {
            try { WriteReport(); }
            finally { if (gcRecorder != null) gcRecorder.enabled = recorderOriginallyEnabled; }
        }
        private void WriteReport()
        {
            if (rows.Count == 0) return;
            string output = Environment.GetEnvironmentVariable("LEGENDARY_DETERMINISTIC_DICTIONARY_SPARSE_BENCHMARK_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.dataPath, "../Temp/DeterministicBenchmarks/dictionary-sparse-hotpath.csv");
            output = Path.GetFullPath(output);
            if (Path.GetExtension(output) != ".csv") throw new InvalidOperationException("Benchmark output must end with .csv.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var csv = new List<string> { "opportunity,operation,backend,distribution,size,items_per_sample,bytes_per_sample,warmups,measurements,median_us,p95_us,mean_us,min_us,ns_per_item,total_main_thread_gc_bytes,max_main_thread_gc_bytes,total_main_thread_gc_events,max_main_thread_gc_events,gc_event_measurements,burst_verified,allocation_inside_operation,reserved_payload_bytes,live_payload_bytes,native_metadata_bytes,fixture_native_bytes,baseline_live_payload_bytes,payload_growth_bytes,baseline_live_pages,live_pages,page_growth,shared_live_pages,retained_leases,active_before,active_after,retained_roots,payload_bytes,primary_capacity_before,primary_capacity_after,secondary_capacity_before,secondary_capacity_after,used_before,used_after,execution_scope,content_digest" };
            foreach (var row in rows)
            {
                var d = row.diagnostics;
                csv.Add(string.Join(",", row.opportunity, row.operation, row.backend, row.distribution, row.size,
                    row.itemsPerSample, row.bytesPerSample, row.warmups, row.measurements, Number(row.medianUs),
                    Number(row.p95Us), Number(row.meanUs), Number(row.minUs), Number(row.nsPerItem),
                    row.totalMainThreadGcBytes, row.maxMainThreadGcBytes, row.totalMainThreadGcEvents, row.maxMainThreadGcEvents,
                    row.mainThreadGcEvents.Length, row.burstVerified, row.allocationInsideOperation,
                    d.reservedPayloadBytes, d.livePayloadBytes, d.nativeMetadataBytes, d.fixtureNativeBytes,
                    d.baselineLivePayloadBytes, d.payloadGrowthBytes, d.baselineLivePages, d.livePages, d.pageGrowth,
                    d.sharedLivePages, d.retainedLeases, d.activeBefore, d.activeAfter, d.retainedRoots, d.payloadBytes,
                    d.primaryCapacityBefore, d.primaryCapacityAfter, d.secondaryCapacityBefore, d.secondaryCapacityAfter,
                    d.usedBefore, d.usedAfter, row.executionScope, row.contentDigest));
            }
            File.WriteAllLines(output, csv);
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), unity = Application.unityVersion,
                burstAssemblyVersion = typeof(BurstCompiler).Assembly.GetName().Version.ToString(),
                performanceAssemblyVersion = typeof(Measure).Assembly.GetName().Version.ToString(),
                cpu = SystemInfo.processorType, processorCount = SystemInfo.processorCount, os = SystemInfo.operatingSystem,
                architecture = IntPtr.Size == 8 ? "64-bit Editor" : "32-bit Editor", stopwatchFrequency = Stopwatch.Frequency,
                burstEnabled = BurstCompiler.IsEnabled, profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                mainThreadGcByteCounterAvailable = byteCounterAvailable, mainThreadGcEventCounterAvailable = eventCounterAvailable,
                gcCalibrationByteDelta = calibrationBytes, gcCalibrationEvents = calibrationEvents,
                gcEmptyActionEvents = emptyGcEvents,
                gcRecorderOriginallyEnabled = recorderOriginallyEnabled, gcRecorderEnabledDuringTimings = recorderEnabledDuringTimings,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                collectionChecks = true,
#endif
                scope = "Editor. Managed is host execution; BurstJob includes Schedule+Complete+Kernel and verifies a BurstDiscard marker inside every measured execution. Only the selected collection operation/batch plus explicit fixture output stores is timed. Construction, initial population, snapshot capture, resets, disposal, validation and diagnostics are untimed. Growth fixtures recreate their original owner before every execution, so physical growth is inside the timed operation; other mutations restore one retained root. Digest validates all logical payload words, order, statuses and retained snapshot content. LivePayload includes current and retained storage capacity, not logical Count; growth is relative to the prepared root baseline. PageGrowth is the observed change in live page count. NativeMetadata is an estimate of accessible native bookkeeping tables and excludes allocator/safety overhead; managed metadata is unavailable (-1). FixtureNativeBytes counts only fixture NativeArrays, excluding managed oracles. GC is the calling thread only; bytes are -1 when the positive 8 KiB calibration fails. GC.Alloc events have nine independent executions and a nine-run empty control; recorder is disabled during timing and restored afterward. Storage counters exclude object headers, pinning, allocator overhead and RSS. No product optimization is introduced by this fixture.",
                results = rows.ToArray()
            };
            File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonUtility.ToJson(report, true));
            TestContext.Out.WriteLine("Dictionary/SparseSet benchmarks: " + output);
        }
        private void Benchmark(string opportunity, string operation, Backend backend, string distribution,
            int size, int items, int bytes, Action action, Action validate, Action setup = null,
            Func<Diagnostics> diagnostics = null, Func<ulong> digest = null, bool allocationInsideOperation = false)
        {
            const int warmups = 8, measurements = 31;
            if (digest == null) throw new ArgumentNullException(nameof(digest));
            ulong expectedDigest = 0; bool hasDigest = false;
            Action verify = () =>
            {
                validate(); ulong actual = digest();
                if (hasDigest) Assert.AreEqual(expectedDigest, actual, "Output changed between equivalent samples.");
                else { expectedDigest = actual; hasDigest = true; }
            };
            for (int i = 0; i < warmups; i++) { setup?.Invoke(); action(); verify(); }
            var times = new double[measurements]; var allocations = new long[measurements];
            GC.GetAllocatedBytesForCurrentThread(); Stopwatch.GetTimestamp();
            for (int i = 0; i < measurements; i++)
            {
                setup?.Invoke();
                // Recorder.Get creates an enabled recorder on this Editor. Keep it off while timing.
                gcRecorder.enabled = false; recorderEnabledDuringTimings |= gcRecorder.enabled;
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                action();
                long elapsed = Stopwatch.GetTimestamp() - start;
                allocations[i] = byteCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                times[i] = elapsed * (1000000.0 / Stopwatch.Frequency);
                verify();
            }
            var events = new long[9]; long totalEvents = 0, maxEvents = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (eventCounterAvailable) { setup?.Invoke(); events[i] = CountGcEvents(action); verify(); }
                else events[i] = -1;
                totalEvents += events[i]; maxEvents = Math.Max(maxEvents, events[i]);
            }
            var d = diagnostics?.Invoke() ?? new Diagnostics();
            var time = new SampleGroup("BatchTime", SampleUnit.Microsecond);
            var gc = new SampleGroup("MainThreadGcBytes", SampleUnit.Byte);
            var gcEvents = new SampleGroup("MainThreadGcEvents", SampleUnit.Undefined);
            var perItem = new SampleGroup("PerItemTime", SampleUnit.Nanosecond);
            double sum = 0; long totalGc = 0, maxGc = 0;
            for (int i = 0; i < measurements; i++)
            {
                Measure.Custom(time, times[i]); if (byteCounterAvailable) Measure.Custom(gc, allocations[i]);
                Measure.Custom(perItem, times[i] * 1000 / items);
                sum += times[i]; totalGc += allocations[i]; maxGc = Math.Max(maxGc, allocations[i]);
            }
            if (eventCounterAvailable) foreach (long value in events) Measure.Custom(gcEvents, value);
            var sorted = (double[])times.Clone(); Array.Sort(sorted); double median = sorted[measurements / 2];
            rows.Add(new Row
            {
                test = TestContext.CurrentContext.Test.FullName, opportunity = opportunity, operation = operation,
                backend = backend.ToString(), distribution = distribution, size = size, itemsPerSample = items,
                bytesPerSample = bytes, warmups = warmups, measurements = measurements, burstVerified = backend == Backend.BurstJob,
                allocationInsideOperation = allocationInsideOperation, executionScope = backend == Backend.BurstJob ? "Schedule+Complete+Kernel" : "HostOperation",
                contentDigest = expectedDigest.ToString("x16", CultureInfo.InvariantCulture),
                minUs = sorted[0], medianUs = median, p95Us = sorted[(int)Math.Ceiling(measurements * 0.95) - 1],
                meanUs = sum / measurements, nsPerItem = median * 1000 / items, totalMainThreadGcBytes = byteCounterAvailable ? totalGc : -1,
                maxMainThreadGcBytes = byteCounterAvailable ? maxGc : -1, samplesUs = times, mainThreadGcBytes = allocations,
                totalMainThreadGcEvents = eventCounterAvailable ? totalEvents : -1, maxMainThreadGcEvents = eventCounterAvailable ? maxEvents : -1,
                mainThreadGcEvents = events, diagnostics = d
            });
        }
        private static Diagnostics Stats(GraphMemoryStats stats) => new Diagnostics
        {
            reservedPayloadBytes = stats.ReservedPayloadBytes, livePayloadBytes = stats.LivePayloadBytes,
            livePages = stats.LivePages, sharedLivePages = stats.SharedLivePages, retainedLeases = stats.BufferSnapshotLeases
        };
        private static long NativeMetadata<T>(NativeStorage<T> storage) where T : unmanaged
        {
            var data = storage.Data; if (data == null) return 0;
            long bytes = sizeof(NativeStorage<T>.Header) + (long)data->Options.SnapshotSlots * (sizeof(int) + sizeof(ulong));
            if (data->Options.Snapshots == SnapshotPolicy.CopyOnWrite)
                return bytes + (long)data->PoolCapacity * (sizeof(NativeStorage<T>.Page) + sizeof(int))
                    + (long)data->MaxPages * sizeof(int) * (1 + data->Options.SnapshotSlots);
            return bytes + (data->Options.Allocation == AllocationPolicy.Grow ? (long)data->Options.SnapshotSlots * (sizeof(IntPtr) + sizeof(int)) : 0);
        }
        private static BufferOptions Options(int capacity, SnapshotPolicy policy = SnapshotPolicy.FullPerTick, int slots = 0)
            => new BufferOptions(capacity, snapshots: policy, snapshotSlots: slots, pageBytes: 4096);
        private static void CheckBurst(int marker) { Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, marker, "The measured job must execute through Burst."); }
        private const ulong DigestSeed = 14695981039346656037UL;
        private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
        private static int Value(int index) => unchecked(index * 104729 ^ 0x31415926);
        private static int[] Permutation(int count)
        {
            var result = new int[count]; for (int i = 0; i < count; i++) result[i] = i;
            uint seed = 0x1234abcd;
            for (int i = count - 1; i > 0; i--)
            { seed = unchecked(seed * 1664525 + 1013904223); int j = (int)(seed % (uint)(i + 1)); int temporary = result[i]; result[i] = result[j]; result[j] = temporary; }
            return result;
        }
    }
}

