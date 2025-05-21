using NUnit.Framework;
using UnityEngine;
using LegendaryTools.AttributeSystem;
using LegendaryTools.TagSystem;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.TestTools;

namespace Tests
{
    public class EntityFullCoverageTests
    {
        private EntityManager manager;
        private Entity rootEntity;

        [SetUp]
        public void SetUp()
        {
            // Create a root entity for the EntityManager
            rootEntity = new Entity();
            manager = new EntityManager(rootEntity);
            manager.AddEntity(rootEntity);
        }

        [TearDown]
        public void TearDown()
        {
            // Entities are not UnityEngine.Objects, so no DestroyImmediate calls on them
            manager = null;
            rootEntity = null;
        }

        #region Basic Initialization & Destruction

        /// <summary>
        /// Tests the no-parameter Initialize(EntityManager) 
        /// to ensure it adds the entity into the manager.
        /// </summary>
        [Test]
        public void Entity_Initialize_NoConfig()
        {
            // Arrange
            Entity entity = new Entity();

            // Act
            entity.Initialize(manager);

            // Assert
            Assert.IsTrue(manager.Entities.Contains(entity),
                "Manager should contain the newly-initialized entity with no config.");
        }

        /// <summary>
        /// Tests the Initialize(EntityManager, EntityData) path 
        /// to verify Data -> Config creation and manager registration.
        /// </summary>
        [Test]
        public void Entity_Initialize_WithEntityData()
        {
            // Arrange
            EntityData data = new EntityData // empty attributes, tags, etc.
            {
                // Could set up some TagFilterMatches, but not mandatory for coverage
            };
            Entity entity = new Entity();

            // Act
            entity.Initialize(manager, data);

            // Assert
            Assert.IsTrue(manager.Entities.Contains(entity),
                "Manager should contain the newly-initialized entity.");
            Assert.IsNotNull(entity.Attributes, 
                "Entity.Attributes should never be null after Initialize.");
            Assert.AreEqual(0, entity.Attributes.Count, 
                "No attributes were in the EntityData, so should have 0 attributes.");
        }

        /// <summary>
        /// Demonstrates removing an Entity from the system via Destroy().
        /// Ensures it is removed from the EntityManager.
        /// </summary>
        [Test]
        public void Entity_Destroy_RemovesFromManager()
        {
            // Arrange
            Entity entity = new Entity();
            entity.Initialize(manager, new EntityData());
            Assert.IsTrue(manager.Entities.Contains(entity));

            // Act
            entity.Destroy();

            // Assert
            Assert.IsFalse(manager.Entities.Contains(entity),
                "Destroyed entity should be removed from the manager's list.");
        }

        #endregion

        #region Tags and OnlyAcceptTags

        /// <summary>
        /// Tests ContainsTag returns false if the tag is null or not found,
        /// and true if the tag is indeed in the entity.
        /// </summary>
        [Test]
        public void Entity_ContainsTag_EdgeCases()
        {
            // Arrange
            Tag tagA = ScriptableObject.CreateInstance<Tag>();
            tagA.Name = "TagA";

            Tag tagB = ScriptableObject.CreateInstance<Tag>();
            tagB.Name = "TagB";

            EntityData data = new EntityData
            {
                tags = new Tag[] { tagA } 
            };

            Entity entity = new Entity();
            entity.Initialize(manager, data);

            // Act & Assert
            Assert.IsFalse(entity.ContainsTag(null),
                "Entity should return false if tag is null.");
            Assert.IsTrue(entity.ContainsTag(tagA),
                "Entity has tagA, so it should be true.");
            Assert.IsFalse(entity.ContainsTag(tagB),
                "Entity does not have tagB, so it should be false.");

            // Cleanup
            Object.DestroyImmediate(tagA);
            Object.DestroyImmediate(tagB);
        }

