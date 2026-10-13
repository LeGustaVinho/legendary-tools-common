using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
#endif

namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
#if !FIXEDPOINT_STANDALONE
    [BurstCompile]
#endif
    [Category("BoundsBenchmark"), Explicit("Select this fixture explicitly for large array/locality and allocation measurements.")]
    public sealed unsafe class BoundsMemoryBenchmarks : BoundsBenchmarkFixture
    {
        private struct Output { internal ulong Digest; internal int Managed; }
#if !FIXEDPOINT_STANDALONE
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Scan(FixedBounds2* two, FixedBounds3* three, long* corners, int dimension, int size, int stride, int cached, Output* result);
        private static Scan native;
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = true), AOT.MonoPInvokeCallback(typeof(Scan))]
        private static void Entry(FixedBounds2* two, FixedBounds3* three, long* corners, int dimension, int size, int stride, int cached, Output* result)
            => ScanBatch(two, three, corners, dimension, size, stride, cached, result);
#endif
        private static void ScanBatch(FixedBounds2* two, FixedBounds3* three, long* corners, int dimension, int size, int stride, int cached, Output* result)
        {
            ulong digest = 14695981039346656037UL;
            for (int at = 0; at < size; at++)
            {
                int index = (at * stride) & (size - 1); bool contains;
                if (cached == 0) contains = dimension == 2 ? two[index].Contains(FixedVector2.zero) : three[index].Contains(FixedVector3.zero);
                else
                {
                    contains = true;
                    for (int axis = 0; axis < dimension; axis++)
                        if (corners[axis * size + index] > 0 || corners[(axis + dimension) * size + index] < 0) { contains = false; break; }
                }
                digest = unchecked((digest ^ (contains ? 1UL : 0UL)) * 1099511628211UL);
            }
            result->Digest = digest; result->Managed = 0; MarkManaged(ref result->Managed);
        }
#if !FIXEDPOINT_STANDALONE
        [BurstDiscard]
