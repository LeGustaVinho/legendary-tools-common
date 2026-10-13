using System;
using LegendaryTools.Deterministic.Samples;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedQuaternionWorldTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private sealed class World : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context;
            internal readonly DeterministicCell<FixedQuaternion> Cell;
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            private readonly IDeterministicSimulation<int,int> simulation;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicCell<FixedQuaternion> native;
#endif
            internal World(SnapshotPolicy snapshots, DeterministicRunnerMode mode = DeterministicRunnerMode.Lockstep, bool burst = false, long initialTick = 0,
                DeterministicReplayArchive<int,Int32Policy> replay = null, int checkpoint = 0)
            {
                FixedDeterministicProfile.TryQuaternionFingerprint(out var numeric).ThrowIfFailure();
#if !FIXEDPOINT_STANDALONE
                Context = burst ? new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory()) : new DeterministicRuntimeContext(DeterministicBackend.Managed);
#else
                if (burst) throw new NotSupportedException(); Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
#endif
                State = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1,numeric,0,1,1),1);
                var options = new BufferOptions(1, snapshotSlots:0);
#if !FIXEDPOINT_STANDALONE
                if (burst)
                {
                    native = new NativeDeterministicCell<FixedQuaternion>(options, Allocator.Persistent); native.TrySet(0, FixedQuaternion.identity).ThrowIfFailure();
                    State.TryRegister(NativeStateBindings.Cell(1,native,default(FixedQuaternionPolicy),1,256,256)).ThrowIfFailure(); simulation = new NativeQuaternionSimulation(native);
                }
                else
