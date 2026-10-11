using System;

namespace LegendaryTools.Deterministic
{
    public readonly struct DeterministicSessionId : IEquatable<DeterministicSessionId>
    {
        public readonly ulong Value;
        public DeterministicSessionId(ulong value) { Value = value; }
        public bool IsValid => Value != 0;
        public bool Equals(DeterministicSessionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is DeterministicSessionId other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
    }
    public readonly struct DeterministicMessageHeader
    {
        public readonly DeterministicSessionId Session;
        public readonly ulong Fingerprint, Sequence;
        public readonly PeerId Sender, Destination;
        public readonly byte Kind;
        public DeterministicMessageHeader(DeterministicSessionId session, ulong fingerprint, PeerId sender, PeerId destination, ulong sequence, byte kind)
        { Session = session; Fingerprint = fingerprint; Sender = sender; Destination = destination; Sequence = sequence; Kind = kind; }
    }
    public static unsafe class DeterministicSessionProtocol
    {
        public const int HeaderBytes = 67, ChecksumBytes = 8, Overhead = HeaderBytes + ChecksumBytes;
        private const uint Magic = 0x3150444cu;
        public static DeterministicStatus TryWrite(ref DeterministicWriter writer, in DeterministicMessageHeader header, ReadOnlySpan<byte> payload)
        {
            if (!header.Session.IsValid || header.Fingerprint == 0 || !header.Sender.IsValid || !header.Destination.IsValid || header.Kind == 0) return DeterministicStatus.InvalidConfiguration;
            if (payload.Length > int.MaxValue - Overhead || !writer.Has(payload.Length + Overhead)) return DeterministicStatus.BufferTooSmall;
            if (!writer.IsMeasuring) fixed (byte* bytes = payload)
            {
                byte* output = writer.Buffer + writer.Position;
                if (payload.Length > 0 && (ulong)bytes < (ulong)output + (uint)(payload.Length + Overhead) &&
                    (ulong)output < (ulong)bytes + (uint)payload.Length) return DeterministicStatus.InvalidConfiguration;
            }
            var trial = writer; int start = trial.Position;
            trial.TryUInt(Magic, 4); trial.TryUInt(DeterministicCompatibilityProfile.ProtocolVersion, 2);
            trial.TryUInt(header.Session.Value, 8); trial.TryUInt(header.Fingerprint, 8);
            WritePeer(ref trial, header.Sender); WritePeer(ref trial, header.Destination);
            trial.TryUInt(header.Sequence, 8); trial.TryUInt(header.Kind, 1); trial.TryUInt((uint)payload.Length, 4);
            fixed (byte* bytes = payload) trial.TryBytes(bytes, payload.Length);
            trial.TryUInt(trial.IsMeasuring ? 0 : DeterministicHash.Xxh64(trial.Buffer + start, HeaderBytes + payload.Length), ChecksumBytes);
            writer = trial; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRead(ref DeterministicReader reader, DeterministicSessionId session, ulong fingerprint,
            PeerId local, PeerId transportSender, out DeterministicMessageHeader header, out ReadOnlySpan<byte> payload)
        {
            header = default; payload = default; var trial = reader; int start = trial.Position;
            if (!trial.Has(HeaderBytes)) return DeterministicStatus.BufferTooSmall;
            trial.TryUInt(4, out ulong magic); trial.TryUInt(2, out ulong version); trial.TryUInt(8, out ulong sessionId); trial.TryUInt(8, out ulong profile);
            ReadPeer(ref trial, out var sender); ReadPeer(ref trial, out var destination); trial.TryUInt(8, out ulong sequence); trial.TryUInt(1, out ulong kind); trial.TryUInt(4, out ulong length);
            if (magic != Magic || version != DeterministicCompatibilityProfile.ProtocolVersion || kind == 0 || length > int.MaxValue - Overhead) return DeterministicStatus.InvalidEncoding;
            if (sessionId != session.Value || profile != fingerprint || sender != transportSender || destination != local) return DeterministicStatus.ProfileMismatch;
            if (!sender.IsValid || !destination.IsValid) return DeterministicStatus.InvalidEncoding;
            if (!trial.Has((int)length + ChecksumBytes)) return DeterministicStatus.BufferTooSmall;
            payload = new ReadOnlySpan<byte>(trial.Buffer + trial.Position, (int)length); trial.Position += (int)length;
            trial.TryUInt(ChecksumBytes, out ulong checksum);
            if (DeterministicHash.Xxh64(trial.Buffer + start, HeaderBytes + (int)length) != checksum) { payload = default; return DeterministicStatus.InvalidEncoding; }
            header = new DeterministicMessageHeader(session, profile, sender, destination, sequence, (byte)kind); reader = trial; return DeterministicStatus.None;
        }
        public static void WritePeer(ref DeterministicWriter writer, PeerId peer) { writer.TryUInt(peer.Player.Value, 8); writer.TryUInt(peer.Incarnation, 8); }
        public static void ReadPeer(ref DeterministicReader reader, out PeerId peer)
        { reader.TryUInt(8, out ulong player); reader.TryUInt(8, out ulong incarnation); peer = new PeerId(new PlayerId(player), incarnation); }
    }
}
