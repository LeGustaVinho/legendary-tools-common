using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;
using UnityEngine;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    /// <summary>Opt-in baselines. Correctness is asserted; hardware-dependent timings are observations.</summary>
    [Category("DeterministicGraphExtendedHotpathBenchmark")]
    [Explicit("Select this fixture explicitly to run graph CPU/memory benchmarks.")]
    public sealed unsafe partial class GraphExtendedHotpathBenchmarks
    {
        public enum Backend { Managed, BurstJob }
        [Serializable] private sealed class Metrics
        {
            public long reservedPayloadBytes, livePayloadBytes, baselineLivePayloadBytes, payloadGrowthBytes;
            public int livePages, sharedLivePages, retainedBufferLeases;
            public int nodes, edges, nodeExtent, edgeExtent, internalScratchReservedBytes, externalScratchReservedBytes;
            public int scratchConsumedBytes, auxiliaryBytes, canonicalBytes, transferBytes, retainedVersions, versionCapacity;
            public long codecMeasureCalls, codecWriteCalls, codecReadCalls;
            public string contentDigest, transferKind, transferDigest;
            public int observedStatus, snapshotsPerSample, nodeCapacity, edgeCapacity;
            public long observedItems, observedMisses;
        }
        [Serializable] private sealed class Row
        {
            public string test, opportunity, operation, backend, scenario, executionScope;
            public int size, itemsPerSample, iterations, warmups, measurements;
            public bool burstVerified;
            public double medianUs, p95Us, minUs, nsPerItem;
            public double[] samplesUs;
            public long[] mainThreadGcBytes, mainThreadGcEvents;
            public long maxMainThreadGcBytes, maxMainThreadGcEvents;
            public Metrics metrics;
        }
        [Serializable] private sealed class Report
        {
            public string utc, unity, burstAssemblyVersion, performanceAssemblyVersion, cpu, os, scope;
            public int processorCount;
            public long stopwatchFrequency, gcCalibrationBytes, gcCalibrationEvents;
            public bool burstEnabled, collectionChecks, gcByteCounterAvailable, gcEventCounterAvailable;
            public long[] emptyActionGcEvents;
            public Row[] results;
        }
        private readonly List<Row> rows = new List<Row>();
        private UnityEngine.Profiling.Recorder gcRecorder;
        private bool recorderWasEnabled, gcBytesAvailable, gcEventsAvailable;
        private long gcCalibrationBytes, gcCalibrationEvents;
        private long[] emptyGcEvents;
        private static readonly Action EmptyAction = () => { };

        [OneTimeSetUp] public void StartGraphReport()
        {
            rows.Clear(); GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[8192]; gcCalibrationBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(probe); gcBytesAvailable = gcCalibrationBytes >= 8192;
            gcRecorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorderWasEnabled = gcRecorder.enabled; gcRecorder.enabled = false;
            gcCalibrationEvents = CountGcEvents(() => { var p = new byte[8192]; GC.KeepAlive(p); });
            gcEventsAvailable = gcCalibrationEvents > 0;
            emptyGcEvents = new long[9]; CountGcEvents(EmptyAction);
            for (int i = 0; i < emptyGcEvents.Length; i++) emptyGcEvents[i] = CountGcEvents(EmptyAction);
        }
        private long CountGcEvents(Action action)
        {
            gcRecorder.enabled = false; gcRecorder.FilterToCurrentThread();
            long events = 0;
            try { gcRecorder.enabled = true; action(); }
            finally { gcRecorder.enabled = false; events = gcRecorder.sampleBlockCount; gcRecorder.CollectFromAllThreads(); }
            return events;
        }
        [OneTimeTearDown] public void SaveGraphReport()
        {
            try
            {
                if (rows.Count == 0) return;
                string path = Environment.GetEnvironmentVariable("LEGENDARY_GRAPH_EXTENDED_HOTPATH_OUTPUT");
                if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.dataPath, "../Temp/DeterministicBenchmarks/graph-extended-hotpaths.json");
                path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
                var report = new Report
                {
                    utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), unity = Application.unityVersion,
                    burstAssemblyVersion = typeof(BurstCompiler).Assembly.GetName().Version.ToString(),
                    performanceAssemblyVersion = typeof(Measure).Assembly.GetName().Version.ToString(),
                    cpu = SystemInfo.processorType, processorCount = SystemInfo.processorCount, os = SystemInfo.operatingSystem,
                    stopwatchFrequency = Stopwatch.Frequency, burstEnabled = BurstCompiler.IsEnabled,
                    gcByteCounterAvailable = gcBytesAvailable, gcEventCounterAvailable = gcEventsAvailable,
                    gcCalibrationBytes = gcCalibrationBytes, gcCalibrationEvents = gcCalibrationEvents, emptyActionGcEvents = emptyGcEvents,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    collectionChecks = true,
#endif
                    scope = "Editor; BurstJob timings include Schedule+Complete+kernel. Construction, setup/restoration, validation, codec probes, disposal and reports are untimed. GC counters cover the main thread only; -1 means unavailable after calibration. GC events are nine separate untimed probes; compare with the empty-action control. MemoryStats includes internal graph workspaces, excludes object/allocator/safety overhead and persistent root descriptors; it is not RSS. External scratch/auxiliary buffers are reported separately. No absolute timing thresholds.",
                    results = rows.ToArray()
                };
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                var csv = new List<string> { "opportunity,operation,backend,scenario,size,items_per_sample,iterations,median_us,p95_us,ns_per_item,max_main_thread_gc_bytes,max_main_thread_gc_events,reserved_payload_bytes,live_payload_bytes,payload_growth_bytes,internal_scratch_reserved_bytes,external_scratch_reserved_bytes,scratch_consumed_bytes,canonical_bytes,transfer_bytes,retained_versions,version_capacity,digest" };
                foreach (var r in rows)
                {
                    var m = r.metrics;
                    csv.Add(string.Join(",", r.opportunity, r.operation, r.backend, r.scenario, r.size, r.itemsPerSample, r.iterations,
                        r.medianUs.ToString("F6", CultureInfo.InvariantCulture), r.p95Us.ToString("F6", CultureInfo.InvariantCulture),
                        r.nsPerItem.ToString("F6", CultureInfo.InvariantCulture), r.maxMainThreadGcBytes, r.maxMainThreadGcEvents,
                        m.reservedPayloadBytes, m.livePayloadBytes, m.payloadGrowthBytes, m.internalScratchReservedBytes,
                        m.externalScratchReservedBytes, m.scratchConsumedBytes, m.canonicalBytes, m.transferBytes,
                        m.retainedVersions, m.versionCapacity, m.contentDigest));
                }
                File.WriteAllLines(Path.ChangeExtension(path, ".csv"), csv);
                TestContext.Out.WriteLine("Graph hotpath report: " + path + "; rows: " + rows.Count);
            }
            finally { if (gcRecorder != null) gcRecorder.enabled = recorderWasEnabled; }
        }
        private void Benchmark(string id, string operation, Backend backend, string scenario, int size, int items,
            int iterations, Action action, Action validate, Action setup, Func<Metrics> diagnostics)
        {
            const int warmups = 5, measurements = 21;
            for (int i = 0; i < warmups; i++) { setup?.Invoke(); action(); validate(); }
            var times = new double[measurements]; var allocations = new long[measurements];
            Stopwatch.GetTimestamp(); GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < measurements; i++)
            {
                setup?.Invoke(); gcRecorder.enabled = false;
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                action(); long elapsed = Stopwatch.GetTimestamp() - start;
                allocations[i] = gcBytesAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                times[i] = elapsed * (1000000.0 / Stopwatch.Frequency); validate();
            }
            var m = diagnostics(); var events = new long[9];
            for (int i = 0; i < events.Length; i++)
            {
                if (!gcEventsAvailable) { events[i] = -1; continue; }
                setup?.Invoke(); events[i] = CountGcEvents(action); validate();
            }
            var sorted = (double[])times.Clone(); Array.Sort(sorted);
            long maxBytes = -1, maxEvents = -1;
            var timeGroup = new SampleGroup("GraphBatchTime", SampleUnit.Microsecond);
            var bytesGroup = new SampleGroup("GraphMainThreadGcBytes", SampleUnit.Byte);
            foreach (double t in times) Measure.Custom(timeGroup, t);
            foreach (long b in allocations) { maxBytes = Math.Max(maxBytes, b); if (gcBytesAvailable) Measure.Custom(bytesGroup, b); }
            foreach (long e in events) maxEvents = Math.Max(maxEvents, e);
            rows.Add(new Row
            {
                test = TestContext.CurrentContext.Test.FullName, opportunity = id, operation = operation,
                backend = backend.ToString(), scenario = scenario, size = size, itemsPerSample = items, iterations = iterations,
                warmups = warmups, measurements = measurements, burstVerified = backend == Backend.BurstJob,
                executionScope = backend == Backend.BurstJob ? "Schedule+Complete+Kernel" : "ManagedOperation",
                medianUs = sorted[measurements / 2], p95Us = sorted[(int)Math.Ceiling(measurements * .95) - 1], minUs = sorted[0],
                nsPerItem = sorted[measurements / 2] * 1000 / items, samplesUs = times,
                mainThreadGcBytes = allocations, mainThreadGcEvents = events, maxMainThreadGcBytes = maxBytes, maxMainThreadGcEvents = maxEvents,
                metrics = m
            });
        }
        private static Metrics Stats(GraphMemoryStats s) => new Metrics
        {
            reservedPayloadBytes = s.ReservedPayloadBytes, livePayloadBytes = s.LivePayloadBytes,
            livePages = s.LivePages, sharedLivePages = s.SharedLivePages, retainedBufferLeases = s.BufferSnapshotLeases,
            scratchConsumedBytes = -1
        };
        private static GraphOptions Options(int nodes, int edges, SnapshotPolicy policy = SnapshotPolicy.FullPerTick,
            int slots = 0, int pageBytes = 4096, bool parallel = false, bool acyclic = false)
            => new GraphOptions(new BufferOptions(nodes, snapshots: policy, snapshotSlots: slots, pageBytes: pageBytes),
                new BufferOptions(edges, snapshots: policy, snapshotSlots: slots, pageBytes: pageBytes),
                cycles: acyclic ? GraphCyclePolicy.Acyclic : GraphCyclePolicy.Allow, allowParallelEdges: parallel);
        private static int InternalScratch(GraphOptions o)
        { GraphScratch.TryMeasure(o.Nodes.MaxCapacity, o.Edges.MaxCapacity, out int n).ThrowIfFailure(); return n; }
        private const int Complete = 734821;
        [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        private static void Reset(NativeArray<long> result) { for (int i = 0; i < result.Length; i++) result[i] = -1; }
        private static void CheckJob(NativeArray<long> result, DeterministicStatus expected = DeterministicStatus.None)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, result[0], "BurstDiscard marker");
            Assert.AreEqual((long)expected, result[1], "Operation status"); Assert.AreEqual(Complete, result[2], "Job completion sentinel");
        }
        private static ulong Mix(ulong h, ulong value) => unchecked((h ^ value) * 1099511628211UL);
        private static string Hex(ulong digest) => digest.ToString("x16", CultureInfo.InvariantCulture);
        private const ulong DigestSeed = 14695981039346656037UL;

        // Ownership/setup are host-only. Jobs receive the concrete Native owner, never this adapter.
        private sealed class Owner : IDisposable
        {
            internal readonly Backend Backend;
            internal readonly GraphOptions Options;
            internal readonly DeterministicGraph<int, int> Managed;
            internal NativeDeterministicGraph<int, int> Native;
            internal Owner(Backend backend, GraphOptions options)
            {
                Backend = backend; Options = options;
                if (backend == Backend.Managed) Managed = new DeterministicGraph<int, int>(options);
                else Native = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            }
            internal GraphNodeHandle AddNode(int value) => Managed != null ? Managed.AddNode(value) : Native.AddNode(value);
            internal GraphEdgeHandle AddEdge(GraphNodeHandle a, GraphNodeHandle b, int value) => Managed != null ? Managed.AddEdge(a, b, value) : Native.AddEdge(a, b, value);
            internal void RemoveNode(GraphNodeHandle h) { (Managed != null ? Managed.TryRemoveNode(h) : Native.TryRemoveNode(h)).ThrowIfFailure(); }
            internal void RemoveEdge(GraphEdgeHandle h) { (Managed != null ? Managed.TryRemoveEdge(h) : Native.TryRemoveEdge(h)).ThrowIfFailure(); }
            internal void SetNode(GraphNodeHandle h, int value) { (Managed != null ? Managed.TrySetNode(h, value) : Native.TrySetNode(h, value)).ThrowIfFailure(); }
            internal int NodeCount => Managed != null ? Managed.NodeCount : Native.NodeCount;
            internal int EdgeCount => Managed != null ? Managed.EdgeCount : Native.EdgeCount;
            internal int Extent => Managed != null ? Managed.AsReadOnly().NodeExtent : Native.AsReadOnly().NodeExtent;
            internal GraphMemoryStats Memory => Managed != null ? Managed.MemoryStats : Native.MemoryStats;
            internal Metrics Metrics()
            {
                var m = Stats(Memory); m.nodes = NodeCount; m.edges = EdgeCount; m.nodeExtent = Extent;
                m.edgeExtent = Managed != null ? Managed.AsReadOnly().EdgeExtent : Native.AsReadOnly().EdgeExtent;
                m.internalScratchReservedBytes = InternalScratch(Options);
                m.nodeCapacity = Managed != null ? Managed.NodeCapacity : Native.NodeCapacity;
                m.edgeCapacity = Managed != null ? Managed.EdgeCapacity : Native.EdgeCapacity; return m;
            }
            internal DeterministicStatus Write(ref DeterministicWriter w) => Managed != null ? Managed.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)) : Native.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy));
            internal byte[] Canonical()
            {
                var measure = DeterministicWriter.CreateMeasure(); Write(ref measure).ThrowIfFailure(); var bytes = new byte[measure.Position];
                fixed (byte* p = bytes) { var w = new DeterministicWriter(p, bytes.Length); Write(ref w).ThrowIfFailure(); Assert.AreEqual(bytes.Length, w.Position); }
                return bytes;
            }
            public void Dispose() { if (Managed != null) Managed.Dispose(); else Native.Dispose(); }
        }
        private sealed class Root : IDisposable
        {
            private readonly Owner owner;
            internal readonly ManagedGraphSnapshot<int, int> Managed;
            internal readonly NativeGraphSnapshot<int, int> Native;
            internal Root(Owner owner)
            {
                this.owner = owner;
                if (owner.Managed != null) owner.Managed.TryCapture(out Managed).ThrowIfFailure();
                else owner.Native.TryCapture(out Native).ThrowIfFailure();
            }
            internal void Restore() { (owner.Managed != null ? owner.Managed.TryRestore(Managed) : owner.Native.TryRestore(Native)).ThrowIfFailure(); }
            internal int GetNode(GraphNodeHandle h)
            {
                int value; var s = owner.Managed != null ? Managed.TryGetNode(h, out value) : Native.TryGetNode(h, out value);
                s.ThrowIfFailure(); return value;
            }
            public void Dispose() { if (owner.Managed != null) Managed.Dispose(); else Native.Dispose(); }
        }
        private static void CheckResult(Backend backend, NativeArray<long> result, DeterministicStatus expected = DeterministicStatus.None)
        {
            if (backend == Backend.BurstJob) CheckJob(result, expected);
            else { Assert.AreEqual((long)expected, result[1]); Assert.AreEqual(Complete, result[2]); }
        }
        private static GraphOptions RingOptions(int extent, SnapshotPolicy policy = SnapshotPolicy.CopyOnWrite, int slots = 2)
            => new GraphOptions(new BufferOptions(extent, snapshots: policy, snapshotSlots: slots),
                new BufferOptions(extent, snapshots: policy, snapshotSlots: slots), allowParallelEdges: true);
        // Both slot maps have the same extent; surviving identities occupy the end of every stride.
        private static GraphNodeHandle[] PopulateHoleyRing(Owner owner, int active, int extent, out GraphEdgeHandle[] liveEdges)
        {
            int stride = extent / active; var all = new GraphNodeHandle[extent]; var live = new GraphNodeHandle[active];
            for (int i = 0; i < extent; i++) all[i] = owner.AddNode(i);
            for (int i = 0; i < extent; i++)
                if (i % stride == stride - 1) live[i / stride] = all[i]; else owner.RemoveNode(all[i]);
            var edges = new GraphEdgeHandle[extent]; liveEdges = new GraphEdgeHandle[active];
            for (int i = 0; i < extent; i++) edges[i] = owner.AddEdge(live[i / stride], live[(i / stride + 1) % active], i);
            for (int i = 0; i < extent; i++)
                if (i % stride == stride - 1) liveEdges[i / stride] = edges[i]; else owner.RemoveEdge(edges[i]);
            return live;
        }
        private static GraphNodeHandle[] PopulateSparse(Owner owner, int active, int extent, bool wide = false)
        {
            if (extent < active || extent % active != 0) throw new ArgumentException("Extent must be a multiple of active count.");
            int stride = extent / active; var all = new GraphNodeHandle[extent]; var keep = new GraphNodeHandle[active];
            for (int i = 0; i < extent; i++) all[i] = owner.AddNode(i);
            for (int i = 0; i < extent; i++)
                if (i % stride == stride - 1) keep[i / stride] = all[i]; else owner.RemoveNode(all[i]);
            // The final active node is isolated: reachability misses and weak components have known answers.
            for (int i = 1; i < active - 1; i++) owner.AddEdge(keep[wide ? 0 : i - 1], keep[i], i);
            Assert.AreEqual(extent, owner.Extent); Assert.AreEqual(active, owner.NodeCount); return keep;
        }
        private static ulong CanonicalDigest(byte[] bytes)
        { fixed (byte* p = bytes) return DeterministicHash.Xxh64(p, bytes.Length); }
    }
}
