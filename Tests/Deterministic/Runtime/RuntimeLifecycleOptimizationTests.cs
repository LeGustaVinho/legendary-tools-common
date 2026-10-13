using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicRuntimeOptimization")]
    public sealed unsafe class RuntimeLifecycleOptimizationTests
    {
        private static DeterministicRuntimeContext Context(DeterministicBackend backend)
        {
#if UNITY_5_3_OR_NEWER
            return new DeterministicRuntimeContext(backend, backend == DeterministicBackend.Managed ? null : new NativeDeterministicMemoryFactory());
#else
            return new DeterministicRuntimeContext(backend);
#endif
        }
        private static int Input(ulong id, long tick) => unchecked((int)(id * 65537 + (ulong)tick * 17));
        [TestCase(2, PredictionPolicy.HoldLast, DeterministicBackend.Managed)]
        [TestCase(65, PredictionPolicy.HoldLast, DeterministicBackend.Managed)]
        [TestCase(2, PredictionPolicy.Neutral, DeterministicBackend.Managed)]
        [TestCase(65, PredictionPolicy.Neutral, DeterministicBackend.Managed)]
#if UNITY_5_3_OR_NEWER
        [TestCase(2, PredictionPolicy.HoldLast, DeterministicBackend.BurstCompiler)]
        [TestCase(65, PredictionPolicy.HoldLast, DeterministicBackend.BurstCompiler)]
        [TestCase(2, PredictionPolicy.Neutral, DeterministicBackend.BurstCompiler)]
        [TestCase(65, PredictionPolicy.Neutral, DeterministicBackend.BurstCompiler)]
#endif
        public void All64PhysicalColumnsPreserveHistoricRostersAndLateCorrections(int history, PredictionPolicy prediction, DeterministicBackend backend)
        {
            const long start = 127;
            using var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend), 64, history, prediction, -999, initialTick: start);
            for (ulong id = 64; id >= 2; id--) inputs.TryRegisterPlayer(new PlayerId(id), start).ThrowIfFailure();
            for (ulong id = 2; id <= 64; id++)
            {
                inputs.TrySubmit(new PlayerId(id), start, Input(id, start), out _).ThrowIfFailure();
                inputs.TrySubmit(new PlayerId(id), start + 1, Input(id, start + 1), out _).ThrowIfFailure();
            }
            inputs.TryResolve(start, false, out _).ThrowIfFailure(); inputs.TryAdvance(start).ThrowIfFailure();
            inputs.TryRegisterPlayer(new PlayerId(1), start + 1).ThrowIfFailure();
            inputs.TryGetActualFrame(start, out var historic).ThrowIfFailure(); Assert.AreEqual(63, historic.Count);
            for (int i = 0; i < 63; i++)
            { Assert.AreEqual((ulong)i + 2, historic[i].Player.Value); Assert.AreEqual(Input((ulong)i + 2, start), historic[i].Value); }
            inputs.TryResolve(start + 1, true, out var frame).ThrowIfFailure(); Assert.AreEqual(64, frame.Count);
            Assert.AreEqual(1UL, frame[0].Player.Value); Assert.IsTrue(frame[0].Predicted); Assert.AreEqual(-999, frame[0].Value);
            for (int i = 1; i < 64; i++)
            { Assert.AreEqual((ulong)i + 1, frame[i].Player.Value); Assert.AreEqual(Input((ulong)i + 1, start + 1), frame[i].Value); Assert.IsFalse(frame[i].Predicted); }
            inputs.TryAdvance(start + 1).ThrowIfFailure();
            inputs.TrySubmit(new PlayerId(1), start + 1, 123456, out long rollback).ThrowIfFailure(); Assert.AreEqual(start + 1, rollback);
            var active = new PlayerId[63]; for (int i = 0; i < 63; i++) active[i] = new PlayerId((ulong)i + 1);
            inputs.TryApplyActiveRoster(active, start + 2).ThrowIfFailure();
            for (ulong id = 1; id <= 63; id++) inputs.TrySubmit(new PlayerId(id), start + 2, Input(id, start + 2), out _).ThrowIfFailure();
            inputs.TryResolve(start + 2, false, out _).ThrowIfFailure(); inputs.TryAdvance(start + 2).ThrowIfFailure();
            var all = new PlayerId[64]; for (int i = 0; i < 64; i++) all[i] = new PlayerId((ulong)i + 1);
            inputs.TryApplyActiveRoster(all, start + 3).ThrowIfFailure();
            // Rejoining ID 64 lives in physical column zero; the newly admitted ID 1 lives in bit 63.
            inputs.TryRewind(start + 1).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, inputs.TryAdvance(start + 1));
            inputs.TryResolve(start + 1, false, out frame).ThrowIfFailure(); Assert.AreEqual(64, frame.Count); Assert.AreEqual(123456, frame[0].Value);
            Assert.AreEqual(64UL, frame[63].Player.Value); inputs.TryAdvance(start + 1).ThrowIfFailure();
            inputs.TryResolve(start + 2, false, out frame).ThrowIfFailure(); Assert.AreEqual(63, frame.Count);
            Assert.AreEqual(63UL, frame[62].Player.Value); inputs.TryAdvance(start + 2).ThrowIfFailure();
            inputs.TryResolve(start + 3, true, out frame).ThrowIfFailure(); Assert.AreEqual(64, frame.Count);
            Assert.AreEqual(prediction == PredictionPolicy.HoldLast ? Input(64, start + 1) : -999, frame[63].Value);
        }
        [TestCase(3, DeterministicBackend.Managed)] [TestCase(65, DeterministicBackend.Managed)]