        /// <summary>
        /// Covers the code path for TryToApplyTo when parent accepts or rejects the child.
        /// (You might want to break this into separate tests, but here we do both.)
        /// </summary>
        [Test]
        public void Entity_TryToApplyTo_ParentAcceptsAndRejects()
        {
            // Arrange
            Tag requiredTag = ScriptableObject.CreateInstance<Tag>();
            requiredTag.Name = "RequiredTag";

            Tag optionalTag = ScriptableObject.CreateInstance<Tag>();
            optionalTag.Name = "OptionalTag";

            // Parent requires "RequiredTag"
            EntityData parentData = new EntityData
            {
                onlyAcceptTags = new TagFilterMatch[]
                {
                    new TagFilterMatch(requiredTag, TagFilterRuleType.Include)
                }
            };
            Entity parentEntity = new Entity();
            parentEntity.Initialize(manager, parentData);

            // Child #1 has the required tag => should succeed
            EntityData childData1 = new EntityData
            {
                tags = new Tag[] { requiredTag, optionalTag }
            };
            Entity childEntity1 = new Entity();
            childEntity1.Initialize(manager, childData1);

            // Child #2 does NOT have the required tag => should fail
            EntityData childData2 = new EntityData
            {
                tags = new Tag[] { optionalTag }
            };
            Entity childEntity2 = new Entity();
            childEntity2.Initialize(manager, childData2);

            // Act
            var (success1, conn1) = childEntity1.TryToApplyTo(parentEntity);
            var (success2, conn2) = childEntity2.TryToApplyTo(parentEntity);

            // Assert
            Assert.IsTrue(success1, "Child1 has the required tag => success expected.");
            Assert.IsNotNull(conn1, "Should get a valid connection for Child1.");

            Assert.IsFalse(success2, "Child2 is missing the required tag => fail expected.");
            Assert.IsNull(conn2, "Connection should be null on failure.");

            // Cleanup tags
            Object.DestroyImmediate(requiredTag);
            Object.DestroyImmediate(optionalTag);
        }

        #endregion

        #region Connect/Disconnect from Parents

        /// <summary>
        /// Demonstrates DisconnectFromParents code path that removes child modifiers
        /// from each parent. 
        /// </summary>
        [Test]
        public void Entity_DisconnectFromParents_RemovesModifiersFromParents()
        {
            // We'll have 1 child with a single modifier attribute, connected to 2 parents
            // Then disconnect to ensure RemoveChildModifiers is called for each parent.

            // Arrange: Create the child with a "modifier" attribute
            EntityData childData = new EntityData();
            Entity childEntity = new Entity();
            childEntity.Initialize(manager, childData);

            // Create a child's attribute that is a "modifier"
            AttributeData attData = new AttributeData();
            var childModifier = new LegendaryTools.AttributeSystem.Attribute(childEntity, attData)
            {
                Type = AttributeType.Modifier,
                Propagation = ModifierPropagation.Parent
            };
            // Actually add it to child
            childEntity.AddAttribute(childModifier);

            // Create 2 parents
            Entity parent1 = new Entity();
            parent1.Initialize(manager, new EntityData());
            Entity parent2 = new Entity();
            parent2.Initialize(manager, new EntityData());

            // Connect child to both parents
            childEntity.ConnectToParent(parent1);
            childEntity.ConnectToParent(parent2);

            // The parent's attributes won't get the child's modifiers automatically 
            // unless we call TryToApplyTo or do it manually. But for coverage of RemoveChildModifiers,
            // it's enough to have them connected.

            // Act
            childEntity.DisconnectFromParents(); 

            // Assert
            // Just verifying no exceptions & child has no parents
            Assert.AreEqual(0, childEntity.ParentNodes.Count,
                "Child should no longer have any parents after DisconnectFromParents().");
        }

