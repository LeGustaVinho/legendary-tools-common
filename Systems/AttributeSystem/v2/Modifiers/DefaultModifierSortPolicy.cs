using System.Collections.Generic;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Default sort policy: does not reorder (stable insertion order).
    /// </summary>
    public sealed class DefaultModifierSortPolicy : IModifierSortPolicy
    {
        public static readonly DefaultModifierSortPolicy Instance = new();

        private DefaultModifierSortPolicy() { }

        public void Sort(List<AttributeInstance> modifiers)
        {
            // Intentionally no-op.
        }
    }
}