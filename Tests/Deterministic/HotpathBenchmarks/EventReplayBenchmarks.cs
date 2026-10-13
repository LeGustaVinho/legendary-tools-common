using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class ExtendedHotpathBenchmarks
    {
        public enum EventOrder { Sorted, Reverse, Shuffled }
        [StructLayout(LayoutKind.Sequential)]
        private struct EventPayload64
        {
            internal long A, B, C, D, E, F, G, H;
            internal static EventPayload64 From(int value) => new EventPayload64
            { A = value, B = value + 1, C = value + 2, D = value + 3, E = value + 4, F = value + 5, G = value + 6, H = value + 7 };
        }
        private sealed class EventSink<T> : IDeterministicEventSink<T> where T : unmanaged
        {
            internal readonly DeterministicEvent<T>[] Values;
            internal int Count, Retracted;
            internal EventSink(int count) { Values = new DeterministicEvent<T>[count]; }
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<T> value)
            {
                if (delivery == DeterministicEventDelivery.Speculated) Values[Count++] = value;
                if (delivery == DeterministicEventDelivery.Retracted) Retracted++;
            }
        }
        private static int[] EventIndices(int count, EventOrder order)
        {
            var indices = new int[count];
            for (int i = 0; i < count; i++) indices[i] = order == EventOrder.Reverse ? count - 1 - i : i;
            if (order == EventOrder.Shuffled)
            {
                uint seed = 0x1234abcd;
                for (int i = count - 1; i > 0; i--)
                { seed = unchecked(seed * 1664525 + 1013904223); int j = (int)(seed % (uint)(i + 1)); int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp; }
            }
            return indices;
        }
        private static IEnumerable<TestCaseData> EventCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int count in new[] { 256, 2048 })
            foreach (EventOrder order in new[] { EventOrder.Sorted, EventOrder.Reverse, EventOrder.Shuffled })
            foreach (bool large in new[] { false, true })
                yield return new TestCaseData(backend, count, order, large).SetName($"H8_EventPrepare_{backend}_{count}_{order}_Payload{(large ? 64 : 4)}");
        }
        [TestCaseSource(nameof(EventCases)), Performance]
        public void EventPreparationSortsLargeBatches(Backend backend, int count, EventOrder order, bool large)
        {
            if (large) EventPreparation(backend, count, order, EventPayload64.From, v =>
            {
                int value = (int)v.A;
                if (v.B != value + 1 || v.C != value + 2 || v.D != value + 3 || v.E != value + 4 || v.F != value + 5 || v.G != value + 6 || v.H != value + 7)
                    Assert.Fail("Large event payload changed at " + value);
                return value;
            });
            else EventPreparation(backend, count, order, value => value, value => value);
        }
        private void EventPreparation<T>(Backend backend, int count, EventOrder order, Func<int, T> payload, Func<T, int> valueOf) where T : unmanaged
        {
            var factory = new TrackingFactory(backend); var indices = EventIndices(count, order); var sink = new EventSink<T>(count);
            using var ledger = new DeterministicEventLedger<T>(Context(backend, factory), 2, count, sink);
            DeterministicStatus status = default;
            Benchmark("H8", "EventLedger.PreparePublish", backend, order + "_Payload" + sizeof(T), count, count,
                count * sizeof(DeterministicEvent<T>), () => status = ledger.TryPreparePublish(0),
                () =>
                {
                    status.ThrowIfFailure(); ledger.TryPublish(0).ThrowIfFailure(); Assert.AreEqual(count, sink.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (!sink.Values[i].Id.Equals(new DeterministicEventId(0, 1, new StableHandle((uint)(i / 4), 1), (ulong)(i % 4))) || valueOf(sink.Values[i].Payload) != i)
                            Assert.Fail("Event order/content at " + i);
                    }
                    ledger.TryRetractFrom(0).ThrowIfFailure(); Assert.AreEqual(count, sink.Retracted); Assert.AreEqual(-1, ledger.NewestTick);
                },
                () =>
                {
                    sink.Count = sink.Retracted = 0; ledger.TryBegin(0, out var writer).ThrowIfFailure();
                    foreach (int i in indices) writer.TryEmit(1, new StableHandle((uint)(i / 4), 1), (ulong)(i % 4), payload(i)).ThrowIfFailure();
                }, () => new Diagnostics { reservedArenaBytes = factory.Reserved, active = count, universe = 2, payloadElementBytes = sizeof(T) });
        }

        public enum ReplayOperation { ReadTailInputs, FindCheckpoints, Import, RejectLateChecksum }
        private const int ReplayLookups = 32, CheckpointInterval = 64;
        private static int ReplayValue(int tick) => unchecked(tick * 104729 ^ 0x31415926);
        private static ulong ReplayHash(int tick) => unchecked(0xd1b54a32d192ed03UL ^ (ulong)(tick + 1) * 0x9e3779b97f4a7c15UL);
        private sealed class ReplayWorld : IDisposable
        {
            internal readonly TrackingFactory Factory;
            internal readonly DeterministicStateRegistry Registry;
            internal readonly DeterministicReplayArchive<int, Int32Policy> Archive, Destination;
            internal readonly DeterministicCell<int> Managed;
            internal NativeDeterministicCell<int> Native;
            internal readonly NativeArray<byte> Packet;
            internal readonly int PacketLength, Records;
            internal ReplayWorld(Backend backend, int ticks)
            {
                Factory = new TrackingFactory(backend); var context = Context(backend, Factory);
                var profile = new DeterministicCompatibilityProfile(1, 2, 3, 4, 5);
                Registry = new DeterministicStateRegistry(context, profile, 1);
                if (backend == Backend.Managed)
                {
                    Managed = new DeterministicCell<int>(Options(1)); Managed.Value = ReplayValue(0);
                    Registry.TryRegister(ManagedStateBindings.Cell(1, Managed, default(Int32Policy), 1, 256, 2048)).ThrowIfFailure();
                }
                else
                {
                    Native = new NativeDeterministicCell<int>(Options(1), Allocator.Persistent); Native.Value = ReplayValue(0);
                    Registry.TryRegister(NativeStateBindings.Cell(1, Native, default(Int32Policy), 1, 256, 2048)).ThrowIfFailure();
                }
                Registry.TryFreeze().ThrowIfFailure(); int maxBytes = ticks * 96 + 2048; Records = ticks + ticks / CheckpointInterval + 1;
                Archive = new DeterministicReplayArchive<int, Int32Policy>(context, profile.Fingerprint, maxBytes, Records, 1);
                Destination = new DeterministicReplayArchive<int, Int32Policy>(context, profile.Fingerprint, maxBytes, Records, 1);
                Archive.TryBegin(Registry, 0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[1];
                for (int tick = 0; tick < ticks; tick++)
                {
                    inputs[0] = new DeterministicPlayerInput<int>(new PlayerId(1), ReplayValue(tick));
                    Archive.TryAppendInputs(tick, inputs, ReplayHash(tick)).ThrowIfFailure();
                    if ((tick + 1) % CheckpointInterval == 0)
                    {
                        if (Managed != null) Managed.Value = ReplayValue(tick + 1); else Native.Value = ReplayValue(tick + 1);
                        Archive.TryAppendCheckpoint(Registry, tick + 1).ThrowIfFailure();
                    }
                }
                Packet = new NativeArray<byte>(maxBytes, Allocator.Persistent);
                var writer = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet), Packet.Length);
                Archive.TryWriteCanonical(ref writer).ThrowIfFailure(); PacketLength = writer.Position;
                // A different existing archive makes a late validation failure observable.
                Destination.TryBegin(Registry, ticks).ThrowIfFailure();
            }
            public void Dispose()
            { Packet.Dispose(); Destination.Dispose(); Archive.Dispose(); Registry.Dispose(); Managed?.Dispose(); if (Native.IsCreated) Native.Dispose(); }
        }
        private static IEnumerable<TestCaseData> ReplayCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int ticks in new[] { 256, 8192 })
            foreach (ReplayOperation operation in new[] { ReplayOperation.ReadTailInputs, ReplayOperation.FindCheckpoints, ReplayOperation.Import, ReplayOperation.RejectLateChecksum })
                yield return new TestCaseData(backend, ticks, operation).SetName($"H9_Replay_{backend}_{ticks}_{operation}");
        }
        [TestCaseSource(nameof(ReplayCases)), Performance]
        public void ReplayIndexAndCanonicalImport(Backend backend, int ticks, ReplayOperation operation)
        {
            using var world = new ReplayWorld(backend, ticks);
            using var verification = new NativeArray<byte>(world.Packet.Length, Allocator.Persistent);
            var values = new int[ReplayLookups]; var hashes = new ulong[ReplayLookups]; var indices = new int[ReplayLookups]; var boundaries = new long[ReplayLookups];
            int status = 0, position = -1;
            byte* packet = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(world.Packet);
            byte* check = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(verification);
            ulong expectedArchiveHash = DeterministicHash.Xxh64(packet, world.PacketLength);
            var baselineWriter = new DeterministicWriter(check, verification.Length);
            world.Destination.TryWriteCanonical(ref baselineWriter).ThrowIfFailure();
            int baselineLength = baselineWriter.Position; ulong baselineHash = DeterministicHash.Xxh64(check, baselineLength);
            var baselineBytes = new byte[baselineLength]; for (int i = 0; i < baselineLength; i++) baselineBytes[i] = check[i];
            var verificationTicks = new[] { 0, ticks / 2, ticks - 1 };
            if (operation == ReplayOperation.RejectLateChecksum) packet[world.PacketLength - 1] ^= 1;
            bool import = operation == ReplayOperation.Import || operation == ReplayOperation.RejectLateChecksum;
            Benchmark("H9", "Replay." + operation, backend, "CheckpointEvery64_1Player", ticks, import ? ticks : ReplayLookups,
                import ? world.PacketLength : 0,
                () =>
                {
                    status = 0;
                    if (import)
                    {
                        var reader = new DeterministicReader(packet, world.PacketLength);
                        status = (int)world.Destination.TryReadCanonical(ref reader, world.Registry); position = reader.Position;
                    }
                    else for (int i = 0; i < ReplayLookups; i++)
                    {
                        int tick = ticks - ReplayLookups + i;
                        if (operation == ReplayOperation.FindCheckpoints)
                            status |= (int)world.Archive.TryFindCheckpoint(tick, out indices[i], out boundaries[i]);
                        else
                        {
                            var result = world.Archive.TryReadInputs(tick, out var inputs, out hashes[i]); status |= (int)result;
                            values[i] = result.IsSuccess() && inputs.Length == 1 && inputs[0].Player == new PlayerId(1) ? inputs[0].Value : int.MinValue;
                        }
                    }
                },
                () =>
                {
                    Assert.AreEqual(operation == ReplayOperation.RejectLateChecksum ? (int)DeterministicStatus.InvalidEncoding : 0, status);
                    if (import)
                    {
                        Assert.AreEqual(operation == ReplayOperation.RejectLateChecksum ? 0 : world.PacketLength, position);
                        bool rejected = operation == ReplayOperation.RejectLateChecksum;
                        Assert.AreEqual(rejected ? 1 : world.Records, world.Destination.Count); Assert.AreEqual(ticks, world.Destination.NextTick);
                        Assert.AreEqual(rejected ? ticks : 0, world.Destination.InitialTick);
                        // Byte equality checks the entire archive without a quadratic untimed series of indexed lookups.
                        var writer = new DeterministicWriter(check, verification.Length); world.Destination.TryWriteCanonical(ref writer).ThrowIfFailure();
                        Assert.AreEqual(rejected ? baselineLength : world.PacketLength, writer.Position);
                        Assert.AreEqual(rejected ? baselineHash : expectedArchiveHash, DeterministicHash.Xxh64(check, writer.Position));
                        for (int i = 0; i < writer.Position; i++)
                            if (check[i] != (rejected ? baselineBytes[i] : packet[i])) Assert.Fail("Replay canonical byte " + i);
                        if (rejected) Assert.AreEqual(DeterministicStatus.NotFound, world.Destination.TryReadInputs(0, out _, out _));
                        else foreach (int tick in verificationTicks)
                        {
                            world.Destination.TryReadInputs(tick, out var inputs, out ulong hash).ThrowIfFailure();
                            if (inputs.Length != 1 || inputs[0].Player.Value != 1 || inputs[0].Value != ReplayValue(tick) || hash != ReplayHash(tick)) Assert.Fail("Replay frame " + tick);
                        }
                    }
                    else for (int i = 0; i < ReplayLookups; i++)
                    {
                        int tick = ticks - ReplayLookups + i;
                        if (operation == ReplayOperation.FindCheckpoints)
                        { Assert.AreEqual(tick / CheckpointInterval * CheckpointInterval, boundaries[i]); Assert.AreEqual(tick / CheckpointInterval * (CheckpointInterval + 1), indices[i]); }
                        else { Assert.AreEqual(ReplayValue(tick), values[i]); Assert.AreEqual(ReplayHash(tick), hashes[i]); }
                    }
                }, diagnostics: () => new Diagnostics
                { reservedArenaBytes = world.Factory.Reserved, active = ticks, recordCount = world.Records, packetBytes = world.PacketLength, scratchBytes = world.Packet.Length + verification.Length, payloadElementBytes = sizeof(int) });
        }
    }
}
