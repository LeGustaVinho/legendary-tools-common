#if UNITY_EDITOR && ODIN_INSPECTOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools.AttributeSystem.OdinInspector
{
    public class AttributeProcessor : OdinAttributeProcessor<Attribute>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<System.Attribute> attributes)
        {
            if (member.Name == nameof(Attribute.Config))
            {
                attributes.Add(new InlineEditorAttribute());
            }

            if (member.Name == nameof(Attribute.Type) ||
                member.Name == nameof(Attribute.Flat) ||
                member.Name == nameof(Attribute.Factor) ||
                member.Name == nameof(Attribute.CurrentValue) ||
                member.Name == nameof(Attribute.Value) ||
                member.Name == nameof(Attribute.ValueAsOption) ||
                member.Name == nameof(Attribute.FlatAsOptionIndex) ||
                member.Name == nameof(Attribute.FlatAsOptionFlag) ||
                member.Name == nameof(Attribute.ValueAsOptionFlag) ||
                member.Name == nameof(Attribute.Parent)
                )
            {
                attributes.Add(new VerticalGroupAttribute("Value"));
                attributes.Add(new LabelWidthAttribute(37));
            }
            
            if (member.Name == nameof(Attribute.ForceApplyIfMissing))
            {
                attributes.Add(new ShowIfAttribute(nameof(Attribute.Type), AttributeType.Modifier));
                attributes.Add(new HorizontalGroupAttribute("Value/ValueFactor"));
            }
            
            if (member.Name == nameof(Attribute.Propagation))
            {
                attributes.Add(new ShowIfAttribute(nameof(Attribute.Type), AttributeType.Modifier));
                attributes.Add(new HorizontalGroupAttribute("Value/ValueFactor", width: 250));
            }
            
            if (member.Name == nameof(Attribute.Flat) ||
                member.Name == nameof(Attribute.Factor))
            {
                attributes.Add(new HorizontalGroupAttribute("Value/ValueFactor", width: 100));
            }

            if (member.Name == nameof(Attribute.FlatAsOptionIndex))
            {
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new ValueDropdownAttribute(nameof(Attribute.EditorOptions)));
                attributes.Add(new ShowIfAttribute(nameof(Attribute.HasOptionsAndIsNotFlags)));
                attributes.Add(new HorizontalGroupAttribute("Value/ValueFactor"));
            }
            
            if (member.Name == nameof(Attribute.FlatAsOptionFlag))
            {
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new CustomValueDrawerAttribute("DrawFlatAsOptionFlag"));
                attributes.Add(new ShowIfAttribute(nameof(Attribute.OptionsAreFlags)));
            }
            
            if (member.Name == nameof(Attribute.Value))
            {
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new HorizontalGroupAttribute("Value/ModValue"));
            }
            
            if (member.Name == nameof(Attribute.ValueAsOption))
            {
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new ShowIfAttribute(nameof(Attribute.HasOptionsAndIsNotFlags)));
                attributes.Add(new HorizontalGroupAttribute("Value/ModValue"));
            }
            
            if (member.Name == nameof(Attribute.ValueAsOptionFlag))
            {
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new HideLabelAttribute());
                attributes.Add(new CustomValueDrawerAttribute("DrawFlatAsOptionFlag"));
                attributes.Add(new ShowIfAttribute(nameof(Attribute.OptionsAreFlags)));
                attributes.Add(new HorizontalGroupAttribute("Value/ModValue"));
            }
            
            if (member.Name == nameof(Attribute.CurrentValue))
            {
                attributes.Add(new ShowIfAttribute(nameof(Attribute.HasCapacity)));
                attributes.Add(new ShowInInspectorAttribute());
            }
            
            if (member.Name == nameof(Attribute.Parent))
            {
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new ShowIfAttribute(nameof(Attribute.HasParent)));
            }
            
            if (member.Name == nameof(Attribute.ModifierConditions))
            {
                attributes.Add(new VerticalGroupAttribute("Mods"));
                attributes.Add(new ShowIfAttribute(nameof(Attribute.Type), AttributeType.Modifier));
            }

            if (member.Name == nameof(Attribute.FlagOperator))
            {
                attributes.Add(new VerticalGroupAttribute("Mods"));
                attributes.Add(new ShowIfAttribute(nameof(Attribute.OptionsAreFlagsAndIsModifier)));
            }
            
            if (member.Name == nameof(Attribute.ModifierConditions))
            {
                attributes.Add(new TableListAttribute());
            }

            if (member.Name == nameof(Attribute.Modifiers))
            {
                attributes.Add(new VerticalGroupAttribute("Mods"));
                attributes.Add(new TableListAttribute());
                attributes.Add(new ShowInInspectorAttribute());
                attributes.Add(new ShowIfAttribute(nameof(Attribute.HasParent)));
            }
        }
    }
}
#endif