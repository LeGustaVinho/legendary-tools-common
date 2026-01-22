namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Cached handle for a component type to avoid repeated type registry lookups in hot paths.
    /// Systems should store this handle and reuse it.
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    public readonly struct ComponentHandle<T> where T : struct
    {
        /// <summary>
        /// Internal numeric type id used by the storage.
        /// </summary>
        public readonly int TypeId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentHandle{T}"/> struct.
        /// </summary>
        /// <param name="typeId">The internal type ID.</param>
        internal ComponentHandle(int typeId)
        {
            TypeId = typeId;
        }

        /// <summary>
        /// Returns a string representation of the component handle.
        /// </summary>
        /// <returns>A string containing the component type name and ID.</returns>
        public override string ToString()
        {
            return $"ComponentHandle<{typeof(T).Name}>(TypeId={TypeId})";
        }
    }
}