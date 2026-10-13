using System;

namespace LegendaryTools.Deterministic
{
    public enum DeterministicConfirmationMode : byte { InputCompletion, ExternalCommit }
    /// <summary>Simulation only. Every future-affecting value must be registered; native jobs must complete before returning.</summary>
    public interface IDeterministicSimulation<TInput, TEvent> where TInput : unmanaged where TEvent : unmanaged
    {
        DeterministicStatus TryStep(in DeterministicInputFrame<TInput> inputs, DeterministicEventWriter<TEvent> events);
    }

    /// <summary>Rollback/lockstep orchestration. Tick N is simulated from checkpoint boundary N to N+1.</summary>
    public sealed unsafe class DeterministicSimulationRunner<TInput, TEquality, TEvent> : IDisposable
        where TInput : unmanaged where TEquality : unmanaged, IDeterministicEquality<TInput> where TEvent : unmanaged
    {
        private readonly DeterministicRuntimeOptions options;
        private readonly DeterministicRunnerMode mode;
        private readonly DeterministicConfirmationMode confirmation;
        private readonly IDeterministicSimulation<TInput, TEvent> simulation;
        private long rollback = -1, replayUntil = -1, committedThrough;
        private bool disposed, faulted;
        private DeterministicStatus suspension;
        public DeterministicStateRegistry State { get; }
        public DeterministicInputHistory<TInput, TEquality> Inputs { get; }
        public IDeterministicWorldHistory History { get; }
        public DeterministicEventLedger<TEvent> Events { get; }
        public long NextTick => Inputs.NextTick;
        public long ConfirmedThrough => Events.ConfirmedThrough;
        public long RollbackCount { get; private set; }
        public long ReplayedTicks { get; private set; }
        public Exception LastInfrastructureException { get; private set; }
        public DeterministicBackend Backend => State.Backend;
        public bool IsFaulted => faulted || State.IsFaulted;
        public DeterministicRunnerMode Mode => mode;
        public DeterministicConfirmationMode Confirmation => confirmation;
        public ulong Fingerprint => DeterministicHash.Mix(DeterministicHash.Mix(
            DeterministicHash.Mix(State.Fingerprint ^ options.Fingerprint) ^ History.HashAlgorithmId) ^ History.HashConfigurationFingerprint);

        public DeterministicSimulationRunner(DeterministicRuntimeContext context, DeterministicRuntimeOptions options,
            DeterministicStateRegistry state, IDeterministicSimulation<TInput, TEvent> simulation,
            ReadOnlySpan<PlayerId> players, TInput neutral, int maxEventsPerTick, TEquality equality = default,
            DeterministicRunnerMode mode = DeterministicRunnerMode.Rollback,
            DeterministicConfirmationMode confirmation = DeterministicConfirmationMode.InputCompletion,
            IDeterministicEventSink<TEvent> eventSink = null, bool deliverSpeculativeEvents = true, long initialTick = 0,
            bool seedInputDelay = true, IDeterministicWorldHistory history = null)
        {
            if (context == null || !options.IsValid || state == null || !state.IsFrozen || simulation == null ||
                state.Backend != context.Backend || players.Length == 0 || players.Length > options.MaxPlayers ||
                initialTick < 0 || initialTick > long.MaxValue - options.InputDelay - 1 || maxEventsPerTick <= 0 ||
                mode > DeterministicRunnerMode.Lockstep || confirmation > DeterministicConfirmationMode.ExternalCommit || !equality.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            for (int i = 0; i < players.Length; i++)
            {
                if (!players[i].IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                for (int j = 0; j < i; j++) if (players[i] == players[j]) DeterministicStatus.DuplicateKey.ThrowIfFailure();
            }
            // Validate before taking ownership or allocating the runner's other ledgers.
            if (history != null && (history.Backend != context.Backend || history.Capacity < (long)options.HistoryTicks + 1 ||
                history.MaxCheckpointBytes < state.MaxCanonicalBytes || history.NewestTick != -1 || history.OldestTick != -1 ||
                history.HashAlgorithmId == 0 || history.HashConfigurationFingerprint == 0))
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.options = options; this.mode = mode; this.confirmation = confirmation; this.simulation = simulation; State = state;
            committedThrough = initialTick - 1;
            try
            {
                Inputs = new DeterministicInputHistory<TInput, TEquality>(context, options.MaxPlayers, options.HistoryTicks, options.Prediction, neutral, equality, initialTick);
                History = history ?? new DeterministicWorldHistory(context, state.MaxCanonicalBytes,
                    checked(options.HistoryTicks + 1), options.Snapshots, options.KeyframeInterval, options.PageBytes);
                Events = new DeterministicEventLedger<TEvent>(context, checked(options.HistoryTicks + 1), maxEventsPerTick, eventSink, deliverSpeculativeEvents, initialTick);
                for (int i = 0; i < players.Length; i++) Inputs.TryRegisterPlayer(players[i], initialTick).ThrowIfFailure();
                if (seedInputDelay) for (int delay = 0; delay < options.InputDelay; delay++)
                    for (int player = 0; player < players.Length; player++) Inputs.TrySubmit(players[player], initialTick + delay, neutral, out _).ThrowIfFailure();
                History.TryCapture(state, initialTick).ThrowIfFailure();
            }
            catch { Inputs?.Dispose(); History?.Dispose(); Events?.Dispose(); throw; }
        }
        private DeterministicStatus Ready() => disposed ? DeterministicStatus.Disposed : IsFaulted ?
            DeterministicStatus.ProtocolConflict : suspension;
        /// <summary>Network integration sets QuorumLost while the active membership cannot acknowledge a strict majority.</summary>
        public void SetSuspension(DeterministicStatus reason) { suspension = reason; }
        public DeterministicStatus TrySubmitInput(PlayerId player, long tick, in TInput input)
        {
            if (disposed) return DeterministicStatus.Disposed;
            var status = Inputs.TrySubmit(player, tick, input, out long correction); if (!status.IsSuccess()) return status;
            if (correction >= 0 && correction < NextTick)
            {
                if (correction <= ConfirmedThrough) { faulted = true; return DeterministicStatus.ProtocolConflict; }
                rollback = rollback < 0 ? correction : Math.Min(rollback, correction);
            }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TrySubmitLocalInput(PlayerId player, long captureTick, in TInput input)
        {
            if (captureTick < 0 || captureTick > long.MaxValue - options.InputDelay) return DeterministicStatus.OutOfRange;
            return TrySubmitInput(player, captureTick + options.InputDelay, input);
        }
        public DeterministicStatus TryCommitInputsThrough(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (confirmation != DeterministicConfirmationMode.ExternalCommit) return DeterministicStatus.InvalidConfiguration;
            if (tick < committedThrough) return DeterministicStatus.OutOfRange;
            if (tick > Inputs.CompleteThrough) return DeterministicStatus.Pending;
            committedThrough = tick; return ConfirmAvailable();
        }
        /// <summary>Apply a committed roster change before resolving that boundary's inputs; existing speculation is replayed afterwards.</summary>
        public DeterministicStatus TryPrepareRoster(ReadOnlySpan<PlayerId> active, long boundary)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (boundary <= ConfirmedThrough || boundary > NextTick || boundary < History.OldestTick || boundary < Inputs.OldestTick) return DeterministicStatus.OutOfRange;
            var valid = Inputs.CanApplyActiveRoster(active, boundary); if (!valid.IsSuccess()) return valid;
            if (boundary < NextTick)
            {
                replayUntil = Math.Max(replayUntil, NextTick);
                var status = History.TryRestore(State, boundary); if (!status.IsSuccess()) return status;
                status = History.TryTruncate(boundary); if (!status.IsSuccess()) return status;
                status = Inputs.TryRewind(boundary); if (!status.IsSuccess()) return status;
                status = Events.TryRetractFrom(boundary); if (!status.IsSuccess()) return status;
                RollbackCount++;
            }
            rollback = -1; return Inputs.TryApplyActiveRoster(active, boundary);
        }
        private DeterministicStatus ConfirmAvailable()
        {
            long limit = Math.Min(NextTick - 1, Inputs.CompleteThrough);
            if (confirmation == DeterministicConfirmationMode.ExternalCommit) limit = Math.Min(limit, committedThrough);
            if (rollback >= 0 && rollback <= limit) return DeterministicStatus.Pending;
            return limit <= ConfirmedThrough ? DeterministicStatus.None : Events.TryConfirmThrough(limit);
        }
        private DeterministicStatus FailTick(long tick, DeterministicStatus failure)
        {
            bool restored;
            try
            {
                restored = History.TryRestore(State, tick).IsSuccess() && History.TryTruncate(tick).IsSuccess() && Inputs.TryRewind(tick).IsSuccess();
                Events.TryAbandon(tick);
            }
            catch (Exception error) { LastInfrastructureException = error; faulted = true; return DeterministicStatus.ProtocolConflict; }
            if (!restored) { faulted = true; return DeterministicStatus.ProtocolConflict; }
            return failure;
        }
        private DeterministicStatus ExecuteTick()
        {
            long tick = NextTick;
            LastInfrastructureException = null;
            bool predict = mode == DeterministicRunnerMode.Rollback;
            if (confirmation == DeterministicConfirmationMode.ExternalCommit)
            {
                if (mode == DeterministicRunnerMode.Lockstep && tick > committedThrough) return DeterministicStatus.Pending;
                if (tick > committedThrough && tick - (ConfirmedThrough + 1) >= options.PredictionTicks) return DeterministicStatus.Pending;
            }
            else if (tick > Inputs.CompleteThrough && tick - (Inputs.CompleteThrough + 1) >= options.PredictionTicks)
                return DeterministicStatus.Pending;
            var status = Inputs.TryResolve(tick, predict, out var inputs); if (!status.IsSuccess()) return status;
            status = Events.TryBegin(tick, out var writer); if (!status.IsSuccess()) { Inputs.TryRewind(tick); return status; }
            try
            {
                status = simulation.TryStep(inputs, writer); if (!status.IsSuccess()) return FailTick(tick, status);
                status = Events.TryPreparePublish(tick); if (!status.IsSuccess()) return FailTick(tick, status);
                status = History.TryCapture(State, tick + 1); if (!status.IsSuccess()) return FailTick(tick, status);
                status = Inputs.TryAdvance(tick); if (!status.IsSuccess()) return FailTick(tick, status);
            }
            catch (Exception error) { LastInfrastructureException = error; return FailTick(tick, DeterministicStatus.InvalidConfiguration); }
            // Publication may already have called an external sink; retrying could duplicate an irreversible effect.
            try
            {
                status = Events.TryPublish(tick); if (!status.IsSuccess()) { faulted = true; return DeterministicStatus.ProtocolConflict; }
                status = ConfirmAvailable(); return status == DeterministicStatus.Pending ? DeterministicStatus.None : status;
            }
            catch (Exception error) { LastInfrastructureException = error; faulted = true; return DeterministicStatus.ProtocolConflict; }
        }
        public DeterministicStatus TrySynchronizeRollback()
            => TrySynchronizeRollback(int.MaxValue, out _);
        /// <summary>Replay at most maxReplayTicks. Pending preserves the remaining correction target and real inputs.</summary>
        public DeterministicStatus TrySynchronizeRollback(int maxReplayTicks, out int replayed)
        {
            replayed = 0;
            if (maxReplayTicks < 0) return DeterministicStatus.OutOfRange;
            var status = Ready(); if (!status.IsSuccess()) return status;
            if (rollback >= 0)
            {
                long target = rollback;
                replayUntil = Math.Max(replayUntil, NextTick);
                if (target < Inputs.OldestTick || target < History.OldestTick || target <= ConfirmedThrough) return DeterministicStatus.HistoryExhausted;
                status = History.TryRestore(State, target); if (!status.IsSuccess()) return status;
                status = History.TryTruncate(target); if (!status.IsSuccess()) return status;
                status = Inputs.TryRewind(target); if (!status.IsSuccess()) return status;
                status = Events.TryRetractFrom(target); if (!status.IsSuccess()) { faulted = true; return status; }
                rollback = -1; RollbackCount++;
            }
            while (NextTick < replayUntil && replayed < maxReplayTicks)
            {
                status = ExecuteTick(); if (!status.IsSuccess()) return status;
                ReplayedTicks++; replayed++;
            }
            if (NextTick < replayUntil) return DeterministicStatus.Pending;
            replayUntil = -1;
            return ConfirmAvailable();
        }
        public DeterministicStatus TryAdvance(int maxTicks, out int executed)
        {
            executed = 0; if (maxTicks < 0) return DeterministicStatus.OutOfRange;
            var status = Ready(); if (!status.IsSuccess()) return status;
            status = TrySynchronizeRollback(); if (!status.IsSuccess()) return status;
            while (executed < maxTicks)
            {
                status = ExecuteTick(); if (!status.IsSuccess()) return status;
                executed++;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Total simulation budget includes replay and new ticks. Legacy TryAdvance keeps its former unbounded catch-up behavior.</summary>
        public DeterministicStatus TryAdvanceBounded(int maxWorkTicks, out int executed, out int replayed)
        {
            executed = replayed = 0;
            if (maxWorkTicks < 0) return DeterministicStatus.OutOfRange;
            var status = Ready(); if (!status.IsSuccess()) return status;
            status = TrySynchronizeRollback(maxWorkTicks, out replayed); if (!status.IsSuccess()) return status;
            while (executed < maxWorkTicks - replayed)
            {
                status = ExecuteTick(); if (!status.IsSuccess()) return status;
                executed++;
            }
            return DeterministicStatus.None;
        }
        public void Dispose()
        { if (disposed) return; disposed = true; Events.Dispose(); History.Dispose(); Inputs.Dispose(); }
    }
}
