namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Default stacking policy: allow everything (AttributeInstance.AddModifier already prevents duplicate references).
    /// </summary>
    public sealed class DefaultModifierStackingPolicy : IModifierStackingPolicy
    {
        public static readonly DefaultModifierStackingPolicy Instance = new();

        private DefaultModifierStackingPolicy() { }

        public bool CanStack(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute)
        {
            return true;
        }
    }
}