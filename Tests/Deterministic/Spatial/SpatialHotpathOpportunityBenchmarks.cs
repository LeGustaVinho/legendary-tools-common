using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LegendaryTools.Deterministic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialHotpathOpportunityBenchmarks.Owner<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy,SpatialBounds2NetworkAccess>.Work))]
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialHotpathOpportunityBenchmarks.Owner<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy,SpatialBounds3NetworkAccess>.Work))]
#endif

namespace LegendaryTools.Deterministic.Tests
{
    [Explicit("Run isolated hotpath measurements separately from correctness tests.")]
    public unsafe class SpatialHotpathOpportunityBenchmarks
    {
        internal enum Operation { Dispatch,Query,Direct,Batch,PerEntityBatch,FailedBatch,SyncDense,SyncForced,SyncOrdinal,Pairs,Measure,Write,Validate,Import,Rebuild }
        private const int Samples=7;
        [OneTimeSetUp] public void Initialize()
        {if(!FixedRuntime.IsInitialized)FixedRuntime.Initialize();if(!FixedBoundsRuntime.IsInitialized)FixedBoundsRuntime.Initialize();}
        internal struct DenseSource<TBounds> : IDeterministicSpatialSource<int,int,TBounds> where TBounds:unmanaged
        {
            internal SpatialRecord<int,int,TBounds>[] Rows;internal int Length;internal ulong Token,Version;
            public ulong Identity=>Token;public ulong Revision=>Version;public int Count=>Length;
            public DeterministicStatus TryGetAt(int ordinal,out SpatialRecord<int,int,TBounds> record)
            {record=default;if((uint)ordinal>=(uint)Length)return DeterministicStatus.OutOfRange;record=Rows[ordinal];return DeterministicStatus.None;}
        }
        internal struct ManagedOrdinalSource<TBounds,TAccess> : IDeterministicSpatialSource<int,int,TBounds>
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds>
        {
            internal DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> Index;
            public ulong Identity=>Index.Identity;public ulong Revision=>Index.Revision;public int Count=>Index.Count;
            public DeterministicStatus TryGetAt(int ordinal,out SpatialRecord<int,int,TBounds> record)=>Index.TryGetAt(ordinal,out record);
        }
#if !FIXEDPOINT_STANDALONE
        internal struct NativeDenseSource<TBounds> : IDeterministicSpatialSource<int,int,TBounds> where TBounds:unmanaged
        {
            [ReadOnly] internal NativeArray<SpatialRecord<int,int,TBounds>> Rows;internal int Length;internal ulong Token,Version;
            public ulong Identity=>Token;public ulong Revision=>Version;public int Count=>Length;
            public DeterministicStatus TryGetAt(int ordinal,out SpatialRecord<int,int,TBounds> record)
            {record=default;if((uint)ordinal>=(uint)Length)return DeterministicStatus.OutOfRange;record=Rows[ordinal];return DeterministicStatus.None;}
        }
#endif
        internal sealed class Owner<TBounds,TAccess,TCodec,TNetworkAccess> : IDisposable
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
            where TNetworkAccess:unmanaged,ISpatialBoundsNetworkAccess<TBounds>
        {
            private readonly bool native;
            private DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> managed,sourceIndex;
            private readonly byte[] queryWork,pairWork,canonicalWork,packetBytes;
            private readonly SpatialHit<int>[] hits;
            private readonly SpatialRecord<int,int,TBounds>[] rows;
            private readonly SpatialMutation<int,int,TBounds>[] mutations;
            private readonly ManagedSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy>[] retained;
            private int retainedCount;
            internal readonly int Capacity,QueryBytes,PairBytes,CanonicalBytes,PacketCapacity;
            internal readonly SpatialIndexOptions Options;
            internal int LastCount,LastBytes,EncodedBytes,CommandCount,SourceCount,PairLimit=-1,CodecMode;
            internal ulong SourceRevision=1,SourceToken=SpatialSourceIdentity.Create();
            internal SpatialQuery Query;
            internal SpatialFilter Filter=SpatialFilter.All;
            internal SpatialBoundsNetworkCodec<TBounds,TNetworkAccess> NetworkCodec;
            internal DeterministicStatus Error;
            internal bool LastOrdinal;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> index,ordinalIndex;
            private NativeArray<byte> nativeQuery,nativePair,nativeCanonical,nativePacket;
            private NativeArray<SpatialHit<int>> nativeHits;
            private NativeArray<SpatialRecord<int,int,TBounds>> nativeRows;
            private NativeArray<SpatialMutation<int,int,TBounds>> nativeMutations;
            private NativeArray<ulong> result;
            private readonly NativeSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy>[] nativeRetained;
#endif
            internal Owner(SpatialIndexOptions options,bool native,int sourceCount=0,bool ordinal=false)
            {
                Options=options;Capacity=options.Records.MaxCapacity;this.native=native;SourceCount=sourceCount;
                SpatialQueryScratch.TryMeasure<int>(Capacity,Capacity*2,out QueryBytes).ThrowIfFailure();
                SpatialPairScratch.TryMeasure<int>(Capacity,Capacity*2,out PairBytes).ThrowIfFailure();
                CanonicalBytes=checked(Capacity*160+4096);PacketCapacity=checked(Capacity*128+4096);
                rows=new SpatialRecord<int,int,TBounds>[Capacity];mutations=new SpatialMutation<int,int,TBounds>[Capacity+1];
                retained=new ManagedSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy>[options.Records.SnapshotSlots];
                for(int i=0;i<Capacity;i++)rows[i]=new SpatialRecord<int,int,TBounds>(i,Box(i,0,false),i);
                Query=Point(false);
#if !FIXEDPOINT_STANDALONE
                if(native)
                {
                    index=new NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(options,Allocator.Persistent);
                    ordinalIndex=new NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(new SpatialIndexOptions(ordinal?sourceCount:0),Allocator.Persistent);
                    nativeQuery=new NativeArray<byte>(QueryBytes,Allocator.Persistent);nativePair=new NativeArray<byte>(PairBytes,Allocator.Persistent);
                    nativeCanonical=new NativeArray<byte>(CanonicalBytes,Allocator.Persistent);nativePacket=new NativeArray<byte>(PacketCapacity,Allocator.Persistent);
                    nativeHits=new NativeArray<SpatialHit<int>>(Capacity,Allocator.Persistent);nativeRows=new NativeArray<SpatialRecord<int,int,TBounds>>(rows,Allocator.Persistent);
                    nativeMutations=new NativeArray<SpatialMutation<int,int,TBounds>>(Capacity+1,Allocator.Persistent);result=new NativeArray<ulong>(4,Allocator.Persistent);
                    nativeRetained=new NativeSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy>[options.Records.SnapshotSlots];
                    if(ordinal)for(int i=0;i<sourceCount;i++)ordinalIndex.TryAdd(i,rows[i].Bounds,i).ThrowIfFailure();
                    return;
                }
#endif
                managed=new DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(options);
                sourceIndex=new DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(new SpatialIndexOptions(ordinal?sourceCount:0));
                if(ordinal)for(int i=0;i<sourceCount;i++)sourceIndex.TryAdd(i,rows[i].Bounds,i).ThrowIfFailure();
                queryWork=new byte[QueryBytes];pairWork=new byte[PairBytes];canonicalWork=new byte[CanonicalBytes];packetBytes=new byte[PacketCapacity];hits=new SpatialHit<int>[Capacity];
            }
            internal static TBounds Box(int key,int shift,bool dense)
            {
                TBounds bounds=default;long x=dense?0:key*16L+shift;
                if(default(TAccess).Dimensions==2)*(FixedBounds2*)&bounds=FixedBounds2.FromMinMax(FixedVector2.FromRaw(x,0),FixedVector2.FromRaw(x+4,4));
                else *(FixedBounds3*)&bounds=FixedBounds3.FromMinMax(FixedVector3.FromRaw(x,0,0),FixedVector3.FromRaw(x+4,4,4));
                return bounds;
            }
            internal static SpatialQuery Point(bool miss)=>default(TAccess).Dimensions==2?SpatialQuery.ForPoint(FixedVector2.FromRaw(miss?-32:0,0)):SpatialQuery.ForPoint(FixedVector3.FromRaw(miss?-32:0,0,0));
            internal static SpatialQuery Near(int k)=>default(TAccess).Dimensions==2?SpatialQuery.ForNearest(FixedVector2.zero,k):SpatialQuery.ForNearest(FixedVector3.zero,k);
            internal static SpatialQuery Ray()=>default(TAccess).Dimensions==2?SpatialQuery.ForRay(new FixedRay2(FixedVector2.FromRaw(-1,0),FixedVector2.FromRaw(1,0))):SpatialQuery.ForRay(new FixedRay3(FixedVector3.FromRaw(-1,0,0),FixedVector3.FromRaw(1,0,0)));
            internal void Populate(int active,int highWater,bool dense=false)
            {
#if !FIXEDPOINT_STANDALONE
                if(native)
                {for(int i=0;i<highWater;i++)index.TryAdd(i,Box(i,0,dense),i).ThrowIfFailure();for(int i=active;i<highWater;i++)index.TryRemove(i).ThrowIfFailure();index.TryRebuild().ThrowIfFailure();return;}
#endif
                for(int i=0;i<highWater;i++)managed.TryAdd(i,Box(i,0,dense),i).ThrowIfFailure();
                for(int i=active;i<highWater;i++)managed.TryRemove(i).ThrowIfFailure();managed.TryRebuild().ThrowIfFailure();
            }
            internal SpatialMemoryStats Stats {
                get {
#if !FIXEDPOINT_STANDALONE
                    if(native)return index.MemoryStats;
#endif
                    return managed.MemoryStats;
                }
            }
            // Payload only: array headers, snapshot handles and the auxiliary source owner are excluded.
            internal long CallerBytes=>(long)QueryBytes+PairBytes+CanonicalBytes+PacketCapacity+Capacity*(sizeof(SpatialHit<int>)+sizeof(SpatialRecord<int,int,TBounds>))+ (Capacity+1L)*sizeof(SpatialMutation<int,int,TBounds>)
                +(native?Capacity*(long)sizeof(SpatialRecord<int,int,TBounds>)+(Capacity+1L)*sizeof(SpatialMutation<int,int,TBounds>)+4*sizeof(ulong):0);
            internal void Commands(int count,int shift,bool failed=false,bool scattered=false,bool dataOnly=false)
            {
                CommandCount=count+(failed?1:0);
                for(int i=0;i<count;i++)
                {
                    int key=scattered?(int)((long)i*(Capacity-1)/Math.Max(1,count-1)):i;
                    mutations[i]=new SpatialMutation<int,int,TBounds>{Kind=dataOnly?SpatialMutationKind.SetData:SpatialMutationKind.UpdateBounds,Key=key,Bounds=Box(key,shift,false),Data=shift};
                }
                if(failed)mutations[count]=new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=int.MaxValue};
#if !FIXEDPOINT_STANDALONE
                if(native)for(int i=0;i<CommandCount;i++)nativeMutations[i]=mutations[i];
#endif
            }
            internal void Retain(bool distinct)
            {
#if !FIXEDPOINT_STANDALONE
                if(native)
                {index.TryCapture(out nativeRetained[retainedCount++]).ThrowIfFailure();if(distinct)index.TrySetData(0,retainedCount).ThrowIfFailure();return;}
#endif
                managed.TryCapture(out retained[retainedCount++]).ThrowIfFailure();if(distinct)managed.TrySetData(0,retainedCount).ThrowIfFailure();
            }
            internal void ResetMutationBaseline(int count,bool scattered)
            {
                if(retainedCount>0)
                {
#if !FIXEDPOINT_STANDALONE
                    if(native){index.TryRestore(nativeRetained[retainedCount-1]).ThrowIfFailure();return;}
#endif
                    managed.TryRestore(retained[retainedCount-1]).ThrowIfFailure();return;
                }
                for(int i=0;i<count;i++)
                {
                    int key=scattered?(int)((long)i*(Capacity-1)/Math.Max(1,count-1)):i;
#if !FIXEDPOINT_STANDALONE
                    if(native){index.TryUpdateBounds(key,Box(key,0,false)).ThrowIfFailure();index.TrySetData(key,key).ThrowIfFailure();continue;}
#endif
                    managed.TryUpdateBounds(key,Box(key,0,false)).ThrowIfFailure();managed.TrySetData(key,key).ThrowIfFailure();
                }
#if !FIXEDPOINT_STANDALONE
                if(native){index.TryRebuild().ThrowIfFailure();return;}
#endif
                managed.TryRebuild().ThrowIfFailure();
            }
            internal void AssertCommands(bool failed)
            {
                for(int i=0;i<CommandCount-(failed?1:0);i++)
                {
                    var command=mutations[i];SpatialRecord<int,int,TBounds> record;
#if !FIXEDPOINT_STANDALONE
                    if(native)index.TryGet(command.Key,out record).ThrowIfFailure();else
#endif
                    managed.TryGet(command.Key,out record).ThrowIfFailure();
                    TBounds expected=failed||command.Kind==SpatialMutationKind.SetData?Box(command.Key,0,false):command.Bounds;
                    Assert.AreEqual(expected,record.Bounds);
                    Assert.AreEqual(!failed&&command.Kind==SpatialMutationKind.SetData?command.Data:command.Key,record.Data);
                }
            }
            internal ulong CanonicalDigest()
            {
                int savedMode=CodecMode,savedBytes=LastBytes;CodecMode=0;Execute(Operation.Write,1);AssertSuccess();ulong hash=1;
#if !FIXEDPOINT_STANDALONE
                if(native){for(int i=0;i<LastBytes;i++)hash=DeterministicHash.Mix(hash^nativePacket[i]);}else
#endif
                for(int i=0;i<LastBytes;i++)hash=DeterministicHash.Mix(hash^packetBytes[i]);
                CodecMode=savedMode;LastBytes=savedBytes;return hash;
            }
            internal void ConfigureCodec(int mode)
            {
                CodecMode=mode;
                if(mode!=0)
                {
                    var axis=new FixedNetworkProfile(FixedRuntime.Settings,FixedKind.Signed64,mode==1?FixedCodec.VarInt:FixedCodec.BitPacked,
                        lowerRaw:mode==2?unchecked((ulong)-2000000L):0,upperRaw:mode==2?2000000UL:0);
                    NetworkCodec=new SpatialBoundsNetworkCodec<TBounds,TNetworkAccess>(new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(axis,default(TAccess).Dimensions)));
                }
                Execute(Operation.Write,1);AssertSuccess();EncodedBytes=LastBytes;
            }
            internal void BumpRevision()=>SourceRevision++;
            internal void ChangePayload()
            {
                SourceRevision++;
                for(int i=0;i<Math.Max(1,SourceCount/100);i++)
                {rows[i].Data++;
#if !FIXEDPOINT_STANDALONE
                    if(native)nativeRows[i]=rows[i];
#endif
                }
            }
            internal void AssertSource()
            {
                for(int i=0;i<SourceCount;i++)
                {
                    SpatialRecord<int,int,TBounds> record;
#if !FIXEDPOINT_STANDALONE
                    if(native)
                    {
                        if(LastOrdinal)index.AsReadOnly(ordinalIndex).TryGet(i,out record).ThrowIfFailure();
                        else index.AsReadOnly(new NativeDenseSource<TBounds>{Rows=nativeRows,Length=SourceCount,Token=SourceToken,Version=SourceRevision}).TryGet(i,out record).ThrowIfFailure();
                    }
                    else
#endif
                    {
                        if(LastOrdinal)managed.AsReadOnly(new ManagedOrdinalSource<TBounds,TAccess>{Index=sourceIndex}).TryGet(i,out record).ThrowIfFailure();
                        else managed.AsReadOnly(new DenseSource<TBounds>{Rows=rows,Length=SourceCount,Token=SourceToken,Version=SourceRevision}).TryGet(i,out record).ThrowIfFailure();
                    }
                    Assert.AreEqual(rows[i].Data,record.Data);Assert.AreEqual(rows[i].Bounds,record.Bounds);
                }
            }
            internal void Execute(Operation operation,int iterations)
            {
                LastOrdinal=operation==Operation.SyncOrdinal;
#if !FIXEDPOINT_STANDALONE
                if(native)
                {
                    new Work{Index=index,SourceIndex=ordinalIndex,Rows=nativeRows,Mutations=nativeMutations,QueryWork=nativeQuery,PairWork=nativePair,CanonicalWork=nativeCanonical,Packet=nativePacket,
                        Hits=nativeHits,Result=result,Op=operation,Iterations=iterations,Query=Query,Filter=Filter,CommandCount=CommandCount,SourceCount=SourceCount,SourceToken=SourceToken,SourceRevision=SourceRevision,
                        PairLimit=PairLimit,CodecMode=CodecMode,EncodedBytes=EncodedBytes,NetworkCodec=NetworkCodec}.Schedule().Complete();
                    Error=(DeterministicStatus)result[1];LastCount=(int)result[2];LastBytes=(int)result[3];return;
                }
#endif
                fixed(byte* q=queryWork)fixed(byte* p=pairWork)fixed(byte* c=canonicalWork)fixed(byte* packet=packetBytes)
                fixed(SpatialHit<int>* h=hits)fixed(SpatialMutation<int,int,TBounds>* commands=mutations)
                {
                var status=DeterministicStatus.None; LastCount=0; LastBytes=0;
                for(int repeat=0;repeat<iterations && status.IsSuccess();repeat++)
                {
                    switch(operation)
                    {
                        case Operation.Dispatch: break;
                        case Operation.Query: status=managed.TryQuery(Query,new SpatialQueryScratch(q,QueryBytes),h,Capacity,out LastCount,Filter,default(SpatialAcceptAll<int,int,TBounds>)); break;
                        case Operation.Direct:
                            for(int i=0;i<CommandCount&&status.IsSuccess();i++) status=managed.TryApply(commands[i]);break;
                        case Operation.Batch: status=managed.TryApplyBatch(new ReadOnlySpan<SpatialMutation<int,int,TBounds>>(commands,CommandCount));break;
                        case Operation.PerEntityBatch:
                            for(int i=0;i<CommandCount&&status.IsSuccess();i++) status=managed.TryApplyBatch(new ReadOnlySpan<SpatialMutation<int,int,TBounds>>(commands+i,1));break;
                        case Operation.FailedBatch:
                            status=managed.TryApplyBatch(new ReadOnlySpan<SpatialMutation<int,int,TBounds>>(commands,CommandCount));
                            if(status==DeterministicStatus.NotFound) status=DeterministicStatus.None;else status=DeterministicStatus.ProtocolConflict;break;
                        case Operation.SyncDense: case Operation.SyncForced:
                            status=managed.TrySynchronize(new DenseSource<TBounds>{Rows=rows,Length=SourceCount,Token=SourceToken,Version=SourceRevision},operation==Operation.SyncForced);LastCount=SourceCount;break;
                        case Operation.SyncOrdinal:
                            status=managed.TrySynchronize(new ManagedOrdinalSource<TBounds,TAccess>{Index=sourceIndex},true);LastCount=SourceCount;break;
                        case Operation.Pairs:
                            status=managed.TryGetPairs(new SpatialPairScratch(p,PairBytes),out var pairs);int consumed=0;
                            while(status.IsSuccess()&&(PairLimit<0||consumed<PairLimit))
                            {status=pairs.TryNext(out var pair,out bool has);if(!has)break;consumed++;}
                            LastCount=consumed;break;
                        case Operation.Measure: case Operation.Write:
                            var writer=operation==Operation.Measure?DeterministicWriter.CreateMeasure():new DeterministicWriter(packet,PacketCapacity);
                            if(CodecMode==0) status=managed.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),default(TCodec));
                            else status=managed.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),NetworkCodec);
                            LastBytes=writer.Position;break;
                        case Operation.Validate: case Operation.Import:
                            var reader=new DeterministicReader(packet,EncodedBytes);var arena=new CanonicalScratch(c,CanonicalBytes);
                            if(CodecMode==0) status=operation==Operation.Validate?managed.TryValidateCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),default(TCodec)):managed.TryReadCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),default(TCodec));
                            else status=operation==Operation.Validate?managed.TryValidateCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),NetworkCodec):managed.TryReadCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),NetworkCodec);
                            LastBytes=reader.Position;break;
                        case Operation.Rebuild: status=managed.TryRebuild();break;
                    }
                }
                Error=status;
                }
            }
            internal void AssertSuccess()
            {
                Error.ThrowIfFailure();
#if !FIXEDPOINT_STANDALONE
                if(native)Assert.AreEqual(0,result[0],"BurstDiscard marker");
#endif
            }
            internal ulong Digest()
            {
                int n;SpatialQuery query=Near(16);ulong hash=1;
#if !FIXEDPOINT_STANDALONE
                if(native)
                {
                    if(Options.Ownership==SpatialStateOwnership.Derived)
                    {
                        SpatialRecord<int,int,TBounds> zero;
                        if(LastOrdinal)index.AsReadOnly(ordinalIndex).TryGet(0,out zero).ThrowIfFailure();
                        else index.AsReadOnly(new NativeDenseSource<TBounds>{Rows=nativeRows,Length=SourceCount,Token=SourceToken,Version=SourceRevision}).TryGet(0,out zero).ThrowIfFailure();
                        return (uint)zero.Data;
                    }
                    index.TryQuery(query,new SpatialQueryScratch((byte*)nativeQuery.GetUnsafePtr(),nativeQuery.Length),(SpatialHit<int>*)nativeHits.GetUnsafePtr(),nativeHits.Length,out n).ThrowIfFailure();
                    for(int i=0;i<n;i++)hash=DeterministicHash.Mix(hash^(uint)nativeHits[i].Key^unchecked((ulong)nativeHits[i].Metric.RawValue)^(uint)nativeHits[i].MetricStatus);return hash;
                }
#endif
                if(Options.Ownership==SpatialStateOwnership.Derived)
                {
                    SpatialRecord<int,int,TBounds> zero;
                    if(LastOrdinal)managed.AsReadOnly(new ManagedOrdinalSource<TBounds,TAccess>{Index=sourceIndex}).TryGet(0,out zero).ThrowIfFailure();
                    else managed.AsReadOnly(new DenseSource<TBounds>{Rows=rows,Length=SourceCount,Token=SourceToken,Version=SourceRevision}).TryGet(0,out zero).ThrowIfFailure();
                    return (uint)zero.Data;
                }
                fixed(byte* q=queryWork)fixed(SpatialHit<int>* h=hits)
                {managed.TryQuery(query,new SpatialQueryScratch(q,QueryBytes),h,hits.Length,out n).ThrowIfFailure();for(int i=0;i<n;i++)hash=DeterministicHash.Mix(hash^(uint)h[i].Key^unchecked((ulong)h[i].Metric.RawValue)^(uint)h[i].MetricStatus);}
                return hash;
            }
