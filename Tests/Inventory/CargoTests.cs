using System;
using System.Collections.Generic;
using NUnit.Framework;
using LegendaryTools.Inventory;

namespace LegendaryTools.Tests.Inventory
{
    [TestFixture]
    public class CargoTests
    {
        private Cargo<string> cargo;
        private Inventory<string> inventory1;
        private Inventory<string> inventory2;

        [SetUp]
        public void SetUp()
        {
            inventory1 = new Inventory<string>();
            inventory2 = new Inventory<string>();
            cargo = new Cargo<string>
            {
                MaxLimit = 100f,
                // Initialize with empty containers
            };
        }

        [Test]
        public void Add_SingleContainerWithinLimit_ShouldReturnTrue()
        {
            float limitToAdd = 50f;
            bool result = cargo.Add(limitToAdd, inventory1);
            Assert.IsTrue(result, "Adding a single container within the limit should return true.");
            Assert.AreEqual(50f, cargo.CurrentLimit, "CurrentLimit should be updated to 50 after adding the container.");
            Assert.AreEqual(1, cargo.Containers.Count, "Containers count should be 1 after adding a container.");
        }

        [Test]
        public void Add_SingleContainerExceedingLimit_ShouldReturnFalse()
        {
            float limitToAdd = 150f;
            bool result = cargo.Add(limitToAdd, inventory1);
            Assert.IsFalse(result, "Adding a single container exceeding the limit should return false.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should remain 0 after failed addition.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should remain 0 after failed addition.");
        }

        [Test]
        public void Add_MultipleContainersWithinLimit_ShouldReturnTrue()
        {
            bool result1 = cargo.Add(30f, inventory1);
            bool result2 = cargo.Add(40f, inventory2);
            Assert.IsTrue(result1, "First addition within limit should return true.");
            Assert.IsTrue(result2, "Second addition within limit should return true.");
            Assert.AreEqual(70f, cargo.CurrentLimit, "CurrentLimit should be 70 after adding two containers.");
            Assert.AreEqual(2, cargo.Containers.Count, "Containers count should be 2 after adding two containers.");
        }

        [Test]
        public void Add_MultipleContainersExceedingLimit_ShouldReturnFalseForLastAddition()
        {
            bool result1 = cargo.Add(60f, inventory1);
            bool result2 = cargo.Add(50f, inventory2); // Total would be 110 > 100
            Assert.IsTrue(result1, "First addition within limit should return true.");
            Assert.IsFalse(result2, "Second addition exceeding limit should return false.");
            Assert.AreEqual(60f, cargo.CurrentLimit, "CurrentLimit should remain 60 after failed addition.");
            Assert.AreEqual(1, cargo.Containers.Count, "Containers count should remain 1 after failed addition.");
        }

        [Test]
        public void Remove_WithinMaxLimit_ShouldReturnContainers()
        {
            cargo.Add(30f, inventory1);
            cargo.Add(40f, inventory2);
            List<ICargoContainer<string>> removed = cargo.Remove(50f);
            Assert.AreEqual(1, removed.Count, "Only one container should be removed within the max limit.");
            Assert.AreEqual(30f, removed[0].Limit, "The removed container should have a limit of 40.");
            Assert.AreEqual(40f, cargo.CurrentLimit, "CurrentLimit should be updated to 30 after removal.");
            Assert.AreEqual(1, cargo.Containers.Count, "Containers count should be 1 after removal.");
        }

        [Test]
        public void Remove_ExceedingMaxLimit_ShouldRemoveAllContainers()
        {
            cargo.Add(30f, inventory1);
            cargo.Add(40f, inventory2);
            List<ICargoContainer<string>> removed = cargo.Remove(100f);
            Assert.AreEqual(2, removed.Count, "Both containers should be removed when max limit is exceeded.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should be 0 after removing all containers.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should be 0 after removing all containers.");
        }

        [Test]
        public void AvailableLimit_ShouldReturnCorrectValue()
        {
            cargo.Add(30f, inventory1);
            cargo.Add(40f, inventory2);
            float available = cargo.AvailableLimit;
            Assert.AreEqual(30f, available, "AvailableLimit should be 30 after adding containers with total limit 70.");
        }

        [Test]
        public void CurrentLimit_ShouldReturnCorrectValue()
        {
            cargo.Add(25f, inventory1);
            cargo.Add(35f, inventory2);
            Assert.AreEqual(60f, cargo.CurrentLimit, "CurrentLimit should correctly reflect the total limit of containers.");
        }

