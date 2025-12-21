using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Runtime instance of an attribute for a particular entity.
    /// Serializable for Unity, JSON and binary formats.
    /// </summary>
    [Serializable]
    public sealed class AttributeInstance
    {
        [field: NonSerialized] public event Action<AttributeInstance, AttributeValue, AttributeValue> BaseValueChanged;

        [field: NonSerialized] public event Action<AttributeInstance, AttributeValue, AttributeValue> ValueChanged;

        [SerializeField] private AttributeDefinition _definition;

        [NonSerialized] private IAttributeResolver _owner;

        [SerializeField] private AttributeValue _baseValue;

        [SerializeField] private AttributeValue _value;

        private readonly List<AttributeInstance> _incomingModifiers = new();

        /// <summary>
        /// Definition used by this instance.
        /// </summary>
        public AttributeDefinition Definition => _definition;

        /// <summary>
        /// Resolver/owner used to resolve other attributes for limit references.
        /// Not serialized; must be re-bound after deserialization.
        /// </summary>
        public IAttributeResolver Owner => _owner;

        /// <summary>
        /// Base value before any modifiers.
        /// </summary>
        public AttributeValue BaseValue => _baseValue;

        /// <summary>
        /// Effective value. Depending on clamp mode, it may be clamped on read.
        /// </summary>
        public AttributeValue Value
        {
            get
            {
                if (Definition == null || Owner == null)
                    return _value;

                return Definition.ClampForGet(_value, Owner);
            }
            private set => _value = value;
        }


        /// <summary>
        /// Creates a new attribute instance with the given base value.
        /// </summary>
        public AttributeInstance(IAttributeResolver owner, AttributeDefinition definition, AttributeValue baseValue)
        {
            _owner = owner;
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            AttributeValue initial = _definition.ClampForSet(baseValue, _owner);
            _baseValue = initial;
            _value = initial;
        }

        /// <summary>
        /// Binds the owner after deserialization.
        /// </summary>
        public void BindOwner(IAttributeResolver owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets the base value and optionally reapplies limits.
        /// Used by commands and entity logic.
        /// </summary>
        internal void SetBaseValue(AttributeValue newBaseValue, bool reapplyLimits = true)
        {
            if (_definition != null && _owner != null && reapplyLimits)
                newBaseValue = _definition.ClampForSet(newBaseValue, _owner);

            AttributeValue oldBase = _baseValue;
            if (oldBase.Raw == newBaseValue.Raw)
                return;

            _baseValue = newBaseValue;
            BaseValueChanged?.Invoke(this, oldBase, newBaseValue);

            SetValueInternal(newBaseValue);
        }

        private void SetValueInternal(AttributeValue newValue)
        {
            AttributeValue oldValue = _value;
            if (oldValue.Raw == newValue.Raw)
                return;

            _value = newValue;
            ValueChanged?.Invoke(this, oldValue, newValue);
        }

        public override string ToString()
        {
            return $"{_definition?.displayName ?? "Unnamed"}: {Value.Raw}";
        }

        // =========================================================
        // Modifiers
        // =========================================================

        /// <summary>
        /// Adds a modifier attribute that affects this attribute.
        /// Does not validate that the modifier is intended for this attribute;
        /// that is handled by higher level systems.
        /// </summary>
        internal void AddModifier(AttributeInstance modifier)
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));

            if (!_incomingModifiers.Contains(modifier)) _incomingModifiers.Add(modifier);
        }

        /// <summary>
        /// Removes a previously added modifier.
        /// </summary>
        internal bool RemoveModifier(AttributeInstance modifier)
        {
            if (modifier == null)
                return false;

            return _incomingModifiers.Remove(modifier);
        }

        /// <summary>
        /// Removes all modifiers.
        /// </summary>
        internal void ClearModifiers()
        {
            _incomingModifiers.Clear();
        }

        /// <summary>
        /// Gets the effective value of this attribute by recursively applying all modifiers.
        /// This does NOT modify BaseValue or Value, it only calculates a result.
        /// </summary>
        public AttributeValue GetEffectiveValue()
        {
            HashSet<AttributeInstance> visited = new();
            return GetEffectiveValueInternal(visited);
        }

        private AttributeValue GetEffectiveValueInternal(HashSet<AttributeInstance> visited)
        {
            if (!visited.Add(this))
                // Cycle detected: return current Value to avoid infinite recursion.
                return Value;

            // Base numeric value (already clamped according to clamp rules).
            AttributeValue baseVal = Value;

            switch (Definition.kind)
            {
                case AttributeKind.Integer:
                    return GetEffectiveIntValue(baseVal, visited);

                case AttributeKind.Float:
                    return GetEffectiveFloatValue(baseVal, visited);

                case AttributeKind.Flags:
                    // For now, flags do not use numeric modifiers.
                    // This can be extended later to support flag-adding/removing modifiers.
                    return baseVal;

                default:
                    return baseVal;
            }
        }

        private AttributeValue GetEffectiveIntValue(AttributeValue baseVal, HashSet<AttributeInstance> visited)
        {
            long baseInt = baseVal.ToInt();
            double flat = 0.0;
            double factor = 0.0;

            foreach (AttributeInstance modifier in _incomingModifiers)
            {
                AttributeDefinition def = modifier.Definition;
                if (def == null || !def.isModifier)
                    continue;

                // Optionally check target attribute: only apply if this is the intended target.
                if (def.modifierTargetAttribute != null &&
                    def.modifierTargetAttribute != Definition)
                    continue;

                AttributeValue modVal = modifier.GetEffectiveValueInternal(visited);

                // Read modifier numeric value as double (supports both int and float-based modifiers).
                double amount = def.kind switch
                {
                    AttributeKind.Integer => modVal.ToInt(),
                    AttributeKind.Float => modVal.ToFloat(),
                    _ => 0.0
                };

                switch (def.modifierValueKind)
                {
                    case AttributeModifierValueKind.Flat:
                        flat += amount;
                        break;
                    case AttributeModifierValueKind.Factor:
                        // factor 0.1 = +10%, -0.1 = -10%
                        factor += amount;
                        break;
                }
            }

            double result = (baseInt + flat) * (1.0 + factor);
            long final = (long)Math.Round(result);

            AttributeValue finalValue = AttributeValue.FromInt(final);
            // Apply clamp-on-get one more time, now on the effective result.
            if (Definition != null && Owner != null)
                finalValue = Definition.ClampForGet(finalValue, Owner);

            return finalValue;
        }

        private AttributeValue GetEffectiveFloatValue(AttributeValue baseVal, HashSet<AttributeInstance> visited)
        {
            double baseDouble = baseVal.ToFloat();
            double flat = 0.0;
            double factor = 0.0;

            foreach (AttributeInstance modifier in _incomingModifiers)
            {
                AttributeDefinition def = modifier.Definition;
                if (def == null || !def.isModifier)
                    continue;

                if (def.modifierTargetAttribute != null &&
                    def.modifierTargetAttribute != Definition)
                    continue;

                AttributeValue modVal = modifier.GetEffectiveValueInternal(visited);

                double amount = def.kind switch
                {
                    AttributeKind.Integer => modVal.ToInt(),
                    AttributeKind.Float => modVal.ToFloat(),
                    _ => 0.0
                };

                switch (def.modifierValueKind)
                {
                    case AttributeModifierValueKind.Flat:
                        flat += amount;
                        break;
                    case AttributeModifierValueKind.Factor:
                        factor += amount;
                        break;
                }
            }

            double result = (baseDouble + flat) * (1.0 + factor);
            AttributeValue finalValue = AttributeValue.FromFloat(result);

            if (Definition != null && Owner != null)
                finalValue = Definition.ClampForGet(finalValue, Owner);

            return finalValue;
        }

        /// <summary>
        /// Returns the names of all active flags based on the effective value
        /// (after modifiers, if flags become modifiable in the future).
        /// </summary>
        public string[] GetEffectiveFlagNames()
        {
            if (Definition == null || Definition.kind != AttributeKind.Flags)
                return Array.Empty<string>();

            if (Definition.flagNames == null || Definition.flagNames.Length == 0)
                return Array.Empty<string>();

            AttributeValue effective = GetEffectiveValue();
            ulong mask = effective.ToFlags();

            List<string> result = new();

            for (int i = 0; i < Definition.flagNames.Length; i++)
            {
                string name = Definition.flagNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                ulong bit = 1UL << i;
                if ((mask & bit) != 0) result.Add(name);
            }

            return result.ToArray();
        }
    }
}