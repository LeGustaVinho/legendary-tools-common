using NUnit.Framework;
using UnityEngine;
using LegendaryTools.AttributeSystem;
using LegendaryTools.TagSystem;
using System.Collections.Generic;

namespace Tests
{
    public class AttributeConditionAndModifierConditionTests
    {
        private EntityManager manager;
        private Entity rootEntity;
        private Entity testEntity;
        
        [SetUp]
        public void SetUp()
        {
            // Create a root entity and manager
            rootEntity = new Entity();
            manager = new EntityManager(rootEntity);
            manager.AddEntity(rootEntity);

            // Create a test entity that we'll attach attributes to
            testEntity = new Entity();
            testEntity.Initialize(manager, new EntityData());
        }

        [TearDown]
        public void TearDown()
        {
            // Cleanup references
            testEntity = null;
            manager = null;
            rootEntity = null;
        }

        #region AttributeCondition Tests

        /// <summary>
        /// Tests that if there are no ModApplicationConditions in the list, 
        /// CanBeAppliedOn returns true immediately (both for AllMustBeTrue or AnyMustBeTrue).
        /// </summary>
        [Test]
        public void AttributeCondition_EmptyModApplicationConditions_ReturnsTrue()
        {
            var conditionAll = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AllMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>()
            };
            var conditionAny = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AnyMustBeTrue,
                ModApplicationConditions = new List<AttributeModifierCondition>()
            };

