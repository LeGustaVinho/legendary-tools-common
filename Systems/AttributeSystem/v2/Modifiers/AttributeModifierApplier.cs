using System;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Helper system that allows any entity to apply its modifier attributes
    /// to any other entity.
    /// </summary>
    public static class AttributeModifierApplier
    {
        /// <summary>
        /// Applies all modifier attributes from source to target.
        /// </summary>
        public static void ApplyAllModifiers(Entity source, Entity target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            foreach (AttributeInstance sourceAttr in source.GetAllAttributes())
            {
                AttributeDefinition def = sourceAttr.Definition;
                if (def == null || !def.isModifier)
                    continue;

                ApplySingleModifierAttribute(sourceAttr, target);
            }
        }

        /// <summary>
        /// Applies a single modifier attribute from its owning entity to a target entity.
        /// </summary>
        public static void ApplySingleModifierAttribute(AttributeInstance modifierAttribute, Entity target)
        {
            if (modifierAttribute == null) throw new ArgumentNullException(nameof(modifierAttribute));
            if (target == null) throw new ArgumentNullException(nameof(target));

            AttributeDefinition def = modifierAttribute.Definition;
            if (def == null || !def.isModifier)
                return;

            if (def.modifierTargetAttribute == null)
            {
                Debug.LogWarning($"Modifier '{def.displayName}' has no target attribute defined.");
                return;
            }

            AttributeDefinition targetDef = def.modifierTargetAttribute;
            AttributeInstance existingTargetAttr = target.GetAttribute(targetDef);

            AttributeInstance targetAttr = existingTargetAttr;

            if (targetAttr == null)
                switch (def.missingTargetBehavior)
                {
                    case MissingTargetAttributeBehavior.Ignore:
                        // Do nothing for this modifier/target combination.
                        return;

                    case MissingTargetAttributeBehavior.Error:
                        throw new InvalidOperationException(
                            $"Target entity '{target.Name}' does not have required attribute '{targetDef.displayName}' for modifier '{def.displayName}'.");

                    case MissingTargetAttributeBehavior.CreateWithDefinitionDefault:
                    {
                        AttributeValue baseVal = targetDef.GetDefaultBaseValue();
                        targetAttr = target.AddAttributeInstance(targetDef, baseVal);
                        break;
                    }

                    case MissingTargetAttributeBehavior.CreateWithZero:
                    {
                        AttributeValue zero;
                        switch (targetDef.kind)
                        {
                            case AttributeKind.Integer:
                                zero = AttributeValue.FromInt(0);
                                break;
                            case AttributeKind.Float:
                                zero = AttributeValue.FromFloat(0.0);
                                break;
                            case AttributeKind.Flags:
                                zero = AttributeValue.FromFlags(0);
                                break;
                            default:
                                zero = AttributeValue.FromInt(0);
                                break;
                        }

                        targetAttr = target.AddAttributeInstance(targetDef, zero);
                        break;
                    }

                    default:
                        return;
                }

            if (targetAttr != null) targetAttr.AddModifier(modifierAttribute);
        }
    }
}