using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    public sealed class NativeDeterministicMemoryFactory : IDeterministicMemoryFactory
    {
        private readonly Allocator allocator;
        public DeterministicBackend Backend => DeterministicBackend.BurstCompiler;
        /// <summary>Legacy calls require Burst. Explicit Native host execution may allocate with requireBurst=false.</summary>
        public NativeDeterministicMemoryFactory(Allocator allocator = Allocator.Persistent, bool requireBurst = true)
        {
            if (requireBurst && !BurstCompiler.IsEnabled) DeterministicStatus.BackendUnavailable.ThrowIfFailure();
            if (allocator != Allocator.Persistent && allocator != Allocator.TempJob)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.allocator = allocator;
        }
        public IDeterministicMemory Allocate(int bytes) => new NativeRuntimeMemory(bytes, allocator);
        private sealed unsafe class NativeRuntimeMemory : IDeterministicMemory
        {
            private byte* pointer;
            private readonly Allocator allocator;
            public DeterministicBackend Backend => DeterministicBackend.BurstCompiler;
            public byte* Pointer => pointer;
            public int Length { get; }
            internal NativeRuntimeMemory(int bytes, Allocator allocator)
            {
                if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
                Length = bytes; this.allocator = allocator;
                pointer = (byte*)UnsafeUtility.MallocTracked(Math.Max(1, bytes), 16, allocator, 1);
                if (pointer == null) throw new OutOfMemoryException();
                UnsafeUtility.MemClear(pointer, Math.Max(1, bytes));
            }
            public void Dispose()
            {
                if (pointer == null) return;
                UnsafeUtility.FreeTracked(pointer, allocator); pointer = null;
            }
        }
    }
}
