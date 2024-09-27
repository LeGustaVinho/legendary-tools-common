using System;
using System.Collections.Generic;
using NUnit.Framework;
using LegendaryTools.Inventory;

namespace LegendaryTools.Tests.Inventory
{
    [TestFixture]
    public class InventoryUnitTests
    {
        private Inventory<string> inventory;

        [SetUp]
        public void Setup()
        {
            inventory = new Inventory<string>();
        }

        #region Add Tests

        [Test]
        public void Add_NewItem_ItemIsAdded()
        {
            // Arrange
            string item = "Sword";
            float amount = 1.0f;

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_NewItem_ItemIsAdded: Inventory should contain the added item.");
            Assert.AreEqual(amount, inventory[item], "Add_NewItem_ItemIsAdded: The amount of the added item should match.");
        }

        [Test]
        public void Add_ExistingItem_AmountIsUpdated()
        {
            // Arrange
            string item = "Shield";
            float initialAmount = 2.0f;
            float additionalAmount = 3.0f;
            inventory.Add(item, initialAmount);

            // Act
            inventory.Add(item, additionalAmount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_ExistingItem_AmountIsUpdated: Inventory should contain the item.");
            Assert.AreEqual(initialAmount + additionalAmount, inventory[item], "Add_ExistingItem_AmountIsUpdated: The amount should be updated correctly.");
        }

        [Test]
        public void Add_MultipleItems_ItemsAreAdded()
        {
            // Arrange
            var items = new Dictionary<string, float>
            {
                { "Potion", 5.0f },
                { "Elixir", 3.0f },
                { "Herb", 10.0f }
            };

            // Act
            foreach (var kvp in items)
            {
                inventory.Add(kvp.Key, kvp.Value);
            }

            // Assert
            foreach (var kvp in items)
            {
                Assert.IsTrue(inventory.Contains(kvp.Key), $"Add_MultipleItems_ItemsAreAdded: Inventory should contain {kvp.Key}.");
                Assert.AreEqual(kvp.Value, inventory[kvp.Key], $"Add_MultipleItems_ItemsAreAdded: The amount of {kvp.Key} should match.");
            }
        }

        [Test]
        public void Add_ZeroAmount_ItemIsAddedWithZero()
        {
            // Arrange
            string item = "Arrow";
            float amount = 0.0f;

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_ZeroAmount_ItemIsAddedWithZero: Inventory should contain the item even with zero amount.");
            Assert.AreEqual(0.0f, inventory[item], "Add_ZeroAmount_ItemIsAddedWithZero: The amount should be zero.");
        }

        [Test]
        public void Add_NegativeAmount_ThrowsException()
        {
            // Arrange
            string item = "Bomb";
            float amount = -1.0f;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => inventory.Add(item, amount), "Add_NegativeAmount_ThrowsException: Adding a negative amount should throw an ArgumentException.");
            Assert.That(ex.Message, Does.Contain("Amount cannot be negative"), "Add_NegativeAmount_ThrowsException: Exception message should indicate negative amount.");
        }

        #endregion

        #region Remove Tests

        [Test]
        public void Remove_ExistingItem_SufficientAmount_RemovesAmount()
        {
            // Arrange
            string item = "Arrow";
            float initialAmount = 10.0f;
            float removeAmount = 4.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsTrue(result, "Remove_ExistingItem_SufficientAmount_RemovesAmount: Remove should return true.");
            Assert.AreEqual(initialAmount - removeAmount, inventory[item], "Remove_ExistingItem_SufficientAmount_RemovesAmount: The amount should be decreased correctly.");
        }

        [Test]
        public void Remove_ExistingItem_ExactAmount_RemovesItem()
        {
            // Arrange
            string item = "Shield";
            float initialAmount = 2.0f;
            float removeAmount = 2.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsTrue(result, "Remove_ExistingItem_ExactAmount_RemovesItem: Remove should return true.");
            Assert.IsFalse(inventory.Contains(item), "Remove_ExistingItem_ExactAmount_RemovesItem: Item should be removed from inventory.");
        }