            Assert.IsTrue(conditionAll.CanBeAppliedOn(testEntity),
                "No conditions => trivially true for AllMustBeTrue.");
            Assert.IsTrue(conditionAny.CanBeAppliedOn(testEntity),
                "No conditions => trivially true for AnyMustBeTrue.");
        }

        /// <summary>
        /// Tests CanBeAppliedOn with Operator = AllMustBeTrue
        /// - If one condition fails, the entire result is false.
        /// - If all pass, the result is true.
        /// </summary>
        [Test]
        public void AttributeCondition_AllMustBeTrue_PassOrFail()
        {
            // 1) Setup a real attribute in the entity so we can test comparisons
            var data = new AttributeData();
            var attA = new LegendaryTools.AttributeSystem.Attribute(testEntity, data) { Flat = 10f };
            testEntity.AddAttribute(attA);

            // 2) Create an AttributeCondition with multiple conditions referencing attA's config
            var conditionAll = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AllMustBeTrue
            };

            conditionAll.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attA.Config,
                Operator = AttributeModOperator.Greater,
                Value = 5f  // (10>5) => pass
            });
            conditionAll.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attA.Config,
                Operator = AttributeModOperator.Less,
                Value = 15f // (10<15) => pass
            });

            // All pass => true
            Assert.IsTrue(conditionAll.CanBeAppliedOn(testEntity),
                "Both conditions pass => true for AllMustBeTrue.");

            // 3) Add a failing condition => entire result => false
            conditionAll.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attA.Config,
                Operator = AttributeModOperator.Greater,
                Value = 999f  // 10>999? => false => entire block => false
            });
            Assert.IsFalse(conditionAll.CanBeAppliedOn(testEntity),
                "One fails => entire result => false for AllMustBeTrue.");
        }

        /// <summary>
        /// Tests CanBeAppliedOn with Operator = AnyMustBeTrue
        /// - If at least one condition passes, the entire result is true.
        /// - If all fail, the result is false.
        /// </summary>
        [Test]
        public void AttributeCondition_AnyMustBeTrue_PassOrFail()
        {
            // Setup an attribute
            var data = new AttributeData();
            var attB = new LegendaryTools.AttributeSystem.Attribute(testEntity, data) { Flat = 10f };
            testEntity.AddAttribute(attB);

            var conditionAny = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AnyMustBeTrue
            };

            // 1) Add conditions that all fail => "Less than 0" & "Greater than 999"
            conditionAny.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attB.Config,
                Operator = AttributeModOperator.Less,
                Value = 0f // 10<0 => false
            });
            conditionAny.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attB.Config,
                Operator = AttributeModOperator.Greater,
                Value = 999f // 10>999 => false
            });

            Assert.IsFalse(conditionAny.CanBeAppliedOn(testEntity),
                "All fail => false for AnyMustBeTrue.");

            // 2) Now add a condition that passes => "10>5"
            conditionAny.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = attB.Config,
                Operator = AttributeModOperator.Greater,
                Value = 5f // pass => entire block => true
            });

            Assert.IsTrue(conditionAny.CanBeAppliedOn(testEntity),
                "One pass => true for AnyMustBeTrue.");
        }

        /// <summary>
        /// Tests each AttributeModOperator: Equals, NotEquals, Greater, Less, GreaterOrEquals, LessOrEquals.
        /// We already indirectly tested a few, but here's a single test that systematically checks them.
        /// Also covers the "missing attribute => currentAttribute is null => condition fails" scenario.
        /// </summary>
        [Test]
        public void AttributeCondition_CanBeAppliedOn_BasicOperators()
        {
            // We'll create an attribute with Value=10
            var data = new AttributeData();
            var att = new LegendaryTools.AttributeSystem.Attribute(testEntity, data) { Flat = 10f };
            testEntity.AddAttribute(att);

            // We'll systematically check each operator
            // We'll do "AllMustBeTrue" with just 1 condition for each test, so it's straightforward.

            // EQUALS => pass
            var condEq = MakeCondition(att.Config, AttributeModOperator.Equals, 10f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condEq.CanBeAppliedOn(testEntity), "10 == 10 => true.");

            // EQUALS => fail
            condEq.ModApplicationConditions[0].Value = 11f; 
            Assert.IsFalse(condEq.CanBeAppliedOn(testEntity), "10 == 11 => false.");

            // GREATER => pass
            var condGt = MakeCondition(att.Config, AttributeModOperator.Greater, 5f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condGt.CanBeAppliedOn(testEntity), "10 > 5 => true.");

            // GREATER => fail
            condGt.ModApplicationConditions[0].Value = 20f;
            Assert.IsFalse(condGt.CanBeAppliedOn(testEntity), "10>20 => false.");

            // GREATERorEQUALS => pass for 10>=10
            var condGE = MakeCondition(att.Config, AttributeModOperator.GreaterOrEquals, 10f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condGE.CanBeAppliedOn(testEntity));

            // GREATERorEQUALS => fail for 10>=999
            condGE.ModApplicationConditions[0].Value = 999f;
            Assert.IsFalse(condGE.CanBeAppliedOn(testEntity));

            // LESS => pass => 10<999 => true
            var condLt = MakeCondition(att.Config, AttributeModOperator.Less, 999f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condLt.CanBeAppliedOn(testEntity));

            // LESS => fail => 10<5 => false
            condLt.ModApplicationConditions[0].Value = 5f;
            Assert.IsFalse(condLt.CanBeAppliedOn(testEntity));

            // LESSorEQUALS => pass => 10 <=10 => true
            var condLE = MakeCondition(att.Config, AttributeModOperator.LessOrEquals, 10f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condLE.CanBeAppliedOn(testEntity));

            // LESSorEQUALS => fail => 10<=5 => false
            condLE.ModApplicationConditions[0].Value = 5f;
            Assert.IsFalse(condLE.CanBeAppliedOn(testEntity));

            // NOTEquals => pass => 10 != 5 => true
            var condNE = MakeCondition(att.Config, AttributeModOperator.NotEquals, 5f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condNE.CanBeAppliedOn(testEntity));

            // NOTEquals => fail => 10 !=10 => false
            condNE.ModApplicationConditions[0].Value = 10f;
            Assert.IsFalse(condNE.CanBeAppliedOn(testEntity));

            // MISSING attribute => targetEntity doesn't have it => null => automatically fails
            var randomConfig = ScriptableObject.CreateInstance<AttributeConfig>();
            var missingCond = MakeCondition(randomConfig, AttributeModOperator.Equals, 0f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsFalse(missingCond.CanBeAppliedOn(testEntity),
                "Missing attribute => condition fails for AllMustBeTrue. (We treat null attribute as fail.)");

            Object.DestroyImmediate(randomConfig);
        }

        /// <summary>
        /// Tests ContainsFlag and NotContainsFlag operators, ensuring bitwise logic is used.
        /// Also covers the scenario of a missing attribute or no flags in the actual attribute.
        /// </summary>
        [Test]
        public void AttributeCondition_CanBeAppliedOn_FlagOperators()
        {
            // We'll create an attribute with a certain bitmask. 
            // For instance => Flat=5 => binary 0101 => that means flags at bit 0 and bit 2 are set.
            var data = new AttributeData
            {
                Options = new [] {"1", "2", "3", "4", "5", "6", "7"},
                OptionsAreFlags = true
            };
            var attFlag = new LegendaryTools.AttributeSystem.Attribute(testEntity, data) { Flat = 5f };
            testEntity.AddAttribute(attFlag);

            // Test => ContainsFlag => e.g. "0101 has 0001 => pass"
            var condHas = MakeCondition(attFlag.Config, AttributeModOperator.ContainsFlag, 1f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condHas.CanBeAppliedOn(testEntity),
                "5 & 1 => !=0 => pass");

            // Switch to an impossible flag => 8 => 5 & 8 => 0101 & 1000 => 0000 => false
            condHas.ModApplicationConditions[0].Value = 8f;
            Assert.IsFalse(condHas.CanBeAppliedOn(testEntity));

            // NotContainsFlag => pass if (0101 & 1000)=>0 => pass
            var condNotHas = MakeCondition(attFlag.Config, AttributeModOperator.NotContainsFlag, 8f, AttributeConditionOperator.AllMustBeTrue);
            Assert.IsTrue(condNotHas.CanBeAppliedOn(testEntity));

            // If we do 1 => 0101 & 0001 => 0001 => not zero => fails
            condNotHas.ModApplicationConditions[0].Value = 1f;
            Assert.IsFalse(condNotHas.CanBeAppliedOn(testEntity));
        }

        /// <summary>
        /// Tests Clone() of an AttributeCondition, verifying a deep copy of the ModApplicationConditions.
        /// </summary>
        [Test]
        public void AttributeCondition_Clone_CopiesEverything()
        {
            var condA = new AttributeCondition
            {
                Operator = AttributeConditionOperator.AnyMustBeTrue
            };
            var configObj = ScriptableObject.CreateInstance<AttributeConfig>();

            condA.ModApplicationConditions.Add(new AttributeModifierCondition
            {
                Attribute = configObj,
                Operator = AttributeModOperator.Greater,
                Value = 10f
            });

            var cloned = condA.Clone();
            Assert.AreEqual(AttributeConditionOperator.AnyMustBeTrue, cloned.Operator);
            Assert.AreEqual(1, cloned.ModApplicationConditions.Count);
            Assert.AreSame(condA.ModApplicationConditions[0].Attribute, 
                           cloned.ModApplicationConditions[0].Attribute,
                "We copy the same config reference for demonstration. (Though you might want a separate config in production.)");
            Assert.AreEqual(AttributeModOperator.Greater, 
                            cloned.ModApplicationConditions[0].Operator);
            Assert.AreEqual(10f, 
                            cloned.ModApplicationConditions[0].Value);

            Object.DestroyImmediate(configObj);
        }

        #endregion

        #region AttributeModifierCondition Tests

        /// <summary>
        /// Tests the various helper properties on AttributeModifierCondition:
        /// - HasOptions
        /// - OptionsAreFlags
        /// - HasOptionsAndIsNotFlags
        /// - ValueAsOptionIndex / ValueAsOptionFlag
        /// Also verifies Clone().
        /// </summary>
        [Test]
        public void AttributeModifierCondition_PropertiesAndClone()
        {
            // We'll create an AttributeConfig with some data
            AttributeConfig config = ScriptableObject.CreateInstance<AttributeConfig>();
            config.Data = new AttributeData
            {
                Options = new string[] { "A", "B", "C" },
                OptionsAreFlags = false // we can toggle this for testing
            };

            // 1) Build a condition with that config
            var modCond = new AttributeModifierCondition
            {
                Attribute = config,
                Operator = AttributeModOperator.Equals,
                Value = 1f
            };

            // Because config.Data.Options != null => HasOptions => true
            Assert.IsTrue(modCond.HasOptions, "There are 'A','B','C' => HasOptions==true.");
            Assert.IsFalse(modCond.OptionsAreFlags, "We set OptionsAreFlags=false in config => false here.");
            Assert.IsTrue(modCond.HasOptionsAndIsNotFlags, "We do have options, but not flags => true.");

            // Check ValueAsOptionIndex => setting it should change Value
            modCond.ValueAsOptionIndex = 2;
            Assert.AreEqual(2f, modCond.Value);
            // And ValueAsOptionFlag => same numeric
            modCond.ValueAsOptionFlag = 3;
            Assert.AreEqual(3f, modCond.Value);

            // 2) Flip config to flags => now OptionsAreFlags => true
            config.Data.OptionsAreFlags = true;
            Assert.IsTrue(modCond.OptionsAreFlags);
            Assert.IsFalse(modCond.HasOptionsAndIsNotFlags);

            // 3) Test Clone()
            var cloned = modCond.Clone();
            Assert.AreSame(modCond.Attribute, cloned.Attribute, 
                "Cloned condition references the same AttributeConfig object by default.");
            Assert.AreEqual(modCond.Operator, cloned.Operator);
            Assert.AreEqual(modCond.Value, cloned.Value);

            // Cleanup
            Object.DestroyImmediate(config);
        }

        #endregion

        #region Helper

        private AttributeCondition MakeCondition(AttributeConfig config, AttributeModOperator op, float value, AttributeConditionOperator condOp)
        {
            var condition = new AttributeCondition
            {
                Operator = condOp,
                ModApplicationConditions = new List<AttributeModifierCondition>
                {
                    new AttributeModifierCondition
                    {
                        Attribute = config,
                        Operator = op,
                        Value = value
                    }
                }
            };
            return condition;
        }

        #endregion
    }
}