#if UNITY_5_3_OR_NEWER
        [TestCase(3, DeterministicBackend.BurstCompiler)] [TestCase(65, DeterministicBackend.BurstCompiler)]
#endif
        public void ForgetIncludesFarFutureRowsBeyondTheWindowAfterRewind(int history, DeterministicBackend backend)
        {
            const long start = 113; var player = new PlayerId(7);
            using var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend), 1, history, PredictionPolicy.HoldLast, -999, initialTick: start);
            inputs.TryRegisterPlayer(player, start).ThrowIfFailure();
            for (long tick = start; tick < start + history; tick++)
            { inputs.TrySubmit(player, tick, Input(7, tick), out _).ThrowIfFailure(); inputs.TryResolve(tick, false, out _).ThrowIfFailure(); inputs.TryAdvance(tick).ThrowIfFailure(); }
            long oldNext = inputs.NextTick, distant = oldNext + history - 1;
            inputs.TrySubmit(player, distant, 123, out _).ThrowIfFailure();
            inputs.TryRewind(start).ThrowIfFailure(); inputs.TryForgetFutureInputs(player, start).ThrowIfFailure();
            while (inputs.NextTick < oldNext)
            {
                long tick = inputs.NextTick; inputs.TryResolve(tick, true, out var frame).ThrowIfFailure();
                Assert.IsTrue(frame[0].Predicted); Assert.AreEqual(-999, frame[0].Value); inputs.TryAdvance(tick).ThrowIfFailure();
            }
            Assert.AreEqual(DeterministicStatus.None, inputs.CanSubmit(player, distant, 456));
            inputs.TrySubmit(player, distant, 456, out _).ThrowIfFailure();
            inputs.TryGetLastActual(player, distant + 1, out int value, out long at).ThrowIfFailure(); Assert.AreEqual(456, value); Assert.AreEqual(distant, at);
        }
        [TestCase(DeterministicBackend.Managed)]
#if UNITY_5_3_OR_NEWER
        [TestCase(DeterministicBackend.BurstCompiler)]
