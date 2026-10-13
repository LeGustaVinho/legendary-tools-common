using System;
using LegendaryTools.Deterministic.Samples;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedQuaternionSessionTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private sealed class Node : IDisposable
        {
            internal readonly DeterministicCell<FixedQuaternion> Cell;
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicNetworkSession<int,Int32Policy,int,Int32Policy> Session;
            private readonly IDeterministicSimulation<int,int> simulation;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicCell<FixedQuaternion> native;
#endif
            internal Node(DeterministicRuntimeContext context, IDeterministicTransport transport, PeerId[] voters, SnapshotPolicy snapshots)
            {
                FixedDeterministicProfile.TryQuaternionFingerprint(out var numeric).ThrowIfFailure(); State=new DeterministicStateRegistry(context,new DeterministicCompatibilityProfile(1,numeric,0,1,1),1);
#if !FIXEDPOINT_STANDALONE
                if (context.Backend == DeterministicBackend.BurstCompiler)
                {
                    native=new NativeDeterministicCell<FixedQuaternion>(new BufferOptions(1,snapshotSlots:0),Allocator.Persistent); native.TrySet(0,FixedQuaternion.identity).ThrowIfFailure();
                    State.TryRegister(NativeStateBindings.Cell(1,native,default(FixedQuaternionPolicy),1,256,256)).ThrowIfFailure(); simulation=new NativeQuaternionSimulation(native);
                }
                else
#endif
                {
                    Cell=new DeterministicCell<FixedQuaternion>(new BufferOptions(1,snapshotSlots:0)); Cell.Value=FixedQuaternion.identity;
                    State.TryRegister(ManagedStateBindings.Cell(1,Cell,default(FixedQuaternionPolicy),1,256,256)).ThrowIfFailure(); simulation=new QuaternionSimulation(Cell);
                }
                State.TryFreeze().ThrowIfFailure();
                Session=new DeterministicNetworkSession<int,Int32Policy,int,Int32Policy>(context,
                    new DeterministicRuntimeOptions(2,PredictionPolicy.HoldLast,snapshots,inputDelay:0,historyTicks:128,pageBytes:64),
                    new DeterministicRaftOptions(4,128,1024,65536),transport,new DeterministicSessionId(4120),State,simulation,voters,DeterministicSessionTopology.PeerToPeer,0,4,4);
            }
            public void Dispose() { Session.Dispose(); if (simulation is IDisposable d) d.Dispose(); State.Dispose(); Cell?.Dispose();
#if !FIXEDPOINT_STANDALONE
                if (native.IsCreated) native.Dispose();
#endif
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void QuaternionSessionsCommitExactWorldsWithDelayLossAndDuplication(SnapshotPolicy snapshots)
        {
            var managed=new DeterministicRuntimeContext(DeterministicBackend.Managed);
#if !FIXEDPOINT_STANDALONE
            var rightContext=new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler,new NativeDeterministicMemoryFactory());
#else
            var rightContext=managed;
#endif
            using (var network=new DeterministicLoopbackNetwork(managed,2,8192,2048,new LoopbackFaults(0,20,80,250,4120)))
            {
                var voters=new[] {new PeerId(new PlayerId(1),1),new PeerId(new PlayerId(2),1)};
                using (var first=network.CreateEndpoint(voters[0])) using (var second=network.CreateEndpoint(voters[1]))
                using (var left=new Node(managed,first,voters,snapshots)) using (var right=new Node(rightContext,second,voters,snapshots))
                {
                    for (int tick=0;tick<60;tick++) { left.Session.TrySubmitLocalInput(tick,tick%9-4).ThrowIfFailure(); right.Session.TrySubmitLocalInput(tick,tick*7%11-5).ThrowIfFailure(); }
                    left.Session.Raft.TryStartElection().ThrowIfFailure();
                    for (int step=0;step<1200;step++)
                    {
                        long time=step*10; network.TryAdvanceTime(time).ThrowIfFailure();
                        var a=left.Session.TryPump(time); var b=right.Session.TryPump(time);
                        Assert.IsTrue(a.IsSuccess() || a==DeterministicStatus.Pending || a==DeterministicStatus.QuorumLost); Assert.IsTrue(b.IsSuccess() || b==DeterministicStatus.Pending || b==DeterministicStatus.QuorumLost);
                    }
                    Assert.AreEqual(60,left.Session.CommittedBoundary); Assert.AreEqual(60,right.Session.CommittedBoundary); Assert.IsFalse(left.Session.IsFaulted); Assert.IsFalse(right.Session.IsFaulted);
                    left.Session.Runner.History.TryRestore(60,out byte* aBytes,out int aLength).ThrowIfFailure(); right.Session.Runner.History.TryRestore(60,out byte* bBytes,out int bLength).ThrowIfFailure(); Assert.AreEqual(aLength,bLength);
                    for (int i=0;i<aLength;i++) Assert.AreEqual(aBytes[i],bBytes[i],"checkpoint byte "+i);
                }
            }
        }
    }
}
