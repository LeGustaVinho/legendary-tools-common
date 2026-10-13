using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using UnityEngine;
#endif

namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
    /// <summary>Timing excludes preparation/assertions/reporting. Memory is logical payload, not RSS or peak stack.</summary>
    public abstract class QuaternionBenchmarkFixture
    {
        private readonly List<string> rows = new List<string>();
        protected const int Warmups = 3, Measurements = 9;
        private bool byteCounter, eventCounter;
        private long calibration, emptyEvents;
#if !FIXEDPOINT_STANDALONE
        private UnityEngine.Profiling.Recorder recorder;
        private bool originalEnabled;
#endif
        private string RuntimeName =>
#if FIXEDPOINT_STANDALONE
            "dotnet-" + Environment.Version;
#else
            "unity-" + Application.unityVersion;
#endif
        public static ulong Fold(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
        [OneTimeSetUp] public void InitializeBenchmark()
        {
            FixedRuntime.Initialize(); Stopwatch.GetTimestamp(); GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[8192];
            byteCounter = GC.GetAllocatedBytesForCurrentThread() - before >= probe.Length; GC.KeepAlive(probe);
#if !FIXEDPOINT_STANDALONE
            recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc"); originalEnabled = recorder.enabled; recorder.enabled = false;
            calibration = Events(() => { var allocated = new byte[8192]; GC.KeepAlive(allocated); }); eventCounter = calibration > 0;
            for (int i = 0; i < 3; i++) emptyEvents = Math.Max(emptyEvents, Events(() => { }));
#endif
        }
        private long Events(Action action)
        {
#if FIXEDPOINT_STANDALONE
            return -1;
#else
            recorder.enabled = false; recorder.FilterToCurrentThread();
            try { recorder.enabled = true; action(); }
            finally { recorder.enabled = false; }
            long events = recorder.sampleBlockCount; recorder.CollectFromAllThreads(); return events;
#endif
        }
        protected void Measure(string opportunity, string operation, string backend, string scope, int precision, string math,
            string scenario, int items, long residentBytes, long extraBytes, long wireBytes, Action action, Action verify,
            Action prepare = null, Func<long> liveBytes = null, bool steadyGc = true)
        {
            for (int i = 0; i < Warmups; i++) { prepare?.Invoke(); action(); verify(); }
            var ticks = new long[Measurements]; var allocations = new long[Measurements]; long maxGc = -1, collections = 0;
            for (int i = 0; i < Measurements; i++)
            {
                prepare?.Invoke(); int count = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); action();
                ticks[i] = Stopwatch.GetTimestamp() - start;
                allocations[i] = byteCounter ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                maxGc = Math.Max(maxGc, allocations[i]);
                collections += GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - count;
                verify();
            }
            if (steadyGc && byteCounter) Assert.AreEqual(0, maxGc, operation + " calling-thread steady GC");
            long events = -1;
            if (eventCounter) for (int i = 0; i < 3; i++) { prepare?.Invoke(); events = Math.Max(events, Events(action)); verify(); }
            if (steadyGc && eventCounter) Assert.LessOrEqual(events, emptyEvents, operation + " calibrated calling-thread GC events");
            var ordered = (long[])ticks.Clone(); Array.Sort(ordered);
            double factor = 1000000.0 / Stopwatch.Frequency, median = ordered[Measurements / 2] * factor;
            double p95 = ordered[(int)Math.Ceiling(Measurements * .95) - 1] * factor;
            rows.Add(string.Join(",", new[] { Q(opportunity), Q(operation), Q(backend), Q(scope), precision.ToString(), Q(math), Q(scenario), items.ToString(),
                residentBytes.ToString(), extraBytes.ToString(), (liveBytes?.Invoke() ?? -1).ToString(), wireBytes.ToString(),
                median.ToString("R", CultureInfo.InvariantCulture), p95.ToString("R", CultureInfo.InvariantCulture), (median * 1000 / items).ToString("R", CultureInfo.InvariantCulture),
                byteCounter ? "true" : "false", maxGc.ToString(), collections.ToString(), eventCounter ? "true" : "false", events.ToString(),
                Q(string.Join(";", ticks)), Q(string.Join(";", allocations)) }));
            TestContext.Out.WriteLine($"QUATERNION_BENCH {opportunity} {operation} {backend} {scenario} median_us={median.ToString("F3", CultureInfo.InvariantCulture)} gc_bytes={maxGc} gc_events={events}");
        }
        private static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        [OneTimeTearDown] public void SaveBenchmark()
        {
            try
            {
                string directory = Environment.GetEnvironmentVariable("LEGENDARY_QUATERNION_BENCHMARK_DIR");
                if (string.IsNullOrWhiteSpace(directory)) directory = TestContext.CurrentContext.WorkDirectory;
                Directory.CreateDirectory(directory); string stem = GetType().Name + "-" + RuntimeName;
                File.WriteAllLines(Path.Combine(directory, stem + ".csv"), new[] { "opportunity,operation,backend,scope,precision,math,scenario,items,resident_payload_bytes,extra_payload_bytes,live_payload_bytes,wire_bytes,median_us,p95_us,ns_per_item,gc_counter_available,max_gc_bytes,gc_collections,gc_events_available,max_gc_events,sample_ticks,sample_gc_bytes" });
                File.AppendAllLines(Path.Combine(directory, stem + ".csv"), rows);
                string burst = "unavailable", cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";
#if !FIXEDPOINT_STANDALONE
                cpu = SystemInfo.processorType; burst = typeof(BurstCompiler).Assembly.GetName().Version + ";enabled=" + BurstCompiler.IsEnabled + ";safety=" + BurstCompiler.Options.EnableBurstSafetyChecks;
#endif
                File.WriteAllLines(Path.Combine(directory, stem + ".environment.txt"), new[] {
                    "utc=" + DateTime.UtcNow.ToString("O"), "runtime=" + RuntimeName, "architecture=" + RuntimeInformation.ProcessArchitecture,
                    "os=" + RuntimeInformation.OSDescription, "cpu=" + cpu, "processors=" + Environment.ProcessorCount, "burst=" + burst,
                    "frequency=" + Stopwatch.Frequency, "warmups=" + Warmups, "measurements=" + Measurements,
                    "tieredCompilation=" + (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default"),
                    "gc=calling thread only; calibrationBytes=" + byteCounter + ";calibrationEvents=" + calibration + ";emptyEvents=" + emptyEvents,
                    "unavailable GC counters use -1; collections are process-wide; event runs are separate and untimed",
                    "memory=logical input/cache/wire/history payload, excludes allocator/object overhead and RSS; sizeof is not peak stack",
                    "timing=preallocated inputs, loop and digest included, one native transition for Burst batches; no timing pass threshold",
                    "prototype=benchmark-only; compare full raw/status/wire semantics before trusting a speedup"
                });
            }
            finally
            {
#if !FIXEDPOINT_STANDALONE
                if (recorder != null) recorder.enabled = originalEnabled;
#endif
            }
        }
    }
}
