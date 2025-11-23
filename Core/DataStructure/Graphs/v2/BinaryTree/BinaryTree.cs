using System;

namespace LegendaryTools.GraphV2
{
    public class BinaryTree : Tree, IBinaryTree
    {
        /// <summary>
        ///     Adds a node to the Binary Tree under the specified parent node.
        /// </summary>
        /// <param name="newNode">The new binary tree node to add.</param>
        /// <param name="parentNode">The parent binary tree node.</param>
        /// <param name="weight">Optional weight for the connection.</param>
        public void AddTreeNode(IBinaryTreeNode newNode, IBinaryTreeNode parentNode, float weight = 1)
        {
            if (newNode == null) throw new ArgumentNullException(nameof(newNode));
            if (newNode == parentNode) throw new InvalidOperationException(nameof(newNode));

            if (parentNode == null)
            {
                // Adding as root
                if (RootNode != null)
                    throw new InvalidOperationException("Binary Tree already has a root.");
                RootNode = newNode;
            }
            else
            {
                if (!Contains(parentNode))
                    throw new ArgumentException("Parent node does not exist in the Binary Tree.");

                // Determine whether to add as left or right child
                BinaryTreeNode binaryParent = parentNode as BinaryTreeNode;
                BinaryTreeNode binaryNewNode = newNode as BinaryTreeNode;

                if (binaryParent == null || binaryNewNode == null)
                    throw new InvalidOperationException("Nodes must be of type BinaryTreeNode.");

                if (binaryParent.Left == null)
                    binaryNewNode.ConnectAsLeftChild(binaryParent, weight);
                else if (binaryParent.Right == null)
                    binaryNewNode.ConnectAsRightChild(binaryParent, weight);
                else
                    throw new InvalidOperationException("Parent node already has both left and right children.");
            }

            Add(newNode);

            // Ensure the Binary Tree remains a directed acyclic graph and maintains binary tree properties
            if (!IsDirectedAcyclic || IsCyclic)
            {
                // Undo addition
                RemoveBinaryTreeNode(newNode, out ITreeNode[] removedNodes);
                if (parentNode != null) newNode.DisconnectFromParent();
                if (parentNode == null) RootNode = null;
                throw new InvalidOperationException("Adding this node violates Binary Tree properties.");
            }
        }

        /// <summary>
        ///     Performs a binary search on the tree using the specified predicate.
        /// </summary>
        /// <param name="predicate">The predicate to match nodes.</param>
        /// <returns>The first node matching the predicate, or null if not found.</returns>
        public IBinaryTreeNode BinarySearch(Predicate<INode> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return BinarySearch(RootNode, predicate);
        }

        private IBinaryTreeNode BinarySearch(ITreeNode node, Predicate<INode> predicate)
        {
            if (node == null) return null;

            if (predicate(node))
                return node as IBinaryTreeNode;

            IBinaryTreeNode found = null;

            if (node is BinaryTreeNode binaryNode)
            {
                found = BinarySearch(binaryNode.Left, predicate);
                if (found != null)
                    return found;

                found = BinarySearch(binaryNode.Right, predicate);
            }

            return found;
        }

        /// <summary>
        ///     Overrides the Remove method to ensure Binary Tree properties are maintained.
        /// </summary>
        /// <param name="node">The node to remove.</param>
        /// <returns>True if the node was removed; otherwise, false.</returns>
        public bool RemoveBinaryTreeNode(IBinaryTreeNode node, out ITreeNode[] removedNodes)
        {
            BinaryTreeNode binaryTreeNode = node as BinaryTreeNode;
            if(binaryTreeNode == null) throw new InvalidOperationException("Nodes must be of type BinaryTreeNode.");
            if (!Contains(node))
            {
                removedNodes = Array.Empty<ITreeNode>();
                return false;
            }
            node.DisconnectFromParent();
            // Remove the node and its subtree
            bool result = RemoveTreeNode(node, out removedNodes);
            return result;
        }
    }
}