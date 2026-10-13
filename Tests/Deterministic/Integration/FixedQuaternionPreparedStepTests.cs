using LegendaryTools.FixedPoint;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class FixedQuaternionPreparedStepTests
    {
        [OneTimeSetUp] public void Initialize()=>FixedRuntime.Initialize();
        private static bool Legacy(in FixedQuaternion current,int degrees,out FixedQuaternion result,out FixedStatus status)
        {
            var angle=Fixed64.FromInt(degrees,out status);var delta=FixedQuaternion.AngleAxis(angle,FixedVector3.up,out var construction);status|=construction;
            result=FixedQuaternion.Multiply(current,delta,out var composition);status|=composition;
            if(status.IsSuccess()&&FixedRuntime.Settings.QuaternionNormalization==FixedQuaternionNormalizationMode.None){result=FixedQuaternion.Normalize(result,out var normalization);status|=normalization;}
            return status.IsSuccess();
        }
        private static void Equal(in FixedQuaternion a,in FixedQuaternion b) {for(int i=0;i<4;i++)Assert.AreEqual(a[i].RawValue,b[i].RawValue);}
        [Test]
        public void PreparedAndUnpreparedTransitionsMatchLegacyStatusAndRawIncludingInvalidCurrent()
        {
            foreach(int degrees in new[]{0,1,7,-14,21,int.MinValue,int.MaxValue})
            foreach(var current in new[]{FixedQuaternion.identity,FixedQuaternion.FromRaw(1,2,3,4),default,FixedQuaternion.NaN,new FixedQuaternion(Fixed64.NegativeZero,0,0,1)})
            {
                bool expected=Legacy(current,degrees,out var q,out var status);var step=QuaternionSimulationTransition.Prepare(degrees,out _);
                Assert.AreEqual(expected,QuaternionSimulationTransition.TryRotate(current,step,out var prepared,out var a));Equal(q,prepared);Assert.AreEqual(status,a);
                Assert.AreEqual(expected,QuaternionSimulationTransition.TryRotate(current,degrees,out var direct,out var b));Equal(q,direct);Assert.AreEqual(status,b);
            }
            Assert.IsFalse(QuaternionSimulationTransition.TryRotate(FixedQuaternion.identity,default(QuaternionRotationStep),out _,out var rejected));Assert.AreEqual(FixedStatus.InvalidConfiguration,rejected);
        }
        [Test]
        public void PreparedCacheRemainsValidAfterRawRollbackRestoresAnEarlierRotation()
        {
            var q=FixedQuaternion.identity;var step=QuaternionSimulationTransition.Prepare(7,out _);var saved=q;
            for(int i=0;i<40;i++){if(i==13)saved=q;QuaternionSimulationTransition.TryRotate(q,step,out q,out var status);Assert.IsTrue(status.IsSuccess());}
            var replay=saved;var legacy=saved;
            for(int i=13;i<40;i++){QuaternionSimulationTransition.TryRotate(replay,step,out replay,out _);Legacy(legacy,7,out legacy,out _);Equal(legacy,replay);}
            Equal(q,replay);
        }
#if !FIXEDPOINT_STANDALONE
        private struct Result {internal FixedQuaternion Q;internal FixedStatus Status;internal int Managed;}
        [BurstCompile(CompileSynchronously=true)]
        private struct Job:IJob
        {
            internal QuaternionRotationStep Step;internal NativeArray<Result> Results;
            public void Execute(){QuaternionSimulationTransition.TryRotate(FixedQuaternion.identity,Step,out var q,out var status);var r=new Result {Q=q,Status=status};Mark(ref r.Managed);Results[0]=r;}
            [BurstDiscard] private static void Mark(ref int value)=>value=1;
        }
        [Test]
        public void HostPreparedInputRunsWithoutManagedFallbackInBurst()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);var step=QuaternionSimulationTransition.Prepare(7,out _);
            using(var a=new NativeArray<Result>(1,Allocator.Persistent))using(var b=new NativeArray<Result>(1,Allocator.Persistent))
            {new Job {Step=step,Results=a}.Execute();new Job {Step=step,Results=b}.Schedule().Complete();Assert.AreEqual(1,a[0].Managed);Assert.AreEqual(0,b[0].Managed);Equal(a[0].Q,b[0].Q);Assert.AreEqual(a[0].Status,b[0].Status);}
        }
#endif
    }
}
