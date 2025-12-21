using System;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Variable provider that exposes an entity's attributes and flags to GenericExpressionEngine.
    /// Examples:
    ///   $HP                 -> numeric value of HP attribute
    ///   $Armor              -> numeric value of Armor attribute
    ///   $Status_Poisoned    -> 1 if 'Poisoned' flag is set on 'Status' flags attribute, otherwise 0
    /// Matching is case-insensitive and uses AttributeDefinition.displayName or asset name.
    /// </summary>
    public sealed class EntityAttributeVariableProvider : IVariableProvider<double>
    {
        private readonly Entity _entity;

        public EntityAttributeVariableProvider(Entity entity)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        }

        public bool TryGetVariable(string name, out double value)
        {
            if (string.IsNullOrEmpty(name))
            {
                value = 0d;
                return false;
            }

            // Variable names are expected to start with '$'
            if (name[0] == '$')
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
            if (attribute == null)
            {
                value = 0d;
                return false;
            }

            AttributeDefinition def = attribute.Definition;
            if (def == null)
            {
                value = 0d;
                return false;
            }

            if (def.kind == AttributeKind.Flags)
            {
                if (flagName == null)
                {
                    // Expose raw bit mask as double (for advanced use)
                    value = attribute.Value.Raw; // ulong -> implícito para double
                    return true;
                }

                int bitIndex = FindFlagIndex(def, flagName);
                if (bitIndex < 0)
                {
                    value = 0d;
                    return false;
                }

                bool isOn = attribute.Value.HasFlag(bitIndex);
                value = isOn ? 1d : 0d;
                return true;
            }

            // Numeric attributes
            if (def.kind == AttributeKind.Integer)
            {
                value = attribute.Value.ToInt();
                return true;
            }

            if (def.kind == AttributeKind.Float)
            {
                value = attribute.Value.ToFloat();
                return true;
            }

            value = 0d;
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

                // Try displayName
                if (!string.IsNullOrEmpty(def.displayName) &&
                    attributeName.Equals(def.displayName, StringComparison.OrdinalIgnoreCase))
                    return attr;

                // Fallback to asset name (ScriptableObject.name)
                if (attributeName.Equals(def.name, StringComparison.OrdinalIgnoreCase)) return attr;
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