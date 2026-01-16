using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Component type flags describing storage/serialization properties.
    /// </summary>
    [Flags]
    public enum ComponentTypeFlags : ushort
    {
        None = 0,

        /// <summary>
        /// Presence-only component. No per-entity payload stored.
        /// </summary>
        Tag = 1 << 0,

        /// <summary>
        /// Data component. Stored as SoA column(s) in chunks.
        /// </summary>
        Data = 1 << 1,

        /// <summary>
        /// Component contains no managed references (safe for raw memory copy).
        /// </summary>
        Blittable = 1 << 2
    }
}