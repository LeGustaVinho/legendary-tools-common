using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
#endif

namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
#if !FIXEDPOINT_STANDALONE
    [BurstCompile]
#endif
    [Category("BoundsBenchmark"), Explicit("Select this fixture explicitly to measure Bounds CPU/memory opportunities.")]
    public sealed unsafe class BoundsCpuBenchmarks : BoundsBenchmarkFixture
    {
#if !FIXEDPOINT_STANDALONE
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(BoundsBenchmarkSample* data, int count, int repeats, BoundsBenchmarkSpec* spec, BoundsBenchmarkResult* output);
        private static Kernel native;
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = true)]
        [AOT.MonoPInvokeCallback(typeof(Kernel))]
        private static void Entry(BoundsBenchmarkSample* data, int count, int repeats, BoundsBenchmarkSpec* spec, BoundsBenchmarkResult* output)
            => BoundsBenchmarkWorkload.Run(data, count, repeats, spec, output);
#endif
        private static BoundsBenchmarkSpec Spec(int dimensions, int precision, FixedBoundsRepresentation representation, BoundsOperation operation)
            => new BoundsBenchmarkSpec { Dimension = dimensions, Numeric = new FixedRuntimeState(new FixedSettings(3, precision)),
                Geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation)), Operation = operation, Queries = 1 };
        private void Batch(BoundsBenchmarkSample[] data, BoundsBenchmarkSpec spec, string opportunity, string scenario, int repeats = 2, long cacheBytes = 0, ulong? expectedDigest = null)
        {
            BoundsBenchmarkSpec* active = stackalloc BoundsBenchmarkSpec[1]; *active = spec;
            BoundsBenchmarkResult* reference = stackalloc BoundsBenchmarkResult[1]; BoundsBenchmarkResult* measured = stackalloc BoundsBenchmarkResult[1];
            fixed (BoundsBenchmarkSample* pin = data)
            {
                BoundsBenchmarkSample* input = pin;
                BoundsBenchmarkWorkload.Run(input, data.Length, repeats, active, reference);
                if (expectedDigest.HasValue) Assert.AreEqual(expectedDigest.Value, reference->Digest, "Prototype semantic parity");
                int items = checked(data.Length * repeats * (spec.Operation == BoundsOperation.ManyContains || spec.Operation == BoundsOperation.CachedContains || spec.Operation == BoundsOperation.CachedBuildAndContains ? spec.Queries : 1));
                long payload = (long)data.Length * sizeof(BoundsBenchmarkSample);
                Action verify = () => { Assert.AreEqual(reference->Digest, measured->Digest); Assert.AreEqual(reference->Status, measured->Status); Assert.AreEqual(reference->Bytes, measured->Bytes); };
                Measure(opportunity, spec.Operation.ToString(), "Managed", "kernel batch including loop/digest", spec.Dimension, spec.Numeric.Settings.DecimalPlaces64,
                    spec.Geometry.Settings.Representation.ToString(), scenario, items, payload, cacheBytes, reference->Bytes / 8,
                    () => BoundsBenchmarkWorkload.Run(input, data.Length, repeats, active, measured), () => { verify(); Assert.AreEqual(1, measured->Managed); });
#if !FIXEDPOINT_STANDALONE
                Assert.IsTrue(BurstCompiler.IsEnabled);
                if (native == null) native = BurstCompiler.CompileFunctionPointer<Kernel>(Entry).Invoke;
                native(input, data.Length, repeats, active, measured); verify(); Assert.AreEqual(0, measured->Managed, "Burst fallback is forbidden");
                Measure(opportunity, spec.Operation.ToString(), "Burst", "function pointer batch including one native transition and loop/digest", spec.Dimension, spec.Numeric.Settings.DecimalPlaces64,
                    spec.Geometry.Settings.Representation.ToString(), scenario, items, payload, cacheBytes, reference->Bytes / 8,
                    () => native(input, data.Length, repeats, active, measured), () => { verify(); Assert.AreEqual(0, measured->Managed); });
#endif
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void IsolateValidationQueriesWideArithmeticAndChangedMutations(int dimensions)
        {
            foreach (int precision in new[] { 3, 18 }) foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var spec = Spec(dimensions, precision, representation, BoundsOperation.LoopControl); var data = BoundsBenchmarkWorkload.Create(128, dimensions, spec.Numeric, spec.Geometry);
                for (int operation = (int)BoundsOperation.LoopControl; operation <= (int)BoundsOperation.Encapsulate; operation++)
                {
                    spec.Operation = (BoundsOperation)operation;
                    string opportunity = operation <= (int)BoundsOperation.ClosestPoint ? "B1" : operation <= (int)BoundsOperation.RayTinyOverflow ? "B2" : "B3";
                    Batch(data, spec, opportunity, spec.Operation.ToString());
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void CompareValidatedQueriesWithPreparedCornersIncludingCacheConstruction(int dimensions)
        {
            BoundsBenchmarkSpec* active = stackalloc BoundsBenchmarkSpec[1]; BoundsBenchmarkResult* result = stackalloc BoundsBenchmarkResult[1];
            foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var spec = Spec(dimensions, 3, representation, BoundsOperation.ManyContains); var data = BoundsBenchmarkWorkload.Create(256, dimensions, spec.Numeric, spec.Geometry);
                foreach (int queries in new[] { 1, 8, 32 })
                {
                    spec.Queries = queries; spec.Operation = BoundsOperation.ManyContains; *active = spec;
                    fixed (BoundsBenchmarkSample* input = data) BoundsBenchmarkWorkload.Run(input, data.Length, 2, active, result);
                    ulong expected = result->Digest;
                    Batch(data, spec, "B4", "queries-per-box=" + queries, expectedDigest: expected);
                    spec.Operation = BoundsOperation.CachedContains;
                    Batch(data, spec, "B4", "prepared-finite-inputs;queries-per-box=" + queries, cacheBytes: data.Length * dimensions * 16L, expectedDigest: expected);
                    spec.Operation = BoundsOperation.CachedBuildAndContains;
                    Batch(data, spec, "B4", "build-two-views-per-box;queries-per-box=" + queries, cacheBytes: data.Length * dimensions * 16L, expectedDigest: expected);
                }
            }
        }
        private static FixedBoundsNetworkProfile Profile(in BoundsBenchmarkSpec spec, FixedCodec codec, bool lossy)
        {
            var axis = new FixedNetworkProfile(spec.Numeric.Settings, FixedKind.Signed64, codec, lossy ? 0 : -1, lossy,
                codec == FixedCodec.BitPacked ? unchecked((ulong)-1000L) : 0, codec == FixedCodec.BitPacked ? 1000UL : 0);
            var vector = new FixedVectorNetworkProfile(axis, spec.Dimension); return new FixedBoundsNetworkProfile(spec.Geometry.Settings.Representation, vector, vector);
        }
        private static void CheckPrototype(BoundsBenchmarkSample[] data, in BoundsBenchmarkSpec spec)
        {
            byte* original = stackalloc byte[300]; byte* candidate = stackalloc byte[300];
            foreach (bool byteCopy in new[] { false, true })
            for (int sample = 0; sample < data.Length; sample++) for (int capacity = 0; capacity <= 60; capacity += 3)
            {
                for (int b = 0; b < 300; b++) original[b] = candidate[b] = 0xA5;
                var a = new FixedBufferWriter(original, capacity, spec.Offset); var bWriter = new FixedBufferWriter(candidate, capacity, spec.Offset);
                bool okA = FixedBoundsNetworkKernel.Write(ref a, data[sample].Box, spec.Dimension, spec.Profile, spec.Numeric, spec.Geometry, out var sa);
                bool okB = BoundsBenchmarkWorkload.SingleStage(ref bWriter, data[sample].Box, spec, byteCopy, out var sb);
                Assert.AreEqual(okA, okB); Assert.AreEqual(sa, sb); Assert.AreEqual(a.BitPosition, bWriter.BitPosition);
                for (int b = 0; b < 300; b++) if (original[b] != candidate[b]) Assert.Fail("Codec prototype changed byte " + b + " / sample " + sample);
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void SeparateCodecValidationStagingBitCopyQuantizationAndFailureCosts(int dimensions)
        {
            byte* packet = stackalloc byte[300];
            BoundsBenchmarkSpec* active = stackalloc BoundsBenchmarkSpec[1]; BoundsBenchmarkResult* expected = stackalloc BoundsBenchmarkResult[1];
            foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            foreach (FixedCodec codec in Enum.GetValues(typeof(FixedCodec))) foreach (bool lossy in new[] { false, true })
            foreach (int offset in codec == FixedCodec.BitPacked ? new[] { 0, 5 } : new[] { 0 })
            {
                var spec = Spec(dimensions, 3, representation, BoundsOperation.NetworkWrite); spec.Offset = offset; spec.Profile = Profile(spec, codec, lossy);
                var data = BoundsBenchmarkWorkload.Create(64, dimensions, spec.Numeric, spec.Geometry); CheckPrototype(data, spec);
                *active = spec;
                fixed (BoundsBenchmarkSample* input = data) BoundsBenchmarkWorkload.Run(input, data.Length, 2, active, expected);
                foreach (var operation in new[] { BoundsOperation.NetworkWrite, BoundsOperation.NetworkWriteSingleStage, BoundsOperation.NetworkWriteSingleStageByteCopy, BoundsOperation.NetworkCanonicalize, BoundsOperation.NetworkShortWrite, BoundsOperation.NetworkRead, BoundsOperation.DescriptorWrite, BoundsOperation.DescriptorRead })
                {
                    spec.Operation = operation;
                    if ((operation == BoundsOperation.DescriptorWrite || operation == BoundsOperation.DescriptorRead) && offset != 0) continue;
                    if (operation == BoundsOperation.NetworkRead || operation == BoundsOperation.DescriptorRead)
                    {
                        var writer = new FixedBufferWriter(packet, 300, offset);
                        bool ok = operation == BoundsOperation.DescriptorRead ? FixedBoundsNetworkKernel.WriteDescriptor(ref writer, spec.Profile, out var status) :
                            FixedBoundsNetworkKernel.Write(ref writer, data[0].Box, dimensions, spec.Profile, spec.Numeric, spec.Geometry, out status);
                        Assert.IsTrue(ok); spec.Packet = packet; spec.PacketLength = writer.BytesWritten;
                    }
                    Batch(data, spec, "B5", codec + ";lossy=" + lossy + ";bit-offset=" + offset, expectedDigest:
                        operation == BoundsOperation.NetworkWrite || operation == BoundsOperation.NetworkWriteSingleStage || operation == BoundsOperation.NetworkWriteSingleStageByteCopy ? expected->Digest : (ulong?)null);
                }
                if (codec == FixedCodec.FixedWidth && !lossy)
                {
                    var writer = new FixedBufferWriter(packet, 300); Assert.IsTrue(FixedBoundsNetworkKernel.Write(ref writer, data[0].Box, dimensions, spec.Profile, spec.Numeric, spec.Geometry, out _));
                    int at = dimensions * 16 - 8; ulong invalid = representation == FixedBoundsRepresentation.MinMax ? 0x8000000000000002UL : ulong.MaxValue;
                    for (int b = 0; b < 8; b++) packet[at + b] = (byte)(invalid >> (8 * b));
                    var reader = new FixedBufferReader(packet, writer.BytesWritten);
                    Assert.IsFalse(FixedBoundsNetworkKernel.Read(ref reader, dimensions, spec.Profile, spec.Numeric, spec.Geometry, out _, out var rejected)); Assert.AreEqual(FixedStatus.InvalidEncoding, rejected);
                    spec.Operation = BoundsOperation.NetworkInvalidLastRead; spec.Packet = packet; spec.PacketLength = writer.BytesWritten;
                    Batch(data, spec, "B5", "invalid-complete-last-field");
                }
            }
        }
        [Test] public void ReportValueAndWideTemporaryFootprints()
        {
            Assert.AreEqual(32, Marshal.SizeOf<FixedBounds2>()); Assert.AreEqual(48, Marshal.SizeOf<FixedBounds3>());
            TestContext.Out.WriteLine("BOUNDS_LAYOUT bounds2=" + sizeof(FixedBounds2) + " bounds3=" + sizeof(FixedBounds3) +
                " kernelBits=" + sizeof(FixedBoundsBits) + " bigUInt=" + sizeof(FixedBigUInt) + " signedWide=" + sizeof(FixedQ) +
                " rayFractionFields=" + (sizeof(FixedQ) + sizeof(ulong)) + " boundsStage=60 vectorStage=30 descriptorStage=264");
        }
    }
}
