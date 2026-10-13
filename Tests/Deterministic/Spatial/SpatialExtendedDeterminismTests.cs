using System;
using System.Collections.Generic;
using System.Numerics;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialExtendedDeterminismTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }

        // Deliberately creates the worst hash chain, with a valid non-default total order.
        private struct CollisionPolicy : IDeterministicEquality<int>, IDeterministicComparer<int>
        {
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int key) => 0;
            public int Compare(in int a, in int b) => b.CompareTo(a);
        }
        private struct Reject54Codec : IDeterministicCodec<int>
        {
            public uint SchemaId => default(Int32Policy).SchemaId;
            public ushort Version => default(Int32Policy).Version;
            public int Measure(in int value) => 4;
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value) => default(Int32Policy).TryWrite(ref writer, value);
            public DeterministicStatus TryRead(ref DeterministicReader reader, out int value)
            { var status = default(Int32Policy).TryRead(ref reader, out value); return status.IsSuccess() && value == 54 ? DeterministicStatus.InvalidEncoding : status; }
        }
        private struct DifferentParity<TBounds> : ISpatialPairPredicate<int, int, TBounds> where TBounds : unmanaged
        { public bool Accept(in SpatialRecord<int, int, TBounds> first, in SpatialRecord<int, int, TBounds> second) => first.Key > second.Key && ((first.Data ^ second.Data) & 1) != 0; }

        private sealed class Backend<TBounds, TAccess, TCodec> : IDisposable
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            private readonly bool native;
            private readonly DeterministicSpatialIndex<int, int, TBounds, TAccess, CollisionPolicy> managed;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, CollisionPolicy> unmanaged;
#endif
            internal struct Lease : IDisposable
            {
                internal bool Native;
                internal ManagedSpatialSnapshot<int, int, TBounds, TAccess, CollisionPolicy> Managed;
#if !FIXEDPOINT_STANDALONE
                internal NativeSpatialSnapshot<int, int, TBounds, TAccess, CollisionPolicy> Unmanaged;
#endif
                public void Dispose()
                {
#if !FIXEDPOINT_STANDALONE
                    if (Native) { Unmanaged.Dispose(); return; }
#endif
                    Managed.Dispose();
                }
            }
            internal Backend(SpatialIndexOptions options, bool native)
            {
                this.native = native;
#if !FIXEDPOINT_STANDALONE
                if (native) { unmanaged = new NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, CollisionPolicy>(options, Allocator.Persistent); return; }
#endif
                managed = new DeterministicSpatialIndex<int, int, TBounds, TAccess, CollisionPolicy>(options);
            }
            internal ulong Revision
            {
                get {
#if !FIXEDPOINT_STANDALONE
                    if (native) return unmanaged.Revision;
#endif
                    return managed.Revision;
                }
            }
            internal DeterministicStatus Apply(SpatialMutation<int, int, TBounds> command)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryApply(command);
#endif
                return managed.TryApply(command);
            }
            internal DeterministicStatus Batch(SpatialMutation<int, int, TBounds>[] commands)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { fixed (SpatialMutation<int, int, TBounds>* input = commands) return unmanaged.TryApplyBatch(input, commands.Length); }
#endif
                return managed.TryApplyBatch(commands);
            }
            internal DeterministicStatus Rebuild()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryRebuild();
#endif
                return managed.TryRebuild();
            }
            internal DeterministicStatus Capture(out Lease lease)
            {
                lease = new Lease { Native = native };
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryCapture(out lease.Unmanaged);
#endif
                return managed.TryCapture(out lease.Managed);
            }
            internal DeterministicStatus Restore(Lease lease)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryRestore(lease.Unmanaged);
