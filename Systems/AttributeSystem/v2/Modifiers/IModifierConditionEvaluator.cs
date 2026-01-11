namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Abstraction point for conditional modifiers (expressions/code) to be implemented later.
    /// </summary>
    public interface IModifierConditionEvaluator
    {
        /// <summary>
        /// Returns true if this modifier should be applied right now.
        /// </summary>
        bool ShouldApply(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute);
    }
}