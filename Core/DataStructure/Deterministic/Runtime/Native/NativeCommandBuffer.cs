using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer]
    public unsafe struct NativeDeterministicCommandWriter<T> where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction] private DeterministicCommand<T>* m_Commands;
        [NativeDisableUnsafePtrRestriction] private int* m_Count;
        private int capacity;
        private uint producerId;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        internal NativeDeterministicCommandWriter(DeterministicCommandWriter<T> writer
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            , AtomicSafetyHandle safety
#endif
            )
        {
            m_Commands = writer.Commands; m_Count = writer.CountPointer;
            capacity = writer.Capacity; producerId = writer.ProducerId;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = safety;
#endif
        }
        public DeterministicStatus TryEnqueue(ushort phase, uint systemId, StableHandle entity, in T payload)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            var writer = new DeterministicCommandWriter<T>(m_Commands, m_Count, capacity, producerId);
            return writer.TryEnqueue(phase, systemId, entity, payload);
        }
    }

    /// <summary>Independent safety handles allow jobs on different producer partitions to run concurrently.</summary>
    public sealed class NativeDeterministicCommandBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly DeterministicCommandBuffer<T> buffer;
        private readonly uint[] producers;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly AtomicSafetyHandle[] safety;
#endif
        private bool disposed;
        public NativeDeterministicCommandBuffer(DeterministicRuntimeContext context, ReadOnlySpan<uint> producerIds, int capacityPerProducer)
        {
            if (context == null || context.Backend != DeterministicBackend.BurstCompiler)
                DeterministicStatus.ProfileMismatch.ThrowIfFailure();
            buffer = new DeterministicCommandBuffer<T>(context, producerIds, capacityPerProducer); producers = producerIds.ToArray();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            safety = new AtomicSafetyHandle[producers.Length];
            for (int i = 0; i < safety.Length; i++) safety[i] = AtomicSafetyHandle.Create();
#endif
        }
        public DeterministicStatus TryGetWriter(uint producerId, out NativeDeterministicCommandWriter<T> writer)
        {
            writer = default; if (disposed) return DeterministicStatus.Disposed;
            for (int i = 0; i < producers.Length; i++) if (producers[i] == producerId)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(safety[i]);
#endif
                var status = buffer.TryGetWriter(producerId, out var raw); if (!status.IsSuccess()) return status;
                writer = new NativeDeterministicCommandWriter<T>(raw
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    , safety[i]
#endif
                    ); return DeterministicStatus.None;
            }
            return DeterministicStatus.NotFound;
        }
        private void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            for (int i = 0; i < safety.Length; i++) AtomicSafetyHandle.CheckWriteAndThrow(safety[i]);
#endif
        }
        public DeterministicStatus TryMerge(out ReadOnlySpan<DeterministicCommand<T>> commands)
        { commands = default; if (disposed) return DeterministicStatus.Disposed; CheckWrite(); return buffer.TryMerge(out commands); }
        public DeterministicStatus TryReset()
        { if (disposed) return DeterministicStatus.Disposed; CheckWrite(); return buffer.TryReset(); }
        public void Dispose()
        {
            if (disposed) return; CheckWrite();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            for (int i = 0; i < safety.Length; i++) { AtomicSafetyHandle.CheckDeallocateAndThrow(safety[i]); AtomicSafetyHandle.Release(safety[i]); }
#endif
            buffer.Dispose(); disposed = true;
        }
    }
}
