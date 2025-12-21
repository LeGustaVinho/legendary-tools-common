using System;
using System.Collections.Generic;
using UnityEngine;
using LegendaryTools.AttributeSystemV2;

/// <summary>
/// MonoBehaviour demo that exercises multiple modifier use cases:
/// 1) Flat buff (+Armor)
/// 2) Factor debuff (-Damage %)
/// 3) Stacking flat buffs
/// 4) Recursive chain (Enchantment -> Item -> Character)
/// 5) Mixed flat + factor on same attribute
/// 6) Missing target attribute behavior (CreateWithDefinitionDefault)
/// 7) Self modifiers (entity applies its own modifiers to itself)
/// 8) Flags: reading effective flag names
/// </summary>
public class ModifierExamplesDemo : MonoBehaviour
{
    [Header("Run")] [SerializeField] private bool runOnStart = true;

    [Header("UI")] [SerializeField] private bool showOnGui = true;

    private DemoDatabase db;

    private void Start()
    {
        db = new DemoDatabase();
        db.BuildRuntimeDefinitions();

        if (runOnStart) RunAllExamples();
    }

    private void OnGUI()
    {
        if (!showOnGui)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 520, 560), GUI.skin.window);
        GUILayout.Label("Modifier Examples Demo");

        if (GUILayout.Button("Run All Examples"))
            RunAllExamples();

        GUILayout.Space(8);

        if (GUILayout.Button("1) Flat +10 Armor (Buff -> Character)"))
            Example_FlatBuffArmor();

        if (GUILayout.Button("2) Factor -10% Damage (Curse -> Sword)"))
            Example_FactorDebuffDamage();

        if (GUILayout.Button("3) Stack Flat +5 Armor x3 (Buffs -> Character)"))
            Example_StackFlatBuffs();

        if (GUILayout.Button("4) Recursive Chain (Enchant -> Sword -> CharacterAttack)"))
            Example_RecursiveChain();

        if (GUILayout.Button("5) Mixed Flat +20 and Factor +15% (Item + Aura -> Character Armor)"))
            Example_MixedFlatAndFactor();

        if (GUILayout.Button("6) Missing Target Behavior (CreateWithDefinitionDefault)"))
            Example_MissingTargetAttributeBehavior();

        if (GUILayout.Button("7) Self Modifier (Sprint +30% Speed)"))
            Example_SelfModifier();

        if (GUILayout.Button("8) Flags Names (Poison + Shielded)"))
            Example_FlagsNames();

        GUILayout.Space(10);
        GUILayout.Label("Check Console for results.");

        GUILayout.EndArea();
    }

    private void RunAllExamples()
    {
        Debug.Log("=== ModifierExamplesDemo: RunAllExamples ===");
        Example_FlatBuffArmor();
        Example_FactorDebuffDamage();
        Example_StackFlatBuffs();
        Example_RecursiveChain();
        Example_MixedFlatAndFactor();
        Example_MissingTargetAttributeBehavior();
        Example_SelfModifier();
        Example_FlagsNames();
        Debug.Log("=== ModifierExamplesDemo: Done ===");
    }

    // ------------------------------------------------------------
    // Example 1: Flat buff (+10 Armor) applied to Character.
    // ------------------------------------------------------------
    private void Example_FlatBuffArmor()
    {
        Debug.Log("---- Example 1: Flat +10 Armor (Buff -> Character) ----");

        Entity character = new(db.CharacterDef);
        Entity buff = new(db.BuffArmorFlat10Def);

        AttributeModifierApplier.ApplyAllModifiers(buff, character);

        PrintEffectiveInt(character, db.ArmorDef, "Character Armor");
    }

    // ------------------------------------------------------------
    // Example 2: Factor debuff (-10% Damage) applied to Sword.
    // ------------------------------------------------------------
    private void Example_FactorDebuffDamage()
    {
        Debug.Log("---- Example 2: Factor -10% Damage (Curse -> Sword) ----");

        Entity sword = new(db.SwordDef_WithDamage);
        Entity curse = new(db.CurseDamageMinus10PercentDef);

        AttributeModifierApplier.ApplyAllModifiers(curse, sword);

        PrintEffectiveInt(sword, db.DamageDef, "Sword Damage");
    }

    // ------------------------------------------------------------
    // Example 3: Stacking flat buffs (+5 Armor x3) applied to Character.
    // ------------------------------------------------------------
    private void Example_StackFlatBuffs()
    {
        Debug.Log("---- Example 3: Stack Flat +5 Armor x3 (Buffs -> Character) ----");

        Entity character = new(db.CharacterDef);
        Entity buffA = new(db.BuffArmorFlat5Def);
        Entity buffB = new(db.BuffArmorFlat5Def);
        Entity buffC = new(db.BuffArmorFlat5Def);

        AttributeModifierApplier.ApplyAllModifiers(buffA, character);
        AttributeModifierApplier.ApplyAllModifiers(buffB, character);
        AttributeModifierApplier.ApplyAllModifiers(buffC, character);

        PrintEffectiveInt(character, db.ArmorDef, "Character Armor");
    }

    // ------------------------------------------------------------
    // Example 4: Recursive chain:
    // Enchant modifies Sword's modifier attribute, Sword modifies CharacterAttack.
    // Enchant -> Sword -> Character
    // ------------------------------------------------------------
    private void Example_RecursiveChain()
    {
        Debug.Log("---- Example 4: Recursive Chain (Enchant -> Sword -> CharacterAttack) ----");

        Entity character = new(db.CharacterDef_WithAttack);
        Entity sword = new(db.SwordDef_AttackToCharacter);
        Entity enchant = new(db.SwordEnchant_FactorPlus20OnSwordModifier);

        // Enchantment modifies Sword's modifier attribute.
        AttributeModifierApplier.ApplyAllModifiers(enchant, sword);

        // Sword applies its modifiers to Character.
        AttributeModifierApplier.ApplyAllModifiers(sword, character);

        // CharacterAttack reads recursively: it sees incoming modifier (from sword),
        // which itself has an incoming modifier (from enchant).
        PrintEffectiveInt(character, db.CharacterAttackDef, "Character Attack");
    }

    // ------------------------------------------------------------
    // Example 5: Mixed flat + factor on the same attribute:
    // Item gives +20 flat Armor and Aura gives +15% Armor.
    // Result = (base + 20) * (1 + 0.15)
    // ------------------------------------------------------------
    private void Example_MixedFlatAndFactor()
    {
        Debug.Log("---- Example 5: Mixed Flat +20 and Factor +15% (Item + Aura -> Character Armor) ----");

        Entity character = new(db.CharacterDef);
        Entity item = new(db.ItemArmorFlat20Def);
        Entity aura = new(db.AuraArmorFactor15Def);

        AttributeModifierApplier.ApplyAllModifiers(item, character);
        AttributeModifierApplier.ApplyAllModifiers(aura, character);

        PrintEffectiveInt(character, db.ArmorDef, "Character Armor");
    }

    // ------------------------------------------------------------
    // Example 6: Missing target attribute behavior:
    // Apply an Evasion modifier to a Character that does NOT have Evasion.
    // Behavior: CreateWithDefinitionDefault.
    // ------------------------------------------------------------
    private void Example_MissingTargetAttributeBehavior()
    {
        Debug.Log("---- Example 6: Missing Target Behavior (CreateWithDefinitionDefault) ----");

        Entity character = new(db.CharacterDef_NoEvasion);
        Entity aura = new(db.AuraEvasionFactor10_CreateDefaultDef);

        // Character has no Evasion attribute in its definition.
        AttributeModifierApplier.ApplyAllModifiers(aura, character);

        // Should now exist (created by behavior).
        AttributeInstance evasion = character.GetAttribute(db.EvasionDef);
        if (evasion == null)
        {
            Debug.LogError("Evasion was not created as expected.");
            return;
        }

        PrintEffectiveFloat(character, db.EvasionDef, "Character Evasion");
    }

    // ------------------------------------------------------------
    // Example 7: Self modifier:
    // The Character contains a modifier attribute that targets Speed with +30% factor.
    // ApplyAllModifiers(character, character).
    // ------------------------------------------------------------
    private void Example_SelfModifier()
    {
        Debug.Log("---- Example 7: Self Modifier (Sprint +30% Speed) ----");

        Entity character = new(db.CharacterDef_WithSelfSprint);

        // Self-apply: entity applies its own modifier attributes to itself.
        AttributeModifierApplier.ApplyAllModifiers(character, character);

        PrintEffectiveFloat(character, db.SpeedDef, "Character Speed");
    }

    // ------------------------------------------------------------
    // Example 8: Flags names:
    // Just shows how to read active flag names from a Flags attribute.
    // (This does not apply modifier math to flags yet; it uses current stored mask.)
    // ------------------------------------------------------------
    private void Example_FlagsNames()
    {
        Debug.Log("---- Example 8: Flags Names (Poison + Shielded) ----");

        Entity character = new(db.CharacterDef_WithStatusFlags);

        AttributeInstance status = character.GetAttribute(db.StatusFlagsDef);
        if (status == null)
        {
            Debug.LogError("StatusFlags attribute not found on character.");
            return;
        }

        string[] active = status.GetEffectiveFlagNames();
        Debug.Log($"Active flags: {(active.Length == 0 ? "<none>" : string.Join(", ", active))}");
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private static void PrintEffectiveInt(Entity entity, AttributeDefinition def, string label)
    {
        AttributeInstance attr = entity.GetAttribute(def);
        if (attr == null)
        {
            Debug.LogWarning($"{label}: <missing attribute '{def.displayName}'>");
            return;
        }

        long baseVal = attr.Value.ToInt();
        long effVal = attr.GetEffectiveValue().ToInt();
        Debug.Log($"{label}: Base={baseVal} Effective={effVal}");
    }

    private static void PrintEffectiveFloat(Entity entity, AttributeDefinition def, string label)
    {
        AttributeInstance attr = entity.GetAttribute(def);
        if (attr == null)
        {
            Debug.LogWarning($"{label}: <missing attribute '{def.displayName}'>");
            return;
        }

        double baseVal = attr.Value.ToFloat();
        double effVal = attr.GetEffectiveValue().ToFloat();
        Debug.Log($"{label}: Base={baseVal:0.###} Effective={effVal:0.###}");
    }

    // ============================================================
    // DemoDatabase: creates ScriptableObjects at runtime so you can
    // run this demo without preparing assets in the Project.
    // ============================================================
    private sealed class DemoDatabase
    {
        // Core Attributes
        public AttributeDefinition ArmorDef;
        public AttributeDefinition DamageDef;
        public AttributeDefinition SpeedDef;
        public AttributeDefinition CharacterAttackDef;
        public AttributeDefinition EvasionDef;
        public AttributeDefinition StatusFlagsDef;

        // Modifier Attributes (Definitions)
        public AttributeDefinition Mod_ArmorFlat10;
        public AttributeDefinition Mod_ArmorFlat5;
        public AttributeDefinition Mod_DamageFactorMinus10Percent;
        public AttributeDefinition Mod_ArmorFlat20;
        public AttributeDefinition Mod_ArmorFactorPlus15Percent;

        public AttributeDefinition Mod_EvasionFactorPlus10Percent_CreateDefault;

        public AttributeDefinition Mod_SwordToCharacterAttackFlat;
        public AttributeDefinition Mod_EnchantFactorPlus20OnSwordModifier;

        public AttributeDefinition Mod_SprintSpeedFactorPlus30;

        // Entity Definitions
        public EntityDefinition CharacterDef;
        public EntityDefinition CharacterDef_WithAttack;
        public EntityDefinition CharacterDef_NoEvasion;
        public EntityDefinition CharacterDef_WithSelfSprint;
        public EntityDefinition CharacterDef_WithStatusFlags;

        public EntityDefinition BuffArmorFlat10Def;
        public EntityDefinition BuffArmorFlat5Def;
        public EntityDefinition CurseDamageMinus10PercentDef;
        public EntityDefinition ItemArmorFlat20Def;
        public EntityDefinition AuraArmorFactor15Def;
        public EntityDefinition AuraEvasionFactor10_CreateDefaultDef;

        public EntityDefinition SwordDef_WithDamage;
        public EntityDefinition SwordDef_AttackToCharacter;
        public EntityDefinition SwordEnchant_FactorPlus20OnSwordModifier;

        public void BuildRuntimeDefinitions()
        {
            BuildCoreAttributeDefinitions();
            BuildModifierAttributeDefinitions();
            BuildEntityDefinitions();
        }

        private void BuildCoreAttributeDefinitions()
        {
            ArmorDef = CreateAttr("Armor", AttributeKind.Integer, "Defense");
            DamageDef = CreateAttr("Damage", AttributeKind.Integer, "Offense");
            SpeedDef = CreateAttr("Speed", AttributeKind.Float, "Movement");
            CharacterAttackDef = CreateAttr("CharacterAttack", AttributeKind.Integer, "Offense");
            EvasionDef = CreateAttr("Evasion", AttributeKind.Float, "Defense");

            StatusFlagsDef = CreateAttr("StatusFlags", AttributeKind.Flags, "Status");
            StatusFlagsDef.flagNames = new[]
            {
                "Poison", // bit 0
                "Stun", // bit 1
                "Shielded", // bit 2
                "Burn" // bit 3
            };

            // Some sensible defaults for missing-attribute creation demo.
            EvasionDef.baseFloat = 10.0;
        }

        private void BuildModifierAttributeDefinitions()
        {
            Mod_ArmorFlat10 = CreateModifierAttr(
                "Mod_ArmorFlat10",
                AttributeKind.Integer,
                ArmorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Ignore);

            Mod_ArmorFlat5 = CreateModifierAttr(
                "Mod_ArmorFlat5",
                AttributeKind.Integer,
                ArmorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Ignore);

            Mod_DamageFactorMinus10Percent = CreateModifierAttr(
                "Mod_DamageFactorMinus10Percent",
                AttributeKind.Float,
                DamageDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Ignore);

            Mod_ArmorFlat20 = CreateModifierAttr(
                "Mod_ArmorFlat20",
                AttributeKind.Integer,
                ArmorDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Ignore);

            Mod_ArmorFactorPlus15Percent = CreateModifierAttr(
                "Mod_ArmorFactorPlus15Percent",
                AttributeKind.Float,
                ArmorDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Ignore);

            Mod_EvasionFactorPlus10Percent_CreateDefault = CreateModifierAttr(
                "Mod_EvasionFactorPlus10Percent_CreateDefault",
                AttributeKind.Float,
                EvasionDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.CreateWithDefinitionDefault);

            // Sword modifies CharacterAttack via a modifier attribute on Sword.
            Mod_SwordToCharacterAttackFlat = CreateModifierAttr(
                "Mod_SwordToCharacterAttackFlat",
                AttributeKind.Integer,
                CharacterAttackDef,
                AttributeModifierValueKind.Flat,
                MissingTargetAttributeBehavior.Ignore);

            // Enchantment modifies the Sword's modifier attribute (recursive chain).
            Mod_EnchantFactorPlus20OnSwordModifier = CreateModifierAttr(
                "Mod_EnchantFactorPlus20OnSwordModifier",
                AttributeKind.Float,
                Mod_SwordToCharacterAttackFlat,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Ignore);

            // Self modifier: +30% Speed.
            Mod_SprintSpeedFactorPlus30 = CreateModifierAttr(
                "Mod_SprintSpeedFactorPlus30",
                AttributeKind.Float,
                SpeedDef,
                AttributeModifierValueKind.Factor,
                MissingTargetAttributeBehavior.Ignore);
        }

        private void BuildEntityDefinitions()
        {
            CharacterDef = CreateEntity("Character",
                (ArmorDef, AttributeValue.FromInt(50)),
                (DamageDef, AttributeValue.FromInt(30)),
                (SpeedDef, AttributeValue.FromFloat(5.0)));

            CharacterDef_WithAttack = CreateEntity("Character_WithAttack",
                (ArmorDef, AttributeValue.FromInt(50)),
                (DamageDef, AttributeValue.FromInt(30)),
                (SpeedDef, AttributeValue.FromFloat(5.0)),
                (CharacterAttackDef, AttributeValue.FromInt(40)));

            // Intentionally no Evasion here.
            CharacterDef_NoEvasion = CreateEntity("Character_NoEvasion",
                (ArmorDef, AttributeValue.FromInt(50)),
                (SpeedDef, AttributeValue.FromFloat(5.0)));

            // Self sprint modifier is inside the character itself.
            CharacterDef_WithSelfSprint = CreateEntity("Character_WithSelfSprint",
                (SpeedDef, AttributeValue.FromFloat(5.0)),
                (Mod_SprintSpeedFactorPlus30, AttributeValue.FromFloat(0.3)));

            // Flags example.
            ulong poison = 1UL << 0;
            ulong shielded = 1UL << 2;
            CharacterDef_WithStatusFlags = CreateEntity("Character_WithStatusFlags",
                (StatusFlagsDef, AttributeValue.FromFlags(poison | shielded)));

            BuffArmorFlat10Def = CreateEntity("Buff_ArmorFlat10",
                (Mod_ArmorFlat10, AttributeValue.FromInt(10)));

            BuffArmorFlat5Def = CreateEntity("Buff_ArmorFlat5",
                (Mod_ArmorFlat5, AttributeValue.FromInt(5)));

            CurseDamageMinus10PercentDef = CreateEntity("Curse_DamageMinus10Percent",
                (Mod_DamageFactorMinus10Percent, AttributeValue.FromFloat(-0.1)));

            ItemArmorFlat20Def = CreateEntity("Item_ArmorFlat20",
                (Mod_ArmorFlat20, AttributeValue.FromInt(20)));

            AuraArmorFactor15Def = CreateEntity("Aura_ArmorFactor15Percent",
                (Mod_ArmorFactorPlus15Percent, AttributeValue.FromFloat(0.15)));

            AuraEvasionFactor10_CreateDefaultDef = CreateEntity("Aura_EvasionFactor10_CreateDefault",
                (Mod_EvasionFactorPlus10Percent_CreateDefault, AttributeValue.FromFloat(0.1)));

            // Sword has Damage for debuff demo.
            SwordDef_WithDamage = CreateEntity("Sword_WithDamage",
                (DamageDef, AttributeValue.FromInt(60)));

            // Sword provides a modifier to CharacterAttack.
            SwordDef_AttackToCharacter = CreateEntity("Sword_AttackToCharacter",
                (Mod_SwordToCharacterAttackFlat, AttributeValue.FromInt(20)));

            // Enchant modifies the sword's modifier attribute by +20%.
            SwordEnchant_FactorPlus20OnSwordModifier = CreateEntity("Enchant_Sword_ModifierFactorPlus20",
                (Mod_EnchantFactorPlus20OnSwordModifier, AttributeValue.FromFloat(0.2)));
        }

        private static AttributeDefinition CreateAttr(string name, AttributeKind kind, string category)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = kind;

            def.categoryName = category;
            def.visibility = AttributeVisibility.Debug;

            // Use simple defaults; clamp can be configured later if desired.
            def.clampMode = AttributeClampMode.None;
            return def;
        }

        private static AttributeDefinition CreateModifierAttr(
            string name,
            AttributeKind kind,
            AttributeDefinition target,
            AttributeModifierValueKind valueKind,
            MissingTargetAttributeBehavior missingBehavior)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = kind;

            def.categoryName = "Modifiers";
            def.visibility = AttributeVisibility.Debug;

            def.isModifier = true;
            def.modifierTargetAttribute = target;
            def.modifierValueKind = valueKind;
            def.missingTargetBehavior = missingBehavior;

            // Conditions prepared but not implemented.
            def.hasCondition = false;
            def.conditionDescription = string.Empty;

            def.clampMode = AttributeClampMode.None;
            return def;
        }

        private static EntityDefinition CreateEntity(string name,
            params (AttributeDefinition def, AttributeValue baseValue)[] entries)
        {
            EntityDefinition entityDef = ScriptableObject.CreateInstance<EntityDefinition>();
            entityDef.entityName = name;
            entityDef.attributes = new List<AttributeEntry>();

            for (int i = 0; i < entries.Length; i++)
            {
                AttributeEntry e = new()
                {
                    definition = entries[i].def,
                    baseValue = entries[i].baseValue
                };
                entityDef.attributes.Add(e);
            }

            return entityDef;
        }
    }
}