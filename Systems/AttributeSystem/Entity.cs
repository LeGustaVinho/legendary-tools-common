using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LegendaryTools.GraphV2;
using LegendaryTools.TagSystem;
using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    [Serializable]
    public class Entity : MultiParentTreeNode, IEntity
    {
        /// <summary>
        /// Stores a reference to this entity's configuration. 
        /// We clone the config if it isn't flagged as a clone to avoid modifying the original asset at runtime.
        /// </summary>
        protected EntityConfig config;

        /// <summary>
        /// This dictionary is used to quickly find attributes by their AttributeConfig.
        /// </summary>
        private readonly Dictionary<AttributeConfig, Attribute> attributesLookup = new Dictionary<AttributeConfig, Attribute>();

        /// <summary>
        /// A cached read-only view of the attributes list. At runtime, you cannot directly add/remove items 
        /// from a ReadOnlyCollection, ensuring that AllAttributes is effectively "locked".
        /// </summary>
        private ReadOnlyCollection<Attribute> readOnlyAttributes;

        /// <summary>
        /// The config property. If the assigned config is not a clone, we clone it to avoid runtime side-effects 
        /// on the original asset.
        /// </summary>
        public virtual EntityConfig Config
        {
            get
            {
                if (config != null && !config.IsClone)
                {
                    config = config.Clone<EntityConfig>(this);
                }
                return config;
            }
            set => config = value.IsClone ? value : value.Clone<EntityConfig>(this);
        }

        /// <summary>
        /// Returns the tags associated with this entity.
        /// </summary>
        public Tag[] Tags => Config.Data.tags;

        /// <summary>
        /// Returns the TagFilterMatch[] that dictates which children this entity can accept.
        /// </summary>
        public TagFilterMatch[] OnlyAcceptTags => Config.Data.onlyAcceptTags;

        /// <summary>
        /// A read-only list of all attributes in this entity. You cannot modify this list at runtime directly,
        /// but it can be changed via the Inspector or via the AddAttribute/RemoveAttribute methods.
        /// </summary>
        public IReadOnlyList<Attribute> Attributes => readOnlyAttributes ??= Config.Data.attributes.AsReadOnly();

        /// <summary>
        /// The EntityManager that "owns" this entity.
        /// </summary>
        public EntityManager EntityManager { private set; get; }

        /// <summary>
        /// Initializes this entity with the provided EntityManager.
        /// </summary>
        public void Initialize(EntityManager entityManager)
        {
            EntityManager = entityManager;
            EntityManager.AddEntity(this);
        }

        /// <summary>
        /// Initializes this entity with the provided EntityManager and an existing EntityConfig.
        /// </summary>
        public void Initialize(EntityManager entityManager, EntityConfig entityConfig)
        {
            EntityManager = entityManager;
            EntityManager.AddEntity(this);
            Config = entityConfig;
        }

        /// <summary>
        /// Initializes this entity with the provided EntityManager and an EntityData. 
        /// Internally creates an EntityConfig for the data.
        /// </summary>
        public void Initialize(EntityManager entityManager, EntityData entityData)
        {
            EntityManager = entityManager;
            EntityManager.AddEntity(this);
            Config = AttributeSystemFactory.CreateEntityConfig(entityData);
        }

        /// <summary>
        /// Called when this Entity should be removed from the game or system.
        /// </summary>
        public void Destroy()
        {
            DisconnectFromParents();
            EntityManager.RemoveEntity(this);
        }

        /// <summary>
        /// Attempts to connect this entity as a child of <paramref name="parentEntity"/>.
        /// It then applies this child's modifiers based on each modifier's propagation setting.
        /// </summary>
        /// <returns>A tuple containing (success, connection) 
        /// where 'success' is true if the connection is allowed by the parent's OnlyAcceptTags.</returns>
        public (bool, INodeConnection) TryToApplyTo(Entity parentEntity)
        {
            // Check if this entity matches the parent's OnlyAcceptTags rules
            foreach (TagFilterMatch tagFilterMatch in parentEntity.OnlyAcceptTags)
            {
                if (!tagFilterMatch.Match(this))
                    return (false, null);
            }

            // Make the connection to the parent
            INodeConnection connection = ConnectToParent(parentEntity);

            // Apply our child's modifiers to the parent entity and/or the parent's children
            parentEntity.ApplyChildModifiers(this);

            return (true, connection);
        }

        /// <summary>
        /// Called when removing the connection from *all* parents of this Entity.
        /// We remove any modifiers we had applied to those parents or their children (Child propagation).
        /// </summary>
        public override void DisconnectFromParents()
        {
            foreach (IMultiParentTreeNode parentNode in ParentNodes)
            {
                if (parentNode is Entity parentEntity)
                {
                    parentEntity.RemoveChildModifiers(this);
                }
            }

            base.DisconnectFromParents();
        }

        /// <summary>
        /// Called when removing the connection from a *single* parent of this Entity.
        /// We remove any modifiers we had applied to that parent or its children (Child propagation).
        /// </summary>
        public void DisconnectFromParent(Entity parentNode)
        {
            parentNode.RemoveChildModifiers(this);
            base.DisconnectFromParent(parentNode);
        }

        /// <summary>
        /// Retrieves an Attribute from this entity by its AttributeConfig, or returns null if not found.
        /// By default, logs an error if not found; you can disable that via <paramref name="emitErrorIfNotFound"/>.
        /// </summary>
        public Attribute GetAttributeByID(AttributeConfig attributeConfig, bool emitErrorIfNotFound = true)
        {
            if (attributeConfig == null)
            {
                Debug.LogWarning("[Entity:GetAttributeByID] attributeConfig is null.");
                return null;
            }

            // Attempt to retrieve from the lookup dictionary first
            if (!attributesLookup.TryGetValue(attributeConfig, out Attribute attribute))
            {
                // If not found in the dictionary, find it in our attributes
                attribute = Attributes.FirstOrDefault(item => item.Config == attributeConfig);
                if (attribute != null)
                {
                    attributesLookup.Add(attributeConfig, attribute);
                }
                else
                {
                    if (emitErrorIfNotFound)
                        Debug.LogWarning($"[Entity:GetAttributeByID({attributeConfig.name})] -> Not found.");
                }
            }
            return attribute;
        }

        /// <summary>
        /// Checks if this Entity has the specified Tag.
        /// </summary>
        public bool ContainsTag(Tag tag)
        {
            if (tag == null) return false;
            return Tags.Contains(tag);
        }

        /// <summary>
        /// Adds *all* modifier attributes from the given entity to this entity's matching attributes.
        /// This is typically used to "transfer" or "apply" all modifier Attributes from one entity to another.
        /// </summary>
        public void AddModifiers(IEntity entitySource)
        {
            // Collect only the modifier attributes from the source
            List<Attribute> allModifiers = entitySource.Attributes
                .Where(item => item.Type == AttributeType.Modifier)
                .ToList();

            foreach (Attribute modifier in allModifiers)
            {
                // Find or create the target attribute in our entity
                Attribute targetAttribute = GetAttributeByID(modifier.Config, !modifier.ForceApplyIfMissing);
                if (targetAttribute == null)
                {
                    if (modifier.ForceApplyIfMissing)
                    {
                        // Create a new attribute if forced
                        targetAttribute = new Attribute(this, modifier.Config)
                        {
                            Type = AttributeType.Attribute
                        };
                        // Instead of AllAttributes.Add(...), call our AddAttribute method
                        AddAttribute(targetAttribute);
                    }
                    else
                    {
                        // Otherwise, skip
                        continue;
                    }
                }

                targetAttribute.AddModifier(modifier);
            }
        }

        /// <summary>
        /// Removes from this entity all modifiers that originate from the given entity.
        /// It loops over every Attribute in this Entity and tells each to remove any modifiers 
        /// that came from the source entity.
        /// </summary>
        public void RemoveModifiers(IEntity entitySource)
        {
            foreach (Attribute attr in Attributes)
            {
                if (attr.Modifiers.Count > 0)
                {
                    attr.RemoveModifiers(entitySource);
                }
            }
        }

        /// <summary>
        /// Applies the "childEntity" modifiers to THIS entity and/or this entity's children,
        /// depending on each modifier's ModifierPropagation setting.
        /// </summary>
        private void ApplyChildModifiers(IEntity childEntity)
        {
            // Collect only the modifier attributes from the child entity
            IEnumerable<Attribute> childModifiers = childEntity.Attributes
                .Where(a => a.Type == AttributeType.Modifier);

            foreach (Attribute mod in childModifiers)
            {
                // We now check mod.Propagation (since it's stored on the Attribute, not on the config)
                switch (mod.Propagation)
                {
                    case ModifierPropagation.Parent:
                        // Only apply to THIS (the parent).
                        AddSingleModifierToThisEntity(mod);
                        break;

                    case ModifierPropagation.Child:
                        // Only apply to THIS entity's children (not "this" entity).
                        AddSingleModifierToChildren(mod, ChildNodes);
                        break;

                    case ModifierPropagation.Both:
                        // Apply to THIS entity and all its children.
                        AddSingleModifierToThisEntity(mod);
                        AddSingleModifierToChildren(mod, ChildNodes);
                        break;
                }
            }
        }

        /// <summary>
        /// Removes modifiers that "childEntity" had previously applied to this entity 
        /// and/or this entity's children, based on the modifiers' propagation.
        /// </summary>
        private void RemoveChildModifiers(IEntity childEntity)
        {
            IEnumerable<Attribute> childModifiers = childEntity.Attributes
                .Where(a => a.Type == AttributeType.Modifier);

            foreach (Attribute mod in childModifiers)
            {
                switch (mod.Propagation)
                {
                    case ModifierPropagation.Parent:
                        RemoveSingleModifierFromThisEntity(mod);
                        break;

                    case ModifierPropagation.Child:
                        RemoveSingleModifierFromChildren(mod, ChildNodes);
                        break;

                    case ModifierPropagation.Both:
                        RemoveSingleModifierFromThisEntity(mod);
                        RemoveSingleModifierFromChildren(mod, ChildNodes);
                        break;
                }
            }
        }

        /// <summary>
        /// Official way to add a brand new Attribute to this entity at runtime.
        /// This sets the attribute's Parent to this Entity and updates the dictionary.
        /// </summary>
        public Attribute AddAttribute(Attribute attribute)
        {
            if (attribute == null) return null;

            // Ensure the Attribute knows its parent
            attribute.Parent = this;

            // Add to the data list
            Config.Data.attributes.Add(attribute);

            // Also store/update in the lookup
            attributesLookup[attribute.Config] = attribute;

            // If we had a read-only wrapper, re-create it so it reflects new items
            readOnlyAttributes = null;

            return attribute;
        }

        /// <summary>
        /// Official way to remove an existing Attribute from this entity at runtime.
        /// This unsets the attribute's Parent and updates the dictionary.
        /// </summary>
        public bool RemoveAttribute(Attribute attribute)
        {
            if (attribute == null) return false;

            bool removed = Config.Data.attributes.Remove(attribute);
            if (removed)
            {
                // Unset the parent
                attribute.Parent = null;

                // Remove from our lookup if it references this exact Attribute
                if (attributesLookup.TryGetValue(attribute.Config, out Attribute existing) && existing == attribute)
                {
                    attributesLookup.Remove(attribute.Config);
                }

                // Refresh read-only collection
                readOnlyAttributes = null;
            }

            return removed;
        }

        /// <summary>
        /// Helper function that adds a single modifier to this entity's matching attribute (or creates one if forced).
        /// </summary>
        private void AddSingleModifierToThisEntity(Attribute mod)
        {
            // Look for an existing attribute with the same config
            Attribute targetAttribute = GetAttributeByID(mod.Config, !mod.ForceApplyIfMissing);

            // If not found, and forced, create one
            if (targetAttribute == null)
            {
                if (mod.ForceApplyIfMissing)
                {
                    targetAttribute = new Attribute(this, mod.Config)
                    {
                        Type = AttributeType.Attribute
                    };
                    AddAttribute(targetAttribute);
                }
                else
                {
                    return;
                }
            }

            // Now apply the modifier
            targetAttribute.AddModifier(mod);
        }

        /// <summary>
        /// Helper function that removes a single modifier from this entity's matching attribute, if it exists.
        /// </summary>
        private void RemoveSingleModifierFromThisEntity(Attribute mod)
        {
            Attribute targetAttribute = GetAttributeByID(mod.Config, false);
            targetAttribute?.RemoveModifier(mod);
        }

        /// <summary>
        /// Recursively applies a single modifier to all child entities.
        /// </summary>
        private void AddSingleModifierToChildren(Attribute mod, List<IMultiParentTreeNode> children)
        {
            Queue<IMultiParentTreeNode> queue = new Queue<IMultiParentTreeNode>(children);

            while (queue.Count > 0)
            {
                IMultiParentTreeNode node = queue.Dequeue();
                if (node is Entity childEntity)
                {
                    Attribute targetAttribute = childEntity.GetAttributeByID(mod.Config, !mod.ForceApplyIfMissing);

                    if (targetAttribute == null)
                    {
                        if (mod.ForceApplyIfMissing)
                        {
                            targetAttribute = new Attribute(childEntity, mod.Config)
                            {
                                Type = AttributeType.Attribute
                            };
                            childEntity.AddAttribute(targetAttribute);
                        }
                        else
                        {
                            continue;
                        }
                    }

                    targetAttribute.AddModifier(mod);

                    // Enqueue the child's children
                    foreach (IMultiParentTreeNode grandchild in childEntity.ChildNodes)
                    {
                        queue.Enqueue(grandchild);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively removes a single modifier from all child entities.
        /// </summary>
        private void RemoveSingleModifierFromChildren(Attribute mod, List<IMultiParentTreeNode> children)
        {
            Queue<IMultiParentTreeNode> queue = new Queue<IMultiParentTreeNode>(children);

            while (queue.Count > 0)
            {
                IMultiParentTreeNode node = queue.Dequeue();
                if (node is Entity childEntity)
                {
                    Attribute targetAttribute = childEntity.GetAttributeByID(mod.Config, false);
                    targetAttribute?.RemoveModifier(mod);

                    foreach (IMultiParentTreeNode grandchild in childEntity.ChildNodes)
                    {
                        queue.Enqueue(grandchild);
                    }
                }
            }
        }
    }
}