#endif
        public void RangeIterationTerminatesAtTheLastRepresentableTick(DeterministicBackend backend)
        {
            long initial = long.MaxValue - 3; var player = new PlayerId(17);
            using var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend), 2, 2, PredictionPolicy.HoldLast, 0, initialTick: initial);
            inputs.TryRegisterPlayer(player, initial).ThrowIfFailure(); inputs.TrySubmit(player, long.MaxValue - 2, 5, out _).ThrowIfFailure();
            inputs.TryResolve(initial, true, out _).ThrowIfFailure(); inputs.TryAdvance(initial).ThrowIfFailure();
            inputs.TrySubmit(player, long.MaxValue - 1, 7, out _).ThrowIfFailure(); inputs.TryRegisterPlayer(new PlayerId(1), long.MaxValue - 1).ThrowIfFailure();
            inputs.TryForgetFutureInputs(player, long.MaxValue - 1).ThrowIfFailure(); inputs.TrySubmit(player, long.MaxValue - 1, 9, out _).ThrowIfFailure();
            inputs.TryResolve(long.MaxValue - 2, false, out _).ThrowIfFailure(); inputs.TryAdvance(long.MaxValue - 2).ThrowIfFailure();
            inputs.TrySubmit(new PlayerId(1), long.MaxValue - 1, 11, out _).ThrowIfFailure(); inputs.TryResolve(long.MaxValue - 1, false, out var frame).ThrowIfFailure();
            Assert.AreEqual(11, frame[0].Value); Assert.AreEqual(9, frame[1].Value); inputs.TryAdvance(long.MaxValue - 1).ThrowIfFailure();
            inputs.TryForgetFutureInputs(player, long.MaxValue).ThrowIfFailure(); inputs.TryRewind(long.MaxValue - 1).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, inputs.TryAdvance(long.MaxValue - 1));
            inputs.TryResolve(long.MaxValue - 1, false, out _).ThrowIfFailure();
        }

        private struct Payload64 { internal long A, B, C, D, E, F, G, H; }
        private static Payload64 Payload(int i) => new Payload64 { A = i, B = ~i, C = i + 3, D = i * 7L, E = i * 11L, F = i + 17, G = i - 3, H = i ^ 0x31415926 };
        private static int Compare(DeterministicCommandHeader a, DeterministicCommandHeader b)
        {
            int result = a.Phase.CompareTo(b.Phase); if (result == 0) result = a.SystemId.CompareTo(b.SystemId);
            if (result == 0) result = a.Entity.Index.CompareTo(b.Entity.Index); if (result == 0) result = a.Entity.Generation.CompareTo(b.Entity.Generation);
            if (result == 0) result = a.Sequence.CompareTo(b.Sequence); return result == 0 ? a.ProducerId.CompareTo(b.ProducerId) : result;
        }
        [TestCase(1, false)] [TestCase(33, false)] [TestCase(4096, false)] [TestCase(4097, false)]
        [TestCase(1, true)] [TestCase(33, true)] [TestCase(4096, true)] [TestCase(4097, true)]
        public void CommandMergeMatchesAnIndependentOracleAcrossIndexSortCutoffAndPartialPartitions(int count, bool ordered)
        {
            uint[] producers = { 37, 11, 71, 3 }; int capacity = (count + 2) / 3;
            using var buffer = new DeterministicCommandBuffer<Payload64>(Context(DeterministicBackend.Managed), producers, capacity);
            var oracle = new List<DeterministicCommand<Payload64>>(count);
            for (int i = 0; i < count; i++)
            {
                var header = new DeterministicCommandHeader((ushort)(i % 5), (uint)(1 + i % 7), new StableHandle((uint)(i % 29), (uint)(1 + i % 3)), (ulong)i * 5, 1);
                oracle.Add(new DeterministicCommand<Payload64>(header, Payload(i)));
            }
            oracle.Sort((a, b) => Compare(a.Header, b.Header));
            var expected = new List<DeterministicCommand<Payload64>>(count);
            for (int rank = 0; rank < count; rank++)
            {
                int producer = rank / capacity; var item = oracle[ordered ? rank : count - 1 - rank]; var header = item.Header;
                header = new DeterministicCommandHeader(header.Phase, header.SystemId, header.Entity, header.Sequence, producers[producer]);
                buffer.TryGetWriter(producers[producer], out var writer).ThrowIfFailure();
                writer.TryEnqueue(header.Phase, header.SystemId, header.Entity, header.Sequence, item.Payload).ThrowIfFailure(); expected.Add(new DeterministicCommand<Payload64>(header, item.Payload));
            }
            expected.Sort((a, b) => Compare(a.Header, b.Header));
            for (int repeat = 0; repeat < 3; repeat++)
            {
                buffer.TryMerge(out var output).ThrowIfFailure(); Assert.AreEqual(count, output.Length);
                for (int i = 0; i < count; i++)
                { Assert.AreEqual(expected[i].Header, output[i].Header); Assert.AreEqual(expected[i].Payload, output[i].Payload); }
            }
            buffer.TryReset().ThrowIfFailure(); buffer.TryMerge(out var empty).ThrowIfFailure(); Assert.AreEqual(0, empty.Length); Assert.AreEqual(0, buffer.Count);
        }
        [TestCase(false)] [TestCase(true)]
        public void DuplicateCommandsFailWithoutPublishingCountOrBorrowedOutput(bool ordered)
        {
            using var buffer = new DeterministicCommandBuffer<Payload64>(Context(DeterministicBackend.Managed), new uint[] { 17 }, 5);
            buffer.TryGetWriter(17, out var writer).ThrowIfFailure(); writer.TryEnqueue(0, 1, new StableHandle(1, 1), 0UL, Payload(1)).ThrowIfFailure();
            buffer.TryMerge(out _).ThrowIfFailure(); Assert.AreEqual(1, buffer.Count);
            foreach (int key in ordered ? new[] { 1, 1, 2, 3 } : new[] { 3, 1, 2, 1 })
                writer.TryEnqueue(0, 1, new StableHandle((uint)key, 1), 0UL, Payload(key)).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, buffer.TryMerge(out var rejected)); Assert.AreEqual(0, rejected.Length); Assert.AreEqual(1, buffer.Count);
            buffer.TryReset().ThrowIfFailure(); writer.TryEnqueue(0, 1, default, Payload(5)).ThrowIfFailure(); buffer.TryMerge(out var recovered).ThrowIfFailure(); Assert.AreEqual(1, recovered.Length);
        }
        [Test]
        public void HeaderComparatorPreservesUnsignedLimitsAndEveryTieBreaker()
        {
            var headers = new[] {
                new DeterministicCommandHeader(0,1,default,0,1), new DeterministicCommandHeader(ushort.MaxValue,uint.MaxValue,new StableHandle(uint.MaxValue,uint.MaxValue),ulong.MaxValue,uint.MaxValue),
                new DeterministicCommandHeader(7,17,new StableHandle(3,1),5,11), new DeterministicCommandHeader(7,17,new StableHandle(3,1),5,12),
                new DeterministicCommandHeader(7,17,new StableHandle(3,1),6,1), new DeterministicCommandHeader(7,17,new StableHandle(3,2),0,1),
                new DeterministicCommandHeader(7,17,new StableHandle(4,0),0,1), new DeterministicCommandHeader(7,18,default,0,1), new DeterministicCommandHeader(8,1,default,0,1) };
            foreach (var a in headers) foreach (var b in headers) Assert.AreEqual(Math.Sign(Compare(a,b)),Math.Sign(a.CompareTo(b)));
        }
    }
}