        [Test]
        public void Remove_ExistingItem_InsufficientAmount_ReturnsFalse()
        {
            // Arrange
            string item = "Potion";
            float initialAmount = 3.0f;
            float removeAmount = 5.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsFalse(result, "Remove_ExistingItem_InsufficientAmount_ReturnsFalse: Remove should return false when amount is insufficient.");
            Assert.AreEqual(initialAmount, inventory[item], "Remove_ExistingItem_InsufficientAmount_ReturnsFalse: The amount should remain unchanged.");
        }

        [Test]
        public void Remove_NonExistingItem_ReturnsFalse()
        {
            // Arrange
            string item = "Elixir";
            float removeAmount = 1.0f;

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsFalse(result, "Remove_NonExistingItem_ReturnsFalse: Remove should return false for non-existing item.");
        }

        [Test]
        public void Remove_ZeroAmount_ReturnsTrue()
        {
            // Arrange
            string item = "Herb";
            float initialAmount = 5.0f;
            float removeAmount = 0.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsTrue(result, "Remove_ZeroAmount_ReturnsTrue: Removing zero amount should return true.");
            Assert.AreEqual(initialAmount, inventory[item], "Remove_ZeroAmount_ReturnsTrue: The amount should remain unchanged.");
        }

        [Test]
        public void Remove_NegativeAmount_ThrowsException()
        {
            // Arrange
            string item = "Bomb";
            float initialAmount = 5.0f;
            float removeAmount = -2.0f;
            inventory.Add(item, initialAmount);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => inventory.Remove(item, removeAmount), "Remove_NegativeAmount_ThrowsException: Removing a negative amount should throw an ArgumentException.");
            Assert.That(ex.Message, Does.Contain("Amount cannot be negative"), "Remove_NegativeAmount_ThrowsException: Exception message should indicate negative amount.");
        }

        #endregion

        #region Contains Tests

        [Test]
        public void Contains_ExistingItem_ReturnsTrue()
        {
            // Arrange
            string item = "Sword";
            inventory.Add(item, 1.0f);

            // Act
            bool contains = inventory.Contains(item);

            // Assert
            Assert.IsTrue(contains, "Contains_ExistingItem_ReturnsTrue: Inventory should contain the existing item.");
        }

        [Test]
        public void Contains_NonExistingItem_ReturnsFalse()
        {
            // Arrange
            string item = "Magic Scroll";

            // Act
            bool contains = inventory.Contains(item);

            // Assert
            Assert.IsFalse(contains, "Contains_NonExistingItem_ReturnsFalse: Inventory should not contain a non-existing item.");
        }

        #endregion

        #region TryGetValue Tests

        [Test]
        public void TryGetValue_ExistingItem_ReturnsTrueAndAmount()
        {
            // Arrange
            string item = "Potion";
            float amount = 5.0f;
            inventory.Add(item, amount);

            // Act
            bool result = inventory.TryGetValue(item, out float retrievedAmount);

            // Assert
            Assert.IsTrue(result, "TryGetValue_ExistingItem_ReturnsTrueAndAmount: TryGetValue should return true for existing item.");
            Assert.AreEqual(amount, retrievedAmount, "TryGetValue_ExistingItem_ReturnsTrueAndAmount: Retrieved amount should match the added amount.");
        }

        [Test]
        public void TryGetValue_NonExistingItem_ReturnsFalseAndZero()
        {
            // Arrange
            string item = "Elixir";

            // Act
            bool result = inventory.TryGetValue(item, out float retrievedAmount);

            // Assert
            Assert.IsFalse(result, "TryGetValue_NonExistingItem_ReturnsFalseAndZero: TryGetValue should return false for non-existing item.");
            Assert.AreEqual(0.0f, retrievedAmount, "TryGetValue_NonExistingItem_ReturnsFalseAndZero: Retrieved amount should be zero.");
        }

        #endregion

        #region GetAll Tests

