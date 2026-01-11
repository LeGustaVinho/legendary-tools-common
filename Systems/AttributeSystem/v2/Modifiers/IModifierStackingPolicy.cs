namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Abstraction point for stacking rules to be implemented later.
    /// </summary>
    public interface IModifierStackingPolicy
    {
        /// <summary>
        /// Returns true if this modifier instance is allowed to be added to the target attribute right now.
        /// </summary>
        bool CanStack(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute);
    }
}