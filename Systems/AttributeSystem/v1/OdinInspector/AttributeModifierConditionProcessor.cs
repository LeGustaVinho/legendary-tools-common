#if UNITY_EDITOR && ODIN_INSPECTOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools.AttributeSystem.OdinInspector 
{
    public class AttributeModifierConditionProcessor : OdinAttributeProcessor<AttributeModifierCondition>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, 
            MemberInfo member, List<System.Attribute> attributes)
        {
            if (member.Name == nameof(AttributeModifierCondition.Value))
            {
                attributes.Add(new VerticalGroupAttribute("Value"));
                attributes.Add(new HideIfAttribute(nameof(AttributeModifierCondition.HasOptions)));
            }
            
            if (member.Name == nameof(AttributeModifierCondition.ValueAsOptionIndex))
            {
                attributes.Add(new VerticalGroupAttribute("Value"));
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new ShowIfAttribute(nameof(AttributeModifierCondition.HasOptionsAndIsNotFlags)));
                attributes.Add(new ValueDropdownAttribute(nameof(AttributeModifierCondition.EditorOptions)));
            }
            
            if (member.Name == nameof(AttributeModifierCondition.ValueAsOptionFlag))
            {
                attributes.Add(new VerticalGroupAttribute("Value"));
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new ShowIfAttribute(nameof(AttributeModifierCondition.OptionsAreFlags)));
                attributes.Add(new CustomValueDrawerAttribute("DrawValueAsOptionFlag"));
            }
        }
    }
}
#endif