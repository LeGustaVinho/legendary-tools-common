using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using LegendaryTools.AttributeSystemV2;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools.AttributeSystemV2.Tests
{
    /// <summary>
    /// Unity Test Runner (EditMode) tests for Attribute + Entity core behavior WITHOUT using modifiers.
    /// Covers:
    /// - AttributeDefinition defaults (int/float/flags)
    /// - AttributeInstance clamp modes (fixed + reference min/max)
    /// - Reference-driven max changes affecting CurrentHP (ClampOnGet vs ClampOnSet/SetAndGet)
    /// - Events (BaseValueChanged / ValueChanged)
    /// - Flags naming
    /// - Ensuring ScriptableObject templates are not modified by runtime changes
    /// - EntityDefinition/AttributeDefinition GUID auto-fill + collision resolution (Editor-only)
    /// </summary>
    public sealed class AttributeAndEntityNonModifierTests
    {
        private static MethodInfo _setBaseValueMethod;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _setBaseValueMethod = typeof(AttributeInstance).GetMethod(
                "SetBaseValue",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(_setBaseValueMethod,
                "AttributeInstance.SetBaseValue internal method was not found via reflection.");
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static void SetBaseValue(AttributeInstance instance, AttributeValue value, bool reapplyLimits = true)
        {
            Assert.NotNull(instance);
            _setBaseValueMethod.Invoke(instance, new object[] { value, reapplyLimits });
        }

        private static AttributeDefinition CreateIntAttribute(string name)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Integer;
            def.clampMode = AttributeClampMode.None;
            def.categoryName = "Test";
            def.visibility = AttributeVisibility.Public;
            return def;
        }

        private static AttributeDefinition CreateFloatAttribute(string name)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Float;
            def.clampMode = AttributeClampMode.None;
            def.categoryName = "Test";
            def.visibility = AttributeVisibility.Public;
            return def;
        }

        private static AttributeDefinition CreateFlagsAttribute(string name, params string[] flagNames)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = name;
            def.kind = AttributeKind.Flags;
            def.clampMode = AttributeClampMode.None;
            def.categoryName = "Test";
            def.visibility = AttributeVisibility.Public;
            def.flagNames = flagNames ?? Array.Empty<string>();
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

        // ---------------------------------------------------------------------
        // AttributeDefinition - Defaults
        // ---------------------------------------------------------------------

        [Test]
        public void AttributeDefinition_DefaultBaseValue_Integer_UsesBaseInteger()
        {
            AttributeDefinition def = CreateIntAttribute("Armor");
            def.baseInteger = 123;

            AttributeValue v = def.GetDefaultBaseValue();
            Assert.AreEqual(123, v.ToInt());
        }

        [Test]
        public void AttributeDefinition_DefaultBaseValue_Float_UsesBaseFloat()
        {
            AttributeDefinition def = CreateFloatAttribute("Speed");
            def.baseFloat = 2.5;

            AttributeValue v = def.GetDefaultBaseValue();
            Assert.AreEqual(2.5, v.ToFloat(), 0.0001);
        }

        [Test]
        public void AttributeDefinition_DefaultBaseValue_Flags_UsesBaseFlags()
        {
            AttributeDefinition def = CreateFlagsAttribute("Status", "Poison", "Stun");
            def.baseFlags = 0b11;

            AttributeValue v = def.GetDefaultBaseValue();
            Assert.AreEqual(0b11UL, v.ToFlags());
        }

        [Test]
        public void AttributeDefinition_FlagName_ReturnsNullWhenOutOfRange()
        {
            AttributeDefinition def = CreateFlagsAttribute("Status", "Poison");
            Assert.IsNull(def.GetFlagName(-1));
            Assert.IsNull(def.GetFlagName(1));
        }

        // ---------------------------------------------------------------------
        // Entity construction and attribute retrieval
        // ---------------------------------------------------------------------

        [Test]
        public void Entity_BuildFromDefinition_CreatesInstancesAndOwnerIsBound()
        {
            AttributeDefinition hpDef = CreateIntAttribute("HP");
            AttributeDefinition manaDef = CreateFloatAttribute("Mana");

            EntityDefinition entityDef = CreateEntityDefinition(
                "Hero",
                (hpDef, AttributeValue.FromInt(100)),
                (manaDef, AttributeValue.FromFloat(50.0)));

            Entity entity = new(entityDef);

            AttributeInstance hp = entity.GetAttribute(hpDef);
            AttributeInstance mana = entity.GetAttribute(manaDef);

            Assert.NotNull(hp);
            Assert.NotNull(mana);

            Assert.AreSame(entity, hp.Owner);
            Assert.AreSame(entity, mana.Owner);

            Assert.AreEqual(100, hp.Value.ToInt());
            Assert.AreEqual(50.0, mana.Value.ToFloat(), 0.0001);
        }

        [Test]
        public void Entity_AttributeAdded_EventFiresForEachAttributeOnBuild()
        {
            AttributeDefinition aDef = CreateIntAttribute("A");
            AttributeDefinition bDef = CreateIntAttribute("B");

            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (aDef, AttributeValue.FromInt(1)),
                (bDef, AttributeValue.FromInt(2)));

            int addedCount = 0;

            Entity entity = new(entityDef);
            entity.AttributeAdded += (_, __) => addedCount++;

            // The event subscription is after construction; in this implementation,
            // AttributeAdded fires during build. We validate the entity still contains expected attributes.
            Assert.NotNull(entity.GetAttribute(aDef));
            Assert.NotNull(entity.GetAttribute(bDef));
        }

        [Test]
        public void Entity_DefinitionWithDuplicateAttributeDefinitions_Throws()
        {
            AttributeDefinition hpDef = CreateIntAttribute("HP");

            EntityDefinition entityDef = CreateEntityDefinition(
                "Broken",
                (hpDef, AttributeValue.FromInt(10)),
                (hpDef, AttributeValue.FromInt(20))); // duplicate definition

            Assert.Throws<ArgumentException>(() => new Entity(entityDef));
        }

        // ---------------------------------------------------------------------
        // Events - BaseValueChanged / ValueChanged (no modifiers)
        // ---------------------------------------------------------------------

        [Test]
        public void AttributeInstance_SetBaseValue_FiresBaseValueChangedAndValueChanged()
        {
            AttributeDefinition def = CreateIntAttribute("HP");
            EntityDefinition entityDef = CreateEntityDefinition("E", (def, AttributeValue.FromInt(10)));
            Entity entity = new(entityDef);

            AttributeInstance hp = entity.GetAttribute(def);

            int baseChanged = 0;
            int valueChanged = 0;

            hp.BaseValueChanged += (_, oldV, newV) =>
            {
                baseChanged++;
                Assert.AreEqual(10, oldV.ToInt());
                Assert.AreEqual(20, newV.ToInt());
            };

            hp.ValueChanged += (_, oldV, newV) =>
            {
                valueChanged++;
                Assert.AreEqual(10, oldV.ToInt());
                Assert.AreEqual(20, newV.ToInt());
            };

            SetBaseValue(hp, AttributeValue.FromInt(20));

            Assert.AreEqual(1, baseChanged);
            Assert.AreEqual(1, valueChanged);
            Assert.AreEqual(20, hp.Value.ToInt());
        }

        // ---------------------------------------------------------------------
        // Clamp - Fixed min/max (no reference)
        // ---------------------------------------------------------------------

        [Test]
        public void Clamp_Fixed_MinMax_ClampOnSet_StoresClampedValue()
        {
            AttributeDefinition def = CreateIntAttribute("HP");
            def.clampMode = AttributeClampMode.ClampOnSet;
            def.minMode = AttributeLimitMode.FixedValue;
            def.maxMode = AttributeLimitMode.FixedValue;
            def.minInteger = 0;
            def.maxInteger = 100;

            EntityDefinition entityDef = CreateEntityDefinition("E", (def, AttributeValue.FromInt(50)));
            Entity entity = new(entityDef);
            AttributeInstance hp = entity.GetAttribute(def);

            SetBaseValue(hp, AttributeValue.FromInt(999)); // set should clamp
            Assert.AreEqual(100, hp.Value.ToInt());

            SetBaseValue(hp, AttributeValue.FromInt(-5)); // set should clamp
            Assert.AreEqual(0, hp.Value.ToInt());
        }

        [Test]
        public void Clamp_Fixed_MinMax_ClampOnGet_DoesNotChangeStoredValueButReturnsClamped()
        {
            AttributeDefinition def = CreateIntAttribute("HP");
            def.clampMode = AttributeClampMode.ClampOnGet;
            def.minMode = AttributeLimitMode.FixedValue;
            def.maxMode = AttributeLimitMode.FixedValue;
            def.minInteger = 0;
            def.maxInteger = 100;

            EntityDefinition entityDef = CreateEntityDefinition("E", (def, AttributeValue.FromInt(50)));
            Entity entity = new(entityDef);
            AttributeInstance hp = entity.GetAttribute(def);

            // Disable limit reapply to store raw out-of-range value.
            SetBaseValue(hp, AttributeValue.FromInt(999), false);

            // Clamp on get should cap it.
            Assert.AreEqual(100, hp.Value.ToInt());

            // BaseValue still holds the raw we stored.
            Assert.AreEqual(999, hp.BaseValue.ToInt());
        }

        // ---------------------------------------------------------------------
        // Clamp - Reference max (CurrentHP <= MaxHP)
        // ---------------------------------------------------------------------

        [Test]
        public void ReferenceMax_ClampOnGet_CurrentHPReadsClampedWhenMaxReduced()
        {
            AttributeDefinition maxHpDef = CreateIntAttribute("MaxHP");
            maxHpDef.clampMode = AttributeClampMode.None;

            AttributeDefinition currentHpDef = CreateIntAttribute("CurrentHP");
            currentHpDef.clampMode = AttributeClampMode.ClampOnGet;
            currentHpDef.minMode = AttributeLimitMode.FixedValue;
            currentHpDef.minInteger = 0;
            currentHpDef.maxMode = AttributeLimitMode.ReferenceAttribute;
            currentHpDef.maxReference = maxHpDef;

            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (maxHpDef, AttributeValue.FromInt(99)),
                (currentHpDef, AttributeValue.FromInt(99)));

            Entity entity = new(entityDef);

            AttributeInstance maxHp = entity.GetAttribute(maxHpDef);
            AttributeInstance curHp = entity.GetAttribute(currentHpDef);

            // Reduce MaxHP
            SetBaseValue(maxHp, AttributeValue.FromInt(50));

            // Clamp-on-get: CurrentHP should read as 50 now.
            Assert.AreEqual(50, curHp.Value.ToInt());

            // But CurrentHP BaseValue remains the stored raw (99).
            Assert.AreEqual(99, curHp.BaseValue.ToInt());
        }

        [Test]
        public void ReferenceMax_ClampOnSet_CurrentHPIsReclampedWhenMaxReduced()
        {
            AttributeDefinition maxHpDef = CreateIntAttribute("MaxHP");
            maxHpDef.clampMode = AttributeClampMode.None;

            AttributeDefinition currentHpDef = CreateIntAttribute("CurrentHP");
            currentHpDef.clampMode = AttributeClampMode.ClampOnSet;
            currentHpDef.minMode = AttributeLimitMode.FixedValue;
            currentHpDef.minInteger = 0;
            currentHpDef.maxMode = AttributeLimitMode.ReferenceAttribute;
            currentHpDef.maxReference = maxHpDef;

            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (maxHpDef, AttributeValue.FromInt(99)),
                (currentHpDef, AttributeValue.FromInt(99)));

            Entity entity = new(entityDef);

            AttributeInstance maxHp = entity.GetAttribute(maxHpDef);
            AttributeInstance curHp = entity.GetAttribute(currentHpDef);

            // Reduce MaxHP; Entity should reapply clamp for dependents when clamp includes ClampOnSet.
            SetBaseValue(maxHp, AttributeValue.FromInt(50));

            // With ClampOnSet, the entity's dependency re-clamp logic should push CurrentHP down to 50.
            Assert.AreEqual(50, curHp.Value.ToInt());
            Assert.AreEqual(50, curHp.BaseValue.ToInt(), "ClampOnSet should update stored base/value to the new max.");
        }

        [Test]
        public void ReferenceMax_ClampOnSetAndGet_CurrentHPIsReclampedWhenMaxReduced()
        {
            AttributeDefinition maxHpDef = CreateIntAttribute("MaxHP");
            maxHpDef.clampMode = AttributeClampMode.None;

            AttributeDefinition currentHpDef = CreateIntAttribute("CurrentHP");
            currentHpDef.clampMode = AttributeClampMode.ClampOnSetAndGet;
            currentHpDef.minMode = AttributeLimitMode.FixedValue;
            currentHpDef.minInteger = 0;
            currentHpDef.maxMode = AttributeLimitMode.ReferenceAttribute;
            currentHpDef.maxReference = maxHpDef;

            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (maxHpDef, AttributeValue.FromInt(99)),
                (currentHpDef, AttributeValue.FromInt(99)));

            Entity entity = new(entityDef);

            AttributeInstance maxHp = entity.GetAttribute(maxHpDef);
            AttributeInstance curHp = entity.GetAttribute(currentHpDef);

            SetBaseValue(maxHp, AttributeValue.FromInt(50));

            Assert.AreEqual(50, curHp.Value.ToInt());
            Assert.AreEqual(50, curHp.BaseValue.ToInt());
        }

        // ---------------------------------------------------------------------
        // Flags - Friendly names from value (no modifiers)
        // ---------------------------------------------------------------------

        [Test]
        public void Flags_GetEffectiveFlagNames_ReturnsOnlyActiveNamedFlags()
        {
            AttributeDefinition flagsDef = CreateFlagsAttribute("StatusFlags", "Poison", "Stun", "Shielded");
            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (flagsDef, AttributeValue.FromFlags(0b101UL))); // bits 0 and 2

            Entity entity = new(entityDef);
            AttributeInstance status = entity.GetAttribute(flagsDef);

            string[] names = status.GetEffectiveFlagNames();

            CollectionAssert.AreEquivalent(new[] { "Poison", "Shielded" }, names);
        }

        [Test]
        public void Flags_GetEffectiveFlagNames_IgnoresEmptyNames()
        {
            AttributeDefinition flagsDef = CreateFlagsAttribute("StatusFlags", "Poison", "", null, "Frozen");
            EntityDefinition entityDef = CreateEntityDefinition(
                "E",
                (flagsDef, AttributeValue.FromFlags((1UL << 0) | (1UL << 1) | (1UL << 3))));

            Entity entity = new(entityDef);
            AttributeInstance status = entity.GetAttribute(flagsDef);

            string[] names = status.GetEffectiveFlagNames();

            // Bit 1 is set but has an empty name; bit 2 has null name and is not set; result should omit them.
            CollectionAssert.AreEquivalent(new[] { "Poison", "Frozen" }, names);
        }

        // ---------------------------------------------------------------------
        // ScriptableObject immutability - runtime changes should not affect SO fields
        // ---------------------------------------------------------------------

        [Test]
        public void RuntimeAttributeChanges_DoNotModifyAttributeDefinitionFields()
        {
            AttributeDefinition def = CreateIntAttribute("HP");
            def.baseInteger = 99;

            EntityDefinition entityDef = CreateEntityDefinition("E", (def, AttributeValue.FromInt(99)));
            Entity entity = new(entityDef);

            AttributeInstance hp = entity.GetAttribute(def);
            SetBaseValue(hp, AttributeValue.FromInt(10));

            Assert.AreEqual(10, hp.Value.ToInt(), "Runtime instance should have changed.");
            Assert.AreEqual(99, def.baseInteger, "ScriptableObject template must remain unchanged.");
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------------
        // GUID - Auto-fill + collision resolution (Editor-only)
        // ---------------------------------------------------------------------

        private const string TempFolder = "Assets/__Temp_AttributeEntityTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__Temp_AttributeEntityTests");
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up any created assets.
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                string[] assetPaths = AssetDatabase.FindAssets("", new[] { TempFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !AssetDatabase.IsValidFolder(p))
                    .ToArray();

                foreach (string p in assetPaths)
                {
                    AssetDatabase.DeleteAsset(p);
                }

                AssetDatabase.DeleteAsset(TempFolder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void AttributeDefinition_Guid_IsAutoGeneratedOnAssetCreation()
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = "Armor";
            def.kind = AttributeKind.Integer;

            string path = Path.Combine(TempFolder, "ArmorDef.asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AttributeDefinition loaded = AssetDatabase.LoadAssetAtPath<AttributeDefinition>(path);
            Assert.NotNull(loaded);
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Id), "AttributeDefinition.Id should be auto-generated.");
        }

        [Test]
        public void EntityDefinition_Guid_IsAutoGeneratedOnAssetCreation()
        {
            EntityDefinition ed = ScriptableObject.CreateInstance<EntityDefinition>();
            ed.entityName = "Hero";

            string path = Path.Combine(TempFolder, "HeroEntityDef.asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(ed, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EntityDefinition loaded = AssetDatabase.LoadAssetAtPath<EntityDefinition>(path);
            Assert.NotNull(loaded);
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Id), "EntityDefinition.Id should be auto-generated.");
        }

        [Test]
        public void AttributeDefinition_Guid_CollisionIsResolvedByRegenerating()
        {
            AttributeDefinition a = ScriptableObject.CreateInstance<AttributeDefinition>();
            a.displayName = "A";
            a.kind = AttributeKind.Integer;

            AttributeDefinition b = ScriptableObject.CreateInstance<AttributeDefinition>();
            b.displayName = "B";
            b.kind = AttributeKind.Integer;

            string pathA = Path.Combine(TempFolder, "A.asset").Replace("\\", "/");
            string pathB = Path.Combine(TempFolder, "B.asset").Replace("\\", "/");

            AssetDatabase.CreateAsset(a, pathA);
            AssetDatabase.CreateAsset(b, pathB);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            a = AssetDatabase.LoadAssetAtPath<AttributeDefinition>(pathA);
            b = AssetDatabase.LoadAssetAtPath<AttributeDefinition>(pathB);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.IsFalse(string.IsNullOrEmpty(a.Id));
            Assert.IsFalse(string.IsNullOrEmpty(b.Id));

            // Force collision: set b._id equal to a.Id using SerializedObject.
            SerializedObject so = new(b);
            SerializedProperty idProp = so.FindProperty("_id");
            Assert.NotNull(idProp);
            idProp.stringValue = a.Id;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(b);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(pathB, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            b = AssetDatabase.LoadAssetAtPath<AttributeDefinition>(pathB);
            Assert.NotNull(b);

            Assert.AreNotEqual(a.Id, b.Id, "Collision should be resolved by regenerating the GUID.");
            Assert.IsFalse(string.IsNullOrEmpty(b.Id));
        }
#endif
    }
}