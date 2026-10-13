using System;
using System.Runtime.InteropServices;
using LegendaryTools.FixedPoint;
using LegendaryTools.FixedPoint.Tests.Benchmarks;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Tests.Benchmarks
{
    internal enum QuaternionRuntimeOperation { Size,Measure,Write,Read,Hash,HashCached,Tick,TickCached,DeltaWrite,DeltaMeasure,DeltaApply,DeltaWrongId,TickPrepared }
    internal unsafe struct QuaternionRuntimeSample
    {
        internal FixedQuaternion Q,Delta;
        internal FixedStatus Construction;
        internal int Degrees,PacketLength;
        internal FixedQuaternionCheckpoint Before,After;
        internal ulong CachedHash;
    }
    internal unsafe struct QuaternionRuntimeSpec
    {
        internal QuaternionRuntimeOperation Operation;
        internal FixedQuaternionNetworkProfile Profile;
        internal FixedQuaternionDeltaMode Mode;
        internal byte* Packets;
        internal int Stride;
        internal QuaternionRotationStep* Steps;
    }
    internal struct QuaternionRuntimeResult {internal ulong Digest;internal uint Status;internal long Bytes;internal int Managed;}

#if !FIXEDPOINT_STANDALONE
    [BurstCompile]
#endif
    [Category("QuaternionBenchmark"),Explicit("Select QuaternionRuntimeBenchmarks explicitly for canonical, delta and history CPU/memory costs.")]
    public sealed unsafe class QuaternionRuntimeBenchmarks : QuaternionBenchmarkFixture
    {
        private static QuaternionRuntimeSample[] Initial(int count)
        {
            var data=new QuaternionRuntimeSample[count];int[] degrees={0,7,-14,21};
            for(int i=0;i<count;i++)
            {
                int angle=degrees[i%degrees.Length];var fixedAngle=Fixed64.FromInt(angle,out var status);
                var delta=FixedQuaternion.AngleAxis(fixedAngle,FixedVector3.up,out var construction);
                data[i]=new QuaternionRuntimeSample {Q=FixedQuaternion.Euler(i%37,i%53,i%97),Degrees=angle,Delta=delta,Construction=status|construction};
            }
            return data;
        }
        private static ulong Hash(ulong hash,in FixedQuaternion value) {for(int i=0;i<4;i++)hash=Fold(hash,unchecked((ulong)value[i].RawValue));return hash;}
        private static void Run(QuaternionRuntimeSample* inputs,int count,QuaternionRuntimeSpec* spec,QuaternionRuntimeResult* output)
        {
            var result=new QuaternionRuntimeResult {Digest=14695981039346656037UL};byte* scratch=stackalloc byte[FixedQuaternionDelta.MaxPacketBytes];byte* restored=stackalloc byte[40];
            var codec=default(FixedQuaternionPolicy);
            for(int i=0;i<count;i++)
            {
                ref var item=ref inputs[i];uint status=0;long bytes=0;FixedQuaternion q=default;bool consume=false;ulong scalar=0;
                switch(spec->Operation)
                {
                    case QuaternionRuntimeOperation.Size:bytes=codec.Measure(item.Q);break;
                    case QuaternionRuntimeOperation.Measure:case QuaternionRuntimeOperation.Write:
                    {var writer=spec->Operation==QuaternionRuntimeOperation.Measure?DeterministicWriter.CreateMeasure():new DeterministicWriter(scratch,FixedQuaternionDelta.MaxPacketBytes);status=(uint)codec.TryWrite(ref writer,item.Q);bytes=writer.Position;if(!writer.IsMeasuring)for(int j=0;j<writer.Position;j++)result.Digest=Fold(result.Digest,scratch[j]);break;}
                    case QuaternionRuntimeOperation.Read:
                    {var reader=new DeterministicReader(spec->Packets+i*spec->Stride,32);status=(uint)codec.TryRead(ref reader,out q);bytes=reader.Position;consume=true;break;}
                    case QuaternionRuntimeOperation.Hash:scalar=item.Before.PayloadHash;break;
                    case QuaternionRuntimeOperation.HashCached:scalar=item.CachedHash;break;
                    case QuaternionRuntimeOperation.Tick:QuaternionSimulationTransition.TryRotate(item.Q,item.Degrees,out q,out var transition);status=(uint)transition;consume=true;break;
                    case QuaternionRuntimeOperation.TickCached:
                    {
                        q=FixedQuaternion.Multiply(item.Q,item.Delta,out var composition);var flags=item.Construction|composition;
                        if(flags.IsSuccess() && FixedRuntime.Settings.QuaternionNormalization==FixedQuaternionNormalizationMode.None){q=FixedQuaternion.Normalize(q,out var normalization);flags|=normalization;}
                        status=(uint)flags;consume=true;break;
                    }
                    case QuaternionRuntimeOperation.TickPrepared:
                        QuaternionSimulationTransition.TryRotate(item.Q,spec->Steps[i%4],out q,out var preparedStatus);status=(uint)preparedStatus;consume=true;break;
                    case QuaternionRuntimeOperation.DeltaWrite:case QuaternionRuntimeOperation.DeltaMeasure:
                    {
                        var writer=spec->Operation==QuaternionRuntimeOperation.DeltaMeasure?DeterministicWriter.CreateMeasure():new DeterministicWriter(scratch,FixedQuaternionDelta.MaxPacketBytes);
                        status=(uint)FixedQuaternionDelta.TryWrite(ref writer,item.Before,item.After,spec->Profile,out var kind,spec->Mode);bytes=writer.Position;scalar=(uint)kind;
                        if(!writer.IsMeasuring)for(int j=0;j<writer.Position;j++)result.Digest=Fold(result.Digest,scratch[j]);break;
                    }
                    case QuaternionRuntimeOperation.DeltaApply:case QuaternionRuntimeOperation.DeltaWrongId:
                    {
                        for(int j=0;j<40;j++)restored[j]=0xa5;var reader=new DeterministicReader(spec->Packets+i*spec->Stride,item.PacketLength);
                        var before=spec->Operation==QuaternionRuntimeOperation.DeltaWrongId?new FixedQuaternionCheckpoint(item.Before.Buffer,item.Before.Length,item.Before.Id+10000,item.Before.ProfileFingerprint):item.Before;
                        status=(uint)FixedQuaternionDelta.TryApply(ref reader,before,spec->Profile,restored,40,out var checkpoint,out q);bytes=reader.Position;scalar=checkpoint.Id;consume=true;
                        for(int j=0;j<40;j++)result.Digest=Fold(result.Digest,restored[j]);break;
                    }
                }
                if(consume)result.Digest=Hash(result.Digest,q);result.Digest=Fold(result.Digest,scalar);result.Digest=Fold(result.Digest,status);result.Digest=Fold(result.Digest,(ulong)bytes);result.Status|=status;result.Bytes+=bytes;
            }
            MarkManaged(ref result.Managed);*output=result;
        }
#if !FIXEDPOINT_STANDALONE
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(QuaternionRuntimeSample* inputs,int count,QuaternionRuntimeSpec* spec,QuaternionRuntimeResult* output);
        private static Kernel native;
        [BurstCompile(CompileSynchronously=true,DisableDirectCall=true)]
        [AOT.MonoPInvokeCallback(typeof(Kernel))]
        private static void Entry(QuaternionRuntimeSample* inputs,int count,QuaternionRuntimeSpec* spec,QuaternionRuntimeResult* output)=>Run(inputs,count,spec,output);
        [BurstDiscard]
#endif
        private static void MarkManaged(ref int value)=>value=1;
        private void Batch(QuaternionRuntimeSample[] data,QuaternionRuntimeSpec spec,string opportunity,string scenario,long cacheBytes=0,ulong? expected=null)
        {
            QuaternionRuntimeSpec* active=stackalloc QuaternionRuntimeSpec[1];*active=spec;QuaternionRuntimeResult* reference=stackalloc QuaternionRuntimeResult[1];QuaternionRuntimeResult* measured=stackalloc QuaternionRuntimeResult[1];
            fixed(QuaternionRuntimeSample* pin=data)
            {
                QuaternionRuntimeSample* input=pin;Run(input,data.Length,active,reference);if(expected.HasValue)Assert.AreEqual(expected.Value,reference->Digest,"cached result/status parity");
                Action verify=()=>{Assert.AreEqual(reference->Digest,measured->Digest);Assert.AreEqual(reference->Status,measured->Status);Assert.AreEqual(reference->Bytes,measured->Bytes);};
                Measure(opportunity,spec.Operation.ToString(),"Managed","preallocated public facade/adapter batch + loop/digest",FixedRuntime.Settings.DecimalPlaces64,FixedRuntime.Settings.MathProfile.ToString(),scenario,data.Length,
                    (long)data.Length*sizeof(QuaternionRuntimeSample),cacheBytes,reference->Bytes,()=>Run(input,data.Length,active,measured),()=>{verify();Assert.AreEqual(1,measured->Managed);});
#if !FIXEDPOINT_STANDALONE
                Assert.IsTrue(BurstCompiler.IsEnabled);if(native==null)native=BurstCompiler.CompileFunctionPointer<Kernel>(Entry).Invoke;
                native(input,data.Length,active,measured);verify();Assert.AreEqual(0,measured->Managed);
                Measure(opportunity,spec.Operation.ToString(),"Burst","one function pointer transition + public facade/adapter batch + loop/digest",FixedRuntime.Settings.DecimalPlaces64,FixedRuntime.Settings.MathProfile.ToString(),scenario,data.Length,
                    (long)data.Length*sizeof(QuaternionRuntimeSample),cacheBytes,reference->Bytes,()=>native(input,data.Length,active,measured),()=>{verify();Assert.AreEqual(0,measured->Managed);});
#endif
            }
        }
        private static ulong Digest(QuaternionRuntimeSample[] data,QuaternionRuntimeSpec spec) {QuaternionRuntimeResult result;fixed(QuaternionRuntimeSample* input=data)Run(input,data.Length,&spec,&result);return result.Digest;}
        [Test]
        public void MeasureCanonicalSizeValidationWriteAndRead()
        {
            foreach(int count in new[]{128,4096})
            {
                var data=Initial(count);var packet=new byte[count*32];
                fixed(byte* pin=packet)
                {
                    var writer=new DeterministicWriter(pin,packet.Length);var codec=default(FixedQuaternionPolicy);foreach(var item in data)codec.TryWrite(ref writer,item.Q).ThrowIfFailure();
                    var spec=new QuaternionRuntimeSpec {Packets=pin,Stride=32};
                    foreach(var operation in new[]{QuaternionRuntimeOperation.Size,QuaternionRuntimeOperation.Measure,QuaternionRuntimeOperation.Write,QuaternionRuntimeOperation.Read})
                    {spec.Operation=operation;Batch(data,spec,"Q6","count="+count+";canonical-exact-32B");}
                }
            }
        }
        [Test]
        public void ComparePerEntityAngleConstructionWithImmutableInputDeltaCache()
        {
            var data=Initial(128);var spec=new QuaternionRuntimeSpec {Operation=QuaternionRuntimeOperation.Tick};ulong expected=Digest(data,spec);
            foreach(var operation in new[]{QuaternionRuntimeOperation.Tick,QuaternionRuntimeOperation.TickCached})
            {spec.Operation=operation;Batch(data,spec,"Q7","shared input keys=0,7,-14,21;normalization="+FixedRuntime.Settings.QuaternionNormalization,operation==QuaternionRuntimeOperation.TickCached?4L*(32+4):0,expected);}
        }
        [Test]
        public void MeasureDeltaDecodeHashDensityAndMeasurementWriterCosts()
        {
            const int count=64;byte* before=stackalloc byte[count*40];byte* after=stackalloc byte[count*40];byte* packets=stackalloc byte[count*FixedQuaternionDelta.MaxPacketBytes];
            foreach(int encoding in new[]{0,1,2,3,4,5}) foreach(FixedQuaternionDeltaMode mode in Enum.GetValues(typeof(FixedQuaternionDeltaMode))) foreach(string density in new[]{"unchanged","sparse","dense"})
            {
                var profile=encoding<3?new FixedQuaternionNetworkProfile(FixedRuntime.Settings,encoding==0?FixedCodec.FixedWidth:encoding==1?FixedCodec.VarInt:FixedCodec.BitPacked,
                    lowerRaw:encoding==2?unchecked((ulong)-2000L):0,upperRaw:encoding==2?2000UL:0):FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings,encoding==3?32:encoding==4?48:64,true);
                var data=Initial(count);var spec=new QuaternionRuntimeSpec {Profile=profile,Mode=mode,Packets=packets,Stride=FixedQuaternionDelta.MaxPacketBytes};
                for(int i=0;i<count;i++)
                {
                    var q=FixedQuaternion.FromRaw(i+1,-i-1,i/2,1000);var next=density=="unchanged"?q:density=="sparse"?FixedQuaternion.FromRaw(i+1,-i-2,i/2,1000):data[i].Q;
                    Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(before+i*40,40,(ulong)i,q,profile,out data[i].Before,out _));
                    Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(after+i*40,40,(ulong)i+1,next,profile,out data[i].After,out _));data[i].CachedHash=data[i].Before.PayloadHash;
                    var writer=new DeterministicWriter(packets+i*spec.Stride,spec.Stride);FixedQuaternionDelta.TryWrite(ref writer,data[i].Before,data[i].After,profile,out _,mode).ThrowIfFailure();data[i].PacketLength=writer.Position;
                }
                string scenario="encoding="+encoding+";mode="+mode+";density="+density;
                spec.Operation=QuaternionRuntimeOperation.Hash;ulong expected=Digest(data,spec);
                foreach(var operation in new[]{QuaternionRuntimeOperation.Hash,QuaternionRuntimeOperation.HashCached,QuaternionRuntimeOperation.DeltaMeasure,QuaternionRuntimeOperation.DeltaWrite,QuaternionRuntimeOperation.DeltaApply,QuaternionRuntimeOperation.DeltaWrongId})
                {spec.Operation=operation;Batch(data,spec,"Q8",scenario,operation==QuaternionRuntimeOperation.HashCached?count*8L:0,operation==QuaternionRuntimeOperation.HashCached?expected:(ulong?)null);}
                // A borrowed checkpoint is mutable: cached hashes require an immutable/owned-byte lifetime.
                ulong captured=data[0].CachedHash;before[0]^=1;Assert.AreNotEqual(captured,data[0].Before.PayloadHash);before[0]^=1;
            }
        }
        [Test]
        public void MeasureProductionPreparedInputAgainstTheSameUnpreparedTransition()
        {
            var data=Initial(128);QuaternionRotationStep* steps=stackalloc QuaternionRotationStep[4];
            for(int i=0;i<4;i++)steps[i]=QuaternionSimulationTransition.Prepare(data[i].Degrees,out _);
            var spec=new QuaternionRuntimeSpec {Operation=QuaternionRuntimeOperation.Tick,Steps=steps};ulong expected=Digest(data,spec);
            spec.Operation=QuaternionRuntimeOperation.TickPrepared;
            Batch(data,spec,"Q7","shared input keys=0,7,-14,21;normalization="+FixedRuntime.Settings.QuaternionNormalization,4L*sizeof(QuaternionRotationStep),expected);
        }
        [Test]
        public void MeasureHistoryCaptureRestoreDensityPageSizeAndLiveRetention()
        {
            const int frames=16;var codec=default(FixedQuaternionPolicy);
            foreach(int count in new[]{128,512}) foreach(int density in new[]{0,1,100})
            {
                int bytes=count*32;var stream=new byte[frames*bytes];var values=Initial(count);
                fixed(byte* pin=stream)
                {
                    for(int tick=0;tick<frames;tick++)
                    {
                        int changed=(count*density+99)/100;for(int i=0;i<changed;i++)values[i].Q.x=Fixed64.FromRaw(values[i].Q.x.RawValue+1);
                        var writer=new DeterministicWriter(pin+tick*bytes,bytes);foreach(var value in values)codec.TryWrite(ref writer,value.Q).ThrowIfFailure();Assert.AreEqual(bytes,writer.Position);
                    }
                    foreach(SnapshotPolicy policy in Enum.GetValues(typeof(SnapshotPolicy))) foreach(int pageSize in policy==SnapshotPolicy.CopyOnWrite?new[]{256,4096}:new[]{256})
                    {
                        History(new DeterministicRuntimeContext(DeterministicBackend.Managed),pin,count,frames,policy,pageSize,density);
#if !FIXEDPOINT_STANDALONE
                        History(new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler,new NativeDeterministicMemoryFactory()),pin,count,frames,policy,pageSize,density);
#endif
                    }
                }
            }
        }
        private void History(DeterministicRuntimeContext context,byte* stream,int count,int frames,SnapshotPolicy policy,int pageSize,int density)
        {
            int bytes=count*32;using var history=new DeterministicWorldHistory(context,bytes,frames,policy,8,pageSize);
            string backend=context.Backend==DeterministicBackend.Managed?"Managed":"NativeHost";
            string scenario="count="+count+";frames="+frames+";policy="+policy+";page="+pageSize+";changed-percent="+density+";clustered";
            Action capture=()=>{for(int tick=0;tick<frames;tick++)history.TryCapture(tick,stream+tick*bytes,bytes).ThrowIfFailure();};
            Action verify=()=>{for(int tick=0;tick<frames;tick++){history.TryRestore(tick,out byte* restored,out int length).ThrowIfFailure();Assert.AreEqual(bytes,length);for(int i=0;i<bytes;i++)if(restored[i]!=stream[tick*bytes+i])Assert.Fail("retained raw byte "+i);}};
            Func<long> live=()=>{if(policy==SnapshotPolicy.CopyOnWrite)return(long)history.LivePages*pageSize;long total=0;for(int tick=0;tick<frames;tick++){history.TryGetStoredBytes(tick,out int stored).ThrowIfFailure();total+=stored;}return total;};
            Measure("Q9","HistoryCapture",backend,"host capture/hash/delta/page management; preencoded frames",3,FixedRuntime.Settings.MathProfile.ToString(),scenario,frames,(long)bytes*frames,history.ReservedBytes,(long)bytes*frames,capture,verify,()=>history.TryReset().ThrowIfFailure(),live);
            byte* restoredPacket=null;int restoredLength=0;
            foreach(int age in new[]{0,7,15})
            {
                int target=frames-1-age;
                Measure("Q9","HistoryRestore",backend,"host reconstruction + checkpoint hash verification",3,FixedRuntime.Settings.MathProfile.ToString(),scenario+";age="+age,1,(long)bytes*frames,history.ReservedBytes,bytes,
                    ()=>history.TryRestore(target,out restoredPacket,out restoredLength).ThrowIfFailure(),()=>{Assert.AreEqual(bytes,restoredLength);for(int i=0;i<bytes;i++)if(restoredPacket[i]!=stream[target*bytes+i])Assert.Fail("restored raw byte "+i);},liveBytes:live);
            }
        }
