#if UNITY_EDITOR && ODIN_INSPECTOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools.AttributeSystem.OdinInspector 
{
    public class AttributeConditionProcessor : OdinAttributeProcessor<AttributeCondition>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<System.Attribute> attributes)
        {
            if (member.Name == nameof(AttributeCondition.Operator))
            {
                attributes.Add(new TableColumnWidthAttribute(130, false));
            }
            
            if (member.Name == nameof(AttributeCondition.ModApplicationConditions))
            {
                attributes.Add(new TableListAttribute());
            }
        }
    }
}
#endif