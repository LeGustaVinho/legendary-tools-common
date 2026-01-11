namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Default evaluator: all modifiers are eligible (no conditions).
    /// </summary>
    public sealed class AlwaysTrueModifierConditionEvaluator : IModifierConditionEvaluator
    {
        public static readonly AlwaysTrueModifierConditionEvaluator Instance = new();

        private AlwaysTrueModifierConditionEvaluator() { }

        public bool ShouldApply(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute)
        {
            return true;
        }
    }
}