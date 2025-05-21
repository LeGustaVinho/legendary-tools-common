using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;
using LegendaryTools.TagSystem;

namespace LegendaryTools.AttributeSystem
{
    public class EntityManager : MultiParentTree
    {
        public readonly List<Entity> Entities = new List<Entity>();

        public EntityManager(Entity rootEntity)
        {
        }
        
        public void AddEntity(Entity entity)
        {
            Entities.Add(entity);
        }
        
        public void RemoveEntity(Entity entity)
        {
            Entities.Remove(entity);
        }

        public List<Entity> FindAll(Predicate<Entity> predicate)
        {
            return Entities.FindAll(predicate);
        }
        
        public List<Entity> FindAllByTag(Tag[] tags)
        {
            return Entities.FindAll(item => Tag.HasTags(tags, item));
        }
    }
}