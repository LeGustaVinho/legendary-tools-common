using System;

namespace LegendaryTools.Deterministic
{
    public enum PredictionPolicy : byte { HoldLast, Neutral }
    public enum DeterministicRunnerMode : byte { Rollback, Lockstep }
    public enum DeterministicSessionTopology : byte { AuthoritativeHost, PeerToPeer }

    /// <summary>Frozen simulation and retention limits. All capacities must be explicit.</summary>
    public readonly struct DeterministicRuntimeOptions
    {
        public readonly int MaxPlayers, HistoryTicks, PredictionTicks, InputDelay, TickRate, KeyframeInterval, PageBytes;
        public readonly SnapshotPolicy Snapshots;
        public readonly PredictionPolicy Prediction;
        public DeterministicRuntimeOptions(int maxPlayers, PredictionPolicy prediction,
            SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int tickRate = 60,
            int inputDelay = 2, int historyTicks = 120, int predictionTicks = 8,
            int keyframeInterval = 16, int pageBytes = 4096)
        {
            MaxPlayers = maxPlayers; Prediction = prediction; Snapshots = snapshots;
            TickRate = tickRate; InputDelay = inputDelay; HistoryTicks = historyTicks;
            PredictionTicks = predictionTicks; KeyframeInterval = keyframeInterval; PageBytes = pageBytes;
        }
        public bool IsValid => MaxPlayers > 0 && MaxPlayers <= 64 && TickRate > 0 && TickRate <= 1000 &&
            InputDelay >= 0 && HistoryTicks >= 2 && PredictionTicks >= 0 && PredictionTicks < HistoryTicks &&
            InputDelay < HistoryTicks && KeyframeInterval > 0 && KeyframeInterval <= HistoryTicks &&
            PageBytes >= 64 && (PageBytes & (PageBytes - 1)) == 0 &&
            Snapshots <= SnapshotPolicy.CopyOnWrite && Prediction <= PredictionPolicy.Neutral;
        public ulong Fingerprint
        {
            get
            {
                ulong hash = 0x4452544f505431UL;
                hash = DeterministicHash.Mix(hash ^ (uint)MaxPlayers);
                hash = DeterministicHash.Mix(hash ^ (uint)TickRate);
                hash = DeterministicHash.Mix(hash ^ (uint)InputDelay);
                hash = DeterministicHash.Mix(hash ^ (uint)PredictionTicks);
                return DeterministicHash.Mix(hash ^ (byte)Prediction);
            }
        }
    }

    /// <summary>Network compatibility excludes local storage/backend and retention policy.</summary>
    public readonly struct DeterministicCompatibilityProfile : IEquatable<DeterministicCompatibilityProfile>
    {
        public const ushort ProtocolVersion = 1;
        public readonly ulong Application, Numeric, Random, InputSchema, SimulationSettings;
        public DeterministicCompatibilityProfile(ulong application, ulong numeric, ulong random,
            ulong inputSchema, ulong simulationSettings)
        { Application = application; Numeric = numeric; Random = random; InputSchema = inputSchema; SimulationSettings = simulationSettings; }
        public bool IsValid => Application != 0 && InputSchema != 0 && SimulationSettings != 0;
        public ulong Fingerprint
        {
            get
            {
                ulong hash = DeterministicHash.Mix(0x4c445250524f4631UL ^ ProtocolVersion);
                hash = DeterministicHash.Mix(hash ^ Application); hash = DeterministicHash.Mix(hash ^ Numeric);
                hash = DeterministicHash.Mix(hash ^ Random); hash = DeterministicHash.Mix(hash ^ InputSchema);
                return DeterministicHash.Mix(hash ^ SimulationSettings);
            }
        }
        public bool Equals(DeterministicCompatibilityProfile other) => Application == other.Application &&
            Numeric == other.Numeric && Random == other.Random && InputSchema == other.InputSchema &&
            SimulationSettings == other.SimulationSettings;
        public override bool Equals(object obj) => obj is DeterministicCompatibilityProfile other && Equals(other);
        public override int GetHashCode() => unchecked((int)Fingerprint);
    }

    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public readonly ulong Value;
        public PlayerId(ulong value) { Value = value; }
        public bool IsValid => Value != 0;
        public bool Equals(PlayerId other) => Value == other.Value;
        public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => unchecked((int)(Value ^ (Value >> 32)));
        public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);
        public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);
    }

    /// <summary>Process identity. A restarted PlayerId has a fresh incarnation and begins as a learner.</summary>
    public readonly struct PeerId : IEquatable<PeerId>, IComparable<PeerId>
    {
        public readonly PlayerId Player;
        public readonly ulong Incarnation;
        public PeerId(PlayerId player, ulong incarnation) { Player = player; Incarnation = incarnation; }
        public bool IsValid => Player.IsValid && Incarnation != 0;
        public bool Equals(PeerId other) => Player == other.Player && Incarnation == other.Incarnation;
        public int CompareTo(PeerId other) { int c = Player.CompareTo(other.Player); return c != 0 ? c : Incarnation.CompareTo(other.Incarnation); }
        public override bool Equals(object obj) => obj is PeerId other && Equals(other);
        public override int GetHashCode() => Player.GetHashCode() ^ unchecked((int)Incarnation);
        public static bool operator ==(PeerId a, PeerId b) => a.Equals(b);
        public static bool operator !=(PeerId a, PeerId b) => !a.Equals(b);
    }
}