#endif
                {
                    Cell = new DeterministicCell<FixedQuaternion>(options); Cell.Value = FixedQuaternion.identity;
                    State.TryRegister(ManagedStateBindings.Cell(1,Cell,default(FixedQuaternionPolicy),1,256,256)).ThrowIfFailure(); simulation = new QuaternionSimulation(Cell);
                }
                State.TryFreeze().ThrowIfFailure(); if (replay != null) replay.TryRestoreCheckpoint(State,checkpoint,out _).ThrowIfFailure();
                var settings = new DeterministicRuntimeOptions(2,PredictionPolicy.HoldLast,snapshots,inputDelay:0,historyTicks:32,predictionTicks:8,keyframeInterval:8,pageBytes:64);
                Runner = new DeterministicSimulationRunner<int,Int32Policy,int>(Context, settings, State, simulation, new[] {new PlayerId(1),new PlayerId(2)},0,2,initialTick:initialTick,mode:mode);
            }
            internal FixedQuaternion Value
            {
                get
                {
                    if (Cell != null) return Cell.Value;
#if !FIXEDPOINT_STANDALONE
                    native.TryGet(0,out var result).ThrowIfFailure(); return result;
#else
                    throw new NotSupportedException();
#endif
                }
            }
            public void Dispose()
            {
                Runner.Dispose(); if (simulation is IDisposable disposable) disposable.Dispose(); State.Dispose(); Cell?.Dispose();
#if !FIXEDPOINT_STANDALONE
                if (native.IsCreated) native.Dispose();
#endif
            }
        }
        private static int Remote(int tick) => tick*7%11-5;
        private static void Submit(World world, int tick)
        { world.Runner.TrySubmitInput(new PlayerId(1),tick,tick%7-3).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(2),tick,Remote(tick)).ThrowIfFailure(); }
        private static void Equal(World a, World b, long boundary)
        {
            for (int i=0;i<4;i++) Assert.AreEqual(a.Value[i].RawValue,b.Value[i].RawValue,"component "+i);
            a.Runner.History.TryGetHash(boundary,out var first).ThrowIfFailure(); b.Runner.History.TryGetHash(boundary,out var second).ThrowIfFailure(); Assert.AreEqual(first,second);
        }
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void TenThousandDelayedTicksRollbackToTheExactOrderedLockstepWorld(SnapshotPolicy policy)
        {
            using (var ordered = new World(policy)) using (var delayed = new World(policy, DeterministicRunnerMode.Rollback))
            {
                for (int tick=0;tick<10000;tick++)
                {
                    Submit(ordered,tick); ordered.Runner.TryAdvance(1,out _).ThrowIfFailure();
                    delayed.Runner.TrySubmitInput(new PlayerId(1),tick,tick%7-3).ThrowIfFailure();
                    if (tick>=4) delayed.Runner.TrySubmitInput(new PlayerId(2),tick-4,Remote(tick-4)).ThrowIfFailure();
                    delayed.Runner.TryAdvance(1,out _).ThrowIfFailure();
                }
                for (int tick=9996;tick<10000;tick++) delayed.Runner.TrySubmitInput(new PlayerId(2),tick,Remote(tick)).ThrowIfFailure();
                delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); Equal(ordered,delayed,10000); Assert.Greater(delayed.Runner.RollbackCount,0);
            }
        }
        [Test] public void ReplayReloadAndSeekRestoreExactQuaternionStateAfterTenThousandTicks()
        {
            const int capacity=2*1024*1024;
            using (var world = new World(SnapshotPolicy.KeyframesAndDeltas))
            using (var archive = new DeterministicReplayArchive<int,Int32Policy>(world.Context,world.Runner.Fingerprint,capacity,10128,2))
            using (var loaded = new DeterministicReplayArchive<int,Int32Policy>(world.Context,world.Runner.Fingerprint,capacity,10128,2))
            {
                archive.TryBegin(world.State,0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[2];
                for (int tick=0;tick<10000;tick++)
                {
                    Submit(world,tick); world.Runner.TryAdvance(1,out _).ThrowIfFailure(); world.Runner.History.TryGetHash(tick+1,out var hash).ThrowIfFailure();
                    inputs[0]=new DeterministicPlayerInput<int>(new PlayerId(1),tick%7-3); inputs[1]=new DeterministicPlayerInput<int>(new PlayerId(2),Remote(tick));
                    archive.TryAppendInputs(tick,inputs,hash).ThrowIfFailure(); if ((tick+1)%1000==0) archive.TryAppendCheckpoint(world.State,tick+1).ThrowIfFailure();
                }
                var packet = new byte[capacity]; fixed (byte* bytes=packet)
                { var writer=new DeterministicWriter(bytes,capacity); archive.TryWriteCanonical(ref writer).ThrowIfFailure(); var reader=new DeterministicReader(bytes,writer.Position); loaded.TryReadCanonical(ref reader,world.State).ThrowIfFailure(); Assert.AreEqual(writer.Position,reader.Position); }
                using (var replay=new World(SnapshotPolicy.KeyframesAndDeltas,replay:loaded)) { loaded.TryReplayTo(replay.Runner,10000).ThrowIfFailure(); Equal(world,replay,10000); }
                loaded.TryFindCheckpoint(8555,out int checkpoint,out long boundary).ThrowIfFailure(); Assert.AreEqual(8000,boundary);
                using (var seek=new World(SnapshotPolicy.KeyframesAndDeltas,initialTick:boundary,replay:loaded,checkpoint:checkpoint)) { loaded.TryReplayTo(seek.Runner,10000).ThrowIfFailure(); Equal(world,seek,10000); }
            }
        }
        [Test] public void CellListAndSparseSetCanonicalImportDoesNotNormalizeOrRewriteRepresentations()
        {
            FixedDeterministicProfile.TryQuaternionFingerprint(out var numeric).ThrowIfFailure(); var context=new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var options=new BufferOptions(4,snapshotSlots:0); var handle=new StableHandle(1,1); var special=new FixedQuaternion(Fixed64.NegativeZero,Fixed64.NaN,Fixed64.PositiveInfinity,2);
            using (var cell=new DeterministicCell<FixedQuaternion>()) using (var list=new DeterministicList<FixedQuaternion>(options))
            using (var sparse=new DeterministicSparseSet<FixedQuaternion>(options,options)) using (var state=new DeterministicStateRegistry(context,new DeterministicCompatibilityProfile(1,numeric,0,1,1),3))
            {
                cell.Value=special; list.Add(special); sparse.Add(handle,special);
                state.TryRegister(ManagedStateBindings.Cell(1,cell,default(FixedQuaternionPolicy),1,256,256)).ThrowIfFailure();
                state.TryRegister(ManagedStateBindings.List(2,list,default(FixedQuaternionPolicy),1,1024,1024)).ThrowIfFailure();
                state.TryRegister(ManagedStateBindings.SparseSet(3,sparse,default(FixedQuaternionPolicy),1,1024,2048)).ThrowIfFailure(); state.TryFreeze().ThrowIfFailure();
                state.TryEncode(0,out byte* data,out int length).ThrowIfFailure(); var packet=new byte[length]; for (int i=0;i<length;i++) packet[i]=data[i];
                cell.Value=FixedQuaternion.identity; list[0]=FixedQuaternion.identity; sparse[handle]=FixedQuaternion.identity;
                fixed (byte* bytes=packet) { var reader=new DeterministicReader(bytes,length); state.TryReadCanonical(ref reader,out _).ThrowIfFailure(); }
                for (int i=0;i<4;i++) { Assert.AreEqual(special[i].RawValue,cell.Value[i].RawValue); Assert.AreEqual(special[i].RawValue,list[0][i].RawValue); Assert.AreEqual(special[i].RawValue,sparse[handle][i].RawValue); }
            }
        }
#if !FIXEDPOINT_STANDALONE
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ManagedAndNativeBurstWorldsProduceIdenticalBoundariesForTenThousandTicks(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var managed=new World(policy)) using (var native=new World(policy,burst:true))
                for (int tick=0;tick<10000;tick++) { Submit(managed,tick); Submit(native,tick); managed.Runner.TryAdvance(1,out _).ThrowIfFailure(); native.Runner.TryAdvance(1,out _).ThrowIfFailure(); Equal(managed,native,tick+1); }
        }
#endif
    }
}
