using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Datagrams are copied before returning. Implementations may lose, duplicate or reorder them.</summary>
    public interface IDeterministicTransport : IDisposable
    {
        PeerId LocalPeer { get; }
        int MaxDatagramBytes { get; }
        DeterministicStatus TrySend(PeerId destination, ReadOnlySpan<byte> packet);
        DeterministicStatus TryReceive(Span<byte> destination, out PeerId sender, out int bytes);
    }
    public readonly struct LoopbackFaults
    {
        public readonly int MinDelayMs, MaxDelayMs, DropPermille, DuplicatePermille;
        public readonly ulong Seed;
        public LoopbackFaults(int minDelayMs = 0, int maxDelayMs = 0, int dropPermille = 0, int duplicatePermille = 0, ulong seed = 1)
        { MinDelayMs = minDelayMs; MaxDelayMs = maxDelayMs; DropPermille = dropPermille; DuplicatePermille = duplicatePermille; Seed = seed; }
        public bool IsValid => MinDelayMs >= 0 && MaxDelayMs >= MinDelayMs && MaxDelayMs < int.MaxValue &&
            DropPermille >= 0 && DropPermille <= 1000 && DuplicatePermille >= 0 && DuplicatePermille <= 1000;
    }
    /// <summary>Explicit monotonic clock and reproducible faults, isolated from gameplay random streams.</summary>
    public sealed unsafe class DeterministicLoopbackNetwork : IDisposable
    {
        private struct Packet { internal PeerId Sender, Destination; internal long Due; internal ulong Order; internal int Length; internal byte Used; }
        private struct EndpointSlot { internal PeerId Peer; internal byte Active; }
        private readonly IDeterministicMemory memory;
        private readonly int maxPeers, maxPackets, maxDatagram;
        private Packet* packets;
        private EndpointSlot* endpoints;
        private byte* links, data;
        private LoopbackFaults faults;
        private ulong random, sequence;
        private bool disposed;
        public long TimeMs { get; private set; }
        public int PendingPackets { get; private set; }
        public DeterministicLoopbackNetwork(DeterministicRuntimeContext context, int maxPeers, int packetCapacity,
            int maxDatagramBytes, LoopbackFaults faults = default)
        {
            if (context == null || maxPeers <= 0 || packetCapacity <= 0 || maxDatagramBytes < 128 || !faults.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.maxPeers = maxPeers; maxPackets = packetCapacity; maxDatagram = maxDatagramBytes;
            this.faults = faults; random = faults.Seed;
            long p = Align((long)sizeof(Packet) * packetCapacity), e = Align((long)sizeof(EndpointSlot) * maxPeers), l = Align((long)maxPeers * maxPeers);
            long bytes = p + e + l + (long)maxDatagram * packetCapacity;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes); packets = (Packet*)memory.Pointer;
            endpoints = (EndpointSlot*)(memory.Pointer + p); links = memory.Pointer + p + e; data = links + l;
        }
        private static long Align(long bytes) => (bytes + 15) & ~15L;
        private uint NextRandom()
        { random += 0x9e3779b97f4a7c15UL; return (uint)DeterministicHash.Mix(random); }
        private int Index(PeerId peer)
        { for (int i = 0; i < maxPeers; i++) if (endpoints[i].Active != 0 && endpoints[i].Peer == peer) return i; return -1; }
        public IDeterministicTransport CreateEndpoint(PeerId peer)
        {
            if (disposed) DeterministicStatus.Disposed.ThrowIfFailure();
            if (!peer.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            if (Index(peer) >= 0) DeterministicStatus.DuplicateKey.ThrowIfFailure();
            for (int slot = 0; slot < maxPeers; slot++) if (endpoints[slot].Active == 0)
            {
                endpoints[slot] = new EndpointSlot { Peer = peer, Active = 1 };
                for (int i = 0; i < maxPeers; i++) links[slot * maxPeers + i] = links[i * maxPeers + slot] = 1;
                return new Endpoint(this, peer);
            }
            DeterministicStatus.CapacityExceeded.ThrowIfFailure(); return null;
        }
        public DeterministicStatus TrySetConnection(PeerId a, PeerId b, bool connected, bool bidirectional = true)
        {
            if (disposed) return DeterministicStatus.Disposed;
            int x = Index(a), y = Index(b); if (x < 0 || y < 0) return DeterministicStatus.NotFound;
            links[x * maxPeers + y] = (byte)(connected ? 1 : 0);
            if (bidirectional) links[y * maxPeers + x] = (byte)(connected ? 1 : 0); return DeterministicStatus.None;
        }
        public DeterministicStatus TrySetFaults(LoopbackFaults value)
        { if (disposed) return DeterministicStatus.Disposed; if (!value.IsValid) return DeterministicStatus.InvalidConfiguration; faults = value; return DeterministicStatus.None; }
        public DeterministicStatus TryAdvanceTime(long nowMs)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (nowMs < TimeMs) return DeterministicStatus.OutOfRange; TimeMs = nowMs;
            for (int i = 0; i < maxPackets; i++) if (packets[i].Used != 0 && packets[i].Due <= TimeMs)
            {
                int from = Index(packets[i].Sender), to = Index(packets[i].Destination);
                if (from < 0 || to < 0 || links[from * maxPeers + to] == 0) Free(i);
            }
            return DeterministicStatus.None;
        }
        private void Free(int slot) { packets[slot].Used = 0; PendingPackets--; }
        private DeterministicStatus Send(PeerId from, PeerId to, ReadOnlySpan<byte> packet)
        {
            if (disposed) return DeterministicStatus.Disposed;
            int x = Index(from), y = Index(to); if (x < 0 || y < 0) return DeterministicStatus.NotFound;
            if (packet.Length > maxDatagram) return DeterministicStatus.BufferTooSmall;
            if (links[x * maxPeers + y] == 0 || NextRandom() % 1000 < (uint)faults.DropPermille) return DeterministicStatus.None;
            int copies = NextRandom() % 1000 < (uint)faults.DuplicatePermille ? 2 : 1;
            if (PendingPackets > maxPackets - copies) return DeterministicStatus.CapacityExceeded;
            for (int copy = 0; copy < copies; copy++)
            {
                int slot = 0; while (packets[slot].Used != 0) slot++;
                long delay = faults.MinDelayMs + NextRandom() % (uint)(faults.MaxDelayMs - faults.MinDelayMs + 1);
                long due = TimeMs > long.MaxValue - delay ? long.MaxValue : TimeMs + delay;
                packet.CopyTo(new Span<byte>(data + (long)slot * maxDatagram, packet.Length));
                packets[slot] = new Packet { Sender = from, Destination = to, Length = packet.Length, Due = due, Order = sequence++, Used = 1 }; PendingPackets++;
            }
            return DeterministicStatus.None;
        }
        private DeterministicStatus Receive(PeerId local, Span<byte> target, out PeerId sender, out int length)
        {
            sender = default; length = 0; if (disposed) return DeterministicStatus.Disposed;
            int at = Index(local); if (at < 0) return DeterministicStatus.Disposed;
            int best = -1;
            for (int i = 0; i < maxPackets; i++) if (packets[i].Used != 0 && packets[i].Destination == local && packets[i].Due <= TimeMs)
            {
                int from = Index(packets[i].Sender); if (from < 0 || links[from * maxPeers + at] == 0) { Free(i); continue; }
                if (best < 0 || packets[i].Due < packets[best].Due || (packets[i].Due == packets[best].Due && packets[i].Order < packets[best].Order)) best = i;
            }
            if (best < 0) return DeterministicStatus.Empty;
            if (target.Length < packets[best].Length) return DeterministicStatus.BufferTooSmall;
            length = packets[best].Length; sender = packets[best].Sender;
            new ReadOnlySpan<byte>(data + (long)best * maxDatagram, length).CopyTo(target); Free(best); return DeterministicStatus.None;
        }
        private void Remove(PeerId peer)
        {
            if (disposed) return; int slot = Index(peer); if (slot >= 0) endpoints[slot].Active = 0;
            for (int i = 0; i < maxPackets; i++) if (packets[i].Used != 0 && (packets[i].Sender == peer || packets[i].Destination == peer)) Free(i);
        }
        private sealed class Endpoint : IDeterministicTransport
        {
            private readonly DeterministicLoopbackNetwork network;
            private bool disposed;
            public PeerId LocalPeer { get; }
            public int MaxDatagramBytes => network.maxDatagram;
            internal Endpoint(DeterministicLoopbackNetwork network, PeerId peer) { this.network = network; LocalPeer = peer; }
            public DeterministicStatus TrySend(PeerId destination, ReadOnlySpan<byte> packet) => disposed ? DeterministicStatus.Disposed : network.Send(LocalPeer, destination, packet);
            public DeterministicStatus TryReceive(Span<byte> destination, out PeerId sender, out int bytes)
            { sender = default; bytes = 0; return disposed ? DeterministicStatus.Disposed : network.Receive(LocalPeer, destination, out sender, out bytes); }
            public void Dispose() { if (disposed) return; disposed = true; network.Remove(LocalPeer); }
        }
        public void Dispose() { if (disposed) return; disposed = true; memory.Dispose(); packets = null; endpoints = null; links = data = null; }
    }
}