        /// <summary>
        /// Demonstrates DisconnectFromParent(singleParent) code path. 
        /// Ensures the child's modifiers are removed from that single parent.
        /// </summary>
        [Test]
        public void Entity_DisconnectFromSingleParent_RemovesModifiers()
        {
            // Similar to the test above, but we only have 1 parent
            // and we call DisconnectFromParent(parent).

            // Arrange
            Entity child = new Entity();
            child.Initialize(manager, new EntityData());

            AttributeData modData = new AttributeData();
            var childMod = new LegendaryTools.AttributeSystem.Attribute(child, modData)
            {
                Type = AttributeType.Modifier,
                Propagation = ModifierPropagation.Parent
            };
            child.AddAttribute(childMod);

            Entity parent = new Entity();
            parent.Initialize(manager, new EntityData());

            // Connect the child
            child.ConnectToParent(parent);

            // Act
            child.DisconnectFromParent(parent);

            // Assert
            Assert.IsFalse(child.ParentNodes.Contains(parent),
                "Child should be disconnected from the parent now.");
        }

        #endregion

        #region Attributes: Add, Remove, Get

        /// <summary>
        /// Tests AddAttribute and RemoveAttribute success cases.
        /// Also covers dictionary usage inside Entity.
        /// </summary>
        [Test]
        public void Entity_AddAndRemoveAttribute()
        {
            // Arrange
            Entity entity = new Entity();
            entity.Initialize(manager, new EntityData());

            // Create an attribute
            AttributeData attData = new AttributeData();
            var attr = new LegendaryTools.AttributeSystem.Attribute(entity, attData);

            // Act: Add
            entity.AddAttribute(attr);

            // Assert
            Assert.IsTrue(entity.Attributes.Contains(attr),
                "Entity.Attributes must contain the newly added attribute.");
            Assert.AreEqual(entity, attr.Parent,
                "Attribute's Parent should be the entity.");

            // Act: Remove
            bool removed = entity.RemoveAttribute(attr);

            // Assert
            Assert.IsTrue(removed, "RemoveAttribute should return true on success.");
            Assert.IsFalse(entity.Attributes.Contains(attr),
                "Entity.Attributes should no longer have the attribute.");
            Assert.IsNull(attr.Parent,
                "Attribute.Parent should become null after removal.");
        }

        /// <summary>
        /// Tests GetAttributeByID to ensure dictionary usage is covered:
        /// - Found in dictionary
        /// - Found in list but not in dictionary
        /// - Not found => error
        /// - Not found => no error
        /// </summary>
        [Test]
        public void Entity_GetAttributeByID_Coverage()
        {
            // Arrange
            Entity entity = new Entity();
            entity.Initialize(manager, new EntityData());

            // We'll create one attribute for the entity, so we have something to find
            var attrData = new AttributeData();
            var attr = new LegendaryTools.AttributeSystem.Attribute(entity, attrData);
            entity.AddAttribute(attr);

            // We'll attempt to do lookups by the attr.Config
            var targetConfig = attr.Config; // The actual config created inside the constructor

            // Act & Assert: Found in list/dictionary
            var found1 = entity.GetAttributeByID(targetConfig);
            Assert.AreEqual(attr, found1,
                "Should find the attribute by its config in the entity's list/dictionary.");

            // Access it a second time to ensure the dictionary path is used
            var found2 = entity.GetAttributeByID(targetConfig);
            Assert.AreEqual(attr, found2, 
                "Should be found again, this time purely from the dictionary.");

            // Not found => with error
            LogAssert.ignoreFailingMessages = true; // So the error message doesn't fail the test
            var dummyConfig = ScriptableObject.CreateInstance<AttributeConfig>();
            var notFound1 = entity.GetAttributeByID(dummyConfig, true);
            Assert.IsNull(notFound1, 
                "Should return null if not found with error option = true.");
            // Clean up the log ignoring
            LogAssert.ignoreFailingMessages = false;

            // Not found => no error
            var notFound2 = entity.GetAttributeByID(dummyConfig, false);
            Assert.IsNull(notFound2, 
                "Should return null if not found with error option = false but no error is logged.");

            // Cleanup
            Object.DestroyImmediate(dummyConfig);
        }

        #endregion

        #region Modifiers: AddModifiers, RemoveModifiers, ApplyChild, RemoveChild

