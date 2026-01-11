namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Defines what to do in Reactive mode when a modifier is not eligible.
    /// </summary>
    public enum ReactiveIneligibleBehavior
    {
        /// <summary>
        /// Remove it if currently applied.
        /// </summary>
        RemoveWhenIneligible,

        /// <summary>
        /// Keep it applied even if it becomes ineligible (sticky behavior).
        /// </summary>
        KeepAppliedWhenIneligible
    }
}