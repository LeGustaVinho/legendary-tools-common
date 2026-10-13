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
    public sealed unsafe partial class NetworkLifecycleHotpathBenchmarks
    {
        public enum EnvelopeOperation { Write, Read, RejectLateChecksum, RejectProfile }
        private const int EnvelopeBatch = 16;
        private static readonly PeerId EnvelopeSender = new PeerId(new PlayerId(17), 19), EnvelopeReceiver = new PeerId(new PlayerId(23), 29);
        private const ulong EnvelopeSession = 0x1122334455667788UL, EnvelopeProfile = 0x8877665544332211UL;
        private struct EnvelopeResult
        {
            internal DeterministicMessageHeader Header;
            internal ulong PayloadAddress;
            internal int Status, Position, Length, Managed;
        }
        private static IEnumerable<TestCaseData> EnvelopeCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int bytes in new[] { 64, 4096, 65536 })
            foreach (int offset in new[] { 0, 3 })
            foreach (EnvelopeOperation operation in Enum.GetValues(typeof(EnvelopeOperation)))
                yield return new TestCaseData(backend, bytes, offset, operation).SetName($"H16_Envelope_{backend}_Bytes{bytes}_Offset{offset}_{operation}");
        }
        private static void ProcessEnvelopes(EnvelopeOperation operation, int bytes, int stride, int offset,
            byte* input, byte* packet, EnvelopeResult* results, int managed)
        {
            var session = new DeterministicSessionId(EnvelopeSession);
            for (int n = 0; n < EnvelopeBatch; n++)
            {
                var result = new EnvelopeResult { Managed = managed };
                if (operation == EnvelopeOperation.Write)
                {
                    var header = new DeterministicMessageHeader(session, EnvelopeProfile, new PeerId(new PlayerId(17), 19), new PeerId(new PlayerId(23), 29), (ulong)n + 31, 37);
                    var writer = new DeterministicWriter(packet + n * stride, offset + bytes + DeterministicSessionProtocol.Overhead, offset);
                    result.Status = (int)DeterministicSessionProtocol.TryWrite(ref writer, header, new ReadOnlySpan<byte>(input + n * bytes, bytes));
                    result.Position = writer.Position;
                }
                else
                {
                    var reader = new DeterministicReader(packet + n * stride, offset + bytes + DeterministicSessionProtocol.Overhead, offset);
                    result.Status = (int)DeterministicSessionProtocol.TryRead(ref reader, session,
                        operation == EnvelopeOperation.RejectProfile ? EnvelopeProfile + 1 : EnvelopeProfile,
                        new PeerId(new PlayerId(23), 29), new PeerId(new PlayerId(17), 19), out result.Header, out var payload);
                    result.Position = reader.Position; result.Length = payload.Length;
                    fixed (byte* borrowed = payload) result.PayloadAddress = (ulong)borrowed;
                }
                results[n] = result;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct EnvelopeJob : IJob
        {
            internal EnvelopeOperation Operation;
            internal int Bytes, Stride, Offset;
            [ReadOnly] internal NativeArray<byte> Input;
            internal NativeArray<byte> Packet;
            internal NativeArray<EnvelopeResult> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed);
                ProcessEnvelopes(Operation, Bytes, Stride, Offset, (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Input),
                    (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet), (EnvelopeResult*)NativeArrayUnsafeUtility.GetUnsafePtr(Results), managed);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static void PutUInt(byte* destination, ref int position, ulong value, int bytes)
        { for (int i = 0; i < bytes; i++) destination[position++] = (byte)(value >> (i * 8)); }
        private static void BuildEnvelopeOracle(byte* packet, byte* input, int bytes, int stride, int offset)
        {
            for (int i = 0; i < stride * EnvelopeBatch; i++) packet[i] = 0xcc;
            for (int n = 0; n < EnvelopeBatch; n++)
            {
                byte* target = packet + stride * n; int position = offset;
                PutUInt(target, ref position, 0x3150444c, 4); PutUInt(target, ref position, DeterministicCompatibilityProfile.ProtocolVersion, 2);
                PutUInt(target, ref position, EnvelopeSession, 8); PutUInt(target, ref position, EnvelopeProfile, 8);
                PutUInt(target, ref position, 17, 8); PutUInt(target, ref position, 19, 8);
                PutUInt(target, ref position, 23, 8); PutUInt(target, ref position, 29, 8);
                PutUInt(target, ref position, (ulong)n + 31, 8); PutUInt(target, ref position, 37, 1); PutUInt(target, ref position, (ulong)bytes, 4);
                for (int i = 0; i < bytes; i++) target[position++] = input[n * bytes + i];
                ulong checksum = DeterministicHash.Xxh64(target + offset, DeterministicSessionProtocol.HeaderBytes + bytes);
                PutUInt(target, ref position, checksum, 8);
                Assert.AreEqual(offset + bytes + DeterministicSessionProtocol.Overhead, position);
            }
        }
        [TestCaseSource(nameof(EnvelopeCases)), Performance]
        public void SessionEnvelopeCopyChecksumAndBorrowedRead(Backend backend, int bytes, int offset, EnvelopeOperation operation)
        {
            int wireBytes = bytes + DeterministicSessionProtocol.Overhead, stride = offset + wireBytes + 5;
            using var input = new NativeArray<byte>(bytes * EnvelopeBatch, Allocator.Persistent);
            using var packet = new NativeArray<byte>(stride * EnvelopeBatch, Allocator.Persistent);
            using var oracle = new NativeArray<byte>(packet.Length, Allocator.Persistent);
            using var results = new NativeArray<EnvelopeResult>(EnvelopeBatch, Allocator.Persistent);
            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet), o = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(oracle);
            byte* data = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(input);
            var output = (EnvelopeResult*)NativeArrayUnsafeUtility.GetUnsafePtr(results);
            for (int n = 0; n < EnvelopeBatch; n++) for (int i = 0; i < bytes; i++) data[n * bytes + i] = (byte)(i * 17 + n * 13 + 3);
            BuildEnvelopeOracle(o, data, bytes, stride, offset);
            if (operation == EnvelopeOperation.RejectLateChecksum)
                for (int n = 0; n < EnvelopeBatch; n++) o[n * stride + offset + wireBytes - 1] ^= 0x80;
            Buffer.MemoryCopy(o, p, packet.Length, packet.Length);
            var job = new EnvelopeJob { Operation = operation, Bytes = bytes, Stride = stride, Offset = offset, Input = input, Packet = packet, Results = results };
            ulong digest = 0;
            Action action = backend == Backend.Managed
                ? (Action)(() => ProcessEnvelopes(operation, bytes, stride, offset, data, p, output, 1))
                : () => job.Schedule().Complete();
            Action setup = operation == EnvelopeOperation.Write ? (Action)(() => { for (int i = 0; i < packet.Length; i++) p[i] = 0xcc; }) : null;
            Benchmark("H16", "Envelope." + operation, backend, "Offset" + offset + "_Batch16", bytes, EnvelopeBatch, wireBytes * EnvelopeBatch, action,
                () =>
                {
                    bool rejection = operation == EnvelopeOperation.RejectLateChecksum || operation == EnvelopeOperation.RejectProfile;
                    var expectedStatus = operation == EnvelopeOperation.RejectLateChecksum ? DeterministicStatus.InvalidEncoding : operation == EnvelopeOperation.RejectProfile ? DeterministicStatus.ProfileMismatch : DeterministicStatus.None;
                    digest = DigestSeed;
                    for (int n = 0; n < EnvelopeBatch; n++)
                    {
                        var result = output[n]; if (backend == Backend.BurstJob) CheckBurst(result.Managed);
                        Assert.AreEqual((int)expectedStatus, result.Status); Assert.AreEqual(rejection ? offset : offset + wireBytes, result.Position);
                        if (operation == EnvelopeOperation.Read)
                        {
                            Assert.AreEqual(EnvelopeSession, result.Header.Session.Value); Assert.AreEqual(EnvelopeProfile, result.Header.Fingerprint);
                            Assert.AreEqual(EnvelopeSender, result.Header.Sender); Assert.AreEqual(EnvelopeReceiver, result.Header.Destination);
                            Assert.AreEqual((ulong)n + 31, result.Header.Sequence); Assert.AreEqual(37, result.Header.Kind); Assert.AreEqual(bytes, result.Length);
                            Assert.AreEqual((ulong)(p + n * stride + offset + DeterministicSessionProtocol.HeaderBytes), result.PayloadAddress, "Read must borrow the packet payload.");
                        }
                        if (rejection)
                        { Assert.AreEqual(0, result.Length); Assert.AreEqual(0UL, result.PayloadAddress); Assert.AreEqual(default(DeterministicMessageHeader), result.Header); }
                        digest = Mix(digest, (ulong)result.Status); digest = Mix(digest, (ulong)result.Position);
                    }
                    // Compare every wire byte, including guards and the late corruption in negative cases.
                    for (int i = 0; i < packet.Length; i++)
                    { if (p[i] != o[i]) Assert.Fail("Envelope byte/guard differs at " + i); digest = Mix(digest, p[i]); }
                }, setup,
                () => new Diagnostics { active = EnvelopeBatch, packetBytes = (long)wireBytes * EnvelopeBatch,
                    externalBufferBytes = (long)input.Length + packet.Length + oracle.Length + results.Length * sizeof(EnvelopeResult), payloadElementBytes = 1 }, () => digest);
        }
    }
}
