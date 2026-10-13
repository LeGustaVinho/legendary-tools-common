using System;
using LegendaryTools.FixedPoint;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Samples
{
    public static class BoundsSimulationTransition
    {
        public static DeterministicStatus TryApply(ref FixedBounds3 bounds, int input)
        {
            if (!bounds.TryGetCenter(out var center, out var status)) return DeterministicStatus.OutOfRange;
            var delta = Fixed64.FromInt(input, out var conversion); status |= conversion;
            center.x = Fixed64.Add(center.x, delta, out var addition); status |= addition;
            if (!status.IsSuccess() || !bounds.TrySetCenter(center, out _)) return DeterministicStatus.OutOfRange;
            return DeterministicStatus.None;
        }
    }
    /// <summary>Caller-owned registered cell. Inputs move the AABB in PlayerId order; writes go through collection APIs.</summary>
    public sealed class BoundsSimulation : IDeterministicSimulation<int, int>
    {
        private readonly DeterministicCell<FixedBounds3> state;
        public BoundsSimulation(DeterministicCell<FixedBounds3> state) { this.state = state ?? throw new ArgumentNullException(nameof(state)); }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
        {
            var next = state.Value;
            for (int i = 0; i < frame.Count; i++)
            {
                var status = BoundsSimulationTransition.TryApply(ref next, unchecked(frame[i].Value * (int)frame[i].Player.Value));
                if (!status.IsSuccess()) return status;
            }
            var written = state.TrySet(next); if (!written.IsSuccess()) return written;
            return events.TryEmit(1, default, 0, unchecked((int)next.center.x.RawValue));
        }
    }
#if !FIXEDPOINT_STANDALONE
    /// <summary>Same transition in a Burst job. Dependencies complete before the host publishes checkpoints/events.</summary>
    public sealed class NativeBoundsSimulation : IDeterministicSimulation<int, int>, IDisposable
    {
        private NativeDeterministicCell<FixedBounds3> state;
        private NativeArray<DeterministicInputSample<int>> inputs;
        private NativeArray<int> result;
        public bool LastStepUsedBurst => result.IsCreated && result[0] == 0;
        public NativeBoundsSimulation(NativeDeterministicCell<FixedBounds3> state, int maxPlayers)
        {
            if (maxPlayers <= 0 || maxPlayers > 64) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            this.state = state; inputs = new NativeArray<DeterministicInputSample<int>>(maxPlayers, Allocator.Persistent);
            try { result = new NativeArray<int>(2, Allocator.Persistent); } catch { inputs.Dispose(); throw; }
        }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
        {
            if (!inputs.IsCreated) return DeterministicStatus.Disposed;
            if (frame.Count > inputs.Length) return DeterministicStatus.CapacityExceeded;
            for (int i = 0; i < frame.Count; i++) inputs[i] = frame[i];
            new StepJob { State = state, Inputs = inputs, Result = result, Count = frame.Count }.Schedule().Complete();
            var status = (DeterministicStatus)result[1]; if (!status.IsSuccess()) return status;
            return events.TryEmit(1, default, 0, unchecked((int)state.Value.center.x.RawValue));
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct StepJob : IJob
        {
            internal NativeDeterministicCell<FixedBounds3> State;
            [ReadOnly] internal NativeArray<DeterministicInputSample<int>> Inputs;
            internal NativeArray<int> Result;
            internal int Count;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var status = State.TryGet(0, out var next);
                for (int i = 0; status.IsSuccess() && i < Count; i++)
                    status = BoundsSimulationTransition.TryApply(ref next, unchecked(Inputs[i].Value * (int)Inputs[i].Player.Value));
                Result[1] = (int)(status.IsSuccess() ? State.TrySet(0, next) : status);
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        public void Dispose() { if (inputs.IsCreated) inputs.Dispose(); if (result.IsCreated) result.Dispose(); }
    }
#endif
}
