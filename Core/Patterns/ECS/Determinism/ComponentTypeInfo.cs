using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Immutable metadata for a registered component type.
    /// </summary>
    public readonly struct ComponentTypeInfo
    {
        /// <summary>
        /// Assigned id (small and incremental within the world).
        /// </summary>
        public readonly ComponentTypeId Id;

        /// <summary>
        /// Flags describing how this component is stored and handled.
        /// </summary>
        public readonly ComponentTypeFlags Flags;

        /// <summary>
        /// Size in bytes for the component payload.
        /// For <see cref="ComponentTypeFlags.Tag"/>, this is 0.
        /// </summary>
        public readonly int SizeBytes;

        /// <summary>
        /// Stride in bytes for the component payload.
        /// For simple SoA arrays this is typically equal to <see cref="SizeBytes"/>.
        /// </summary>
        public readonly int StrideBytes;

        /// <summary>
        /// Managed type reference (debug/tooling only; not part of deterministic state).
        /// </summary>
        public readonly Type ManagedType;

        /// <summary>
        /// Debug name (not part of deterministic state).
        /// </summary>
        public readonly string Name;

        internal ComponentTypeInfo(ComponentTypeId id, ComponentTypeFlags flags, int sizeBytes, int strideBytes,
            Type managedType, string name)
        {
            Id = id;
            Flags = flags;
            SizeBytes = sizeBytes;
            StrideBytes = strideBytes;
            ManagedType = managedType;
            Name = name;
        }

        public override string ToString()
        {
            return $"{Name} => {Id.Value} ({Flags}, size={SizeBytes}, stride={StrideBytes})";
        }
    }
}