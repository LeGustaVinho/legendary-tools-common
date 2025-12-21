using System;
using System.Collections.Generic;
using LegendaryTools.TagSystem;

namespace LegendaryTools.AttributeSystem
{
    [Serializable]
    public class EntityData
    {
        public Tag[] tags;
        
        public TagFilterMatch[] onlyAcceptTags;
        
        public List<Attribute> attributes = new List<Attribute>();

        /// <summary>
        /// Faz uma cópia/clonagem do próprio EntityData, inclusive clonando seus atributos.
        /// </summary>
        public virtual EntityData Clone(IEntity parent)
        {
            EntityData clone = new EntityData();
            
            // Clona a lista de Tags
            if (tags != null)
            {
                clone.tags = new Tag[tags.Length];
                Array.Copy(tags, clone.tags, tags.Length);
            }
            else
            {
                clone.tags = new Tag[0];
            }

            // Clona a lista de TagFilterMatch
            if (onlyAcceptTags != null)
            {
                clone.onlyAcceptTags = new TagFilterMatch[onlyAcceptTags.Length];
                Array.Copy(onlyAcceptTags, clone.onlyAcceptTags, onlyAcceptTags.Length);
            }
            else
            {
                clone.onlyAcceptTags = new TagFilterMatch[0];
            }

            // Clona a lista de atributos
            clone.attributes = new List<Attribute>();
            foreach (Attribute attr in attributes)
            {
                clone.attributes.Add(attr.Clone(parent));
            }

            return clone;
        }
    }
}