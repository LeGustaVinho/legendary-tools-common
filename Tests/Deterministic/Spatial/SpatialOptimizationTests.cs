using System;
using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialOptimizationTests
    {
        [OneTimeSetUp] public void Initialize()
        { if(!FixedRuntime.IsInitialized)FixedRuntime.Initialize();if(!FixedBoundsRuntime.IsInitialized)FixedBoundsRuntime.Initialize(); }

        private struct CountingPredicate<TBounds> : ISpatialPredicate<int,int,TBounds> where TBounds:unmanaged
        {
            internal int* Calls;
            public bool Accept(in SpatialRecord<int,int,TBounds> record){Calls[record.Key]++;return true;}
        }
        private struct PointerSource<TBounds> : IDeterministicSpatialSource<int,int,TBounds> where TBounds:unmanaged
        {
            internal SpatialRecord<int,int,TBounds>* Rows;
            internal ulong Version;
            public ulong Identity=>0x51234;public ulong Revision=>Version;public int Count=>16;
            public DeterministicStatus TryGetAt(int ordinal,out SpatialRecord<int,int,TBounds> record)
            {record=default;if((uint)ordinal>=16)return DeterministicStatus.OutOfRange;record=Rows[ordinal];return DeterministicStatus.None;}
        }

        private sealed class Probe<TBounds,TAccess,TCodec> : IDisposable
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            internal readonly DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> Managed;
            internal readonly SpatialIndexOptions Options;
            private readonly bool native;
#if !FIXEDPOINT_STANDALONE
            internal NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy> Native;
            internal NativeSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy> NativeSnapshot;
#endif
            private ManagedSpatialSnapshot<int,int,TBounds,TAccess,Int32Policy> snapshot;
            internal Probe(SpatialIndexOptions options,bool native)
            {
                Options=options;this.native=native;
#if !FIXEDPOINT_STANDALONE
                if(native){Native=new NativeDeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(options,Allocator.Persistent);return;}
#endif
                Managed=new DeterministicSpatialIndex<int,int,TBounds,TAccess,Int32Policy>(options);
            }
            internal static TBounds Box(long min,long max)
            {
                TBounds result=default;
                if(default(TAccess).Dimensions==2)*(FixedBounds2*)&result=FixedBounds2.FromMinMax(FixedVector2.FromRaw(min,min),FixedVector2.FromRaw(max,max));
                else *(FixedBounds3*)&result=FixedBounds3.FromMinMax(FixedVector3.FromRaw(min,min,min),FixedVector3.FromRaw(max,max,max));
                return result;
            }
            internal int OverflowCount {
                get {
#if !FIXEDPOINT_STANDALONE
                    if(native)return Native.Storage.State.OverflowCount;
#endif
                    return Managed.Storage.State.OverflowCount;
                }
            }
            internal long UsedBytes {
                get {
#if !FIXEDPOINT_STANDALONE
                    if(native)return Native.MemoryStats.UsedBytes;
#endif
                    return Managed.MemoryStats.UsedBytes;
                }
            }
            internal void Add(int key,long min,long max)=>Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Add,Key=key,Bounds=Box(min,max),Data=key}).ThrowIfFailure();
            internal DeterministicStatus Apply(SpatialMutation<int,int,TBounds> mutation)
            {
#if !FIXEDPOINT_STANDALONE
                if(native)return Native.TryApply(mutation);
#endif
                return Managed.TryApply(mutation);
            }
            internal DeterministicStatus Batch(params SpatialMutation<int,int,TBounds>[] mutations)
            {
#if !FIXEDPOINT_STANDALONE
                if(native){fixed(SpatialMutation<int,int,TBounds>* pointer=mutations)return Native.TryApplyBatch(pointer,mutations.Length);}
#endif
                return Managed.TryApplyBatch(mutations);
            }
            internal void Capture()
            {
#if !FIXEDPOINT_STANDALONE
                if(native){Native.TryCapture(out NativeSnapshot).ThrowIfFailure();return;}
#endif
                Managed.TryCapture(out snapshot).ThrowIfFailure();
            }
            internal void Restore()
            {
#if !FIXEDPOINT_STANDALONE
                if(native){Native.TryRestore(NativeSnapshot).ThrowIfFailure();return;}
#endif
                Managed.TryRestore(snapshot).ThrowIfFailure();
            }
            internal void Rebuild()
            {
#if !FIXEDPOINT_STANDALONE
                if(native){Native.TryRebuild().ThrowIfFailure();return;}
#endif
                Managed.TryRebuild().ThrowIfFailure();
            }
            internal int At(int ordinal)
            {
                SpatialRecord<int,int,TBounds> record;
#if !FIXEDPOINT_STANDALONE
                if(native)Native.TryGetAt(ordinal,out record).ThrowIfFailure();else
#endif
                Managed.TryGetAt(ordinal,out record).ThrowIfFailure();
                return record.Key;
            }
            internal DeterministicStatus Synchronize(PointerSource<TBounds> source)
            {
#if !FIXEDPOINT_STANDALONE
                if(native)return Native.TrySynchronize(source,true);
#endif
                return Managed.TrySynchronize(source,true);
            }
            internal void VerifySource(PointerSource<TBounds> source)
            {
#if !FIXEDPOINT_STANDALONE
                if(native)Assert.AreEqual(16,Native.Count);else
#endif
                Assert.AreEqual(16,Managed.Count);
                for(int i=0;i<16;i++)
                {
                    SpatialRecord<int,int,TBounds> actual;
#if !FIXEDPOINT_STANDALONE
                    if(native)Native.AsReadOnly(source).TryGet(source.Rows[i].Key,out actual).ThrowIfFailure();else
#endif
                    Managed.AsReadOnly(source).TryGet(source.Rows[i].Key,out actual).ThrowIfFailure();
                    Assert.AreEqual(source.Rows[i].Data,actual.Data);Assert.AreEqual(source.Rows[i].Bounds,actual.Bounds);
                }
            }
            internal void DirtyBuildWorkspace()
            {
                for(int i=0;i<Options.Records.MaxCapacity;i++)
                {
#if !FIXEDPOINT_STANDALONE
                    if(native){Native.Storage.WriteOrder(i,2);continue;}
#endif
                    Managed.Storage.WriteOrder(i,2);
                }
            }
            internal void Query(params int[] expected)
            {
                SpatialQueryScratch.TryMeasure<int>(Options.Records.MaxCapacity,Options.Records.MaxCapacity*2,out int bytes).ThrowIfFailure();
                byte* work=stackalloc byte[bytes];SpatialHit<int>* hits=stackalloc SpatialHit<int>[Options.Records.MaxCapacity];
                for(int i=0;i<bytes;i++)work[i]=0xcd;
                var query=SpatialQuery.FromEnvelope(new SpatialRawBounds{MinX=8,MinY=8,MinZ=8,MaxX=16,MaxY=16,MaxZ=16},default(TAccess).Dimensions);
                int* calls=stackalloc int[32];for(int i=0;i<32;i++)calls[i]=0;int written;
#if !FIXEDPOINT_STANDALONE
                if(native)Native.TryQuery(query,new SpatialQueryScratch(work,bytes),hits,Options.Records.MaxCapacity,out written,SpatialFilter.All,new CountingPredicate<TBounds>{Calls=calls}).ThrowIfFailure();else
#endif
                Managed.TryQuery(query,new SpatialQueryScratch(work,bytes),hits,Options.Records.MaxCapacity,out written,SpatialFilter.All,new CountingPredicate<TBounds>{Calls=calls}).ThrowIfFailure();
                Assert.AreEqual(expected.Length,written);
                for(int i=0;i<written;i++){Assert.AreEqual(expected[i],hits[i].Key);Assert.AreEqual(1,calls[hits[i].Key]);}
                for(int i=0;i<32;i++)Assert.LessOrEqual(calls[i],1,"Each record should be visited at most once, including multi-cell objects");
            }
            internal void CanonicalRoundTrip()
            {
                byte* packet=stackalloc byte[16384];byte* arena=stackalloc byte[32768];
                var writer=new DeterministicWriter(packet,16384,7);
#if !FIXEDPOINT_STANDALONE
                if(native)Native.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),default(TCodec)).ThrowIfFailure();else