        /// <summary>
        /// Tests AddModifiers / RemoveModifiers code paths:
        /// - Only attributes of type Modifier are added.
        /// - If the target attribute does not exist but ForceApplyIfMissing = true, a new attribute is created.
        /// - If ForceApplyIfMissing = false, skip.
        /// </summary>
        [Test]
        public void Entity_AddRemoveModifiers()
        {
            // Arrange
            // Source entity with 2 attributes: 
            //    1) a normal attribute
            //    2) a modifier attribute
            //        - We'll test ForceApplyIfMissing on & off
            Entity srcEntity = new Entity();
            srcEntity.Initialize(manager, new EntityData());

            var normalData = new AttributeData();
            var normalAttr = new LegendaryTools.AttributeSystem.Attribute(srcEntity, normalData, "Normal")
            {
                Type = AttributeType.Attribute
            };
            srcEntity.AddAttribute(normalAttr);

            var modDataA = new AttributeData();
            var modAttrA = new LegendaryTools.AttributeSystem.Attribute(srcEntity, modDataA, "A")
            {
                Type = AttributeType.Modifier,
                ForceApplyIfMissing = false
            };
            srcEntity.AddAttribute(modAttrA);

            var modDataB = new AttributeData();
            var modAttrB = new LegendaryTools.AttributeSystem.Attribute(srcEntity, modDataB, "B")
            {
                Type = AttributeType.Modifier,
                ForceApplyIfMissing = true // will force creation if missing
            };
            srcEntity.AddAttribute(modAttrB);

            // Target entity (the one that calls AddModifiers)
            Entity targetEntity = new Entity();
            targetEntity.Initialize(manager, new EntityData());

            // Act: AddModifiers
            targetEntity.AddModifiers(srcEntity);

            // Assert:
            // - The normal attribute from src shouldn't be applied to target
            // - The modAttrA (ForceApplyIfMissing=false) => tries to add, but the target does not have that config => skip
            // - The modAttrB (ForceApplyIfMissing=true) => the target does not have that config => we create new
            var newAttrInTarget = targetEntity.Attributes.FirstOrDefault(a => a.Config == modAttrB.Config);
            Assert.IsNotNull(newAttrInTarget, 
                "ForceApplyIfMissing = true => target should create a brand new attribute for modAttrB.");
            Assert.IsTrue(newAttrInTarget.Modifiers.Contains(modAttrB), 
                "modAttrB should be present in the newly created attribute's Modifiers list.");

            // Now we call RemoveModifiers to remove those from the target
            targetEntity.RemoveModifiers(srcEntity);

            // The attribute remains in target, but the actual 'modAttrB' should be removed from its .Modifiers
            Assert.IsFalse(newAttrInTarget.Modifiers.Contains(modAttrB),
                "After removing, the mod should no longer be in the target attribute's .Modifiers list.");
        }

