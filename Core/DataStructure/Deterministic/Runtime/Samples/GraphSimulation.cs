using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Samples
{
    public sealed class GraphSimulation : IDeterministicSimulation<int, int>
    {
        private DeterministicGraph<int, int> graph;
        private readonly GraphNodeHandle a, b;
        public GraphSimulation(DeterministicGraph<int, int> graph, GraphNodeHandle a, GraphNodeHandle b)
        { this.graph = graph ?? throw new ArgumentNullException(nameof(graph)); this.a = a; this.b = b; }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
        {
            var s = graph.TryGetNode(a, out int value); if (!s.IsSuccess()) return s;
            for (int i = 0; i < frame.Count; i++) value = GraphSimulationTransition<DeterministicGraph<int, int>>.Fold(value, frame[i].Value, frame[i].Player);
            s = GraphSimulationTransition<DeterministicGraph<int, int>>.TryApply(ref graph, a, b, value);
            return s.IsSuccess() ? events.TryEmit(1, a.Value, 0, value) : s;
        }
    }
    public sealed class NativeGraphSimulation : IDeterministicSimulation<int, int>, IDisposable
    {
        private NativeDeterministicGraph<int, int> graph;
        private readonly GraphNodeHandle a, b;
        private NativeArray<DeterministicInputSample<int>> inputs;
        private NativeArray<int> results;
        public bool LastStepUsedBurst => results.IsCreated && results[0] == 0;
        public NativeGraphSimulation(NativeDeterministicGraph<int, int> graph, GraphNodeHandle a, GraphNodeHandle b, int maxPlayers)
        {
            if (maxPlayers <= 0 || maxPlayers > 64) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            this.graph = graph; this.a = a; this.b = b; inputs = new NativeArray<DeterministicInputSample<int>>(maxPlayers, Allocator.Persistent);
            try { results = new NativeArray<int>(3, Allocator.Persistent); } catch { inputs.Dispose(); throw; }
        }
        public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
        {
            if (!inputs.IsCreated) return DeterministicStatus.Disposed; if (frame.Count > inputs.Length) return DeterministicStatus.CapacityExceeded;
            for (int i = 0; i < frame.Count; i++) inputs[i] = frame[i];
            new StepJob { Graph = graph, A = a, B = b, Inputs = inputs, Count = frame.Count, Results = results }.Schedule().Complete();
            var s = (DeterministicStatus)results[1]; return s.IsSuccess() ? events.TryEmit(1, a.Value, 0, results[2]) : s;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct StepJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal GraphNodeHandle A, B;
            [ReadOnly] internal NativeArray<DeterministicInputSample<int>> Inputs;
            internal NativeArray<int> Results;
            internal int Count;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                var s = Graph.TryGetNode(A, out int value); if (!s.IsSuccess()) { Results[1] = (int)s; return; }
                for (int i = 0; i < Count; i++) value = GraphSimulationTransition<NativeDeterministicGraph<int, int>>.Fold(value, Inputs[i].Value, Inputs[i].Player);
                Results[1] = (int)GraphSimulationTransition<NativeDeterministicGraph<int, int>>.TryApply(ref Graph, A, B, value); Results[2] = value;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        public void Dispose() { if (inputs.IsCreated) inputs.Dispose(); if (results.IsCreated) results.Dispose(); }
    }
}
