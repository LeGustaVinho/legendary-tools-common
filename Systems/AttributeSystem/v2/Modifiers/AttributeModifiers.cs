namespace LegendaryTools.AttributeSystemV2
{
    public enum AttributeModifierConditionMode
    {
        None,
        ExpressionOnly,
        CodeOnly,
        ExpressionAndCode
    }

    /// <summary>
    /// How a modifier value is interpreted.
    /// Flat: added/subtracted directly.
    /// Factor: applied as a percentage (0.1 = +10%, -0.1 = -10%).
    /// </summary>
    public enum AttributeModifierValueKind
    {
        Flat,
        Factor
    }

    /// <summary>
    /// Behavior when applying a modifier to an entity that does not have
    /// the target attribute the modifier expects to change.
    /// </summary>
    public enum MissingTargetAttributeBehavior
    {
        /// <summary>
        /// Do nothing, silently ignore this modifier for this entity.
        /// </summary>
        Ignore,

        /// <summary>
        /// Create the target attribute on the entity using the definition default base value.
        /// </summary>
        CreateWithDefinitionDefault,

        /// <summary>
        /// Create the target attribute on the entity with a zero base value.
        /// </summary>
        CreateWithZero,

        /// <summary>
        /// Throw an exception when the target attribute is missing.
        /// </summary>
        Error
    }
}