using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    // No NUnit dependency: this same source runs in the actual Mono/IL2CPP validation players.
    public static unsafe class GraphDeterminismCorpus
    {
        public struct Result
        {
            public ulong Hash;
            public long BytesCompared;
            public int Operations, ReplayedOperations, AlgorithmComparisons, SuccessfulMutations, RejectedMutations;
        }
        private const int Stride = 4096, ScratchBytes = 65536;
        private static uint Next(ref uint random)
        { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random; }
        private static GraphNodeHandle Node(DeterministicGraph<int, int> graph, uint choice)
        {
            if (graph.NodeCount == 0) return default; int ordinal = (int)(choice % (uint)graph.NodeCount);
            var iterator = graph.Nodes; for (int i = 0; i <= ordinal; i++) iterator.MoveNext(); return iterator.Current.Handle;
        }
        private static GraphEdgeHandle Edge(DeterministicGraph<int, int> graph, uint choice)
        {
            if (graph.EdgeCount == 0) return default; int ordinal = (int)(choice % (uint)graph.EdgeCount);
            var iterator = graph.Edges; for (int i = 0; i <= ordinal; i++) iterator.MoveNext(); return iterator.Current.Handle;
        }
        private static GraphMutation<int, int> Mutation(DeterministicGraph<int, int> graph, int step, ref uint random)
        {
            uint choice = Next(ref random);
            var mutation = new GraphMutation<int, int> { Node = Node(graph, Next(ref random)), Target = Node(graph, Next(ref random)),
                Edge = Edge(graph, Next(ref random)), NodeData = unchecked((int)Next(ref random)), EdgeData = unchecked((int)Next(ref random)) };
            if (choice % 7 == 0) mutation.Node = new GraphNodeHandle(new StableHandle((uint)graph.Options.Nodes.MaxCapacity + 1, 1));
            if (choice % 11 == 0) mutation.Edge = new GraphEdgeHandle(new StableHandle((uint)graph.Options.Edges.MaxCapacity + 1, 1));
            mutation.Source = mutation.Node; int kind = (int)(choice % 16);
            mutation.Kind = step < 12 ? GraphMutationKind.AddNode : step % 173 == 0 ? GraphMutationKind.Clear :
                kind < 3 ? GraphMutationKind.AddNode : kind < 8 ? GraphMutationKind.AddEdge : kind < 10 ? GraphMutationKind.SetNode :
                kind == 10 ? GraphMutationKind.SetEdge : kind < 13 ? GraphMutationKind.RemoveEdge : GraphMutationKind.RemoveNode;
            return mutation;
        }
        private static ulong Fold(ulong digest, ulong value) => DeterministicHash.Mix(digest ^ value);
        private static ulong Order(ulong digest, GraphNodeHandle* output, int count)
        { digest = Fold(digest, (uint)count); for (int i = 0; i < count; i++) digest = Fold(digest, output[i].Value.Packed); return digest; }
        private static ulong Algorithms<TReader>(TReader reader, byte* memory, byte* output, out DeterministicStatus status)
            where TReader : struct, IDeterministicGraphReader<int, int>
        {
            status = reader.Check(); if (!status.IsSuccess()) return 0;
            ulong digest = Fold(0x4752415048535452UL, (uint)reader.NodeCount); GraphNodeHandle first = default, last = default;
            for (int i = 0; i < reader.NodeExtent; i++)
            {
                var s = reader.TryNodeAt(i, out var node, out _);
                if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) { status = s; return 0; }
                if (!first.IsValid) first = node; last = node;
            }
            var scratch = new CanonicalScratch(memory, ScratchBytes);
            status = GraphAlgorithms<int, int>.TryHasCycle(reader, ref scratch, out bool cycle); if (!status.IsSuccess()) return 0;
            digest = Fold(digest, cycle ? 1UL : 0);
            var nodes = new GraphWorkBuffer<GraphNodeHandle>((GraphNodeHandle*)output, reader.Options.Nodes.MaxCapacity);
            if (first.IsValid)
            {
                for (int direction = 0; direction < 2; direction++)
                {
                    scratch.Position = 0;
                    status = GraphAlgorithms<int, int>.TryBreadthFirst(reader, first, ref scratch, ref nodes, out int count, (GraphTraversal)direction);
                    if (!status.IsSuccess()) return 0; digest = Order(digest, (GraphNodeHandle*)output, count);
                    scratch.Position = 0;
                    status = GraphAlgorithms<int, int>.TryDepthFirst(reader, first, ref scratch, ref nodes, out count, (GraphTraversal)direction);
                    if (!status.IsSuccess()) return 0; digest = Order(digest, (GraphNodeHandle*)output, count);
                }
                scratch.Position = 0; status = GraphAlgorithms<int, int>.TryReachable(reader, first, last, ref scratch, out bool reachable);
                if (!status.IsSuccess()) return 0; digest = Fold(digest, reachable ? 1UL : 0);
            }
            scratch.Position = 0; var components = new GraphWorkBuffer<GraphComponent>((GraphComponent*)output, reader.Options.Nodes.MaxCapacity);
            status = GraphAlgorithms<int, int>.TryComponents(reader, ref scratch, ref components, out int nodeCount, out int componentCount);
            if (!status.IsSuccess()) return 0; digest = Fold(digest, (uint)componentCount);
            for (int i = 0; i < nodeCount; i++)
            { digest = Fold(digest, components.Read(i).Node.Value.Packed); digest = Fold(digest, (uint)components.Read(i).Component); }
            scratch.Position = 0; status = GraphAlgorithms<int, int>.TryTopologicalOrder(reader, ref scratch, ref nodes, out int topologicalCount);
            digest = Fold(digest, (byte)status);
            if (status.IsSuccess()) digest = Order(digest, (GraphNodeHandle*)output, topologicalCount);
            else if (status != DeterministicStatus.CycleDetected && status != DeterministicStatus.InvalidConfiguration) return 0;
            status = DeterministicStatus.None; return digest;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct TraceJob : IJob
        {
            public NativeDeterministicGraph<int, int> Graph;
            [ReadOnly] public NativeArray<GraphMutation<int, int>> Mutations;
            public NativeArray<byte> Bytes, Scratch, Output, Replay;
            public NativeArray<int> Statuses, Lengths, Control;
            public NativeArray<GraphMutationResult> Results;
            public NativeArray<ulong> Digests;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Control[0] = managed; Control[5] = -1;
                var status = Graph.TryCapture(out var baseline); Control[1] = (int)status; if (!status.IsSuccess()) return;
                var memory = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch);
                var output = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Output);
                var bytes = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Bytes);
                var replay = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Replay);
                for (int i = 0; i < Mutations.Length; i++)
                {
                    Statuses[i] = (int)Graph.TryApply(Mutations[i], out var result); Results[i] = result;
                    var writer = new DeterministicWriter(bytes + i * Stride, Stride);
                    status = Graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)); Lengths[i] = writer.Position;
                    if (!status.IsSuccess()) { Control[5] = i; Control[6] = (int)status; baseline.Dispose(); return; }
                    if (i % 32 == 0 || i == Mutations.Length - 1)
                    {
                        Digests[i] = Algorithms(Graph.AsReadOnly(), memory, output, out status);
                        if (!status.IsSuccess()) { Control[5] = i; Control[6] = (int)status; baseline.Dispose(); return; }
                    }
                }
                Control[2] = 1234; status = Graph.TryRestore(baseline); Control[3] = (int)status;
                if (!status.IsSuccess()) { baseline.Dispose(); return; }
                for (int i = 0; i < Mutations.Length; i++)
                {
                    status = Graph.TryApply(Mutations[i], out var result);
                    if ((int)status != Statuses[i] || result.Node != Results[i].Node || result.Edge != Results[i].Edge)
                    { Control[5] = i; baseline.Dispose(); return; }
                    var writer = new DeterministicWriter(replay, Stride);
                    status = Graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy));
                    if (!status.IsSuccess() || writer.Position != Lengths[i] || !Equal(replay, bytes + i * Stride, writer.Position))
                    { Control[5] = i; Control[6] = (int)status; baseline.Dispose(); return; }
                    if (i % 32 == 0 || i == Mutations.Length - 1)
                    {
                        ulong digest = Algorithms(Graph.AsReadOnly(), memory, output, out status);
                        if (!status.IsSuccess() || digest != Digests[i])
                        { Control[5] = i; Control[6] = (int)status; baseline.Dispose(); return; }
                    }
                }
                baseline.Dispose(); Control[4] = 1234; Control[6] = 0;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static bool Equal(byte* a, byte* b, int length)
        { for (int i = 0; i < length; i++) if (a[i] != b[i]) return false; return true; }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        public static Result Run(GraphOptions options, uint seed, int steps, bool reverseCompletion)
        {
            Require(BurstCompiler.IsEnabled, "Burst is disabled");
            Require(seed != 0 && steps >= 12 && options.Nodes.MaxCapacity <= 24 && options.Edges.MaxCapacity <= 48,
                "Corpus limits do not match the configured graph");
            using var managed = new DeterministicGraph<int, int>(options);
            var mutations = new GraphMutation<int, int>[steps]; var expectedStatus = new int[steps];
            var expectedResults = new GraphMutationResult[steps]; var expectedLengths = new int[steps];
            var expectedDigests = new ulong[steps]; var expectedBytes = new byte[steps * Stride];
            var scratchBytes = new byte[ScratchBytes]; var outputBytes = new byte[Stride]; uint random = seed;
            var report = new Result { Operations = steps, ReplayedOperations = steps * 2 };
            fixed (byte* bytes = expectedBytes) fixed (byte* scratch = scratchBytes) fixed (byte* output = outputBytes)
            {
                for (int i = 0; i < steps; i++)
                {
                    mutations[i] = Mutation(managed, i, ref random);
                    var status = managed.TryApply(mutations[i], out expectedResults[i]); expectedStatus[i] = (int)status;
                    if (status.IsSuccess()) report.SuccessfulMutations++; else report.RejectedMutations++;
                    var writer = new DeterministicWriter(bytes + i * Stride, Stride);
                    managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); expectedLengths[i] = writer.Position;
                    report.Hash = Fold(report.Hash, DeterministicHash.Xxh64(writer.Buffer, writer.Position));
                    report.Hash = Fold(report.Hash, (byte)status); report.Hash = Fold(report.Hash, expectedResults[i].Node.Value.Packed);
                    report.Hash = Fold(report.Hash, expectedResults[i].Edge.Value.Packed);
                    if (i % 32 == 0 || i == steps - 1)
                    {
                        expectedDigests[i] = Algorithms(managed.AsReadOnly(), scratch, output, out status); status.ThrowIfFailure();
                        report.Hash = Fold(report.Hash, expectedDigests[i]); report.AlgorithmComparisons += 4;
                    }
                    report.BytesCompared += (long)writer.Position * 4; // two peers, each including byte-for-byte rollback replay
                }
            }
            using var first = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var second = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var commands = new NativeArray<GraphMutation<int, int>>(mutations, Allocator.Persistent);
            using var bytesA = new NativeArray<byte>(expectedBytes.Length, Allocator.Persistent); using var bytesB = new NativeArray<byte>(expectedBytes.Length, Allocator.Persistent);
            using var scratchA = new NativeArray<byte>(ScratchBytes, Allocator.Persistent); using var scratchB = new NativeArray<byte>(ScratchBytes, Allocator.Persistent);
            using var outputA = new NativeArray<byte>(Stride, Allocator.Persistent); using var outputB = new NativeArray<byte>(Stride, Allocator.Persistent);
            using var replayA = new NativeArray<byte>(Stride, Allocator.Persistent); using var replayB = new NativeArray<byte>(Stride, Allocator.Persistent);
            using var statusesA = new NativeArray<int>(steps, Allocator.Persistent); using var statusesB = new NativeArray<int>(steps, Allocator.Persistent);
            using var lengthsA = new NativeArray<int>(steps, Allocator.Persistent); using var lengthsB = new NativeArray<int>(steps, Allocator.Persistent);
            using var resultsA = new NativeArray<GraphMutationResult>(steps, Allocator.Persistent); using var resultsB = new NativeArray<GraphMutationResult>(steps, Allocator.Persistent);
            using var digestsA = new NativeArray<ulong>(steps, Allocator.Persistent); using var digestsB = new NativeArray<ulong>(steps, Allocator.Persistent);
            using var controlA = new NativeArray<int>(7, Allocator.Persistent); using var controlB = new NativeArray<int>(7, Allocator.Persistent);
            var ca = controlA; var cb = controlB; for (int i = 0; i < 7; i++) { ca[i] = -999; cb[i] = -999; }
            var jobA = new TraceJob { Graph = first, Mutations = commands, Bytes = bytesA, Scratch = scratchA, Output = outputA, Replay = replayA,
                Statuses = statusesA, Lengths = lengthsA, Results = resultsA, Digests = digestsA, Control = controlA };
            var jobB = new TraceJob { Graph = second, Mutations = commands, Bytes = bytesB, Scratch = scratchB, Output = outputB, Replay = replayB,
                Statuses = statusesB, Lengths = lengthsB, Results = resultsB, Digests = digestsB, Control = controlB };
            JobHandle a, b;
            if (reverseCompletion) { b = jobB.Schedule(); a = jobA.Schedule(); }
            else { a = jobA.Schedule(); b = jobB.Schedule(); }
            // Complete both even when the first completion throws, before disposing any owner.
            try { if (reverseCompletion) a.Complete(); else b.Complete(); }
            finally { if (reverseCompletion) b.Complete(); else a.Complete(); }
            foreach (var control in new[] { controlA, controlB })
                Require(control[0] == 0 && control[1] == 0 && control[2] == 1234 && control[3] == 0 && control[4] == 1234 && control[5] == -1 && control[6] == 0,
                    $"Burst trace did not finish: seed {seed:x8}, marker {control[0]}, failure step {control[5]}, status {control[6]}");
            var pointerA = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytesA);
            var pointerB = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytesB);
            fixed (byte* expected = expectedBytes)
            for (int i = 0; i < steps; i++)
            {
                bool match = statusesA[i] == expectedStatus[i] && statusesB[i] == expectedStatus[i] &&
                    resultsA[i].Node == expectedResults[i].Node && resultsB[i].Node == expectedResults[i].Node &&
                    resultsA[i].Edge == expectedResults[i].Edge && resultsB[i].Edge == expectedResults[i].Edge &&
                    lengthsA[i] == expectedLengths[i] && lengthsB[i] == expectedLengths[i] &&
                    digestsA[i] == expectedDigests[i] && digestsB[i] == expectedDigests[i] &&
                    Equal(expected + i * Stride, pointerA + i * Stride, expectedLengths[i]) &&
                    Equal(expected + i * Stride, pointerB + i * Stride, expectedLengths[i]);
                Require(match, $"Managed/Burst divergence: seed {seed:x8}, step {i}, operation {mutations[i].Kind}");
            }
            Require(report.SuccessfulMutations > 12 && report.RejectedMutations > 0, "Corpus did not exercise both successful and rejected mutations");
            return report;
        }
    }
}
