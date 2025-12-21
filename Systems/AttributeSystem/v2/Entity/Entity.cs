using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Runtime instance of an entity based on an EntityDefinition.
    /// Serializable and compatible with Unity, JSON and binary formats.
    /// </summary>
    [Serializable]
    public sealed class Entity : IAttributeResolver, ISerializationCallbackReceiver
    {
        [Serializable]
        private class AttributeRecord
        {
            public AttributeDefinition definition;
            public AttributeInstance instance;
        }

        [SerializeField] private string _name;

        [SerializeField] private EntityDefinition _definition;

        [SerializeField] private List<AttributeRecord> _serializedAttributes = new();

        [NonSerialized] private Dictionary<AttributeDefinition, AttributeInstance> _attributes = new();

        [field: NonSerialized] public event Action<Entity, AttributeInstance> AttributeAdded;

        /// <summary>
        /// Original definition (template) for this entity.
        /// </summary>
        public EntityDefinition Definition => _definition;

        /// <summary>
        /// Entity name. Defaults to Definition.entityName.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Initializes a new runtime entity from a definition.
        /// </summary>
        public Entity(EntityDefinition definition, string overrideName = null)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _name = string.IsNullOrEmpty(overrideName) ? definition.entityName : overrideName;

            BuildFromDefinition();
        }

        /// <summary>
        /// Builds attribute instances from the ScriptableObject definition.
        /// </summary>
        private void BuildFromDefinition()
        {
            _serializedAttributes ??= new List<AttributeRecord>();
            _serializedAttributes.Clear();
            _attributes = new Dictionary<AttributeDefinition, AttributeInstance>();

            foreach (AttributeEntry entry in _definition.attributes)
            {
                if (entry == null || entry.definition == null)
                    continue;

                AttributeInstance instance = new(this, entry.definition, entry.baseValue);
                AttributeRecord record = new()
                {
                    definition = entry.definition,
                    instance = instance
                };

                _serializedAttributes.Add(record);
                _attributes.Add(entry.definition, instance);

                instance.ValueChanged += OnAttributeValueChanged;
                AttributeAdded?.Invoke(this, instance);
            }
        }

        /// <summary>
        /// Called whenever any attribute value changes.
        /// Used to re-apply limits when using reference-based min/max.
        /// </summary>
        private void OnAttributeValueChanged(AttributeInstance source, AttributeValue oldValue, AttributeValue newValue)
        {
            AttributeDefinition sourceDef = source.Definition;
            if (sourceDef == null)
                return;

            foreach (AttributeInstance target in _attributes.Values)
            {
                AttributeDefinition def = target.Definition;
                if (def == null || def == sourceDef)
                    continue;

                bool usesAsMin = def.minMode == AttributeLimitMode.ReferenceAttribute &&
                                 def.minReference == sourceDef;

                bool usesAsMax = def.maxMode == AttributeLimitMode.ReferenceAttribute &&
                                 def.maxReference == sourceDef;

                if (!usesAsMin && !usesAsMax)
                    continue;

                // Only re-apply clamp on dependent attributes if clampMode includes ClampOnSet.
                if (def.clampMode == AttributeClampMode.ClampOnSet ||
                    def.clampMode == AttributeClampMode.ClampOnSetAndGet)
                    // Reapply SetBaseValue with the same raw base, but new limits.
                    target.SetBaseValue(target.BaseValue);
            }
        }

        /// <summary>
        /// Gets an attribute instance by its definition.
        /// </summary>
        public AttributeInstance GetAttribute(AttributeDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            _attributes.TryGetValue(definition, out AttributeInstance instance);
            return instance;
        }

        /// <summary>
        /// Gets all attribute instances.
        /// </summary>
        public IEnumerable<AttributeInstance> GetAllAttributes()
        {
            return _attributes.Values;
        }

        /// <summary>
        /// Adds a new attribute instance to this entity at runtime.
        /// If the attribute already exists, returns the existing instance.
        /// </summary>
        internal AttributeInstance AddAttributeInstance(AttributeDefinition definition, AttributeValue baseValue)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (_attributes.TryGetValue(definition, out AttributeInstance existing))
                return existing;

            AttributeInstance instance = new(this, definition, baseValue);
            _attributes.Add(definition, instance);

            AttributeAdded?.Invoke(this, instance);
            return instance;
        }

        public override string ToString()
        {
            return _name;
        }

        #region IAttributeResolver

        AttributeInstance IAttributeResolver.GetAttribute(AttributeDefinition definition)
        {
            return GetAttribute(definition);
        }

        #endregion

        #region ISerializationCallbackReceiver

        public void OnBeforeSerialize()
        {
            // Nothing special to do, list already mirrors dictionary.
        }

        public void OnAfterDeserialize()
        {
            RebuildLookup();
        }

        /// <summary>
        /// Rebuilds the dictionary and rebinds owners/events after deserialization.
        /// </summary>
        private void RebuildLookup()
        {
            _attributes = new Dictionary<AttributeDefinition, AttributeInstance>();

            if (_serializedAttributes == null)
                return;

            foreach (AttributeRecord record in _serializedAttributes)
            {
                if (record == null || record.definition == null || record.instance == null)
                    continue;

                record.instance.BindOwner(this);
                record.instance.ValueChanged += OnAttributeValueChanged;

                _attributes[record.definition] = record.instance;
                AttributeAdded?.Invoke(this, record.instance);
            }
        }

        #endregion
    }
}