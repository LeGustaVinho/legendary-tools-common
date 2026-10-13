using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class DictionarySparseHotpathBenchmarks
    {
        internal interface IPayload
        {
            int WordCount { get; }
            void Fill(int key, int version);
            ulong Word(int index);
        }
        internal struct Payload64 : IPayload
        {
            private fixed ulong words[8];
            public int WordCount => 8;
            public void Fill(int key, int version) { fixed (ulong* p = words) for (int i = 0; i < 8; i++) p[i] = PayloadWord(key, version, i); }
            public ulong Word(int index) { fixed (ulong* p = words) return p[index]; }
        }
        internal struct Payload256 : IPayload
        {
            private fixed ulong words[32];
            public int WordCount => 32;
            public void Fill(int key, int version) { fixed (ulong* p = words) for (int i = 0; i < 32; i++) p[i] = PayloadWord(key, version, i); }
            public ulong Word(int index) { fixed (ulong* p = words) return p[index]; }
        }
        private static ulong PayloadWord(int key, int version, int word) => unchecked(
            ((ulong)(uint)Value(key) << 32) ^ ((ulong)(uint)version * 0x9e3779b97f4a7c15UL) ^
            ((ulong)(word + 1) * 0xd6e8feb86659fd93UL));
        private static T Payload<T>(int key, int version = 0) where T : unmanaged, IPayload
        { T value = default; value.Fill(key, version); return value; }
        private static ulong VerifyPayload<T>(T value, int key, int version, ulong digest) where T : unmanaged, IPayload
        {
            for (int i = 0; i < value.WordCount; i++)
            {
                ulong actual = value.Word(i);
                if (actual != PayloadWord(key, version, i)) Assert.Fail("Payload differs at key " + key + ", word " + i);
                digest = Mix(digest, actual);
            }
            return digest;
        }
        private static void VerifyZero<T>(T value) where T : unmanaged, IPayload
        { for (int i = 0; i < value.WordCount; i++) if (value.Word(i) != 0) Assert.Fail("Failed lookup published a payload."); }
        internal struct MapHashPolicy : IDeterministicEquality<int>
        {
            internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int key) => Collisions ? 1UL : default(Int32Policy).Hash(key);
        }
        private static BufferOptions Growing(int initial, int maximum, SnapshotPolicy policy, int slots = 1)
            => new BufferOptions(initial, maximum, AllocationPolicy.Grow, policy, slots, 4096);
        private static int[] Range(int count, int stride = 1)
        { var keys = new int[count]; for (int i = 0; i < count; i++) keys[i] = i * stride; return keys; }
        private static int[] Without(int[] keys, int excluded)
        {
            var result = new List<int>(keys.Length);
            foreach (int key in keys) if (key != excluded) result.Add(key);
            return result.ToArray();
        }
        private static int[] Appended(int[] keys, int key)
        { var result = new int[keys.Length + 1]; Array.Copy(keys, result, keys.Length); result[keys.Length] = key; return result; }

        private sealed class DictionaryOwner<T> : IDisposable where T : unmanaged, IPayload
        {
            internal readonly Backend Backend;
            internal readonly MapHashPolicy Policy;
            internal DeterministicDictionary<int, T, MapHashPolicy> Managed;
            internal NativeDeterministicDictionary<int, T, MapHashPolicy> Native;
            internal ManagedDictionarySnapshot<int, T> ManagedRoot;
            internal NativeDictionarySnapshot<int, T> NativeRoot;
            internal NativeArray<int> Inputs, Statuses, Marker, OutputKeys;
            internal NativeArray<T> Values, OutputValues;
            private bool captured;
            private int[] rootKeys;
            private bool[] visited;
            private GraphMemoryStats baseline;
            private int primaryBefore, secondaryBefore, countBefore, usedBefore;
            internal DictionaryOwner(Backend backend, BufferOptions options, bool collisions, int calls, int outputs = 0)
            {
                Backend = backend; Policy = new MapHashPolicy { Collisions = collisions };
                if (backend == Backend.Managed) Managed = new DeterministicDictionary<int, T, MapHashPolicy>(options, Policy);
                else Native = new NativeDeterministicDictionary<int, T, MapHashPolicy>(options, Allocator.Persistent, Policy);
                Inputs = new NativeArray<int>(calls, Allocator.Persistent); Values = new NativeArray<T>(calls, Allocator.Persistent);
                Statuses = new NativeArray<int>(calls, Allocator.Persistent); Marker = new NativeArray<int>(1, Allocator.Persistent);
                OutputKeys = new NativeArray<int>(outputs, Allocator.Persistent); OutputValues = new NativeArray<T>(outputs, Allocator.Persistent);
                visited = new bool[options.MaxCapacity];
            }
            internal OrderedMapState State => Backend == Backend.Managed ? Managed.State : Native.Entries.Data->MapState;
            internal int Capacity => Backend == Backend.Managed ? Managed.Capacity : Native.Capacity;
            internal int BucketCapacity => Backend == Backend.Managed ? Managed.Buckets.Capacity : Native.Buckets.Capacity;
            internal GraphMemoryStats Memory => Backend == Backend.Managed ? Managed.Entries.Owner.MemoryStats + Managed.Buckets.Owner.MemoryStats : Native.Entries.MemoryStats + Native.Buckets.MemoryStats;
            internal OrderedHashEntry<int, T> Entry(int index) => Backend == Backend.Managed ? Managed.Entries.Read(index) : Native.Entries.Read(index);
            internal int Bucket(int index) => Backend == Backend.Managed ? Managed.Buckets.Read(index) : Native.Buckets.Read(index);
            internal void AddInitial(int key)
            { var value = Payload<T>(key); (Backend == Backend.Managed ? Managed.TryAdd(key, value) : Native.TryAdd(key, value)).ThrowIfFailure(); }
            internal void RemoveInitial(int key)
            { (Backend == Backend.Managed ? Managed.TryRemove(key) : Native.TryRemove(key)).ThrowIfFailure(); }
            internal void CaptureBaseline(int[] keys, bool retain = true)
            {
                rootKeys = keys;
                if (retain)
                {
                    if (Backend == Backend.Managed) Managed.TryCapture(out ManagedRoot).ThrowIfFailure();
                    else Native.TryCapture(out NativeRoot).ThrowIfFailure();
                    captured = true;
                }
                baseline = Memory; primaryBefore = Capacity; secondaryBefore = BucketCapacity; countBefore = State.Count; usedBefore = State.Used;
            }
            internal void Reset()
            { (Backend == Backend.Managed ? Managed.TryRestore(ManagedRoot) : Native.TryRestore(NativeRoot)).ThrowIfFailure(); }
            internal void CheckStatus(int expected)
            {
                if (Backend == Backend.BurstJob) CheckBurst(Marker[0]);
                for (int i = 0; i < Statuses.Length; i++) Assert.AreEqual(expected, Statuses[i], "Operation status " + i);
            }
            internal ulong Validate(int[] keys, Func<int, int> version = null)
            {
                var state = State; Assert.AreEqual(keys.Length, state.Count);
                ulong digest = Mix(DigestSeed, (ulong)keys.Length); int encoded = state.OrderHead, previous = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    Assert.Greater(encoded, 0); Assert.LessOrEqual(encoded, state.Used);
                    var entry = Entry(encoded - 1);
                    Assert.AreEqual(1, entry.Active); Assert.AreEqual(keys[i], entry.Key); Assert.AreEqual(previous, entry.OrderPrevious);
                    Assert.AreEqual(Policy.Hash(keys[i]), entry.Hash);
                    digest = Mix(digest, (uint)entry.Key); digest = VerifyPayload(entry.Value, keys[i], version?.Invoke(keys[i]) ?? 0, digest);
                    previous = encoded; encoded = entry.OrderNext;
                }
                Assert.AreEqual(0, encoded); Assert.AreEqual(previous, state.OrderTail);
                Array.Clear(visited, 0, state.Used); int indexed = 0;
                for (int bucket = 0; bucket < BucketCapacity; bucket++)
                {
                    encoded = Bucket(bucket); int steps = 0;
                    while (encoded != 0)
                    {
                        int slot = encoded - 1;
                        Assert.Less((uint)slot, (uint)state.Used); Assert.LessOrEqual(++steps, state.Count, "Bucket cycle");
                        Assert.IsFalse(visited[slot]); visited[slot] = true; indexed++;
                        var entry = Entry(slot); Assert.AreEqual(1, entry.Active);
                        Assert.AreEqual(bucket, (int)(entry.Hash & (ulong)(BucketCapacity - 1))); encoded = entry.BucketNext;
                    }
                }
                Assert.AreEqual(keys.Length, indexed);
                if (captured)
                {
                    Assert.IsTrue(Backend == Backend.Managed ? ManagedRoot.IsCreated : NativeRoot.IsCreated);
                    var rootState = Backend == Backend.Managed ? ManagedRoot.State : NativeRoot.State;
                    Assert.AreEqual(rootKeys.Length, rootState.Count);
                    digest = Mix(digest, (ulong)rootKeys.Length); encoded = rootState.OrderHead; previous = 0;
                    for (int i = 0; i < rootKeys.Length; i++)
                    {
                        Assert.Greater(encoded, 0);
                        OrderedHashEntry<int, T> entry;
                        if (Backend == Backend.Managed) ManagedRoot.Entries.TryGet(encoded - 1, out entry).ThrowIfFailure();
                        else entry = NativeRoot.Entries.ReadSnapshot(NativeRoot.EntrySlot, encoded - 1);
                        Assert.AreEqual(rootKeys[i], entry.Key); Assert.AreEqual(1, entry.Active); Assert.AreEqual(previous, entry.OrderPrevious);
                        digest = Mix(digest, (uint)entry.Key); digest = VerifyPayload(entry.Value, rootKeys[i], 0, digest);
                        previous = encoded; encoded = entry.OrderNext;
                    }
                    Assert.AreEqual(0, encoded); Assert.AreEqual(previous, rootState.OrderTail);
                }
                return digest;
            }
            internal Diagnostics Diagnostics()
            {
                var d = Stats(Memory); d.nativeMetadataBytes = Backend == Backend.Managed ? -1 : NativeMetadata(Native.Entries) + NativeMetadata(Native.Buckets);
                d.baselineLivePayloadBytes = baseline.LivePayloadBytes; d.payloadGrowthBytes = d.livePayloadBytes - baseline.LivePayloadBytes;
                d.baselineLivePages = baseline.LivePages; d.pageGrowth = d.livePages - baseline.LivePages;
                d.activeBefore = countBefore; d.activeAfter = State.Count; d.retainedRoots = captured ? 1 : 0; d.payloadBytes = sizeof(T);
                d.primaryCapacityBefore = primaryBefore; d.primaryCapacityAfter = Capacity; d.secondaryCapacityBefore = secondaryBefore; d.secondaryCapacityAfter = BucketCapacity;
                d.usedBefore = usedBefore; d.usedAfter = State.Used;
                d.fixtureNativeBytes = (long)(Inputs.Length + Statuses.Length + Marker.Length + OutputKeys.Length) * sizeof(int) + (long)(Values.Length + OutputValues.Length) * sizeof(T);
                return d;
            }
            public void Dispose()
            {
                if (captured) { if (Backend == Backend.Managed) ManagedRoot.Dispose(); else NativeRoot.Dispose(); }
                if (Managed != null) Managed.Dispose(); else if (Native.IsCreated) Native.Dispose();
                Inputs.Dispose(); Values.Dispose(); Statuses.Dispose(); Marker.Dispose(); OutputKeys.Dispose(); OutputValues.Dispose();
            }
        }

        private sealed class SparseOwner<T> : IDisposable where T : unmanaged, IPayload
        {
            internal readonly Backend Backend;
            internal DeterministicSparseSet<T> Managed;
            internal NativeDeterministicSparseSet<T> Native;
            internal ManagedSparseSetSnapshot<T> ManagedRoot;
            internal NativeSparseSetSnapshot<T> NativeRoot;
            internal NativeArray<StableHandle> Inputs, OutputHandles;
            internal NativeArray<int> Statuses, Marker;
            internal NativeArray<T> Values, OutputValues;
            private StableHandle[] rootHandles;
            private GraphMemoryStats baseline;
            private int primaryBefore, secondaryBefore, countBefore;
            internal SparseOwner(Backend backend, BufferOptions dense, BufferOptions sparse, int calls, int outputs = 0)
            {
                Backend = backend;
                if (backend == Backend.Managed) Managed = new DeterministicSparseSet<T>(dense, sparse);
                else Native = new NativeDeterministicSparseSet<T>(dense, sparse, Allocator.Persistent);
                Inputs = new NativeArray<StableHandle>(calls, Allocator.Persistent); Values = new NativeArray<T>(calls, Allocator.Persistent);
                Statuses = new NativeArray<int>(calls, Allocator.Persistent); Marker = new NativeArray<int>(1, Allocator.Persistent);
                OutputHandles = new NativeArray<StableHandle>(outputs, Allocator.Persistent); OutputValues = new NativeArray<T>(outputs, Allocator.Persistent);
            }
            internal int Count => Backend == Backend.Managed ? Managed.Count : Native.Count;
            internal int Capacity => Backend == Backend.Managed ? Managed.Capacity : Native.Capacity;
            internal int Slots => Backend == Backend.Managed ? Managed.EntitySlotCapacity : Native.EntitySlotCapacity;
            internal GraphMemoryStats Memory => Backend == Backend.Managed ? Managed.Dense.Owner.MemoryStats + Managed.Sparse.Owner.MemoryStats : Native.Dense.MemoryStats + Native.Sparse.MemoryStats;
            internal SparseSetEntry<T> Entry(int index) => Backend == Backend.Managed ? Managed.Dense.Read(index) : Native.Dense.Read(index);
            internal SparseSetIndex Index(int slot) => Backend == Backend.Managed ? Managed.Sparse.Read(slot) : Native.Sparse.Read(slot);
            internal void AddInitial(StableHandle handle)
            { var value = Payload<T>((int)handle.Index); (Backend == Backend.Managed ? Managed.TryAdd(handle, value) : Native.TryAdd(handle, value)).ThrowIfFailure(); }
            internal void CaptureBaseline(StableHandle[] handles, bool retain = true)
            {
                rootHandles = handles;
                if (retain)
                {
                    if (Backend == Backend.Managed) Managed.TryCapture(out ManagedRoot).ThrowIfFailure();
                    else Native.TryCapture(out NativeRoot).ThrowIfFailure();
                }
                baseline = Memory; primaryBefore = Capacity; secondaryBefore = Slots; countBefore = Count;
            }
            internal void Reset()
            { (Backend == Backend.Managed ? Managed.TryRestore(ManagedRoot) : Native.TryRestore(NativeRoot)).ThrowIfFailure(); }
            internal void CheckStatus(int expected)
            {
                if (Backend == Backend.BurstJob) CheckBurst(Marker[0]);
                for (int i = 0; i < Statuses.Length; i++) Assert.AreEqual(expected, Statuses[i], "Operation status " + i);
            }
            internal ulong Validate(StableHandle[] handles, bool retained = true)
            {
                Assert.AreEqual(handles.Length, Count); ulong digest = Mix(DigestSeed, (ulong)handles.Length);
                for (int i = 0; i < handles.Length; i++)
                {
                    var entry = Entry(i); Assert.AreEqual(handles[i], entry.Handle);
                    var cell = Index((int)handles[i].Index); Assert.AreEqual(i + 1, cell.DenseIndex); Assert.AreEqual(handles[i].Generation, cell.Generation);
                    digest = Mix(digest, handles[i].Packed); digest = VerifyPayload(entry.Value, (int)handles[i].Index, 0, digest);
                }
                if (retained)
                {
                    Assert.IsTrue(Backend == Backend.Managed ? ManagedRoot.IsCreated : NativeRoot.IsCreated);
                    Assert.AreEqual(rootHandles.Length, Backend == Backend.Managed ? ManagedRoot.Count : NativeRoot.Count);
                    digest = Mix(digest, (ulong)rootHandles.Length);
                    for (int i = 0; i < rootHandles.Length; i++)
                    {
                        T value; var status = Backend == Backend.Managed ? ManagedRoot.TryGet(rootHandles[i], out value) : NativeRoot.TryGet(rootHandles[i], out value);
                        status.ThrowIfFailure(); digest = Mix(digest, rootHandles[i].Packed); digest = VerifyPayload(value, (int)rootHandles[i].Index, 0, digest);
                    }
                }
                return digest;
            }
            internal Diagnostics Diagnostics()
            {
                var d = Stats(Memory); d.nativeMetadataBytes = Backend == Backend.Managed ? -1 : NativeMetadata(Native.Dense) + NativeMetadata(Native.Sparse);
                d.baselineLivePayloadBytes = baseline.LivePayloadBytes; d.payloadGrowthBytes = d.livePayloadBytes - baseline.LivePayloadBytes;
                d.baselineLivePages = baseline.LivePages; d.pageGrowth = d.livePages - baseline.LivePages;
                d.activeBefore = countBefore; d.activeAfter = Count; d.retainedRoots = d.retainedLeases / 2; d.payloadBytes = sizeof(T);
                d.primaryCapacityBefore = primaryBefore; d.primaryCapacityAfter = Capacity; d.secondaryCapacityBefore = secondaryBefore; d.secondaryCapacityAfter = Slots;
                d.fixtureNativeBytes = (long)(Inputs.Length + OutputHandles.Length) * sizeof(StableHandle) + (long)(Statuses.Length + Marker.Length) * sizeof(int) + (long)(Values.Length + OutputValues.Length) * sizeof(T);
                return d;
            }
            public void Dispose()
            {
                if (Backend == Backend.Managed) { if (ManagedRoot.IsCreated) ManagedRoot.Dispose(); Managed.Dispose(); }
                else { if (NativeRoot.IsCreated) NativeRoot.Dispose(); if (Native.IsCreated) Native.Dispose(); }
                Inputs.Dispose(); Values.Dispose(); Statuses.Dispose(); Marker.Dispose(); OutputHandles.Dispose(); OutputValues.Dispose();
            }
        }
    }
}

