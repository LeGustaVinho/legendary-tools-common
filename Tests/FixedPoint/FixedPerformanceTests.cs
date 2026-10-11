using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedPerformanceTests
    {
        [Test]
        public void NumericKernelsAndPointerCodecsAllocateNoManagedMemoryAfterWarmup()
        {
            FixedRuntime.Initialize();
            byte* bytes = stackalloc byte[16];
            var profile = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64);
            ulong checksum = Run(bytes, profile, 100);
            long before = GC.GetAllocatedBytesForCurrentThread();
            checksum ^= Run(bytes, profile, 10000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "Arithmetic and pointer network kernels must remain allocation-free.");
            Assert.AreNotEqual(0UL, checksum);
        }
        private static ulong Run(byte* bytes, FixedNetworkProfile profile, int count)
        {
            ulong checksum = 0;
            for (int i = 0; i < count; i++)
            {
                Fixed64 a = Fixed64.FromRaw(i + 1), b = Fixed64.FromRaw(1234);
                Fixed64 value = (a * b + b) / b;
                value += FixedMath.Sqrt(a);
                var writer = new FixedBufferWriter(bytes, 16);
                FixedNetwork.TryWrite(ref writer, value, profile, out _);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                FixedNetwork.TryRead(ref reader, profile, out Fixed64 result, out _);
                checksum += unchecked((ulong)result.RawValue);
            }
            return checksum;
        }
    }
}
