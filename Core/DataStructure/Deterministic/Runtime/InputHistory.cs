using System;

namespace LegendaryTools.Deterministic
{
    public readonly struct DeterministicInputSample<T> where T : unmanaged
    {
        public readonly PlayerId Player;
        public readonly T Value;
        public readonly bool Predicted;
        public DeterministicInputSample(PlayerId player, T value, bool predicted) { Player = player; Value = value; Predicted = predicted; }
    }
    /// <summary>Read-only borrowed input frame, valid until the next resolve/roster change or owner disposal.</summary>
    public readonly unsafe struct DeterministicInputFrame<T> where T : unmanaged
    {
        private readonly DeterministicInputSample<T>* samples;
        public readonly long Tick;
        public readonly int Count;
        internal DeterministicInputFrame(DeterministicInputSample<T>* samples, long tick, int count)
        { this.samples = samples; Tick = tick; Count = count; }
        public DeterministicStatus TryGet(int index, out DeterministicInputSample<T> value)
        { value = default; if ((uint)index >= (uint)Count || samples == null) return DeterministicStatus.OutOfRange; value = samples[index]; return DeterministicStatus.None; }
        public DeterministicInputSample<T> this[int index] { get { TryGet(index, out var value).ThrowIfFailure(); return value; } }
    }