        [Test]
        public void GetAll_NonEmptyInventory_ReturnsAllItems()
        {
            // Arrange
            var items = new Dictionary<string, float>
            {
                { "Sword", 1.0f },
                { "Shield", 2.0f },
                { "Potion", 5.0f }
            };
            foreach (var kvp in items)
            {
                inventory.Add(kvp.Key, kvp.Value);
            }

            // Act
            var allItems = inventory.GetAll();

            // Assert
            Assert.AreEqual(items.Count, allItems.Count, "GetAll_NonEmptyInventory_ReturnsAllItems: The count of items should match.");
            foreach (var kvp in items)
            {
                Assert.IsTrue(allItems.ContainsKey(kvp.Key), $"GetAll_NonEmptyInventory_ReturnsAllItems: Should contain key {kvp.Key}.");
                Assert.AreEqual(kvp.Value, allItems[kvp.Key], $"GetAll_NonEmptyInventory_ReturnsAllItems: The amount for {kvp.Key} should match.");
            }
        }

        [Test]
        public void GetAll_EmptyInventory_ReturnsEmptyDictionary()
        {
            // Act
            var allItems = inventory.GetAll();

            // Assert
            Assert.IsNotNull(allItems, "GetAll_EmptyInventory_ReturnsEmptyDictionary: Returned dictionary should not be null.");
            Assert.IsEmpty(allItems, "GetAll_EmptyInventory_ReturnsEmptyDictionary: Returned dictionary should be empty.");
        }

        [Test]
        public void GetAll_ReturnsClone_NotReference()
        {
            // Arrange
            string item = "Herb";
            float amount = 10.0f;
            inventory.Add(item, amount);

            // Act
            var allItems = inventory.GetAll();
            allItems[item] = 20.0f;

            // Assert
            Assert.AreEqual(amount, inventory[item], "GetAll_ReturnsClone_NotReference: Modifying the returned dictionary should not affect the inventory.");
        }

        #endregion

        #region Indexer Tests

        [Test]
        public void Indexer_GetExistingItem_ReturnsCorrectAmount()
        {
            // Arrange
            string item = "Bow";
            float amount = 1.5f;
            inventory.Add(item, amount);

            // Act
            float retrievedAmount = inventory[item];

            // Assert
            Assert.AreEqual(amount, retrievedAmount, "Indexer_GetExistingItem_ReturnsCorrectAmount: Indexer should return the correct amount.");
        }

        [Test]
        public void Indexer_GetNonExistingItem_ThrowsKeyNotFoundException()
        {
            // Arrange
            string item = "Magic Ring";

            // Act & Assert
            Assert.Throws<KeyNotFoundException>(() =>
            {
                var amount = inventory[item];
            }, "Indexer_GetNonExistingItem_ThrowsKeyNotFoundException: Accessing a non-existing item should throw KeyNotFoundException.");
        }

        [Test]
        public void Indexer_SetItem_ThrowsNotImplementedException()
        {
            // Arrange
            string item = "Helmet";
            float amount = 2.0f;

            // Act & Assert
            Assert.Throws<NotImplementedException>(() =>
            {
                inventory[item] = amount;
            }, "Indexer_SetItem_ThrowsNotImplementedException: Setting an item via indexer should throw NotImplementedException.");
        }

        #endregion

        #region Clear Tests

