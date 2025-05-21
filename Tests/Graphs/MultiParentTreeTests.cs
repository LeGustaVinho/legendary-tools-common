using System;
using NUnit.Framework;

namespace LegendaryTools.GraphV2.Tests
{
[TestFixture]
    public class MultiParentTreeTests
    {
        private MultiParentTree tree;

        [SetUp]
        public void Setup()
        {
            // Runs before each test; instantiate a fresh tree
            tree = new MultiParentTree();
        }

        [Test]
        public void Test01_AddTreeNodeAsRoot()
        {
            // Arrange
            var node = new MultiParentTreeNode();

            // Act
            tree.AddTreeNode(node, null);

            // Assert
            Assert.AreEqual(node, tree.RootNode, "RootNode should be the newly added node.");
        }

        [Test]
        public void Test02_AddTreeNodeWithValidParent()
        {
            // Arrange
            var rootNode = new MultiParentTreeNode();
            var childNode = new MultiParentTreeNode();
            tree.AddTreeNode(rootNode, null);

            // Act
            tree.AddTreeNode(childNode, rootNode);

            // Assert
            Assert.AreEqual(rootNode, tree.RootNode, "RootNode should remain as rootNode.");
            Assert.Contains(childNode, rootNode.ChildNodes, "Child node should be in rootNode's ChildNodes.");
            Assert.Contains(rootNode, childNode.ParentNodes, "rootNode should be in child's ParentNodes.");
        }

        [Test]
        public void Test03_AddTreeNodeWithSelfAsParent_ThrowsException()
        {
            // Arrange
            var node = new MultiParentTreeNode();

            // Act + Assert
            // We expect an exception because a node cannot be its own parent
            Assert.Throws<InvalidOperationException>(() => tree.AddTreeNode(node, node));
        }

        [Test]
        public void Test04_AddTreeNodeWithNullParentWhenRootExists()
        {
            // Arrange
            var rootNode = new MultiParentTreeNode();
            var secondRootCandidate = new MultiParentTreeNode();
            tree.AddTreeNode(rootNode, null);

            // Act
            tree.AddTreeNode(secondRootCandidate, null);

            // Assert
            // The original root node should remain the actual root
            // The second node is added to the graph but not as the root
            Assert.AreEqual(rootNode, tree.RootNode, "RootNode should stay as the first rootNode.");
            Assert.IsTrue(tree.Contains(secondRootCandidate), "Second node is in the graph.");
            Assert.AreNotEqual(secondRootCandidate, tree.RootNode, "Second node should not override the existing root.");
        }

        [Test]
        public void Test05_RemoveTreeNodeRemovesDescendants()
        {
            // Arrange
            var rootNode = new MultiParentTreeNode();
            var child1 = new MultiParentTreeNode();
            var child2 = new MultiParentTreeNode();
            var grandChild = new MultiParentTreeNode();

            // Build structure: root -> child1 -> grandChild, root -> child2
            tree.AddTreeNode(rootNode, null);
            tree.AddTreeNode(child1, rootNode);
            tree.AddTreeNode(child2, rootNode);
            tree.AddTreeNode(grandChild, child1);

            // Act
            var result = tree.RemoveTreeNode(child1, out IMultiParentTreeNode[] removed);

            // Assert
            Assert.IsTrue(result, "RemoveTreeNode should return true for a valid node.");
            Assert.Contains(child1, removed, "child1 should be in the removed list.");
            Assert.Contains(grandChild, removed, "grandChild should be removed with child1.");

            // They should no longer exist in the tree
            Assert.IsFalse(tree.Contains(child1), "child1 should no longer be in the tree.");
            Assert.IsFalse(tree.Contains(grandChild), "grandChild should no longer be in the tree.");

            // rootNode and child2 should remain
            Assert.IsTrue(tree.Contains(rootNode), "rootNode should still be in the tree.");
            Assert.IsTrue(tree.Contains(child2), "child2 should still be in the tree.");
        }

        [Test]
        public void Test06_RemoveTreeNodeThatDoesNotExist()
        {
            // Arrange
            var node = new MultiParentTreeNode(); // Not added to the tree

            // Act
            var result = tree.RemoveTreeNode(node, out IMultiParentTreeNode[] removed);

            // Assert
            Assert.IsFalse(result, "Should return false when removing a node that isn't in the tree.");
            Assert.IsEmpty(removed, "Removed array should be empty.");
        }

