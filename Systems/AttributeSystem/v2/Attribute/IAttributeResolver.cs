namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Interface used to resolve attribute instances by definition.
    /// </summary>
    public interface IAttributeResolver
    {
        AttributeInstance GetAttribute(AttributeDefinition definition);
    }
}