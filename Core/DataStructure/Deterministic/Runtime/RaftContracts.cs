using System;

namespace LegendaryTools.Deterministic
{
    public enum DeterministicRaftRole : byte { Learner, Follower, Candidate, Leader }
    public readonly struct DeterministicRaftOptions
    {
        public readonly int MaxPeers, LogCapacity, MaxCommandBytes, MaxSnapshotBytes;
        public readonly int HeartbeatMs, MinElectionMs, MaxElectionMs, QuorumTimeoutMs, MaxPacketsPerPump;
        public readonly int SnapshotWindowChunks, MembershipTimeoutMs;
        public DeterministicRaftOptions(int maxPeers, int logCapacity, int maxCommandBytes, int maxSnapshotBytes,
            int heartbeatMs = 50, int minElectionMs = 250, int maxElectionMs = 500,
            int quorumTimeoutMs = 350, int maxPacketsPerPump = 128, int snapshotWindowChunks = 8, int membershipTimeoutMs = 1500)
        {
            MaxPeers = maxPeers; LogCapacity = logCapacity; MaxCommandBytes = maxCommandBytes; MaxSnapshotBytes = maxSnapshotBytes;
            HeartbeatMs = heartbeatMs; MinElectionMs = minElectionMs; MaxElectionMs = maxElectionMs;
            QuorumTimeoutMs = quorumTimeoutMs; MaxPacketsPerPump = maxPacketsPerPump;
            SnapshotWindowChunks = snapshotWindowChunks; MembershipTimeoutMs = membershipTimeoutMs;
        }
        public bool IsValid => MaxPeers > 0 && MaxPeers <= 64 && LogCapacity >= 2 &&
            MaxCommandBytes >= MaxPeers * 32 + 2 && MaxSnapshotBytes >= MaxCommandBytes + 64 &&
            HeartbeatMs > 0 && MinElectionMs > HeartbeatMs * 2L && MaxElectionMs >= MinElectionMs &&
            MaxElectionMs < int.MaxValue && QuorumTimeoutMs >= HeartbeatMs * 2L && MaxPacketsPerPump > 0 &&
            SnapshotWindowChunks > 0 && SnapshotWindowChunks <= 64 && (MembershipTimeoutMs == 0 || MembershipTimeoutMs >= QuorumTimeoutMs);
    }
    public unsafe interface IDeterministicRaftStateMachine
    {
        DeterministicStatus TryApply(ulong index, ulong term, ReadOnlySpan<byte> command);
        DeterministicStatus TryWriteSnapshot(ref DeterministicWriter writer);
        DeterministicStatus TryValidateSnapshot(ref DeterministicReader reader);
        DeterministicStatus TryInstallSnapshot(ref DeterministicReader reader);
    }
    public interface IDeterministicRaftApplicationMessages
    { DeterministicStatus TryHandle(PeerId sender, byte kind, ReadOnlySpan<byte> payload); }
    public readonly struct DeterministicRaftLogInfo
    {
        public readonly ulong Index, Term;
        public readonly int Bytes;
        public readonly bool IsCommand;
        internal DeterministicRaftLogInfo(ulong index, ulong term, int bytes, bool command) { Index = index; Term = term; Bytes = bytes; IsCommand = command; }
    }
    public static class DeterministicQuorum
    {
        public static int Count(ulong mask) { int count = 0; while (mask != 0) { mask &= mask - 1; count++; } return count; }
        public static bool HasMajority(ulong acknowledgements, ulong voters)
            => voters != 0 && Count(acknowledgements & voters) > Count(voters) / 2;
        public static bool HasQuorum(ulong acknowledgements, ulong oldVoters, ulong newVoters = 0)
            => HasMajority(acknowledgements, oldVoters) && (newVoters == 0 || HasMajority(acknowledgements, newVoters));
    }
}