        [Test]
        public void Test07_RemoveNullNode()
        {
            // Act
            var result = tree.RemoveTreeNode(null, out IMultiParentTreeNode[] removed);

            // Assert
            Assert.IsFalse(result, "Should return false when removing a null node.");
            Assert.IsEmpty(removed, "Removed array should be empty for null node.");
        }

        [Test]
        public void Test08_DisconnectFromParent_RemovesLinkInBothDirections()
        {
            // Arrange
            var rootNode = new MultiParentTreeNode();
            var childNode = new MultiParentTreeNode();
            tree.AddTreeNode(rootNode, null);
            tree.AddTreeNode(childNode, rootNode);

            // Act
            childNode.DisconnectFromParent(rootNode);

            // Assert
            Assert.False(rootNode.ChildNodes.Contains(childNode), "Child should be removed from parent's ChildNodes.");
            Assert.False(childNode.ParentNodes.Contains(rootNode), "Parent should be removed from child's ParentNodes.");
        }

        [Test]
        public void Test09_DisconnectFromParents_RemovesAllParents()
        {
            // Arrange
            var parentA = new MultiParentTreeNode();
            var parentB = new MultiParentTreeNode();
            var child = new MultiParentTreeNode();

            tree.AddTreeNode(parentA, null);
            tree.AddTreeNode(parentB, null);
            // Connect child to both parents
            child.ConnectToParent(parentA);
            child.ConnectToParent(parentB);

            // Act
            child.DisconnectFromParents();

            // Assert
            Assert.IsFalse(parentA.ChildNodes.Contains(child), "child should be removed from parentA's ChildNodes.");
            Assert.IsFalse(parentB.ChildNodes.Contains(child), "child should be removed from parentB's ChildNodes.");
            Assert.IsEmpty(child.ParentNodes, "Child should have no parents after DisconnectFromParents.");
        }

        [Test]
        public void Test10_ConnectToParentMultipleTimes_NoDuplicateRelationships()
        {
            // Arrange
            var rootNode = new MultiParentTreeNode();
            var childNode = new MultiParentTreeNode();
            tree.AddTreeNode(rootNode, null);

            // Act
            childNode.ConnectToParent(rootNode);
            childNode.ConnectToParent(rootNode); // Attempt again

            // Assert
            Assert.AreEqual(1, childNode.ParentNodes.Count, "ParentNodes should have exactly 1 unique parent.");
            Assert.AreEqual(1, rootNode.ChildNodes.Count, "ChildNodes should have exactly 1 unique child.");
        }

        [Test]
        public void Test11_DepthFirstSearchFindsNode()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            var childA = new MultiParentTreeNode();
            var childB = new MultiParentTreeNode();
            tree.AddTreeNode(root, null);
            tree.AddTreeNode(childA, root);
            tree.AddTreeNode(childB, root);

            // Act
            var foundNode = tree.DepthFirstSearch(n => n == childB);

            // Assert
            Assert.AreEqual(childB, foundNode, "DFS should find childB node.");
        }

        [Test]
        public void Test12_DepthFirstSearchReturnsNullIfNotFound()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            tree.AddTreeNode(root, null);

            // A node not added to the tree
            var outsideNode = new MultiParentTreeNode();

            // Act
            var result = tree.DepthFirstSearch(n => n == outsideNode);