#endif
                return managed.TryRestore(lease.Managed);
            }
            internal DeterministicStatus Query(SpatialQuery query, SpatialQueryScratch scratch, SpatialHit<int>* output, int capacity, out int count)
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryQuery(query, scratch, output, capacity, out count);
#endif
                return managed.TryQuery(query, scratch, output, capacity, out count);
            }
            internal byte[] Encode()
            {
                byte[] packet = new byte[32768]; int length;
                for (int i = 0; i < 7; i++) packet[i] = 0xa5;
                fixed (byte* bytes = packet)
                {
                    var writer = new DeterministicWriter(bytes, packet.Length, 7);
#if !FIXEDPOINT_STANDALONE
                    if (native) unmanaged.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    else
#endif
                    managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    length = writer.Position;
                }
                Array.Resize(ref packet, length); return packet;
            }
            internal DeterministicStatus Read<TDataCodec>(ref DeterministicReader reader, ref CanonicalScratch scratch, TDataCodec codec)
                where TDataCodec : unmanaged, IDeterministicCodec<int>
            {
#if !FIXEDPOINT_STANDALONE
                if (native) return unmanaged.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), codec, default(TCodec));
#endif
                return managed.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), codec, default(TCodec));
            }
            internal List<(int First, int Second)> Pairs(SpatialFilter filter)
            {
                SpatialPairScratch.TryMeasure<int>(48, 96, out int size).ThrowIfFailure();
                var workspace = new byte[size]; var result = new List<(int, int)>();
                fixed (byte* work = workspace)
                {
                    var scratch = new SpatialPairScratch(work, size);
#if !FIXEDPOINT_STANDALONE
                    if (native)
                    {
                        unmanaged.TryGetPairs(scratch, filter, default(DifferentParity<TBounds>), out var pairs).ThrowIfFailure();
                        for (;;) { pairs.TryNext(out var pair, out bool has).ThrowIfFailure(); if (!has) break; result.Add((pair.First, pair.Second)); }
                        return result;
                    }
#endif
                    managed.TryGetPairs(scratch, filter, default(DifferentParity<TBounds>), out var iterator).ThrowIfFailure();
                    for (;;) { iterator.TryNext(out var pair, out bool has).ThrowIfFailure(); if (!has) break; result.Add((pair.First, pair.Second)); }
                }
                return result;
            }
            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { unmanaged.Dispose(); return; }
