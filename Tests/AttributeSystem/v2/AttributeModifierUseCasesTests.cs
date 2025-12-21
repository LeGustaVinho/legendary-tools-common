using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LegendaryTools.AttributeSystemV2;

namespace LegendaryTools.AttributeSystemV2.Tests
{
    /// <summary>
    /// Unity Test Runner (EditMode) tests covering the modifier use cases.
    /// These tests create ScriptableObject definitions at runtime and build runtime Entities from them.
    /// </summary>
    public sealed class AttributeModifierUseCasesTests
    {
        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------

        private static AttributeDefinition CreateIntAttribute(string name)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Integer;
            def.clampMode = AttributeClampMode.None;
            def.visibility = AttributeVisibility.Public;
            def.categoryName = "Test";
            return def;
        }

        private static AttributeDefinition CreateFloatAttribute(string name)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Float;
            def.clampMode = AttributeClampMode.None;
            def.visibility = AttributeVisibility.Public;
            def.categoryName = "Test";
            return def;
        }

        private static AttributeDefinition CreateFlagsAttribute(string name, string[] flagNames)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Flags;
            def.clampMode = AttributeClampMode.None;
            def.visibility = AttributeVisibility.Public;
            def.categoryName = "Test";
            def.flagNames = flagNames ?? Array.Empty<string>();
            return def;
        }

        private static AttributeDefinition CreateModifierDefinition(
            string name,
            AttributeKind kind,
            AttributeDefinition targetAttribute,
            AttributeModifierValueKind valueKind,
            MissingTargetAttributeBehavior missingTargetBehavior,
            bool hasCondition = false,
            string conditionDescription = null)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = kind;
            def.clampMode = AttributeClampMode.None;
            def.visibility = AttributeVisibility.Public;
            def.categoryName = "Test/Modifier";

            def.isModifier = true;
            def.modifierTargetAttribute = targetAttribute;
            def.modifierValueKind = valueKind;
            def.missingTargetBehavior = missingTargetBehavior;

            def.hasCondition = hasCondition;
            def.conditionDescription = conditionDescription;

            return def;
        }

        private static EntityDefinition CreateEntityDefinition(string name,
            params (AttributeDefinition def, AttributeValue baseValue)[] entries)
        {
            EntityDefinition ed = ScriptableObject.CreateInstance<EntityDefinition>();
            ed.entityName = name;
            ed.attributes = new List<AttributeEntry>();

            foreach ((AttributeDefinition def, AttributeValue baseValue) e in entries)
            {
                ed.attributes.Add(new AttributeEntry
                {
                    definition = e.def,
                    baseValue = e.baseValue
                });
            }

            return ed;
        }

        // ------------------------------------------------------------
        // Use case 1: Flat +10 Armor applied to character
        // ------------------------------------------------------------

        [Test]
        public void UseCase1_FlatArmorBuff_AppliesPlus10()
        {
            AttributeDefinition armorDef = CreateIntAttribute("Armor");

            AttributeDefinition armorPlus10Def = CreateModifierDefinition(
                "Armor +10 (Flat)",
                AttributeKind.Integer,
                armorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (armorDef, AttributeValue.FromInt(50)));

            EntityDefinition buffDef = CreateEntityDefinition(
                "Buff",
                (armorPlus10Def, AttributeValue.FromInt(10)));

            Entity character = new(characterDef);
            Entity buff = new(buffDef);

            AttributeModifierApplier.ApplyAllModifiers(buff, character);

            AttributeInstance armor = character.GetAttribute(armorDef);
            Assert.NotNull(armor);

            long effectiveArmor = armor.GetEffectiveValue().ToInt();
            Assert.AreEqual(60, effectiveArmor);
        }

        // ------------------------------------------------------------
        // Use case 2: Factor -10% Damage debuff applied to sword
        // ------------------------------------------------------------

        [Test]
        public void UseCase2_FactorDamageDebuff_ReducesBy10Percent()
        {
            AttributeDefinition damageDef = CreateIntAttribute("Damage");

            AttributeDefinition damageMinus10PctDef = CreateModifierDefinition(
                "Damage -10% (Factor)",
                AttributeKind.Float,
                damageDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition swordDef = CreateEntityDefinition(
                "Sword",
                (damageDef, AttributeValue.FromInt(100)));

            EntityDefinition curseDef = CreateEntityDefinition(
                "Curse",
                (damageMinus10PctDef, AttributeValue.FromFloat(-0.1)));

            Entity sword = new(swordDef);
            Entity curse = new(curseDef);

            AttributeModifierApplier.ApplyAllModifiers(curse, sword);

            AttributeInstance dmg = sword.GetAttribute(damageDef);
            Assert.NotNull(dmg);

            long effectiveDamage = dmg.GetEffectiveValue().ToInt();
            Assert.AreEqual(90, effectiveDamage);
        }

        // ------------------------------------------------------------
        // Use case 3: Stack buffs (+5 Armor) three times
        // ------------------------------------------------------------

        [Test]
        public void UseCase3_StackFlatBuffs_AddsMultipleSources()
        {
            AttributeDefinition armorDef = CreateIntAttribute("Armor");

            AttributeDefinition armorPlus5Def = CreateModifierDefinition(
                "Armor +5 (Flat)",
                AttributeKind.Integer,
                armorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (armorDef, AttributeValue.FromInt(10)));

            EntityDefinition buffA_Def = CreateEntityDefinition("BuffA", (armorPlus5Def, AttributeValue.FromInt(5)));
            EntityDefinition buffB_Def = CreateEntityDefinition("BuffB", (armorPlus5Def, AttributeValue.FromInt(5)));
            EntityDefinition buffC_Def = CreateEntityDefinition("BuffC", (armorPlus5Def, AttributeValue.FromInt(5)));

            Entity character = new(characterDef);
            Entity buffA = new(buffA_Def);
            Entity buffB = new(buffB_Def);
            Entity buffC = new(buffC_Def);

            AttributeModifierApplier.ApplyAllModifiers(buffA, character);
            AttributeModifierApplier.ApplyAllModifiers(buffB, character);
            AttributeModifierApplier.ApplyAllModifiers(buffC, character);

            AttributeInstance armor = character.GetAttribute(armorDef);
            Assert.NotNull(armor);

            long effectiveArmor = armor.GetEffectiveValue().ToInt();
            Assert.AreEqual(25, effectiveArmor);
        }

        // ------------------------------------------------------------
        // Use case 4: Recursive chain: Enchantment -> Sword -> Character
        // (Implemented as: enchant modifies sword's modifier value, sword modifies character)
        // ------------------------------------------------------------

        [Test]
        public void UseCase4_RecursiveChain_EnchantAffectsSwordWhichAffectsCharacter()
        {
            // Character attribute
            AttributeDefinition characterAttackDef = CreateIntAttribute("CharacterAttack");

            // Sword has a modifier attribute that targets CharacterAttack (+Factor)
            AttributeDefinition swordAttackBonusFactorDef = CreateModifierDefinition(
                "Sword Attack Bonus (Factor)",
                AttributeKind.Float,
                characterAttackDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            // Enchantment modifies the sword's modifier attribute itself (flat +0.1)
            AttributeDefinition enchantAddsBonusFactorDef = CreateModifierDefinition(
                "Enchant +BonusFactor (Flat)",
                AttributeKind.Float,
                swordAttackBonusFactorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (characterAttackDef, AttributeValue.FromInt(100)));

            EntityDefinition swordDef = CreateEntityDefinition(
                "Sword",
                (swordAttackBonusFactorDef, AttributeValue.FromFloat(0.1)));

            EntityDefinition enchantDef = CreateEntityDefinition(
                "Enchant",
                (enchantAddsBonusFactorDef, AttributeValue.FromFloat(0.1)));

            Entity character = new(characterDef);
            Entity sword = new(swordDef);
            Entity enchant = new(enchantDef);

            // Enchant modifies Sword
            AttributeModifierApplier.ApplyAllModifiers(enchant, sword);

            // Sword modifies Character
            AttributeModifierApplier.ApplyAllModifiers(sword, character);

            AttributeInstance attack = character.GetAttribute(characterAttackDef);
            Assert.NotNull(attack);

            // Expected: base 100 * (1 + (0.1 + 0.1)) = 120
            long effectiveAttack = attack.GetEffectiveValue().ToInt();
            Assert.AreEqual(120, effectiveAttack);
        }

        // ------------------------------------------------------------
        // Use case 5: Flat + Factor mix: (base + flat) * (1 + factor)
        // ------------------------------------------------------------

        [Test]
        public void UseCase5_MixedFlatAndFactor_ComposesCorrectly()
        {
            AttributeDefinition armorDef = CreateIntAttribute("Armor");

            AttributeDefinition armorFlatPlus20Def = CreateModifierDefinition(
                "Armor +20 (Flat)",
                AttributeKind.Integer,
                armorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            AttributeDefinition armorFactorPlus15PctDef = CreateModifierDefinition(
                "Armor +15% (Factor)",
                AttributeKind.Float,
                armorDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef =
                CreateEntityDefinition("Character", (armorDef, AttributeValue.FromInt(100)));
            EntityDefinition itemDef = CreateEntityDefinition("Item", (armorFlatPlus20Def, AttributeValue.FromInt(20)));
            EntityDefinition auraDef =
                CreateEntityDefinition("Aura", (armorFactorPlus15PctDef, AttributeValue.FromFloat(0.15)));

            Entity character = new(characterDef);
            Entity item = new(itemDef);
            Entity aura = new(auraDef);

            AttributeModifierApplier.ApplyAllModifiers(item, character);
            AttributeModifierApplier.ApplyAllModifiers(aura, character);

            AttributeInstance armor = character.GetAttribute(armorDef);
            Assert.NotNull(armor);

            // (100 + 20) * 1.15 = 138
            long effectiveArmor = armor.GetEffectiveValue().ToInt();
            Assert.AreEqual(138, effectiveArmor);
        }

        // ------------------------------------------------------------
        // Use case 6: Missing target attribute behaviors
        // - CreateWithZero should create the attribute on target.
        // - Ignore should do nothing (attribute remains missing).
        // ------------------------------------------------------------

        [Test]
        public void UseCase6_MissingTarget_CreateWithZero_CreatesAttribute()
        {
            AttributeDefinition evasionDef = CreateFloatAttribute("Evasion");

            AttributeDefinition evasionPlus10PctDef = CreateModifierDefinition(
                "Evasion +10% (Factor)",
                AttributeKind.Float,
                evasionDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.CreateWithZero);

            EntityDefinition characterDef = CreateEntityDefinition("Character" /* no evasion */);
            EntityDefinition auraDef =
                CreateEntityDefinition("Aura", (evasionPlus10PctDef, AttributeValue.FromFloat(0.1)));

            Entity character = new(characterDef);
            Entity aura = new(auraDef);

            AttributeModifierApplier.ApplyAllModifiers(aura, character);

            AttributeInstance evasion = character.GetAttribute(evasionDef);
            Assert.NotNull(evasion, "Evasion should have been created due to CreateWithZero.");

            // Base is zero, factor won't change the value.
            double effective = evasion.GetEffectiveValue().ToFloat();
            Assert.AreEqual(0.0, effective, 0.0001);
        }

        [Test]
        public void UseCase6_MissingTarget_Ignore_DoesNotCreateAttribute()
        {
            AttributeDefinition evasionDef = CreateFloatAttribute("Evasion");

            AttributeDefinition evasionPlus10PctIgnoreDef = CreateModifierDefinition(
                "Evasion +10% (Factor, IgnoreMissing)",
                AttributeKind.Float,
                evasionDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Ignore);

            EntityDefinition characterDef = CreateEntityDefinition("Character" /* no evasion */);
            EntityDefinition auraDef =
                CreateEntityDefinition("Aura", (evasionPlus10PctIgnoreDef, AttributeValue.FromFloat(0.1)));

            Entity character = new(characterDef);
            Entity aura = new(auraDef);

            AttributeModifierApplier.ApplyAllModifiers(aura, character);

            AttributeInstance evasion = character.GetAttribute(evasionDef);
            Assert.IsNull(evasion, "Evasion should remain missing due to Ignore behavior.");
        }

        // ------------------------------------------------------------
        // Use case 7: Self modifiers (entity modifies itself)
        // ------------------------------------------------------------

        [Test]
        public void UseCase7_SelfModifiers_ApplyFromEntityToItself()
        {
            AttributeDefinition speedDef = CreateFloatAttribute("Speed");

            AttributeDefinition sprintPlus30PctDef = CreateModifierDefinition(
                "Sprint +30% (Factor)",
                AttributeKind.Float,
                speedDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (speedDef, AttributeValue.FromFloat(10.0)),
                (sprintPlus30PctDef, AttributeValue.FromFloat(0.3)));

            Entity character = new(characterDef);

            AttributeModifierApplier.ApplyAllModifiers(character, character);

            AttributeInstance speed = character.GetAttribute(speedDef);
            Assert.NotNull(speed);

            double effectiveSpeed = speed.GetEffectiveValue().ToFloat();
            Assert.AreEqual(13.0, effectiveSpeed, 0.0001);
        }

        // ------------------------------------------------------------
        // Use case 8: Flags - get friendly flag names from effective value
        // ------------------------------------------------------------

        [Test]
        public void UseCase8_Flags_GetEffectiveFlagNames_ReturnsActiveNames()
        {
            AttributeDefinition statusDef = CreateFlagsAttribute(
                "StatusFlags",
                new[] { "Poison", "Stun", "Shielded" });

            // Bits 0 and 2 set => 0b101 => 5
            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (statusDef, AttributeValue.FromFlags(5UL)));

            Entity character = new(characterDef);

            AttributeInstance status = character.GetAttribute(statusDef);
            Assert.NotNull(status);

            string[] names = status.GetEffectiveFlagNames();
            CollectionAssert.AreEquivalent(new[] { "Poison", "Shielded" }, names);
        }

        // ------------------------------------------------------------
        // Use case 9: Equip/unequip simulation by rebuilding the runtime entity
        // (No explicit "remove modifiers" API in this version.)
        // ------------------------------------------------------------

        [Test]
        public void UseCase9_EquipUnequip_SimulatedByRebuildingEntityState()
        {
            AttributeDefinition armorDef = CreateIntAttribute("Armor");

            AttributeDefinition armorPlus10Def = CreateModifierDefinition(
                "Armor +10 (Flat)",
                AttributeKind.Integer,
                armorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error);

            EntityDefinition characterDef = CreateEntityDefinition("Character", (armorDef, AttributeValue.FromInt(50)));
            EntityDefinition buffDef = CreateEntityDefinition("Buff", (armorPlus10Def, AttributeValue.FromInt(10)));

            Entity buff = new(buffDef);

            // Equipped (modifiers applied)
            Entity equippedCharacter = new(characterDef);
            AttributeModifierApplier.ApplyAllModifiers(buff, equippedCharacter);

            long equippedArmor = equippedCharacter.GetAttribute(armorDef).GetEffectiveValue().ToInt();
            Assert.AreEqual(60, equippedArmor);

            // Unequipped (fresh entity, no modifiers applied)
            Entity unequippedCharacter = new(characterDef);
            long unequippedArmor = unequippedCharacter.GetAttribute(armorDef).GetEffectiveValue().ToInt();
            Assert.AreEqual(50, unequippedArmor);
        }

        // ------------------------------------------------------------
        // Use case 10: Conditional prepared (not evaluated yet)
        // Modifier still applies even if hasCondition = true.
        // ------------------------------------------------------------

        [Test]
        public void UseCase10_ConditionalPrepared_IsNotEvaluatedYet_ModifierStillApplies()
        {
            AttributeDefinition strengthDef = CreateIntAttribute("Strength");

            AttributeDefinition conditionalBuffDef = CreateModifierDefinition(
                "Strength +5 (Conditional Prepared)",
                AttributeKind.Integer,
                strengthDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Error,
                true,
                "Apply only if Class == Warrior (not implemented).");

            EntityDefinition characterDef = CreateEntityDefinition(
                "Character",
                (strengthDef, AttributeValue.FromInt(10)));

            EntityDefinition buffDef = CreateEntityDefinition(
                "Buff",
                (conditionalBuffDef, AttributeValue.FromInt(5)));

            Entity character = new(characterDef);
            Entity buff = new(buffDef);

            AttributeModifierApplier.ApplyAllModifiers(buff, character);

            AttributeInstance strength = character.GetAttribute(strengthDef);
            Assert.NotNull(strength);

            long effectiveStrength = strength.GetEffectiveValue().ToInt();
            Assert.AreEqual(15, effectiveStrength, "Condition is not evaluated yet, so the modifier should apply.");
        }
    }
}