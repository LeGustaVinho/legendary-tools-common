namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Holds the replicated match seed used to derive all deterministic randomness.
    /// Store this in your simulation/world state and replicate from host/server.
    /// </summary>
    public readonly struct MatchSeed
    {
        /// <summary>64-bit match seed (replicated).</summary>
        public readonly ulong Value;

        public MatchSeed(ulong value)
        {
            Value = value;
        }
    }
}