#endif
        private static void MarkManaged(ref int value) => value = 1;
        [TestCase(2)] [TestCase(3)]
        public void ScanActualBoundsArraysAgainstPreparedSoACornersAtDifferentWorkingSetSizes(int dimension)
        {
            Output* measured = stackalloc Output[1]; Output* reference = stackalloc Output[1];
            foreach (int size in new[] { 64, 4096, 65536 })
            {
                var two = dimension == 2 ? new FixedBounds2[size] : null; var three = dimension == 3 ? new FixedBounds3[size] : null;
                var corners = new long[dimension * 2 * size];
                for (int i = 0; i < size; i++)
                {
                    long c = i % 127 - 63, e = 4 + i % 13;
                    if (dimension == 2)
                    {
                        two[i] = FixedBounds2.FromCenterExtents(FixedVector2.FromRaw(c, c + 3), FixedVector2.FromRaw(e, e + 2));
                        var lo = two[i].min; var hi = two[i].max; corners[i] = lo.x.RawValue; corners[size + i] = lo.y.RawValue; corners[2 * size + i] = hi.x.RawValue; corners[3 * size + i] = hi.y.RawValue;
                    }
                    else
                    {
                        three[i] = FixedBounds3.FromCenterExtents(FixedVector3.FromRaw(c, c + 3, c - 4), FixedVector3.FromRaw(e, e + 2, e + 4));
                        var lo = three[i].min; var hi = three[i].max; corners[i] = lo.x.RawValue; corners[size + i] = lo.y.RawValue; corners[2 * size + i] = lo.z.RawValue;
                        corners[3 * size + i] = hi.x.RawValue; corners[4 * size + i] = hi.y.RawValue; corners[5 * size + i] = hi.z.RawValue;
                    }
                }
                fixed (FixedBounds2* p2 = two) fixed (FixedBounds3* p3 = three) fixed (long* pc = corners)
                {
                    FixedBounds2* a = p2; FixedBounds3* b = p3; long* columns = pc;
                    foreach (int stride in new[] { 1, 8191 })
                    {
                        ScanBatch(a, b, columns, dimension, size, stride, 0, reference);
                        foreach (int cache in new[] { 0, 1 })
                        {
                            string scenario = "count=" + size + ";stride=" + stride + ";prepared=" + (cache == 1);
                            Action verify = () => Assert.AreEqual(reference->Digest, measured->Digest, "AoS/SoA semantic parity");
                            long boxBytes = (long)size * (dimension == 2 ? sizeof(FixedBounds2) : sizeof(FixedBounds3));
                            Measure("B6", cache == 0 ? "AoS.Contains" : "SoA.PreparedContains", "Managed", "one array scan including loop/digest",
                                dimension, FixedRuntime.Settings.DecimalPlaces64, FixedBoundsRuntime.Settings.Representation.ToString(), scenario, size, boxBytes, cache == 0 ? 0 : corners.LongLength * sizeof(long), 0,
                                () => ScanBatch(a, b, columns, dimension, size, stride, cache, measured), () => { verify(); Assert.AreEqual(1, measured->Managed); });
#if !FIXEDPOINT_STANDALONE
                            Assert.IsTrue(BurstCompiler.IsEnabled); if (native == null) native = BurstCompiler.CompileFunctionPointer<Scan>(Entry).Invoke;
                            native(a, b, columns, dimension, size, stride, cache, measured); verify(); Assert.AreEqual(0, measured->Managed);
                            Measure("B6", cache == 0 ? "AoS.Contains" : "SoA.PreparedContains", "Burst", "one native transition plus one array scan",
                                dimension, FixedRuntime.Settings.DecimalPlaces64, FixedBoundsRuntime.Settings.Representation.ToString(), scenario, size, boxBytes, cache == 0 ? 0 : corners.LongLength * sizeof(long), 0,
                                () => native(a, b, columns, dimension, size, stride, cache, measured), () => { verify(); Assert.AreEqual(0, measured->Managed); });
#endif
                        }
                    }
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void ColdAllocationAndClearCostsOfActualManagedAndNativeBoundsArrays(int dimension)
        {
            foreach (int size in new[] { 64, 4096, 65536 })
            {
                FixedBounds2[] two = null; FixedBounds3[] three = null;
                long payload = (long)size * (dimension == 2 ? sizeof(FixedBounds2) : sizeof(FixedBounds3));
                Measure("B7", "Array.AllocateAndClear", "Managed", "array allocation and zero initialization; release untimed", dimension, FixedRuntime.Settings.DecimalPlaces64,
                    FixedBoundsRuntime.Settings.Representation.ToString(), "count=" + size, size, payload, 0, 0,
                    () => { if (dimension == 2) two = new FixedBounds2[size]; else three = new FixedBounds3[size]; },
                    () => { Assert.AreEqual(size, dimension == 2 ? two.Length : three.Length); Assert.IsTrue(dimension == 2 ? two[size - 1].IsValid : three[size - 1].IsValid); },
                    () => { two = null; three = null; }, cold: true, liveBytes: () => payload);
#if !FIXEDPOINT_STANDALONE
                NativeArray<FixedBounds2> a = default; NativeArray<FixedBounds3> b = default;
                Action release = () => { if (a.IsCreated) a.Dispose(); if (b.IsCreated) b.Dispose(); a = default; b = default; };
                try
                {
                    Measure("B7", "Array.AllocateAndClear", "NativeHost", "NativeArray Persistent allocation and zero initialization; disposal untimed", dimension, FixedRuntime.Settings.DecimalPlaces64,
                        FixedBoundsRuntime.Settings.Representation.ToString(), "count=" + size, size, payload, 0, 0,
                        () => { if (dimension == 2) a = new NativeArray<FixedBounds2>(size, Allocator.Persistent); else b = new NativeArray<FixedBounds3>(size, Allocator.Persistent); },
                        () => { Assert.AreEqual(size, dimension == 2 ? a.Length : b.Length); Assert.IsTrue(dimension == 2 ? a[size - 1].IsValid : b[size - 1].IsValid); }, release, cold: true, liveBytes: () => payload);
                }
                finally { release(); }
#endif
            }
        }
    }
}
