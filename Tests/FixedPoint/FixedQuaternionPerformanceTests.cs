using System;
using System.Diagnostics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedQuaternionPerformanceTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        [Test] public void WarmKernelsAndPointerCodecsAllocateNoManagedHeap()
        {
            var sample=FixedQuaternionReplayCorpus.Create()[700]; ulong hash=0;
            for (int i=0;i<100;i++) hash^=FixedQuaternionReplayCorpus.Hash(FixedQuaternionReplayCorpus.Evaluate(sample));
            long before=GC.GetAllocatedBytesForCurrentThread();
            for (int i=0;i<1000;i++) hash^=FixedQuaternionReplayCorpus.Hash(FixedQuaternionReplayCorpus.Evaluate(sample));
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.AreEqual(0,allocated); GC.KeepAlive(hash);
        }
        [Test] public void RecordCompositionNormalizationInterpolationAndActualPayloadCosts()
        {
            var q=FixedQuaternion.Euler(10,20,30); var b=FixedQuaternion.Euler(50,60,70); ulong hash=0;
            foreach (var mode in new[] {FixedQuaternionNormalizationMode.None,FixedQuaternionNormalizationMode.NormalizeResult})
            {
                var state=new FixedRuntimeState(new FixedSettings(quaternionNormalization:mode));
                for (int i=0;i<100;i++) FixedQuaternionKernel.Multiply(q.Bits,b.Bits,state,out _);
                var watch=Stopwatch.StartNew(); for (int i=0;i<10000;i++) hash^=FixedQuaternionKernel.Multiply(q.Bits,b.Bits,state,out _).X; watch.Stop();
                TestContext.Progress.WriteLine("QUATERNION_BENCH Multiply mode="+mode+" iterations=10000 ms="+watch.Elapsed.TotalMilliseconds);
            }
            var timer=Stopwatch.StartNew(); for (int i=0;i<1000;i++) hash^=FixedQuaternion.Slerp(q,b,Fixed64.FromRatio(1,3)).x.Bits; timer.Stop();
            TestContext.Progress.WriteLine("QUATERNION_BENCH Slerp iterations=1000 ms="+timer.Elapsed.TotalMilliseconds);
            byte* bytes=stackalloc byte[40];
            foreach (var profile in new[] {new FixedQuaternionNetworkProfile(FixedRuntime.Settings),new FixedQuaternionNetworkProfile(FixedRuntime.Settings,FixedCodec.VarInt),FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings,32,true),FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings,48,true),FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings,64,true)})
            { var writer=new FixedBufferWriter(bytes,40); Assert.IsTrue(FixedQuaternionNetwork.TryWrite(ref writer,q,profile,out _)); TestContext.Progress.WriteLine("QUATERNION_WIRE "+profile.Codec+" "+profile.TotalBits+" bytes="+writer.BytesWritten); }
            GC.KeepAlive(hash);
        }
    }
}
