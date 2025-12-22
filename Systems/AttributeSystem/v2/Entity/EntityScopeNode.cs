using System;
using LegendaryTools.GraphV2;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Tree node that binds an Entity to a scope name.
    /// </summary>
    public sealed class EntityScopeNode : TreeNode
    {
        public Entity Entity { get; }
        public string ScopeName { get; }

        public EntityScopeNode(Entity entity, string scopeName)
        {
            Entity = entity ?? throw new ArgumentNullException(nameof(entity));
            ScopeName = string.IsNullOrWhiteSpace(scopeName)
                ? throw new ArgumentException("Scope name is required.", nameof(scopeName))
                : scopeName;
        }

        public override string ToString()
        {
            return $"{ScopeName} ({Entity.Name})";
        }
    }
}