        [Test]
        public void Add_ListOfContainersWithinLimit_ShouldReturnTrue()
        {
            var containersToAdd = new List<ICargoContainer<string>>
            {
                new CargoContainer<string>(20f, inventory1),
                new CargoContainer<string>(30f, inventory2)
            };
            bool result = cargo.Add(containersToAdd);
            Assert.IsTrue(result, "Adding a list of containers within the limit should return true.");
            Assert.AreEqual(50f, cargo.CurrentLimit, "CurrentLimit should be 50 after adding the list of containers.");
            Assert.AreEqual(2, cargo.Containers.Count, "Containers count should be 2 after adding the list.");
            Assert.IsEmpty(containersToAdd, "containersToAdd list should be empty after successful addition.");
        }

        [Test]
        public void Add_ListOfContainersExceedingLimit_ShouldReturnFalse()
        {
            var containersToAdd = new List<ICargoContainer<string>>
            {
                new CargoContainer<string>(60f, inventory1),
                new CargoContainer<string>(50f, inventory2) // Total 110 > 100
            };
            bool result = cargo.Add(containersToAdd);
            Assert.IsFalse(result, "Adding a list of containers exceeding the limit should return false.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should remain 0 after failed addition.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should remain 0 after failed addition.");
            Assert.AreEqual(2, containersToAdd.Count, "containersToAdd list should remain unchanged after failed addition.");
        }

        [Test]
        public void RemoveAll_ShouldClearAllContainers()
        {
            cargo.Add(20f, inventory1);
            cargo.Add(30f, inventory2);
            List<ICargoContainer<string>> removed = cargo.RemoveAll();
            Assert.AreEqual(2, removed.Count, "Both containers should be removed when RemoveAll is called.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should be 0 after removing all containers.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should be 0 after removing all containers.");
        }

