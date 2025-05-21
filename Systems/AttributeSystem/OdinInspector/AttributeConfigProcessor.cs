#if UNITY_EDITOR && ODIN_INSPECTOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools.AttributeSystem.OdinInspector
{
    public class AttributeConfigProcessor : OdinAttributeProcessor<AttributeConfig>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<System.Attribute> attributes)
        {
            if (member.Name == nameof(AttributeConfig.Data))
            {
                attributes.Add(new InlinePropertyAttribute());
                attributes.Add(new HideLabelAttribute());
            }
        }
    }
}
#endif