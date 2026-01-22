namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Determinism-friendly manifest of registered component types.
    /// This is intended for validation/handshake (e.g., lockstep peers) and debugging.
    /// </summary>
    public readonly struct ComponentManifest
    {
        /// <summary>
        /// Total number of registered components.
        /// </summary>
        public readonly int Count;

        /// <summary>
        /// Global XOR hash of all component stable hashes. Order-independent.
        /// </summary>
        public readonly ulong Xor64;

        /// <summary>
        /// Global arithmetic sum of all component stable hashes. Order-independent.
        /// </summary>
        public readonly ulong Sum64;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentManifest"/> struct.
        /// </summary>
        /// <param name="count">Component count.</param>
        /// <param name="xor64">XOR checksum.</param>
        /// <param name="sum64">Sum checksum.</param>
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