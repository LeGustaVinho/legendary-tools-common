using LegendaryTools.FixedPoint;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>The same rotation transition can be hosted by any ECS or deterministic component storage.</summary>
    public sealed class QuaternionSimulation : IDeterministicSimulation<int, int>
    {
        private readonly DeterministicCell<FixedQuaternion> state;
        private QuaternionRotationStep prepared;
        public QuaternionSimulation(DeterministicCell<FixedQuaternion> state) { this.state = state; }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
        {
            int degrees = 0; for (int i = 0; i < inputs.Count; i++) degrees = unchecked(degrees + inputs[i].Value);
            if (!prepared.IsPrepared || prepared.Degrees != degrees) prepared = QuaternionSimulationTransition.Prepare(degrees, out _);
            if (!QuaternionSimulationTransition.TryRotate(state.Value, prepared, out var next, out _)) return DeterministicStatus.OutOfRange;
            var status = state.TrySet(next); if (!status.IsSuccess()) return status;
            return events.TryEmit(1, default, 0, next.GetHashCode());
        }
    }
#if !FIXEDPOINT_STANDALONE
    public sealed class NativeQuaternionSimulation : IDeterministicSimulation<int, int>, System.IDisposable
    {
        private NativeDeterministicCell<FixedQuaternion> state;
        private readonly NativeArray<int> result;
        private QuaternionRotationStep prepared;
        public NativeQuaternionSimulation(NativeDeterministicCell<FixedQuaternion> state)
        { this.state = state; result = new NativeArray<int>(3, Allocator.Persistent); }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
        {
            int degrees = 0; for (int i = 0; i < inputs.Count; i++) degrees = unchecked(degrees + inputs[i].Value);
            if (!prepared.IsPrepared || prepared.Degrees != degrees) prepared = QuaternionSimulationTransition.Prepare(degrees, out _);
            var output = result; output[2] = 0; new RotateJob { State = state, Step = prepared, Result = result }.Schedule().Complete();
            if (result[2] != 0) return DeterministicStatus.BackendUnavailable;
            var status = (DeterministicStatus)result[1]; return status.IsSuccess() ? events.TryEmit(1, default, 0, result[0]) : status;
        }
        public void Dispose() => result.Dispose();
        [BurstCompile(CompileSynchronously = true)]
        private struct RotateJob : IJob
        {
            internal NativeDeterministicCell<FixedQuaternion> State;
            internal NativeArray<int> Result;
            internal QuaternionRotationStep Step;
            public void Execute()
            {
                MarkManaged(); var status = State.TryGet(0, out var current);
                if (status.IsSuccess() && QuaternionSimulationTransition.TryRotate(current, Step, out var next, out _))
                { status = State.TrySet(0, next); Result[0] = next.GetHashCode(); }
                else if (status.IsSuccess()) status = DeterministicStatus.OutOfRange;
                Result[1] = (int)status;
            }
            [BurstDiscard] private void MarkManaged() => Result[2] = 1;
        }
    }
#endif
}