            // Assert
            Assert.IsNull(result, "Should return null if the node is not found.");
        }

        [Test]
        public void Test13_HeightFirstSearchFindsNode()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            var childA = new MultiParentTreeNode();
            var childB = new MultiParentTreeNode();
            tree.AddTreeNode(root, null);
            tree.AddTreeNode(childA, root);
            tree.AddTreeNode(childB, root);

            // Act
            var foundNode = tree.HeightFirstSearch(n => n == childB);

            // Assert
            Assert.AreEqual(childB, foundNode, "BFS should find childB node.");
        }

        [Test]
        public void Test14_HeightFirstSearchReturnsNullIfNotFound()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            tree.AddTreeNode(root, null);

            var nonExistentNode = new MultiParentTreeNode();

            // Act
            var result = tree.HeightFirstSearch(n => n == nonExistentNode);

            // Assert
            Assert.IsNull(result, "Should return null if the node does not exist in the tree.");
        }

        [Test]
        public void Test15_WidthCalculation()
        {
            // Arrange
            // Structure: root has 2 children, each child has 2 children => level 0: root, level 1: 2 nodes, level 2: 4 nodes => max width = 4
            var root = new MultiParentTreeNode();
            var c1 = new MultiParentTreeNode();
            var c2 = new MultiParentTreeNode();
            var gc1 = new MultiParentTreeNode();
            var gc2 = new MultiParentTreeNode();
            var gc3 = new MultiParentTreeNode();
            var gc4 = new MultiParentTreeNode();

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(c1, root);
            tree.AddTreeNode(c2, root);
            tree.AddTreeNode(gc1, c1);
            tree.AddTreeNode(gc2, c1);
            tree.AddTreeNode(gc3, c2);
            tree.AddTreeNode(gc4, c2);

            // Act
            var width = tree.Width;

            // Assert
            Assert.AreEqual(4, width, "Tree width should be 4 at the deepest level.");
        }

        [Test]
        public void Test16_HeightCalculation()
        {
            // Arrange
            // Structure: root -> child -> grandChild => height should be 3
            var root = new MultiParentTreeNode();
            var child = new MultiParentTreeNode();
            var grandChild = new MultiParentTreeNode();

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child, root);
            tree.AddTreeNode(grandChild, child);

            // Act
            var height = tree.Height;

            // Assert
            Assert.AreEqual(3, height, "Height should reflect the longest path from root to leaf.");
        }

        [Test]
        public void Test17_DepthFirstTraverseOrder_BasicCheck()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            var childA = new MultiParentTreeNode();
            var childB = new MultiParentTreeNode();
            var grandChildA = new MultiParentTreeNode();

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(childA, root);
            tree.AddTreeNode(childB, root);
            tree.AddTreeNode(grandChildA, childA);

            // Act
            var dfsList = tree.DepthFirstTraverse();

            // Assert
            // The order can vary based on DFS approach (A first or B first).
            // We'll do a minimal check that all are present and root is first.
            Assert.AreEqual(root, dfsList[0], "Root should be first in the DFS list.");
            CollectionAssert.Contains(dfsList, childA);
            CollectionAssert.Contains(dfsList, childB);
            CollectionAssert.Contains(dfsList, grandChildA);
            Assert.AreEqual(4, dfsList.Count);
        }

        [Test]
        public void Test18_HeightFirstTraverseOrder_BasicCheck()
        {
            // Arrange
            var root = new MultiParentTreeNode();
            var childA = new MultiParentTreeNode();
            var childB = new MultiParentTreeNode();
            var grandChildA = new MultiParentTreeNode();
            var grandChildB = new MultiParentTreeNode();

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(childA, root);
            tree.AddTreeNode(childB, root);
            tree.AddTreeNode(grandChildA, childA);
            tree.AddTreeNode(grandChildB, childB);

            // Act
            var bfsList = tree.HeightFirstTraverse();

            // Assert
            Assert.AreEqual(root, bfsList[0], "Root should be at index 0 in BFS traversal.");
            // The second and third items should be childA and childB in some order
            Assert.IsTrue((bfsList[1] == childA && bfsList[2] == childB) ||
                          (bfsList[1] == childB && bfsList[2] == childA),
                          "Child nodes should come after root in BFS.");
            CollectionAssert.Contains(bfsList, grandChildA);
            CollectionAssert.Contains(bfsList, grandChildB);
            Assert.AreEqual(5, bfsList.Count);
        }

        [Test]
        public void Test19_AddMultipleNodesAsRoots_OnlyFirstBecomesRoot()
        {
            // Arrange
            var root1 = new MultiParentTreeNode();
            var root2 = new MultiParentTreeNode();

            // Act
            tree.AddTreeNode(root1, null);
            tree.AddTreeNode(root2, null);

            // Assert
            Assert.AreEqual(root1, tree.RootNode, "Only the first node without a parent should become the actual RootNode.");
            Assert.IsTrue(tree.Contains(root2), "Second node should still be added to the graph, but not as RootNode.");
        }

        [Test]
        public void Test20_DisconnectParentTwice_NoException()
        {
            // Arrange
            var parent = new MultiParentTreeNode();
            var child = new MultiParentTreeNode();
            tree.AddTreeNode(parent, null);
            tree.AddTreeNode(child, parent);

            // Act & Assert
            // Disconnect once
            child.DisconnectFromParent(parent);
            // Disconnect again (should not throw or break anything)
            Assert.DoesNotThrow(() => child.DisconnectFromParent(parent),
                "Calling DisconnectFromParent multiple times should not cause exceptions.");
        }
    }
}