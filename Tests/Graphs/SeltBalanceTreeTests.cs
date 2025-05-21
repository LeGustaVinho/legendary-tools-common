using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LegendaryTools.GraphV2.Tests
{
    [TestFixture]
    public class SelfBalanceTreeTests
    {
        // Test 1: Insert into an empty tree and verify the root node
        [Test]
        public void TestInsertIntoEmptyTree()
        {
            // Arrange
            int degree = 3; // Minimum degree t = 3
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(degree);

            // Act
            int keyToInsert = 10;
            SelfBalanceTreeNode<int> newNode = new SelfBalanceTreeNode<int>();
            newNode.Keys.Add(keyToInsert);
            bTree.AddSelfBalanceTreeNode(newNode);

            // Assert
            SelfBalanceTreeNode<int> rootNode = bTree.RootNode as SelfBalanceTreeNode<int>;
            Assert.IsNotNull(rootNode, "Root node should not be null after insertion.");
            Assert.AreEqual(1, rootNode.Keys.Count, "Root node should contain exactly one key after insertion.");
            Assert.AreEqual(keyToInsert, rootNode.Keys[0], $"Root node should contain the inserted key {keyToInsert}.");
        }

        // Test 2: Insert nodes to cause a split and verify tree structure
        [Test]
        public void TestInsertCausesSplit()
        {
            // Arrange
            int degree = 3; // t = 3
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(degree);

            // Act
            // Insert keys to fill the root node and cause a split
            int[] keysToInsert = { 10, 20, 5, 6, 12, 30 };
            foreach (int key in keysToInsert)
            {
                SelfBalanceTreeNode<int> newNode = new SelfBalanceTreeNode<int>();
                newNode.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(newNode);
            }

            // Assert
            SelfBalanceTreeNode<int> rootNode = bTree.RootNode as SelfBalanceTreeNode<int>;
            Assert.IsNotNull(rootNode, "Root node should not be null after insertions.");
            Assert.AreEqual(1, rootNode.Keys.Count, "Root node should contain one key after split.");
            int expectedRootKey = 10;
            Assert.AreEqual(expectedRootKey, rootNode.Keys[0],
                $"Root node key should be {expectedRootKey} after split.");

            Assert.AreEqual(2, rootNode.ChildNodes.Count, "Root node should have two children after split.");

            SelfBalanceTreeNode<int> leftChild = rootNode.ChildNodes[0] as SelfBalanceTreeNode<int>;
            SelfBalanceTreeNode<int> rightChild = rootNode.ChildNodes[1] as SelfBalanceTreeNode<int>;

            // Left child keys should be 5,6
            Assert.AreEqual(2, leftChild.Keys.Count, "Left child should have two keys after split.");
            CollectionAssert.AreEquivalent(new[] { 5, 6 }, leftChild.Keys, "Left child keys should be 5 and 6.");

            // Right child keys should be 12,20,30
            Assert.AreEqual(3, rightChild.Keys.Count, "Right child should have three keys after split.");
            CollectionAssert.AreEquivalent(new[] { 12, 20, 30 }, rightChild.Keys,
                "Right child keys should be 12, 20, and 30.");
        }

        // Test 3: Insert multiple nodes and verify in-order traversal gives sorted keys
        [Test]
        public void TestInOrderTraversalAfterInsertions()
        {
            // Arrange
            int degree = 3; // t = 3
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(degree);

            // Act
            // Insert keys in random order
            int[] keysToInsert = { 50, 20, 70, 10, 30, 60, 80, 15, 25, 65 };
            foreach (int key in keysToInsert)
            {
                SelfBalanceTreeNode<int> newNode = new SelfBalanceTreeNode<int>();
                newNode.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(newNode);
            }

            // Collect keys using in-order traversal
            List<int> collectedKeys = new List<int>();
            InOrderTraversal(bTree.RootNode as SelfBalanceTreeNode<int>, collectedKeys);

            // Assert
            int[] expectedKeys = (int[])keysToInsert.Clone();
            Array.Sort(expectedKeys);
            Assert.AreEqual(expectedKeys.Length, collectedKeys.Count,
                "Collected keys count should match the number of inserted keys.");
            CollectionAssert.AreEqual(expectedKeys, collectedKeys, "In-order traversal should result in sorted keys.");
        }

        // Helper method for in-order traversal
        private void InOrderTraversal(SelfBalanceTreeNode<int> node, List<int> keys)
        {
            if (node == null)
                return;

            int i;
            for (i = 0; i < node.Keys.Count; i++)
            {
                if (!node.IsLeaf) InOrderTraversal(node.ChildNodes[i] as SelfBalanceTreeNode<int>, keys);
                keys.Add(node.Keys[i]);
            }

            if (!node.IsLeaf) InOrderTraversal(node.ChildNodes[i] as SelfBalanceTreeNode<int>, keys);
        }

        // Test 4: Insert duplicate keys and verify they are handled correctly
        [Test]
        public void TestInsertDuplicateKeys()
        {
            // Arrange
            int degree = 3;
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(degree);

            // Act
            int[] keysToInsert = { 10, 20, 10, 30, 20 };
            foreach (int key in keysToInsert)
            {
                SelfBalanceTreeNode<int> newNode = new SelfBalanceTreeNode<int>();
                newNode.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(newNode);
            }

            // Collect keys using in-order traversal
            List<int> collectedKeys = new List<int>();
            InOrderTraversal(bTree.RootNode as SelfBalanceTreeNode<int>, collectedKeys);

            // Assert
            int expectedKeyCount = keysToInsert.Length;
            Assert.AreEqual(expectedKeyCount, collectedKeys.Count, "Collected keys count should include duplicates.");
            int[] expectedKeys = (int[])keysToInsert.Clone();
            Array.Sort(expectedKeys);
            collectedKeys.Sort();
            CollectionAssert.AreEqual(expectedKeys, collectedKeys,
                "In-order traversal should include duplicates and be sorted.");
        }

        // Test 5: Insert nodes and verify nodes do not exceed maximum keys per node
        [Test]
        public void TestNodeKeysDoNotExceedMaximum()
        {
            // Arrange
            int degree = 3;
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(degree);

            // Act
            // Insert enough keys to fill multiple nodes
            int numberOfKeys = 100;
            for (int i = 1; i <= numberOfKeys; i++)
            {
                SelfBalanceTreeNode<int> newNode = new SelfBalanceTreeNode<int>();
                newNode.Keys.Add(i);
                bTree.AddSelfBalanceTreeNode(newNode);
            }

            // Collect all nodes
            List<SelfBalanceTreeNode<int>> allNodes = new List<SelfBalanceTreeNode<int>>();
            CollectAllNodes(bTree.RootNode as SelfBalanceTreeNode<int>, allNodes);

            // Assert
            int maxKeysPerNode = 2 * degree - 1;
            foreach (SelfBalanceTreeNode<int> node in allNodes)
                Assert.LessOrEqual(node.Keys.Count, maxKeysPerNode,
                    $"Node keys count ({node.Keys.Count}) should not exceed maximum allowed ({maxKeysPerNode}).");
        }

        // Helper method to collect all nodes in the tree
        private void CollectAllNodes(SelfBalanceTreeNode<int> node, List<SelfBalanceTreeNode<int>> allNodes)
        {
            if (node == null)
                return;

            allNodes.Add(node);

            if (!node.IsLeaf)
                foreach (ITreeNode child in node.ChildNodes)
                    CollectAllNodes(child as SelfBalanceTreeNode<int>, allNodes);
        }

        // Test 1: Insert into an empty tree
        [Test]
        public void Test_InsertIntoEmptyTree()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2); // Degree t = 2
            SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
            node.Keys.Add(10);

            // Act
            bTree.AddSelfBalanceTreeNode(node);

            // Assert
            SelfBalanceTreeNode<int> root = (SelfBalanceTreeNode<int>)bTree.RootNode;
            Assert.IsNotNull(root, "Root node should not be null after insertion.");
            Assert.AreEqual(1, root.Keys.Count, "Root node should have one key.");
            Assert.AreEqual(10, root.Keys[0], "Root node should contain the key 10.");
        }

        // Test 2: Insert multiple keys and verify tree structure
        [Test]
        public void Test_InsertMultipleKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5, 6, 12 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            SelfBalanceTreeNode<int> root = (SelfBalanceTreeNode<int>)bTree.RootNode;
            Assert.IsNotNull(root, "Root node should not be null after multiple insertions.");
            Assert.AreEqual(1, root.Keys.Count, "Root node should have one key after splits.");
            Assert.AreEqual(10, root.Keys[0], "Root node's key should be 10 after splits.");
            Assert.AreEqual(2, root.ChildNodes.Count, "Root node should have two children after splits.");

            SelfBalanceTreeNode<int> leftChild = root.ChildNodes[0] as SelfBalanceTreeNode<int>;
            SelfBalanceTreeNode<int> rightChild = root.ChildNodes[1] as SelfBalanceTreeNode<int>;

            Assert.AreEqual(2, leftChild.Keys.Count, "Left child should have two keys.");
            Assert.AreEqual(2, rightChild.Keys.Count, "Right child should have two keys.");
        }

        // Test 3: Tree remains balanced after sequential insertions
        [Test]
        public void Test_TreeRemainsBalancedAfterInsertions()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            for (int i = 1; i <= 20; i++)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(i);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> root = (SelfBalanceTreeNode<int>)bTree.RootNode;

            // Assert
            Assert.IsTrue(IsBalanced(root, bTree.Degree), "Tree should remain balanced after sequential insertions.");
        }

        // Helper method to check if the tree is balanced
        private bool IsBalanced(SelfBalanceTreeNode<int> node, int degree)
        {
            if (node == null) return true;

            if (node.Keys.Count > 2 * degree - 1)
                return false;

            foreach (SelfBalanceTreeNode<int> child in node.ChildNodes)
                if (!IsBalanced(child, degree))
                    return false;

            return true;
        }

        // Test 4: Insert duplicate keys
        [Test]
        public void Test_InsertDuplicateKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 15, 15, 15 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            int foundCount = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 15);
            Assert.AreEqual(3, foundCount, "Tree should contain three instances of the key 15.");
        }

        // Helper method to count occurrences of a key
        private int CountOccurrences(SelfBalanceTreeNode<int> node, int key)
        {
            int count = 0;
            if (node == null) return count;

            count += node.Keys.FindAll(k => k == key).Count;

            foreach (SelfBalanceTreeNode<int> child in node.ChildNodes) count += CountOccurrences(child, key);

            return count;
        }

        // Test 5: Insert keys in ascending order
        [Test]
        public void Test_InsertAscendingOrder()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            for (int i = 1; i <= 10; i++)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(i);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Keys should be in ascending order after insertions.");
        }

        // Test 6: Insert keys in descending order
        [Test]
        public void Test_InsertDescendingOrder()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            for (int i = 10; i >= 1; i--)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(i);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Keys should be in ascending order after insertions.");
        }

        // Test 7: Insert random keys
        [Test]
        public void Test_InsertRandomKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            int[] keys = { 25, 5, 15, 35, 10, 20, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Keys should be in ascending order after random insertions.");
        }

        // Test 10: Insert into tree with small degree
        [Test]
        public void Test_InsertIntoSmallDegreeTree()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 5, 15, 25, 35, 45 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Keys should be in ascending order in small degree tree.");
        }

        // Test 11: Insert into tree with larger degree
        [Test]
        public void Test_InsertIntoLargeDegreeTree()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(5); // Degree t = 5
            int[] keys = { 100, 200, 300, 400, 500, 600, 700, 800 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            SelfBalanceTreeNode<int> root = (SelfBalanceTreeNode<int>)bTree.RootNode;
            Assert.IsTrue(root.Keys.Count <= 2 * bTree.Degree - 1,
                "Root should not exceed maximum keys for large degree tree.");
        }

        // Test 13: Number of keys is as expected after insertions
        [Test]
        public void Test_NumberOfKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            int[] keys = { 10, 20, 5, 15, 25, 30, 35, 40 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            int totalKeys = CountTotalKeys((SelfBalanceTreeNode<int>)bTree.RootNode);
            Assert.AreEqual(keys.Length, totalKeys,
                "Total number of keys in tree should match number of inserted keys.");
        }

        // Helper method to count total keys in the tree
        private int CountTotalKeys(SelfBalanceTreeNode<int> node)
        {
            int count = node.Keys.Count;
            foreach (SelfBalanceTreeNode<int> child in node.ChildNodes) count += CountTotalKeys(child);
            return count;
        }

        // Test 14: Keys are correctly distributed according to B-Tree properties
        [Test]
        public void Test_KeysDistributedCorrectly()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 20, 40, 60, 80, 100, 120 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            Assert.IsTrue(CheckBTreeProperties(bTree, (SelfBalanceTreeNode<int>)bTree.RootNode, bTree.Degree),
                "Tree should satisfy B-Tree properties after insertions.");
        }

        // Helper method to check B-Tree properties
        private bool CheckBTreeProperties(SelfBalanceTree<int> bTree, SelfBalanceTreeNode<int> node, int degree)
        {
            if (node == null) return true;

            // Check number of keys
            if (node != bTree.RootNode && (node.Keys.Count < degree - 1 || node.Keys.Count > 2 * degree - 1))
                return false;

            // Check number of children
            if (!node.IsLeaf)
            {
                if (node.ChildNodes.Count != node.Keys.Count + 1)
                    return false;

                foreach (SelfBalanceTreeNode<int> child in node.ChildNodes)
                    if (!CheckBTreeProperties(bTree, child, degree))
                        return false;
            }

            return true;
        }

        // Test 15: Inserting negative keys
        [Test]
        public void Test_InsertNegativeKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { -10, -20, -30, -40, -50 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Negative keys should be in ascending order after insertions.");
        }

        // Test 16: Insert zero as a key
        [Test]
        public void Test_InsertZeroKey()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 0, 10, 20 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            bool foundZero = ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 0);
            Assert.IsTrue(foundZero, "Tree should contain the key 0 after insertion.");
        }

        // Helper method to check if tree contains a key
        private bool ContainsKey(SelfBalanceTreeNode<int> node, int key)
        {
            if (node == null)
                return false;

            if (node.Keys.Contains(key))
                return true;

            foreach (SelfBalanceTreeNode<int> child in node.ChildNodes)
                if (ContainsKey(child, key))
                    return true;

            return false;
        }

        // Test 17: Insert maximum integer value
        [Test]
        public void Test_InsertMaxInt()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int maxInt = int.MaxValue;
            int[] keys = { maxInt, 10, 20 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            bool foundMaxInt = ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, maxInt);
            Assert.IsTrue(foundMaxInt, "Tree should contain the maximum integer value after insertion.");
        }

        // Test 18: Insert minimum integer value
        [Test]
        public void Test_InsertMinInt()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int minInt = int.MinValue;
            int[] keys = { minInt, 10, 20 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            bool foundMinInt = ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, minInt);
            Assert.IsTrue(foundMinInt, "Tree should contain the minimum integer value after insertion.");
        }

        // Test 19: Insert keys with large gaps
        [Test]
        public void Test_InsertKeysWithLargeGaps()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            int[] keys = { 1, 1000, 2000, 3000, 4000 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Keys with large gaps should be in order after insertions.");
        }

        // Test 20: Insert keys with duplicates and verify counts
        [Test]
        public void Test_InsertDuplicatesAndVerifyCounts()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 10, 10, 20, 20, 30 };

            // Act
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Assert
            int count10 = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 10);
            int count20 = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 20);
            int count30 = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 30);

            Assert.AreEqual(3, count10, "There should be three instances of key 10.");
            Assert.AreEqual(2, count20, "There should be two instances of key 20.");
            Assert.AreEqual(1, count30, "There should be one instance of key 30.");
        }

        // Test 1: Remove a key from a leaf node
        [Test]
        public void Test_RemoveKeyFromLeafNode()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(5);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 5.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 5),
                "Key 5 should be removed from the tree.");
        }

        // Test 2: Remove a key from an internal node where the predecessor child has enough keys
        [Test]
        public void Test_RemoveKeyFromInternalNode_PredecessorHasEnoughKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 20, 10, 30, 5, 15 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(20);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 20.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 20),
                "Key 20 should be removed from the tree.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after removing key 20.");
        }

        // Helper method to assert the tree is in order
        private void AssertTreeInOrder(SelfBalanceTreeNode<int> node, string message)
        {
            List<int> keys = new List<int>();
            InOrderTraversal(node, keys);

            for (int i = 1; i < keys.Count; i++) Assert.IsTrue(keys[i - 1] <= keys[i], message);
        }

        // Test 3: Remove a key from an internal node where the successor child has enough keys
        [Test]
        public void Test_RemoveKeyFromInternalNode_SuccessorHasEnoughKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 50, 30, 70, 10, 40, 60, 80 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(50);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 50.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 50),
                "Key 50 should be removed from the tree.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after removing key 50.");
        }

        // Test 4: Remove a key causing a merge of children
        [Test]
        public void Test_RemoveKey_CausingMerge()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 20, 5, 15, 25, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Remove 25 to cause a merge
            SelfBalanceTreeNode<int> nodeToRemove1 = new SelfBalanceTreeNode<int>();
            nodeToRemove1.Keys.Add(25);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove1, out ISelfBalanceTreeNode<int>[] removedNodes1);

            // Act
            SelfBalanceTreeNode<int> nodeToRemove2 = new SelfBalanceTreeNode<int>();
            nodeToRemove2.Keys.Add(20);
            bool success =
                bTree.RemoveSelfBalanceTreeNode(nodeToRemove2, out ISelfBalanceTreeNode<int>[] removedNodes2);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 20.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 20),
                "Key 20 should be removed from the tree.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode, "Tree should remain in order after merging.");
        }

        // Test 5: Attempt to remove a key that does not exist
        [Test]
        public void Test_RemoveNonExistentKey()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(40);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsFalse(success, "Removal should fail for non-existent key 40.");
        }

        // Test 6: Remove a key from the root node
        [Test]
        public void Test_RemoveKeyFromRoot()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(10);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 10.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 10),
                "Key 10 should be removed from the tree.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after removing key from root.");
        }

        // Test 7: Remove multiple keys in sequence
        [Test]
        public void Test_RemoveMultipleKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 5, 15, 25, 35, 45 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);
                Assert.IsTrue(success, $"Removal should succeed for key {key}.");
                Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, key),
                    $"Key {key} should be removed from the tree.");
            }

            Assert.IsNull(bTree.RootNode, "Tree should be empty after removing all keys.");
        }

        // Test 8: Remove keys until the tree becomes empty
        [Test]
        public void Test_RemoveUntilTreeIsEmpty()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 30, 40, 50 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);
                Assert.IsTrue(success, $"Removal should succeed for key {key}.");
            }

            Assert.IsNull(bTree.RootNode, "Tree should be empty after all keys are removed.");
        }

        // Test 9: Remove a key causing underflow and requiring borrow from previous sibling
        [Test]
        public void Test_RemoveKey_UnderflowBorrowFromPrevSibling()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5, 15, 25 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Remove 25 to set up the underflow
            SelfBalanceTreeNode<int> nodeToRemove1 = new SelfBalanceTreeNode<int>();
            nodeToRemove1.Keys.Add(25);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove1, out ISelfBalanceTreeNode<int>[] removedNodes1);

            // Act
            SelfBalanceTreeNode<int> nodeToRemove2 = new SelfBalanceTreeNode<int>();
            nodeToRemove2.Keys.Add(15);
            bool success =
                bTree.RemoveSelfBalanceTreeNode(nodeToRemove2, out ISelfBalanceTreeNode<int>[] removedNodes2);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 15.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after borrowing from previous sibling.");
        }

        // Test 10: Remove a key causing underflow and requiring borrow from next sibling
        [Test]
        public void Test_RemoveKey_UnderflowBorrowFromNextSibling()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 20, 10, 30, 5, 15, 25, 35 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Remove 5 and 15 to set up the underflow
            SelfBalanceTreeNode<int> nodeToRemove1 = new SelfBalanceTreeNode<int>();
            nodeToRemove1.Keys.Add(5);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove1, out ISelfBalanceTreeNode<int>[] removedNodes1);

            SelfBalanceTreeNode<int> nodeToRemove2 = new SelfBalanceTreeNode<int>();
            nodeToRemove2.Keys.Add(15);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove2, out ISelfBalanceTreeNode<int>[] removedNodes2);

            // Act
            SelfBalanceTreeNode<int> nodeToRemove3 = new SelfBalanceTreeNode<int>();
            nodeToRemove3.Keys.Add(10);
            bool success =
                bTree.RemoveSelfBalanceTreeNode(nodeToRemove3, out ISelfBalanceTreeNode<int>[] removedNodes3);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 10.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after borrowing from next sibling.");
        }

        // Test 11: Remove a key causing underflow and requiring merge with previous sibling
        [Test]
        public void Test_RemoveKey_UnderflowMergeWithPrevSibling()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 50, 20, 70, 10, 30, 60, 80 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Remove 10 and 30 to cause underflow
            SelfBalanceTreeNode<int> nodeToRemove1 = new SelfBalanceTreeNode<int>();
            nodeToRemove1.Keys.Add(10);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove1, out ISelfBalanceTreeNode<int>[] removedNodes1);

            SelfBalanceTreeNode<int> nodeToRemove2 = new SelfBalanceTreeNode<int>();
            nodeToRemove2.Keys.Add(30);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove2, out ISelfBalanceTreeNode<int>[] removedNodes2);

            // Act
            SelfBalanceTreeNode<int> nodeToRemove3 = new SelfBalanceTreeNode<int>();
            nodeToRemove3.Keys.Add(20);
            bool success =
                bTree.RemoveSelfBalanceTreeNode(nodeToRemove3, out ISelfBalanceTreeNode<int>[] removedNodes3);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 20.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after merging with previous sibling.");
        }

        // Test 12: Remove a key causing underflow and requiring merge with next sibling
        [Test]
        public void Test_RemoveKey_UnderflowMergeWithNextSibling()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 50, 20, 70, 10, 30, 60, 80 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Remove 60 and 80 to cause underflow
            SelfBalanceTreeNode<int> nodeToRemove1 = new SelfBalanceTreeNode<int>();
            nodeToRemove1.Keys.Add(60);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove1, out ISelfBalanceTreeNode<int>[] removedNodes1);

            SelfBalanceTreeNode<int> nodeToRemove2 = new SelfBalanceTreeNode<int>();
            nodeToRemove2.Keys.Add(80);
            bTree.RemoveSelfBalanceTreeNode(nodeToRemove2, out ISelfBalanceTreeNode<int>[] removedNodes2);

            // Act
            SelfBalanceTreeNode<int> nodeToRemove3 = new SelfBalanceTreeNode<int>();
            nodeToRemove3.Keys.Add(70);
            bool success =
                bTree.RemoveSelfBalanceTreeNode(nodeToRemove3, out ISelfBalanceTreeNode<int>[] removedNodes3);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 70.");
            AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                "Tree should remain in order after merging with next sibling.");
        }

        // Test 13: Remove duplicate keys
        [Test]
        public void Test_RemoveDuplicateKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 10, 20, 20, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(10);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            Assert.IsTrue(success, "Removal should succeed for key 10.");
            int count10 = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 10);
            Assert.AreEqual(1, count10, "There should be one instance of key 10 remaining.");

            nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(10);
            success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out removedNodes);

            Assert.IsTrue(success, "Removal should succeed for key 10.");
            count10 = CountOccurrences((SelfBalanceTreeNode<int>)bTree.RootNode, 10);
            Assert.AreEqual(0, count10, "There should be no instances of key 10 remaining.");
        }

        // Test 14: Remove from a tree with degree greater than 2
        [Test]
        public void Test_RemoveFromLargeDegreeTree()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(4); // Degree t = 4
            int[] keys = { 10, 20, 30, 40, 50, 60, 70 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(40);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for key 40 in large degree tree.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, 40),
                "Key 40 should be removed from the tree.");
        }

        // Test 15: Remove the minimum key in the tree
        [Test]
        public void Test_RemoveMinimumKey()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 50, 20, 70, 10, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            int minKey = 10;
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(minKey);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for minimum key 10.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, minKey),
                "Minimum key should be removed from the tree.");
        }

        // Test 16: Remove the maximum key in the tree
        [Test]
        public void Test_RemoveMaximumKey()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 50, 20, 70, 60, 80 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            int maxKey = 80;
            SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
            nodeToRemove.Keys.Add(maxKey);
            bool success = bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

            // Assert
            Assert.IsTrue(success, "Removal should succeed for maximum key 80.");
            Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, maxKey),
                "Maximum key should be removed from the tree.");
        }

        // Test 17: Remove keys in random order
        [Test]
        public void Test_RemoveKeysInRandomOrder()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            int[] keys = { 25, 15, 35, 10, 20, 30, 40 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            int[] keysToRemove = { 30, 15, 40, 25 };
            // Act & Assert
            foreach (int key in keysToRemove)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);
                Assert.IsTrue(success, $"Removal should succeed for key {key}.");
                AssertTreeInOrder((SelfBalanceTreeNode<int>)bTree.RootNode,
                    $"Tree should remain in order after removing key {key}.");
            }
        }

        // Test 18: Remove keys from a tree with negative keys
        [Test]
        public void Test_RemoveNegativeKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { -10, -20, -30, -40 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);
                Assert.IsTrue(success, $"Removal should succeed for negative key {key}.");
                Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, key),
                    $"Negative key {key} should be removed from the tree.");
            }
        }

        // Test 19: Remove keys with large integer values
        [Test]
        public void Test_RemoveLargeIntegerKeys()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 1000000, 2000000, 3000000 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act & Assert
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);
                Assert.IsTrue(success, $"Removal should succeed for large key {key}.");
                Assert.IsFalse(ContainsKey((SelfBalanceTreeNode<int>)bTree.RootNode, key),
                    $"Large key {key} should be removed from the tree.");
            }
        }

        // Test 20: Remove keys and verify tree remains valid B-Tree after each removal
        [Test]
        public void Test_RemoveKeys_VerifyBTreeProperties()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(3);
            int[] keys = { 50, 20, 70, 10, 30, 60, 80 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            int[] keysToRemove = { 30, 70, 20, 80 };
            // Act & Assert
            foreach (int key in keysToRemove)
            {
                SelfBalanceTreeNode<int> nodeToRemove = new SelfBalanceTreeNode<int>();
                nodeToRemove.Keys.Add(key);
                bool success =
                    bTree.RemoveSelfBalanceTreeNode(nodeToRemove, out ISelfBalanceTreeNode<int>[] removedNodes);

                Assert.IsTrue(success, $"Removal should succeed for key {key}.");
                Assert.IsTrue(CheckBTreeProperties((SelfBalanceTreeNode<int>)bTree.RootNode, bTree.Degree),
                    $"Tree should satisfy B-Tree properties after removing key {key}.");
            }
        }

        // Helper method to check B-Tree properties
        private bool CheckBTreeProperties(SelfBalanceTreeNode<int> node, int degree)
        {
            if (node == null) return true;

            // Check number of keys
            if (node.ParentNode == null)
            {
                // Root node
                if (node.Keys.Count < 1 || node.Keys.Count > 2 * degree - 1)
                    return false;
            }
            else
            {
                // Non-root nodes
                if (node.Keys.Count < degree - 1 || node.Keys.Count > 2 * degree - 1)
                    return false;
            }

            // Check number of children
            if (!node.IsLeaf)
            {
                if (node.ChildNodes.Count != node.Keys.Count + 1)
                    return false;

                foreach (SelfBalanceTreeNode<int> child in node.ChildNodes)
                    if (!CheckBTreeProperties(child, degree))
                        return false;
            }

            return true;
        }

        // Test 1: Test if the tree is a directed acyclic graph
        [Test]
        public void Test_IsDirectedAcyclic()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);

            // Act
            bool isDirectedAcyclic = bTree.IsDirectedAcyclic;

            // Assert
            Assert.IsTrue(isDirectedAcyclic, "SelfBalanceTree should be a directed acyclic graph upon initialization.");
        }

        // Test 2: Test RootNode property returns the correct node
        [Test]
        public void Test_RootNodeProperty()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
            node.Keys.Add(10);

            // Act
            bTree.AddSelfBalanceTreeNode(node);
            SelfBalanceTreeNode<int> rootNode = bTree.RootNode as SelfBalanceTreeNode<int>;

            // Assert
            Assert.IsNotNull(rootNode, "RootNode should not be null after adding a node.");
            Assert.AreEqual(10, rootNode.Keys[0], "RootNode should contain the key 10.");
        }

        // Test 3: Test Height property returns expected value
        [Test]
        public void Test_HeightProperty()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5, 6, 12 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            int height = bTree.Height;

            // Assert
            Assert.AreEqual(2, height, "Height should be 2 after adding 5 keys to a B-Tree of degree 2.");
        }

        // Test 4: Test Width property returns expected value
        [Test]
        public void Test_WidthProperty()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 1, 2, 3, 4, 5, 6, 7 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }
            
            // Act
            int width = bTree.Width;

            // Assert
            Assert.AreEqual(3, width, "Width should be 4 after adding 7 keys to a B-Tree of degree 2.");
        }

        // Test 5: Test AllNodes property returns all nodes in the tree
        [Test]
        public void Test_AllNodesProperty()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 6, 5, };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }
            
            // Act
            INode[] allNodes = bTree.AllNodes;

            // Assert
            Assert.AreEqual(3, allNodes.Length, "AllNodes should return 3 nodes after adding 3 keys.");
        }

        // Test 7: Test DepthFirstSearch method
        [Test]
        public void Test_DepthFirstSearch()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 10, 20, 5, 6, 12, 15, 30 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            ITreeNode searchResult = bTree.DepthFirstSearch(n => ((SelfBalanceTreeNode<int>)n).Keys.Contains(15));

            // Assert
            Assert.IsNotNull(searchResult, "DepthFirstSearch should find the node containing key 15.");
            Assert.IsTrue(((SelfBalanceTreeNode<int>)searchResult).Keys.Contains(15),
                "Found node should contain the key 15.");
        }

        // Test 8: Test HeightFirstSearch method
        [Test]
        public void Test_HeightFirstSearch()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 1, 2, 3, 4, 5, 6, 7 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            ITreeNode searchResult = bTree.HeightFirstSearch(n => ((SelfBalanceTreeNode<int>)n).Keys.Contains(5));

            // Assert
            Assert.IsNotNull(searchResult, "HeightFirstSearch should find the node containing key 5.");
            Assert.AreEqual(5, ((SelfBalanceTreeNode<int>)searchResult).Keys[0],
                "Found node should contain the key 5.");
        }

        // Test 9: Test DepthFirstTraverse method
        [Test]
        public void Test_DepthFirstTraverse()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 15, 10, 20, 5, 12, 17, 25 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            List<ITreeNode> traversal = bTree.DepthFirstTraverse();

            // Assert
            Assert.IsNotNull(traversal, "DepthFirstTraverse should return a list of nodes.");
            Assert.AreEqual(3, traversal.Count, "Traversal should visit all nodes.");
        }

        // Test 10: Test HeightFirstTraverse method
        [Test]
        public void Test_HeightFirstTraverse()
        {
            // Arrange
            SelfBalanceTree<int> bTree = new SelfBalanceTree<int>(2);
            int[] keys = { 8, 4, 12, 2, 6, 10, 14 };
            foreach (int key in keys)
            {
                SelfBalanceTreeNode<int> node = new SelfBalanceTreeNode<int>();
                node.Keys.Add(key);
                bTree.AddSelfBalanceTreeNode(node);
            }

            // Act
            List<ITreeNode> traversal = bTree.HeightFirstTraverse();

            // Assert
            Assert.IsNotNull(traversal, "HeightFirstTraverse should return a list of nodes.");
            Assert.AreEqual(3, traversal.Count, "Traversal should visit all nodes.");
        }
    }
}