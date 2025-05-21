using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public class MultiParentTreeNode : Node, IMultiParentTreeNode
    {
        /// <summary>
        /// List of parent nodes. Because this is multi-parent, multiple parent nodes may point to this node.
        /// </summary>
        public List<IMultiParentTreeNode> ParentNodes { get; protected set; }

        List<IMultiParentTreeNode> IMultiParentTreeNode.ParentNodes
        {
            get => ParentNodes;
            set => ParentNodes = value;
        }

        /// <summary>
        /// List of child nodes. Many nodes can share the same parent.
        /// </summary>
        public List<IMultiParentTreeNode> ChildNodes { get; }

        /// <summary>
        /// Constructor. Reuses the base Node constructor, but initializes the parent and child lists.
        /// </summary>
        /// <param name="shouldMergeOppositeConnections">If true, opposite connections are merged into bidirectional.</param>
        public MultiParentTreeNode(bool shouldMergeOppositeConnections = false)
            : base(shouldMergeOppositeConnections)
        {
            ParentNodes = new List<IMultiParentTreeNode>();
            ChildNodes = new List<IMultiParentTreeNode>();
        }

        /// <summary>
        /// Connects this node to a parent node (unidirectional from parent to child).
        /// Ensures consistency in ParentNodes and ChildNodes.
        /// </summary>
        /// <param name="parent">The parent node to connect.</param>
        /// <returns>The resulting connection (unidirectional parent -> this).</returns>
        public INodeConnection ConnectToParent(IMultiParentTreeNode parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            // Basic validation: a node cannot be its own parent
            if (parent == this)
            {
                throw new InvalidOperationException("A node cannot be its own parent.");
            }

            // Check if it's already a parent
            if (ParentNodes.Contains(parent))
            {
                // If it exists, just return the existing connection (if any)
                INodeConnection existingConnection = FindConnectionBetweenNodes(parent, this);
                if (existingConnection != null)
                {
                    return existingConnection;
                }
            }

            // Create connection from parent -> child (this)
            INodeConnection connection = parent.ConnectTo(this, NodeConnectionDirection.Unidirectional);

            // Update in-memory relationships
            ParentNodes.Add(parent);
            parent.ChildNodes.Add(this);

            return connection;
        }

        /// <summary>
        /// Disconnects this node from all parents. Removes the connection and updates
        /// ChildNodes and ParentNodes for each parent accordingly.
        /// </summary>
        public virtual void DisconnectFromParents()
        {
            // Make a copy to avoid iteration issues while removing
            IMultiParentTreeNode[] parentsCopy = ParentNodes.ToArray();
            foreach (IMultiParentTreeNode parent in parentsCopy)
            {
                DisconnectFromParent(parent);
            }
        }

        /// <summary>
        /// Disconnects this node from a specific parent.
        /// </summary>
        /// <param name="parentNode">The parent node to disconnect from.</param>
        public virtual void DisconnectFromParent(IMultiParentTreeNode parentNode)
        {
            if (parentNode == null) return;
            if (!ParentNodes.Contains(parentNode)) return;

            // Look for the unidirectional connection parent->this
            INodeConnection conn = parentNode.FindConnectionBetweenNodes(parentNode, this);
            conn?.Disconnect();

            // Remove references from both sides
            ParentNodes.Remove(parentNode);
            parentNode.ChildNodes.Remove(this);
        }
    }
}
