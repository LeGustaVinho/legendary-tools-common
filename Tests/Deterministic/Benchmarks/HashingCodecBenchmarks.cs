using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.Benchmarks
{
    public sealed unsafe partial class OperationBenchmarks
    {
        private static IEnumerable<TestCaseData> HashCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.BurstJob })
            foreach (int bytes in new[] { 64, 4096, 65536, 1048576 })
            foreach (int offset in new[] { 0, 3 }) yield return new TestCaseData(backend, bytes, offset);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct HashJob : IJob
        {
            [ReadOnly] internal NativeArray<byte> Input;
            internal int Offset, Bytes, Repetitions;
            internal NativeArray<ulong> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = (ulong)managed;
                byte* pointer = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Input) + Offset;
                ulong digest = 0;
                for (int seed = 0; seed < Repetitions; seed++)
                    digest = ((digest << 7) | (digest >> 57)) ^ DeterministicHash.Xxh64(pointer, Bytes, (ulong)seed);
                Result[1] = digest;
            }
        }
        // Generated independently by Tools/Deterministic/generate_benchmark_hash_vectors.py.
        private static ulong HashOracle(int bytes)
        {
            switch (bytes)
            {
                case 64: return 0xd84a316076afe79dUL;
                case 4096: return 0x12ed3c4454182de7UL;
                case 65536: return 0x8a1a6d2a8e3e5540UL;
                case 1048576: return 0xfe22482c4aff8387UL;
                default: throw new ArgumentOutOfRangeException(nameof(bytes));
            }
        }
        [TestCaseSource(nameof(HashCases)), Performance]
        public void Xxh64PacketHash(ExecutionBackend backend, int bytes, int offset)
        {
            int repetitions = bytes <= 4096 ? 64 : 4;
            var input = new byte[bytes + 4];
            for (int i = 0; i < bytes; i++) input[offset + i] = unchecked((byte)(i * 131 + (i >> 2) + 17));
            ulong expected = HashOracle(bytes); string distribution = offset == 0 ? "Offset0" : "Offset3";
            if (backend == ExecutionBackend.Managed)
            {
                ulong actual = 0;
                Benchmark("XXH64.PacketHash", backend, distribution, bytes, repetitions, bytes * repetitions,
                    () =>
                    {
                        fixed (byte* pointer = input)
                        {
                            actual = 0;
                            for (int seed = 0; seed < repetitions; seed++)
                                actual = ((actual << 7) | (actual >> 57)) ^ DeterministicHash.Xxh64(pointer + offset, bytes, (ulong)seed);
                        }
                    }, () => Assert.AreEqual(expected, actual, "Independent XXH64 oracle"));
            }
            else
            {
                using (var native = new NativeArray<byte>(input, Allocator.Persistent))
                using (var result = new NativeArray<ulong>(2, Allocator.Persistent))
                {
                    var job = new HashJob { Input = native, Result = result, Offset = offset, Bytes = bytes, Repetitions = repetitions };
                    Benchmark("XXH64.PacketHash", backend, distribution, bytes, repetitions, bytes * repetitions,
                        () => job.Schedule().Complete(), () => { CheckBurst((int)result[0]); Assert.AreEqual(expected, result[1], "Independent XXH64 oracle in Burst"); });
                }
            }
        }

        private struct BenchmarkHashPolicy : IDeterministicEquality<int>
        {
            internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) => Collisions ? 1UL : default(Int32Policy).Hash(value);
        }
        private static IEnumerable<TestCaseData> DictionaryCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.BurstJob })
            foreach (int size in new[] { 128, 512, 2048 })
            foreach (bool collisions in new[] { false, true })
            foreach (DictionaryOperation operation in Enum.GetValues(typeof(DictionaryOperation))) yield return new TestCaseData(backend, size, collisions, operation);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct DictionaryJob : IJob
        {
            internal DictionaryOperation Operation;
            [ReadOnly] internal NativeArray<int> Keys, Queries;
            internal NativeDeterministicDictionary<int, int, BenchmarkHashPolicy> Map;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                int status = 0, digest = 0, hits = 0;
                if (Operation == DictionaryOperation.Insert)
                    for (int i = 0; i < Keys.Length; i++) status |= (int)Map.TryAdd(Keys[i], Keys[i] * 17 - 7);
                else for (int i = 0; i < Queries.Length; i++)
                {
                    var s = Map.TryGet(Queries[i], out int value);
                    if (s == DeterministicStatus.None) hits++;
                    else { if (s != DeterministicStatus.NotFound) status |= (int)s; value = -12345; }
                    digest = unchecked(digest * 31 + value);
                }
                Result[1] = status; Result[2] = digest; Result[3] = hits;
            }
        }
        [TestCaseSource(nameof(DictionaryCases)), Performance]
        public void DictionaryHashAndCollisionChains(ExecutionBackend backend, int size, bool collisions, DictionaryOperation operation)
        {
            int[] keys = Keys(size, InputOrder.Shuffled), queries = new int[size * 2]; int expectedDigest = 0;
            for (int i = 0; i < queries.Length; i++)
            { int key = keys[i / 2] + (i % 2 == 0 ? 0 : size); queries[i] = key; expectedDigest = unchecked(expectedDigest * 31 + (key < size ? key * 17 - 7 : -12345)); }
            var policy = new BenchmarkHashPolicy { Collisions = collisions };
            string name = operation == DictionaryOperation.Insert ? "Dictionary.Insert" : "Dictionary.HitAndMissLookup";
            string distribution = collisions ? "ConstantHash" : "Int32Hash"; int items = operation == DictionaryOperation.Insert ? size : queries.Length;
            if (backend == ExecutionBackend.Managed)
            {
                using (var map = new DeterministicDictionary<int, int, BenchmarkHashPolicy>(Options(size), policy))
                {
                    if (operation == DictionaryOperation.HitAndMissLookup) for (int i = 0; i < size; i++) map.Add(keys[i], keys[i] * 17 - 7);
                    int status = 0, actualDigest = 0, hits = 0;
                    Benchmark(name, backend, distribution, size, items, 0,
                        () =>
                        {
                            status = actualDigest = hits = 0;
                            if (operation == DictionaryOperation.Insert) for (int i = 0; i < size; i++) status |= (int)map.TryAdd(keys[i], keys[i] * 17 - 7);
                            else for (int i = 0; i < queries.Length; i++)
                            {
                                var s = map.TryGet(queries[i], out int value);
                                if (s == DeterministicStatus.None) hits++; else { if (s != DeterministicStatus.NotFound) status |= (int)s; value = -12345; }
                                actualDigest = unchecked(actualDigest * 31 + value);
                            }
                        },
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(size, map.Count);
                            if (operation == DictionaryOperation.HitAndMissLookup) { Assert.AreEqual(size, hits); Assert.AreEqual(expectedDigest, actualDigest); }
                            else for (int i = 0; i < size; i++) { map.TryGet(i, out int value).ThrowIfFailure(); if (value != i * 17 - 7) Assert.Fail("Inserted value " + i); }
                        }, operation == DictionaryOperation.Insert ? (Action)map.Clear : null);
                }
            }
            else
            {
                using (var map = new NativeDeterministicDictionary<int, int, BenchmarkHashPolicy>(Options(size), Allocator.Persistent, policy))
                using (var input = new NativeArray<int>(keys, Allocator.Persistent))
                using (var nativeQueries = new NativeArray<int>(queries, Allocator.Persistent))
                using (var result = new NativeArray<int>(4, Allocator.Persistent))
                {
                    if (operation == DictionaryOperation.HitAndMissLookup) for (int i = 0; i < size; i++) map.TryAdd(keys[i], keys[i] * 17 - 7).ThrowIfFailure();
                    var job = new DictionaryJob { Operation = operation, Keys = input, Queries = nativeQueries, Map = map, Result = result };
                    Benchmark(name, backend, distribution, size, items, 0, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(size, map.Count);
                            if (operation == DictionaryOperation.HitAndMissLookup) { Assert.AreEqual(expectedDigest, result[2]); Assert.AreEqual(size, result[3]); }
                            else for (int i = 0; i < size; i++) { map.TryGet(i, out int value).ThrowIfFailure(); if (value != i * 17 - 7) Assert.Fail("Burst inserted value " + i); }
                        }, operation == DictionaryOperation.Insert ? (Action)(() => map.TryClear().ThrowIfFailure()) : null);
                }
            }
        }

        private static IEnumerable<TestCaseData> CodecCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.BurstJob })
            foreach (int size in new[] { 128, 4096, 65536 })
            foreach (CodecOperation operation in Enum.GetValues(typeof(CodecOperation))) yield return new TestCaseData(backend, size, operation);
        }
        private static int ValueAt(int index) => unchecked(index * 104729 ^ 0x31415926);
        [BurstCompile(CompileSynchronously = true)]
        private struct CodecJob : IJob
        {
            internal CodecOperation Operation;
            internal int PacketBytes;
            internal NativeDeterministicArray<int> Values;
            internal NativeArray<byte> Packet, Scratch;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                byte* pointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet);
                if (Operation == CodecOperation.Write)
                {
                    var writer = new DeterministicWriter(pointer, Packet.Length);
                    Result[1] = (int)Values.TryWriteCanonical(ref writer, default(Int32Policy)); Result[2] = writer.Position;
                }
                else
                {
                    var reader = new DeterministicReader(pointer, PacketBytes);
                    var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                    Result[1] = (int)Values.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)); Result[2] = reader.Position;
                }
            }
        }
        [TestCaseSource(nameof(CodecCases)), Performance]
        public void CanonicalEncodeOrValidateAndImport(ExecutionBackend backend, int size, CodecOperation operation)
        {
            int capacity = 4096; var codec = default(Int32Policy);
            for (int i = 0; i < size; i++) capacity = checked(capacity + codec.Measure(ValueAt(i)));
            using (var reference = new DeterministicArray<int>(Options(size)))
            using (var expectedPacket = new NativeArray<byte>(capacity, Allocator.Persistent))
            using (var packet = new NativeArray<byte>(capacity, Allocator.Persistent))
            using (var scratchBytes = new NativeArray<byte>(size * 24 + 4096, Allocator.Persistent))
            {
                for (int i = 0; i < size; i++) reference[i] = ValueAt(i);
                var referenceWriter = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(expectedPacket), capacity);
                reference.TryWriteCanonical(ref referenceWriter, default(Int32Policy)).ThrowIfFailure(); int packetBytes = referenceWriter.Position;
                byte* pointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet);
                byte* expected = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(expectedPacket);
                byte* scratchPointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(scratchBytes);
                UnsafeUtility.MemCpy(pointer, expected, packetBytes);
                string name = operation == CodecOperation.Write ? "Canonical.Encode" : "Canonical.ValidateAndImport";
                if (backend == ExecutionBackend.Managed)
                {
                    using (var values = new DeterministicArray<int>(Options(size)))
                    {
                        for (int i = 0; i < size; i++) values[i] = ValueAt(i);
                        DeterministicStatus status = DeterministicStatus.None; int position = 0;
                        Benchmark(name, backend, "Int32Array", size, size, packetBytes,
                            () =>
                            {
                                if (operation == CodecOperation.Write)
                                { var writer = new DeterministicWriter(pointer, capacity); status = values.TryWriteCanonical(ref writer, default(Int32Policy)); position = writer.Position; }
                                else
                                { var reader = new DeterministicReader(pointer, packetBytes); var scratch = new CanonicalScratch(scratchPointer, scratchBytes.Length); status = values.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)); position = reader.Position; }
                            },
                            () =>
                            {
                                Assert.AreEqual(DeterministicStatus.None, status); Assert.AreEqual(packetBytes, position);
                                if (operation == CodecOperation.Write) { for (int i = 0; i < packetBytes; i++) if (pointer[i] != expected[i]) Assert.Fail("Canonical byte " + i); }
                                else for (int i = 0; i < size; i++) if (values[i] != ValueAt(i)) Assert.Fail("Imported value " + i);
                            }, operation == CodecOperation.Read ? (Action)values.Clear : () => UnsafeUtility.MemSet(pointer, 0xcd, capacity));
                    }
                }
                else
                {
                    using (var values = new NativeDeterministicArray<int>(Options(size), Allocator.Persistent))
                    using (var result = new NativeArray<int>(3, Allocator.Persistent))
                    {
                        var valueWriter = values; for (int i = 0; i < size; i++) valueWriter[i] = ValueAt(i);
                        var job = new CodecJob { Operation = operation, Values = values, Packet = packet, Scratch = scratchBytes, PacketBytes = packetBytes, Result = result };
                        Benchmark(name, backend, "Int32Array", size, size, packetBytes, () => job.Schedule().Complete(),
                            () =>
                            {
                                CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(packetBytes, result[2]);
                                if (operation == CodecOperation.Write) { for (int i = 0; i < packetBytes; i++) if (pointer[i] != expected[i]) Assert.Fail("Burst canonical byte " + i); }
                                else for (int i = 0; i < size; i++) if (values[i] != ValueAt(i)) Assert.Fail("Burst imported value " + i);
                            }, operation == CodecOperation.Read ? (Action)(() => values.TryClear().ThrowIfFailure()) : () => UnsafeUtility.MemSet(pointer, 0xcd, capacity));
                    }
                }
            }
        }
    }
}
