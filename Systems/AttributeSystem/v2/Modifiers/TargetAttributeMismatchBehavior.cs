namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Behavior when attempting to apply a modifier to a specific attribute instance
    /// that is not the modifier's intended target.
    /// </summary>
    public enum TargetAttributeMismatchBehavior
    {
        /// <summary>
        /// Ignore silently.
        /// </summary>
        Ignore,

        /// <summary>
        /// Log a warning.
        /// </summary>
        Warn,

        /// <summary>
        /// Throw an exception.
        /// </summary>
        Error,

        /// <summary>
        /// Ignore the requested attribute and redirect to the modifier definition target attribute.
        /// </summary>
        RedirectToDefinitionTarget,

        /// <summary>
        /// Force-apply even if it targets a different attribute definition.
        /// </summary>
        ApplyAnyway
    }
}