#endif
                Managed.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),default(TCodec)).ThrowIfFailure();
                Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Clear}).ThrowIfFailure();Assert.AreEqual(0,OverflowCount);
                var reader=new DeterministicReader(packet,writer.Position,7);var scratch=new CanonicalScratch(arena,32768){Position=13};
#if !FIXEDPOINT_STANDALONE
                if(native)Native.TryReadCanonical(ref reader,ref scratch,default(Int32Policy),default(Int32Policy),default(TCodec)).ThrowIfFailure();else
#endif
                Managed.TryReadCanonical(ref reader,ref scratch,default(Int32Policy),default(Int32Policy),default(TCodec)).ThrowIfFailure();
                Assert.AreEqual(writer.Position,reader.Position);
            }
            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if(native){if(NativeSnapshot.IsCreated)NativeSnapshot.Dispose();Native.Dispose();return;}
#endif
                snapshot.Dispose();Managed.Dispose();
            }
        }

        [TestCase(2,false)] [TestCase(3,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(2,true)] [TestCase(3,true)]
#endif
        public void GridMultiCellDedupAndOverflowSurviveRestoreImportAndFailedBatch(int dimensions,bool native)
        {
            foreach(var rollback in new[]{SpatialRollbackPolicy.Compact,SpatialRollbackPolicy.FastRestore})
            {
                if(dimensions==2)Grid<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(native,rollback);
                else Grid<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(native,rollback);
            }
        }
        private static void Grid<TBounds,TAccess,TCodec>(bool native,SpatialRollbackPolicy rollback)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            var options=new SpatialIndexOptions(new BufferOptions(32,snapshotSlots:2,pageBytes:128),SpatialIndexStrategy.SparseGrid,
                rollback:rollback,cellSize:Fixed64.FromRaw(16),maxCellsPerObject:8,maxCellsPerQuery:128);
            using var index=new Probe<TBounds,TAccess,TCodec>(options,native);
            index.Add(2,8,12);index.Add(1,0,20);index.Add(3,-128,128);index.Rebuild();
            Assert.AreEqual(1,index.OverflowCount);index.Query(1,2,3);index.Capture();
            index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.UpdateBounds,Key=3,Bounds=Probe<TBounds,TAccess,TCodec>.Box(0,4)}).ThrowIfFailure();
            Assert.AreEqual(0,index.OverflowCount);index.Query(1,2);
            index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=3}).ThrowIfFailure();index.Add(4,-128,128);
            Assert.AreEqual(1,index.OverflowCount);index.Query(1,2,4);index.Restore();Assert.AreEqual(1,index.OverflowCount);index.Query(1,2,3);
            Assert.AreEqual(DeterministicStatus.NotFound,index.Batch(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=3},new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=99}));
            Assert.AreEqual(1,index.OverflowCount);index.Query(1,2,3);index.CanonicalRoundTrip();Assert.AreEqual(1,index.OverflowCount);index.Query(1,2,3);
            index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Clear}).ThrowIfFailure();Assert.AreEqual(0,index.OverflowCount);index.Query();
        }

        [TestCase(2,false)] [TestCase(3,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(2,true)] [TestCase(3,true)]
#endif
        public void NonGeometricStaticBatchClonesOnlyChangedRecordPages(int dimensions,bool native)
        {
            if(dimensions==2)DataBatch<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(native);
            else DataBatch<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(native);
        }
        private static void DataBatch<TBounds,TAccess,TCodec>(bool native)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            using var index=new Probe<TBounds,TAccess,TCodec>(new SpatialIndexOptions(new BufferOptions(32,snapshotSlots:1,pageBytes:128),SpatialIndexStrategy.StaticBvh,rollback:SpatialRollbackPolicy.FastRestore),native);
            for(int i=0;i<16;i++)index.Add(i,i*32,i*32+4);index.Rebuild();index.Capture();long before=index.UsedBytes;
            index.Batch(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.SetData,Key=0,Data=777},new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.SetMetadata,Key=1,Metadata=new SpatialMetadata(3,7)}).ThrowIfFailure();
            int entryBytes=sizeof(OrderedHashEntry<int,SpatialRecord<int,int,TBounds>>);
            Assert.AreEqual(2L*Math.Max(1,128/entryBytes)*entryBytes,index.UsedBytes-before,"Batch must preserve shared acceleration pages");
            index.Restore();Assert.AreEqual(before,index.UsedBytes);
        }

        private struct InvalidSizeCodec : IDeterministicCodec<int>
        {
            internal bool Reject;
            public uint SchemaId=>0xdead31;public ushort Version=>1;
            public int Measure(in int value)=>3;
            public DeterministicStatus TryWrite(ref DeterministicWriter writer,in int value)=>Reject?DeterministicStatus.InvalidEncoding:default(Int32Policy).TryWrite(ref writer,value);
            public DeterministicStatus TryRead(ref DeterministicReader reader,out int value)=>default(Int32Policy).TryRead(ref reader,out value);
        }
        [TestCase(2,false)] [TestCase(3,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(2,true)] [TestCase(3,true)]
#endif
        public void OrdinalReadsFromEitherEndPreserveInsertionOrderAcrossReusedSlotsAndRestore(int dimensions,bool native)
        {
            if(dimensions==2)Ordinals<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(native);
            else Ordinals<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(native);
        }
        private static void Ordinals<TBounds,TAccess,TCodec>(bool native)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            using var index=new Probe<TBounds,TAccess,TCodec>(new SpatialIndexOptions(new BufferOptions(32,snapshotSlots:1),SpatialIndexStrategy.SparseGrid,rollback:SpatialRollbackPolicy.FastRestore,cellSize:Fixed64.FromRaw(16)),native);
            var expected=new List<int>();
            for(int i=0;i<13;i++){int key=i*7;index.Add(key,0,4);expected.Add(key);}
            foreach(int key in new[]{0,28,63,84}){index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=key}).ThrowIfFailure();expected.Remove(key);}
            for(int i=0;i<expected.Count;i++)Assert.AreEqual(expected[i],index.At(i));index.Capture();
            for(int i=0;i<4;i++){index.Add(100+i,0,4);expected.Add(100+i);}
            for(int i=expected.Count-1;i>=0;i--)Assert.AreEqual(expected[i],index.At(i));
            index.Restore();expected.RemoveRange(expected.Count-4,4);
            for(int i=0;i<expected.Count;i++)Assert.AreEqual(expected[i],index.At(i));
        }
        [TestCase(2,false)] [TestCase(3,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(2,true)] [TestCase(3,true)]
#endif
        public void SynchronizationReusesMarksWithFullMembershipReplacementAndDuplicateRollback(int dimensions,bool native)
        {
            foreach(SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
            {
                if(dimensions==2)Synchronization<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(strategy,native);
                else Synchronization<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(strategy,native);
            }
        }
        private static void Synchronization<TBounds,TAccess,TCodec>(SpatialIndexStrategy strategy,bool native)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            using var index=new Probe<TBounds,TAccess,TCodec>(new SpatialIndexOptions(16,strategy,SpatialStateOwnership.Derived,cellSize:Fixed64.FromRaw(16)),native);
            SpatialRecord<int,int,TBounds>* rows=stackalloc SpatialRecord<int,int,TBounds>[16];SpatialRecord<int,int,TBounds>* saved=stackalloc SpatialRecord<int,int,TBounds>[16];
            for(int i=0;i<16;i++)rows[i]=new SpatialRecord<int,int,TBounds>(i,Probe<TBounds,TAccess,TCodec>.Box(0,4),i);
            var source=new PointerSource<TBounds>{Rows=rows,Version=1};index.DirtyBuildWorkspace();index.Synchronize(source).ThrowIfFailure();index.VerifySource(source);
            for(int i=8;i<16;i++)rows[i]=new SpatialRecord<int,int,TBounds>(100+i,Probe<TBounds,TAccess,TCodec>.Box(0,4),100+i);
            source.Version++;index.Synchronize(source).ThrowIfFailure();index.VerifySource(source);for(int i=0;i<16;i++)saved[i]=rows[i];
            rows[0]=new SpatialRecord<int,int,TBounds>(200,Probe<TBounds,TAccess,TCodec>.Box(0,4),200);rows[1]=rows[0];source.Version++;
            Assert.AreEqual(DeterministicStatus.DuplicateKey,index.Synchronize(source));
            for(int i=0;i<16;i++)rows[i]=saved[i];source.Version--;index.VerifySource(source);
            for(int i=0;i<16;i++)rows[i]=new SpatialRecord<int,int,TBounds>(200+i,Probe<TBounds,TAccess,TCodec>.Box(0,4),200+i);
            source.Version++;index.Synchronize(source).ThrowIfFailure();index.VerifySource(source);
        }
        [TestCase(false)] [TestCase(true)]
        public void MeasuringAndWritingRejectBadCodecWithoutPublishingCursorOrDestination(bool reject)
        {
            using var index=new DeterministicSpatialIndex2<int,int,Int32Policy>(new SpatialIndexOptions(4));
            index.TryAdd(1,FixedBounds2.FromMinMax(FixedVector2.zero,FixedVector2.FromRaw(4,4)),5).ThrowIfFailure();
            byte* packet=stackalloc byte[1024];for(int i=0;i<1024;i++)packet[i]=0xa5;
            var writer=new DeterministicWriter(packet,1024,17);var measure=DeterministicWriter.CreateMeasure();measure.Position=17;
            var expected=reject?DeterministicStatus.InvalidEncoding:DeterministicStatus.ProtocolConflict;
            Assert.AreEqual(expected,index.TryWriteCanonical(ref writer,default(Int32Policy),new InvalidSizeCodec{Reject=reject},default(FixedBounds2Policy)));
            Assert.AreEqual(expected,index.TryWriteCanonical(ref measure,default(Int32Policy),new InvalidSizeCodec{Reject=reject},default(FixedBounds2Policy)));
            Assert.AreEqual(17,writer.Position);Assert.AreEqual(17,measure.Position);for(int i=0;i<1024;i++)Assert.AreEqual(0xa5,packet[i]);
        }
    }
}
