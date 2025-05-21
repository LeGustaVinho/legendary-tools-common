using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using LegendaryTools.AttributeSystem;
using LegendaryTools.TagSystem;

namespace Tests
{
    public class AttributeFullCoverageTests
    {
        private EntityManager manager;
        private Entity rootEntity;
        private Entity parentEntity;

        [SetUp]
        public void SetUp()
        {
            // 1) Create a root entity for the manager
            rootEntity = new Entity();
            
            // 2) Create an EntityManager with the root
            manager = new EntityManager(rootEntity);
            manager.AddEntity(rootEntity);

            // 3) Create a 'parentEntity' that we can use as the parent for all attributes
            parentEntity = new Entity();
            parentEntity.Initialize(manager, new EntityData());
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up references
            parentEntity = null;
            manager = null;
            rootEntity = null;
        }

        #region Constructors

        /// <summary>
        /// Verifies that the Attribute(IEntity, AttributeData) constructor 
        /// sets up the Config via AttributeSystemFactory and links the parent.
        /// </summary>
        [Test]
        public void Attribute_Ctor_WithAttributeData_SetsConfigAndParent()
        {
            // Arrange
            AttributeData data = new AttributeData
            {
                MinCapacity = 5f,
                HasCapacity = true
            };

            // Act
            var att = new Attribute(parentEntity, data);

            // Assert
            Assert.IsNotNull(att.Config, 
                "The constructor that takes AttributeData should create an AttributeConfig internally.");
            Assert.AreSame(parentEntity, att.Parent, 
                "The 'Parent' property should match the entity passed to the constructor.");
            Assert.IsTrue(att.Config.Data.HasCapacity,
                "The newly created config's Data should mirror the provided AttributeData (HasCapacity).");
            Assert.AreEqual(5f, att.Config.Data.MinCapacity, 
                "The config's MinCapacity should match the data's MinCapacity.");
        }

        /// <summary>
        /// Verifies that the Attribute(IEntity, AttributeConfig) constructor 
        /// directly assigns the config and the parent.
        /// </summary>
        [Test]
        public void Attribute_Ctor_WithAttributeConfig_AssignsAsIs()
        {
            // Allowed to create a real AttributeConfig instance for coverage.
            // We just won't unit-test the config itself.
            AttributeData data = new AttributeData { HasCapacity = false };
            AttributeConfig config = ScriptableObject.CreateInstance<AttributeConfig>();
            config.Data = data;

            var att = new Attribute(parentEntity, config);
            Assert.AreSame(config, att.Config, 
                "Constructor should store the same config reference passed in.");
            Assert.AreSame(parentEntity, att.Parent, 
                "Parent must match the entity passed in the constructor.");

            // Cleanup
            Object.DestroyImmediate(config);
        }

        #endregion

        #region Modifiers: AddModifier, RemoveModifier, RemoveModifiers, Events

        /// <summary>
        /// Verifies that AddModifier returns true and invokes OnAttributeModAdd 
        /// if the modifier passes ModApplicationCanBeAccepted checks.
        /// </summary>
        [Test]
        public void Attribute_AddModifier_SuccessAndEvent()
        {
            // We'll create a main attribute and a "modifier attribute".
            AttributeData mainData = new AttributeData();
            Attribute mainAtt = new Attribute(parentEntity, mainData);

            // Create a simple modifier attribute (with no conditions => always accepted).
            AttributeData modData = new AttributeData();
            Attribute modAtt = new Attribute(parentEntity, modData)
            {
                Type = AttributeType.Modifier
            };

            bool eventFired = false;
            mainAtt.OnAttributeModAdd += (addedMod) => { eventFired = true; };

            // Act
            bool result = mainAtt.AddModifier(modAtt);

            // Assert
            Assert.IsTrue(result, "Should return true when successfully added.");
            Assert.Contains(modAtt, mainAtt.Modifiers, 
                "Modifier list should contain the new modifier.");
            Assert.IsTrue(eventFired, 
                "OnAttributeModAdd event should have fired.");
        }

        /// <summary>
        /// Verifies that AddModifier returns false (and no event) if ModApplicationCanBeAccepted fails.
        /// </summary>
        [Test]
        public void Attribute_AddModifier_FailsIfNotAccepted()
        {
            // The acceptance is governed by ModApplicationCanBeAccepted => we'll add a condition that can't be met.
            Attribute mainAtt = new Attribute(parentEntity, new AttributeData());
            
            // Build a "modifier" that has a failing condition.
            Attribute modifierAtt = new Attribute(parentEntity, new AttributeData())
            {
                Type = AttributeType.Modifier
            };
            // Create an impossible condition => attribute config not present in parent
            var impossibleConfig = ScriptableObject.CreateInstance<AttributeConfig>();
            var condition = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AllMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>
                {
                    new AttributeModifierCondition
                    {
                        Attribute = impossibleConfig,
                        Operator = AttributeModOperator.Greater,
                        Value = 9999f
                    }
                }
            };
            modifierAtt.ModifierConditions.Add(condition);

