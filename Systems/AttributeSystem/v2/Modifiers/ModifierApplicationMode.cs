namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Defines how modifiers are applied between entities.
    /// </summary>
    public enum ModifierApplicationMode
    {
        /// <summary>
        /// Applies eligible modifiers once. No automatic re-check.
        /// </summary>
        Direct,

        /// <summary>
        /// Creates a live link that re-checks eligibility on state changes and applies/removes accordingly.
        /// </summary>
        Reactive
    }
}