using System.Collections.Generic;
using LegendaryTools.TagSystem;

namespace LegendaryTools.AttributeSystem
{
    public interface IEntity : ITaggable
    {
        public TagFilterMatch[] OnlyAcceptTags { get; }
        
        IReadOnlyList<Attribute> Attributes { get; }
        void AddModifiers(IEntity entitySource);
        void RemoveModifiers(IEntity entitySource);
        Attribute GetAttributeByID(AttributeConfig attributeConfig, bool emitErrorIfNotFound = true);
    }
}