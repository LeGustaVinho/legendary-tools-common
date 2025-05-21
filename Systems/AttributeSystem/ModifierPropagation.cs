namespace LegendaryTools.AttributeSystem
{
    /// <summary>
    /// Defines how modifiers propagate when an AttributeSystem is added as a child to another.
    /// </summary>
    public enum ModifierPropagation
    {
        /// <summary>
        /// Applies the modifiers of the child only to the parent.
        /// </summary>
        Parent,

        /// <summary>
        /// Applies the modifiers to all children of the parent recursively.
        /// </summary>
        Child,

        /// <summary>
        /// Applies the modifiers both to the parent and its children recursively.
        /// </summary>
        Both
    }
}