using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class GraphExtendedHotpathBenchmarks
    {
        public enum WireOperation { WriteCanonical, ValidateCanonical, ReadCanonical, DeltaFacade, DeltaCheckpoints, ApplyDelta, ReverseDelta }
        public enum WireScenario { Chain, AcyclicChain, FragmentedChain }
        private interface IWireGraph
        {
            GraphOptions Options { get; }
            DeterministicStatus Write(ref DeterministicWriter w);
            DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate);
            DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode);
            DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse);
        }
        private struct ManagedWireGraph : IWireGraph
        {
            internal DeterministicGraph<int, int> Graph;
            public GraphOptions Options => Graph.Options;
            public DeterministicStatus Write(ref DeterministicWriter w) => Graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate) => validate
                ? Graph.TryValidateCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))
                : Graph.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode)
                => Graph.TryWriteDelta(basis, ref w, ref s, default(Int32Policy), default(Int32Policy), out kind, mode);
            public DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse)
                => Graph.TryApplyDelta(ref r, ref s, default(Int32Policy), default(Int32Policy), reverse);
        }
        private struct NativeWireGraph : IWireGraph
        {
            internal NativeDeterministicGraph<int, int> Graph;
            public GraphOptions Options => Graph.Options;
            public DeterministicStatus Write(ref DeterministicWriter w) => Graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate) => validate
                ? Graph.TryValidateCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))
                : Graph.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode)
                => Graph.TryWriteDelta(basis, ref w, ref s, default(Int32Policy), default(Int32Policy), out kind, mode);
            public DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse)
                => Graph.TryApplyDelta(ref r, ref s, default(Int32Policy), default(Int32Policy), reverse);
        }
        private static void Wire<TGraph>(TGraph graph, WireOperation operation, GraphDeltaMode mode,
            NativeArray<byte> basis, NativeArray<byte> target, NativeArray<byte> packet, int packetLength,
            NativeArray<byte> memory, NativeArray<long> result, int iterations) where TGraph : struct, IWireGraph
        {
            var b = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(basis);
            var t = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(target);
            var p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet);
            var work = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(memory);
            for (int i = 0; i < iterations; i++)
            {
                var scratch = new CanonicalScratch(work, memory.Length); var writer = new DeterministicWriter(p, packet.Length);
                var reader = new DeterministicReader(operation == WireOperation.ApplyDelta || operation == WireOperation.ReverseDelta ? p : b,
                    operation == WireOperation.ApplyDelta || operation == WireOperation.ReverseDelta ? packetLength : basis.Length);
                var checkpoint = new GraphCheckpoint(b, basis.Length); GraphTransferKind kind = default; DeterministicStatus s;
                switch (operation)
                {
                    case WireOperation.WriteCanonical: s = graph.Write(ref writer); break;
                    case WireOperation.ValidateCanonical: s = graph.Read(ref reader, ref scratch, true); break;
                    case WireOperation.ReadCanonical: s = graph.Read(ref reader, ref scratch, false); break;
                    case WireOperation.DeltaFacade: s = graph.Delta(checkpoint, ref writer, ref scratch, out kind, mode); break;
                    case WireOperation.DeltaCheckpoints:
                        s = GraphDeltaWire<int, int, Int32Policy, Int32Policy>.TryWriteDelta(ref writer, checkpoint,
                            new GraphCheckpoint(t, target.Length), ref scratch, graph.Options, default, default, out kind, mode); break;
                    default: s = graph.Apply(ref reader, ref scratch, operation == WireOperation.ReverseDelta); break;
                }
                result[1] = (long)s; result[3] = writer.Position; result[4] = scratch.Position; result[5] = reader.Position; result[6] = (long)kind;
                if (!s.IsSuccess()) { result[2] = Complete; return; }
            }
            result[2] = Complete;
        }
        [BurstCompile(CompileSynchronously = true)] private struct WireJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            [ReadOnly] internal NativeArray<byte> Basis, Target;
            internal NativeArray<byte> Packet, Scratch;
            internal NativeArray<long> Result;
            internal WireOperation Operation;
            internal GraphDeltaMode Mode;
            internal int PacketLength, Iterations;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Wire(new NativeWireGraph { Graph = Graph }, Operation, Mode, Basis, Target, Packet, PacketLength, Scratch, Result, Iterations);
            }
        }
        private static Action WireAction(Owner owner, WireOperation op, GraphDeltaMode mode, NativeArray<byte> basis,
            NativeArray<byte> target, NativeArray<byte> packet, int length, NativeArray<byte> scratch, NativeArray<long> result, int iterations)
        {
            if (owner.Managed != null)
            {
                var graph = new ManagedWireGraph { Graph = owner.Managed };
                return () => Wire(graph, op, mode, basis, target, packet, length, scratch, result, iterations);
            }
            var job = new WireJob { Graph = owner.Native, Operation = op, Mode = mode, Basis = basis, Target = target,
                Packet = packet, PacketLength = length, Scratch = scratch, Result = result, Iterations = iterations };
            return () => job.Schedule().Complete();
        }
        private static void Import(Owner owner, NativeArray<byte> bytes, NativeArray<byte> memory)
        {
            var reader = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes), bytes.Length);
            var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(memory), memory.Length);
            (owner.Managed != null ? owner.Managed.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy))
                : owner.Native.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy))).ThrowIfFailure();
            Assert.AreEqual(bytes.Length, reader.Position);
        }
    }
}
