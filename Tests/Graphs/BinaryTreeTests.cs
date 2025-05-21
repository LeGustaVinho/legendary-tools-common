using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace LegendaryTools.GraphV2.Tests
{
    [TestFixture]
    public class BinaryTreeUnitTests
    {
        private BinaryTree _binaryTree;

        [SetUp]
        public void Setup()
        {
            _binaryTree = new BinaryTree();
        }

        /// <summary>
        ///     Test adding a root node to the binary tree.
        /// </summary>
        [Test]
        public void AddRootNode_ShouldSetAsRoot()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            _binaryTree.AddTreeNode(rootNode, null);

            Assert.AreEqual(rootNode, _binaryTree.RootNode, "Root node should be set correctly.");
            Assert.IsTrue(_binaryTree.Contains(rootNode), "Binary tree should contain the root node.");
        }

        /// <summary>
        ///     Test adding a left child to the root node.
        /// </summary>
        [Test]
        public void AddLeftChild_ShouldSetAsLeftChild()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            _binaryTree.AddTreeNode(rootNode, null);
            _binaryTree.AddTreeNode(leftChild, rootNode);

            Assert.AreEqual(leftChild, ((BinaryTreeNode)_binaryTree.RootNode).Left,
                "Left child should be set correctly.");
            Assert.AreEqual(rootNode, leftChild.ParentNode, "Parent node of left child should be root node.");
        }

        /// <summary>
        ///     Test adding a right child to the root node.
        /// </summary>
        [Test]
        public void AddRightChild_ShouldSetAsRightChild()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            _binaryTree.AddTreeNode(rootNode, null);
            _binaryTree.AddTreeNode(leftChild, rootNode);

            Assert.AreEqual(leftChild, ((BinaryTreeNode)_binaryTree.RootNode).Left,
                "Right child should be set correctly.");
            Assert.AreEqual(rootNode, leftChild.ParentNode, "Parent node of right child should be root node.");
        }

        /// <summary>
        ///     Test that adding a third child to the root node throws an exception.
        /// </summary>
        [Test]
        public void AddThirdChild_ShouldThrowException()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode rightChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode thirdChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(rootNode, null);
            _binaryTree.AddTreeNode(leftChild, rootNode);
            _binaryTree.AddTreeNode(rightChild, rootNode);

            InvalidOperationException ex =
                Assert.Throws<InvalidOperationException>(() => _binaryTree.AddTreeNode(thirdChild, rootNode));
            Assert.AreEqual("Parent node already has both left and right children.", ex.Message,
                "Adding a third child should throw InvalidOperationException.");
        }

        /// <summary>
        ///     Test removing a leaf node from the binary tree.
        /// </summary>
        [Test]
        public void RemoveLeafNode_ShouldRemoveSuccessfully()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(rootNode, null);
            _binaryTree.AddTreeNode(leftChild, rootNode);

            bool removed = _binaryTree.RemoveBinaryTreeNode(leftChild, out ITreeNode[] removedNodes);
            Assert.IsTrue(removed, "Leaf node should be removed successfully.");
            Assert.IsFalse(_binaryTree.Contains(leftChild),
                "Binary tree should no longer contain the removed leaf node.");
            Assert.IsNull(rootNode.Left, "Left child reference should be null after removal.");
        }

        /// <summary>
        ///     Test removing a node with children from the binary tree.
        /// </summary>
        [Test]
        public void RemoveNodeWithChildren_ShouldRemoveSubtree()
        {
            BinaryTreeNode rootNode = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftGrandChild = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(rootNode, null);
            _binaryTree.AddTreeNode(leftChild, rootNode);
            _binaryTree.AddTreeNode(leftGrandChild, leftChild);

            bool removed = _binaryTree.RemoveBinaryTreeNode(leftChild, out ITreeNode[] removedNodes);
            Assert.IsTrue(removed, "Node with children should be removed successfully.");

            Assert.IsFalse(_binaryTree.Contains(leftChild), "Binary tree should no longer contain the removed node.");
            Assert.IsFalse(_binaryTree.Contains(leftGrandChild),
                "Binary tree should no longer contain the removed subtree.");
            Assert.IsNull(rootNode.Left, "Left child reference should be null after removal.");
        }

        /// <summary>
        ///     Test calculating the height of the binary tree.
        /// </summary>
        [Test]
        public void Height_ShouldReturnCorrectValue()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode left = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode right = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);

            Assert.AreEqual(3, _binaryTree.Height,
                "Height should correctly reflect the longest path from root to leaf.");
        }

        /// <summary>
        ///     Test calculating the width of the binary tree.
        /// </summary>
        [Test]
        public void Width_ShouldReturnCorrectValue()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode left = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode right = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            Assert.AreEqual(2, _binaryTree.Width,
                "Width should correctly reflect the maximum number of nodes at any level.");
        }

        /// <summary>
        ///     Test depth-first traversal of the binary tree.
        /// </summary>
        [Test]
        public void DepthFirstTraverse_ShouldReturnCorrectOrder()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            List<string> traversal = _binaryTree.DepthFirstTraverse().Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "D", "E", "C" };

            Assert.AreEqual(expected, traversal, "Depth-first traversal should return nodes in the correct order.");
        }

        /// <summary>
        ///     Test breadth-first (height-first) traversal of the binary tree.
        /// </summary>
        [Test]
        public void HeightFirstTraverse_ShouldReturnCorrectOrder()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            List<string> traversal = _binaryTree.HeightFirstTraverse().Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "C", "D", "E" };

            Assert.AreEqual(expected, traversal, "Breadth-first traversal should return nodes in the correct order.");
        }

        /// <summary>
        ///     Test depth-first search for an existing node.
        /// </summary>
        [Test]
        public void DepthFirstSearch_NodeExists_ShouldReturnNode()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode target = new BinaryTreeNode { Id = "E" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(target, left);

            ITreeNode foundNode = _binaryTree.DepthFirstSearch(n => n.Id == "E");

            Assert.IsNotNull(foundNode, "Depth-first search should find the existing node.");
            Assert.AreEqual("E", foundNode.Id, "Depth-first search should return the correct node.");
        }

        /// <summary>
        ///     Test depth-first search for a non-existing node.
        /// </summary>
        [Test]
        public void DepthFirstSearch_NodeDoesNotExist_ShouldReturnNull()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);

            ITreeNode foundNode = _binaryTree.DepthFirstSearch(n => n.Id == "Z");

            Assert.IsNull(foundNode, "Depth-first search should return null for non-existing node.");
        }

        /// <summary>
        ///     Test breadth-first search for an existing node.
        /// </summary>
        [Test]
        public void HeightFirstSearch_NodeExists_ShouldReturnNode()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode target = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode rightLeft = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(rightLeft, right);

            ITreeNode foundNode = _binaryTree.HeightFirstSearch(n => n.Id == "C");

            Assert.IsNotNull(foundNode, "Breadth-first search should find the existing node.");
            Assert.AreEqual("C", foundNode.Id, "Breadth-first search should return the correct node.");
        }

        /// <summary>
        ///     Test breadth-first search for a non-existing node.
        /// </summary>
        [Test]
        public void HeightFirstSearch_NodeDoesNotExist_ShouldReturnNull()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);

            ITreeNode foundNode = _binaryTree.HeightFirstSearch(n => n.Id == "Z");

            Assert.IsNull(foundNode, "Breadth-first search should return null for non-existing node.");
        }

        /// <summary>
        ///     Test binary search for an existing node.
        /// </summary>
        [Test]
        public void BinarySearch_NodeExists_ShouldReturnNode()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode target = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode rightRight = new BinaryTreeNode { Id = "D" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(rightRight, right);

            IBinaryTreeNode foundNode = _binaryTree.BinarySearch(n => n.Id == "D");

            Assert.IsNotNull(foundNode, "Binary search should find the existing node.");
            Assert.AreEqual("D", foundNode.Id, "Binary search should return the correct node.");
        }

        /// <summary>
        ///     Test binary search for a non-existing node.
        /// </summary>
        [Test]
        public void BinarySearch_NodeDoesNotExist_ShouldReturnNull()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);

            IBinaryTreeNode foundNode = _binaryTree.BinarySearch(n => n.Id == "Z");

            Assert.IsNull(foundNode, "Binary search should return null for non-existing node.");
        }

        /// <summary>
        ///     Test that the binary tree remains acyclic after additions.
        /// </summary>
        [Test]
        public void IsAcyclic_ShouldReturnTrue()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "B" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            Assert.IsTrue(_binaryTree.IsAcyclic, "Binary tree should be acyclic.");
        }

        /// <summary>
        ///     Test that the binary tree is directed.
        /// </summary>
        [Test]
        public void IsDirected_ShouldReturnTrue()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "B" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            Assert.IsTrue(_binaryTree.IsDirected, "Binary tree should be directed.");
        }

        /// <summary>
        ///     Test that the binary tree does not contain a specific node.
        /// </summary>
        [Test]
        public void Contains_NodeDoesNotExist_ShouldReturnFalse()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode node = new BinaryTreeNode { Id = "B" };

            _binaryTree.AddTreeNode(root, null);

            Assert.IsFalse(_binaryTree.Contains(node), "Binary tree should not contain a node that wasn't added.");
        }

        /// <summary>
        ///     Test the AllNodes property returns all nodes in the tree.
        /// </summary>
        [Test]
        public void AllNodes_ShouldReturnAllNodes()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);

            List<string> allNodes = _binaryTree.AllNodes.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "C", "D" };

            CollectionAssert.AreEquivalent(expected, allNodes, "AllNodes should return all nodes in the tree.");
        }

        /// <summary>
        ///     Test the AllNodesRecursive property returns all nodes recursively in the tree.
        /// </summary>
        [Test]
        public void AllNodesRecursive_ShouldReturnAllNodesRecursively()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            List<string> allNodesRecursive = _binaryTree.AllNodesRecursive.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "C", "D", "E" };

            CollectionAssert.AreEquivalent(expected, allNodesRecursive,
                "AllNodesRecursive should return all nodes in the tree recursively.");
        }

        /// <summary>
        ///     Test adding a node that would create a cycle throws an exception.
        /// </summary>
        [Test]
        public void AddNode_CreatingCycle_ShouldThrowException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode grandChild = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);
            _binaryTree.AddTreeNode(grandChild, child);

            // Attempt to connect root as a child of grandChild, creating a cycle
            Assert.Throws<InvalidOperationException>(() => _binaryTree.AddTreeNode(root, grandChild));
        }

        /// <summary>
        ///     Test that the binary tree maintains its properties after multiple additions and removals.
        /// </summary>
        [Test]
        public void MultipleAdditionsAndRemovals_ShouldMaintainProperties()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };
            BinaryTreeNode rightLeft = new BinaryTreeNode { Id = "F" };

            // Add nodes
            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);
            _binaryTree.AddTreeNode(rightLeft, right);

            // Remove a node
            bool removed = _binaryTree.RemoveBinaryTreeNode(leftRight, out ITreeNode[] removedNodes);
            Assert.IsTrue(removed, "Node E should be removed successfully.");
            Assert.IsFalse(_binaryTree.Contains(leftRight), "Binary tree should no longer contain node E.");

            // Check tree properties
            Assert.AreEqual(3, _binaryTree.Height, "Height should remain correct after removal.");
            Assert.AreEqual(2, _binaryTree.Width, "Width should remain correct after removal.");

            // Verify remaining nodes
            List<string> expectedIds = new List<string> { "A", "B", "C", "D", "F" };
            List<string> allNodes = _binaryTree.AllNodes.Select(n => n.Id).ToList();
            CollectionAssert.AreEquivalent(expectedIds, allNodes,
                "Binary tree should contain the correct remaining nodes.");
        }

        /// <summary>
        ///     Test that removing the root node clears the tree.
        /// </summary>
        [Test]
        public void RemoveRootNode_ShouldClearTree()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);

            bool removed = _binaryTree.RemoveBinaryTreeNode(root, out ITreeNode[] removedNodes);
            Assert.IsTrue(removed, "Root node should be removed successfully.");
            Assert.IsNull(_binaryTree.RootNode, "Root node should be null after removal.");
            Assert.IsFalse(_binaryTree.Contains(root), "Binary tree should no longer contain the root node.");
            Assert.IsFalse(_binaryTree.Contains(left),
                "Binary tree should no longer contain child nodes after root removal.");
        }

        /// <summary>
        ///     Test that adding a null node throws an exception.
        /// </summary>
        [Test]
        public void AddNullNode_ShouldThrowArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => _binaryTree.AddTreeNode(null, null));
            Assert.AreEqual("newNode", ex.ParamName, "Adding a null node should throw ArgumentNullException.");
        }

        /// <summary>
        ///     Test that adding a node with a non-existent parent throws an exception.
        /// </summary>
        [Test]
        public void AddNode_WithNonExistentParent_ShouldThrowException()
        {
            BinaryTreeNode node = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode parent = new BinaryTreeNode { Id = "B" };

            ArgumentException ex = Assert.Throws<ArgumentException>(() => _binaryTree.AddTreeNode(node, parent));
            Assert.AreEqual("Parent node does not exist in the Binary Tree.", ex.Message,
                "Adding a node with a non-existent parent should throw ArgumentException.");
        }

        /// <summary>
        ///     Test that the Neighbours property of a node returns correct neighbors.
        /// </summary>
        [Test]
        public void Neighbours_ShouldReturnCorrectNeighbors()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);

            List<string> neighbors = left.Neighbours.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A" };

            CollectionAssert.AreEquivalent(expected, neighbors,
                "Neighbours should return only the parent node for a left child.");
        }

        /// <summary>
        ///     Test that attempting to connect a node to itself throws an exception.
        /// </summary>
        [Test]
        public void ConnectNodeToItself_ShouldThrowException()
        {
            BinaryTreeNode node = new BinaryTreeNode { Id = "A" };

            _binaryTree.AddTreeNode(node, null);

            InvalidOperationException ex =
                Assert.Throws<InvalidOperationException>(() => _binaryTree.AddTreeNode(node, node));
        }

        /// <summary>
        ///     Test that adding a duplicate node throws an exception.
        /// </summary>
        [Test]
        public void AddDuplicateNode_ShouldThrowException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };

            _binaryTree.AddTreeNode(root, null);

            Assert.Throws<InvalidOperationException>(() => _binaryTree.AddTreeNode(root, null));
        }

        /// <summary>
        ///     Test that the binary tree correctly updates when multiple nodes are added and removed.
        /// </summary>
        [Test]
        public void AddAndRemoveMultipleNodes_ShouldMaintainCorrectStructure()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode b = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode c = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode d = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode e = new BinaryTreeNode { Id = "E" };
            BinaryTreeNode f = new BinaryTreeNode { Id = "F" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(b, root);
            _binaryTree.AddTreeNode(c, root);
            _binaryTree.AddTreeNode(d, b);
            _binaryTree.AddTreeNode(e, b);
            _binaryTree.AddTreeNode(f, c);

            // Remove node B
            bool removed = _binaryTree.RemoveBinaryTreeNode(b, out ITreeNode[] removedNodes);
            Assert.IsTrue(removed, "Node B should be removed successfully.");
            Assert.IsFalse(_binaryTree.Contains(b), "Binary tree should no longer contain node B.");
            Assert.IsFalse(_binaryTree.Contains(d), "Binary tree should no longer contain node D.");
            Assert.IsFalse(_binaryTree.Contains(e), "Binary tree should no longer contain node E.");

            // Verify remaining structure
            Assert.AreEqual(1, _binaryTree.RootNode.ChildNodes.Count,
                "Root should have correct number of children after removal.");
            Assert.IsNull(((BinaryTreeNode)_binaryTree.RootNode).Left, "Left child should be null after removal.");
            Assert.AreEqual(c, ((BinaryTreeNode)_binaryTree.RootNode).Right, "Right child should remain unchanged.");
            Assert.IsTrue(_binaryTree.Contains(c), "Binary tree should still contain node C.");
            Assert.IsTrue(_binaryTree.Contains(f), "Binary tree should still contain node F.");
        }

        /// <summary>
        ///     Test that the binary search works correctly in a larger tree.
        /// </summary>
        [Test]
        public void BinarySearch_LargeTree_ShouldFindCorrectNode()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            _binaryTree.AddTreeNode(root, null);

            // Add multiple levels
            List<BinaryTreeNode> currentLevel = new List<BinaryTreeNode> { root };
            for (int i = 1; i <= 5; i++)
            {
                List<BinaryTreeNode> nextLevel = new List<BinaryTreeNode>();
                int widthIndex = 1;
                foreach (BinaryTreeNode node in currentLevel)
                {
                    BinaryTreeNode left = new BinaryTreeNode { Id = $"L{i}{widthIndex}" };
                    widthIndex++;
                    BinaryTreeNode right = new BinaryTreeNode { Id = $"R{i}{widthIndex}" };
                    widthIndex++;
                    _binaryTree.AddTreeNode(left, node);
                    _binaryTree.AddTreeNode(right, node);
                    nextLevel.Add(left);
                    nextLevel.Add(right);
                }

                currentLevel = nextLevel;
            }

            // Search for a specific node
            string target = "L31";
            IBinaryTreeNode foundNode = _binaryTree.BinarySearch(n => n.Id == target);

            Assert.IsNotNull(foundNode, $"Binary search should find node with Id '{target}'.");
            Assert.AreEqual(target, foundNode.Id, "Binary search should return the correct node.");
        }

        /// <summary>
        ///     Test that attempting to remove a node not in the tree returns false.
        /// </summary>
        [Test]
        public void RemoveNonExistentNode_ShouldReturnFalse()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode node = new BinaryTreeNode { Id = "B" };

            _binaryTree.AddTreeNode(root, null);

            bool removed = _binaryTree.RemoveBinaryTreeNode(node, out ITreeNode[] removedNodes);
            Assert.IsFalse(removed, "Attempting to remove a non-existent node should return false.");
        }

        /// <summary>
        ///     Test that the binary tree is empty upon initialization.
        /// </summary>
        [Test]
        public void BinaryTree_Initialization_ShouldBeEmpty()
        {
            Assert.IsNull(_binaryTree.RootNode, "Binary tree should have no root node upon initialization.");
            Assert.AreEqual(0, _binaryTree.Height, "Height should be 0 for an empty binary tree.");
            Assert.AreEqual(0, _binaryTree.Width, "Width should be 0 for an empty binary tree.");
            Assert.IsTrue(_binaryTree.IsDirected, "Empty binary tree should be directed.");
            Assert.IsTrue(_binaryTree.IsAcyclic, "Empty binary tree should be acyclic.");
        }

        /// <summary>
        ///     Test that adding multiple root nodes throws an exception.
        /// </summary>
        [Test]
        public void AddMultipleRootNodes_ShouldThrowException()
        {
            BinaryTreeNode firstRoot = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };
            BinaryTreeNode secondRoot = new BinaryTreeNode { Id = Guid.NewGuid().ToString() };

            _binaryTree.AddTreeNode(firstRoot, null);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _binaryTree.AddTreeNode(secondRoot, null),
                "Adding a second root node should throw an InvalidOperationException.");
            Assert.AreEqual("Binary Tree already has a root.", ex.Message,
                "Exception message should indicate that the root already exists.");
        }

        /// <summary>
        ///     Test that node connections have the correct direction.
        /// </summary>
        [Test]
        public void NodeConnections_ShouldHaveCorrectDirection()
        {
            BinaryTreeNode parent = new BinaryTreeNode { Id = "Parent" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };

            _binaryTree.AddTreeNode(parent, null);
            _binaryTree.AddTreeNode(child, parent);

            INodeConnection connection = parent.Connections.FirstOrDefault(conn => conn.ToNode == child);
            Assert.IsNotNull(connection, "Connection from parent to child should exist.");
            Assert.AreEqual(NodeConnectionDirection.Unidirectional, connection.Direction,
                "Connection should be unidirectional.");
            Assert.AreEqual(parent, connection.FromNode, "Connection's FromNode should be the parent.");
            Assert.AreEqual(child, connection.ToNode, "Connection's ToNode should be the child.");
        }

        /// <summary>
        ///     Test that removing a connection from a node works correctly.
        /// </summary>
        [Test]
        public void RemoveConnection_ShouldUpdateNeighbours()
        {
            BinaryTreeNode parent = new BinaryTreeNode { Id = "Parent" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };

            _binaryTree.AddTreeNode(parent, null);
            _binaryTree.AddTreeNode(child, parent);

            // Remove the connection by disconnecting the child from the parent
            child.DisconnectFromParent();

            Assert.IsNull(child.ParentNode, "Child's ParentNode should be null after disconnection.");
            Assert.IsFalse(parent.Connections.Any(conn => conn.ToNode == child),
                "Parent should no longer have a connection to the child.");
            Assert.IsFalse(child.Connections.Any(conn => conn.FromNode == parent),
                "Child should no longer have inbound connections from the parent.");
        }

        /// <summary>
        ///     Test that all children of a node have the correct parent reference.
        /// </summary>
        [Test]
        public void Children_ShouldHaveCorrectParentReferences()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);

            Assert.AreEqual(root, child1.ParentNode, "Child1 should have Root as its parent.");
            Assert.AreEqual(root, child2.ParentNode, "Child2 should have Root as its parent.");
        }

        /// <summary>
        ///     Test that Height and Width are updated correctly after adding and removing nodes.
        /// </summary>
        [Test]
        public void HeightAndWidth_AfterAddRemoveNodes_ShouldUpdateCorrectly()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };
            BinaryTreeNode grandChild = new BinaryTreeNode { Id = "GrandChild" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);
            _binaryTree.AddTreeNode(grandChild, child1);

            Assert.AreEqual(3, _binaryTree.Height, "Height should be 3 after adding grandchild.");
            Assert.AreEqual(2, _binaryTree.Width, "Width should be 2 at the root level.");

            // Remove grandChild
            _binaryTree.RemoveBinaryTreeNode(grandChild, out _);
            Assert.AreEqual(2, _binaryTree.Height, "Height should be 2 after removing grandchild.");
            Assert.AreEqual(2, _binaryTree.Width, "Width should remain 2 after removal.");
        }

        /// <summary>
        ///     Test that AllNodes returns only nodes in the current graph, excluding child graphs.
        /// </summary>
        [Test]
        public void AllNodes_ShouldExcludeNodesInChildGraphs()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            BinaryTreeNode externalNode = new BinaryTreeNode { Id = "External" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            BinaryTree childGraph = new BinaryTree();
            childGraph.AddTreeNode(externalNode, null);

            _binaryTree.AddGraph(childGraph);

            List<string> allNodes = _binaryTree.AllNodes.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "Root", "Child" };

            CollectionAssert.AreEquivalent(expected, allNodes,
                "AllNodes should exclude nodes from child graphs.");
        }

        /// <summary>
        ///     Test that AllNodesRecursive includes nodes from child graphs.
        /// </summary>
        [Test]
        public void AllNodesRecursive_ShouldIncludeNodesInChildGraphs()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            BinaryTreeNode externalNode1 = new BinaryTreeNode { Id = "External1" };
            BinaryTreeNode externalNode2 = new BinaryTreeNode { Id = "External2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            BinaryTree childGraph = new BinaryTree();
            childGraph.AddTreeNode(externalNode1, null);
            childGraph.AddTreeNode(externalNode2, externalNode1);

            _binaryTree.AddGraph(childGraph);

            List<string> allNodesRecursive = _binaryTree.AllNodesRecursive.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "Root", "Child", "External1", "External2" };

            CollectionAssert.AreEquivalent(expected, allNodesRecursive,
                "AllNodesRecursive should include nodes from child graphs.");
        }

        /// <summary>
        ///     Test that ParentGraph property is correctly set when adding a child graph.
        /// </summary>
        [Test]
        public void ParentGraph_ShouldBeSetCorrectly_AfterAddingChildGraph()
        {
            BinaryTree parentGraph = new BinaryTree();
            BinaryTree childGraph = new BinaryTree();

            parentGraph.AddGraph(childGraph);

            Assert.AreEqual(parentGraph, childGraph.ParentGraph,
                "Child graph's ParentGraph should be set to the parent graph.");
            Assert.Contains(childGraph, parentGraph.ChildGraphs,
                "Parent graph's ChildGraphs should include the added child graph.");
        }

        /// <summary>
        ///     Test that GraphHierarchy returns the correct hierarchy of graphs.
        /// </summary>
        [Test]
        public void GraphHierarchy_ShouldReturnCorrectHierarchy()
        {
            BinaryTree rootGraph = new BinaryTree();
            BinaryTree childGraph1 = new BinaryTree();
            BinaryTree childGraph2 = new BinaryTree();
            BinaryTree grandChildGraph = new BinaryTree();

            rootGraph.AddGraph(childGraph1);
            rootGraph.AddGraph(childGraph2);
            childGraph1.AddGraph(grandChildGraph);

            IGraph[] hierarchy = grandChildGraph.GraphHierarchy;

            List<IGraph> expected = new List<IGraph> { rootGraph, childGraph1};

            CollectionAssert.AreEqual(expected, hierarchy,
                "GraphHierarchy should return the correct hierarchy from grandChildGraph up to the rootGraph.");
        }

        /// <summary>
        ///     Test that adding a child graph that is already a child throws an exception.
        /// </summary>
        [Test]
        public void AddDuplicateChildGraph_ShouldThrowException()
        {
            BinaryTree parentGraph = new BinaryTree();
            BinaryTree childGraph = new BinaryTree();

            parentGraph.AddGraph(childGraph);

            Assert.Throws<InvalidOperationException>(
                () => parentGraph.AddGraph(childGraph),
                "Adding a duplicate child graph should throw an InvalidOperationException.");
        }

        /// <summary>
        ///     Test that removing a child graph updates ParentGraph correctly.
        /// </summary>
        [Test]
        public void RemoveChildGraph_ShouldUpdateParentGraph()
        {
            BinaryTree parentGraph = new BinaryTree();
            BinaryTree childGraph = new BinaryTree();

            parentGraph.AddGraph(childGraph);
            parentGraph.RemoveGraph(childGraph);

            Assert.IsNull(childGraph.ParentGraph, "Child graph's ParentGraph should be null after removal.");
            Assert.IsFalse(parentGraph.ChildGraphs.Contains(childGraph),
                "Parent graph's ChildGraphs should not contain the removed child graph.");
        }

        /// <summary>
        ///     Test that traversal methods return empty lists when the tree is empty.
        /// </summary>
        [Test]
        public void Traversals_OnEmptyTree_ShouldReturnEmptyLists()
        {
            List<ITreeNode> depthFirst = _binaryTree.DepthFirstTraverse();
            List<ITreeNode> heightFirst = _binaryTree.HeightFirstTraverse();

            Assert.IsEmpty(depthFirst, "Depth-first traversal should return an empty list for an empty tree.");
            Assert.IsEmpty(heightFirst, "Breadth-first traversal should return an empty list for an empty tree.");
        }

        /// <summary>
        ///     Test that DepthFirstTraverse returns nodes in pre-order.
        /// </summary>
        [Test]
        public void DepthFirstTraverse_ShouldReturnPreOrderTraversal()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            List<string> traversal = _binaryTree.DepthFirstTraverse().Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "D", "E", "C" };

            Assert.AreEqual(expected, traversal, "Depth-first traversal should follow pre-order sequence.");
        }

        /// <summary>
        ///     Test that HeightFirstTraverse returns nodes in level-order.
        /// </summary>
        [Test]
        public void HeightFirstTraverse_ShouldReturnLevelOrderTraversal()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode leftLeft = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode leftRight = new BinaryTreeNode { Id = "E" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);
            _binaryTree.AddTreeNode(leftLeft, left);
            _binaryTree.AddTreeNode(leftRight, left);

            List<string> traversal = _binaryTree.HeightFirstTraverse().Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "A", "B", "C", "D", "E" };

            Assert.AreEqual(expected, traversal, "Breadth-first traversal should follow level-order sequence.");
        }

        /// <summary>
        ///     Test that Neighbours property of the root node includes only its children.
        /// </summary>
        [Test]
        public void Neighbours_RootNode_ShouldIncludeOnlyChildren()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode left = new BinaryTreeNode { Id = "Left" };
            BinaryTreeNode right = new BinaryTreeNode { Id = "Right" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(left, root);
            _binaryTree.AddTreeNode(right, root);

            INode[] neighbors = root.Neighbours;
            List<string> neighborIds = neighbors.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "Left", "Right" };

            CollectionAssert.AreEquivalent(expected, neighborIds,
                "Root node's Neighbours should include only its children.");
        }

        /// <summary>
        ///     Test that Neighbours property of an internal node includes parent and children.
        /// </summary>
        [Test]
        public void Neighbours_InternalNode_ShouldIncludeParentAndChildren()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode parent = new BinaryTreeNode { Id = "Parent" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(parent, root);
            _binaryTree.AddTreeNode(child1, parent);
            _binaryTree.AddTreeNode(child2, parent);

            INode[] neighbors = parent.Neighbours;
            List<string> neighborIds = neighbors.Select(n => n.Id).ToList();
            List<string> expected = new List<string> { "Root", "Child1", "Child2" };

            CollectionAssert.AreEquivalent(expected, neighborIds,
                "Internal node's Neighbours should include parent and children.");
        }

        /// <summary>
        ///     Test that attempting to disconnect a node updates both parent and child references correctly.
        /// </summary>
        [Test]
        public void DisconnectFromParent_ShouldUpdateReferences()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            child.DisconnectFromParent();

            Assert.IsNull(child.ParentNode, "Child's ParentNode should be null after disconnection.");
            Assert.IsFalse(root.ChildNodes.Contains(child),
                "Root's ChildNodes should not contain the child after disconnection.");
            Assert.IsEmpty(root.Connections, "Root should have no connections after child is disconnected.");
        }

        /// <summary>
        ///     Test that attempting to add a node to multiple parents is not allowed.
        /// </summary>
        [Test]
        public void AddNode_ToMultipleParents_ShouldThrowException()
        {
            BinaryTreeNode root1 = new BinaryTreeNode { Id = "Root1" };
            BinaryTreeNode root2 = new BinaryTreeNode { Id = "Root2" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    _binaryTree.AddTreeNode(root1, null);
                    _binaryTree.AddTreeNode(root2, null);
                    _binaryTree.AddTreeNode(child, root1);
                    _binaryTree.AddTreeNode(child, root2);
                },
                "Adding a node to a second parent should throw an InvalidOperationException.");
        }

        /// <summary>
        ///     Test that attempting to connect a node as both left and right child throws an exception.
        /// </summary>
        [Test]
        public void ConnectAsLeftAndRightChild_ShouldThrowException()
        {
            BinaryTreeNode parent = new BinaryTreeNode { Id = "Parent" };
            BinaryTreeNode leftChild = new BinaryTreeNode { Id = "LeftChild" };
            BinaryTreeNode rightChild = new BinaryTreeNode { Id = "RightChild" };

            _binaryTree.AddTreeNode(parent, null);
            _binaryTree.AddTreeNode(leftChild, parent);
            _binaryTree.AddTreeNode(rightChild, parent);

            // Attempt to add another left child
            BinaryTreeNode anotherLeft = new BinaryTreeNode { Id = "AnotherLeft" };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _binaryTree.AddTreeNode(anotherLeft, parent),
                "Adding a third child should throw an InvalidOperationException.");
            Assert.AreEqual("Parent node already has both left and right children.", ex.Message,
                "Exception message should indicate that the parent already has both children.");
        }

        /// <summary>
        ///     Test that the tree maintains correct parent references after multiple disconnections and reconnections.
        /// </summary>
        [Test]
        public void ReconnectNodes_ShouldMaintainCorrectParentReferences()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);

            // Disconnect child1 and reconnect to child2
            _binaryTree.RemoveBinaryTreeNode(child1, out ITreeNode[] removedNodes);
            _binaryTree.AddTreeNode(child1, child2);

            Assert.AreEqual(child2, child1.ParentNode, "Child1's new parent should be Child2.");
            Assert.IsFalse(root.ChildNodes.Contains(child1), "Root should no longer have Child1 as a child.");
            Assert.IsTrue(child2.ChildNodes.Contains(child1), "Child2 should now have Child1 as a child.");
        }

        /// <summary>
        ///     Test that the tree does not allow adding null as a child node.
        /// </summary>
        [Test]
        public void AddNullChild_ShouldThrowArgumentNullException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            _binaryTree.AddTreeNode(root, null);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => _binaryTree.AddTreeNode(null, root),
                "Adding a null child node should throw an ArgumentNullException.");
            Assert.AreEqual("newNode", ex.ParamName, "Exception should indicate that 'newNode' parameter is null.");
        }

        /// <summary>
        ///     Test that attempting to disconnect a node without a parent does not throw an exception.
        /// </summary>
        [Test]
        public void DisconnectWithoutParent_ShouldNotThrowException()
        {
            BinaryTreeNode node = new BinaryTreeNode { Id = "Orphan" };

            Assert.DoesNotThrow(() => node.DisconnectFromParent(),
                "Disconnecting a node without a parent should not throw an exception.");
            Assert.IsNull(node.ParentNode, "Node's ParentNode should remain null after disconnection.");
        }

        /// <summary>
        ///     Test that removing a child graph removes all its nodes from AllNodesRecursive.
        /// </summary>
        [Test]
        public void RemoveChildGraph_ShouldRemoveItsNodesFromAllNodesRecursive()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            BinaryTreeNode externalNode = new BinaryTreeNode { Id = "External" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            BinaryTree childGraph = new BinaryTree();
            childGraph.AddTreeNode(externalNode, null);
            _binaryTree.AddGraph(childGraph);

            // Verify AllNodesRecursive includes externalNode
            List<string> allNodesRecursiveBefore = _binaryTree.AllNodesRecursive.Select(n => n.Id).ToList();
            CollectionAssert.Contains(allNodesRecursiveBefore, "External",
                "AllNodesRecursive should include External node.");

            // Remove child graph
            _binaryTree.RemoveGraph(childGraph);

            // Verify AllNodesRecursive no longer includes externalNode
            List<string> allNodesRecursiveAfter = _binaryTree.AllNodesRecursive.Select(n => n.Id).ToList();
            CollectionAssert.DoesNotContain(allNodesRecursiveAfter, "External",
                "AllNodesRecursive should exclude External node after removal.");
        }

        /// <summary>
        ///     Test that GraphHierarchy returns an empty array for a root graph with no parents.
        /// </summary>
        [Test]
        public void GraphHierarchy_RootGraph_ShouldReturnEmptyArray()
        {
            BinaryTree rootGraph = new BinaryTree();

            IGraph[] hierarchy = rootGraph.GraphHierarchy;

            Assert.IsEmpty(hierarchy, "GraphHierarchy should return an empty array for the root graph.");
        }

        /// <summary>
        ///     Test that adding a graph as a child to itself throws an exception.
        /// </summary>
        [Test]
        public void AddGraph_AsChildOfItself_ShouldThrowException()
        {
            BinaryTree graph = new BinaryTree();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => graph.AddGraph(graph),
                "Adding a graph as a child of itself should throw an InvalidOperationException.");
            Assert.AreEqual("A graph cannot be a child of itself.", ex.Message,
                "Exception message should indicate that a graph cannot be its own child.");
        }

        /// <summary>
        ///     Test that traversals return correct nodes after complex modifications.
        /// </summary>
        [Test]
        public void Traversals_AfterComplexModifications_ShouldReturnCorrectNodes()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode b = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode c = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode d = new BinaryTreeNode { Id = "D" };
            BinaryTreeNode e = new BinaryTreeNode { Id = "E" };
            BinaryTreeNode f = new BinaryTreeNode { Id = "F" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(b, root);
            _binaryTree.AddTreeNode(c, root);
            _binaryTree.AddTreeNode(d, b);
            _binaryTree.AddTreeNode(e, b);
            _binaryTree.AddTreeNode(f, c);

            // Remove node B and its subtree
            _binaryTree.RemoveBinaryTreeNode(b, out _);

            // Expected traversal after removal: A, C, F
            List<string> depthFirst = _binaryTree.DepthFirstTraverse().Select(n => n.Id).ToList();
            List<string> expectedDepthFirst = new List<string> { "A", "C", "F" };
            CollectionAssert.AreEqual(expectedDepthFirst, depthFirst,
                "Depth-first traversal should reflect removed subtree.");

            List<string> heightFirst = _binaryTree.HeightFirstTraverse().Select(n => n.Id).ToList();
            List<string> expectedHeightFirst = new List<string> { "A", "C", "F" };
            CollectionAssert.AreEqual(expectedHeightFirst, heightFirst,
                "Breadth-first traversal should reflect removed subtree.");
        }

        /// <summary>
        ///     Test that the tree remains directed after multiple additions and removals.
        /// </summary>
        [Test]
        public void IsDirected_AfterMultipleOperations_ShouldRemainDirected()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };
            BinaryTreeNode grandChild = new BinaryTreeNode { Id = "GrandChild" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);
            _binaryTree.AddTreeNode(grandChild, child1);

            Assert.IsTrue(_binaryTree.IsDirected, "Binary tree should remain directed after multiple additions.");

            // Remove a node and check direction
            _binaryTree.RemoveBinaryTreeNode(child1, out _);
            Assert.IsTrue(_binaryTree.IsDirected, "Binary tree should remain directed after node removal.");
        }

        /// <summary>
        ///     Test that the tree remains acyclic after multiple operations.
        /// </summary>
        [Test]
        public void IsAcyclic_AfterMultipleOperations_ShouldRemainAcyclic()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };
            BinaryTreeNode grandChild = new BinaryTreeNode { Id = "GrandChild" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);
            _binaryTree.AddTreeNode(grandChild, child1);

            Assert.IsTrue(_binaryTree.IsAcyclic, "Binary tree should remain acyclic after multiple additions.");

            // Remove a node and check acyclicity
            _binaryTree.RemoveBinaryTreeNode(child1, out _);
            Assert.IsTrue(_binaryTree.IsAcyclic, "Binary tree should remain acyclic after node removal.");
        }

        /// <summary>
        ///     Test that attempting to add a node to a non-binary parent throws an exception.
        /// </summary>
        [Test]
        public void AddNode_ToNonBinaryParent_ShouldThrowException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };
            BinaryTreeNode child3 = new BinaryTreeNode { Id = "Child3" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, root);

            // Attempt to add a third child, which should fail
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _binaryTree.AddTreeNode(child3, root),
                "Adding a third child to a binary parent should throw an InvalidOperationException.");
            Assert.AreEqual("Parent node already has both left and right children.", ex.Message,
                "Exception message should indicate that the parent has both children.");
        }

        /// <summary>
        ///     Test that traversals work correctly after reconnecting nodes.
        /// </summary>
        [Test]
        public void Traversals_AfterReconnectingNodes_ShouldReturnUpdatedOrder()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "A" };
            BinaryTreeNode b = new BinaryTreeNode { Id = "B" };
            BinaryTreeNode c = new BinaryTreeNode { Id = "C" };
            BinaryTreeNode d = new BinaryTreeNode { Id = "D" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(b, root);
            _binaryTree.AddTreeNode(c, b);
            _binaryTree.AddTreeNode(d, c);

            
            _binaryTree.RemoveBinaryTreeNode(d, out ITreeNode[] removed);
            // Reconnect D to root
            _binaryTree.AddTreeNode(d, root);

            // Expected Depth-first: A, B, C, D
            List<string> depthFirst = _binaryTree.DepthFirstTraverse().Select(n => n.Id).ToList();
            List<string> expectedDepthFirst = new List<string> { "A", "B", "C", "D" };
            CollectionAssert.AreEqual(expectedDepthFirst, depthFirst,
                "Depth-first traversal should reflect node reconnection.");

            // Expected Breadth-first: A, B, D, C
            List<string> heightFirst = _binaryTree.HeightFirstTraverse().Select(n => n.Id).ToList();
            List<string> expectedHeightFirst = new List<string> { "A", "B", "D", "C" };
            CollectionAssert.AreEqual(expectedHeightFirst, heightFirst,
                "Breadth-first traversal should reflect node reconnection.");
        }

        /// <summary>
        ///     Test that the tree correctly handles nodes with complex connection directions.
        /// </summary>
        [Test]
        public void NodeConnections_WithComplexDirections_ShouldHandleCorrectly()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            // Manually change the connection direction to bidirectional
            INodeConnection connection = root.Connections.FirstOrDefault(conn => conn.ToNode == child);
            if (connection != null) connection.Direction = NodeConnectionDirection.Bidirectional;

            // Verify Neighbours reflect bidirectional connection
            INode[] rootNeighbors = root.Neighbours;
            INode[] childNeighbors = child.Neighbours;

            CollectionAssert.Contains(rootNeighbors, child, "Root's Neighbours should include the child.");
            CollectionAssert.Contains(childNeighbors, root,
                "Child's Neighbours should include the root due to bidirectional connection.");
        }

        /// <summary>
        ///     Test that the tree does not allow creating cycles through complex operations.
        /// </summary>
        [Test]
        public void PreventCycles_WithComplexOperations_ShouldThrowException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child1 = new BinaryTreeNode { Id = "Child1" };
            BinaryTreeNode child2 = new BinaryTreeNode { Id = "Child2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child1, root);
            _binaryTree.AddTreeNode(child2, child1);

            // Attempt to connect root as a child of child2, creating a cycle
            Assert.Throws<InvalidOperationException>(() => _binaryTree.AddTreeNode(root, child2),
                "Connecting root as a child of child2 should throw an InvalidOperationException due to cycle creation.");
        }

        /// <summary>
        ///     Test that the BinarySearch method correctly handles null predicates.
        /// </summary>
        [Test]
        public void BinarySearch_WithNullPredicate_ShouldThrowArgumentNullException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            _binaryTree.AddTreeNode(root, null);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => _binaryTree.BinarySearch(null),
                "Passing a null predicate to BinarySearch should throw an ArgumentNullException.");
            Assert.AreEqual("predicate", ex.ParamName, "Exception should indicate that 'predicate' parameter is null.");
        }

        /// <summary>
        ///     Test that DepthFirstSearch correctly handles null predicates.
        /// </summary>
        [Test]
        public void DepthFirstSearch_WithNullPredicate_ShouldThrowArgumentNullException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            _binaryTree.AddTreeNode(root, null);

            Assert.Throws<ArgumentNullException>(() => _binaryTree.DepthFirstSearch(null),
                "Passing a null predicate to DepthFirstSearch should throw an ArgumentNullException.");
        }

        /// <summary>
        ///     Test that HeightFirstSearch correctly handles null predicates.
        /// </summary>
        [Test]
        public void HeightFirstSearch_WithNullPredicate_ShouldThrowArgumentNullException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            _binaryTree.AddTreeNode(root, null);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => _binaryTree.HeightFirstSearch(null),
                "Passing a null predicate to HeightFirstSearch should throw an ArgumentNullException.");
            Assert.AreEqual("predicate", ex.ParamName, "Exception should indicate that 'predicate' parameter is null.");
        }

        /// <summary>
        ///     Test that adding a node to a non-binary child graph throws an exception.
        /// </summary>
        [Test]
        public void AddNode_ToNonBinaryChildGraph_ShouldThrowException()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            BinaryTreeNode grandChild1 = new BinaryTreeNode { Id = "GrandChild1" };
            BinaryTreeNode grandChild2 = new BinaryTreeNode { Id = "GrandChild2" };
            BinaryTreeNode grandChild3 = new BinaryTreeNode { Id = "GrandChild3" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);

            _binaryTree.AddTreeNode(grandChild1, child);
            _binaryTree.AddTreeNode(grandChild2, child);

            // Attempt to add a third grandchild, which should fail
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _binaryTree.AddTreeNode(grandChild3, child),
                "Adding a third grandchild to a binary child graph should throw an InvalidOperationException.");
            Assert.AreEqual("Parent node already has both left and right children.", ex.Message,
                "Exception message should indicate that the parent has both children.");
        }

        /// <summary>
        ///     Test that removing a node updates the graph hierarchy correctly.
        /// </summary>
        [Test]
        public void RemoveNode_ShouldUpdateGraphHierarchy()
        {
            BinaryTreeNode root = new BinaryTreeNode { Id = "Root" };
            BinaryTreeNode child = new BinaryTreeNode { Id = "Child" };
            BinaryTreeNode grandChild = new BinaryTreeNode { Id = "GrandChild" };
            BinaryTreeNode root2 = new BinaryTreeNode { Id = "root2" };

            _binaryTree.AddTreeNode(root, null);
            _binaryTree.AddTreeNode(child, root);
            _binaryTree.AddTreeNode(grandChild, child);

            // Add child graph
            BinaryTree childGraph = new BinaryTree();
            childGraph.AddTreeNode(root2, null);
            _binaryTree.AddGraph(childGraph);

            // Remove grandChild from child graph
            _binaryTree.RemoveBinaryTreeNode(grandChild, out _);

            Assert.IsFalse(_binaryTree.AllNodesRecursive.Contains(grandChild),
                "Graph hierarchy should no longer include the removed grandChild node.");
        }

        /// <summary>
        ///     Test that attempting to add a null graph as a child throws an exception.
        /// </summary>
        [Test]
        public void AddNullGraph_ShouldThrowArgumentNullException()
        {
            BinaryTree parentGraph = new BinaryTree();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => parentGraph.AddGraph(null),
                "Adding a null graph should throw an ArgumentNullException.");
            Assert.AreEqual("child", ex.ParamName, "Exception should indicate that 'child' parameter is null.");
        }
        
        /// <summary>
        ///     Test that the BinaryTree correctly handles multiple child graphs.
        /// </summary>
        [Test]
        public void BinaryTree_WithMultipleChildGraphs_ShouldHandleCorrectly()
        {
            BinaryTree rootGraph = new BinaryTree();
            BinaryTree childGraph1 = new BinaryTree();
            BinaryTree childGraph2 = new BinaryTree();

            rootGraph.AddGraph(childGraph1);
            rootGraph.AddGraph(childGraph2);

            Assert.Contains(childGraph1, rootGraph.ChildGraphs, "ChildGraph1 should be present in ChildGraphs.");
            Assert.Contains(childGraph2, rootGraph.ChildGraphs, "ChildGraph2 should be present in ChildGraphs.");
            Assert.AreEqual(rootGraph, childGraph1.ParentGraph, "ChildGraph1's ParentGraph should be rootGraph.");
            Assert.AreEqual(rootGraph, childGraph2.ParentGraph, "ChildGraph2's ParentGraph should be rootGraph.");
        }

        /// <summary>
        ///     Test that the ParentGraph property is null for a root graph.
        /// </summary>
        [Test]
        public void ParentGraph_RootGraph_ShouldBeNull()
        {
            BinaryTree rootGraph = new BinaryTree();

            Assert.IsNull(rootGraph.ParentGraph, "Root graph's ParentGraph should be null.");
        }

        /// <summary>
        ///     Test that adding and removing child graphs maintains correct ParentGraph references.
        /// </summary>
        [Test]
        public void AddRemoveChildGraphs_ShouldMaintainParentGraphReferences()
        {
            BinaryTree rootGraph = new BinaryTree();
            BinaryTree childGraph1 = new BinaryTree();
            BinaryTree childGraph2 = new BinaryTree();

            rootGraph.AddGraph(childGraph1);
            rootGraph.AddGraph(childGraph2);

            Assert.AreEqual(rootGraph, childGraph1.ParentGraph, "ChildGraph1's ParentGraph should be rootGraph.");
            Assert.AreEqual(rootGraph, childGraph2.ParentGraph, "ChildGraph2's ParentGraph should be rootGraph.");

            rootGraph.RemoveGraph(childGraph1);

            Assert.IsNull(childGraph1.ParentGraph, "ChildGraph1's ParentGraph should be null after removal.");
            Assert.AreEqual(rootGraph, childGraph2.ParentGraph, "ChildGraph2's ParentGraph should remain rootGraph.");
        }
    }
}