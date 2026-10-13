using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeGraphCanonicalDeltaTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct TransferJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Source, Target, Replica;
            internal NativeArray<byte> SourceBytes, TargetBytes, DeltaBytes, Scratch;
            internal NativeArray<GraphNodeHandle> Nodes;
            internal NativeArray<ulong> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = (ulong)managed;
                for (int i = 0; i < 24; i++) { Nodes[i] = Source.AddNode(i); if (i != 0) Source.AddEdge(Nodes[i - 1], Nodes[i], i); }
                var sw = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(SourceBytes), SourceBytes.Length);
                Results[1] = (ulong)Source.TryWriteCanonical(ref sw, default(Int32Policy), default(Int32Policy)); Results[2] = (ulong)sw.Position;
                var workspace = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch); var scratch = new CanonicalScratch(workspace, Scratch.Length);
                var r = new DeterministicReader(sw.Buffer, sw.Position); Results[3] = (ulong)Target.TryReadCanonical(ref r, ref scratch, default(Int32Policy), default(Int32Policy));
                scratch.Position = 0; r.Position = 0; Results[4] = (ulong)Replica.TryReadCanonical(ref r, ref scratch, default(Int32Policy), default(Int32Policy));
                Target.TrySetNode(Nodes[0], 1000); Target.TryRemoveNode(Nodes[4]); var replacement = Target.AddNode(99); Target.AddEdge(replacement, Nodes[0], 42);
                var dw = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(DeltaBytes), DeltaBytes.Length); scratch.Position = 0;
                Results[5] = (ulong)Target.TryWriteDelta(new GraphCheckpoint(sw.Buffer, sw.Position), ref dw, ref scratch, default(Int32Policy), default(Int32Policy), out var kind, GraphDeltaMode.Reversible);
                Results[6] = (ulong)dw.Position; Results[7] = (ulong)kind;
                r = new DeterministicReader(dw.Buffer, dw.Position); scratch.Position = 0;
                Results[8] = (ulong)Replica.TryApplyDelta(ref r, ref scratch, default(Int32Policy), default(Int32Policy));
                var tw = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(TargetBytes), TargetBytes.Length);
                Results[9] = (ulong)Replica.TryWriteCanonical(ref tw, default(Int32Policy), default(Int32Policy)); Results[10] = (ulong)tw.Position;
                r = new DeterministicReader(dw.Buffer, dw.Position); scratch.Position = 0;
                Results[11] = (ulong)Replica.TryApplyDelta(ref r, ref scratch, default(Int32Policy), default(Int32Policy), true);
                tw.Position = 0; Replica.TryWriteCanonical(ref tw, default(Int32Policy), default(Int32Policy));
                Results[12] = DeterministicHash.Xxh64(sw.Buffer, sw.Position); Results[13] = DeterministicHash.Xxh64(tw.Buffer, tw.Position);
                Results[14] = Source.AddNode(88).Value.Packed; Results[15] = Replica.AddNode(88).Value.Packed; Results[16] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void NativeCheckpointAndReversibleDeltaMatchManagedBytesInsideBurst()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled); var options = new GraphOptions(32, 64);
            using var source = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var target = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var replica = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var sourceBytes = new NativeArray<byte>(8192, Allocator.TempJob); using var targetBytes = new NativeArray<byte>(8192, Allocator.TempJob);
            using var delta = new NativeArray<byte>(8192, Allocator.TempJob); using var scratch = new NativeArray<byte>(131072, Allocator.TempJob);
            using var nodes = new NativeArray<GraphNodeHandle>(24, Allocator.TempJob); using var results = new NativeArray<ulong>(17, Allocator.TempJob);
            var resultAccess = results; for (int i = 0; i < resultAccess.Length; i++) resultAccess[i] = 999;
            new TransferJob { Source = source, Target = target, Replica = replica, SourceBytes = sourceBytes, TargetBytes = targetBytes, DeltaBytes = delta, Scratch = scratch, Nodes = nodes, Results = results }.Schedule().Complete();
            foreach (int i in new[] { 0, 1, 3, 4, 5, 8, 9, 11 }) Assert.AreEqual(0, results[i], "Status index " + i);
            Assert.AreEqual(1234, results[16]); Assert.AreEqual(results[12], results[13]); Assert.AreEqual(results[14], results[15]);
            using var managed = new DeterministicGraph<int, int>(options); var handles = new GraphNodeHandle[24];
            for (int i = 0; i < handles.Length; i++) { handles[i] = managed.AddNode(i); if (i != 0) managed.AddEdge(handles[i - 1], handles[i], i); }
            var bytes = new byte[8192]; fixed (byte* p = bytes)
            { var w = new DeterministicWriter(p, bytes.Length); managed.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); Assert.AreEqual((int)results[2], w.Position); for (int i = 0; i < w.Position; i++) Assert.AreEqual(bytes[i], sourceBytes[i]); }
            managed.TrySetNode(handles[0], 1000).ThrowIfFailure(); managed.TryRemoveNode(handles[4]).ThrowIfFailure(); var replacement = managed.AddNode(99); managed.AddEdge(replacement, handles[0], 42);
            var output = new byte[8192]; var workspace = new byte[131072];
            fixed (byte* p = bytes) fixed (byte* o = output) fixed (byte* w = workspace)
            { var writer = new DeterministicWriter(o, output.Length); var staging = new CanonicalScratch(w, workspace.Length); managed.TryWriteDelta(new GraphCheckpoint(p, (int)results[2]), ref writer, ref staging, default(Int32Policy), default(Int32Policy), out var kind, GraphDeltaMode.Reversible).ThrowIfFailure(); Assert.AreEqual((int)results[6], writer.Position); Assert.AreEqual((ulong)kind, results[7]); for (int i = 0; i < writer.Position; i++) Assert.AreEqual(output[i], delta[i]); }
        }
    }
}