#endif
                managed.Dispose();
            }
        }
        private static IEnumerable<bool> Backends()
        {
            yield return false;
#if !FIXEDPOINT_STANDALONE
            yield return true;
#endif
        }
        private static SpatialIndexOptions Options(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback, int cells = 2)
            => new SpatialIndexOptions(new BufferOptions(48, snapshotSlots: 2, pageBytes: 128), strategy, rollback: rollback,
                cellSize: Fixed64.FromRaw(8), fatMargin: Fixed64.FromRaw(7), maxCellsPerObject: cells, maxCellsPerQuery: 1);
        private static TBounds Box<TBounds, TAccess>(int key, int tick) where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            uint h = unchecked((uint)(key * 941 + tick * 313) * 1664525u + 1013904223u);
            long x = h % 41 - 20L, y = (h >> 9) % 41 - 20L, z = (h >> 18) % 41 - 20L, width = key % 11;
            TBounds box = default;
            if (default(TAccess).Dimensions == 2) *(FixedBounds2*)&box = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, y), FixedVector2.FromRaw(x + width, y + width));
            else *(FixedBounds3*)&box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, y, z), FixedVector3.FromRaw(x + width, y + width, z + width));
            return box;
        }
        private static SpatialQuery[] Queries(int dimensions, int tick)
        {
            var p = FixedVector3.FromRaw(tick % 17 - 8, tick % 9 - 4, 0);
            return new[] {
                dimensions == 2 ? SpatialQuery.ForNearest((FixedVector2)p, 37) : SpatialQuery.ForNearest(p, 37),
                SpatialQuery.FromEnvelope(new SpatialRawBounds { MinX=-24, MinY=-24, MinZ=-24, MaxX=24, MaxY=24, MaxZ=24 }, dimensions),
                dimensions == 2 ? SpatialQuery.ForCircle((FixedVector2)p, Fixed64.FromRaw(16)) : SpatialQuery.ForSphere(p, Fixed64.FromRaw(16)),
                dimensions == 2 ? SpatialQuery.ForRay(new FixedRay2((FixedVector2)p, FixedVector2.FromRaw(1,0))) : SpatialQuery.ForRay(new FixedRay3(p, FixedVector3.FromRaw(1,0,0))),
                dimensions == 2 ? SpatialQuery.ForSegment(FixedVector2.FromRaw(-64,0), FixedVector2.FromRaw(64,0)) : SpatialQuery.ForSegment(FixedVector3.FromRaw(-64,0,0), FixedVector3.FromRaw(64,0,0)),
                dimensions == 2 ? SpatialQuery.ForPoint((FixedVector2)p) : SpatialQuery.ForPoint(p)
            };
        }
        private static void EqualQueries<TBounds,TAccess,TCodec>(Backend<TBounds,TAccess,TCodec> a, Backend<TBounds,TAccess,TCodec> b, int tick)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            SpatialQueryScratch.TryMeasure<int>(48, 96, out int size).ThrowIfFailure();
            byte* work = stackalloc byte[size]; SpatialHit<int>* left = stackalloc SpatialHit<int>[49]; SpatialHit<int>* right = stackalloc SpatialHit<int>[49];
            var scratch = new SpatialQueryScratch(work, size);
            foreach (var query in Queries(default(TAccess).Dimensions,tick))
            {
                for (int i=0;i<49;i++) { left[i].Key=999; right[i].Key=999; }
                a.Query(query,scratch,left,48,out int na).ThrowIfFailure(); b.Query(query,scratch,right,48,out int nb).ThrowIfFailure();
                Assert.AreEqual(na,nb);
                for(int i=0;i<na;i++) { Assert.AreEqual(left[i].Key,right[i].Key); Assert.AreEqual(left[i].Metric.RawValue,right[i].Metric.RawValue); Assert.AreEqual(left[i].MetricStatus,right[i].MetricStatus); }
                for(int i=na;i<49;i++) { Assert.AreEqual(999,left[i].Key); Assert.AreEqual(999,right[i].Key); }
            }
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact, 2)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact, 3)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore, 2)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore, 3)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact, 2)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact, 3)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore, 2)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore, 3)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact, 2)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact, 3)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore, 2)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore, 3)]
        public void CollisionsAndCommutingUpdateOrdersPreserveQueriesAndCanonicalBytes(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback, int dimensions)
        {
            if(dimensions==2) Permute<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(strategy,rollback);
            else Permute<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(strategy,rollback);
        }
        private static void Permute<TBounds,TAccess,TCodec>(SpatialIndexStrategy strategy,SpatialRollbackPolicy rollback)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            SpatialQueryScratch.TryMeasure<int>(48,96,out int size).ThrowIfFailure(); byte* work=stackalloc byte[size]; SpatialHit<int>* hits=stackalloc SpatialHit<int>[48];
            foreach(bool native in Backends())
            {
                using var forward=new Backend<TBounds,TAccess,TCodec>(Options(strategy,rollback),native);
                using var reversed=new Backend<TBounds,TAccess,TCodec>(Options(strategy,rollback,8),native);
                for(int i=0;i<37;i++) { int key=i*11%37; var add=new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Add,Key=key,Data=key}; forward.Apply(add).ThrowIfFailure(); reversed.Apply(add).ThrowIfFailure(); }
                var origin=default(TAccess).Dimensions==2?SpatialQuery.ForNearest(FixedVector2.zero,37):SpatialQuery.ForNearest(FixedVector3.zero,37);
                forward.Query(origin,new SpatialQueryScratch(work,size),hits,48,out int found).ThrowIfFailure(); Assert.AreEqual(37,found);
                for(int i=0;i<found;i++) Assert.AreEqual(36-i,hits[i].Key,"Non-default comparer must break exact distance ties.");
                for(int tick=1;tick<=24;tick++)
                {
                    for(int ordinal=0;ordinal<37;ordinal++)
                    {
                        int key=ordinal; var command=new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.UpdateBounds,Key=key,Bounds=Box<TBounds,TAccess>(key,tick)};
                        forward.Apply(command).ThrowIfFailure(); key=36-ordinal; command.Key=key; command.Bounds=Box<TBounds,TAccess>(key,tick); reversed.Apply(command).ThrowIfFailure();
                    }
                    if(tick%3==0) forward.Rebuild().ThrowIfFailure(); // Other StaticBvh remains dirty intentionally.
                    EqualQueries(forward,reversed,tick); CollectionAssert.AreEqual(forward.Encode(),reversed.Encode());
                }
            }
        }

        [TestCase(SpatialIndexStrategy.DynamicBvh, 2)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, 3)]
        [TestCase(SpatialIndexStrategy.SparseGrid, 2)]
        [TestCase(SpatialIndexStrategy.SparseGrid, 3)]
        [TestCase(SpatialIndexStrategy.StaticBvh, 2)]
        [TestCase(SpatialIndexStrategy.StaticBvh, 3)]
        public void TruncationAndCodecFailureAtNonzeroOffsetsPreserveBytesCursorsAndRevision(SpatialIndexStrategy strategy,int dimensions)
        {
            if(dimensions==2) AtomicRead<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(strategy);
            else AtomicRead<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(strategy);
        }
        private static void AtomicRead<TBounds,TAccess,TCodec>(SpatialIndexStrategy strategy)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            byte* arena=stackalloc byte[32768];
            foreach(bool native in Backends()) foreach(var rollback in new[]{SpatialRollbackPolicy.Compact,SpatialRollbackPolicy.FastRestore})
            {
                using var source=new Backend<TBounds,TAccess,TCodec>(Options(strategy,rollback),native);
                using var target=new Backend<TBounds,TAccess,TCodec>(Options(strategy,rollback),native);
                for(int key=0;key<8;key++)
                { source.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Add,Key=key,Data=21+key*11,Bounds=Box<TBounds,TAccess>(key,0)}).ThrowIfFailure();
                  target.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Add,Key=key+10,Data=-key,Bounds=Box<TBounds,TAccess>(key,1)}).ThrowIfFailure(); }
                var packet=source.Encode(); var before=target.Encode(); ulong revision=target.Revision;
                fixed(byte* bytes=packet)
                {
                    for(int length=7;length<packet.Length;length++)
                    {
                        var reader=new DeterministicReader(bytes,length,7); var scratch=new CanonicalScratch(arena,32768){Position=13};
                        Assert.AreNotEqual(DeterministicStatus.None,target.Read(ref reader,ref scratch,default(Int32Policy)));
                        Assert.AreEqual(7,reader.Position); Assert.AreEqual(13,scratch.Position); Assert.AreEqual(revision,target.Revision);
                    }
                    CollectionAssert.AreEqual(before,target.Encode());
                    var rejected=new DeterministicReader(bytes,packet.Length,7); var staged=new CanonicalScratch(arena,32768){Position=13};
                    Assert.AreEqual(DeterministicStatus.InvalidEncoding,target.Read(ref rejected,ref staged,default(Reject54Codec)));
                    Assert.AreEqual(7,rejected.Position); Assert.AreEqual(13,staged.Position); Assert.AreEqual(revision,target.Revision); CollectionAssert.AreEqual(before,target.Encode());
                    target.Read(ref rejected,ref staged,default(Int32Policy)).ThrowIfFailure(); Assert.AreEqual(packet.Length,rejected.Position);
                    CollectionAssert.AreEqual(packet,target.Encode()); EqualQueries(source,target,0);
                }
            }
        }

        private static BigInteger Number(long raw)=>raw==Fixed64.NegativeZero.RawValue?BigInteger.Zero:new BigInteger(raw);
        private static (BigInteger[] Min,BigInteger[] Max) Corners<TBounds,TAccess>(TBounds bounds)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds>
        {
            int d=default(TAccess).Dimensions; FixedVector3 first=default,second=default;
            if(d==2) { (*(FixedBounds2*)&bounds).GetStorage(out var a,out var b); first=a; second=b; } else (*(FixedBounds3*)&bounds).GetStorage(out first,out second);
            var min=new BigInteger[d];var max=new BigInteger[d];
            for(int axis=0;axis<d;axis++) { var a=Number(first[axis].RawValue);var b=Number(second[axis].RawValue); min[axis]=FixedBoundsRuntime.Settings.Representation==FixedBoundsRepresentation.MinMax?a:a-b; max[axis]=FixedBoundsRuntime.Settings.Representation==FixedBoundsRepresentation.MinMax?b:a+b; }
            return(min,max);
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh, 2)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, 3)]
        [TestCase(SpatialIndexStrategy.SparseGrid, 2)]
        [TestCase(SpatialIndexStrategy.SparseGrid, 3)]
        [TestCase(SpatialIndexStrategy.StaticBvh, 2)]
        [TestCase(SpatialIndexStrategy.StaticBvh, 3)]
        public void FilteredPairsAfterRandomMutationsMatchIndependentStoredCornerOracle(SpatialIndexStrategy strategy,int dimensions)
        {
            if(dimensions==2) PairOracle<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy>(strategy);
            else PairOracle<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(strategy);
        }
        private static void PairOracle<TBounds,TAccess,TCodec>(SpatialIndexStrategy strategy)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
        {
            foreach(bool native in Backends())
            {
                using var index=new Backend<TBounds,TAccess,TCodec>(Options(strategy,SpatialRollbackPolicy.Compact,1),native);
                var model=new Dictionary<int,SpatialRecord<int,int,TBounds>>();
                for(int tick=0;tick<90;tick++)
                {
                    int key=tick*17%43; bool remove=tick%7==0&&model.ContainsKey(key);
                    if(remove) { index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.Remove,Key=key}).ThrowIfFailure(); model.Remove(key); }
                    else
                    {
                        var record=new SpatialRecord<int,int,TBounds>(key,Box<TBounds,TAccess>(key,tick),tick-key,new SpatialMetadata((uint)(key%4),(uint)(key%8)));
                        index.Apply(new SpatialMutation<int,int,TBounds>{Kind=model.ContainsKey(key)?SpatialMutationKind.UpdateBounds:SpatialMutationKind.Add,Key=key,Bounds=record.Bounds,Data=record.Data,Metadata=record.Metadata}).ThrowIfFailure();
                        index.Apply(new SpatialMutation<int,int,TBounds>{Kind=SpatialMutationKind.SetData,Key=key,Data=record.Data}).ThrowIfFailure();
                        model[key]=record;
                    }
                    if(tick%5!=0) continue;
                    foreach(uint mask in new[]{uint.MaxValue,5u,0u})
                    {
                        var expected=new List<(int First,int Second)>(); var keys=new List<int>(model.Keys); keys.Sort((a,b)=>b.CompareTo(a));
                        for(int a=0;a<keys.Count;a++) for(int b=a+1;b<keys.Count;b++)
                        {
                            var left=model[keys[a]];var right=model[keys[b]];
                            if((mask&(1u<<(int)left.Metadata.Layer))==0||(mask&(1u<<(int)right.Metadata.Layer))==0) continue;
                            if(mask!=uint.MaxValue&&((left.Metadata.Flags&1)==0||(right.Metadata.Flags&1)==0||(left.Metadata.Flags&4)!=0||(right.Metadata.Flags&4)!=0)) continue;
                            if(((left.Data^right.Data)&1)==0) continue;
                            var l=Corners<TBounds,TAccess>(left.Bounds);var r=Corners<TBounds,TAccess>(right.Bounds);bool overlap=true;
                            for(int axis=0;axis<l.Min.Length;axis++) if(l.Min[axis]>r.Max[axis]||l.Max[axis]<r.Min[axis]) overlap=false;
                            if(overlap) expected.Add((left.Key,right.Key));
                        }
                        var filter=mask==uint.MaxValue?SpatialFilter.All:new SpatialFilter(mask,1,4);
                        CollectionAssert.AreEqual(expected,index.Pairs(filter));
                    }
                }
            }
        }

        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore)]
        public void RecycledSnapshotLeasesCannotRestoreOrReleaseNewRootsAndFailedBatchesPreserveWire(SpatialIndexStrategy strategy,SpatialRollbackPolicy rollback)
        {
            foreach(bool native in Backends())
            {
                using var index=new Backend<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(Options(strategy,rollback),native);
                using var other=new Backend<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy>(Options(strategy,rollback),native);
                for(int key=0;key<37;key++) index.Apply(new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.Add,Key=key,Bounds=Box<FixedBounds3,SpatialBounds3Access>(key,0)}).ThrowIfFailure();
                index.Capture(out var old).ThrowIfFailure();
                index.Apply(new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.SetData,Key=0,Data=17}).ThrowIfFailure();
                index.Capture(out var kept).ThrowIfFailure(); var stale = old; old.Dispose(); index.Capture(out var reused).ThrowIfFailure();
                try
                {
                    Assert.AreEqual(DeterministicStatus.Disposed,index.Restore(stale)); stale.Dispose();
                    Assert.AreEqual(DeterministicStatus.InvalidOwner,other.Restore(kept));
                    Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded,index.Capture(out _));
                    var baseline=index.Encode();
                    var commands=new[]{new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.Remove,Key=0},new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.Add,Key=0,Data=999,Bounds=Box<FixedBounds3,SpatialBounds3Access>(0,12)},new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.Remove,Key=777}};
                    Assert.AreEqual(DeterministicStatus.NotFound,index.Batch(commands));CollectionAssert.AreEqual(baseline,index.Encode());
                    index.Apply(new SpatialMutation<int,int,FixedBounds3>{Kind=SpatialMutationKind.Clear}).ThrowIfFailure();
                    index.Restore(reused).ThrowIfFailure();CollectionAssert.AreEqual(baseline,index.Encode());
                    index.Restore(kept).ThrowIfFailure();CollectionAssert.AreEqual(baseline,index.Encode());
                }
                finally { reused.Dispose();kept.Dispose(); }
            }
        }

        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore)]
        public void FixedMutationCaptureCanonicalImportQueryAndRestoreHaveZeroSteadyGc(SpatialIndexStrategy strategy,SpatialRollbackPolicy rollback)
        {
            var options=Options(strategy,rollback);
            using var index=new DeterministicSpatialIndex3<int,int,Int32Policy>(options);
            for(int key=0;key<24;key++) index.TryAdd(key,Box<FixedBounds3,SpatialBounds3Access>(key,0),key).ThrowIfFailure();
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure();
            byte* queryWork=stackalloc byte[bytes];byte* packet=stackalloc byte[16384];byte* importWork=stackalloc byte[32768];SpatialHit<int>* hits=stackalloc SpatialHit<int>[24];
            var query=SpatialQuery.ForNearest(FixedVector3.zero,12);var moved=Box<FixedBounds3,SpatialBounds3Access>(0,4);
            int errors=0;long before=0;
            for(int iteration=-16;iteration<200;iteration++)
            {
                if(iteration==0) before=GC.GetAllocatedBytesForCurrentThread();
                errors|=(int)index.TryCapture(out var snapshot);
                errors|=(int)index.TryUpdateBounds(0,moved);errors|=(int)index.TrySetData(0,iteration);
                errors|=(int)index.TryQuery(query,new SpatialQueryScratch(queryWork,bytes),hits,24,out _);
                var writer=new DeterministicWriter(packet,16384);errors|=(int)index.TryWriteCanonical(ref writer,default(Int32Policy),default(Int32Policy),default(FixedBounds3Policy));
                var reader=new DeterministicReader(packet,writer.Position);var scratch=new CanonicalScratch(importWork,32768);
                errors|=(int)index.TryReadCanonical(ref reader,ref scratch,default(Int32Policy),default(Int32Policy),default(FixedBounds3Policy));
                errors|=(int)index.TryRestore(snapshot);snapshot.Dispose();
            }
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.AreEqual(0,errors);Assert.AreEqual(0,allocated,"Fixed hotpaths, including canonical import and COW, must not allocate after warmup.");
        }