        [Test]
        public void TransferWhenPossibleTo_TargetHasAvailableLimit_ShouldTransferContainers()
        {
            cargo.Add(40f, inventory1);
            cargo.Add(30f, inventory2);
            
            Cargo<string> targetCargo = new Cargo<string>
            {
                MaxLimit = 100f
            };
            
            cargo.TransferWhenPossibleTo(targetCargo);
            
            Assert.AreEqual(70f, targetCargo.CurrentLimit, "Target cargo should have a CurrentLimit of 70 after transfer.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "Source cargo should have a CurrentLimit of 0 after transfer.");
            Assert.AreEqual(0, cargo.Containers.Count, "Source cargo should have no containers after transfer.");
            Assert.AreEqual(2, targetCargo.Containers.Count, "Target cargo should have 2 containers after transfer.");
        }

        [Test]
        public void TransferWhenPossibleTo_TargetHasInsufficientLimit_ShouldTransferPartialContainers()
        {
            cargo.Add(60f, inventory1);
            cargo.Add(50f, inventory2);
            
            Cargo<string> targetCargo = new Cargo<string>
            {
                MaxLimit = 100f
            };
            
            bool addResult = targetCargo.Add(40f, new Inventory<string>()); // Now target has 40, available 60
            Assert.IsTrue(addResult, "Initial addition to target cargo should succeed.");
            
            cargo.TransferWhenPossibleTo(targetCargo);
            
            Assert.AreEqual(100f, targetCargo.CurrentLimit, "Target cargo should have a CurrentLimit of 100 after partial transfer.");
            Assert.AreEqual(0, cargo.CurrentLimit, "Source cargo should have a CurrentLimit of 0 after partial transfer.");
            Assert.AreEqual(0, cargo.Containers.Count, "Source cargo should have 1 container left after partial transfer.");
            Assert.AreEqual(2, targetCargo.Containers.Count, "Target cargo should have 2 containers after partial transfer.");
        }

        [Test]
        public void TransferAllTo_ShouldTransferAllContainersToInventory()
        {
            cargo.Add(30f, inventory1);
            cargo.Add(40f, inventory2);
            
            Inventory<string> targetInventory = new Inventory<string>();
            cargo.TransferAllTo(targetInventory);
            
            Assert.AreEqual(0f, cargo.CurrentLimit, "Source cargo should have a CurrentLimit of 0 after transferring all containers.");
            Assert.AreEqual(0, cargo.Containers.Count, "Source cargo should have no containers after transferring all.");
        }

        [Test]
        public void Constructor_ShouldInitializeMaxLimitCorrectly()
        {
            float maxLimit = 200f;
            Cargo<string> newCargo = new Cargo<string>
            {
                MaxLimit = maxLimit
            };
            Assert.AreEqual(maxLimit, newCargo.MaxLimit, "Constructor should correctly initialize MaxLimit.");
            Assert.AreEqual(0f, newCargo.CurrentLimit, "Initial CurrentLimit should be 0.");
            Assert.IsEmpty(newCargo.Containers, "Initial Containers list should be empty.");
        }

        [Test]
        public void OnCargoLimitChange_EventShouldBeInvokedOnAdd()
        {
            float oldLimit = 0f;
            float newLimit = 50f;
            bool eventInvoked = false;
            cargo.OnCargoLimitChange += (old, newL) =>
            {
                eventInvoked = true;
                Assert.AreEqual(oldLimit, old, "Old limit should match before addition.");
                Assert.AreEqual(newLimit, newL, "New limit should match after addition.");
            };
            
            cargo.Add(newLimit, inventory1);
            Assert.IsTrue(eventInvoked, "OnCargoLimitChange event should be invoked on addition.");
        }

        [Test]
        public void OnCargoLimitChange_EventShouldBeInvokedOnRemove()
        {
            cargo.Add(60f, inventory1);
            float oldLimit = 60f;
            float newLimit = 60f;
            bool eventInvoked = false;
            cargo.OnCargoLimitChange += (old, newL) =>
            {
                eventInvoked = true;
                Assert.AreEqual(oldLimit, old, "Old limit should match before removal.");
                Assert.AreEqual(newLimit, newL, "New limit should match after removal.");
            };
            
            cargo.Remove(30f);
            Assert.IsTrue(eventInvoked, "OnCargoLimitChange event should be invoked on removal.");
        }

        [Test]
        public void Add_NullInventory_ShouldReturnFalse()
        {
            bool result = cargo.Add(20f, null);
            Assert.IsFalse(result, "Adding with null inventory should return false.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should remain 0 after failed addition.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should remain 0 after failed addition.");
        }

        [Test]
        public void Remove_MoreThanExistingLimit_ShouldReturnTrueAndChangeCargo()
        {
            cargo.Add(30f, inventory1);
            List<ICargoContainer<string>> result = cargo.Remove(40f);
            Assert.IsTrue(result.Count > 0, "Removing should return true.");
            Assert.AreEqual(0, cargo.CurrentLimit, "CurrentLimit should be changed after removal.");
            Assert.AreEqual(0, cargo.Containers.Count, "Containers count should be changed after removal.");
        }

        [Test]
        public void Add_WithZeroLimit_ShouldReturnTrueAndAddContainer()
        {
            bool result = cargo.Add(0f, inventory1);
            Assert.IsTrue(result, "Adding a container with zero limit should return true.");
            Assert.AreEqual(0f, cargo.CurrentLimit, "CurrentLimit should remain 0 after adding a zero limit container.");
            Assert.AreEqual(1, cargo.Containers.Count, "Containers count should be 1 after adding a zero limit container.");
        }

        [Test]
        public void Remove_WithZeroLimit_ShouldReturnEmptyList()
        {
            cargo.Add(30f, inventory1);
            List<ICargoContainer<string>> removed = cargo.Remove(0f);
            Assert.IsEmpty(removed, "Removing with zero limit should return an empty list.");
            Assert.AreEqual(30f, cargo.CurrentLimit, "CurrentLimit should remain unchanged after removing with zero limit.");
            Assert.AreEqual(1, cargo.Containers.Count, "Containers count should remain unchanged after removing with zero limit.");
        }

        [Test]
        public void Containers_ShouldReturnCloneOfContainersList()
        {
            cargo.Add(20f, inventory1);
            List<ICargoContainer<string>> containers = cargo.Containers;
            Assert.AreEqual(1, containers.Count, "Containers property should return the correct number of containers.");
            containers.Add(new CargoContainer<string>(10f, inventory2));
            Assert.AreEqual(1, cargo.Containers.Count, "Modifying the returned Containers list should not affect the original Containers.");
        }

        [Test]
        public void TransferAllTo_NullInventory_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => cargo.TransferAllTo(null), "Transferring all to a null inventory should throw ArgumentNullException.");
        }

        [Test]
        public void Add_SameInventoryMultipleTimes_ShouldHandleSeparately()
        {
            bool result1 = cargo.Add(20f, inventory1);
            bool result2 = cargo.Add(30f, inventory1);
            Assert.IsTrue(result1, "First addition with the same inventory should return true.");
            Assert.IsTrue(result2, "Second addition with the same inventory should return true.");
            Assert.AreEqual(50f, cargo.CurrentLimit, "CurrentLimit should correctly sum multiple additions with the same inventory.");
            Assert.AreEqual(2, cargo.Containers.Count, "Containers count should reflect multiple additions with the same inventory.");
        }

        [Test]
        public void RemoveAll_ShouldInvokeOnCargoLimitChangeEvent()
        {
            cargo.Add(25f, inventory1);
            bool eventInvoked = false;
            float oldLimit = 25f;
            float newLimit = 0f;
            cargo.OnCargoLimitChange += (old, newL) =>
            {
                eventInvoked = true;
                Assert.AreEqual(oldLimit, old, "Old limit should match before removing all.");
                Assert.AreEqual(newLimit, newL, "New limit should match after removing all.");
            };
            
            List<ICargoContainer<string>> removed = cargo.RemoveAll();
            Assert.IsTrue(eventInvoked, "OnCargoLimitChange event should be invoked when RemoveAll is called.");
        }
    }
}
