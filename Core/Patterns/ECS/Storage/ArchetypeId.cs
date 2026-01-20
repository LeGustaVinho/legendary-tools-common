namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Deterministic archetype identifier derived from its ordered component signature.
    /// </summary>
    public readonly struct ArchetypeId
    {
        /// <summary>
        /// Gets the raw 64-bit id value.
        /// </summary>
        public readonly ulong Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArchetypeId"/> struct.
        /// </summary>
        /// <param name="value">Raw id value.</param>
        public ArchetypeId(ulong value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public override string ToString() => $"ArchetypeId(0x{Value:X16})";
    }
}