            bool addEventFired = false;
            mainAtt.OnAttributeModAdd += _ => addEventFired = true;

            // Act
            bool result = mainAtt.AddModifier(modifierAtt);

            // Assert
            Assert.IsFalse(result, "Should return false if the condition fails.");
            Assert.IsFalse(addEventFired, "Event should not fire on failure.");
            Assert.IsEmpty(mainAtt.Modifiers, "No modifiers were actually added.");

            // Cleanup
            Object.DestroyImmediate(impossibleConfig);
        }

        /// <summary>
        /// Verifies RemoveModifier returns true only if the attribute is currently in the Modifiers list, 
        /// and it fires the OnAttributeModRemove event.
        /// </summary>
        [Test]
        public void Attribute_RemoveModifier_SuccessAndEvent()
        {
            // Arrange
            Attribute mainAtt = new Attribute(parentEntity, new AttributeData());
            Attribute mod = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };
            mainAtt.AddModifier(mod);

            bool removeEventFired = false;
            mainAtt.OnAttributeModRemove += _ => removeEventFired = true;

            // Act
            bool result = mainAtt.RemoveModifier(mod);

            // Assert
            Assert.IsTrue(result, "Should return true if the modifier was present and removed.");
            Assert.IsFalse(mainAtt.Modifiers.Contains(mod), 
                "The modifier should be gone from .Modifiers list.");
            Assert.IsTrue(removeEventFired, 
                "Should have fired the OnAttributeModRemove event.");
        }

        /// <summary>
        /// Verifies RemoveModifier returns false (and no event) if the attribute is not in the Modifiers list.
        /// </summary>
        [Test]
        public void Attribute_RemoveModifier_FailIfNotPresent()
        {
            Attribute mainAtt = new Attribute(parentEntity, new AttributeData());
            Attribute mod = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };

            bool eventFired = false;
            mainAtt.OnAttributeModRemove += _ => eventFired = true;

            bool result = mainAtt.RemoveModifier(mod);
            Assert.IsFalse(result, "Return false if the modifier wasn't found in the list.");
            Assert.IsFalse(eventFired, "No remove event if it wasn't actually removed.");
        }

        /// <summary>
        /// Verifies RemoveModifiers(IEntity) removes all modifiers that share the same Parent == entity.
        /// Fires the OnAttributeModRemove event for each removed.
        /// </summary>
        [Test]
        public void Attribute_RemoveModifiers_RemovesAllFromSameEntity()
        {
            // We'll create multiple modifiers from the same child entity
            var childEntity = new Entity();
            childEntity.Initialize(manager, new EntityData());

            Attribute mainAtt = new Attribute(parentEntity, new AttributeData());
            Attribute mod1 = new Attribute(childEntity, new AttributeData()) { Type = AttributeType.Modifier };
            Attribute mod2 = new Attribute(childEntity, new AttributeData()) { Type = AttributeType.Modifier };
            Attribute mod3 = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier }; 
            // ^ has Parent=parentEntity => won't be removed by RemoveModifiers(childEntity)

            mainAtt.AddModifier(mod1);
            mainAtt.AddModifier(mod2);
            mainAtt.AddModifier(mod3);

            int removeEventCount = 0;
            mainAtt.OnAttributeModRemove += _ => removeEventCount++;

            // Act
            mainAtt.RemoveModifiers(childEntity);

            // Assert
            Assert.IsFalse(mainAtt.Modifiers.Contains(mod1),
                "mod1 should be removed (same childEntity parent).");
            Assert.IsFalse(mainAtt.Modifiers.Contains(mod2),
                "mod2 should be removed (same childEntity parent).");
            Assert.IsTrue(mainAtt.Modifiers.Contains(mod3),
                "mod3 remains (different Parent).");
            Assert.AreEqual(2, removeEventCount,
                "Fired OnAttributeModRemove for mod1 and mod2 only.");
        }

        #endregion

        #region ModApplicationCanBeAccepted

        /// <summary>
        /// Directly tests ModApplicationCanBeAccepted:
        /// - Null => false
        /// - Empty conditions => true
        /// - Single condition pass/fail
        /// - AllMustBeTrue vs AnyMustBeTrue
        /// </summary>
        [Test]
        public void Attribute_ModApplicationCanBeAccepted_VariousConditions()
        {
            // Create mainAtt so parentEntity has it
            Attribute mainAtt = new Attribute(parentEntity, new AttributeData());
            parentEntity.AddAttribute(mainAtt);
            mainAtt.Flat = 10f; // so mainAtt.Value=10

            // (A) If attributeModifier is null => false
            Assert.IsFalse(mainAtt.ModApplicationCanBeAccepted(null));

            // (B) No conditions => true
            var emptyMod = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };
            Assert.IsTrue(mainAtt.ModApplicationCanBeAccepted(emptyMod),
                "Empty conditions => trivially accepted.");

            // (C) Single condition => "mainAtt.Value > 5" => true
            var condConfig = mainAtt.Config;
            var cond = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AllMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>
                {
                    new AttributeModifierCondition
                    {
                        Attribute = condConfig,
                        Operator = AttributeModOperator.Greater,
                        Value = 5f
                    }
                }
            };
            var mod1 = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };
            mod1.ModifierConditions.Add(cond);
            Assert.IsTrue(mainAtt.ModApplicationCanBeAccepted(mod1));

            // (D) Fails => "mainAtt.Value < 5" => false
            var failCond = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AllMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>
                {
                    new AttributeModifierCondition
                    {
                        Attribute = condConfig,
                        Operator = AttributeModOperator.Less,
                        Value = 5f
                    }
                }
            };
            var failMod = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };
            failMod.ModifierConditions.Add(failCond);
            Assert.IsFalse(mainAtt.ModApplicationCanBeAccepted(failMod));

            // (E) ANY must be true => if at least one passes => overall true
            var orCond = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AnyMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>
                {
                    new AttributeModifierCondition
                    {
                        Attribute = condConfig,
                        Operator = AttributeModOperator.Less,
                        Value = 5f
                    },
                    new AttributeModifierCondition
                    {
                        Attribute = condConfig,
                        Operator = AttributeModOperator.Greater,
                        Value = 1f
                    }
                }
            };
            var orMod = new Attribute(parentEntity, new AttributeData()) { Type = AttributeType.Modifier };
            orMod.ModifierConditions.Add(orCond);
            Assert.IsTrue(mainAtt.ModApplicationCanBeAccepted(orMod),
                "ANY => 10 > 1 => passes => overall true.");
        }

        #endregion

        #region Usage/Capacity: AddUsage, RemoveUsage, OnAttributeCapacityChange

        [Test]
        public void Attribute_AddUsage_ErrorNoCapacity()
        {
            AttributeData data = new AttributeData { HasCapacity = false };
            var att = new Attribute(parentEntity, data);

            var status = att.AddUsage(10f);
            Assert.AreEqual(AttributeUsageStatus.ErrorNoCapacity, status);
        }

        [Test]
        public void Attribute_AddUsage_ErrorNegativeValue()
        {
            AttributeData data = new AttributeData { HasCapacity = true };
            var att = new Attribute(parentEntity, data);

            var status = att.AddUsage(-5f);
            Assert.AreEqual(AttributeUsageStatus.ErrorNegativeValue, status);
        }

        [Test]
        public void Attribute_AddUsage_ErrorBelowMinimum()
        {
            // MinCapacity=3 => if we add 2 => newCapacity=2 => <3 => ErrorBelowMinimum
            var data = new AttributeData
            {
                HasCapacity = true,
                MinCapacity = 3f
            };
            var att = new Attribute(parentEntity, data);

            var status = att.AddUsage(2f);
            Assert.AreEqual(AttributeUsageStatus.ErrorBelowMinimum, status);
        }

        [Test]
        public void Attribute_AddUsage_ClampsToMax()
        {
            // If newCapacity>Value && !AllowExceedCapacity => clamp => WarningClampedToMax
            var data = new AttributeData
            {
                HasCapacity = true,
                AllowExceedCapacity = false,
                MinCapacity = 0f
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 10f; // => Value=10

            bool eventFired = false;
            att.OnAttributeCapacityChange += (_, __) => eventFired = true;

            var status = att.AddUsage(15f); // would be 15 => clamp to 10
            Assert.AreEqual(AttributeUsageStatus.WarningClampedToMax, status);
            Assert.AreEqual(10f, att.CurrentValue);
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void Attribute_AddUsage_Success()
        {
            var data = new AttributeData
            {
                HasCapacity = true,
                AllowExceedCapacity = true,
                MinCapacity = 0f
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 10f; // => Value=10

            float oldVal = att.CurrentValue; // 0
            bool eventFired = false;
            att.OnAttributeCapacityChange += (_, __) => eventFired = true;

            var status = att.AddUsage(5f); // => 5 <= Value(10) => success
            Assert.AreEqual(AttributeUsageStatus.Success, status);
            Assert.AreEqual(5f, att.CurrentValue);
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void Attribute_RemoveUsage_ErrorNoCapacity()
        {
            var att = new Attribute(parentEntity, new AttributeData { HasCapacity = false });
            var status = att.RemoveUsage(1f);
            Assert.AreEqual(AttributeUsageStatus.ErrorNoCapacity, status);
        }

        [Test]
        public void Attribute_RemoveUsage_ErrorNegativeValue()
        {
            var att = new Attribute(parentEntity, new AttributeData { HasCapacity = true });
            var status = att.RemoveUsage(-5f);
            Assert.AreEqual(AttributeUsageStatus.ErrorNegativeValue, status);
        }

        [Test]
        public void Attribute_RemoveUsage_ClampsToMin()
        {
            var att = new Attribute(parentEntity, new AttributeData
            {
                HasCapacity = true,
                MinCapacity = 2f
            });
            att.Flat = 10f; // => Value=10
            att.AddUsage(5f); // sets CurrentValue=5

            bool eventFired = false;
            att.OnAttributeCapacityChange += (_, __) => eventFired = true;

            // removing 10 => newCapacity=5-10=-5 => clamp up to 2 => WarningClampedToMinimum
            var status = att.RemoveUsage(10f);
            Assert.AreEqual(AttributeUsageStatus.WarningClampedToMinimum, status);
            Assert.AreEqual(2f, att.CurrentValue);
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void Attribute_RemoveUsage_Success()
        {
            var att = new Attribute(parentEntity, new AttributeData
            {
                HasCapacity = true,
                MinCapacity = 0f
            });
            att.Flat = 10f;
            att.AddUsage(7f); // => CurrentValue=7

            bool eventFired = false;
            att.OnAttributeCapacityChange += (_, __) => eventFired = true;

            var status = att.RemoveUsage(3f); // => 4 => success
            Assert.AreEqual(AttributeUsageStatus.Success, status);
            Assert.AreEqual(4f, att.CurrentValue);
            Assert.IsTrue(eventFired);
        }

        #endregion

        #region Value Calculation / GetValueWithModifiers

        [Test]
        public void Attribute_Value_NoConfig_ReturnsZero()
        {
            // If config is null => returns 0
            Attribute att = new Attribute(parentEntity, (AttributeConfig)null);
            Assert.AreEqual(0f, att.Value);
        }

        [Test]
        public void Attribute_Value_FlagOptions_AppliesBitwiseOps()
        {
            // HasOptions=true, OptionsAreFlags=true => bitwise logic
            var data = new AttributeData
            {
                OptionsAreFlags = true,
                Options = new [] {"1", "2", "3", "4", "5", "6", "7"}
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 1f; // start => 0001

            // AddFlag => +2 => 0011 => RemoveFlag => -1 => 0010 => Set => 8 => 1000
            var modAdd = new Attribute(parentEntity, data)
            {
                Type = AttributeType.Modifier,
                FlagOperator = AttributeFlagModOperator.AddFlag,
                Flat = 2f
            };
            var modRemove = new Attribute(parentEntity, data)
            {
                Type = AttributeType.Modifier,
                FlagOperator = AttributeFlagModOperator.RemoveFlag,
                Flat = 1f
            };
            var modSet = new Attribute(parentEntity, data)
            {
                Type = AttributeType.Modifier,
                FlagOperator = AttributeFlagModOperator.Set,
                Flat = 8f
            };
            att.AddModifier(modAdd);
            att.AddModifier(modRemove);
            att.AddModifier(modSet);

            Assert.AreEqual(8f, att.Value);
        }

        [Test]
        public void Attribute_Value_OptionsButNotFlags_OverridesWithLastModifierFlat()
        {
            var data = new AttributeData
            {
                OptionsAreFlags = false,
                Options = new [] {"1", "2", "3", "4", "5", "6", "7"}
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 0f; // start

            var mod1 = new Attribute(parentEntity, data) { Flat = 5f };
            var mod2 = new Attribute(parentEntity, data) { Flat = 3f };
            var mod3 = new Attribute(parentEntity, data) { Flat = 99f };
            att.AddModifier(mod1);
            att.AddModifier(mod2);
            att.AddModifier(mod3);

            Assert.AreEqual(99f, att.Value,
                "Non-flag options => final override is the last modifier's Flat.");
        }

        [Test]
        public void Attribute_Value_NormalWithStackPenalties()
        {
            var data = new AttributeData
            {
                StackPenaults = new float[] { 1f, 0.5f, 0.2f }
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 5f; 
            att.Factor = 0.1f; 

            // 3 modifiers, each with Flat=2, Factor=0.2
            var m1 = new Attribute(parentEntity, data) { Flat = 2f, Factor = 0.2f };
            var m2 = new Attribute(parentEntity, data) { Flat = 2f, Factor = 0.2f };
            var m3 = new Attribute(parentEntity, data) { Flat = 2f, Factor = 0.2f };
            att.AddModifier(m1);
            att.AddModifier(m2);
            att.AddModifier(m3);

            // totalFlat=6, totalFactor= 0.2*(1f)+0.2*(0.5)+0.2*(0.2)= 0.2+0.1+0.04=0.34
            // final => (5+6)*(1+0.1+0.34)=11*(1.44)=15.84
            Assert.AreEqual(15.84f, att.Value, 0.0001f);
        }

        [Test]
        public void Attribute_Value_ClampMinMax()
        {
            var data = new AttributeData
            {
                HasMinMax = true,
                MinMaxValue = new Vector2(5f, 10f)
            };
            var att = new Attribute(parentEntity, data);

            // If we set Flat=3 => final => 3 => clamp => 5
            att.Flat = 3f;
            Assert.AreEqual(5f, att.Value);

            // If we set Flat=12 => final => 12 => clamp => 10
            att.Flat = 12f;
            Assert.AreEqual(10f, att.Value);
        }

        #endregion

        #region Value Conversions

        [Test]
        public void Attribute_ValueConversions_CoversAllProperties()
        {
            // We'll set up a config that has string[] Options => e.g. 0->A, 1->B, 2->C
            var data = new AttributeData
            {
                Options = new[] { "A", "B", "C" }
            };
            var att = new Attribute(parentEntity, data);
            att.Flat = 1f;

            Assert.AreEqual(true, att.ValueAsBool);
            Assert.AreEqual((short)1, att.ValueAsShort);
            Assert.AreEqual(1, att.ValueAsInt);
            Assert.AreEqual(1L, att.ValueAsLong);

            Assert.AreEqual("B", att.ValueAsOption,
                "Index=1 => 'B'");

            att.Flat = 5f; // out of range => empty
            Assert.AreEqual(string.Empty, att.ValueAsOption);

            // ValueAsOptionFlag => just cast to int
            Assert.AreEqual(5, att.ValueAsOptionFlag);
        }

        [Test]
        public void Attribute_FlatAsOptionIndex_And_FlatAsOptionFlag()
        {
            var att = new Attribute(parentEntity, new AttributeData());
            att.FlatAsOptionIndex = 2; 
            Assert.AreEqual(2f, att.Flat);

            att.FlatAsOptionFlag = 5;
            Assert.AreEqual(5f, att.Flat);
            Assert.AreEqual(5, att.FlatAsOptionIndex);
            Assert.AreEqual(5, att.FlatAsOptionFlag);
        }

        #endregion

        #region Cloning

        [Test]
        public void Attribute_Clone_CopiesAllButModifiers()
        {
            var att = new Attribute(parentEntity, new AttributeData());
            att.Flat = 10f;
            att.Factor = 0.2f;
            att.FlagOperator = AttributeFlagModOperator.Set;
            att.Type = AttributeType.Attribute; // ensures capacity usage is allowed
            att.ForceApplyIfMissing = true;

            // Enable capacity usage
            att.Config.Data.HasCapacity = true;
            att.AddUsage(3f);

            // This is the correct check for usage
            Assert.AreEqual(3f, att.CurrentValue, 
                "Usage was set to 3, so CurrentValue should be 3.");

            // Add a condition for checking if it clones conditions
            var cond = new AttributeCondition();
            att.ModifierConditions.Add(cond);

            // Clone it
            var otherEntity = new Entity();
            otherEntity.Initialize(manager, new EntityData());

            Attribute cloned = att.Clone(otherEntity);

            // Verify capacity usage is also copied
            Assert.AreEqual(3f, cloned.CurrentValue, 0.0001f, 
                "Clone's CurrentValue matches original usage.");

            // Check other fields
            Assert.AreEqual(10f, cloned.Flat);
            Assert.AreEqual(0.2f, cloned.Factor);
            Assert.AreEqual(AttributeFlagModOperator.Set, cloned.FlagOperator);
            Assert.AreEqual(AttributeType.Attribute, cloned.Type);
            Assert.IsTrue(cloned.ForceApplyIfMissing);
            Assert.AreEqual(1, cloned.ModifierConditions.Count);
            Assert.AreSame(otherEntity, cloned.Parent);
        }

        #endregion
    }
}
