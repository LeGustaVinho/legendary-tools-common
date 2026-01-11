using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// High-level reusable system that applies/removes modifier attributes between entities.
    /// Supports Direct and Reactive modes.
    /// </summary>
    public sealed class AttributeModifierCoordinator
    {
        public IModifierConditionEvaluator ConditionEvaluator { get; set; } = AlwaysTrueModifierConditionEvaluator.Instance;
        public IModifierSortPolicy SortPolicy { get; set; } = DefaultModifierSortPolicy.Instance;
        public IModifierStackingPolicy StackingPolicy { get; set; } = DefaultModifierStackingPolicy.Instance;

        /// <summary>
        /// Applies all modifiers from source to target in the selected mode.
        /// In Reactive mode returns a disposable session that keeps modifiers updated.
        /// </summary>
        public EntityModifierSession ApplyAll(
            Entity source,
            Entity target,
            ModifierApplicationMode mode,
            ReactiveIneligibleBehavior reactiveIneligibleBehavior = ReactiveIneligibleBehavior.RemoveWhenIneligible)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (mode == ModifierApplicationMode.Direct)
            {
                ApplyAllDirect(source, target);
                return null;
            }

            return new EntityModifierSession(this, source, target, reactiveIneligibleBehavior);
        }

        /// <summary>
        /// Direct apply: evaluates eligibility once and applies what is eligible.
        /// </summary>
        public List<ModifierApplyResult> ApplyAllDirect(Entity source, Entity target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            List<ModifierApplyResult> results = new();

            foreach (AttributeInstance modifier in source.GetAllAttributes())
            {
                if (!IsModifierAttribute(modifier))
                    continue;

                ModifierApplyResult result = ApplySingleDirect(source, modifier, target);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Applies a specific modifier attribute from source to target (Direct).
        /// </summary>
        public ModifierApplyResult ApplySingleDirect(Entity source, AttributeInstance modifier, Entity target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (target == null) throw new ArgumentNullException(nameof(target));

            AttributeDefinition def = modifier.Definition;
            if (def == null || !def.isModifier)
                return new ModifierApplyResult("<unknown>", "<unknown>", applied: false, reason: "Not a modifier.");

            if (def.modifierTargetAttribute == null)
                return new ModifierApplyResult(def.displayName, "<null>", applied: false, reason: "No target attribute defined.");

            AttributeInstance targetAttr = EnsureTargetAttributeExistsOrResolve(def, target, out string reasonIfFailed);
            if (targetAttr == null)
                return new ModifierApplyResult(def.displayName, def.modifierTargetAttribute.displayName, applied: false, reason: reasonIfFailed);

            // Condition hook (prepared for later).
            if (!IsEligibleByCondition(source, modifier, target, targetAttr))
                return new ModifierApplyResult(def.displayName, targetAttr.Definition.displayName, applied: false, reason: "Condition evaluated to false.");

            if (!StackingPolicy.CanStack(source, modifier, target, targetAttr))
                return new ModifierApplyResult(def.displayName, targetAttr.Definition.displayName, applied: false, reason: "Stacking policy denied.");

            targetAttr.AddModifier(modifier);

            return new ModifierApplyResult(def.displayName, targetAttr.Definition.displayName, applied: true, reason: "Applied.");
        }

        /// <summary>
        /// Attempts to apply a modifier to a specific target attribute instance,
        /// offering mismatch behavior configuration.
        /// </summary>
        public ModifierApplyResult ApplyToSpecificAttributeDirect(
            Entity source,
            AttributeInstance modifier,
            Entity target,
            AttributeInstance requestedTargetAttribute,
            TargetAttributeMismatchBehavior mismatchBehavior)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (requestedTargetAttribute == null) throw new ArgumentNullException(nameof(requestedTargetAttribute));

            AttributeDefinition modDef = modifier.Definition;
            if (modDef == null || !modDef.isModifier)
                return new ModifierApplyResult("<unknown>", "<unknown>", applied: false, reason: "Not a modifier.");

            AttributeDefinition intended = modDef.modifierTargetAttribute;

            bool mismatch = intended != null && requestedTargetAttribute.Definition != intended;

            if (mismatch)
            {
                switch (mismatchBehavior)
                {
                    case TargetAttributeMismatchBehavior.Ignore:
                        return new ModifierApplyResult(modDef.displayName, requestedTargetAttribute.Definition.displayName, false, "Mismatch ignored.");
                    case TargetAttributeMismatchBehavior.Warn:
                        Debug.LogWarning($"Modifier '{modDef.displayName}' intended for '{intended.displayName}', but requested '{requestedTargetAttribute.Definition.displayName}'.");
                        return new ModifierApplyResult(modDef.displayName, requestedTargetAttribute.Definition.displayName, false, "Mismatch warned.");
                    case TargetAttributeMismatchBehavior.Error:
                        throw new InvalidOperationException($"Modifier '{modDef.displayName}' intended for '{intended.displayName}', but requested '{requestedTargetAttribute.Definition.displayName}'.");
                    case TargetAttributeMismatchBehavior.RedirectToDefinitionTarget:
                        return ApplySingleDirect(source, modifier, target);
                    case TargetAttributeMismatchBehavior.ApplyAnyway:
                        break;
                }
            }

            if (!IsEligibleByCondition(source, modifier, target, requestedTargetAttribute))
                return new ModifierApplyResult(modDef.displayName, requestedTargetAttribute.Definition.displayName, false, "Condition evaluated to false.");

            if (!StackingPolicy.CanStack(source, modifier, target, requestedTargetAttribute))
                return new ModifierApplyResult(modDef.displayName, requestedTargetAttribute.Definition.displayName, false, "Stacking policy denied.");

            requestedTargetAttribute.AddModifier(modifier);
            return new ModifierApplyResult(modDef.displayName, requestedTargetAttribute.Definition.displayName, true, "Applied.");
        }

        /// <summary>
        /// Removes all modifiers coming from source that are currently linked to target.
        /// Works for Direct or Reactive (you can call it manually anytime).
        /// </summary>
        public int RemoveAllFromSource(Entity source, Entity target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            int removed = 0;

            foreach (AttributeInstance modifier in source.GetAllAttributes())
            {
                if (!IsModifierAttribute(modifier))
                    continue;

                removed += RemoveSingleModifier(source, modifier, target);
            }

            return removed;
        }

        /// <summary>
        /// Removes a single modifier attribute from whatever target attribute it was applied to (if any).
        /// Returns number of removals (0 or 1).
        /// </summary>
        public int RemoveSingleModifier(Entity source, AttributeInstance modifier, Entity target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (target == null) throw new ArgumentNullException(nameof(target));

            AttributeDefinition def = modifier.Definition;
            if (def == null || !def.isModifier || def.modifierTargetAttribute == null)
                return 0;

            AttributeInstance targetAttr = target.GetAttribute(def.modifierTargetAttribute);
            if (targetAttr == null)
                return 0;

            return targetAttr.RemoveModifier(modifier) ? 1 : 0;
        }

        internal void BuildOrRefreshLinks(Entity source, Entity target, List<ModifierLink> links)
        {
            links.Clear();

            // Collect all modifier attributes from source.
            List<AttributeInstance> modifiers = new();
            foreach (AttributeInstance a in source.GetAllAttributes())
            {
                if (IsModifierAttribute(a))
                    modifiers.Add(a);
            }

            // Sort hook (prepared for priority later).
            SortPolicy.Sort(modifiers);

            foreach (AttributeInstance modifier in modifiers)
            {
                AttributeDefinition def = modifier.Definition;
                if (def == null || def.modifierTargetAttribute == null)
                    continue;

                AttributeInstance targetAttr = EnsureTargetAttributeExistsOrResolve(def, target, out _);
                if (targetAttr == null)
                    continue;

                links.Add(new ModifierLink(source, target, modifier, targetAttr));
            }
        }

        internal void EvaluateAndApplyLinks(List<ModifierLink> links, ReactiveIneligibleBehavior ineligibleBehavior)
        {
            for (int i = 0; i < links.Count; i++)
            {
                ModifierLink link = links[i];
                if (link.IsDisposed)
                    continue;

                bool eligible = IsEligibleByCondition(link.Source, link.Modifier, link.Target, link.TargetAttribute) &&
                                StackingPolicy.CanStack(link.Source, link.Modifier, link.Target, link.TargetAttribute);

                if (eligible)
                {
                    if (!link.IsApplied)
                    {
                        link.TargetAttribute.AddModifier(link.Modifier);
                        link.MarkApplied(true);
                    }
                }
                else
                {
                    if (link.IsApplied && ineligibleBehavior == ReactiveIneligibleBehavior.RemoveWhenIneligible)
                    {
                        link.TargetAttribute.RemoveModifier(link.Modifier);
                        link.MarkApplied(false);
                    }
                }
            }
        }

        internal void RemoveLinks(List<ModifierLink> links)
        {
            for (int i = 0; i < links.Count; i++)
            {
                ModifierLink link = links[i];
                if (link.IsDisposed)
                    continue;

                if (link.IsApplied)
                {
                    link.TargetAttribute.RemoveModifier(link.Modifier);
                    link.MarkApplied(false);
                }

                link.Dispose();
            }
        }

        private static bool IsModifierAttribute(AttributeInstance attribute)
        {
            return attribute?.Definition != null && attribute.Definition.isModifier;
        }

        private bool IsEligibleByCondition(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute)
        {
            // Prepared for later:
            // - modifier.Definition.hasCondition
            // - modifier.Definition.conditionMode, conditionExpression, etc.
            // For now we delegate to evaluator (default is AlwaysTrue).
            return ConditionEvaluator == null || ConditionEvaluator.ShouldApply(source, modifier, target, targetAttribute);
        }

        private static AttributeInstance EnsureTargetAttributeExistsOrResolve(AttributeDefinition modifierDef, Entity target, out string reasonIfFailed)
        {
            reasonIfFailed = null;

            AttributeDefinition targetDef = modifierDef.modifierTargetAttribute;
            if (targetDef == null)
            {
                reasonIfFailed = "Modifier has no target definition.";
                return null;
            }

            AttributeInstance targetAttr = target.GetAttribute(targetDef);

            if (targetAttr != null)
            {
                // Optional: kind mismatch guard.
                if (targetAttr.Definition != targetDef)
                {
                    reasonIfFailed = "Target attribute definition mismatch.";
                    return null;
                }

                return targetAttr;
            }

            switch (modifierDef.missingTargetBehavior)
            {
                case MissingTargetAttributeBehavior.Ignore:
                    reasonIfFailed = "Target missing (Ignore).";
                    return null;

                case MissingTargetAttributeBehavior.Error:
                    reasonIfFailed = $"Target entity '{target.Name}' does not have required attribute '{targetDef.displayName}'.";
                    return null;

                case MissingTargetAttributeBehavior.CreateWithDefinitionDefault:
                {
                    AttributeValue baseVal = targetDef.GetDefaultBaseValue();
                    return target.AddAttributeInstance(targetDef, baseVal);
                }

                case MissingTargetAttributeBehavior.CreateWithZero:
                {
                    AttributeValue zero = targetDef.kind switch
                    {
                        AttributeKind.Integer => AttributeValue.FromInt(0),
                        AttributeKind.Float => AttributeValue.FromFloat(0.0),
                        AttributeKind.Flags => AttributeValue.FromFlags(0),
                        _ => AttributeValue.FromInt(0)
                    };

                    return target.AddAttributeInstance(targetDef, zero);
                }

                default:
                    reasonIfFailed = "Unknown missing target behavior.";
                    return null;
            }
        }
    }
}
