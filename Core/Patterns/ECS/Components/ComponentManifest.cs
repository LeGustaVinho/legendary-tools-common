namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Determinism-friendly manifest of registered component types.
    /// This is intended for validation/handshake (e.g., lockstep peers) and debugging.
    /// </summary>
    public readonly struct ComponentManifest
    {
        public readonly int Count;
        public readonly ulong Xor64;
        public readonly ulong Sum64;

        public ComponentManifest(int count, ulong xor64, ulong sum64)
        {
            Count = count;
            Xor64 = xor64;
            Sum64 = sum64;
        }

        public override string ToString()
        {
            return $"ComponentManifest(Count={Count}, Xor64=0x{Xor64:X16}, Sum64=0x{Sum64:X16})";
        }
    }
}