#if !FIXEDPOINT_STANDALONE
            [BurstCompile(CompileSynchronously=true)] internal struct Work : IJob
            {
                internal NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> Index;
                [ReadOnly] internal NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> SourceIndex;
                [ReadOnly] internal NativeArray<SpatialRecord<int,int,TBounds>> Rows;
                [ReadOnly] internal NativeArray<SpatialMutation<int,int,TBounds>> Mutations;
                internal NativeArray<byte> QueryWork,PairWork,CanonicalWork,Packet;
                internal NativeArray<SpatialHit<int>> Hits;internal NativeArray<ulong> Result;
                internal Operation Op;internal int Iterations,CommandCount,SourceCount,PairLimit,CodecMode,EncodedBytes;
                internal SpatialQuery Query;internal SpatialFilter Filter;internal ulong SourceToken,SourceRevision;
                internal SpatialBoundsNetworkCodec<TBounds,TNetworkAccess> NetworkCodec;
                public void Execute()
                {
                    byte* q=(byte*)QueryWork.GetUnsafePtr(),p=(byte*)PairWork.GetUnsafePtr(),c=(byte*)CanonicalWork.GetUnsafePtr(),packet=(byte*)Packet.GetUnsafePtr();
                    var h=(SpatialHit<int>*)Hits.GetUnsafePtr();var commands=(SpatialMutation<int,int,TBounds>*)Mutations.GetUnsafeReadOnlyPtr();
                    int Capacity=Hits.Length,QueryBytes=QueryWork.Length,PairBytes=PairWork.Length,PacketCapacity=Packet.Length,CanonicalBytes=CanonicalWork.Length;
                    var operation=Op;
                    
                var status=DeterministicStatus.None; int LastCount=0, LastBytes=0;
                for(int repeat=0;repeat<Iterations && status.IsSuccess();repeat++)
                {
                    switch(operation)
                    {
                        case Operation.Dispatch: break;
                        case Operation.Query: status=Index.TryQuery(Query,new SpatialQueryScratch(q,QueryBytes),h,Capacity,out LastCount,Filter,default(SpatialAcceptAll<int,int,TBounds>)); break;
                        case Operation.Direct:
                            for(int i=0;i<CommandCount&&status.IsSuccess();i++) status=Index.TryApply(commands[i]);break;
                        case Operation.Batch: status=Index.TryApplyBatch(commands,CommandCount);break;
                        case Operation.PerEntityBatch:
                            for(int i=0;i<CommandCount&&status.IsSuccess();i++) status=Index.TryApplyBatch(commands+i,1);break;
                        case Operation.FailedBatch:
                            status=Index.TryApplyBatch(commands,CommandCount);
                            if(status==DeterministicStatus.NotFound) status=DeterministicStatus.None;else status=DeterministicStatus.ProtocolConflict;break;
                        case Operation.SyncDense: case Operation.SyncForced:
                            status=Index.TrySynchronize(new NativeDenseSource<TBounds>{Rows=Rows,Length=SourceCount,Token=SourceToken,Version=SourceRevision},operation==Operation.SyncForced);LastCount=SourceCount;break;
                        case Operation.SyncOrdinal:
                            status=Index.TrySynchronize(SourceIndex,true);LastCount=SourceCount;break;
                        case Operation.Pairs:
                            status=Index.TryGetPairs(new SpatialPairScratch(p,PairBytes),out var pairs);int consumed=0;
                            while(status.IsSuccess()&&(PairLimit<0||consumed<PairLimit))
                            {status=pairs.TryNext(out var pair,out bool has);if(!has)break;consumed++;}
                            LastCount=consumed;break;
                        case Operation.Measure: case Operation.Write:
                            var writer=operation==Operation.Measure?DeterministicWriter.CreateMeasure():new DeterministicWriter(packet,PacketCapacity);
                            if(CodecMode==0) status=Index.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),default(TCodec));
                            else status=Index.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),NetworkCodec);
                            LastBytes=writer.Position;break;
                        case Operation.Validate: case Operation.Import:
                            var reader=new DeterministicReader(packet,EncodedBytes);var arena=new CanonicalScratch(c,CanonicalBytes);
                            if(CodecMode==0) status=operation==Operation.Validate?Index.TryValidateCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),default(TCodec)):Index.TryReadCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),default(TCodec));
                            else status=operation==Operation.Validate?Index.TryValidateCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),NetworkCodec):Index.TryReadCanonical(ref reader,ref arena,default(Int32Policy),default(Int32Policy),NetworkCodec);
                            LastBytes=reader.Position;break;
                        case Operation.Rebuild: status=Index.TryRebuild();break;
                    }
                }
                int marker=0;MarkManaged(ref marker);Result[0]=(ulong)marker;Result[1]=(ulong)status;Result[2]=(ulong)LastCount;Result[3]=(ulong)LastBytes;
                }
                [BurstDiscard]private static void MarkManaged(ref int marker)=>marker=1;
            }