    /// <summary>Immutable actual inputs, prediction history and bounded past/future windows. Admission changes require ordered session commands.</summary>
    public sealed unsafe class DeterministicInputHistory<T, TEquality> : IDisposable
        where T : unmanaged where TEquality : unmanaged, IDeterministicEquality<T>
    {
        private struct Player
        {
            internal PlayerId Id;
            internal long Activation, Deactivation, CarryTick;
            internal T Carry;
        }
        private struct Cell { internal T Actual, Used; internal byte HasActual, WasUsed, WasPredicted; }
        private readonly IDeterministicMemory memory;
        private readonly int maxPlayers, historyTicks, rows;
        private readonly T neutral;
        private readonly TEquality equality;
        private readonly PredictionPolicy prediction;
        private Player* players;
        private long* rowTicks;
        private ulong* rowActive;
        private Cell* cells;
        private DeterministicInputSample<T>* samples;
        private bool disposed;
        private long floor;
        public DeterministicBackend Backend { get; }
        public long NextTick { get; private set; }
        public long OldestTick => floor;
        public long CompleteThrough { get; private set; }
        public int PlayerCount { get; private set; }
        public int HistoryTicks => historyTicks;
        public DeterministicInputHistory(DeterministicRuntimeContext context, int maxPlayers, int historyTicks,
            PredictionPolicy prediction, T neutral, TEquality equality = default, long initialTick = 0)
        {
            if (context == null || maxPlayers <= 0 || maxPlayers > 64 || historyTicks < 2 || initialTick < 0 ||
                initialTick == long.MaxValue || prediction > PredictionPolicy.Neutral || !equality.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            Backend = context.Backend; this.maxPlayers = maxPlayers; this.historyTicks = historyTicks;
            this.prediction = prediction; this.neutral = neutral; this.equality = equality;
            NextTick = floor = initialTick; CompleteThrough = initialTick - 1; rows = checked(historyTicks * 2);
            long p = Align((long)sizeof(Player) * maxPlayers), r = Align((long)sizeof(long) * rows);
            long c = Align((long)sizeof(Cell) * maxPlayers * rows), s = Align((long)sizeof(DeterministicInputSample<T>) * maxPlayers);
            if (p + r * 2 + c + s > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)(p + r * 2 + c + s)); players = (Player*)memory.Pointer;
            rowTicks = (long*)(memory.Pointer + p); rowActive = (ulong*)(memory.Pointer + p + r);
            cells = (Cell*)(memory.Pointer + p + r * 2);
            samples = (DeterministicInputSample<T>*)(memory.Pointer + p + r * 2 + c);
            for (int i = 0; i < rows; i++) rowTicks[i] = -1;
        }
        private static long Align(long value) => (value + 15) & ~15L;
        private int Index(PlayerId player)
        { for (int i = 0; i < PlayerCount; i++) if (players[i].Id == player) return i; return -1; }
        private bool Active(int player, long tick)
        {
            if (tick >= players[player].Activation) return tick < players[player].Deactivation;
            int row = Row(tick, false); return row >= 0 && (rowActive[row] & (1UL << player)) != 0;
        }
        private ulong CurrentMask(long tick)
        {
            ulong mask = 0; for (int i = 0; i < PlayerCount; i++)
                if (tick >= players[i].Activation && tick < players[i].Deactivation) mask |= 1UL << i;
            return mask;
        }
        private void RefreshFutureMasks(long boundary)
        { for (int row = 0; row < rows; row++) if (rowTicks[row] >= boundary) rowActive[row] = CurrentMask(rowTicks[row]); }
        public DeterministicStatus TryGetPlayer(int index, out PlayerId player)
        {
            player = default; if (disposed) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)PlayerCount) return DeterministicStatus.OutOfRange;
            player = players[index].Id; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRegisterPlayer(PlayerId player, long activationTick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!player.IsValid || activationTick < NextTick || activationTick == long.MaxValue) return DeterministicStatus.InvalidConfiguration;
            if (Index(player) >= 0) return DeterministicStatus.DuplicateKey;
            if (PlayerCount == maxPlayers) return DeterministicStatus.CapacityExceeded;
            int index = 0; while (index < PlayerCount && players[index].Id.CompareTo(player) < 0) index++;
            for (int i = PlayerCount; i > index; i--) players[i] = players[i - 1];
            for (int row = 0; row < rows; row++)
            {
                for (int i = PlayerCount; i > index; i--) cells[row * maxPlayers + i] = cells[row * maxPlayers + i - 1];
                cells[row * maxPlayers + index] = default;
                ulong low = index == 0 ? 0 : (1UL << index) - 1;
                rowActive[row] = (rowActive[row] & low) | ((rowActive[row] & ~low) << 1);
            }
            players[index] = new Player { Id = player, Activation = activationTick, Deactivation = long.MaxValue, CarryTick = -1, Carry = neutral };
            PlayerCount++; RefreshFutureMasks(activationTick); CompleteThrough = Math.Min(CompleteThrough, activationTick - 1); AdvanceComplete(); return DeterministicStatus.None;
        }
        public DeterministicStatus TryDeactivatePlayer(PlayerId player, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            int index = Index(player); if (index < 0) return DeterministicStatus.NotFound;
            if (tick < NextTick || tick < players[index].Activation) return DeterministicStatus.OutOfRange;
            if (players[index].Deactivation != long.MaxValue && players[index].Deactivation != tick) return DeterministicStatus.ProtocolConflict;
            players[index].Deactivation = tick; RefreshFutureMasks(tick); AdvanceComplete(); return DeterministicStatus.None;
        }
        public DeterministicStatus CanApplyActiveRoster(ReadOnlySpan<PlayerId> active, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < floor || tick > NextTick || active.Length > maxPlayers) return DeterministicStatus.OutOfRange;
            int additions = 0;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i].IsValid || (i > 0 && active[i - 1].CompareTo(active[i]) >= 0)) return DeterministicStatus.InvalidConfiguration;
                if (Index(active[i]) < 0) additions++;
            }
            return PlayerCount + additions > maxPlayers ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
        }
        /// <summary>Apply an ordered active roster at this boundary. Historic row masks preserve previous participation intervals.</summary>
        public DeterministicStatus TryApplyActiveRoster(ReadOnlySpan<PlayerId> active, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick != NextTick) return DeterministicStatus.OutOfRange;
            var valid = CanApplyActiveRoster(active, tick); if (!valid.IsSuccess()) return valid;
            for (int i = 0; i < PlayerCount; i++)
            {
                bool found = false; for (int j = 0; j < active.Length; j++) if (players[i].Id == active[j]) { found = true; break; }
                if (!found && (Active(i, tick) || players[i].Activation > tick)) players[i].Deactivation = tick;
            }
            for (int i = 0; i < active.Length; i++)
            {
                int index = Index(active[i]);
                if (index < 0) TryRegisterPlayer(active[i], tick);
                else if (!Active(index, tick) || players[index].Activation > tick)
                { players[index].Activation = tick; players[index].Deactivation = long.MaxValue; }
            }
            RefreshFutureMasks(tick); CompleteThrough = Math.Min(CompleteThrough, tick - 1); AdvanceComplete(); return DeterministicStatus.None;
        }
        public DeterministicStatus TrySeedLastInput(PlayerId player, long tick, in T value)
        {
            if (disposed) return DeterministicStatus.Disposed;
            int index = Index(player); if (index < 0) return DeterministicStatus.NotFound;
            if (tick < -1 || tick >= floor) return DeterministicStatus.OutOfRange;
            players[index].CarryTick = tick; players[index].Carry = value; return DeterministicStatus.None;
        }
        private int Row(long tick, bool create)
        {
            if (tick < 0) return -1;
            int row = (int)(tick % rows); if (rowTicks[row] == tick) return row;
            if (!create) return -1;
            for (int i = 0; i < maxPlayers; i++) cells[row * maxPlayers + i] = default;
            rowTicks[row] = tick; rowActive[row] = CurrentMask(tick); return row;
        }
        private bool InWindow(long tick) => tick >= floor && tick >= 0 && tick - NextTick < historyTicks;
        public DeterministicStatus TrySubmit(PlayerId player, long tick, in T value, out long rollbackTick)
        {
            rollbackTick = -1; if (disposed) return DeterministicStatus.Disposed;
            var valid = CanSubmit(player, tick, value); if (!valid.IsSuccess()) return valid;
            int index = Index(player);
            int row = Row(tick, true); ref Cell cell = ref cells[row * maxPlayers + index];
            if (cell.HasActual != 0) return equality.Equals(cell.Actual, value) ? DeterministicStatus.None : DeterministicStatus.ProtocolConflict;
            if (cell.WasUsed != 0 && cell.WasPredicted != 0 && !equality.Equals(cell.Used, value)) rollbackTick = tick;
            cell.Actual = value; cell.HasActual = 1; AdvanceComplete(); return DeterministicStatus.None;
        }
        public DeterministicStatus CanSubmit(PlayerId player, long tick, in T value)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick == long.MaxValue || !InWindow(tick)) return DeterministicStatus.HistoryExhausted;
            int index = Index(player); if (index < 0 || !Active(index, tick)) return DeterministicStatus.NotFound;
            int row = Row(tick, false); if (row < 0) return DeterministicStatus.None;
            ref Cell cell = ref cells[row * maxPlayers + index];
            return cell.HasActual == 0 || equality.Equals(cell.Actual, value) ? DeterministicStatus.None : DeterministicStatus.ProtocolConflict;
        }
        public DeterministicStatus TryGetActualFrame(long tick, out DeterministicInputFrame<T> frame)
        {
            frame = default; if (disposed) return DeterministicStatus.Disposed;
            if (!InWindow(tick)) return DeterministicStatus.HistoryExhausted;
            if (!Complete(tick)) return DeterministicStatus.Pending;
            int row = Row(tick, false), count = 0;
            for (int i = 0; i < PlayerCount; i++) if (Active(i, tick))
                samples[count++] = new DeterministicInputSample<T>(players[i].Id, cells[row * maxPlayers + i].Actual, false);
            frame = new DeterministicInputFrame<T>(samples, tick, count); return DeterministicStatus.None;
        }
        public DeterministicStatus TryGetLastActual(PlayerId player, long boundary, out T value, out long tick)
        {
            value = neutral; tick = -1; if (disposed) return DeterministicStatus.Disposed;
            if (boundary < floor || boundary < 0 || boundary - NextTick > historyTicks) return DeterministicStatus.HistoryExhausted;
            int index = Index(player); if (index < 0) return DeterministicStatus.NotFound;
            for (long at = boundary - 1; at >= floor; at--)
            {
                int row = Row(at, false);
                if (row >= 0 && cells[row * maxPlayers + index].HasActual != 0) { value = cells[row * maxPlayers + index].Actual; tick = at; return DeterministicStatus.None; }
            }
            if (players[index].CarryTick < boundary) { value = players[index].Carry; tick = players[index].CarryTick; } return DeterministicStatus.None;
        }
        /// <summary>Only a committed change of input ownership may discard uncommitted future actual inputs.</summary>
        public DeterministicStatus TryForgetFutureInputs(PlayerId player, long boundary)
        {
            if (disposed) return DeterministicStatus.Disposed;
            int index = Index(player); if (index < 0) return DeterministicStatus.NotFound;
            if (boundary < NextTick) return DeterministicStatus.OutOfRange;
            for (int row = 0; row < rows; row++) if (rowTicks[row] >= boundary) cells[row * maxPlayers + index] = default;
            CompleteThrough = Math.Min(CompleteThrough, boundary - 1); AdvanceComplete(); return DeterministicStatus.None;
        }
        private bool Complete(long tick)
        {
            int row = Row(tick, false);
            for (int i = 0; i < PlayerCount; i++) if (Active(i, tick) && (row < 0 || cells[row * maxPlayers + i].HasActual == 0)) return false;
            return true;
        }
        private void AdvanceComplete()
        {
            if (PlayerCount == 0) return;
            long limit = NextTick > long.MaxValue - historyTicks ? long.MaxValue : NextTick + historyTicks;
            while (CompleteThrough < limit - 1 && Complete(CompleteThrough + 1)) CompleteThrough++;
        }
        private T Predict(int player, long tick)
        {
            if (prediction == PredictionPolicy.Neutral) return neutral;
            for (long previous = tick - 1; previous >= floor; previous--)
            {
                int row = Row(previous, false);
                if (row >= 0 && cells[row * maxPlayers + player].HasActual != 0) return cells[row * maxPlayers + player].Actual;
            }
            return players[player].CarryTick < tick ? players[player].Carry : neutral;
        }
        public DeterministicStatus TryResolve(long tick, bool allowPrediction, out DeterministicInputFrame<T> frame)
        {
            frame = default; if (disposed) return DeterministicStatus.Disposed;
            if (tick != NextTick || tick == long.MaxValue || PlayerCount == 0) return DeterministicStatus.OutOfRange;
            int existing = Row(tick, false);
            if (!allowPrediction && !Complete(tick)) return DeterministicStatus.Pending;
            int row = existing < 0 ? Row(tick, true) : existing, count = 0;
            for (int i = 0; i < PlayerCount; i++) if (Active(i, tick))
            {
                ref Cell cell = ref cells[row * maxPlayers + i]; bool predicted = cell.HasActual == 0;
                T value = predicted ? Predict(i, tick) : cell.Actual;
                samples[count++] = new DeterministicInputSample<T>(players[i].Id, value, predicted);
                cell.Used = value; cell.WasUsed = 1; cell.WasPredicted = (byte)(predicted ? 1 : 0);
            }
            frame = new DeterministicInputFrame<T>(samples, tick, count); return DeterministicStatus.None;
        }
        public DeterministicStatus TryAdvance(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick != NextTick || tick == long.MaxValue) return DeterministicStatus.OutOfRange;
            int row = Row(tick, false); if (row < 0) return DeterministicStatus.ProtocolConflict;
            for (int i = 0; i < PlayerCount; i++) if (Active(i, tick) && cells[row * maxPlayers + i].WasUsed == 0) return DeterministicStatus.ProtocolConflict;
            NextTick++;
            long newFloor = Math.Max(floor, NextTick - historyTicks);
            while (floor < newFloor)
            {
                int evicted = Row(floor, false);
                if (evicted >= 0) for (int i = 0; i < PlayerCount; i++)
                    if (cells[evicted * maxPlayers + i].HasActual != 0 && players[i].CarryTick < floor)
                    { players[i].Carry = cells[evicted * maxPlayers + i].Actual; players[i].CarryTick = floor; }
                floor++;
            }
            AdvanceComplete(); return DeterministicStatus.None;
        }
        public DeterministicStatus TryRewind(long boundary)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (boundary < floor || boundary > NextTick) return DeterministicStatus.HistoryExhausted;
            for (int row = 0; row < rows; row++) if (rowTicks[row] >= boundary && rowTicks[row] <= NextTick)
                for (int i = 0; i < PlayerCount; i++) { cells[row * maxPlayers + i].WasUsed = 0; cells[row * maxPlayers + i].WasPredicted = 0; }
            NextTick = boundary; return DeterministicStatus.None;
        }
        public void Dispose()
        { if (disposed) return; disposed = true; memory.Dispose(); players = null; rowTicks = null; rowActive = null; cells = null; samples = null; }
    }
}
