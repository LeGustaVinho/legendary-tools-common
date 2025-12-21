using LegendaryTools.TagSystem;
using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    public static class AttributeSystemFactory
    {
        // 1) Create an AttributeData (POCO) with some default values
        public static AttributeData CreateAttributeData(
            bool optionsAreFlags = false, 
            bool hasCapacity = false, 
            bool allowExceed = false,
            float minCapacity = 0,
            bool hasMinMax = false,
            Vector2 minMaxValue = default,
            float[] stackPenaults = null,
            params string[] options)
        {
            return new AttributeData
            {
                OptionsAreFlags = optionsAreFlags,
                Options = (options == null || options.Length == 0) ? null : options,
                HasCapacity = hasCapacity,
                AllowExceedCapacity = allowExceed,
                MinCapacity = minCapacity,
                HasMinMax = hasMinMax,
                MinMaxValue = minMaxValue,
                StackPenaults = stackPenaults
            };
        }

        // 2) Wrap that AttributeData inside an AttributeConfig ScriptableObject
        public static AttributeConfig CreateAttributeConfig(AttributeData data)
        {
            AttributeConfig config = ScriptableObject.CreateInstance<AttributeConfig>();
            config.Data = data;
            return config;
        }

        // 3) Create an EntityData (POCO)
        public static EntityData CreateEntityData(Tag[] tags = null, TagFilterMatch[] onlyAcceptTags = null)
        {
            return new EntityData
            {
                tags = tags ?? new Tag[0],
                onlyAcceptTags = onlyAcceptTags ?? new TagFilterMatch[0],
                attributes = new System.Collections.Generic.List<Attribute>()
            };
        }

        // 4) Wrap that EntityData inside an EntityConfig ScriptableObject
        public static EntityConfig CreateEntityConfig(EntityData data, string name = "EntityConfig")
        {
            EntityConfig config = ScriptableObject.CreateInstance<EntityConfig>();
            config.name = name;
            config.Data = data;
            return config;
        }
    }
}