#endif
            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if(native){for(int i=0;i<retainedCount;i++)nativeRetained[i].Dispose();index.Dispose();ordinalIndex.Dispose();nativeQuery.Dispose();nativePair.Dispose();nativeCanonical.Dispose();nativePacket.Dispose();nativeHits.Dispose();nativeRows.Dispose();nativeMutations.Dispose();result.Dispose();return;}
#endif
                for(int i=0;i<retainedCount;i++)retained[i].Dispose();managed.Dispose();sourceIndex.Dispose();
            }
        }
        private static SpatialIndexOptions Options(int capacity,SpatialIndexStrategy strategy,SpatialStateOwnership ownership=SpatialStateOwnership.Autonomous,
            int slots=0,int page=4096,SpatialRollbackPolicy rollback=SpatialRollbackPolicy.Compact,bool grow=false,long margin=8)
            =>new SpatialIndexOptions(new BufferOptions(grow?16:capacity,capacity,grow?AllocationPolicy.Grow:AllocationPolicy.Fixed,snapshotSlots:slots,pageBytes:page),
                strategy,ownership,rollback,cellSize:Fixed64.FromRaw(16),fatMargin:Fixed64.FromRaw(margin),maxCellsPerObject:8,maxCellsPerQuery:32);

        private static void Measure<TBounds,TAccess,TCodec,TNetworkAccess>(StreamWriter report,string family,string label,Owner<TBounds,TAccess,TCodec,TNetworkAccess> owner,
            bool native,int active,int used,Operation operation,int iterations,Action<int> before=null,Action after=null)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
            where TNetworkAccess:unmanaged,ISpatialBoundsNetworkAccess<TBounds>
        {
            var times=new double[Samples];var allocations=new long[Samples];long delta=0;
            for(int sample=-2;sample<Samples;sample++)
            {
                before?.Invoke(sample+2);long live=owner.Stats.UsedBytes;
                long gc=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();owner.Execute(operation,iterations);
                long elapsed=Stopwatch.GetTimestamp()-start,allocated=GC.GetAllocatedBytesForCurrentThread()-gc;
                owner.AssertSuccess();if(sample>=0){times[sample]=elapsed*1000000.0/Stopwatch.Frequency/iterations;allocations[sample]=allocated;delta=Math.Max(delta,owner.Stats.UsedBytes-live);}after?.Invoke();
            }
            Array.Sort(times);Array.Sort(allocations);var stats=owner.Stats;ulong digest=operation==Operation.Dispatch?0:owner.Digest();
            string row=string.Join("\t",native?"Burst scheduled batches":"Managed",FixedBoundsRuntime.Settings.Representation,family,label,default(TAccess).Dimensions,owner.Options.Strategy,
                owner.Options.Rollback,owner.Capacity,active,used,owner.Options.Records.SnapshotSlots,owner.Options.Records.PageBytes,iterations,Samples,
                times[Samples/2].ToString("F3",CultureInfo.InvariantCulture),times[Samples-1].ToString("F3",CultureInfo.InvariantCulture),
                allocations[Samples/2],stats.ReservedBytes,stats.UsedBytes,delta,owner.CallerBytes,owner.LastBytes,owner.LastCount,digest);
            report.WriteLine(row);report.Flush();
        }
        private static void Run<TBounds,TAccess,TCodec,TNetworkAccess>(string family,int capacity,bool native,StreamWriter report)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
            where TNetworkAccess:unmanaged,ISpatialBoundsNetworkAccess<TBounds>
        {
            foreach(SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
            {
                if(family=="query")
                {
                    ulong? expected=null;
                    foreach(int used in new[]{64,capacity})
                    {
                        using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy),native);owner.Populate(64,used);
                        ulong digest=owner.Digest();if(expected.HasValue)Assert.AreEqual(expected.Value,digest);expected=digest;
                        if(native)Measure(report,family,"dispatch",owner,native,64,used,Operation.Dispatch,128);
                        foreach(string kind in new[]{"point_hit","point_miss","filter_none","nearest_k1","nearest_k64","ray_all"})
                        {
                            owner.Filter=kind=="filter_none"?default:SpatialFilter.All;
                            owner.Query=kind=="point_miss"?Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Point(true):kind=="nearest_k1"?Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Near(1):
                                kind=="nearest_k64"?Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Near(64):kind=="ray_all"?Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Ray():Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Point(false);
                            Measure(report,family,kind,owner,native,64,used,Operation.Query,128);
                            Assert.AreEqual(kind=="point_miss"||kind=="filter_none"?0:kind=="nearest_k64"||kind=="ray_all"?64:1,owner.LastCount);
                        }
                    }
                    using var dense=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy),native);dense.Populate(capacity,capacity,true);
                    foreach(int k in new[]{1,64,capacity}){dense.Query=Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Near(k);Measure(report,family,"dense_nearest_k"+k,dense,native,capacity,capacity,Operation.Query,8);Assert.AreEqual(k,dense.LastCount);}
                    dense.Query=Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Ray();Measure(report,family,"dense_ray_all",dense,native,capacity,capacity,Operation.Query,8);Assert.AreEqual(capacity,dense.LastCount);
                }
                else if(family=="sync")
                {
                    int n=Math.Min(capacity,2048);
                    using var dense=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy,SpatialStateOwnership.Derived),native,n);
                    dense.Execute(Operation.SyncForced,1);dense.AssertSuccess();
                    Measure(report,family,"dense_same_revision_fast",dense,native,n,n,Operation.SyncDense,32,after:dense.AssertSource);
                    Measure(report,family,"dense_forced_unchanged",dense,native,n,n,Operation.SyncForced,2,after:dense.AssertSource);
                    Measure(report,family,"dense_revision_only",dense,native,n,n,Operation.SyncDense,1,_=>dense.BumpRevision(),dense.AssertSource);
                    Measure(report,family,"dense_payload_1_percent",dense,native,n,n,Operation.SyncDense,1,_=>dense.ChangePayload(),dense.AssertSource);
                    using var ordinal=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy,SpatialStateOwnership.Derived),native,n,true);
                    ordinal.Execute(Operation.SyncOrdinal,1);ordinal.AssertSuccess();Measure(report,family,"index_ordinal_forced_unchanged",ordinal,native,n,n,Operation.SyncOrdinal,2,after:ordinal.AssertSource);
                }
                else if(family=="mutation")
                {
                    foreach(int page in new[]{256,4096})foreach(int roots in new[]{0,4})
                    {
                        foreach(string mode in new[]{"contained_direct","escaping_direct","escaping_batch","escaping_entity_batch","failed_late_batch","cow_data_clustered","cow_data_scattered"})
                        {
                            using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy,slots:roots,page:page,rollback:SpatialRollbackPolicy.FastRestore),native);
                            owner.Populate(capacity,capacity);for(int i=0;i<roots;i++)owner.Retain(false);
                            int count=32;Operation op=mode=="escaping_batch"?Operation.Batch:mode=="escaping_entity_batch"?Operation.PerEntityBatch:mode=="failed_late_batch"?Operation.FailedBatch:Operation.Direct;
                            bool failed=mode=="failed_late_batch",scattered=mode=="cow_data_scattered";ulong baseline=failed?owner.CanonicalDigest():0;
                            Measure(report,family,mode,owner,native,capacity,capacity,op,1,s=>
                            {owner.ResetMutationBaseline(count,scattered);owner.Commands(count,mode=="contained_direct"?1:128,failed:failed,scattered:scattered,dataOnly:mode.StartsWith("cow_data"));},
                            ()=>{owner.AssertCommands(failed);if(failed)Assert.AreEqual(baseline,owner.CanonicalDigest(),"Failed batch changed canonical state");});
                        }
                    }
                    if(strategy==SpatialIndexStrategy.StaticBvh)
                    {
                        using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy),native);owner.Populate(capacity,capacity);
                        owner.Commands(1,8);owner.Execute(Operation.Direct,1);owner.AssertSuccess();owner.Query=Owner<TBounds,TAccess,TCodec,TNetworkAccess>.Near(8);
                        Measure(report,family,"static_dirty_queries",owner,native,capacity,capacity,Operation.Query,64);
                        ulong expected=owner.Digest();
                        Measure(report,family,"static_rebuild",owner,native,capacity,capacity,Operation.Rebuild,1,_=>
                        {owner.Commands(1,0);owner.Execute(Operation.Direct,1);owner.AssertSuccess();owner.Commands(1,8);owner.Execute(Operation.Direct,1);owner.AssertSuccess();});
                        Assert.AreEqual(expected,owner.Digest());
                        Measure(report,family,"static_rebuilt_queries",owner,native,capacity,capacity,Operation.Query,64);
                        Assert.AreEqual(expected,owner.Digest());
                    }
                }
                else if(family=="pairs")
                {
                    foreach(bool dense in new[]{false,true})foreach(int used in new[]{128,capacity})
                    {
                        using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy),native);owner.Populate(128,used,dense);
                        foreach(int limit in new[]{1,32,-1})
                        {owner.PairLimit=limit;Measure(report,family,(dense?"dense":"disjoint")+"_prefix"+limit,owner,native,128,used,Operation.Pairs,2);Assert.AreEqual(dense?(limit<0?8128:limit):0,owner.LastCount);}
                    }
                }
                else if(family=="canonical")
                {
                    using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy),native);owner.Populate(capacity,capacity);ulong expected=owner.Digest();
                    foreach(int codec in new[]{0,1,2})
                    {
                        owner.ConfigureCodec(codec);
                        foreach(Operation operation in new[]{Operation.Measure,Operation.Write,Operation.Validate,Operation.Import})
                        {Measure(report,family,(codec==0?"raw":codec==1?"varint":"bitpacked")+"_"+operation,owner,native,capacity,capacity,operation,2);Assert.AreEqual(owner.EncodedBytes,owner.LastBytes);Assert.AreEqual(expected,owner.Digest());}
                    }
                }
                else if(family=="memory")
                {
                    foreach(var rollback in new[]{SpatialRollbackPolicy.Compact,SpatialRollbackPolicy.FastRestore})foreach(int roots in new[]{0,4})
                    {
                        long start=Stopwatch.GetTimestamp(),gc=GC.GetAllocatedBytesForCurrentThread();
                        using var owner=new Owner<TBounds,TAccess,TCodec,TNetworkAccess>(Options(capacity,strategy,slots:roots,rollback:rollback,grow:true),native);
                        double constructionUs=(Stopwatch.GetTimestamp()-start)*1000000.0/Stopwatch.Frequency;long constructorGc=GC.GetAllocatedBytesForCurrentThread()-gc;
                        owner.Populate(16,16);for(int i=0;i<roots;i++)owner.Retain(true);
                        SpatialMemory.TryMeasure<int,int,TBounds>(owner.Options,out var worst).ThrowIfFailure();Assert.LessOrEqual(owner.Stats.ReservedBytes,worst.ReservedBytes);
                        Measure(report,family,"grow_initial16",owner,native,16,16,Operation.Query,64);
                        report.WriteLine(string.Join("\t",native?"Burst scheduled batches":"Managed",FixedBoundsRuntime.Settings.Representation,family,"constructor_including_caller_buffers",default(TAccess).Dimensions,strategy,rollback,capacity,16,16,roots,4096,1,1,
                            constructionUs.ToString("F3",CultureInfo.InvariantCulture),constructionUs.ToString("F3",CultureInfo.InvariantCulture),constructorGc,owner.Stats.ReservedBytes,owner.Stats.UsedBytes,0,owner.CallerBytes,0,0,0));report.Flush();
                    }
                }
            }
        }
        [TestCase("query",1024,false)] [TestCase("query",8192,false)]
        [TestCase("sync",1024,false)] [TestCase("sync",8192,false)]
        [TestCase("mutation",1024,false)] [TestCase("mutation",8192,false)]
        [TestCase("pairs",1024,false)] [TestCase("pairs",8192,false)]
        [TestCase("canonical",1024,false)] [TestCase("canonical",8192,false)]
        [TestCase("memory",1024,false)] [TestCase("memory",8192,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase("query",1024,true)] [TestCase("query",8192,true)]
        [TestCase("sync",1024,true)] [TestCase("sync",8192,true)]
        [TestCase("mutation",1024,true)] [TestCase("mutation",8192,true)]
        [TestCase("pairs",1024,true)] [TestCase("pairs",8192,true)]
        [TestCase("canonical",1024,true)] [TestCase("canonical",8192,true)]
        [TestCase("memory",1024,true)] [TestCase("memory",8192,true)]
#endif
        [Timeout(1800000)]
        public void MeasurePreviouslyUnisolatedCosts(string family,int capacity,bool native)
        {
            string folder=Environment.GetEnvironmentVariable("LEGENDARY_SPATIAL_HOTPATH_REPORT_DIR")??TestContext.CurrentContext.WorkDirectory??Environment.CurrentDirectory;
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,$"spatial-hotpaths-{FixedBoundsRuntime.Settings.Representation}-{(native?"burst":"managed")}-{family}-{capacity}.tsv");
            using var report=new StreamWriter(path);
            report.WriteLine("backend\trepresentation\tfamily\tcase\tdimensions\tstrategy\trollback\tcapacity\tactive\tused\tsnapshot_slots\tpage_bytes\titerations\tsamples\tmedian_us_per_op\tp95_us_per_op\thost_gc_bytes_median_batch\treserved_payload_bytes\tlive_payload_bytes\tmax_live_growth_bytes\tcaller_buffer_bytes\tcanonical_bytes\tresult_count\tdigest");
            Run<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy,SpatialBounds2NetworkAccess>(family,capacity,native,report);
            Run<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy,SpatialBounds3NetworkAccess>(family,capacity,native,report);
            TestContext.Progress.WriteLine("Spatial hotpath benchmark report: "+path);
        }
    }
}