#if !FIXEDPOINT_STANDALONE
        private struct TickResult {internal FixedQuaternion Q;internal FixedStatus Status;internal int Managed;}
        [BurstCompile(CompileSynchronously=true)]
        private struct OneJob : IJob
        {
            [ReadOnly] internal NativeArray<QuaternionRuntimeSample> Inputs;
            internal NativeArray<TickResult> Outputs;
            public void Execute() {for(int i=0;i<Inputs.Length;i++)Outputs[i]=Evaluate(Inputs[i]);}
        }
        [BurstCompile(CompileSynchronously=true)]
        private struct ParallelJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<QuaternionRuntimeSample> Inputs;
            internal NativeArray<TickResult> Outputs;
            public void Execute(int index)=>Outputs[index]=Evaluate(Inputs[index]);
        }
        private static TickResult Evaluate(QuaternionRuntimeSample input)
        {
            QuaternionSimulationTransition.TryRotate(input.Q,input.Degrees,out var q,out var status);var result=new TickResult {Q=q,Status=status};MarkManaged(ref result.Managed);return result;
        }
        [Test]
        public void MeasureBurstSchedulingCompletionAndBatchGranularity()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            foreach(int count in new[]{1,64,1024})
            using(var inputs=new NativeArray<QuaternionRuntimeSample>(Initial(count),Allocator.Persistent))
            using(var outputs=new NativeArray<TickResult>(count,Allocator.Persistent))
            {
                var expected=new TickResult[count];for(int i=0;i<count;i++)expected[i]=Evaluate(inputs[i]);
                Action verify=()=>{for(int i=0;i<count;i++){var actual=outputs[i];Assert.AreEqual(0,actual.Managed,"scheduled Burst fallback");Assert.AreEqual(expected[i].Status,actual.Status);for(int c=0;c<4;c++)Assert.AreEqual(expected[i].Q[c].RawValue,actual.Q[c].RawValue);}};
                var one=new OneJob {Inputs=inputs,Outputs=outputs};
                Measure("Q10","OneJobScheduleComplete","BurstJob","includes scheduling + completion + per-entity transition",3,FixedRuntime.Settings.MathProfile.ToString(),"count="+count,count,(long)count*sizeof(QuaternionRuntimeSample),count*(long)sizeof(TickResult),0,
                    ()=>one.Schedule().Complete(),verify,steadyGc:false);
                foreach(int batch in new[]{1,16,128})
                {
                    var parallel=new ParallelJob {Inputs=inputs,Outputs=outputs};
                    Measure("Q10","ParallelScheduleComplete","BurstJob","includes scheduling + completion + per-entity transition",3,FixedRuntime.Settings.MathProfile.ToString(),"count="+count+";batch="+batch,count,(long)count*sizeof(QuaternionRuntimeSample),count*(long)sizeof(TickResult),0,
                        ()=>parallel.Schedule(count,batch).Complete(),verify,steadyGc:false);
                }
            }
        }
#endif
    }
}
