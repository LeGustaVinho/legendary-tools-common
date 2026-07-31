using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    [CreateAssetMenu(fileName = "New AttributeConfig", menuName = "Legendary Tools/Attribute System/V1/Attribute Config")]
    public class AttributeConfig : ScriptableObject
    {
        public AttributeData Data;
    }
}
