using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Single attribute entry used inside an EntityDefinition.
    /// </summary>
    [Serializable]
    public class AttributeEntry
    {
        public AttributeDefinition definition;
        public AttributeValue baseValue;

        public void ResetToDefinitionDefault()
        {
            if (definition != null) baseValue = definition.GetDefaultBaseValue();
        }
    }
}