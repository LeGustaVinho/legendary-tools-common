using System.Collections.Generic;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Abstraction point for modifier ordering (priority/sorting) to be implemented later.
    /// </summary>
    public interface IModifierSortPolicy
    {
        void Sort(List<AttributeInstance> modifiers);
    }
}