         /// <summary>
        /// Demonstrates propagation of modifiers (Parent, Child, Both) through TryToApplyTo -> ApplyChildModifiers,
        /// and removal via DisconnectFromParent -> RemoveChildModifiers.
        /// </summary>
        [Test]
        public void Entity_TryToApplyTo_And_DisconnectFromParent_PropagationCoverage()
        {
            // 1) Set up a parent entity and a "grandchild" so we can test Child-propagation.
            Entity parent = new Entity();
            parent.Initialize(manager, new EntityData());

            // Make a grandchild that belongs to parent:
            Entity grandchild = new Entity();
            grandchild.Initialize(manager, new EntityData());

            // Attempt to attach the grandchild to the parent so that the parent has at least one child:
            (bool gcSuccess, _) = grandchild.TryToApplyTo(parent);
            Assert.IsTrue(gcSuccess, "Grandchild should be accepted (no tag restrictions).");

            // 2) Create a child entity that holds three Modifier Attributes with different propagation settings.
            Entity child = new Entity();
            child.Initialize(manager, new EntityData());

            var modParent = new LegendaryTools.AttributeSystem.Attribute(child, new AttributeData())
            {
                Type = AttributeType.Modifier,
                Propagation = ModifierPropagation.Parent,
                ForceApplyIfMissing = true  // ensure coverage for forced creation
            };
            child.AddAttribute(modParent);

            var modChild = new LegendaryTools.AttributeSystem.Attribute(child, new AttributeData())
            {
                Type = AttributeType.Modifier,
                Propagation = ModifierPropagation.Child,
                ForceApplyIfMissing = true
            };
            child.AddAttribute(modChild);

            var modBoth = new LegendaryTools.AttributeSystem.Attribute(child, new AttributeData())
            {
                Type = AttributeType.Modifier,
                Propagation = ModifierPropagation.Both,
                ForceApplyIfMissing = true
            };
            child.AddAttribute(modBoth);

            // 3) Now have the child "apply itself" to the parent. Internally calls parent.ApplyChildModifiers(this).
            (bool success, _) = child.TryToApplyTo(parent);
            Assert.IsTrue(success, 
                "Child should be accepted if no TagFilter restrictions exist. This triggers ApplyChildModifiers.");

            // 4) Check that the parent's attributes (for Parent / Both) and the grandchild's attributes (for Child / Both)
            //    were created as needed due to ForceApplyIfMissing = true. 
            //    Also check those attributes have the relevant mod in their .Modifiers list.

            // For modParent and modBoth => the parent gets those attributes.
            var parentAttrParent = parent.Attributes.FirstOrDefault(a => a.Config == modParent.Config);
            Assert.NotNull(parentAttrParent, "Parent entity should have created an attribute for modParent.");
            Assert.IsTrue(parentAttrParent.Modifiers.Contains(modParent),
                "The parent's new attribute should contain the 'modParent' reference.");

            var parentAttrBoth = parent.Attributes.FirstOrDefault(a => a.Config == modBoth.Config);
            Assert.NotNull(parentAttrBoth, "Parent entity should have created an attribute for modBoth.");
            Assert.IsTrue(parentAttrBoth.Modifiers.Contains(modBoth),
                "The parent's 'both' attribute should contain 'modBoth' reference.");

            // For modChild and modBoth => the grandchild should have them.
            var grandchildAttrChild = grandchild.Attributes.FirstOrDefault(a => a.Config == modChild.Config);
            Assert.NotNull(grandchildAttrChild, 
                "Grandchild should have created an attribute for modChild (Child propagation).");
            Assert.IsTrue(grandchildAttrChild.Modifiers.Contains(modChild),
                "Grandchild's attribute for modChild should have 'modChild' in .Modifiers.");

            var grandchildAttrBoth = grandchild.Attributes.FirstOrDefault(a => a.Config == modBoth.Config);
            Assert.NotNull(grandchildAttrBoth,
                "Grandchild should also have an attribute for modBoth (Both propagation).");
            Assert.IsTrue(grandchildAttrBoth.Modifiers.Contains(modBoth),
                "Grandchild's 'both' attribute should contain 'modBoth' reference.");

            // 5) Now call DisconnectFromParent to remove the child from the parent. This indirectly calls
            //    parent.RemoveChildModifiers(child). Check that the parent's attributes and the grandchild's attributes
            //    no longer contain these modifiers.

            child.DisconnectFromParent(parent);

            // The parent attributes for 'modParent' and 'modBoth' should no longer contain those modifiers.
            Assert.IsFalse(parentAttrParent.Modifiers.Contains(modParent),
                "After removal, parent's attribute for modParent should not contain modParent anymore.");
            Assert.IsFalse(parentAttrBoth.Modifiers.Contains(modBoth),
                "After removal, parent's attribute for modBoth should not contain modBoth anymore.");

            // The grandchild's attributes for 'modChild' and 'modBoth' should no longer contain them.
            Assert.IsFalse(grandchildAttrChild.Modifiers.Contains(modChild),
                "Grandchild's attribute for modChild should have removed modChild after parent's removal call.");
            Assert.IsFalse(grandchildAttrBoth.Modifiers.Contains(modBoth),
                "Grandchild's attribute for modBoth should have removed modBoth after parent's removal call.");
        }

        #endregion
    }
}
