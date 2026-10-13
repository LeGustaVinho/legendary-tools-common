using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.PerformanceTesting;
using UnityEngine;

namespace LegendaryTools.Deterministic.Tests.Benchmarks
{
    [Category("DeterministicBenchmark")]
    [Explicit("Select this fixture to measure performance; timings have no hardware-independent pass threshold.")]
    public sealed unsafe partial class OperationBenchmarks
    {
        public enum ExecutionBackend { Managed, NativeHost, BurstJob }
        public enum InputOrder { Ascending, Descending, Shuffled, EqualKeys }
        public enum SortedCollection { SortedList, Avl }
        public enum DictionaryOperation { Insert, HitAndMissLookup }
        public enum CodecOperation { Write, Read }
        private const int Warmups = 8, Samples = 31;
        private readonly List<Row> rows = new List<Row>();

        [Serializable]
        private sealed class Row
        {
            public string test, operation, backend, distribution, executionScope;
            public int size, itemsPerSample, bytesPerSample, warmups, measurements;
            public bool burstVerified;
            public double minUs, medianUs, p95Us, meanUs, nsPerItem, mibPerSecond;
            public long totalMainThreadGcBytes, maxMainThreadGcBytes;
            public double[] samplesUs;
            public long[] mainThreadGcBytes;
        }
        [Serializable]
        private sealed class Report
        {
            public string utc, unity, burstAssemblyVersion, performanceAssemblyVersion, cpu, os, architecture, scope;
            public int processorCount, warmups, measurements;
            public long stopwatchFrequency;
            public bool collectionChecks, burstEnabled;
            public Row[] results;
        }
        [OneTimeSetUp] public void StartReport() => rows.Clear();
        [OneTimeTearDown]
        public void SaveReport()
        {
            if (rows.Count == 0) return;
            string output = Environment.GetEnvironmentVariable("LEGENDARY_DETERMINISTIC_BENCHMARK_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.dataPath, "../Temp/DeterministicBenchmarks/operations.csv");
            output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output));
            var csv = new List<string> { "operation,backend,distribution,size,items_per_sample,bytes_per_sample,warmups,measurements,min_us,median_us,p95_us,mean_us,ns_per_item,mib_per_second,total_main_thread_gc_bytes,max_main_thread_gc_bytes,burst_verified,execution_scope" };
            foreach (var row in rows)
                csv.Add(string.Join(",", row.operation, row.backend, row.distribution, row.size, row.itemsPerSample, row.bytesPerSample,
                    row.warmups, row.measurements, Number(row.minUs), Number(row.medianUs), Number(row.p95Us), Number(row.meanUs),
                    Number(row.nsPerItem), Number(row.mibPerSecond), row.totalMainThreadGcBytes, row.maxMainThreadGcBytes,
                    row.burstVerified, row.executionScope));
            File.WriteAllLines(output, csv);
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), unity = Application.unityVersion,
                burstAssemblyVersion = typeof(BurstCompiler).Assembly.GetName().Version.ToString(),
                performanceAssemblyVersion = typeof(Measure).Assembly.GetName().Version.ToString(),
                cpu = SystemInfo.processorType, processorCount = SystemInfo.processorCount, os = SystemInfo.operatingSystem,
                architecture = IntPtr.Size == 8 ? "64-bit Editor" : "32-bit Editor", stopwatchFrequency = Stopwatch.Frequency,
                warmups = Warmups, measurements = Samples, burstEnabled = BurstCompiler.IsEnabled,
                scope = "Editor only. Setup, validation, allocations of fixtures and report writing are outside the timed action. BurstJob includes Schedule and Complete; GC counts the calling Managed thread only.",
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                collectionChecks = true,
#endif
                results = rows.ToArray()
            };
            File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonUtility.ToJson(report, true));
            TestContext.Out.WriteLine("Benchmark CSV: " + output);
        }
        private static string Number(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

        private void Benchmark(string operation, ExecutionBackend backend, string distribution, int size, int items,
            int bytes, Action action, Action validate, Action setup = null)
        {
            for (int i = 0; i < Warmups; i++) { setup?.Invoke(); action(); validate(); }
            var times = new double[Samples]; var allocations = new long[Samples];
            GC.GetAllocatedBytesForCurrentThread(); Stopwatch.GetTimestamp();
            for (int i = 0; i < Samples; i++)
            {
                setup?.Invoke();
                long gcBefore = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                action();
                long elapsed = Stopwatch.GetTimestamp() - start;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - gcBefore;
                times[i] = elapsed * (1000000.0 / Stopwatch.Frequency);
                validate();
            }
            // Submit samples after timing so Performance Testing's bookkeeping is excluded.
            var time = new SampleGroup("BatchTime", SampleUnit.Microsecond);
            var gc = new SampleGroup("MainThreadGcBytes", SampleUnit.Byte);
            var perItem = new SampleGroup("PerItemTime", SampleUnit.Nanosecond);
            var throughput = new SampleGroup("ThroughputMiBPerSecond", SampleUnit.Undefined, true);
            double sum = 0; long totalGc = 0, maxGc = 0;
            for (int i = 0; i < Samples; i++)
            {
                Measure.Custom(time, times[i]); Measure.Custom(gc, allocations[i]); Measure.Custom(perItem, times[i] * 1000 / items);
                if (bytes != 0 && times[i] > 0) Measure.Custom(throughput, bytes / 1048576.0 / (times[i] / 1000000));
                sum += times[i]; totalGc += allocations[i]; maxGc = Math.Max(maxGc, allocations[i]);
            }
            var sorted = (double[])times.Clone(); Array.Sort(sorted); double median = sorted[Samples / 2];
            rows.Add(new Row
            {
                test = TestContext.CurrentContext.Test.FullName, operation = operation, backend = backend.ToString(), distribution = distribution,
                size = size, itemsPerSample = items, bytesPerSample = bytes, warmups = Warmups, measurements = Samples,
                burstVerified = backend == ExecutionBackend.BurstJob,
                executionScope = backend == ExecutionBackend.BurstJob ? "Schedule+Complete+Kernel" : "HostOperation",
                minUs = sorted[0], medianUs = median, p95Us = sorted[(int)Math.Ceiling(Samples * 0.95) - 1], meanUs = sum / Samples,
                nsPerItem = median * 1000 / items, mibPerSecond = bytes == 0 || median == 0 ? 0 : bytes / 1048576.0 / (median / 1000000),
                totalMainThreadGcBytes = totalGc, maxMainThreadGcBytes = maxGc, samplesUs = times, mainThreadGcBytes = allocations
            });
        }

        private static BufferOptions Options(int capacity) => new BufferOptions(capacity, snapshotSlots: 0);
        private static int[] Keys(int count, InputOrder order)
        {
            var keys = new int[count]; for (int i = 0; i < count; i++) keys[i] = order == InputOrder.Descending ? count - i - 1 : i;
            if (order == InputOrder.EqualKeys) Array.Clear(keys, 0, count);
            if (order == InputOrder.Shuffled)
            {
                uint seed = 0x514c973du;
                for (int i = count - 1; i > 0; i--)
                { seed = unchecked(seed * 1664525u + 1013904223u); int j = (int)(seed % (uint)(i + 1)); int swap = keys[j]; keys[j] = keys[i]; keys[i] = swap; }
            }
            return keys;
        }
        private static void CheckBurst(int marker)
        { Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, marker, "Measured job must execute in Burst."); }
        [BurstDiscard] private static void MarkManaged(ref int marker) => marker = 1;
    }
}
