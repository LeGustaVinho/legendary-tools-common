using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LegendaryTools.GraphV2
{
    public class Tree : Graph, ITree
    {
        public ITreeNode RootNode { get; protected set; }

        public override bool IsDirected => RootNode == null || base.IsDirected;

        public int Height
        {
            get
            {
                if (RootNode == null) return 0;
                return CalculateHeight(RootNode);
            }
        }

        public int Width
        {
            get
            {
                if (RootNode == null) return 0;
                return CalculateWidth(RootNode);
            }
        }

        public void AddTreeNode(ITreeNode newNode, ITreeNode parentNode)
        {
            if (newNode == null) throw new ArgumentNullException(nameof(newNode));
            if (parentNode == null && RootNode != null)
                throw new InvalidOperationException(
                    "Root node already exists. To add additional nodes, specify a parent.");

            if (parentNode == null)
            {
                if (RootNode != null)
                    throw new InvalidOperationException("Root node already exists.");
                RootNode = newNode;
            }
            else
            {
                if (!Contains(parentNode))
                    throw new ArgumentException("Parent node does not exist in the tree.");
                newNode.ConnectToParent(parentNode);
            }

            Add(newNode);

            // Ensure the tree remains acyclic and directed
            if (IsCyclic || !IsDirectedAcyclic)
            {
                // Undo addition
                Remove(newNode);
                if (parentNode != null) newNode.DisconnectFromParent();
                if (parentNode == null) RootNode = null;
                throw new InvalidOperationException("Adding this node creates a cycle or violates tree properties.");
            }
        }

        public virtual bool RemoveTreeNode(ITreeNode node, out ITreeNode[] removedNodes)
        {
            removedNodes = Array.Empty<ITreeNode>();
            if (node == null) return false;
            if (!Contains(node)) return false;

            List<ITreeNode> nodesToRemove = new List<ITreeNode>();
            CollectSubtree(node, nodesToRemove);

            foreach (ITreeNode n in nodesToRemove)
            {
                Remove(n);
                if (n.ParentNode != null) n.ParentNode.ChildNodes.Remove(n);
            }

            if (node == RootNode) RootNode = null;

            removedNodes = nodesToRemove.ToArray();
            return true;
        }

        protected void CollectSubtree(ITreeNode node, List<ITreeNode> collection)
        {
            collection.Add(node);
            foreach (ITreeNode child in node.ChildNodes) CollectSubtree(child, collection);
        }

        public ITreeNode DepthFirstSearch(Predicate<INode> predicate)
        {
            return DepthFirstSearch(RootNode, predicate);
        }

        private ITreeNode DepthFirstSearch(ITreeNode node, Predicate<INode> predicate)
        {
            if (node == null) return null;
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (predicate(node)) return node;
            
            foreach (ITreeNode child in node.ChildNodes)
            {
                ITreeNode result = DepthFirstSearch(child, predicate);
                if (result != null) return result;
            }

            return null;
        }

        public ITreeNode HeightFirstSearch(Predicate<INode> predicate)
        {
            if (RootNode == null) return null;
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            Queue<ITreeNode> queue = new Queue<ITreeNode>();
            queue.Enqueue(RootNode);

            while (queue.Count > 0)
            {
                ITreeNode current = queue.Dequeue();
                if (predicate(current))
                    return current;

                foreach (ITreeNode child in current.ChildNodes) queue.Enqueue(child);
            }

            return null;
        }

        public List<ITreeNode> DepthFirstTraverse()
        {
            List<ITreeNode> traversal = new List<ITreeNode>();
            DepthFirstTraverse(RootNode, traversal);
            return traversal;
        }

        protected void ReplaceRootNode(ITreeNode newRootNode)
        {
            if (newRootNode == null) throw new ArgumentNullException(nameof(newRootNode));

            if (RootNode != null)
            {
                // Optionally, disconnect the old root node from the tree
                // For B-Tree, we typically adjust the root without removing the old root
            }

            RootNode = newRootNode;
            if (!Contains(newRootNode))
            {
                Add(newRootNode);
            }

            // Ensure the tree remains acyclic and directed
            if (IsCyclic || !IsDirected)
            {
                // Undo addition
                Remove(newRootNode);
                RootNode = null;
                throw new InvalidOperationException("Setting this node as root creates a cycle or violates tree properties.");
            }
        }
        
        private void DepthFirstTraverse(ITreeNode node, List<ITreeNode> traversal)
        {
            if (node == null) return;
            traversal.Add(node);
            foreach (ITreeNode child in node.ChildNodes) DepthFirstTraverse(child, traversal);
        }

        public List<ITreeNode> HeightFirstTraverse()
        {
            List<ITreeNode> traversal = new List<ITreeNode>();
            if (RootNode == null) return traversal;

            Queue<ITreeNode> queue = new Queue<ITreeNode>();
            queue.Enqueue(RootNode);

            while (queue.Count > 0)
            {
                ITreeNode current = queue.Dequeue();
                traversal.Add(current);
                foreach (ITreeNode child in current.ChildNodes) queue.Enqueue(child);
            }

            return traversal;
        }

        private int CalculateHeight(ITreeNode node)
        {
            if (node.ChildNodes.Count == 0) return 1;
            return 1 + node.ChildNodes.Max(CalculateHeight);
        }

        private int CalculateWidth(ITreeNode node)
        {
            int maxWidth = 0;
            Queue<ITreeNode> queue = new Queue<ITreeNode>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                int levelSize = queue.Count;
                maxWidth = Math.Max(maxWidth, levelSize);

                for (int i = 0; i < levelSize; i++)
                {
                    ITreeNode current = queue.Dequeue();
                    foreach (ITreeNode child in current.ChildNodes) queue.Enqueue(child);
                }
            }

            return maxWidth;
        }
        
        public virtual void VisualizeTree(bool showDepth = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("B-Tree");

            if (showDepth)
                VisualizeNode(RootNode, "", true, sb, 0);
            else
                VisualizeNode(RootNode, "", true, sb);

            Debug.Log(sb.ToString());
        }

        private void VisualizeNode(ITreeNode node, string indent, bool last, StringBuilder sb)
        {
            if (node == null)
                return;

            // Print the current node's keys with appropriate indentation
            sb.AppendLine(indent + (last ? "└─ " : "├─ ") + $"[Id: {node.Id}]");

            // Update the indentation for child nodes
            indent += last ? "   " : "│  ";

            // Recursively print all child nodes
            for (int i = 0; i < node.ChildNodes.Count; i++)
            {
                ITreeNode childNode = node.ChildNodes[i];
                VisualizeNode(childNode, indent, i == node.ChildNodes.Count - 1, sb);
            }
        }

        private void VisualizeNode(ITreeNode node, string indent, bool last, StringBuilder sb, int depth)
        {
            if (node == null)
                return;

            sb.AppendLine($"{indent}{(last ? "└──" : "├──")} [Depth: {depth}] [Id: {node.Id}]");
            indent += last ? "    " : "│   ";

            for (int i = 0; i < node.ChildNodes.Count; i++)
            {
                ITreeNode childNode = node.ChildNodes[i];
                VisualizeNode(childNode, indent, i == node.ChildNodes.Count - 1, sb, depth + 1);
            }
        }
    }
}