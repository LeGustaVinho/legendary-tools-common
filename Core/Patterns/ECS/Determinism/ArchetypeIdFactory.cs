#nullable enable

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Creates deterministic <see cref="ArchetypeId"/> values from a <see cref="ComponentTypeSet"/> signature.
    /// </summary>
    public static class ArchetypeIdFactory
    {
        /// <summary>
        /// Computes an <see cref="ArchetypeId"/> from the set's stable 64-bit hash.
        /// </summary>
        public static ArchetypeId FromSignature(in ComponentTypeSet signature)
        {
            ulong h = signature.GetStableHash64();
            return new ArchetypeId(h);
        }
    }
}