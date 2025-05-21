using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace LegendaryTools.GraphV2.Tests
{
    [TestFixture]
    public class TreeTests
    {
        // Helper method to create a new Tree instance
        private ITree CreateTree()
        {
            return new Tree();
        }

        // Helper method to create a new TreeNode with a unique ID
        private ITreeNode CreateTreeNode(string id = null)
        {
            return new TreeNode
            {
                Id = id ?? Guid.NewGuid().ToString()
            };
        }

        [Test]
        public void AddRootNode_Succeeds()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");

            // Act
            tree.AddTreeNode(root, null);

            // Assert
            Assert.AreEqual(root, tree.RootNode, "Root node should be set correctly.");
            Assert.IsTrue(tree.Contains(root), "Tree should contain the root node.");
            Assert.AreEqual(1, tree.AllNodes.Length, "Tree should have exactly one node after adding root.");
        }

        [Test]
        public void AddChildNode_Succeeds()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(root, null);

            // Act
            tree.AddTreeNode(child, root);

            // Assert
            Assert.AreEqual(root, child.ParentNode, "Child node's parent should be the root node.");
            Assert.Contains(child, root.ChildNodes, "Root node should contain the child node.");
            Assert.IsTrue(tree.Contains(child), "Tree should contain the child node.");
            Assert.AreEqual(2, tree.AllNodes.Length, "Tree should have exactly two nodes after adding a child.");
        }

        [Test]
        public void AddMultipleChildNodes_Succeeds()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode child3 = CreateTreeNode("child3");

            tree.AddTreeNode(root, null);

            // Act
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(child3, root);

            // Assert
            CollectionAssert.Contains(root.ChildNodes, child1, "Root should contain child1.");
            CollectionAssert.Contains(root.ChildNodes, child2, "Root should contain child2.");
            CollectionAssert.Contains(root.ChildNodes, child3, "Root should contain child3.");
            Assert.AreEqual(4, tree.AllNodes.Length, "Tree should have exactly four nodes after adding three children.");
        }

        [Test]
        public void AddMultipleRoots_ThrowsException()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root1 = CreateTreeNode("root1");
            ITreeNode root2 = CreateTreeNode("root2");

            tree.AddTreeNode(root1, null);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => tree.AddTreeNode(root2, null), "Adding a second root node should throw an exception.");
            Assert.AreEqual("Root node already exists. To add additional nodes, specify a parent.", ex.Message, "Exception message should indicate that the root already exists.");
        }

        [Test]
        public void RemoveLeafNode_Succeeds()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child, root);

            // Act
            bool removed = tree.RemoveTreeNode(child, out ITreeNode[] removedNodes);

            // Assert
            Assert.IsTrue(removed, "Leaf node should be removed successfully.");
            Assert.IsFalse(tree.Contains(child), "Tree should no longer contain the removed leaf node.");
            CollectionAssert.Contains(removedNodes, child, "No additional nodes should be removed when removing a leaf node.");
            Assert.AreEqual(1, tree.AllNodes.Length, "Tree should have exactly one node after removing the leaf.");
        }

        [Test]
        public void RemoveNodeWithChildren_SucceedsAndRemovesSubtree()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode grandchild = CreateTreeNode("grandchild");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(grandchild, child1);

            // Act
            bool removed = tree.RemoveTreeNode(child1, out ITreeNode[] removedNodes);

            // Assert
            Assert.IsTrue(removed, "Node with children should be removed successfully.");
            Assert.IsFalse(tree.Contains(child1), "Tree should no longer contain the removed node.");
            Assert.IsFalse(tree.Contains(grandchild), "Tree should no longer contain the grandchild node.");
            CollectionAssert.Contains(removedNodes, child1, "Removed nodes should include child1.");
            CollectionAssert.Contains(removedNodes, grandchild, "Removed nodes should include grandchild.");
            Assert.AreEqual(2, tree.AllNodes.Length, "Tree should have exactly two nodes after removal.");
        }

        [Test]
        public void HeightCalculation_IsCorrect()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode grandchild = CreateTreeNode("grandchild");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(grandchild, child1);

            // Act
            int height = tree.Height;

            // Assert
            Assert.AreEqual(3, height, "Tree height should be correctly calculated as 3.");
        }

        [Test]
        public void WidthCalculation_IsCorrect()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode child3 = CreateTreeNode("child3");
            ITreeNode grandchild1 = CreateTreeNode("grandchild1");
            ITreeNode grandchild2 = CreateTreeNode("grandchild2");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(child3, root);
            tree.AddTreeNode(grandchild1, child1);
            tree.AddTreeNode(grandchild2, child2);

            // Act
            int width = tree.Width;

            // Assert
            Assert.AreEqual(3, width, "Tree width should be correctly calculated as 3 at the root level.");
        }

        [Test]
        public void DepthFirstSearch_FindsNode()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode grandchild = CreateTreeNode("grandchild");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(grandchild, child1);

            // Act
            ITreeNode found = tree.DepthFirstSearch(node => node.Id == "grandchild");

            // Assert
            Assert.IsNotNull(found, "Depth-first search should find the grandchild node.");
            Assert.AreEqual(grandchild, found, "The found node should be the grandchild node.");
        }

        [Test]
        public void HeightFirstSearch_FindsNode()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode grandchild = CreateTreeNode("grandchild");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(grandchild, child1);

            // Act
            ITreeNode found = tree.HeightFirstSearch(node => node.Id == "grandchild");

            // Assert
            Assert.IsNotNull(found, "Height-first search should find the grandchild node.");
            Assert.AreEqual(grandchild, found, "The found node should be the grandchild node.");
        }

        [Test]
        public void DepthFirstTraverse_ReturnsCorrectOrder()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("A");
            ITreeNode nodeB = CreateTreeNode("B");
            ITreeNode nodeC = CreateTreeNode("C");
            ITreeNode nodeD = CreateTreeNode("D");
            ITreeNode nodeE = CreateTreeNode("E");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(nodeB, root);
            tree.AddTreeNode(nodeC, root);
            tree.AddTreeNode(nodeD, nodeB);
            tree.AddTreeNode(nodeE, nodeB);

            // Act
            List<ITreeNode> traversal = tree.DepthFirstTraverse();

            // Assert
            List<string> expectedOrder = new List<string> { "A", "B", "D", "E", "C" };
            List<string> actualOrder = traversal.Select(n => n.Id).ToList();
            CollectionAssert.AreEqual(expectedOrder, actualOrder, "Depth-first traversal should follow the correct order.");
        }

        [Test]
        public void HeightFirstTraverse_ReturnsCorrectOrder()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("A");
            ITreeNode nodeB = CreateTreeNode("B");
            ITreeNode nodeC = CreateTreeNode("C");
            ITreeNode nodeD = CreateTreeNode("D");
            ITreeNode nodeE = CreateTreeNode("E");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(nodeB, root);
            tree.AddTreeNode(nodeC, root);
            tree.AddTreeNode(nodeD, nodeB);
            tree.AddTreeNode(nodeE, nodeC);

            // Act
            List<ITreeNode> traversal = tree.HeightFirstTraverse();

            // Assert
            List<string> expectedOrder = new List<string> { "A", "B", "C", "D", "E" };
            List<string> actualOrder = traversal.Select(n => n.Id).ToList();
            CollectionAssert.AreEqual(expectedOrder, actualOrder, "Height-first traversal should follow the correct order.");
        }

        [Test]
        public void AddingNodeCreatesCycle_ThrowsException()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child, root);

            // Act
            // Attempt to connect root as a child of 'child', creating a cycle
            Assert.Throws<InvalidOperationException>(() => tree.AddTreeNode(root, child), "Adding a node that creates a cycle should throw an exception.");
        }

        [Test]
        public void Tree_IsAcyclic_ReturnsTrue()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, child1);

            // Act
            bool isAcyclic = tree.IsAcyclic;

            // Assert
            Assert.IsTrue(isAcyclic, "Tree should be acyclic.");
        }

        [Test]
        public void Neighbours_ReturnsCorrectNodes()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);

            // Act
            INode[] neighboursOfRoot = root.Neighbours;
            INode[] neighboursOfChild1 = child1.Neighbours;

            // Assert
            CollectionAssert.AreEquivalent(new[] { child1, child2 }, neighboursOfRoot, "Root node should have child1 and child2 as neighbours.");
        }

        [Test]
        public void AllNodes_ContainsAllNodes()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode grandchild = CreateTreeNode("grandchild");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.AddTreeNode(grandchild, child1);

            // Act
            INode[] allNodes = tree.AllNodes;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { root, child1, child2, grandchild }, allNodes, "AllNodes should contain all nodes in the tree.");
        }

        [Test]
        public void AllNodesRecursive_ContainsAllNodes()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode parentChild = CreateTreeNode("parentChild");

            parentTree.AddTreeNode(parentRoot, null);
            parentTree.AddTreeNode(parentChild, parentRoot);

            ITree childTree = CreateTree();
            ITreeNode childRoot = CreateTreeNode("childRoot");
            ITreeNode childChild = CreateTreeNode("childChild");

            childTree.AddTreeNode(childRoot, null);
            childTree.AddTreeNode(childChild, childRoot);

            // Act
            parentTree.AddGraph(childTree);
            INode[] allNodesRecursive = parentTree.AllNodesRecursive;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { parentRoot, parentChild, childRoot, childChild }, allNodesRecursive, "AllNodesRecursive should contain all nodes from the parent and child graphs.");
        }

        [Test]
        public void Contains_ReturnsTrueForExistingNode()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");

            tree.AddTreeNode(root, null);

            // Act
            bool contains = tree.Contains(root);

            // Assert
            Assert.IsTrue(contains, "Tree should contain the existing root node.");
        }

        [Test]
        public void Contains_ReturnsFalseForNonExistingNode()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode nonExisting = CreateTreeNode("nonExisting");

            tree.AddTreeNode(root, null);

            // Act
            bool contains = tree.Contains(nonExisting);

            // Assert
            Assert.IsFalse(contains, "Tree should not contain a node that was not added.");
        }

        [Test]
        public void AddGraphAndRemoveGraph_Succeeds()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");

            ITree childTree = CreateTree();
            ITreeNode childRoot = CreateTreeNode("childRoot");
            ITreeNode childChild = CreateTreeNode("childChild");

            parentTree.AddTreeNode(parentRoot, null);
            childTree.AddTreeNode(childRoot, null);
            childTree.AddTreeNode(childChild, childRoot);

            // Act
            parentTree.AddGraph(childTree);

            // Assert after adding
            CollectionAssert.Contains(parentTree.ChildGraphs, childTree, "Parent tree should contain the child graph after adding.");
            Assert.AreEqual(3, parentTree.AllNodesRecursive.Length, "AllNodesRecursive should include nodes from both parent and child graphs.");

            // Act
            parentTree.RemoveGraph(childTree);

            // Assert after removing
            CollectionAssert.DoesNotContain(parentTree.ChildGraphs, childTree, "Parent tree should not contain the child graph after removal.");
            CollectionAssert.DoesNotContain(parentTree.GraphHierarchy, childTree, "GraphHierarchy should not include the child graph after removal.");
            Assert.AreEqual(1, parentTree.AllNodesRecursive.Length, "AllNodesRecursive should only include nodes from the parent graph after removal.");
        }
        
        [Test]
        public void AddNodeWithDuplicateId_ThrowsException()
        {
            // Arrange
            ITree tree = CreateTree();
            string duplicateId = "duplicateId";
            ITreeNode root = CreateTreeNode(duplicateId);
            ITreeNode duplicateNode = CreateTreeNode(duplicateId);

            tree.AddTreeNode(root, null);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => tree.AddTreeNode(duplicateNode, root), "Adding a node with a duplicate ID should throw an exception.");
        }

        [Test]
        public void AddNullNode_ThrowsArgumentNullException()
        {
            // Arrange
            ITree tree = CreateTree();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => tree.AddTreeNode(null, null), "Adding a null node should throw an ArgumentNullException.");
        }

        [Test]
        public void RemoveRootNode_SucceedsAndClearsTree()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            tree.AddTreeNode(root, null);

            // Act
            bool removed = tree.RemoveTreeNode(root, out ITreeNode[] removedNodes);

            // Assert
            Assert.IsTrue(removed, "Root node should be removed successfully.");
            Assert.IsNull(tree.RootNode, "Tree should have no root node after removal.");
            CollectionAssert.Contains(removedNodes, root, "Removed nodes should include the root node.");
            Assert.AreEqual(0, tree.AllNodes.Length, "Tree should be empty after removing the root node.");
        }

        [Test]
        public void SearchNonExistingNode_ReturnsNull()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            tree.AddTreeNode(root, null);

            // Act
            ITreeNode found = tree.DepthFirstSearch(node => node.Id == "nonExisting");

            // Assert
            Assert.IsNull(found, "Searching for a non-existing node should return null.");
        }

        [Test]
        public void HeightAndWidth_WhenTreeIsEmpty_ReturnsZero()
        {
            // Arrange
            ITree tree = CreateTree();

            // Act
            int height = tree.Height;
            int width = tree.Width;

            // Assert
            Assert.AreEqual(0, height, "Height of an empty tree should be zero.");
            Assert.AreEqual(0, width, "Width of an empty tree should be zero.");
        }

        [Test]
        public void MultipleAddRemoveOperations_MaintainsIntegrity()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");
            ITreeNode child3 = CreateTreeNode("child3");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);

            // Act
            tree.RemoveTreeNode(child1, out ITreeNode[] removedNodes);
            tree.AddTreeNode(child3, root);

            // Assert
            Assert.IsFalse(tree.Contains(child1), "Child1 should have been removed from the tree.");
            Assert.IsTrue(tree.Contains(child2), "Child2 should still be in the tree.");
            Assert.IsTrue(tree.Contains(child3), "Child3 should have been added to the tree.");
            Assert.AreEqual(3, tree.AllNodes.Length, "Tree should have three nodes after add/remove operations.");
        }

        [Test]
        public void ConnectToParent_WithNullParent_ThrowsException()
        {
            // Arrange
            ITreeNode node = CreateTreeNode("node");

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => node.ConnectToParent(null), "Connecting to a null parent should throw an ArgumentNullException.");
        }

        [Test]
        public void DisconnectFromParent_Succeeds()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child, root);

            // Act
            child.DisconnectFromParent();

            // Assert
            Assert.IsNull(child.ParentNode, "Child node should have no parent after disconnection.");
            CollectionAssert.DoesNotContain(root.ChildNodes, child, "Root's child nodes should not contain the disconnected child.");
            Assert.IsTrue(tree.Contains(child), "Tree should still contain the child node after disconnection.");
            Assert.AreEqual(2, tree.AllNodes.Length, "Tree should still have two nodes after disconnection.");
        }

        [Test]
        public void Tree_IsDirected_ReturnsTrue()
        {
            // Arrange
            ITree tree = CreateTree();

            // Act
            bool isDirected = tree.IsDirected;

            // Assert
            Assert.IsTrue(isDirected, "Tree should be directed.");
        }

        [Test]
        public void ParentGraph_ReturnsCorrectGraph()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childTree = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot = CreateTreeNode("childRoot");

            parentTree.AddTreeNode(parentRoot, null);
            childTree.AddTreeNode(childRoot, null);
            parentTree.AddGraph(childTree);

            // Act
            IGraph parentGraph = childTree.ParentGraph;

            // Assert
            Assert.AreEqual(parentTree, parentGraph, "Child graph's parent should be the parent tree.");
        }

        [Test]
        public void ChildGraphs_ReturnsAddedGraphs()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childTree1 = CreateTree();
            ITree childTree2 = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot1 = CreateTreeNode("childRoot1");
            ITreeNode childRoot2 = CreateTreeNode("childRoot2");

            parentTree.AddTreeNode(parentRoot, null);
            childTree1.AddTreeNode(childRoot1, null);
            childTree2.AddTreeNode(childRoot2, null);

            // Act
            parentTree.AddGraph(childTree1);
            parentTree.AddGraph(childTree2);

            // Assert
            CollectionAssert.Contains(parentTree.ChildGraphs, childTree1, "Parent tree should contain childTree1.");
            CollectionAssert.Contains(parentTree.ChildGraphs, childTree2, "Parent tree should contain childTree2.");
            Assert.AreEqual(2, parentTree.ChildGraphs.Length, "Parent tree should have two child graphs.");
        }

        [Test]
        public void GraphHierarchy_ReturnsCorrectOrder()
        {
            // Arrange
            ITree grandParentTree = CreateTree();
            ITree parentTree = CreateTree();
            ITree childTree = CreateTree();
            ITreeNode grandParentRoot = CreateTreeNode("grandParentRoot");
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot = CreateTreeNode("childRoot");

            grandParentTree.AddTreeNode(grandParentRoot, null);
            parentTree.AddTreeNode(parentRoot, null);
            childTree.AddTreeNode(childRoot, null);

            grandParentTree.AddGraph(parentTree);
            parentTree.AddGraph(childTree);

            // Act
            IGraph[] hierarchy = childTree.GraphHierarchy;

            // Assert
            CollectionAssert.AreEqual(new IGraph[] { grandParentTree, parentTree}, hierarchy, "GraphHierarchy should list parent graphs in correct order.");
        }

        [Test]
        public void AddNodeWithInvalidParent_ThrowsException()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode invalidParent = CreateTreeNode("invalidParent");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(root, null);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => tree.AddTreeNode(child, invalidParent), "Adding a node with a parent not in the tree should throw an ArgumentException.");
            Assert.AreEqual("Parent node does not exist in the tree.", ex.Message, "Exception message should indicate invalid parent.");
        }

        [Test]
        public void Traversal_OnEmptyTree_ReturnsEmptyList()
        {
            // Arrange
            ITree tree = CreateTree();

            // Act
            List<ITreeNode> depthFirst = tree.DepthFirstTraverse();
            List<ITreeNode> heightFirst = tree.HeightFirstTraverse();

            // Assert
            Assert.IsEmpty(depthFirst, "Depth-first traversal of an empty tree should return an empty list.");
            Assert.IsEmpty(heightFirst, "Height-first traversal of an empty tree should return an empty list.");
        }

        [Test]
        public void AllNodes_AfterAddingAndRemovingNodes_IsAccurate()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, root);
            tree.RemoveTreeNode(child1, out _);

            // Act
            INode[] allNodes = tree.AllNodes;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { root, child2 }, allNodes, "AllNodes should accurately reflect current nodes after additions and removals.");
        }

        [Test]
        public void AllNodesRecursive_AfterAddingChildGraphs_IsAccurate()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph1 = CreateTree();
            ITree childGraph2 = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot1 = CreateTreeNode("childRoot1");
            ITreeNode childRoot2 = CreateTreeNode("childRoot2");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph1.AddTreeNode(childRoot1, null);
            childGraph2.AddTreeNode(childRoot2, null);

            parentTree.AddGraph(childGraph1);
            parentTree.AddGraph(childGraph2);

            // Act
            INode[] allNodesRecursive = parentTree.AllNodesRecursive;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { parentRoot, childRoot1, childRoot2 }, allNodesRecursive, "AllNodesRecursive should include nodes from all child graphs.");
        }

        [Test]
        public void RemoveNonExistingNode_ReturnsFalse()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode nonExisting = CreateTreeNode("nonExisting");

            tree.AddTreeNode(root, null);

            // Act
            bool removed = tree.RemoveTreeNode(nonExisting, out ITreeNode[] removedNodes);

            // Assert
            Assert.IsFalse(removed, "Attempting to remove a non-existing node should return false.");
            CollectionAssert.IsEmpty(removedNodes, "No nodes should be removed when attempting to remove a non-existing node.");
            Assert.AreEqual(1, tree.AllNodes.Length, "Tree should still contain the root node.");
        }

        [Test]
        public void BidirectionalConnection_IsNotAllowed_InTree()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode parent = CreateTreeNode("parent");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(parent, null);
            tree.AddTreeNode(child, parent);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => child.ConnectToParent(parent), "Attempting to create a bidirectional connection should throw an exception.");
            Assert.AreEqual("Connecting to this parent would create a cycle.", ex.Message, "Exception message should indicate that a cycle is created.");
        }

        [Test]
        public void AddGraph_WithSubGraphs_Succeeds()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph = CreateTree();
            ITree subChildGraph = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot = CreateTreeNode("childRoot");
            ITreeNode subChildRoot = CreateTreeNode("subChildRoot");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph.AddTreeNode(childRoot, null);
            subChildGraph.AddTreeNode(subChildRoot, null);

            childGraph.AddGraph(subChildGraph);
            parentTree.AddGraph(childGraph);

            // Act
            IGraph[] hierarchy = subChildGraph.GraphHierarchy;

            // Assert
            CollectionAssert.AreEqual(new IGraph[] { parentTree, childGraph }, hierarchy, "GraphHierarchy should correctly include all ancestor graphs.");
            Assert.Contains(childGraph, parentTree.ChildGraphs, "Parent tree should contain childGraph.");
            Assert.Contains(subChildGraph, childGraph.ChildGraphs, "Child graph should contain subChildGraph.");
        }

        [Test]
        public void Tree_IsDirectedAcyclic_ReturnsTrue()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            ITreeNode child1 = CreateTreeNode("child1");
            ITreeNode child2 = CreateTreeNode("child2");

            tree.AddTreeNode(root, null);
            tree.AddTreeNode(child1, root);
            tree.AddTreeNode(child2, child1);

            // Act
            bool isDirectedAcyclic = tree.IsDirectedAcyclic;

            // Assert
            Assert.IsTrue(isDirectedAcyclic, "Tree should be directed acyclic.");
        }

        [Test]
        public void Tree_IsDirectedCyclic_ReturnsFalse()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");

            tree.AddTreeNode(root, null);

            // Act
            bool isDirectedCyclic = tree.IsDirectedCyclic;

            // Assert
            Assert.IsFalse(isDirectedCyclic, "Tree should not be directed cyclic.");
        }

        [Test]
        public void Tree_IsCyclic_ReturnsFalse()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");

            tree.AddTreeNode(root, null);

            // Act
            bool isCyclic = tree.IsCyclic;

            // Assert
            Assert.IsFalse(isCyclic, "Tree should not be cyclic.");
        }

        [Test]
        public void DepthFirstSearch_OnSingleNode_FindsRoot()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            tree.AddTreeNode(root, null);

            // Act
            ITreeNode found = tree.DepthFirstSearch(node => node.Id == "root");

            // Assert
            Assert.IsNotNull(found, "Depth-first search should find the root node.");
            Assert.AreEqual(root, found, "The found node should be the root node.");
        }

        [Test]
        public void HeightFirstSearch_OnSingleNode_FindsRoot()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode root = CreateTreeNode("root");
            tree.AddTreeNode(root, null);

            // Act
            ITreeNode found = tree.HeightFirstSearch(node => node.Id == "root");

            // Assert
            Assert.IsNotNull(found, "Height-first search should find the root node.");
            Assert.AreEqual(root, found, "The found node should be the root node.");
        }

        [Test]
        public void Tree_WithMultipleChildGraphs_AllNodesRecursive_AggregatesCorrectly()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph1 = CreateTree();
            ITree childGraph2 = CreateTree();
            ITree subChildGraph = CreateTree();

            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot1 = CreateTreeNode("childRoot1");
            ITreeNode childRoot2 = CreateTreeNode("childRoot2");
            ITreeNode subChildRoot = CreateTreeNode("subChildRoot");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph1.AddTreeNode(childRoot1, null);
            childGraph2.AddTreeNode(childRoot2, null);
            subChildGraph.AddTreeNode(subChildRoot, null);

            childGraph1.AddGraph(subChildGraph);
            parentTree.AddGraph(childGraph1);
            parentTree.AddGraph(childGraph2);

            // Act
            INode[] allNodesRecursive = parentTree.AllNodesRecursive;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { parentRoot, childRoot1, childRoot2, subChildRoot }, allNodesRecursive, "AllNodesRecursive should include all nodes from parent, child, and sub-child graphs.");
        }

        [Test]
        public void TreeNode_ConnectToParent_SetsCorrectConnectionDirection()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode parent = CreateTreeNode("parent");
            ITreeNode child = CreateTreeNode("child");

            tree.AddTreeNode(parent, null);

            // Act
            tree.AddTreeNode(child, parent);
            INodeConnection connection = parent.Connections.FirstOrDefault(conn => conn.ToNode == child);

            // Assert
            Assert.IsNotNull(connection, "Connection should exist between parent and child.");
            Assert.AreEqual(NodeConnectionDirection.Unidirectional, connection.Direction, "Connection direction should be unidirectional from parent to child.");
        }

        [Test]
        public void DisconnectFromParent_WhenNoParent_DoesNothing()
        {
            // Arrange
            ITree tree = CreateTree();
            ITreeNode node = CreateTreeNode("node");
            tree.AddTreeNode(node, null);

            // Act
            node.DisconnectFromParent();

            // Assert
            Assert.IsNull(node.ParentNode, "Node should have no parent.");
            CollectionAssert.IsEmpty(node.Neighbours, "Node should have no neighbours after disconnecting.");
        }

        [Test]
        public void AddGraph_WithNullGraph_ThrowsArgumentNullException()
        {
            // Arrange
            ITree tree = CreateTree();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => tree.AddGraph(null), "Adding a null graph should throw an ArgumentNullException.");
        }

        [Test]
        public void AddGraph_CreatesProperHierarchy()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot = CreateTreeNode("childRoot");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph.AddTreeNode(childRoot, null);

            // Act
            parentTree.AddGraph(childGraph);

            // Assert
            Assert.AreEqual(parentTree, childGraph.ParentGraph, "Child graph's parent should be set correctly.");
            CollectionAssert.Contains(parentTree.ChildGraphs, childGraph, "Parent tree should contain the child graph.");
        }

        [Test]
        public void RemoveGraph_RemovesHierarchyCorrectly()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot = CreateTreeNode("childRoot");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph.AddTreeNode(childRoot, null);
            parentTree.AddGraph(childGraph);

            // Act
            parentTree.RemoveGraph(childGraph);

            // Assert
            Assert.IsNull(childGraph.ParentGraph, "Child graph's parent should be null after removal.");
            CollectionAssert.DoesNotContain(parentTree.ChildGraphs, childGraph, "Parent tree should no longer contain the removed child graph.");
        }

        [Test]
        public void Tree_AfterRemovingAllGraphs_AllNodesRecursive_OnlyIncludesParentNodes()
        {
            // Arrange
            ITree parentTree = CreateTree();
            ITree childGraph1 = CreateTree();
            ITree childGraph2 = CreateTree();
            ITreeNode parentRoot = CreateTreeNode("parentRoot");
            ITreeNode childRoot1 = CreateTreeNode("childRoot1");
            ITreeNode childRoot2 = CreateTreeNode("childRoot2");

            parentTree.AddTreeNode(parentRoot, null);
            childGraph1.AddTreeNode(childRoot1, null);
            childGraph2.AddTreeNode(childRoot2, null);
            parentTree.AddGraph(childGraph1);
            parentTree.AddGraph(childGraph2);

            // Act
            parentTree.RemoveGraph(childGraph1);
            parentTree.RemoveGraph(childGraph2);
            INode[] allNodesRecursive = parentTree.AllNodesRecursive;

            // Assert
            CollectionAssert.AreEquivalent(new INode[] { parentRoot }, allNodesRecursive, "After removing all child graphs, AllNodesRecursive should only include parent nodes.");
        }
    }
}