        [Test]
        public void Clear_NonEmptyInventory_RemovesAllItems()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);
            inventory.Add("Shield", 2.0f);

            // Act
            inventory.Clear();

            // Assert
            Assert.IsEmpty(inventory.GetAll(), "Clear_NonEmptyInventory_RemovesAllItems: Inventory should be empty after Clear.");
        }

        [Test]
        public void Clear_EmptyInventory_RemainsEmpty()
        {
            // Act
            inventory.Clear();

            // Assert
            Assert.IsEmpty(inventory.GetAll(), "Clear_EmptyInventory_RemainsEmpty: Inventory should remain empty after Clear.");
        }

        #endregion

        #region TransferAllTo Tests

        [Test]
        public void TransferAllTo_NonEmptySource_TransfersAllItems()
        {
            // Arrange
            var source = new Inventory<string>();
            source.Add("Potion", 5.0f);
            source.Add("Elixir", 3.0f);
            var target = new Inventory<string>();

            // Act
            source.TransferAllTo(target);

            // Assert
            Assert.IsEmpty(source.GetAll(), "TransferAllTo_NonEmptySource_TransfersAllItems: Source inventory should be empty after transfer.");
            Assert.AreEqual(2, target.Count, "TransferAllTo_NonEmptySource_TransfersAllItems: Target inventory should have two items.");
            Assert.AreEqual(5.0f, target["Potion"], "TransferAllTo_NonEmptySource_TransfersAllItems: Potion amount should match.");
            Assert.AreEqual(3.0f, target["Elixir"], "TransferAllTo_NonEmptySource_TransfersAllItems: Elixir amount should match.");
        }

        [Test]
        public void TransferAllTo_EmptySource_RemainsEmpty()
        {
            // Arrange
            var source = new Inventory<string>();
            var target = new Inventory<string>();

            // Act
            source.TransferAllTo(target);

            // Assert
            Assert.IsEmpty(source.GetAll(), "TransferAllTo_EmptySource_RemainsEmpty: Source inventory should remain empty after transfer.");
            Assert.IsEmpty(target.GetAll(), "TransferAllTo_EmptySource_RemainsEmpty: Target inventory should remain empty after transfer.");
        }

        [Test]
        public void TransferAllTo_TargetAlreadyHasItems_AmountsAreUpdated()
        {
            // Arrange
            var source = new Inventory<string>();
            source.Add("Potion", 5.0f);
            var target = new Inventory<string>();
            target.Add("Potion", 2.0f);

            // Act
            source.TransferAllTo(target);

            // Assert
            Assert.IsEmpty(source.GetAll(), "TransferAllTo_TargetAlreadyHasItems_AmountsAreUpdated: Source inventory should be empty after transfer.");
            Assert.AreEqual(1, target.Count, "TransferAllTo_TargetAlreadyHasItems_AmountsAreUpdated: Target inventory should have one item.");
            Assert.AreEqual(7.0f, target["Potion"], "TransferAllTo_TargetAlreadyHasItems_AmountsAreUpdated: Potion amount should be updated correctly.");
        }

        #endregion

        #region OnInventoryChange Event Tests

        [Test]
        public void Add_Item_OnInventoryChangeIsTriggered()
        {
            // Arrange
            string item = "Sword";
            float amount = 1.0f;
            bool eventTriggered = false;
            inventory.OnInventoryChange += (addedItem, oldAmount, newAmount) =>
            {
                if (addedItem == item && oldAmount == 0 && newAmount == amount)
                {
                    eventTriggered = true;
                }
            };

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(eventTriggered, "Add_Item_OnInventoryChangeIsTriggered: OnInventoryChange should be triggered when adding an item.");
        }

        [Test]
        public void Remove_Item_OnInventoryChangeIsTriggered()
        {
            // Arrange
            string item = "Shield";
            float initialAmount = 2.0f;
            float removeAmount = 1.0f;
            inventory.Add(item, initialAmount);
            bool eventTriggered = false;
            inventory.OnInventoryChange += (removedItem, oldAmount, newAmount) =>
            {
                if (removedItem == item && oldAmount == initialAmount && newAmount == initialAmount - removeAmount)
                {
                    eventTriggered = true;
                }
            };

            // Act
            inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsTrue(eventTriggered, "Remove_Item_OnInventoryChangeIsTriggered: OnInventoryChange should be triggered when removing an item.");
        }

        [Test]
        public void Clear_OnInventoryChangeIsTriggeredForEachItem()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);
            inventory.Add("Shield", 2.0f);
            int eventCount = 0;
            inventory.OnInventoryChange += (item, oldAmount, newAmount) =>
            {
                eventCount++;
                Assert.AreEqual(0.0f, newAmount, "Clear_OnInventoryChangeIsTriggeredForEachItem: New amount should be zero.");
            };

            // Act
            inventory.Clear();

            // Assert
            Assert.AreEqual(2, eventCount, "Clear_OnInventoryChangeIsTriggeredForEachItem: OnInventoryChange should be triggered for each item.");
        }

        [Test]
        public void TransferAllTo_OnInventoryChangeIsTriggeredForEachItem()
        {
            // Arrange
            var source = new Inventory<string>();
            source.Add("Potion", 5.0f);
            source.Add("Elixir", 3.0f);
            bool potionEvent = false;
            bool elixirEvent = false;
            source.OnInventoryChange += (item, oldAmount, newAmount) =>
            {
                if (item == "Potion" && oldAmount == 5.0f && newAmount == 0.0f)
                {
                    potionEvent = true;
                }
                if (item == "Elixir" && oldAmount == 3.0f && newAmount == 0.0f)
                {
                    elixirEvent = true;
                }
            };
            var target = new Inventory<string>();

            // Act
            source.TransferAllTo(target);

            // Assert
            Assert.IsTrue(potionEvent, "TransferAllTo_OnInventoryChangeIsTriggeredForEachItem: OnInventoryChange should be triggered for Potion.");
            Assert.IsTrue(elixirEvent, "TransferAllTo_OnInventoryChangeIsTriggeredForEachItem: OnInventoryChange should be triggered for Elixir.");
        }

        #endregion

        #region IDictionary Implementation Tests

        [Test]
        public void Keys_ReturnsAllKeys()
        {
            // Arrange
            var items = new Dictionary<string, float>
            {
                { "Sword", 1.0f },
                { "Shield", 2.0f }
            };
            foreach (var kvp in items)
            {
                inventory.Add(kvp.Key, kvp.Value);
            }

            // Act
            var keys = inventory.Keys;

            // Assert
            CollectionAssert.AreEquivalent(items.Keys, keys, "Keys_ReturnsAllKeys: Keys property should return all keys in the inventory.");
        }

        [Test]
        public void Values_ReturnsAllValues()
        {
            // Arrange
            var items = new Dictionary<string, float>
            {
                { "Sword", 1.0f },
                { "Shield", 2.0f }
            };
            foreach (var kvp in items)
            {
                inventory.Add(kvp.Key, kvp.Value);
            }

            // Act
            var values = inventory.Values;

            // Assert
            CollectionAssert.AreEquivalent(items.Values, values, "Values_ReturnsAllValues: Values property should return all values in the inventory.");
        }

        [Test]
        public void Count_ReturnsCorrectNumberOfItems()
        {
            // Arrange
            Assert.AreEqual(0, inventory.Count, "Count_ReturnsCorrectNumberOfItems: Initial count should be zero.");
            inventory.Add("Sword", 1.0f);
            inventory.Add("Shield", 2.0f);

            // Act
            int count = inventory.Count;

            // Assert
            Assert.AreEqual(2, count, "Count_ReturnsCorrectNumberOfItems: Count should reflect the number of items added.");
        }

        [Test]
        public void IsReadOnly_ReturnsFalse()
        {
            // Act
            bool isReadOnly = inventory.IsReadOnly;

            // Assert
            Assert.IsFalse(isReadOnly, "IsReadOnly_ReturnsFalse: Inventory should not be read-only.");
        }

        [Test]
        public void Add_KeyValuePair_AddsItem()
        {
            // Arrange
            var kvp = new KeyValuePair<string, float>("Potion", 5.0f);

            // Act
            inventory.Add(kvp);

            // Assert
            Assert.IsTrue(inventory.Contains(kvp.Key), "Add_KeyValuePair_AddsItem: Inventory should contain the added key.");
            Assert.AreEqual(kvp.Value, inventory[kvp.Key], "Add_KeyValuePair_AddsItem: The amount should match the KeyValuePair value.");
        }

        [Test]
        public void Contains_KeyValuePair_ReturnsTrueIfExists()
        {
            // Arrange
            var kvp = new KeyValuePair<string, float>("Elixir", 3.0f);
            inventory.Add(kvp.Key, kvp.Value);

            // Act
            bool contains = inventory.Contains(kvp);

            // Assert
            Assert.IsTrue(contains, "Contains_KeyValuePair_ReturnsTrueIfExists: Should return true for existing KeyValuePair.");
        }

        [Test]
        public void Contains_KeyValuePair_ReturnsFalseIfValueMismatch()
        {
            // Arrange
            var kvp = new KeyValuePair<string, float>("Herb", 10.0f);
            inventory.Add(kvp.Key, 5.0f);

            // Act
            bool contains = inventory.Contains(kvp);

            // Assert
            Assert.IsFalse(contains, "Contains_KeyValuePair_ReturnsFalseIfValueMismatch: Should return false if the value does not match.");
        }

        [Test]
        public void Remove_KeyValuePair_RemovesItemIfMatches()
        {
            // Arrange
            var kvp = new KeyValuePair<string, float>("Bomb", 2.0f);
            inventory.Add(kvp.Key, kvp.Value);

            // Act
            bool removed = inventory.Remove(kvp);

            // Assert
            Assert.IsTrue(removed, "Remove_KeyValuePair_RemovesItemIfMatches: Should return true when removing existing KeyValuePair.");
            Assert.IsFalse(inventory.Contains(kvp.Key), "Remove_KeyValuePair_RemovesItemIfMatches: Item should be removed from inventory.");
        }

        [Test]
        public void Remove_KeyValuePair_ReturnsFalseIfNotMatching()
        {
            // Arrange
            var kvp = new KeyValuePair<string, float>("Bomb", 2.0f);
            inventory.Add(kvp.Key, 1.0f);

            // Act
            bool removed = inventory.Remove(kvp);

            // Assert
            Assert.IsFalse(removed, "Remove_KeyValuePair_ReturnsFalseIfNotMatching: Should return false if KeyValuePair does not match.");
            Assert.IsTrue(inventory.Contains(kvp.Key), "Remove_KeyValuePair_ReturnsFalseIfNotMatching: Item should still exist in inventory.");
        }

        [Test]
        public void CopyTo_CopiesItemsToArray()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);
            inventory.Add("Shield", 2.0f);
            var array = new KeyValuePair<string, float>[2];

            // Act
            inventory.CopyTo(array, 0);

            // Assert
            Assert.Contains(new KeyValuePair<string, float>("Sword", 1.0f), array, "CopyTo_CopiesItemsToArray: Array should contain the Sword KeyValuePair.");
            Assert.Contains(new KeyValuePair<string, float>("Shield", 2.0f), array, "CopyTo_CopiesItemsToArray: Array should contain the Shield KeyValuePair.");
        }

        [Test]
        public void GetEnumerator_IteratesThroughAllItems()
        {
            // Arrange
            var items = new Dictionary<string, float>
            {
                { "Sword", 1.0f },
                { "Shield", 2.0f },
                { "Potion", 5.0f }
            };
            foreach (var kvp in items)
            {
                inventory.Add(kvp.Key, kvp.Value);
            }

            // Act
            var enumeratedItems = new Dictionary<string, float>();
            foreach (var kvp in inventory)
            {
                enumeratedItems.Add(kvp.Key, kvp.Value);
            }

            // Assert
            CollectionAssert.AreEquivalent(items, enumeratedItems, "GetEnumerator_IteratesThroughAllItems: Enumerator should iterate through all items correctly.");
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void Add_NullItem_ThrowsArgumentNullException()
        {
            // Arrange
            string item = null;
            float amount = 1.0f;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => inventory.Add(item, amount), "Add_NullItem_ThrowsArgumentNullException: Adding a null item should throw ArgumentNullException.");
        }

        [Test]
        public void Remove_NullItem_ReturnsFalse()
        {
            // Arrange
            string item = null;

            // Act
            bool result = inventory.Remove(item, 1.0f);

            // Assert
            Assert.IsFalse(result, "Remove_NullItem_ReturnsFalse: Removing a null item should return false.");
        }

        [Test]
        public void Contains_NullItem_ReturnsFalse()
        {
            // Arrange
            string item = null;

            // Act
            bool contains = inventory.Contains(item);

            // Assert
            Assert.IsFalse(contains, "Contains_NullItem_ReturnsFalse: Inventory should not contain a null item.");
        }

        [Test]
        public void TryGetValue_NullItem_ReturnsFalse()
        {
            // Arrange
            string item = null;

            // Act
            bool result = inventory.TryGetValue(item, out float amount);

            // Assert
            Assert.IsFalse(result, "TryGetValue_NullItem_ReturnsFalse: TryGetValue should return false for a null item.");
            Assert.AreEqual(0.0f, amount, "TryGetValue_NullItem_ReturnsFalse: Retrieved amount should be zero for a null item.");
        }

        [Test]
        public void TransferAllTo_NullInventory_ThrowsArgumentNullException()
        {
            // Arrange
            IInventory<string> target = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => inventory.TransferAllTo(target), "TransferAllTo_NullInventory_ThrowsArgumentNullException: Transferring to a null inventory should throw ArgumentNullException.");
        }

        #endregion

        #region Additional Functional Tests

        [Test]
        public void Add_Item_WithLargeAmount()
        {
            // Arrange
            string item = "Gold";
            float amount = float.MaxValue;

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_Item_WithLargeAmount: Inventory should contain the item.");
            Assert.AreEqual(amount, inventory[item], "Add_Item_WithLargeAmount: The amount should match the large value added.");
        }

        [Test]
        public void Remove_Item_ToNegativeAmount_ThrowsException()
        {
            // Arrange
            string item = "Silver";
            float initialAmount = 5.0f;
            float removeAmount = 10.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsFalse(result, "Remove_Item_ToNegativeAmount_ThrowsException: Removing more than available should return false.");
            Assert.AreEqual(initialAmount, inventory[item], "Remove_Item_ToNegativeAmount_ThrowsException: The amount should remain unchanged.");
        }

        [Test]
        public void Add_Item_WithDecimalAmount()
        {
            // Arrange
            string item = "Gem";
            float amount = 2.5f;

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_Item_WithDecimalAmount: Inventory should contain the item.");
            Assert.AreEqual(amount, inventory[item], "Add_Item_WithDecimalAmount: The amount should match the decimal value added.");
        }

        [Test]
        public void Remove_Item_PartialAndFull()
        {
            // Arrange
            string item = "Key";
            float initialAmount = 3.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool firstRemove = inventory.Remove(item, 1.0f);
            bool secondRemove = inventory.Remove(item, 2.0f);

            // Assert
            Assert.IsTrue(firstRemove, "Remove_Item_PartialAndFull: First remove should return true.");
            Assert.IsTrue(secondRemove, "Remove_Item_PartialAndFull: Second remove should return true.");
            Assert.IsFalse(inventory.Contains(item), "Remove_Item_PartialAndFull: Item should be removed after full amount is taken.");
        }

        [Test]
        public void TransferAllTo_MultipleTransfers_TargetAccumulatesAmounts()
        {
            // Arrange
            var source1 = new Inventory<string>();
            source1.Add("Potion", 5.0f);
            var source2 = new Inventory<string>();
            source2.Add("Potion", 3.0f);
            var target = new Inventory<string>();

            // Act
            source1.TransferAllTo(target);
            source2.TransferAllTo(target);

            // Assert
            Assert.AreEqual(0, source1.Count, "TransferAllTo_MultipleTransfers_TargetAccumulatesAmounts: Source1 should be empty after transfer.");
            Assert.AreEqual(0, source2.Count, "TransferAllTo_MultipleTransfers_TargetAccumulatesAmounts: Source2 should be empty after transfer.");
            Assert.AreEqual(1, target.Count, "TransferAllTo_MultipleTransfers_TargetAccumulatesAmounts: Target should have one item.");
            Assert.AreEqual(8.0f, target["Potion"], "TransferAllTo_MultipleTransfers_TargetAccumulatesAmounts: Potion amount should be accumulated correctly in target.");
        }

        [Test]
        public void Clear_WithEventSubscribers_NoExceptions()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);
            inventory.OnInventoryChange += (item, oldAmount, newAmount) => { /* Do nothing */ };

            // Act & Assert
            Assert.DoesNotThrow(() => inventory.Clear(), "Clear_WithEventSubscribers_NoExceptions: Clearing inventory with event subscribers should not throw exceptions.");
        }

        [Test]
        public void Add_Item_WithFloatingPointPrecision()
        {
            // Arrange
            string item = "Magic Dust";
            float amount1 = 0.1f;
            float amount2 = 0.2f;
            float expectedTotal = 0.3f;

            // Act
            inventory.Add(item, amount1);
            inventory.Add(item, amount2);

            // Assert
            Assert.AreEqual(expectedTotal, inventory[item], 1e-6, "Add_Item_WithFloatingPointPrecision: The total amount should handle floating point precision correctly.");
        }

        [Test]
        public void Remove_Item_WithFloatingPointPrecision()
        {
            // Arrange
            string item = "Magic Dust";
            float initialAmount = 0.3f;
            float removeAmount = 0.1f;
            float expectedAmount = 0.2f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsTrue(result, "Remove_Item_WithFloatingPointPrecision: Remove should return true.");
            Assert.AreEqual(expectedAmount, inventory[item], 1e-6, "Remove_Item_WithFloatingPointPrecision: The amount should handle floating point precision correctly.");
        }

        [Test]
        public void TransferAllTo_SameInventory_NoDuplicates()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => inventory.TransferAllTo(inventory), "TransferAllTo_SameInventory_NoDuplicates: Transferring to the same inventory should throw InvalidOperationException.");
        }

        [Test]
        public void GetEnumerator_ModifyingInventoryDuringEnumeration_ThrowsException()
        {
            // Arrange
            inventory.Add("Sword", 1.0f);
            inventory.Add("Shield", 2.0f);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (var kvp in inventory)
                {
                    inventory.Add("Potion", 5.0f);
                }
            }, "GetEnumerator_ModifyingInventoryDuringEnumeration_ThrowsException: Modifying inventory during enumeration should throw InvalidOperationException.");
        }

        [Test]
        public void Add_Item_MaxFloatAmount()
        {
            // Arrange
            string item = "Infinity Stone";
            float amount = float.MaxValue;

            // Act
            inventory.Add(item, amount);

            // Assert
            Assert.IsTrue(inventory.Contains(item), "Add_Item_MaxFloatAmount: Inventory should contain the item.");
            Assert.AreEqual(amount, inventory[item], "Add_Item_MaxFloatAmount: The amount should match float.MaxValue.");
        }

        [Test]
        public void Remove_Item_MaxFloatAmount_RemovesCorrectly()
        {
            // Arrange
            string item = "Infinity Stone";
            float amount = float.MaxValue;
            inventory.Add(item, amount);

            // Act
            bool result = inventory.Remove(item, amount);

            // Assert
            Assert.IsTrue(result, "Remove_Item_MaxFloatAmount_RemovesCorrectly: Remove should return true.");
            Assert.IsFalse(inventory.Contains(item), "Remove_Item_MaxFloatAmount_RemovesCorrectly: Item should be removed from inventory.");
        }

        [Test]
        public void Remove_Item_ResultingInNegativeAmount_ThrowsException()
        {
            // Arrange
            string item = "Silver";
            float initialAmount = 1.0f;
            float removeAmount = 2.0f;
            inventory.Add(item, initialAmount);

            // Act
            bool result = inventory.Remove(item, removeAmount);

            // Assert
            Assert.IsFalse(result, "Remove_Item_ResultingInNegativeAmount_ThrowsException: Removing more than available should return false.");
            Assert.AreEqual(initialAmount, inventory[item], "Remove_Item_ResultingInNegativeAmount_ThrowsException: The amount should remain unchanged.");
        }

        [Test]
        public void Add_Item_WithLargeNumberOfItems()
        {
            // Arrange
            int itemCount = 1000;
            for (int i = 0; i < itemCount; i++)
            {
                inventory.Add($"Item_{i}", 1.0f);
            }

            // Act
            int count = inventory.Count;

            // Assert
            Assert.AreEqual(itemCount, count, "Add_Item_WithLargeNumberOfItems: Inventory should contain all added items.");
        }

        [Test]
        public void Remove_Item_FromLargeInventory_RemovesCorrectly()
        {
            // Arrange
            int itemCount = 1000;
            string targetItem = "Item_500";
            for (int i = 0; i < itemCount; i++)
            {
                inventory.Add($"Item_{i}", 1.0f);
            }

            // Act
            bool removed = inventory.Remove(targetItem, 1.0f);

            // Assert
            Assert.IsTrue(removed, "Remove_Item_FromLargeInventory_RemovesCorrectly: Remove should return true for existing item.");
            Assert.IsFalse(inventory.Contains(targetItem), "Remove_Item_FromLargeInventory_RemovesCorrectly: Item should be removed from inventory.");
            Assert.AreEqual(itemCount - 1, inventory.Count, "Remove_Item_FromLargeInventory_RemovesCorrectly: Inventory count should decrease by one.");
        }

        #endregion
    }
}