using System;
using System.Runtime.InteropServices;

namespace LegendaryTools.Deterministic
{
    /// <summary>Host-owned bounded memory. Borrowed pointers must not outlive their owner or outstanding jobs.</summary>
    public unsafe interface IDeterministicMemory : IDisposable
    {
        DeterministicBackend Backend { get; }
        byte* Pointer { get; }
        int Length { get; }
    }
    public interface IDeterministicMemoryFactory
    {
        DeterministicBackend Backend { get; }
        IDeterministicMemory Allocate(int bytes);
    }

    public sealed class ManagedDeterministicMemoryFactory : IDeterministicMemoryFactory
    {
        public static readonly ManagedDeterministicMemoryFactory Instance = new ManagedDeterministicMemoryFactory();
        private ManagedDeterministicMemoryFactory() { }
        public DeterministicBackend Backend => DeterministicBackend.Managed;
        public IDeterministicMemory Allocate(int bytes) => new ManagedDeterministicMemory(bytes);
    }
    internal sealed unsafe class ManagedDeterministicMemory : IDeterministicMemory
    {
        private GCHandle pin;
        private byte* pointer;
        public DeterministicBackend Backend => DeterministicBackend.Managed;
        public byte* Pointer => pointer;
        public int Length { get; }
        internal ManagedDeterministicMemory(int bytes)
        {
            if (bytes < 0 || bytes > int.MaxValue - 15) throw new ArgumentOutOfRangeException(nameof(bytes));
            Length = bytes; var storage = new byte[bytes + 15]; pin = GCHandle.Alloc(storage, GCHandleType.Pinned);
            pointer = (byte*)(((ulong)pin.AddrOfPinnedObject().ToPointer() + 15) & ~15UL);
        }
        public void Dispose() { pointer = null; if (pin.IsAllocated) pin.Free(); }
    }

    /// <summary>Explicit backend selection. Native support is supplied by the Unity integration; no fallback exists.</summary>
    public sealed class DeterministicRuntimeContext
    {
        public DeterministicBackend Backend { get; }
        public IDeterministicMemoryFactory Memory { get; }
        public DeterministicRuntimeContext(DeterministicBackend backend, IDeterministicMemoryFactory memory = null)
        {
            if (backend > DeterministicBackend.BurstCompiler) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            memory = memory ?? (backend == DeterministicBackend.Managed ? ManagedDeterministicMemoryFactory.Instance : null);
            if (memory == null) DeterministicStatus.BackendUnavailable.ThrowIfFailure();
            if (memory.Backend != backend) DeterministicStatus.ProfileMismatch.ThrowIfFailure();
            Backend = backend; Memory = memory;
        }
    }

    internal static unsafe class RuntimeBytes
    {
        internal static void Clear(byte* target, int bytes) => new Span<byte>(target, bytes).Clear();
        internal static void Copy(byte* from, byte* to, int bytes)
            => new ReadOnlySpan<byte>(from, bytes).CopyTo(new Span<byte>(to, bytes));
        internal static bool Equal(byte* a, byte* b, int bytes)
        {
            // Explicit byte loads keep arbitrary alignment portable and avoid per-element Span overhead in Mono.
            int i = 0;
            for (; i <= bytes - 8; i += 8)
                if (((a[i] ^ b[i]) | (a[i + 1] ^ b[i + 1]) | (a[i + 2] ^ b[i + 2]) | (a[i + 3] ^ b[i + 3]) |
                    (a[i + 4] ^ b[i + 4]) | (a[i + 5] ^ b[i + 5]) | (a[i + 6] ^ b[i + 6]) | (a[i + 7] ^ b[i + 7])) != 0) return false;
            for (; i < bytes; i++) if (a[i] != b[i]) return false;
            return true;
        }
        internal static bool Overlap(byte* a, int aBytes, byte* b, int bBytes)
        { return aBytes > 0 && bBytes > 0 && a < b + bBytes && b < a + aBytes; }
    }
}
