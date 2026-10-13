using System;

namespace LegendaryTools.Deterministic
{
    public interface IDeterministicSessionObserver
    {
        void OnCheckpointInstalled(long boundary);
        void OnLeaderChanged(PeerId leader);
        void OnReadinessChanged(bool ready);
    }
    /// <summary>Shared authoritative-host/P2P session, with Raft as the commit and migration authority.</summary>
    public sealed unsafe partial class DeterministicNetworkSession<TInput, TEquality, TEvent, TCodec> :
        IDeterministicRaftStateMachine, IDeterministicRaftApplicationMessages, IDisposable
        where TInput : unmanaged where TEquality : unmanaged, IDeterministicEquality<TInput>
        where TEvent : unmanaged where TCodec : unmanaged, IDeterministicCodec<TInput>
    {
        private const byte InputMessage = 32, JoinMessage = 33, RedirectMessage = 34, StateHashMessage = 36;
        private struct Owner { internal PeerId Peer; internal TInput Carry; internal byte Active; }
        private struct BufferedInput { internal long Tick; internal TInput Value; }
        private struct ExpectedHash { internal long Boundary; internal ulong Hash, Term; }
        private readonly DeterministicRuntimeContext context;
        private readonly DeterministicRuntimeOptions runtimeOptions;
        private readonly DeterministicRaftOptions raftOptions;
        private readonly DeterministicStateRegistry state;
        private readonly IDeterministicSimulation<TInput, TEvent> simulation;
        private readonly IDeterministicEventSink<TEvent> eventSink;
        private readonly IDeterministicSessionObserver observer;
        private readonly Func<long, IDeterministicWorldHistory> historyFactory;
        private readonly TEquality equality;
        private readonly TCodec codec;
        private readonly TInput neutral;
        private readonly int maxInputBytes, maxEvents, bufferedRows, maxDatagram;
        private readonly DeterministicRunnerMode runnerMode;
        private readonly DeterministicSessionTopology topology;
        private readonly IDeterministicMemory memory;
        private readonly ulong fingerprint;
        private Owner* owners, committedOwners, stagedOwners;
        private int ownerCount, committedOwnerCount;
        private BufferedInput* outgoing, incomingInputs;
        private ExpectedHash* expectedHashes;
        private PeerId* waiting;
        private int waitingCount;
        private DeterministicPlayerInput<TInput>* frameInputs;
        private byte* packet;
        private long appliedBoundary, proposedBoundary, localCaptureStart, lastSend = -1, lastJoin = -1;
        private PeerId observedLeader, joinTarget;
        private ulong observedTerm;
        private bool disposed, faulted, observedReady, joinRequested;
        public DeterministicRaftNode Raft { get; }
        public DeterministicSimulationRunner<TInput, TEquality, TEvent> Runner { get; private set; }
        public long CommittedBoundary => appliedBoundary;
        public bool IsReady => !IsFaulted && !disposed && Raft.HasQuorum && Raft.IsMembershipStable && Raft.IsAdmitted;
        public bool IsFaulted => faulted || Raft.IsFaulted || Runner.IsFaulted;
        public PeerId LocalPeer => Raft.LocalPeer;
        public ulong Fingerprint => fingerprint;
        /// <summary>Earliest capture tick still needed from this process after checkpoint installation or ordered ownership changes.</summary>
        public long EarliestLocalCaptureTick => Math.Max(localCaptureStart, Math.Max(0, appliedBoundary - runtimeOptions.InputDelay));

        public DeterministicNetworkSession(DeterministicRuntimeContext context, DeterministicRuntimeOptions runtimeOptions,
            DeterministicRaftOptions raftOptions, IDeterministicTransport transport, DeterministicSessionId session,
            DeterministicStateRegistry state, IDeterministicSimulation<TInput, TEvent> simulation, ReadOnlySpan<PeerId> initialVoters,
            DeterministicSessionTopology topology, TInput neutral, int maxInputBytes, int maxEventsPerTick,
            TEquality equality = default, TCodec codec = default, DeterministicRunnerMode mode = DeterministicRunnerMode.Rollback,
            bool startAsLearner = false, IDeterministicEventSink<TEvent> eventSink = null,
            IDeterministicSessionObserver observer = null, long initialTick = 0,
            ReadOnlySpan<DeterministicPlayerInput<TInput>> initialCarry = default,
            Func<long, IDeterministicWorldHistory> historyFactory = null)
        {
            if (context == null || !runtimeOptions.IsValid || !raftOptions.IsValid || transport == null || state == null || !state.IsFrozen ||
                state.Backend != context.Backend || simulation == null || !equality.IsValid || codec.SchemaId == 0 || codec.Version == 0 ||
                maxInputBytes <= 0 || maxInputBytes > transport.MaxDatagramBytes - DeterministicSessionProtocol.Overhead - 32 ||
                maxEventsPerTick <= 0 || topology > DeterministicSessionTopology.PeerToPeer || initialVoters.Length == 0 || initialVoters.Length > runtimeOptions.MaxPlayers)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.context = context; this.runtimeOptions = runtimeOptions; this.raftOptions = raftOptions; this.state = state;
            this.simulation = simulation; this.eventSink = eventSink; this.observer = observer; this.equality = equality; this.codec = codec;
            this.historyFactory = historyFactory;
            this.neutral = neutral; this.maxInputBytes = maxInputBytes; maxEvents = maxEventsPerTick; runnerMode = mode; this.topology = topology;
            maxDatagram = transport.MaxDatagramBytes; bufferedRows = checked(runtimeOptions.HistoryTicks * 2);
            long ownerBytes = Align((long)sizeof(Owner) * runtimeOptions.MaxPlayers), inputBytes = Align((long)sizeof(BufferedInput) * bufferedRows);
            long hashes = Align((long)sizeof(ExpectedHash) * (runtimeOptions.HistoryTicks + 1));
            long wait = Align((long)sizeof(PeerId) * raftOptions.MaxPeers), frames = Align((long)sizeof(DeterministicPlayerInput<TInput>) * runtimeOptions.MaxPlayers);
            long bytes = ownerBytes * 3 + inputBytes * 2 + hashes + wait + frames + maxDatagram;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes); owners = (Owner*)memory.Pointer; committedOwners = (Owner*)(memory.Pointer + ownerBytes);
            stagedOwners = (Owner*)(memory.Pointer + ownerBytes * 2); outgoing = (BufferedInput*)(memory.Pointer + ownerBytes * 3);
            incomingInputs = (BufferedInput*)((byte*)outgoing + inputBytes); expectedHashes = (ExpectedHash*)((byte*)incomingInputs + inputBytes);
            waiting = (PeerId*)((byte*)expectedHashes + hashes); frameInputs = (DeterministicPlayerInput<TInput>*)((byte*)waiting + wait); packet = (byte*)frameInputs + frames;
            for (int i = 0; i < bufferedRows; i++) outgoing[i].Tick = -1;
            for (int i = 0; i <= runtimeOptions.HistoryTicks; i++) expectedHashes[i].Boundary = -1;
            ownerCount = committedOwnerCount = initialVoters.Length;
            for (int i = 0; i < ownerCount; i++) owners[i] = new Owner { Peer = initialVoters[i], Carry = neutral, Active = 1 };
            SortOwners(owners, ownerCount);
            for (int i = 0; i < ownerCount; i++)
            {
                for (int j = 0; j < initialCarry.Length; j++) if (initialCarry[j].Player == owners[i].Peer.Player) owners[i].Carry = initialCarry[j].Value;
                committedOwners[i] = owners[i];
            }
            appliedBoundary = proposedBoundary = localCaptureStart = initialTick;
            try
            {
                Runner = CreateRunner(owners, ownerCount, initialTick, true);
                fingerprint = DeterministicHash.Mix(Runner.Fingerprint ^ ((ulong)codec.SchemaId << 16 | codec.Version) ^
                    DeterministicHash.Mix((uint)maxInputBytes) ^ ((ulong)(byte)topology << 56));
                Raft = new DeterministicRaftNode(context, transport, session, fingerprint, raftOptions, initialVoters, this, startAsLearner);
                Raft.SetApplicationMessages(this); Runner.SetSuspension(DeterministicStatus.QuorumLost);
            }
            catch { Runner?.Dispose(); memory.Dispose(); throw; }
        }
        private static long Align(long bytes) => (bytes + 15) & ~15L;
        private static void SortOwners(Owner* values, int count)
        {
            for (int i = 1; i < count; i++) { var value = values[i]; int j = i - 1; while (j >= 0 && values[j].Peer.Player.CompareTo(value.Peer.Player) > 0) { values[j + 1] = values[j]; j--; } values[j + 1] = value; }
        }
        private DeterministicSimulationRunner<TInput, TEquality, TEvent> CreateRunner(Owner* source, int count, long boundary, bool seedDelay)
        {
            PlayerId* players = stackalloc PlayerId[64]; int activeCount = 0;
            for (int i = 0; i < count; i++) if (source[i].Active != 0) players[activeCount++] = source[i].Peer.Player;
            IDeterministicWorldHistory history = historyFactory?.Invoke(boundary);
            if (historyFactory != null && history == null) throw new ArgumentException("The history factory must return a fresh empty provider.");
            if (Runner != null && ReferenceEquals(history, Runner.History))
                throw new ArgumentException("A replacement runner cannot reuse its live predecessor's history.");
            DeterministicSimulationRunner<TInput, TEquality, TEvent> runner = null;
            try
            {
                runner = new DeterministicSimulationRunner<TInput, TEquality, TEvent>(context, runtimeOptions, state, simulation,
                    new ReadOnlySpan<PlayerId>(players, activeCount), neutral, maxEvents, equality, runnerMode,
                    DeterministicConfirmationMode.ExternalCommit, eventSink, initialTick: boundary, seedInputDelay: false,
                    history: history, captureInitialHistory: seedDelay);
                for (int i = 0; i < count; i++)
                {
                    if (source[i].Active == 0) { runner.Inputs.TryRegisterPlayer(source[i].Peer.Player, boundary).ThrowIfFailure(); runner.Inputs.TryDeactivatePlayer(source[i].Peer.Player, boundary).ThrowIfFailure(); }
                    runner.Inputs.TrySeedLastInput(source[i].Peer.Player, boundary - 1, source[i].Carry).ThrowIfFailure();
                    if (seedDelay && source[i].Active != 0) for (int delay = 0; delay < runtimeOptions.InputDelay; delay++)
                        runner.TrySubmitInput(source[i].Peer.Player, boundary + delay,
                            boundary == 0 || runtimeOptions.Prediction == PredictionPolicy.Neutral ? neutral : source[i].Carry).ThrowIfFailure();
                }
                return runner;
            }
            catch { if (runner != null) runner.Dispose(); else history?.Dispose(); throw; }
        }
        private int OwnerIndex(PlayerId player)
        { for (int i = 0; i < ownerCount; i++) if (owners[i].Peer.Player == player) return i; return -1; }
        public DeterministicStatus TrySubmitLocalInput(long captureTick, in TInput input)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (IsFaulted) return DeterministicStatus.ProtocolConflict;
            int owner = OwnerIndex(LocalPeer.Player);
            if (!Raft.IsAdmitted || !Raft.IsMembershipStable || owner < 0 || owners[owner].Peer != LocalPeer) return DeterministicStatus.Pending;
            if (captureTick < 0 || captureTick > long.MaxValue - runtimeOptions.InputDelay) return DeterministicStatus.OutOfRange;
            var measure = DeterministicWriter.CreateMeasure(); var status = codec.TryWrite(ref measure, input); if (!status.IsSuccess()) return status;
            if (measure.Position > maxInputBytes) return DeterministicStatus.CapacityExceeded;
            long target = captureTick + runtimeOptions.InputDelay; status = Runner.TrySubmitInput(LocalPeer.Player, target, input); if (!status.IsSuccess()) return status;
            outgoing[(int)(target % bufferedRows)] = new BufferedInput { Tick = target, Value = input }; return DeterministicStatus.None;
        }
        /// <summary>Existing live processes may reconnect with their incarnation. A restarted process must use a fresh learner incarnation.</summary>
        public DeterministicStatus TryRequestJoinOrReconnect(PeerId bootstrap)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!bootstrap.IsValid || bootstrap == LocalPeer) return DeterministicStatus.InvalidConfiguration;
            joinTarget = bootstrap; joinRequested = true; lastJoin = -1; return DeterministicStatus.None;
        }
        private DeterministicStatus UpdateStableRoster()
        {
            if (!Raft.IsMembershipStable || Raft.LastApplied != Raft.CommitIndex) return DeterministicStatus.None;
            PeerId* ids = stackalloc PeerId[64]; var status = Raft.TryGetVoters(new Span<PeerId>(ids, 64), out int count); if (!status.IsSuccess()) return status;
            if (count > runtimeOptions.MaxPlayers) return DeterministicStatus.CapacityExceeded;
            bool changed = count != ownerCount;
            for (int i = 0; i < count && !changed; i++) changed = owners[i].Peer != ids[i];
            if (!changed) return DeterministicStatus.None;
            for (int i = 0; i < count; i++)
            {
                stagedOwners[i] = new Owner { Peer = ids[i], Carry = neutral, Active = 1 };
                int old = OwnerIndex(ids[i].Player); if (old >= 0) Runner.Inputs.TryGetLastActual(ids[i].Player, appliedBoundary, out stagedOwners[i].Carry, out _);
            }
            return PrepareOwners(stagedOwners, count, appliedBoundary);
        }
        private DeterministicStatus PrepareOwners(Owner* source, int count, long boundary)
        {
            PlayerId* players = stackalloc PlayerId[64];
            for (int i = 0; i < count; i++)
            {
                players[i] = source[i].Peer.Player;
                Runner.Inputs.TryGetLastActual(players[i], boundary, out source[i].Carry, out _);
            }
            var status = Runner.TryPrepareRoster(new ReadOnlySpan<PlayerId>(players, count), boundary); if (!status.IsSuccess()) return status;
            for (int i = 0; i < count; i++)
            {
                int old = OwnerIndex(source[i].Peer.Player); bool replacement = old < 0 || owners[old].Peer != source[i].Peer;
                if (replacement)
                {
                    Runner.Inputs.TryForgetFutureInputs(source[i].Peer.Player, boundary).ThrowIfFailure();
                    TInput seed = runtimeOptions.Prediction == PredictionPolicy.Neutral ? neutral : source[i].Carry;
                    for (int delay = 0; delay < runtimeOptions.InputDelay; delay++) Runner.TrySubmitInput(source[i].Peer.Player, boundary + delay, seed).ThrowIfFailure();
                    if (source[i].Peer.Player == LocalPeer.Player)
                    { localCaptureStart = boundary; for (int row = 0; row < bufferedRows; row++) outgoing[row].Tick = -1; }
                }
            }
            ownerCount = count; for (int i = 0; i < count; i++) owners[i] = source[i]; proposedBoundary = boundary;
            RememberOwners(false);
            return DeterministicStatus.None;
        }
        private void RememberOwners(bool fromFrame)
        {
            for (int i = 0; i < committedOwnerCount; i++) committedOwners[i].Active = 0;
            for (int i = 0; i < ownerCount; i++)
            {
                int found = -1; for (int j = 0; j < committedOwnerCount; j++) if (committedOwners[j].Peer.Player == owners[i].Peer.Player) { found = j; break; }
                if (found < 0) { found = committedOwnerCount++; }
                committedOwners[found] = owners[i]; committedOwners[found].Active = 1;
                if (fromFrame) for (int j = 0; j < ownerCount; j++) if (frameInputs[j].Player == owners[i].Peer.Player) committedOwners[found].Carry = frameInputs[j].Value;
            }
            SortOwners(committedOwners, committedOwnerCount);
        }
        private void SynchronizeProposalBoundary()
        {
            if (observedTerm == Raft.CurrentTerm && observedLeader == Raft.Leader) return;
            observedTerm = Raft.CurrentTerm; proposedBoundary = appliedBoundary;
            for (ulong index = Raft.LastApplied + 1; index <= Raft.LastLogIndex; index++)
            {
                if (!Raft.TryGetLog(index, out var info, out var command).IsSuccess() || !info.IsCommand || command.Length < 9 || command[0] != 1) continue;
                ulong tick = 0; for (int i = 0; i < 8; i++) tick |= (ulong)command[1 + i] << (8 * i);
                if (tick < long.MaxValue) proposedBoundary = Math.Max(proposedBoundary, (long)tick + 1);
            }
        }
        public DeterministicStatus TryPump(long nowMs, int maxSimulationTicks = 1, int maxProposals = 8)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (IsFaulted) return DeterministicStatus.ProtocolConflict;
            if (maxSimulationTicks < 0 || maxProposals < 0) return DeterministicStatus.OutOfRange;
            var status = Raft.TryPump(nowMs);
            if (!status.IsSuccess() && status != DeterministicStatus.Pending) { Runner.SetSuspension(status); return status; }
            status = UpdateStableRoster(); if (!status.IsSuccess()) return status;
            SynchronizeProposalBoundary();
            if (Raft.Role == DeterministicRaftRole.Leader && Raft.HasQuorum)
            {
                ProcessAdmissions();
                RemoveTimedOutMembers(nowMs);
                if (Raft.IsMembershipStable) for (int i = 0; i < maxProposals; i++)
                {
                    status = ProposeFrame(); if (status == DeterministicStatus.Pending || status == DeterministicStatus.HistoryExhausted || status == DeterministicStatus.CapacityExceeded) break;
                    if (!status.IsSuccess()) return status;
                }
                if (waitingCount == 0 && Raft.LastLogIndex - Raft.SnapshotIndex > (uint)raftOptions.LogCapacity / 2) Raft.TryCompact();
            }
            if (lastSend < 0 || nowMs - lastSend >= raftOptions.HeartbeatMs)
            { SendInputs(); SendStateHash(); lastSend = nowMs; }
            if (joinRequested && (lastJoin < 0 || nowMs - lastJoin >= raftOptions.HeartbeatMs * 2L))
            { Raft.TrySendApplication(joinTarget, JoinMessage, ReadOnlySpan<byte>.Empty); lastJoin = nowMs; }
            bool ready = IsReady;
            Runner.SetSuspension(ready ? DeterministicStatus.None : Raft.HasQuorum ? DeterministicStatus.Pending : DeterministicStatus.QuorumLost);
            if (ready)
            {
                status = Runner.TryAdvance(maxSimulationTicks, out _);
                if (!status.IsSuccess() && status != DeterministicStatus.Pending) return status;
            }
            status = CheckHashes(); if (!status.IsSuccess()) { faulted = true; Runner.SetSuspension(status); return status; }
            if (observedLeader != Raft.Leader) { observedLeader = Raft.Leader; observer?.OnLeaderChanged(observedLeader); }
            if (observedReady != ready) { observedReady = ready; observer?.OnReadinessChanged(ready); }
            if (ready && joinRequested) joinRequested = false;
            return ready ? DeterministicStatus.None : Raft.HasQuorum ? DeterministicStatus.Pending : DeterministicStatus.QuorumLost;
        }
        private void RemoveTimedOutMembers(long nowMs)
        {
            if (raftOptions.MembershipTimeoutMs == 0 || !Raft.IsMembershipStable) return;
            PeerId* voters = stackalloc PeerId[64]; Raft.TryGetVoters(new Span<PeerId>(voters, 64), out int count);
            int retained = 0;
            for (int i = 0; i < count; i++)
            {
                if (voters[i] == LocalPeer || !Raft.TryGetLastContact(voters[i], out long seen).IsSuccess() || nowMs - seen < raftOptions.MembershipTimeoutMs)
                    voters[retained++] = voters[i];
            }
            if (retained > 0 && retained < count) Raft.TryBeginMembershipChange(new ReadOnlySpan<PeerId>(voters, retained), out _);
        }
        public void Dispose()
        { if (disposed) return; disposed = true; Raft.Dispose(); Runner.Dispose(); memory.Dispose(); owners = committedOwners = stagedOwners = null; outgoing = incomingInputs = null; expectedHashes = null; waiting = null; frameInputs = null; packet = null; }
    }
}
