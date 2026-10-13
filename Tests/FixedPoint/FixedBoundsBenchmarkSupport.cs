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
    /// <summary>Shared recorder. Preparation, assertions, disposal and reporting are outside timed samples.</summary>
    public abstract class BoundsBenchmarkFixture
    {
        private readonly List<string> rows = new List<string>();
        private bool counterAvailable;
        private bool eventCounterAvailable;
        private long eventCalibration;
        private long[] emptyEvents = { -1, -1, -1 };
#if !FIXEDPOINT_STANDALONE
        private UnityEngine.Profiling.Recorder gcRecorder;
        private bool recorderOriginallyEnabled;
#endif
        protected const int Warmups = 3, Measurements = 9;
        protected string RuntimeName
        {
            get
            {
#if FIXEDPOINT_STANDALONE
                return "dotnet-" + Environment.Version;
#else
                return "unity-" + Application.unityVersion;
#endif
            }
        }
        [OneTimeSetUp] public void InitializeBenchmark()
        {
            FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize();
            GC.GetAllocatedBytesForCurrentThread(); Stopwatch.GetTimestamp();
            long before = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[8192];
            counterAvailable = GC.GetAllocatedBytesForCurrentThread() - before >= probe.Length; GC.KeepAlive(probe);
            eventCounterAvailable = false; eventCalibration = 0;
#if !FIXEDPOINT_STANDALONE
            gcRecorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc"); recorderOriginallyEnabled = gcRecorder.enabled; gcRecorder.enabled = false;
            eventCalibration = CountEvents(() => { var allocation = new byte[8192]; GC.KeepAlive(allocation); }); eventCounterAvailable = eventCalibration > 0;
            Action empty = () => { }; CountEvents(empty); for (int i = 0; i < emptyEvents.Length; i++) emptyEvents[i] = CountEvents(empty);
#endif
            rows.Clear();
        }
        private long CountEvents(Action action)
        {
#if FIXEDPOINT_STANDALONE
            return -1;
#else
            bool previous = gcRecorder.enabled; gcRecorder.enabled = false; gcRecorder.FilterToCurrentThread(); long count;
            try { gcRecorder.enabled = true; action(); }
            finally { gcRecorder.enabled = false; count = gcRecorder.sampleBlockCount; gcRecorder.CollectFromAllThreads(); gcRecorder.enabled = previous; }
            return count;
#endif
        }
        protected void Measure(string opportunity, string operation, string backend, string scope, int dimension, int precision,
            string representation, string scenario, int items, long residentPayload, long extraPayload, long wireBytes,
            Action action, Action verify, Action prepare = null, bool cold = false, Func<long> liveBytes = null)
        {
            for (int i = 0; i < Warmups; i++) { prepare?.Invoke(); action(); verify(); }
            var ticks = new long[Measurements]; var gc = new long[Measurements]; long maxGc = 0, totalGc = 0;
            long gcCollections = 0;
            for (int i = 0; i < Measurements; i++)
            {
                prepare?.Invoke();
#if !FIXEDPOINT_STANDALONE
                gcRecorder.enabled = false;
#endif
                int collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                action();
                ticks[i] = Stopwatch.GetTimestamp() - start;
                gc[i] = counterAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                gcCollections += GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - collections;
                if (counterAvailable) { maxGc = Math.Max(maxGc, gc[i]); totalGc += gc[i]; }
                verify();
            }
            var ordered = (long[])ticks.Clone(); Array.Sort(ordered); double scale = 1000000.0 / Stopwatch.Frequency;
            double median = ordered[Measurements / 2] * scale, p95 = ordered[(int)Math.Ceiling(Measurements * .95) - 1] * scale;
            if (!cold && counterAvailable) Assert.AreEqual(0, maxGc, operation + " steady calling-thread GC");
            long[] events = { -1, -1, -1 }; long maxEvents = -1;
            if (eventCounterAvailable)
                for (int i = 0; i < events.Length; i++) { prepare?.Invoke(); events[i] = CountEvents(action); maxEvents = Math.Max(maxEvents, events[i]); verify(); }
            string samples = string.Join(";", Array.ConvertAll(ticks, value => value.ToString(CultureInfo.InvariantCulture)));
            string allocations = string.Join(";", Array.ConvertAll(gc, value => value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(string.Join(",", new[] { Quote(opportunity), Quote(operation), Quote(backend), Quote(scope), dimension.ToString(), precision.ToString(),
                Quote(representation), Quote(scenario), items.ToString(), residentPayload.ToString(), extraPayload.ToString(), wireBytes.ToString(),
                (liveBytes?.Invoke() ?? -1).ToString(), cold ? "true" : "false", counterAvailable ? "true" : "false",
                median.ToString("R", CultureInfo.InvariantCulture), p95.ToString("R", CultureInfo.InvariantCulture),
                (median * 1000 / items).ToString("R", CultureInfo.InvariantCulture), counterAvailable ? totalGc.ToString() : "-1",
                counterAvailable ? maxGc.ToString() : "-1", gcCollections.ToString(), Quote(samples), Quote(allocations),
                eventCounterAvailable ? "true" : "false", maxEvents.ToString(), Quote(string.Join(";", events)) }));
            TestContext.Out.WriteLine("BOUNDS_BENCH " + opportunity + " " + operation + " " + backend + " " + representation + " " + scenario +
                " median_us=" + median.ToString("F3", CultureInfo.InvariantCulture) + " ns/item=" + (median * 1000 / items).ToString("F3", CultureInfo.InvariantCulture) + " gc=" + (counterAvailable ? maxGc : -1) + " gc_events=" + maxEvents);
        }
        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        [OneTimeTearDown] public void SaveBenchmark()
        {
            try { WriteBenchmark(); }
            finally
            {
#if !FIXEDPOINT_STANDALONE
                if (gcRecorder != null) gcRecorder.enabled = recorderOriginallyEnabled;
#endif
            }
        }
        private void WriteBenchmark()
        {
            string directory = Environment.GetEnvironmentVariable("LEGENDARY_BOUNDS_BENCHMARK_DIR");
            if (string.IsNullOrWhiteSpace(directory)) directory = TestContext.CurrentContext.WorkDirectory;
            Directory.CreateDirectory(directory);
            string stem = GetType().Name + "-" + FixedBoundsRuntime.Settings.Representation + "-" + RuntimeName;
            File.WriteAllLines(Path.Combine(directory, stem + ".csv"), new[] {
                "opportunity,operation,backend,scope,dimension,precision,representation,scenario,items,resident_payload_bytes,extra_payload_bytes,wire_bytes,live_payload_bytes,cold_allocation,gc_counter_available,median_us,p95_us,ns_per_item,total_gc_bytes,max_gc_bytes,gc_collections,sample_ticks,sample_gc_bytes,gc_events_available,max_gc_events,sample_gc_events"
            });
            File.AppendAllLines(Path.Combine(directory, stem + ".csv"), rows);
            string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";
            string burst = "unavailable";
#if !FIXEDPOINT_STANDALONE
            cpu = SystemInfo.processorType;
            burst = typeof(BurstCompiler).Assembly.GetName().Version + "; enabled=" + BurstCompiler.IsEnabled + "; safetyChecks=" + BurstCompiler.Options.EnableBurstSafetyChecks;
#endif
            File.WriteAllLines(Path.Combine(directory, stem + ".environment.txt"), new[] {
                "utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), "runtime=" + RuntimeName,
                "architecture=" + RuntimeInformation.ProcessArchitecture, "os=" + RuntimeInformation.OSDescription,
                "cpu=" + cpu, "processorCount=" + Environment.ProcessorCount, "burst=" + burst,
                "stopwatchFrequency=" + Stopwatch.Frequency, "warmups=" + Warmups, "measurements=" + Measurements,
                "boundsMode=" + FixedBoundsRuntime.Settings.Representation,
                "gcScope=calling thread only; collection counts are process-wide; unavailable counters use -1",
                "gcEvents=three separate untimed executions; calibration=" + eventCalibration + "; emptyControl=" + string.Join(";", emptyEvents) + "; recorder disabled during timings",
                "memoryScope=logical payload only; allocator/object/safety overhead and RSS are excluded",
                "timingScope=preallocated inputs; preparation, verification, disposal and reporting are untimed",
                "thresholds=semantic parity and steady GC only; CPU timings have no hardware-independent pass threshold"
            });
        }
    }
}
