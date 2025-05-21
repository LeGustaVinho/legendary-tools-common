using System.Collections.Generic;
using System.Linq;
using LegendaryTools.GraphV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools; // for LogAssert
using LegendaryTools.AttributeSystem;
using LegendaryTools.TagSystem;

namespace Tests
{
    public class EntityTests
    {
        private EntityManager manager;
        private Entity rootEntity;
        
        [SetUp]
        public void SetUp()
        {
            // Create a root Entity that we can give to our EntityManager.
            // We do NOT create any EntityConfig here; we just instantiate the Entity directly.
            rootEntity = new Entity();
            
            // Create the EntityManager with a root entity (required by the constructor).
            manager = new EntityManager(rootEntity);
            // Manually add the root to the manager
            manager.AddEntity(rootEntity);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up references. We are not calling DestroyImmediate on Entities
            // because they are NOT UnityEngine.Objects. This is just a normal teardown.
            manager = null;
            rootEntity = null;
        }

        /// <summary>
        /// 1) Checks that initializing an Entity with an EntityData successfully 
        /// registers it into the EntityManager and sets up an empty attribute list.
        /// </summary>
        [Test]
        public void Entity_Initialize_WithEntityData()
        {
            // Arrange
            EntityData data = new EntityData(); // No tags, no attributes
            Entity testEntity = new Entity();

            // Act
            testEntity.Initialize(manager, data);

            // Assert
            Assert.IsTrue(manager.Entities.Contains(testEntity), 
                "Manager should contain the newly-initialized entity.");
            Assert.IsNotNull(testEntity.Attributes, "Attributes list should not be null.");
            Assert.AreEqual(0, testEntity.Attributes.Count, 
                "Since no attributes were provided, the list count should be zero.");
        }

        /// <summary>
        /// 2) Tests that adding a new Attribute (using AttributeData) to the Entity 
        /// actually inserts it into the Attributes list and sets its Parent correctly.
        /// </summary>
        [Test]
        public void Entity_AddAttribute_ShouldSucceed()
        {
            // Arrange
            Entity testEntity = new Entity();
            testEntity.Initialize(manager, new EntityData());

            // Create a new attribute using its (IEntity parent, AttributeData data) constructor
            AttributeData attData = new AttributeData(); 
            var attribute = new LegendaryTools.AttributeSystem.Attribute(testEntity, attData);

            // Act
            testEntity.AddAttribute(attribute);

            // Assert
            Assert.IsTrue(testEntity.Attributes.Contains(attribute), 
                "Entity attributes should contain the newly added attribute.");
            Assert.AreEqual(testEntity, attribute.Parent, 
                "The attribute's Parent property should be set to the entity.");
        }

        /// <summary>
        /// 3) Tests that removing a previously added Attribute 
        /// actually removes it from the entity's Attributes list.
        /// </summary>
        [Test]
        public void Entity_RemoveAttribute_ShouldSucceed()
        {
            // Arrange
            Entity testEntity = new Entity();
            testEntity.Initialize(manager, new EntityData());

            // Create and add an Attribute
            AttributeData attData = new AttributeData();
            var attribute = new LegendaryTools.AttributeSystem.Attribute(testEntity, attData);
            testEntity.AddAttribute(attribute);

            // Sanity check
            Assert.IsTrue(testEntity.Attributes.Contains(attribute));

            // Act
            bool removed = testEntity.RemoveAttribute(attribute);

            // Assert
            Assert.IsTrue(removed, "RemoveAttribute should return true if attribute was successfully removed.");
            Assert.IsFalse(testEntity.Attributes.Contains(attribute), 
                "Entity attributes should not contain the removed attribute anymore.");
            Assert.IsNull(attribute.Parent, "Once removed, the attribute's Parent should be null.");
        }

        /// <summary>
        /// 4) Tests TryToApplyTo when the parent Entity's OnlyAcceptTags rules ALLOW the child.
        ///    Ensures success == true and that we get a valid connection object back.
        /// </summary>
        [Test]
        public void Entity_TryToApplyTo_ParentAcceptsChild_Success()
        {
            // Arrange
            // Create a Tag
            Tag requiredTag = ScriptableObject.CreateInstance<Tag>();
            requiredTag.Name = "RequiredTag";

            // Create a parent entity with an OnlyAcceptTags rule that requires "RequiredTag"
            EntityData parentData = new EntityData
            {
                onlyAcceptTags = new TagFilterMatch[]
                {
                    new TagFilterMatch(requiredTag, TagFilterRuleType.Include)
                }
            };
            Entity parentEntity = new Entity();
            parentEntity.Initialize(manager, parentData);

            // Create a child entity that HAS the required tag
            EntityData childData = new EntityData
            {
                tags = new Tag[] { requiredTag }
            };
            Entity childEntity = new Entity();
            childEntity.Initialize(manager, childData);

            // Act
            (bool success, INodeConnection connection) = childEntity.TryToApplyTo(parentEntity);

            // Assert
            Assert.IsTrue(success, "Child entity should be accepted by parent when it meets the required tag.");
            Assert.IsNotNull(connection, "A valid connection should be returned when success is true.");

            // Cleanup: we only destroy the Tag ScriptableObject, since Entities are not Unity objects
            Object.DestroyImmediate(requiredTag);
        }

        /// <summary>
        /// 5) Tests TryToApplyTo when the parent Entity's OnlyAcceptTags rules REJECT the child.
        ///    Ensures success == false and that the returned connection is null.
        /// </summary>
        [Test]
        public void Entity_TryToApplyTo_ParentRejectsChild_Failure()
        {
            // Arrange
            // Create a Tag
            Tag requiredTag = ScriptableObject.CreateInstance<Tag>();
            requiredTag.Name = "RequiredTag";

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

            // Child has no tags => it won't match
            EntityData childData = new EntityData
            {
                tags = new Tag[] { /* Intentionally empty or different Tag */ }
            };
            Entity childEntity = new Entity();
            childEntity.Initialize(manager, childData);

            // Act
            (bool success, INodeConnection connection) = childEntity.TryToApplyTo(parentEntity);

            // Assert
            Assert.IsFalse(success, "Child entity should be rejected because it does not have the required tag.");
            Assert.IsNull(connection, "Connection should be null when success is false.");

            // Cleanup Tag
            Object.DestroyImmediate(requiredTag);
        }
    }
}