#if !FIXEDPOINT_STANDALONE
        [BurstCompile(CompileSynchronously = true)]
        private struct CollisionJob : IJob
        {
            internal NativeDeterministicSpatialIndex<int,int,FixedBounds3,SpatialBounds3Access,CollisionPolicy> Index;
            internal NativeArray<byte> QueryWork, PairWork;
            internal NativeArray<SpatialHit<int>> Hits;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int errors=0;
                for(int key=0;key<16;key++) errors|=(int)Index.TryAdd(key,default,key);
                errors|=(int)Index.TryQuery(SpatialQuery.ForNearest(FixedVector3.zero,8),new SpatialQueryScratch((byte*)QueryWork.GetUnsafePtr(),QueryWork.Length),
                    (SpatialHit<int>*)Hits.GetUnsafePtr(),Hits.Length,out int n);
                if(n!=8) errors|=1;
                for(int i=0;i<n;i++) if(Hits[i].Key!=15-i) errors|=1;
                errors|=(int)Index.TryGetPairs(new SpatialPairScratch((byte*)PairWork.GetUnsafePtr(),PairWork.Length),SpatialFilter.All,default(DifferentParity<FixedBounds3>),out var pairs);
                int count=0;
                for(int first=15;first>=0;first--) for(int second=first-1;second>=0;second--)
                {
                    if(((first^second)&1)==0) continue;
                    errors|=(int)pairs.TryNext(out var pair,out bool has);
                    if(!has||pair.First!=first||pair.Second!=second) errors|=1;count++;
                }
                errors|=(int)pairs.TryNext(out _,out bool extra);if(extra) errors|=1;
                int marker=0; Managed(ref marker);Result[0]=errors;Result[1]=count;Result[2]=marker;
            }
            [BurstDiscard] private static void Managed(ref int marker) => marker=1;
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)] [TestCase(SpatialIndexStrategy.SparseGrid)] [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void CollisionHashDescendingOrderingAndPairPredicateExecuteInActualBurst(SpatialIndexStrategy strategy)
        {
            var options=Options(strategy,SpatialRollbackPolicy.FastRestore);
            var index=new NativeDeterministicSpatialIndex<int,int,FixedBounds3,SpatialBounds3Access,CollisionPolicy>(options,Allocator.Persistent);
            try
            {
                SpatialQueryScratch.TryMeasure<int>(48,96,out int qs).ThrowIfFailure();SpatialPairScratch.TryMeasure<int>(48,96,out int ps).ThrowIfFailure();
                using var q=new NativeArray<byte>(qs,Allocator.TempJob);using var p=new NativeArray<byte>(ps,Allocator.TempJob);
                using var hits=new NativeArray<SpatialHit<int>>(48,Allocator.TempJob);using var result=new NativeArray<int>(3,Allocator.TempJob);
                new CollisionJob{Index=index,QueryWork=q,PairWork=p,Hits=hits,Result=result}.Schedule().Complete();
                Assert.AreEqual(0,result[0]);Assert.AreEqual(64,result[1]);Assert.AreEqual(0,result[2],"BurstDiscard marker must prove execution was compiled.");
            }
            finally { index.Dispose(); }
        }
#endif
    }
}

