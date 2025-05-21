using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public class MultiParentTree : Graph, IMultiParentTree
    {
        /// <summary>
        /// Multi-parent nodes often need one or more "roots".
        /// However, this interface only defines a single RootNode.
        /// If you need multiple roots, you can adapt this logic.
        /// </summary>
        public IMultiParentTreeNode RootNode { get; private set; }

        /// <summary>
        /// Returns the height of the tree (the maximum depth) starting from RootNode.
        /// If RootNode is null, returns 0.
        /// </summary>
        public int Height
        {
            get
            {
                if (RootNode == null)
                    return 0;

                return GetHeight(RootNode);
            }
        }

        /// <summary>
        /// Returns the "width" of the tree (the maximum number of nodes at any level).
        /// This is computed via BFS to count how many nodes exist at each level.
        /// </summary>
        public int Width
        {
            get
            {
                if (RootNode == null)
                    return 0;

                int maxWidth = 0;
                Queue<IMultiParentTreeNode> queue = new Queue<IMultiParentTreeNode>();
                queue.Enqueue(RootNode);

                while (queue.Count > 0)
                {
                    int levelCount = queue.Count;
                    maxWidth = Math.Max(maxWidth, levelCount);

                    for (int i = 0; i < levelCount; i++)
                    {
                        IMultiParentTreeNode current = queue.Dequeue();
                        foreach (IMultiParentTreeNode child in current.ChildNodes)
                        {
                            queue.Enqueue(child);
                        }
                    }
                }

                return maxWidth;
            }
        }

        /// <summary>
        /// Adds a node to the multi-parent tree, optionally assigning a parent node.
        /// If <paramref name="parentNode"/> is null and no RootNode exists,
        /// the new node becomes the root.
        /// </summary>
        /// <param name="newNode">The new node to be added</param>
        /// <param name="parentNode">The parent of the new node (can be null)</param>
        public void AddTreeNode(IMultiParentTreeNode newNode, IMultiParentTreeNode parentNode)
        {
            if (newNode == null)
                throw new ArgumentNullException(nameof(newNode));

            // Basic validation to avoid self-parenting
            if (newNode == parentNode)
            {
                throw new InvalidOperationException("A node cannot be its own parent.");
            }

            // Add node to the Graph (if not already present)
            Add(newNode);

            // If there's no root yet and parentNode is null, make this node the root
            if (RootNode == null && parentNode == null)
            {
                RootNode = newNode;
                return;
            }

            // If parentNode != null, connect child -> parent
            if (parentNode != null)
            {
                // Optional: if parentNode is not in the graph, you can also add it or throw an exception
                if (!Contains(parentNode))
                {
                    // For demonstration, we add the parent too if it's missing.
                    Add(parentNode);
                }

                newNode.ConnectToParent(parentNode);
            }
        }

        /// <summary>
        /// Removes a specific node from the tree and all its descendants (subtree).
        /// Returns these removed nodes via the 'removedNodes' out parameter.
        /// Returns true if removal is successful, otherwise false.
        /// </summary>
        /// <param name="node">The node to be removed</param>
        /// <param name="removedNodes">All nodes that were removed (the entire subtree)</param>
        /// <returns>true if removed, false otherwise</returns>
        public bool RemoveTreeNode(IMultiParentTreeNode node, out IMultiParentTreeNode[] removedNodes)
        {
            if (node == null)
            {
                removedNodes = Array.Empty<IMultiParentTreeNode>();
                return false;
            }

            // If the node is not part of this graph
            if (!Contains(node))
            {
                removedNodes = Array.Empty<IMultiParentTreeNode>();
                return false;
            }

            // Gather the node and all its descendants
            List<IMultiParentTreeNode> toRemove = new List<IMultiParentTreeNode>();
            CollectSubtreeNodes(node, toRemove);

            // Physically remove them from the graph
            foreach (IMultiParentTreeNode n in toRemove)
            {
                // Disconnect the node from its parents (removes the reference in their ChildNodes)
                n.DisconnectFromParents();
                base.Remove(n);
            }

            // If the removed node was the root, clear the reference
            if (node == RootNode)
            {
                RootNode = null;
            }

            removedNodes = toRemove.ToArray();
            return true;
        }

        /// <summary>
        /// Performs a Depth First Search starting from RootNode,
        /// looking for the first node that matches the <paramref name="predicate"/>.
        /// Returns null if not found or if RootNode is null.
        /// </summary>
        public IMultiParentTreeNode DepthFirstSearch(Predicate<INode> predicate)
        {
            if (RootNode == null) return null;
            return DFS(RootNode, predicate);
        }

        /// <summary>
        /// Performs a "Height First Search" (BFS) starting from RootNode,
        /// looking for the first node that matches the <paramref name="predicate"/>.
        /// Returns null if not found or if RootNode is null.
        /// </summary>
        public IMultiParentTreeNode HeightFirstSearch(Predicate<INode> predicate)
        {
            if (RootNode == null) return null;

            Queue<IMultiParentTreeNode> queue = new Queue<IMultiParentTreeNode>();
            queue.Enqueue(RootNode);

            while (queue.Count > 0)
            {
                IMultiParentTreeNode current = queue.Dequeue();
                if (predicate(current))
                    return current;

                foreach (IMultiParentTreeNode child in current.ChildNodes)
                {
                    queue.Enqueue(child);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns all MultiParentTreeNode objects traversed in DFS order.
        /// </summary>
        public List<IMultiParentTreeNode> DepthFirstTraverse()
        {
            List<IMultiParentTreeNode> visited = new List<IMultiParentTreeNode>();
            if (RootNode == null) return visited;

            DepthFirstTraverseRecursive(RootNode, visited);
            return visited;
        }

        /// <summary>
        /// Returns all MultiParentTreeNode objects traversed in BFS order.
        /// </summary>
        public List<IMultiParentTreeNode> HeightFirstTraverse()
        {
            List<IMultiParentTreeNode> visited = new List<IMultiParentTreeNode>();
            if (RootNode == null) return visited;

            Queue<IMultiParentTreeNode> queue = new Queue<IMultiParentTreeNode>();
            queue.Enqueue(RootNode);

            while (queue.Count > 0)
            {
                IMultiParentTreeNode current = queue.Dequeue();
                visited.Add(current);

                foreach (IMultiParentTreeNode child in current.ChildNodes)
                {
                    if (!visited.Contains(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return visited;
        }

        #region Private Helper Methods

        /// <summary>
        /// Returns the height of a node (the maximum distance to a leaf child).
        /// </summary>
        private int GetHeight(IMultiParentTreeNode node)
        {
            if (node.ChildNodes.Count == 0)
                return 1; // Leaf has height 1

            int maxChildHeight = 0;
            foreach (IMultiParentTreeNode child in node.ChildNodes)
            {
                int childHeight = GetHeight(child);
                if (childHeight > maxChildHeight)
                    maxChildHeight = childHeight;
            }
            return 1 + maxChildHeight;
        }

        /// <summary>
        /// Recursively performs DFS to find a node that matches the predicate.
        /// </summary>
        private IMultiParentTreeNode DFS(IMultiParentTreeNode current, Predicate<INode> predicate)
        {
            if (predicate(current))
                return current;

            foreach (IMultiParentTreeNode child in current.ChildNodes)
            {
                IMultiParentTreeNode result = DFS(child, predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Recursively collects all child nodes (including the specified node).
        /// </summary>
        private void CollectSubtreeNodes(IMultiParentTreeNode node, List<IMultiParentTreeNode> collector)
        {
            if (!collector.Contains(node))
                collector.Add(node);

            foreach (IMultiParentTreeNode child in node.ChildNodes)
            {
                CollectSubtreeNodes(child, collector);
            }
        }

        /// <summary>
        /// Recursively traverses nodes in DFS order and builds the visited list.
        /// </summary>
        private void DepthFirstTraverseRecursive(IMultiParentTreeNode node, List<IMultiParentTreeNode> visited)
        {
            visited.Add(node);
            foreach (IMultiParentTreeNode child in node.ChildNodes)
            {
                if (!visited.Contains(child))
                {
                    DepthFirstTraverseRecursive(child, visited);
                }
            }
        }

        #endregion
    }
}