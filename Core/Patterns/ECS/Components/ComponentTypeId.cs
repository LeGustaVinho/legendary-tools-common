namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Stable (within a World instance) small identifier for a component type.
    /// </summary>
    public readonly struct ComponentTypeId
    {
        /// <summary>
        /// Gets the raw numeric value of the identifier.
        /// </summary>
        public readonly int Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentTypeId"/> struct.
        /// </summary>
        /// <param name="value">Raw identifier value.</param>
        public ComponentTypeId(int value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ComponentTypeId({Value})";
        }
    }
}