using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LegendaryTools.GenericExpressionEngine;
using NUnit.Framework;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2.Tests
{
    internal static class ModifierTestHelpers
    {
        public static AttributeDefinition CreateIntAttribute(string displayName, long baseValue)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.name = displayName;
            def.displayName = displayName;
            def.kind = AttributeKind.Integer;
            def.baseInteger = baseValue;
            return def;
        }

        public static AttributeDefinition CreateFloatAttribute(string displayName, double baseValue)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.name = displayName;
            def.displayName = displayName;
            def.kind = AttributeKind.Float;
            def.baseFloat = baseValue;
            return def;
        }

        public static AttributeDefinition CreateFlagsAttribute(string displayName, ulong baseFlags,
            params string[] flagNames)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.name = displayName;
            def.displayName = displayName;
            def.kind = AttributeKind.Flags;
            def.baseFlags = baseFlags;
            def.flagNames = flagNames ?? Array.Empty<string>();
            return def;
        }

        public static AttributeDefinition CreateModifier(
            string displayName,
            AttributeKind kind,
            AttributeDefinition targetAttribute,
            AttributeModifierValueKind valueKind,
            MissingTargetAttributeBehavior missingBehavior,
            bool hasCondition = false,
            AttributeModifierConditionMode conditionMode = AttributeModifierConditionMode.ExpressionOnly,
            string conditionExpression = null,
            string conditionKey = null)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.name = displayName;
            def.displayName = displayName;
            def.kind = kind;

            def.isModifier = true;
            def.modifierTargetAttribute = targetAttribute;
            def.modifierValueKind = valueKind;
            def.missingTargetBehavior = missingBehavior;

            def.hasCondition = hasCondition;
            def.conditionMode = conditionMode;
            def.conditionExpression = conditionExpression;
            def.conditionExpressionKey = conditionKey;

            return def;
        }

        public static Entity CreateEntity(string entityName,
            params (AttributeDefinition def, AttributeValue baseValue)[] attrs)
        {
            EntityDefinition entityDef = ScriptableObject.CreateInstance<EntityDefinition>();
            entityDef.entityName = entityName;

            entityDef.attributes = new List<AttributeEntry>();
            foreach ((AttributeDefinition def, AttributeValue baseValue) a in attrs)
            {
                entityDef.attributes.Add(new AttributeEntry
                {
                    definition = a.def,
                    baseValue = a.baseValue
                });
            }

            return new Entity(entityDef);
        }

        public static void SetAttributeBaseValue(AttributeInstance instance, AttributeValue newBaseValue)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            MethodInfo m = instance.GetType().GetMethod(
                "SetBaseValue",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(m, "AttributeInstance.SetBaseValue (internal) was not found via reflection.");

            // Signature: SetBaseValue(AttributeValue newBaseValue, bool reapplyLimits = true)
            ParameterInfo[] parameters = m.GetParameters();
            object[] args = parameters.Length == 2
                ? new object[] { newBaseValue, true }
                : new object[] { newBaseValue };

            m.Invoke(instance, args);
        }

        public static bool RemoveIncomingModifier(AttributeInstance targetAttr, AttributeInstance modifierAttr)
        {
            if (targetAttr == null) throw new ArgumentNullException(nameof(targetAttr));
            if (modifierAttr == null) throw new ArgumentNullException(nameof(modifierAttr));

            MethodInfo m = targetAttr.GetType().GetMethod(
                "RemoveModifier",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(m, "AttributeInstance.RemoveModifier (internal) was not found via reflection.");

            object result = m.Invoke(targetAttr, new object[] { modifierAttr });
            return result is bool b && b;
        }

        public static INumberOperations<double> CreateDoubleOps()
        {
            Type iface = typeof(INumberOperations<double>);

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract) continue;
                    if (!iface.IsAssignableFrom(t)) continue;

                    ConstructorInfo ctor = t.GetConstructor(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (ctor == null) continue;

                    return (INumberOperations<double>)ctor.Invoke(null);
                }
            }

            Assert.Fail(
                "No INumberOperations<double> implementation with parameterless ctor was found in loaded assemblies.");
            return null;
        }

        public static ExpressionEngine<double> CreateExpressionEngine(INumberOperations<double> ops)
        {
            Type engineType = typeof(ExpressionEngine<double>);

            // Try ctor(INumberOperations<double>)
            foreach (ConstructorInfo ctor in engineType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic |
                                                                        BindingFlags.Instance))
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(typeof(INumberOperations<double>)))
                    return (ExpressionEngine<double>)ctor.Invoke(new object[] { ops });
            }

            // Try parameterless ctor
            ConstructorInfo empty = engineType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);

            if (empty != null)
                return (ExpressionEngine<double>)empty.Invoke(null);

            Assert.Fail(
                "No compatible ExpressionEngine<double> ctor found (expected () or (INumberOperations<double>)).");
            return null;
        }

        /// <summary>
        /// Minimal reactive binding used only for tests:
        /// - Binds source->target
        /// - On Refresh(), evaluates all source modifiers and applies/removes based on evaluator.ShouldApply().
        /// </summary>
        public sealed class TestReactiveModifierBinding
        {
            private readonly ExpressionModifierConditionEvaluator<double> _evaluator;
            private readonly Entity _source;
            private readonly Entity _target;

            // Track what we applied (targetAttr, modifierAttr).
            private readonly HashSet<(AttributeInstance targetAttr, AttributeInstance modifierAttr)> _applied = new();

            public TestReactiveModifierBinding(ExpressionModifierConditionEvaluator<double> evaluator, Entity source,
                Entity target)
            {
                _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _target = target ?? throw new ArgumentNullException(nameof(target));
            }

            public void Refresh()
            {
                foreach (AttributeInstance srcAttr in _source.GetAllAttributes())
                {
                    AttributeDefinition modDef = srcAttr.Definition;
                    if (modDef == null || !modDef.isModifier) continue;
                    if (modDef.modifierTargetAttribute == null) continue;

                    AttributeInstance targetAttr = _target.GetAttribute(modDef.modifierTargetAttribute);
                    if (targetAttr == null)
                        // For reactive tests we only cover "target exists" scenarios.
                        continue;

                    bool shouldApply = _evaluator.ShouldApply(_source, srcAttr, _target, targetAttr);

                    (AttributeInstance targetAttr, AttributeInstance srcAttr) key = (targetAttr, srcAttr);
                    bool isApplied = _applied.Contains(key);

                    if (shouldApply && !isApplied)
                    {
                        AttributeModifierApplier.ApplySingleModifierAttribute(srcAttr, _target);
                        _applied.Add(key);
                    }
                    else if (!shouldApply && isApplied)
                    {
                        RemoveIncomingModifier(targetAttr, srcAttr);
                        _applied.Remove(key);
                    }
                }
            }
        }
    }

    public sealed class AttributeModifiers_GenericExpressions_Tests
    {
        [Test]
        public void Direct_FlatModifier_AppliesAndRemoves()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuff, AttributeValue.FromInt(10)));

            Entity target = ModifierTestHelpers.CreateEntity(
                "Target",
                (armor, AttributeValue.FromInt(100)));

            AttributeModifierApplier.ApplyAllModifiers(source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.AreEqual(110, targetArmor.GetEffectiveValue().ToInt());

            // Remove modifier (internal) via reflection to validate remove use-case.
            AttributeInstance buffInstance = source.GetAttribute(armorBuff);
            Assert.IsTrue(ModifierTestHelpers.RemoveIncomingModifier(targetArmor, buffInstance));
            Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Direct_FactorModifier_Applies()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition armorBuffPct = ModifierTestHelpers.CreateModifier(
                "ArmorBuffPct",
                AttributeKind.Float,
                armor,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuffPct, AttributeValue.FromFloat(0.10))); // +10%

            Entity target = ModifierTestHelpers.CreateEntity(
                "Target",
                (armor, AttributeValue.FromInt(100)));

            AttributeModifierApplier.ApplyAllModifiers(source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.AreEqual(110, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void MissingTargetBehavior_Ignore_DoesNothing()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 0);

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Ignore);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuff, AttributeValue.FromInt(10)));

            Entity target = ModifierTestHelpers.CreateEntity("Target" /* no Armor */);

            Assert.DoesNotThrow(() => AttributeModifierApplier.ApplyAllModifiers(source, target));
            Assert.IsNull(target.GetAttribute(armor));
        }

        [Test]
        public void MissingTargetBehavior_CreateWithDefinitionDefault_CreatesAttribute()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 123); // definition default base

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.CreateWithDefinitionDefault);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuff, AttributeValue.FromInt(10)));

            Entity target = ModifierTestHelpers.CreateEntity("Target" /* no Armor */);

            AttributeModifierApplier.ApplyAllModifiers(source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.IsNotNull(targetArmor);
            Assert.AreEqual(133, targetArmor.GetEffectiveValue().ToInt()); // 123 + 10
        }

        [Test]
        public void MissingTargetBehavior_CreateWithZero_CreatesAttribute()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 999); // ignored for zero mode

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.CreateWithZero);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuff, AttributeValue.FromInt(10)));

            Entity target = ModifierTestHelpers.CreateEntity("Target" /* no Armor */);

            AttributeModifierApplier.ApplyAllModifiers(source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.IsNotNull(targetArmor);
            Assert.AreEqual(10, targetArmor.GetEffectiveValue().ToInt()); // 0 + 10
        }

        [Test]
        public void MissingTargetBehavior_Error_Throws()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 0);

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuff, AttributeValue.FromInt(10)));

            Entity target = ModifierTestHelpers.CreateEntity("Target" /* no Armor */);

            Assert.Throws<InvalidOperationException>(() =>
                AttributeModifierApplier.ApplyAllModifiers(source, target));
        }

        [Test]
        public void Conditional_Expression_UsesTreeRelations_SelfParent()
        {
            // Target condition: apply only if self.parent.$Level >= 10
            AttributeDefinition level = ModifierTestHelpers.CreateIntAttribute("Level", 10);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "self.parent.$Level >= 10");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (level, AttributeValue.FromInt(10)),
                (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (armor, AttributeValue.FromInt(100)));

            // Build scope tree:
            // root
            //  └─ source
            //      └─ target
            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(source, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            // Evaluate + apply manually (Direct+Conditional)
            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.IsNotNull(targetArmor);

            AttributeInstance srcBuff = source.GetAttribute(buff);
            Assert.IsNotNull(srcBuff);

            bool shouldApply = evaluator.ShouldApply(source, srcBuff, target, targetArmor);
            Assert.IsTrue(shouldApply);

            AttributeModifierApplier.ApplySingleModifierAttribute(srcBuff, target);
            Assert.AreEqual(110, targetArmor.GetEffectiveValue().ToInt());

            // Now drop Level to 5 and ensure expression becomes false (without reactive binding it won't auto-remove).
            AttributeInstance srcLevel = source.GetAttribute(level);
            ModifierTestHelpers.SetAttributeBaseValue(srcLevel, AttributeValue.FromInt(5));

            shouldApply = evaluator.ShouldApply(source, srcBuff, target, targetArmor);
            Assert.IsFalse(shouldApply);
        }

        [Test]
        public void Conditional_Expression_UsesFlags()
        {
            // Apply if target.$Status_Poisoned == 1
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);
            AttributeDefinition status = ModifierTestHelpers.CreateFlagsAttribute("Status", 0UL, "Poisoned", "Stunned");

            AttributeDefinition debuff = ModifierTestHelpers.CreateModifier(
                "ArmorDebuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "target.$Status_Poisoned == 1");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (debuff, AttributeValue.FromInt(-10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (armor, AttributeValue.FromInt(100)),
                (status, AttributeValue.FromFlags(0UL)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            AttributeInstance targetStatus = target.GetAttribute(status);
            AttributeInstance srcDebuff = source.GetAttribute(debuff);

            // Initially not poisoned => condition false
            Assert.IsFalse(evaluator.ShouldApply(source, srcDebuff, target, targetArmor));

            // Set Poisoned flag
            ulong poisonedMask = AttributeValue.FromFlags(0UL).WithFlag(0).ToFlags();
            ModifierTestHelpers.SetAttributeBaseValue(targetStatus, AttributeValue.FromFlags(poisonedMask));

            Assert.IsTrue(evaluator.ShouldApply(source, srcDebuff, target, targetArmor));

            AttributeModifierApplier.ApplySingleModifierAttribute(srcDebuff, target);
            Assert.AreEqual(90, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Reactive_Condition_AppliesAndRemoves_OnTargetStateChange()
        {
            // Apply buff (+10 Armor) only if target.$HP < 50
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 100);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "target.$HP < 50");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (hp, AttributeValue.FromInt(100)),
                (armor, AttributeValue.FromInt(100)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            ModifierTestHelpers.TestReactiveModifierBinding binding = new(evaluator, source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            AttributeInstance targetHp = target.GetAttribute(hp);

            // HP=100 => not applied
            binding.Refresh();
            Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());

            // HP=40 => applied
            ModifierTestHelpers.SetAttributeBaseValue(targetHp, AttributeValue.FromInt(40));
            binding.Refresh();
            Assert.AreEqual(110, targetArmor.GetEffectiveValue().ToInt());

            // HP=80 => removed
            ModifierTestHelpers.SetAttributeBaseValue(targetHp, AttributeValue.FromInt(80));
            binding.Refresh();
            Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Direct_MultipleModifiers_FlatAndFactor_StacksAsExpected()
        {
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition flatPlus10 = ModifierTestHelpers.CreateModifier(
                "FlatPlus10",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            AttributeDefinition flatMinus5 = ModifierTestHelpers.CreateModifier(
                "FlatMinus5",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            AttributeDefinition pctPlus10 = ModifierTestHelpers.CreateModifier(
                "PctPlus10",
                AttributeKind.Float,
                armor,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            AttributeDefinition pctMinus10 = ModifierTestHelpers.CreateModifier(
                "PctMinus10",
                AttributeKind.Float,
                armor,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (flatPlus10, AttributeValue.FromInt(10)),
                (flatMinus5, AttributeValue.FromInt(-5)),
                (pctPlus10, AttributeValue.FromFloat(0.10)),
                (pctMinus10, AttributeValue.FromFloat(-0.10)));

            Entity target = ModifierTestHelpers.CreateEntity(
                "Target",
                (armor, AttributeValue.FromInt(100)));

            AttributeModifierApplier.ApplyAllModifiers(source, target);

            // (100 + (10 - 5)) * (1 + (0.10 - 0.10)) = 105
            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.AreEqual(105, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Recursive_ModifierOnModifier_ChangesFinalTargetValue()
        {
            // armor target
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            // buff on source that targets armor
            AttributeDefinition armorBuffFlat = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            // another modifier (aura) that targets the modifier attribute itself (buff gets amplified)
            AttributeDefinition amplifyBuffFactor = ModifierTestHelpers.CreateModifier(
                "AmplifyBuffFactor",
                AttributeKind.Float,
                armorBuffFlat,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            Entity root = ModifierTestHelpers.CreateEntity("Root");

            // Source has the buff base=10
            Entity source = ModifierTestHelpers.CreateEntity(
                "Source",
                (armorBuffFlat, AttributeValue.FromInt(10)));

            // Aura has factor=+100% targeting ArmorBuffFlat (so 10 becomes 20)
            Entity aura = ModifierTestHelpers.CreateEntity(
                "Aura",
                (amplifyBuffFactor, AttributeValue.FromFloat(1.0)));

            Entity target = ModifierTestHelpers.CreateEntity(
                "Target",
                (armor, AttributeValue.FromInt(100)));

            // Build scope tree just to keep consistency with the expression-based ecosystem
            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, aura, "aura");
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, target, "target");

            // 1) Aura modifies Source (buff becomes 20 effectively)
            AttributeModifierApplier.ApplyAllModifiers(aura, source);

            // 2) Source modifies Target (armor gets +20)
            AttributeModifierApplier.ApplyAllModifiers(source, target);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            Assert.AreEqual(120, targetArmor.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Conditional_UsesAliases_PlayerScopeAndSelfScope()
        {
            // Apply only if player HP < 50
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 100);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "player.$HP < 50 && self.$Armor >= 0");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));
            Entity player = ModifierTestHelpers.CreateEntity("Player", (hp, AttributeValue.FromInt(100)),
                (armor, AttributeValue.FromInt(100)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, player, "target");
            scopeTree.AddAlias(player, "player"); // alias "player" -> same node as "target"

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            AttributeInstance targetArmor = player.GetAttribute(armor);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            // HP=100 => false
            Assert.IsFalse(evaluator.ShouldApply(source, srcBuff, player, targetArmor));

            // HP=40 => true
            AttributeInstance playerHp = player.GetAttribute(hp);
            ModifierTestHelpers.SetAttributeBaseValue(playerHp, AttributeValue.FromInt(40));
            Assert.IsTrue(evaluator.ShouldApply(source, srcBuff, player, targetArmor));
        }

        [Test]
        public void Conditional_UsesSiblingNavigation_NextSiblingAndPrevSibling()
        {
            // Condition: apply if next sibling HP == 200 and prev sibling HP == 50
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "self.prevSibling.$HP == 50 && self.nextSibling.$HP == 200");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));

            // Three siblings under the same parent:
            Entity sib0 = ModifierTestHelpers.CreateEntity("Sib0", (hp, AttributeValue.FromInt(50)));
            Entity sib1 = ModifierTestHelpers.CreateEntity("Sib1", (hp, AttributeValue.FromInt(999)),
                (armor, AttributeValue.FromInt(100))); // target
            Entity sib2 = ModifierTestHelpers.CreateEntity("Sib2", (hp, AttributeValue.FromInt(200)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");

            Entity parent = ModifierTestHelpers.CreateEntity("Parent");
            scopeTree.AddChild(root, parent, "parent");

            scopeTree.AddChild(parent, sib0, "sib0");
            scopeTree.AddChild(parent, sib1, "sib1");
            scopeTree.AddChild(parent, sib2, "sib2");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            AttributeInstance targetArmor = sib1.GetAttribute(armor);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            Assert.IsTrue(evaluator.ShouldApply(source, srcBuff, sib1, targetArmor));

            // Break prev sibling HP condition
            AttributeInstance sib0Hp = sib0.GetAttribute(hp);
            ModifierTestHelpers.SetAttributeBaseValue(sib0Hp, AttributeValue.FromInt(49));
            Assert.IsFalse(evaluator.ShouldApply(source, srcBuff, sib1, targetArmor));

            // Restore and break next sibling HP condition
            ModifierTestHelpers.SetAttributeBaseValue(sib0Hp, AttributeValue.FromInt(50));
            AttributeInstance sib2Hp = sib2.GetAttribute(hp);
            ModifierTestHelpers.SetAttributeBaseValue(sib2Hp, AttributeValue.FromInt(201));
            Assert.IsFalse(evaluator.ShouldApply(source, srcBuff, sib1, targetArmor));
        }

        [Test]
        public void Conditional_StaleCacheIsCleared_BetweenEvaluations()
        {
            // This test validates that the evaluator/context clears cached variable lookups.
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 100);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "target.$HP < 50");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (hp, AttributeValue.FromInt(100)),
                (armor, AttributeValue.FromInt(100)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            AttributeInstance targetHp = target.GetAttribute(hp);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            // First evaluation (HP=100) => false
            Assert.IsFalse(evaluator.ShouldApply(source, srcBuff, target, targetArmor));

            // Change HP to 40 and evaluate again => must become true (no stale cache)
            ModifierTestHelpers.SetAttributeBaseValue(targetHp, AttributeValue.FromInt(40));
            Assert.IsTrue(evaluator.ShouldApply(source, srcBuff, target, targetArmor));

            // Change HP back to 100 => must become false again
            ModifierTestHelpers.SetAttributeBaseValue(targetHp, AttributeValue.FromInt(100));
            Assert.IsFalse(evaluator.ShouldApply(source, srcBuff, target, targetArmor));
        }

        [Test]
        public void ManualWrongTargetAttachment_IsIgnoredByEffectiveValue()
        {
            // Even if someone attaches a modifier to the wrong attribute instance manually,
            // the GetEffectiveValue should ignore it due to target attribute mismatch.
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 100);

            AttributeDefinition armorBuff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            Entity source = ModifierTestHelpers.CreateEntity("Source", (armorBuff, AttributeValue.FromInt(999)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (armor, AttributeValue.FromInt(100)),
                (hp, AttributeValue.FromInt(100)));

            AttributeInstance buffInstance = source.GetAttribute(armorBuff);
            AttributeInstance targetHp = target.GetAttribute(hp);

            // Wrong manual attachment: add armorBuff as incoming modifier to HP.
            AttributeModifierApplier.ApplySingleModifierAttribute(buffInstance, target); // correct attach goes to Armor
            // Now also attach incorrectly via reflection by calling AddModifier on HP
            MethodInfo addMod = targetHp.GetType()
                .GetMethod("AddModifier", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(addMod, "AttributeInstance.AddModifier (internal) was not found via reflection.");
            addMod.Invoke(targetHp, new object[] { buffInstance });

            // HP should not be affected because modifier targets Armor, not HP
            Assert.AreEqual(100, targetHp.GetEffectiveValue().ToInt());
        }

        [Test]
        public void Conditional_AmbiguousChildRelation_ThrowsOrFailsResolution()
        {
            // "self.child" is only valid if exactly one child exists; with multiple children, relation resolution fails.
            // In that case, the scope path cannot be resolved and the engine ends up throwing KeyNotFoundException
            // for the scoped variable lookup.
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "self.child.$HP > 0");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));

            Entity parent = ModifierTestHelpers.CreateEntity("Parent", (armor, AttributeValue.FromInt(100)));
            Entity childA = ModifierTestHelpers.CreateEntity("ChildA", (hp, AttributeValue.FromInt(1)));
            Entity childB = ModifierTestHelpers.CreateEntity("ChildB", (hp, AttributeValue.FromInt(2)));

            EntityScopeTree scopeTree = new();
            scopeTree.SetRoot(root);
            scopeTree.AddChild(root, source, "source");
            scopeTree.AddChild(root, parent, "parent");
            scopeTree.AddChild(parent, childA, "childA");
            scopeTree.AddChild(parent, childB, "childB");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> ctx = new(scopeTree, ops);
            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, ctx, cache);

            AttributeInstance parentArmor = parent.GetAttribute(armor);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            Assert.Throws<KeyNotFoundException>(() => { evaluator.ShouldApply(source, srcBuff, parent, parentArmor); });
        }

        [Test]
        public void Reactive_TargetStateFlap_AddsAndRemovesModifierAutomatically()
        {
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "$HP > 0");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (hp, AttributeValue.FromInt(0)),
                (armor, AttributeValue.FromInt(100)));

            EntityScopeTree tree = new();
            tree.SetRoot(root);
            tree.AddChild(root, source, "source");
            tree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> scopeCtx = new(tree, ops);
            scopeCtx.Install();

            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, scopeCtx, cache);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            AttributeInstance targetHp = target.GetAttribute(hp);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            using (ReactiveModifierBinding<double> reactive = new(scopeCtx, evaluator, source, srcBuff, target,
                       targetArmor))
            {
                // Starts false -> not applied.
                Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());

                // Flip condition true -> applied.
                SetBaseValue(targetHp, AttributeValue.FromInt(1));
                Assert.AreEqual(110, targetArmor.GetEffectiveValue().ToInt());

                // Flip condition false -> removed.
                SetBaseValue(targetHp, AttributeValue.FromInt(0));
                Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());
            }
        }

        [Test]
        public void Reactive_SourceStateFlap_ReevaluatesConditionAndSyncsModifier()
        {
            AttributeDefinition power = ModifierTestHelpers.CreateIntAttribute("Power", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 50);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "source.$Power >= 2");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (power, AttributeValue.FromInt(0)),
                (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (armor, AttributeValue.FromInt(50)));

            EntityScopeTree tree = new();
            tree.SetRoot(root);
            tree.AddChild(root, source, "source");
            tree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> scopeCtx = new(tree, ops);
            scopeCtx.Install();

            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, scopeCtx, cache);

            AttributeInstance srcPower = source.GetAttribute(power);
            AttributeInstance srcBuff = source.GetAttribute(buff);
            AttributeInstance targetArmor = target.GetAttribute(armor);

            using (ReactiveModifierBinding<double> reactive = new(scopeCtx, evaluator, source, srcBuff, target,
                       targetArmor))
            {
                Assert.AreEqual(50, targetArmor.GetEffectiveValue().ToInt());

                SetBaseValue(srcPower, AttributeValue.FromInt(2));
                Assert.AreEqual(60, targetArmor.GetEffectiveValue().ToInt());

                SetBaseValue(srcPower, AttributeValue.FromInt(1));
                Assert.AreEqual(50, targetArmor.GetEffectiveValue().ToInt());
            }
        }

        [Test]
        public void Reactive_UsesTreeRelations_SelfParentCondition()
        {
            AttributeDefinition auraOn = ModifierTestHelpers.CreateIntAttribute("AuraOn", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 20);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "self.parent.$AuraOn == 1");

            Entity root = ModifierTestHelpers.CreateEntity("Root");

            // Parent is also the modifier source (contains AuraOn + the modifier attribute).
            Entity parent = ModifierTestHelpers.CreateEntity(
                "Parent",
                (auraOn, AttributeValue.FromInt(0)),
                (buff, AttributeValue.FromInt(5)));

            Entity child = ModifierTestHelpers.CreateEntity(
                "Child",
                (armor, AttributeValue.FromInt(20)));

            EntityScopeTree tree = new();
            tree.SetRoot(root);
            tree.AddChild(root, parent, "parent");
            tree.AddChild(parent, child, "child");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> scopeCtx = new(tree, ops);
            scopeCtx.Install();

            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, scopeCtx, cache);

            AttributeInstance parentAura = parent.GetAttribute(auraOn);
            AttributeInstance srcBuff = parent.GetAttribute(buff);
            AttributeInstance childArmor = child.GetAttribute(armor);

            using (ReactiveModifierBinding<double> reactive = new(scopeCtx, evaluator, parent, srcBuff, child,
                       childArmor))
            {
                Assert.AreEqual(20, childArmor.GetEffectiveValue().ToInt());

                SetBaseValue(parentAura, AttributeValue.FromInt(1));
                Assert.AreEqual(25, childArmor.GetEffectiveValue().ToInt());

                SetBaseValue(parentAura, AttributeValue.FromInt(0));
                Assert.AreEqual(20, childArmor.GetEffectiveValue().ToInt());
            }
        }

        [Test]
        public void Reactive_DisposeStopsAutoSync()
        {
            AttributeDefinition hp = ModifierTestHelpers.CreateIntAttribute("HP", 0);
            AttributeDefinition armor = ModifierTestHelpers.CreateIntAttribute("Armor", 100);

            AttributeDefinition buff = ModifierTestHelpers.CreateModifier(
                "ArmorBuffFlat",
                AttributeKind.Integer,
                armor,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                AttributeModifierConditionMode.ExpressionOnly,
                "$HP > 0");

            Entity root = ModifierTestHelpers.CreateEntity("Root");
            Entity source = ModifierTestHelpers.CreateEntity("Source", (buff, AttributeValue.FromInt(10)));
            Entity target = ModifierTestHelpers.CreateEntity("Target", (hp, AttributeValue.FromInt(0)),
                (armor, AttributeValue.FromInt(100)));

            EntityScopeTree tree = new();
            tree.SetRoot(root);
            tree.AddChild(root, source, "source");
            tree.AddChild(root, target, "target");

            INumberOperations<double> ops = ModifierTestHelpers.CreateDoubleOps();
            ExpressionEngine<double> engine = ModifierTestHelpers.CreateExpressionEngine(ops);
            EntityExpressionScopeContext<double> scopeCtx = new(tree, ops);
            scopeCtx.Install();

            DictionaryCompiledExpressionCache<double> cache = new();
            ExpressionModifierConditionEvaluator<double> evaluator = new(engine, scopeCtx, cache);

            AttributeInstance targetArmor = target.GetAttribute(armor);
            AttributeInstance targetHp = target.GetAttribute(hp);
            AttributeInstance srcBuff = source.GetAttribute(buff);

            ReactiveModifierBinding<double> reactive = new(scopeCtx, evaluator, source, srcBuff, target, targetArmor);
            try
            {
                Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());

                reactive.Dispose();

                // Condition becomes true, but no longer auto-syncs.
                SetBaseValue(targetHp, AttributeValue.FromInt(1));
                Assert.AreEqual(100, targetArmor.GetEffectiveValue().ToInt());
            }
            finally
            {
                reactive.Dispose();
            }
        }

        private sealed class ReactiveModifierBinding<T> : IDisposable
        {
            private readonly EntityExpressionScopeContext<T> _scopeCtx;
            private readonly ExpressionModifierConditionEvaluator<T> _evaluator;

            private readonly Entity _source;
            private readonly AttributeInstance _modifier;
            private readonly Entity _target;
            private readonly AttributeInstance _targetAttribute;

            private bool _isDisposed;

            public ReactiveModifierBinding(
                EntityExpressionScopeContext<T> scopeCtx,
                ExpressionModifierConditionEvaluator<T> evaluator,
                Entity source,
                AttributeInstance modifier,
                Entity target,
                AttributeInstance targetAttribute)
            {
                _scopeCtx = scopeCtx ?? throw new ArgumentNullException(nameof(scopeCtx));
                _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));
                _target = target ?? throw new ArgumentNullException(nameof(target));
                _targetAttribute = targetAttribute ?? throw new ArgumentNullException(nameof(targetAttribute));

                HookEntity(_source);
                HookEntity(_target);

                // Initial sync.
                Refresh();
            }

            public void Dispose()
            {
                if (_isDisposed) return;
                _isDisposed = true;

                UnhookEntity(_source);
                UnhookEntity(_target);
            }

            private void HookEntity(Entity e)
            {
                e.AttributeAdded += OnAttributeAdded;
                foreach (AttributeInstance attr in e.GetAllAttributes())
                {
                    attr.ValueChanged += OnAnyValueChanged;
                }
            }

            private void UnhookEntity(Entity e)
            {
                e.AttributeAdded -= OnAttributeAdded;
                foreach (AttributeInstance attr in e.GetAllAttributes())
                {
                    attr.ValueChanged -= OnAnyValueChanged;
                }
            }

            private void OnAttributeAdded(Entity entity, AttributeInstance instance)
            {
                instance.ValueChanged += OnAnyValueChanged;
                Refresh();
            }

            private void OnAnyValueChanged(AttributeInstance inst, AttributeValue oldValue, AttributeValue newValue)
            {
                Refresh();
            }

            private void Refresh()
            {
                _scopeCtx.SetSelf(_target);

                bool shouldApply = _evaluator.ShouldApply(_source, _modifier, _target, _targetAttribute);
                if (shouldApply)
                    AttributeModifierApplier.ApplySingleModifierAttribute(_modifier, _target);
                else
                    RemoveModifier(_targetAttribute, _modifier);
            }
        }

        private static void RemoveModifier(AttributeInstance targetAttribute, AttributeInstance modifier)
        {
            MethodInfo mi = typeof(AttributeInstance).GetMethod(
                "RemoveModifier",
                BindingFlags.Instance | BindingFlags.NonPublic);

            mi.Invoke(targetAttribute, new object[] { modifier });
        }

        private static void SetBaseValue(AttributeInstance inst, AttributeValue value)
        {
            MethodInfo mi = typeof(AttributeInstance).GetMethod(
                "SetBaseValue",
                BindingFlags.Instance | BindingFlags.NonPublic);

            mi.Invoke(inst, new object[] { value, true });
        }
    }
}