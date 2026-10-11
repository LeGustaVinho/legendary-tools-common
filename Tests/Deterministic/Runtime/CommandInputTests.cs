using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class CommandInputTests
    {
        private static DeterministicRuntimeContext Context() => new DeterministicRuntimeContext(DeterministicBackend.Managed);
        [Test]
        public void CommandOrderDependsOnStableHeadersAcrossProducerSchedules()
        {
            var random = new Random(9761); var oracle = new List<DeterministicCommand<int>>();
            using (var buffer = new DeterministicCommandBuffer<int>(Context(), new uint[] { 43, 7, 21 }, 90))
            {
                foreach (uint producer in new uint[] { 21, 43, 7 })
                {
                    buffer.TryGetWriter(producer, out var writer).ThrowIfFailure();
                    for (int sequence = 0; sequence < 90; sequence++)
                    {
                        ushort phase = (ushort)random.Next(5); uint system = (uint)random.Next(1, 8); var entity = new StableHandle((uint)random.Next(30), (uint)random.Next(1, 4));
                        writer.TryEnqueue(phase, system, entity, (ulong)sequence, sequence * (int)producer).ThrowIfFailure();
                        oracle.Add(new DeterministicCommand<int>(new DeterministicCommandHeader(phase, system, entity, (ulong)sequence, producer), sequence * (int)producer));
                    }
                    Assert.AreEqual(DeterministicStatus.CapacityExceeded, writer.TryEnqueue(0, 1, default, 9));
                }
                oracle.Sort((a, b) =>
                {
                    int c = a.Header.Phase.CompareTo(b.Header.Phase); if (c == 0) c = a.Header.SystemId.CompareTo(b.Header.SystemId);
                    if (c == 0) c = a.Header.Entity.Index.CompareTo(b.Header.Entity.Index); if (c == 0) c = a.Header.Entity.Generation.CompareTo(b.Header.Entity.Generation);
                    if (c == 0) c = a.Header.Sequence.CompareTo(b.Header.Sequence); return c == 0 ? a.Header.ProducerId.CompareTo(b.Header.ProducerId) : c;
                });
                buffer.TryMerge(out var result).ThrowIfFailure(); Assert.AreEqual(oracle.Count, result.Length);
                for (int i = 0; i < result.Length; i++) { Assert.AreEqual(oracle[i].Header, result[i].Header); Assert.AreEqual(oracle[i].Payload, result[i].Payload); }
                buffer.TryReset().ThrowIfFailure(); buffer.TryGetWriter(7, out var duplicate).ThrowIfFailure();
                duplicate.TryEnqueue(0, 1, default, 9UL, 10); duplicate.TryEnqueue(0, 1, default, 9UL, 11);
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, buffer.TryMerge(out _));
            }
        }
        [Test]
        public void CommandCodecRoundTripsExplicitFieldsAndPreservesShortDestination()
        {
            byte* bytes = stackalloc byte[64]; for (int i = 0; i < 64; i++) bytes[i] = 0xcd;
            var codec = new DeterministicCommandCodec<int, Int32Policy>(default);
            var value = new DeterministicCommand<int>(new DeterministicCommandHeader(7, 42, new StableHandle(87, 3), ulong.MaxValue, 3), -5);
            var writer = new DeterministicWriter(bytes, 26); Assert.AreEqual(DeterministicStatus.BufferTooSmall, codec.TryWrite(ref writer, value));
            Assert.AreEqual(0, writer.Position); for (int i = 0; i < 64; i++) Assert.AreEqual(0xcd, bytes[i]);
            writer = new DeterministicWriter(bytes, 64); codec.TryWrite(ref writer, value).ThrowIfFailure();
            var reader = new DeterministicReader(bytes, writer.Position); codec.TryRead(ref reader, out var restored).ThrowIfFailure(); Assert.AreEqual(value.Header, restored.Header); Assert.AreEqual(-5, restored.Payload);
        }
        [TestCase(PredictionPolicy.HoldLast)] [TestCase(PredictionPolicy.Neutral)]
        public void LateActualInputsRequestRollbackOnlyWhenTheUsedPredictionDiffers(PredictionPolicy prediction)
        {
            using (var history = new DeterministicInputHistory<int, Int32Policy>(Context(), 3, 8, prediction, 0))
            {
                var a = new PlayerId(20); var b = new PlayerId(10); history.TryRegisterPlayer(a, 0).ThrowIfFailure(); history.TryRegisterPlayer(b, 0).ThrowIfFailure();
                history.TrySubmit(a, 0, 7, out _).ThrowIfFailure(); history.TrySubmit(b, 0, 3, out _).ThrowIfFailure();
                history.TryResolve(0, false, out var first).ThrowIfFailure(); Assert.AreEqual(b, first[0].Player); Assert.AreEqual(3, first[0].Value); history.TryAdvance(0).ThrowIfFailure();
                history.TrySubmit(b, 1, 4, out _).ThrowIfFailure(); history.TryResolve(1, true, out var frame).ThrowIfFailure();
                int expected = prediction == PredictionPolicy.HoldLast ? 7 : 0; Assert.AreEqual(expected, frame[1].Value); Assert.IsTrue(frame[1].Predicted); history.TryAdvance(1).ThrowIfFailure();
                history.TrySubmit(a, 1, expected, out long rollback).ThrowIfFailure(); Assert.AreEqual(-1, rollback);
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, history.TrySubmit(a, 1, expected + 1, out _));
                history.TrySubmit(b, 2, 4, out _).ThrowIfFailure(); history.TryResolve(2, true, out _).ThrowIfFailure(); history.TryAdvance(2).ThrowIfFailure();
                history.TrySubmit(a, 2, expected + 5, out rollback).ThrowIfFailure(); Assert.AreEqual(2, rollback);
                history.TryRewind(2).ThrowIfFailure(); history.TryResolve(2, false, out frame).ThrowIfFailure(); Assert.AreEqual(expected + 5, frame[1].Value); Assert.IsFalse(frame[1].Predicted);
            }
        }
        [Test]
        public void RosterAdmissionPreservesInputsAndHoldLastSurvivesHistoryEviction()
        {
            using (var history = new DeterministicInputHistory<int, Int32Policy>(Context(), 3, 4, PredictionPolicy.HoldLast, 0))
            {
                var old = new PlayerId(50); var joining = new PlayerId(10); history.TryRegisterPlayer(old, 0).ThrowIfFailure();
                for (int tick = 0; tick < 5; tick++) { history.TrySubmit(old, tick, tick + 10, out _); history.TryResolve(tick, false, out _); history.TryAdvance(tick); }
                history.TrySubmit(old, 5, 77, out _).ThrowIfFailure(); history.TryRegisterPlayer(joining, 6).ThrowIfFailure();
                history.TryResolve(5, false, out var before).ThrowIfFailure(); Assert.AreEqual(1, before.Count); Assert.AreEqual(77, before[0].Value); history.TryAdvance(5).ThrowIfFailure();
                history.TrySubmit(joining, 6, 2, out _).ThrowIfFailure(); history.TryResolve(6, true, out var frame).ThrowIfFailure();
                Assert.AreEqual(2, frame.Count); Assert.AreEqual(joining, frame[0].Player); Assert.AreEqual(77, frame[1].Value); history.TryAdvance(6).ThrowIfFailure();
                history.TryDeactivatePlayer(joining, 7).ThrowIfFailure();
                for (int tick = 7; tick < 17; tick++) { history.TryResolve(tick, true, out frame).ThrowIfFailure(); Assert.AreEqual(1, frame.Count); Assert.AreEqual(77, frame[0].Value); history.TryAdvance(tick).ThrowIfFailure(); }
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TrySubmit(old, 1, 11, out _)); Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRewind(1));
            }
        }
        [Test]
        public void RejoiningPlayerAndSortedInsertionPreserveHistoricParticipationDuringRewind()
        {
            using (var history = new DeterministicInputHistory<int, Int32Policy>(Context(), 3, 16, PredictionPolicy.HoldLast, 0))
            {
                var a = new PlayerId(20); var b = new PlayerId(30); var c = new PlayerId(10);
                history.TryRegisterPlayer(a, 0).ThrowIfFailure(); history.TryRegisterPlayer(b, 0).ThrowIfFailure();
                for (int tick = 0; tick < 7; tick++)
                {
                    if (tick == 2) history.TryApplyActiveRoster(new[] { a }, tick).ThrowIfFailure();
                    if (tick == 4) history.TryApplyActiveRoster(new[] { a, b }, tick).ThrowIfFailure();
                    if (tick == 6) history.TryRegisterPlayer(c, tick).ThrowIfFailure();
                    history.TrySubmit(a, tick, tick + 1, out _).ThrowIfFailure();
                    if (tick < 2 || tick >= 4) history.TrySubmit(b, tick, tick + 9, out _).ThrowIfFailure();
                    if (tick == 6) history.TrySubmit(c, tick, 99, out _).ThrowIfFailure();
                    history.TryResolve(tick, false, out _).ThrowIfFailure(); history.TryAdvance(tick).ThrowIfFailure();
                }
                history.TryRewind(0).ThrowIfFailure();
                for (int tick = 0; tick < 7; tick++)
                {
                    history.TryResolve(tick, false, out var frame).ThrowIfFailure();
                    Assert.AreEqual(tick == 6 ? 3 : tick >= 2 && tick < 4 ? 1 : 2, frame.Count);
                    Assert.AreEqual(tick == 6 ? c : a, frame[0].Player); history.TryAdvance(tick).ThrowIfFailure();
                }
                Assert.AreEqual(DeterministicStatus.NotFound, history.CanSubmit(b, 3, 11));
            }
        }
        [Test]
        public void OrderedRosterReplacementCancelsEarlierScheduledFutureAdmission()
        {
            using (var history = new DeterministicInputHistory<int, Int32Policy>(Context(), 2, 16, PredictionPolicy.Neutral, 0))
            {
                var a = new PlayerId(1); var b = new PlayerId(2); history.TryRegisterPlayer(a, 0).ThrowIfFailure(); history.TryRegisterPlayer(b, 4).ThrowIfFailure();
                history.TryApplyActiveRoster(new[] { a }, 0).ThrowIfFailure();
                for (int tick = 0; tick < 6; tick++)
                {
                    history.TrySubmit(a, tick, tick, out _).ThrowIfFailure(); history.TryResolve(tick, false, out var frame).ThrowIfFailure();
                    Assert.AreEqual(1, frame.Count); history.TryAdvance(tick).ThrowIfFailure();
                }
                Assert.AreEqual(DeterministicStatus.NotFound, history.CanSubmit(b, 4, 1));
            }
        }
    }
}
