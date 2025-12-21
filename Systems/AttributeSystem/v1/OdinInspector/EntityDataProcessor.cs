#if UNITY_EDITOR && ODIN_INSPECTOR
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools.AttributeSystem.OdinInspector
{
    public class EntityDataProcessor : OdinAttributeProcessor<EntityData>
    {
        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<System.Attribute> attributes)
        {
            if (member.Name == nameof(EntityData.attributes))
            {
                attributes.Add(new TableListAttribute());
                attributes.Add(new ShowInInspectorAttribute());
            }
        }
    }
}
#endif