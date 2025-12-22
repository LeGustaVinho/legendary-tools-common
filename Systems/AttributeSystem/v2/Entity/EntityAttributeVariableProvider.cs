using System;
using System.Collections.Generic;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Exposes an Entity's attributes and flags as variables to GenericExpressionEngine.
    /// Examples:
    ///   $HP                 -> numeric value of HP attribute
    ///   $Armor              -> numeric value of Armor attribute
    ///   $Status_Poisoned    -> true/1 if 'Poisoned' flag is set on 'Status' flags attribute, otherwise false/0
    /// Matching is case-insensitive and uses AttributeDefinition.displayName or asset name.
    /// </summary>
    public sealed class EntityAttributeVariableProvider<T> : IVariableProvider<T>
    {
        private readonly Entity _entity;
        private readonly INumberOperations<T> _ops;

        public EntityAttributeVariableProvider(Entity entity, INumberOperations<T> ops)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        }

        public bool TryGetVariable(string name, out T value)
        {
            if (string.IsNullOrEmpty(name))
            {
                value = _ops.Zero;
                return false;
            }

            // Variable names are expected to start with '$'
            if (name.Length > 0 && name[0] == '$')
                name = name.Substring(1);

            // Optional flag syntax: Attribute_FlagName
            string attributeName;
            string flagName = null;

            int underscoreIndex = name.IndexOf('_');
            if (underscoreIndex > 0 && underscoreIndex < name.Length - 1)
            {
                attributeName = name.Substring(0, underscoreIndex);
                flagName = name.Substring(underscoreIndex + 1);
            }
            else
            {
                attributeName = name;
            }

            AttributeInstance attribute = FindAttributeByName(attributeName);
            if (attribute == null || attribute.Definition == null)
            {
                value = _ops.Zero;
                return false;
            }

            AttributeDefinition def = attribute.Definition;

            if (def.kind == AttributeKind.Flags)
            {
                if (flagName == null)
                {
                    // Advanced: expose raw bitmask as numeric (may lose precision for large masks if T cannot represent it exactly).
                    value = _ops.FromDouble(attribute.Value.Raw);
                    return true;
                }

                int bitIndex = FindFlagIndex(def, flagName);
                if (bitIndex < 0)
                {
                    value = _ops.Zero;
                    return false;
                }

                bool isOn = attribute.Value.HasFlag(bitIndex);
                value = _ops.FromBoolean(isOn);
                return true;
            }

            // Numeric attributes
            if (def.kind == AttributeKind.Integer)
            {
                value = _ops.FromDouble(attribute.Value.ToInt());
                return true;
            }

            if (def.kind == AttributeKind.Float)
            {
                value = _ops.FromDouble(attribute.Value.ToFloat());
                return true;
            }

            value = _ops.Zero;
            return false;
        }

        private AttributeInstance FindAttributeByName(string attributeName)
        {
            if (string.IsNullOrEmpty(attributeName))
                return null;

            foreach (AttributeInstance attr in _entity.GetAllAttributes())
            {
                AttributeDefinition def = attr.Definition;
                if (def == null)
                    continue;

                if (!string.IsNullOrEmpty(def.displayName) &&
                    attributeName.Equals(def.displayName, StringComparison.OrdinalIgnoreCase))
                    return attr;

                if (attributeName.Equals(def.name, StringComparison.OrdinalIgnoreCase))
                    return attr;
            }

            return null;
        }

        private static int FindFlagIndex(AttributeDefinition def, string flagName)
        {
            if (def.flagNames == null || string.IsNullOrEmpty(flagName))
                return -1;

            for (int i = 0; i < def.flagNames.Length; i++)
            {
                string name = def.flagNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                if (flagName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}