using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>Minimal ordered integer simulation. The caller owns the state collection.</summary>
    public sealed class CounterSimulation : IDeterministicSimulation<int, int>
    {
        private readonly DeterministicCell<int> value;
        public CounterSimulation(DeterministicCell<int> value) { this.value = value ?? throw new ArgumentNullException(nameof(value)); }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
        {
            int next = value.Value;
            for (int i = 0; i < inputs.Count; i++) next = unchecked(next * 31 + inputs[i].Value * (int)inputs[i].Player.Value);
            value.Value = next; return events.TryEmit(1, default, 0, next);
        }
    }
    /// <summary>Same transition in a Burst job. The host completes dependencies before snapshots/events are published.</summary>
    public sealed class NativeCounterSimulation : IDeterministicSimulation<int, int>, IDisposable
    {
        private NativeDeterministicCell<int> value;
        private NativeArray<DeterministicInputSample<int>> inputs;
        private NativeArray<int> result;
        public bool LastStepUsedBurst => result.IsCreated && result[0] == 0;
        public NativeCounterSimulation(NativeDeterministicCell<int> value, int maxPlayers)
        {
            if (maxPlayers <= 0 || maxPlayers > 64) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            this.value = value; inputs = new NativeArray<DeterministicInputSample<int>>(maxPlayers, Allocator.Persistent);
            try { result = new NativeArray<int>(2, Allocator.Persistent); } catch { inputs.Dispose(); throw; }
        }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
        {
            if (!inputs.IsCreated) return DeterministicStatus.Disposed;
            if (frame.Count > inputs.Length) return DeterministicStatus.CapacityExceeded;
            for (int i = 0; i < frame.Count; i++) inputs[i] = frame[i];
            new StepJob { State = value, Inputs = inputs, Result = result, Count = frame.Count }.Schedule().Complete();
            var status = (DeterministicStatus)result[1]; if (!status.IsSuccess()) return status;
            return events.TryEmit(1, default, 0, value.Value);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct StepJob : IJob
        {
            internal NativeDeterministicCell<int> State;
            [ReadOnly] internal NativeArray<DeterministicInputSample<int>> Inputs;
            internal NativeArray<int> Result;
            internal int Count;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                int next = State.Value;
                for (int i = 0; i < Count; i++) next = unchecked(next * 31 + Inputs[i].Value * (int)Inputs[i].Player.Value);
                Result[1] = (int)State.TrySet(0, next);
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        public void Dispose() { if (inputs.IsCreated) inputs.Dispose(); if (result.IsCreated) result.Dispose(); }
    }
}
